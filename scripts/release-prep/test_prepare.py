"""Focused offline helper tests. Fixtures never evaluate the real repository with altered metadata."""
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('release_prep', HERE / 'prepare.py')
prep = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prep)
ROOT = HERE.parents[1]


def contents(root):
    return {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob('*') if p.is_file()}


class ReleasePrepTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='tsharpvision-release-prep-')
        self.root = Path(self.temp.name)
        self.addCleanup(self.temp.cleanup)
        self.data = dict(version='1.1.0-preview.1', channel='prerelease', summary='Reviewed fixes.',
                         changes=['A consumer-visible fix.'], compatibility='No reviewed API changes.',
                         limitations=['Hosted publication remains unverified.'], scope=['Reviewed fixture scope.'],
                         readme_summary='A consumer-visible fix.')
        for relative in ('.github/workflows/release.yml', '.github/workflows/publish-nuget.yml',
                         'scripts/prepare-github-release.ps1', 'scripts/complete-github-release.ps1',
                         'scripts/publish-tvdemo-release.ps1', 'scripts/pack-local-packages.ps1',
                         'scripts/release-prep/README.md',
                         'scripts/release-prep/manual-release-template.md'):
            dest = self.root / relative
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / relative, dest)
        (self.root / 'README.md').write_text('# Fixture\n\nPackage metadata currently defaults to `1.0.0`.\n\n## Requirements\n\n.NET 10.\n', encoding='utf-8')
        (self.root / 'CHANGELOG.md').write_text('# Changelog\n\n## 1.0.0\n\n- Preserved historical text.\n', encoding='utf-8')
        self.packages = [dict(IsPackable='true', PackageId=i, Version=self.data['version'],
                              PackageVersion=self.data['version'], TargetFramework='net10.0',
                              RepositoryUrl='https://example.invalid', PackageLicenseExpression='MIT',
                              PackageReadmeFile='README.md', Description='Fixture description',
                              Project=i + '.csproj', InternalDependencies=[], Bundled=[],
                              PackAsTool='true' if i in prep.EXPECTED_TOOLS else 'false',
                              ToolCommandName=prep.EXPECTED_TOOLS.get(i, ''), IsTool=i in prep.EXPECTED_TOOLS)
                         for i in sorted(prep.EXPECTED_LIBRARY_IDS | set(prep.EXPECTED_TOOLS))]
        self.evaluation = patch.object(prep, 'evaluate', side_effect=lambda root, commands: (self.data['version'], copy.deepcopy(self.packages)))
        self.evaluation.start()
        self.addCleanup(self.evaluation.stop)

    def run_prep(self, mode='preview', data=None):
        return prep.execute(self.root, data if data is not None else self.data, Path('Temp/release'), mode)

    def assert_blocked_without_writes(self, data=None, message=None):
        before = contents(self.root)
        with self.assertRaisesRegex(prep.Blocked, message or '.'):
            self.run_prep('apply', data)
        self.assertEqual(before, contents(self.root))

    def test_preview_apply_and_idempotency(self):
        before = contents(self.root)
        self.assertEqual(5, len(self.run_prep()))
        self.assertEqual(before, contents(self.root))
        self.assertEqual(5, len(self.run_prep('apply')))
        prepared = contents(self.root)
        self.assertEqual([], self.run_prep())
        self.assertEqual([], self.run_prep('apply'))
        self.assertEqual(prepared, contents(self.root))
        changelog = prep.read(self.root / 'CHANGELOG.md')
        self.assertIn('## 1.0.0\n\n- Preserved historical text.', changelog)
        self.assertEqual(1, changelog.count('## ' + self.data['version']))

    def test_invalid_semver(self):
        for version in ('1.0', '01.0.0', '1.0.0-01', 'v1.0.0', '1.0.0-rc..1'):
            data = dict(self.data, version=version)
            self.assert_blocked_without_writes(data, 'SemVer')

    def test_wrong_target(self):
        self.assert_blocked_without_writes(dict(self.data, version='2.0.0-preview.1'), 'differs from repository')

    def test_unsupported_release_state(self):
        for changes in (dict(channel='nightly'), dict(channel='stable'), dict(version='1.1.0+build.1', channel='stable')):
            self.assert_blocked_without_writes(dict(self.data, **changes), 'Unsupported release state')

    def test_required_metadata(self):
        for key in ('version', 'channel', 'summary', 'changes', 'compatibility', 'limitations', 'scope', 'readme_summary'):
            data = dict(self.data)
            del data[key]
            self.assert_blocked_without_writes(data, 'Missing required')

    def test_duplicate_changelog(self):
        path = self.root / 'CHANGELOG.md'
        path.write_text(prep.read(path) + '\n## ' + self.data['version'] + '\n\n- First\n\n## ' + self.data['version'] + '\n\n- Conflict\n', encoding='utf-8')
        self.assert_blocked_without_writes(message='duplicate changelog')

    def test_conflicting_existing_section(self):
        path = self.root / 'CHANGELOG.md'
        path.write_text(prep.read(path) + '\n## ' + self.data['version'] + '\n\n- Different notes\n', encoding='utf-8')
        self.assert_blocked_without_writes(message='conflicts')

    def test_git_and_publication_actions_are_rejected(self):
        for action in ('git status', 'git commit', 'git push', 'git tag', 'gh workflow run', 'gh release create', 'dotnet nuget push', 'nuget push'):
            self.assert_blocked_without_writes(dict(self.data, action=action), 'Unsupported metadata/action')
        result = subprocess.run(['python', str(HERE / 'prepare.py'), '--execute', 'git status'], capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)

    def test_future_versions(self):
        for version, channel in (('1.0.0-rc.5', 'prerelease'), ('1.0.0', 'stable'),
                                 ('1.1.0-preview.1', 'prerelease'), ('1.1.0', 'stable'), ('2.0.0-preview.1', 'prerelease')):
            self.data.update(version=version, channel=channel)
            # Choose versions not already present in preserved history for this fixture.
            (self.root / 'CHANGELOG.md').write_text('# Changelog\n\n## 0.9.0\n\n- Historical.\n', encoding='utf-8')
            for p in self.packages:
                p.update(Version=version, PackageVersion=version)
            planned = prep.plan(self.root, self.data, Path('Temp/release'), [])
            notes = planned[self.root / 'Temp/release/release-notes-draft.md']
            self.assertIn('# TSharpVision ' + version, notes)
            self.assertIn('Release type: ' + channel, notes)

    def test_known_limitations_section_only_for_a_nonempty_list(self):
        self.data['limitations'] = []
        planned = prep.plan(self.root, self.data, Path('Temp/release'), [])
        notes = planned[self.root / 'Temp/release/release-notes-draft.md']
        self.assertNotIn('## Known limitations', notes)
        self.data['limitations'] = ['A real, user-visible limitation.']
        notes = prep.plan(self.root, self.data, Path('Temp/release'), [])[self.root / 'Temp/release/release-notes-draft.md']
        self.assertIn('## Known limitations\n\n- A real, user-visible limitation.', notes)

    def test_out_of_scope_claim(self):
        self.assert_blocked_without_writes(dict(self.data, excluded_claims=['consumer-visible']), 'Out-of-scope')

    def test_architecture_drift(self):
        (self.root / '.github/workflows/release.yml').write_text('name: changed', encoding='utf-8')
        self.assert_blocked_without_writes(message='architecture changed')

    def test_output_path_escape(self):
        with self.assertRaisesRegex(prep.Blocked, 'inside target checkout'):
            prep.execute(self.root, self.data, Path('../outside'), 'apply')

    def test_real_evaluator_uses_only_property_queries(self):
        # Restore the real evaluator and replace only the external .NET process.
        self.evaluation.stop()
        (self.root / 'Directory.Build.props').write_text('<Project><PropertyGroup><Version>1.1.0-preview.1</Version></PropertyGroup></Project>')
        (self.root / 'TSharpVision.slnx').write_text('<Solution>' + ''.join('<Project Path="' + p['Project'] + '" />' for p in self.packages) + '</Solution>')
        for p in self.packages:
            (self.root / p['Project']).write_text('<Project />')
        calls = []
        def fake_dotnet(command, **kwargs):
            calls.append(command)
            package = next(p for p in self.packages if p['Project'] == Path(command[2]).name)
            return subprocess.CompletedProcess(command, 0, json.dumps({'Properties': package}), '')
        with patch.object(prep.subprocess, 'run', side_effect=fake_dotnet):
            version, packages = prep.evaluate(self.root, [])
        self.assertEqual(12, len(packages))
        self.assertEqual(9, sum(not p['IsTool'] for p in packages))
        self.assertTrue(all(c[:2] == ['dotnet', 'msbuild'] and '-p:EnableSourceControlManagerQueries=false' in c and not any(a.startswith(('-t:', '-target:')) for a in c) for c in calls))

    def test_no_ai_skill_files_are_required(self):
        # The fixture copies only the documented release files: no .agents/.claude skill folders.
        self.assertFalse((self.root / '.agents').exists())
        self.assertFalse((self.root / '.claude').exists())
        self.assertEqual(5, len(self.run_prep()))
        source = prep.read(HERE / 'prepare.py')
        self.assertNotIn('.agents', source)
        self.assertNotIn('SKILL.md', source)

    def test_readme_documents_the_human_procedure(self):
        text = prep.read(HERE / 'README.md')
        for token in prep.README_TOKENS + ['release-1.2.0.json', 'pack-local-packages.ps1', '--mode apply']:
            self.assertIn(token, text)
        for ref in prep.re.findall(r'`(scripts/release-prep/[^`]+)`', text):
            self.assertTrue((ROOT / ref).is_file(), ref)

    def test_missing_readme_blocks_as_architecture(self):
        (self.root / 'scripts/release-prep/README.md').unlink()
        self.assert_blocked_without_writes(message='architecture file missing: scripts/release-prep/README.md')

    def twelve_packages_1_2_0(self):
        # Nine libraries (the extensions depend on the core) and three tools that bundle the core.
        self.data.update(version='1.2.0', channel='stable', changelog='existing')
        (self.root / 'CHANGELOG.md').write_text('# Changelog\n\n## 1.2.0\n\nMaintained notes.\n\n## 1.1.0\n\n- Older.\n', encoding='utf-8')
        for p in self.packages:
            p.update(Version='1.2.0', PackageVersion='1.2.0',
                     InternalDependencies=[] if p['PackageId'] == 'TSharpVision' or p['IsTool'] else ['TSharpVision'],
                     Bundled=['TSharpVision'] if p['IsTool'] else [])

    def test_1_2_0_preview_with_maintained_changelog(self):
        self.twelve_packages_1_2_0()
        before = contents(self.root)
        changed = self.run_prep()
        self.assertEqual(before, contents(self.root))  # preview writes nothing
        names = sorted(p.relative_to(self.root).as_posix() for p in changed)
        self.assertEqual(['README.md', 'Temp/release/manual-release-checklist.md', 'Temp/release/release-notes-draft.md',
                          'Temp/release/release-preflight-report.md'], names)
        planned = prep.plan(self.root, self.data, Path('Temp/release'), [])
        self.assertEqual(prep.read(self.root / 'CHANGELOG.md'), planned[self.root / 'CHANGELOG.md'])  # kept as is
        notes = planned[self.root / 'Temp/release/release-notes-draft.md']
        self.assertIn('# TSharpVision 1.2.0', notes)
        self.assertEqual(12, notes.count('- `TSharpVision'))
        self.assertEqual(3, notes.count('(command `'))
        for tool in prep.EXPECTED_TOOLS:
            self.assertIn('dotnet tool install --global ' + tool + ' --version 1.2.0', notes)
        report = planned[self.root / 'Temp/release/release-preflight-report.md']
        self.assertIn('PASS: publication (dependency) order: TSharpVision, TSharpVision.CodeEditor, TSharpVision.Drivers.Console, '
                      'TSharpVision.Drivers.SDL, TSharpVision.Drivers.Terminal, TSharpVision.Graphics, TSharpVision.HexView, '
                      'TSharpVision.TableView, TSharpVision.Terminal, TSharpVision.HelpCompiler, TSharpVision.ResourceCompiler, '
                      'TSharpVision.ResourceTools.', report)
        self.assertIn('all 12 evaluated versions (9 libraries,\n3 .NET tools)', report)
        self.assertIn('| TSharpVision.Graphics | library | 1.2.0 | 1.2.0 | TSharpVision |', report)
        self.assertIn('| TSharpVision.ResourceCompiler | .NET tool `svrc` | 1.2.0 | 1.2.0 | none; bundles TSharpVision |', report)
        checklist = planned[self.root / 'Temp/release/manual-release-checklist.md']
        self.assertIn('- `TSharpVision.ResourceTools` (command `svres`)', checklist)
        for text in planned.values():
            self.assertNotIn(str(self.root), text)  # no checkout path in any output

    def test_existing_changelog_mode_requires_the_section(self):
        self.twelve_packages_1_2_0()
        (self.root / 'CHANGELOG.md').write_text('# Changelog\n\n## 1.1.0\n\n- Older.\n', encoding='utf-8')
        self.assert_blocked_without_writes(message='requires a maintained')
        self.assert_blocked_without_writes(dict(self.data, changelog='rewrite'), 'changelog must be one of')

    def test_dependency_order_and_cycles(self):
        self.twelve_packages_1_2_0()
        order = [p['PackageId'] for p in prep.dependency_order([p for p in self.packages if not p['IsTool']])]
        self.assertEqual('TSharpVision', order[0])
        self.assertEqual(sorted(order[1:]), order[1:])
        self.assertEqual(9, len(order))
        cyclic = [dict(PackageId='A', InternalDependencies=['B']), dict(PackageId='B', InternalDependencies=['A'])]
        with self.assertRaisesRegex(prep.Blocked, 'Cycle'):
            prep.dependency_order(cyclic)

    def test_package_inventory_mismatch_blocks(self):
        self.evaluation.stop()
        (self.root / 'Directory.Build.props').write_text('<Project><PropertyGroup><Version>1.1.0-preview.1</Version></PropertyGroup></Project>')
        eight = [p for p in self.packages if p['PackageId'] != 'TSharpVision.Graphics']
        (self.root / 'TSharpVision.slnx').write_text('<Solution>' + ''.join('<Project Path="' + p['Project'] + '" />' for p in eight) + '</Solution>')
        for p in eight:
            (self.root / p['Project']).write_text('<Project />')
        def fake_dotnet(command, **kwargs):
            package = next(p for p in eight if p['Project'] == Path(command[2]).name)
            return subprocess.CompletedProcess(command, 0, json.dumps({'Properties': package}), '')
        with patch.object(prep.subprocess, 'run', side_effect=fake_dotnet):
            with self.assertRaisesRegex(prep.Blocked, 'nine-library'):
                prep.evaluate(self.root, [])
        self.evaluation.start()

    def evaluate_fixture(self, packages, references=None):
        """Real evaluator over fixture projects; references maps a project to the projects it references."""
        (self.root / 'Directory.Build.props').write_text('<Project><PropertyGroup><Version>1.1.0-preview.1</Version></PropertyGroup></Project>')
        (self.root / 'TSharpVision.slnx').write_text('<Solution>' + ''.join('<Project Path="' + p['Project'] + '" />' for p in packages) + '</Solution>')
        for p in packages:
            refs = (references or {}).get(p['PackageId'], [])
            (self.root / p['Project']).write_text('<Project><ItemGroup>' + ''.join('<ProjectReference Include="' + r + '.csproj" />' for r in refs) + '</ItemGroup></Project>')
        def fake_dotnet(command, **kwargs):
            package = next(p for p in packages if p['Project'] == Path(command[2]).name)
            return subprocess.CompletedProcess(command, 0, json.dumps({'Properties': {k: v for k, v in package.items() if k not in ('IsTool', 'Bundled', 'InternalDependencies', 'Project')}}), '')
        with patch.object(prep.subprocess, 'run', side_effect=fake_dotnet):
            return prep.evaluate(self.root, [])

    def test_tool_inventory_mismatch_blocks(self):
        self.evaluation.stop()
        self.addCleanup(self.evaluation.start)
        renamed = [dict(p, ToolCommandName='rc') if p['PackageId'] == 'TSharpVision.ResourceCompiler' else p for p in self.packages]
        missing = [p for p in self.packages if p['PackageId'] != 'TSharpVision.ResourceTools']
        as_library = [dict(p, PackAsTool='false') if p['PackageId'] == 'TSharpVision.HelpCompiler' else p for p in self.packages]
        for packages, message in ((renamed, 'three-tool'), (missing, 'three-tool'), (as_library, 'nine-library')):
            with self.assertRaisesRegex(prep.Blocked, message):
                self.evaluate_fixture(packages)

    def test_tools_bundle_project_references_instead_of_depending(self):
        self.evaluation.stop()
        self.addCleanup(self.evaluation.start)
        references = {i: ['TSharpVision'] for i in list(prep.EXPECTED_TOOLS) + ['TSharpVision.Graphics']}
        version, packages = self.evaluate_fixture(self.packages, references)
        by_id = {p['PackageId']: p for p in packages}
        self.assertEqual(['TSharpVision'], by_id['TSharpVision.Graphics']['InternalDependencies'])
        for tool in prep.EXPECTED_TOOLS:
            self.assertTrue(by_id[tool]['IsTool'])
            self.assertEqual([], by_id[tool]['InternalDependencies'])
            self.assertEqual(['TSharpVision'], by_id[tool]['Bundled'])

    def test_missing_dotnet_is_reported(self):
        self.evaluation.stop()
        with patch.object(prep.shutil, 'which', return_value=None):
            with self.assertRaisesRegex(prep.Blocked, r'\.NET SDK \(dotnet\) is not on PATH'):
                prep.evaluate(self.root, [])
        self.evaluation.start()

    def test_cli_reports_missing_and_malformed_metadata(self):
        script = str(HERE / 'prepare.py')
        missing = subprocess.run([sys.executable, script, '--metadata', str(self.root / 'none.json'), '--output', 'Temp/x'],
                                 capture_output=True, text=True)
        self.assertEqual(2, missing.returncode)
        self.assertIn('BLOCKED: Metadata file not found', missing.stderr)
        bad = self.root / 'bad.json'
        bad.write_text('{not json', encoding='utf-8')
        malformed = subprocess.run([sys.executable, script, '--metadata', str(bad), '--output', 'Temp/x'],
                                   capture_output=True, text=True)
        self.assertEqual(2, malformed.returncode)
        self.assertIn('BLOCKED: Metadata is not valid JSON', malformed.stderr)
        listed = self.root / 'list.json'
        listed.write_text('[]', encoding='utf-8')
        not_object = subprocess.run([sys.executable, script, '--metadata', str(listed), '--output', 'Temp/x'],
                                    capture_output=True, text=True)
        self.assertEqual(2, not_object.returncode)
        self.assertIn('must be a JSON object', not_object.stderr)

    @unittest.skipUnless(os.environ.get('TSV_RELEASE_PREP_REAL_REPO') == '1',
                         'Real-repository preview: set TSV_RELEASE_PREP_REAL_REPO=1 (runs dotnet msbuild on every project).')
    def test_real_repository_preview_changes_nothing(self):
        def git(*args):
            return subprocess.run(['git', '-C', str(ROOT), *args], capture_output=True, check=True).stdout
        self.evaluation.stop()
        tracked = {name: (ROOT / name).read_bytes() for name in ('README.md', 'CHANGELOG.md', 'Directory.Build.props')}
        index_before = hashlib.sha256(git('diff', '--cached', '--binary')).hexdigest()
        status_before = git('status', '--short')
        result = subprocess.run([sys.executable, str(HERE / 'prepare.py'), '--metadata', str(HERE / 'release-1.2.0.json'),
                                 '--output', 'Temp/release-1.2.0', '--mode', 'preview'], capture_output=True, text=True, cwd=ROOT)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn('PASS: preview', result.stdout)
        self.assertEqual(tracked, {name: (ROOT / name).read_bytes() for name in tracked})
        self.assertEqual(index_before, hashlib.sha256(git('diff', '--cached', '--binary')).hexdigest())
        self.assertEqual(status_before, git('status', '--short'))
        self.assertFalse((ROOT / 'Temp/release-1.2.0').exists())
        self.evaluation.start()


if __name__ == '__main__':
    unittest.main(verbosity=2)
