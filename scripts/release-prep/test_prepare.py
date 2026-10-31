"""Focused offline helper tests. Fixtures never evaluate the real repository with altered metadata."""
import copy
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
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
                         '.agents/skills/tsharpvision-version-bump/SKILL.md',
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
                              Project=i + '.csproj', InternalDependencies=[])
                         for i in sorted(prep.EXPECTED_IDS)]
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
            self.assertNotIn('TFrame', notes)

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
        self.assertEqual(8, len(packages))
        self.assertTrue(all(c[:2] == ['dotnet', 'msbuild'] and '-p:EnableSourceControlManagerQueries=false' in c and not any(a.startswith(('-t:', '-target:')) for a in c) for c in calls))

    def test_skill_structure_and_references(self):
        first = prep.read(ROOT / '.agents/skills/tsharpvision-release-prep/SKILL.md')
        second = prep.read(ROOT / '.claude/skills/tsharpvision-release-prep/SKILL.md')
        self.assertEqual(first, second)
        for directory in ('.agents', '.claude'):
            file = ROOT / directory / 'skills/tsharpvision-release-prep/SKILL.md'
            text = prep.read(file)
            self.assertTrue(text.startswith('---\nname: tsharpvision-release-prep\ndescription: '))
            self.assertEqual(2, len(text.split('---\n', 2)) - 1)
            self.assertNotIn('TODO', text)
            for ref in prep.re.findall(r'\]\(([^)]+)\)', text):
                self.assertTrue((file.parent / ref).is_file(), ref)


if __name__ == '__main__':
    unittest.main(verbosity=2)
