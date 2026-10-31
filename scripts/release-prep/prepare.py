"""Offline release documentation preparation. Python standard library + .NET SDK only."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

NUMBER = r'(?:0|[1-9][0-9]*)'
IDENT = r'(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
SEMVER = re.compile(rf'{NUMBER}\.{NUMBER}\.{NUMBER}(?:-{IDENT}(?:\.{IDENT})*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\Z')
EXPECTED_IDS = {'TSharpVision', 'TSharpVision.Drivers.Console', 'TSharpVision.Drivers.Terminal', 'TSharpVision.Drivers.SDL', 'TSharpVision.CodeEditor', 'TSharpVision.HexView', 'TSharpVision.TableView', 'TSharpVision.Terminal'}
PROPERTIES = 'IsPackable,PackageId,Version,PackageVersion,TargetFramework,RepositoryUrl,PackageLicenseExpression,PackageReadmeFile,Description'

class Blocked(ValueError):
    pass


def read(path):
    return path.read_text(encoding='utf-8-sig')


def validate_metadata(data):
    allowed = {'version', 'channel', 'date', 'summary', 'changes', 'compatibility', 'limitations', 'scope', 'excluded_claims', 'readme_summary'}
    if set(data) - allowed:
        raise Blocked('Unsupported metadata/action fields: ' + ', '.join(sorted(set(data) - allowed)))
    for key in ('version', 'channel', 'summary', 'changes', 'compatibility', 'limitations', 'scope', 'readme_summary'):
        if not data.get(key):
            raise Blocked('Missing required release metadata: ' + key)
    if not isinstance(data['version'], str) or not SEMVER.fullmatch(data['version']):
        raise Blocked('Invalid SemVer')
    if '+' in data['version']:
        raise Blocked('Unsupported release state: existing NuGet pack helper rejects build metadata')
    pre = '-' in data['version']
    if data['channel'] not in ('stable', 'prerelease') or (data['channel'] == 'prerelease') != pre:
        raise Blocked('Unsupported release state: channel must agree with SemVer')
    for key in ('summary', 'compatibility', 'readme_summary'):
        if not isinstance(data[key], str) or not data[key].strip():
            raise Blocked('Missing required text: ' + key)
    for key in ('changes', 'limitations', 'scope', 'excluded_claims'):
        if key not in data and key == 'excluded_claims':
            continue
        if not isinstance(data[key], list) or not all(isinstance(x, str) and x.strip() for x in data[key]):
            raise Blocked('Expected nonempty text list: ' + key)
    if data.get('date'):
        try:
            datetime.date.fromisoformat(data['date'])
        except (TypeError, ValueError):
            raise Blocked('Date must be an established ISO YYYY-MM-DD date')


def architecture(root):
    # Inspect only; never execute existing release helpers (some invoke Git/API calls).
    requirements = {
        '.github/workflows/release.yml': ['name: GitHub Release', "- 'v*'", 'push:', 'tags:', 'prepare-github-release.ps1', 'publish-tvdemo-release.ps1', 'complete-github-release.ps1', '-Publish', 'ref: ${{ github.sha }}', 'needs: [validate, demo]'],
        '.github/workflows/publish-nuget.yml': ['name: Publish NuGet', 'workflow_dispatch:', 'environment: release', 'NuGet/login@v1', 'vars.NUGET_USER', 'dotnet nuget push', 'ref: ${{ github.sha }}', 'pack-local-packages.ps1'],
        'scripts/prepare-github-release.ps1': ['^## (.+?)', 'git rev-parse HEAD', 'Packages are available from NuGet.org'],
        'scripts/complete-github-release.ps1': ['--draft', '--draft=false', '--prerelease=', 'SHA256SUMS.txt', 'digest', 'Assert-RemoteTagCommit'],
        'scripts/publish-tvdemo-release.ps1': ['win-x64', 'linux-x64', 'osx-x64', 'osx-arm64', '--self-contained true'],
        'scripts/pack-local-packages.ps1': ['UseRepositoryVersion', 'ExpectedCommit', 'ReleaseManifestPath', 'build metadata is not allowed'],
        '.agents/skills/tsharpvision-version-bump/SKILL.md': ['name: tsharpvision-version-bump', 'Directory.Build.props'],
    }
    evidence = {}
    for name, tokens in requirements.items():
        path = root / name
        if not path.is_file():
            raise Blocked('Release architecture file missing: ' + name)
        text = read(path)
        if any(token not in text for token in tokens):
            raise Blocked('Release architecture changed; inspect and update shared procedure before use: ' + name)
        evidence[name] = hashlib.sha256(path.read_bytes()).hexdigest()
    # A signature check routes unfamiliar architectures back to agent inspection;
    # it is not a general YAML interpreter or proof of hosted environment policy.
    return evidence


def evaluate(root, commands):
    props = ET.parse(root / 'Directory.Build.props').getroot()
    versions = [n.text for n in props.iter('Version')]
    if len(versions) != 1:
        raise Blocked('Authoritative version source requires manual discovery')
    packages = []
    paths = [p.attrib['Path'] for p in ET.parse(root / 'TSharpVision.slnx').iter('Project')]
    for relative in paths:
        path = root / relative.replace('\\', '/')
        command = ['dotnet', 'msbuild', str(path), '-nologo', '-p:EnableSourceControlManagerQueries=false', '-getProperty:' + PROPERTIES]
        commands.append(command)
        # Property-only evaluation: no targets, restore, build, pack or repository helpers.
        result = subprocess.run(command, cwd=root, capture_output=True, text=True, check=False)
        if result.returncode:
            raise Blocked('MSBuild evaluation failed for ' + relative + ': ' + result.stderr + result.stdout)
        try:
            p = json.loads(result.stdout)['Properties']
        except (KeyError, ValueError):
            raise Blocked('Invalid MSBuild property output: ' + relative)
        if p['IsPackable'] == 'true':
            p['Project'] = relative
            packages.append(p)
    if {p['PackageId'] for p in packages} != EXPECTED_IDS or len(packages) != 8:
        raise Blocked('Package inventory differs from the reviewed eight-package architecture')
    for p in packages:
        if p['PackageId'] != Path(p['Project']).stem:
            raise Blocked('Package identity override requires owner review')
        for key in ('RepositoryUrl', 'PackageLicenseExpression', 'PackageReadmeFile', 'Description'):
            if not p.get(key):
                raise Blocked('Missing package metadata: ' + p['PackageId'] + '/' + key)
        if p['TargetFramework'] != 'net10.0':
            raise Blocked('Target framework differs from reviewed release architecture')
    by_path = {(root / p['Project']).resolve(): p['PackageId'] for p in packages}
    for p in packages:
        path = root / p['Project']
        project = ET.parse(path)
        p['InternalDependencies'] = []
        for ref in project.iter('ProjectReference'):
            dep = (path.parent / ref.attrib['Include'].replace('\\', '/')).resolve()
            if dep not in by_path:
                raise Blocked('Ungoverned project dependency: ' + str(dep))
            p['InternalDependencies'].append(by_path[dep])
        for ref in project.iter('PackageReference'):
            if ref.attrib.get('Include', '').startswith('TSharpVision'):
                raise Blocked('Internal PackageReference requires separate version consistency review')
    return versions[0], sorted(packages, key=lambda p: p['PackageId'])


def changelog_section(text, version):
    matches = list(re.finditer(r'^## (.+?)\s*$', text, re.M))
    found = [i for i, m in enumerate(matches) if m.group(1) == version]
    if len(found) > 1:
        raise Blocked('Conflicting duplicate changelog entries for ' + version)
    if not found:
        return None
    i = found[0]
    return text[matches[i].end(): matches[i + 1].start() if i + 1 < len(matches) else len(text)].strip()


def plan(root, data, output, commands):
    validate_metadata(data)
    root = root.resolve()
    output = (root / output).resolve()
    if not output.is_relative_to(root / 'Temp'):
        raise Blocked('Review output must stay inside target checkout Temp/')
    evidence = architecture(root)
    version, packages = evaluate(root, commands)
    v = data['version']
    if version != v or any(p['Version'] != v or p['PackageVersion'] != v for p in packages):
        raise Blocked('Requested version differs from repository; use tsharpvision-version-bump first. No edits made.')
    readme = read(root / 'README.md')
    changelog = read(root / 'CHANGELOG.md')
    # Keep exact heading consumed by prepare-github-release.ps1; pending/date is body text.
    body = ('Pending release.' if not data.get('date') else 'Release date: ' + data['date'] + '.') + '\n\n' + '\n'.join('- ' + c for c in data['changes'])
    existing = changelog_section(changelog, v)
    if existing is not None and existing != body:
        raise Blocked('Existing changelog entry conflicts with supplied release metadata; review manually')
    if existing is None:
        first = re.search(r'^## ', changelog, re.M)
        if not first:
            raise Blocked('Unrecognized maintained changelog structure')
        changelog = changelog[:first.start()] + '## ' + v + '\n\n' + body + '\n\n' + changelog[first.start():]
    default = r'Package metadata currently defaults to `[^`]+`\.'
    if len(re.findall(default, readme)) != 1:
        raise Blocked('README version statement requires manual discovery')
    readme = re.sub(default, 'Package metadata currently defaults to `' + v + '`.', readme)
    start, end = '<!-- release-prep:upcoming -->', '<!-- /release-prep:upcoming -->'
    upcoming = start + '\n**Upcoming ' + v + '** (' + data['channel'] + ', not yet published): ' + data['readme_summary'] + ' See [CHANGELOG.md](CHANGELOG.md).\n' + end
    if start in readme or end in readme:
        if readme.count(start) != 1 or readme.count(end) != 1 or readme.index(end) < readme.index(start):
            raise Blocked('Conflicting README release-prep markers')
        readme = re.sub(re.escape(start) + r'.*?' + re.escape(end), lambda _: upcoming, readme, flags=re.S)
    else:
        marker = '\n## Requirements'
        if marker not in readme:
            raise Blocked('README insertion point requires manual review')
        readme = readme.replace(marker, '\n' + upcoming + '\n' + marker, 1)
    release_claims = body + '\n' + data['summary'] + '\n' + data['readme_summary']
    for forbidden in data.get('excluded_claims', []):
        if forbidden.casefold() in release_claims.casefold():
            raise Blocked('Out-of-scope feature claim: ' + forbidden)
    if re.search(r'https://[^\s)]+/(?:releases/download|packages)/[^\s)]*' + re.escape(v), readme):
        raise Blocked('README points to unverified target-version download artifact')
    inventory = '\n'.join('- `' + p['PackageId'] + '`' for p in packages)
    notes = f'''# TSharpVision {v}

Release type: {data['channel']}. Draft only; publication has not been verified.

{data['summary']}

## Fixes / changes

{chr(10).join('- ' + c for c in data['changes'])}

## Compatibility

{data['compatibility']}

## Package inventory

{inventory}

## Installation / update after publication

First verify the version exists on your chosen feed for all packages you use. Then run:

```shell
dotnet add package TSharpVision.Drivers.Console --version {v}
```

Choose the matching Terminal or SDL driver for your platform; keep extension versions aligned.
The driver brings the core transitively. Until publication, retain an available feed version.
.NET 10 is required; existing platform restrictions still apply.

## Verification and limitations

All eight package identities and effective Version/PackageVersion values were evaluated locally.
This preparation did not pack any package, run implementation tests, or verify public availability.
Generated nuspec dependency ranges and hosted publication state remain owner checks.

{chr(10).join('- ' + c for c in data['limitations'])}
'''
    template = read(root / 'scripts/release-prep/manual-release-template.md')
    checklist = template.replace('{{VERSION}}', v).replace('{{CHANNEL}}', data['channel']).replace('{{PACKAGES}}', inventory).replace('{{ROOT}}', str(root).replace("'", "''"))
    if '{{' in checklist:
        raise Blocked('Unresolved runbook template placeholder')
    package_rows = '\n'.join('| ' + p['PackageId'] + ' | ' + p['Version'] + ' | ' + p['PackageVersion'] + ' | ' + (', '.join(p['InternalDependencies']) or 'none') + ' |' for p in packages)
    warnings = [
        'WARNING: GitHub Release notes generated by prepare-github-release.ps1 claim NuGet availability without a NuGet workflow dependency. Owner sequencing/decision required; workflows unchanged.',
        'WARNING: Package publication is nontransactional; duplicate versions fail. Inspect partial publication before any retry.',
        'NOT VERIFIED: Hosted release environment tag rules, reviewers, NuGet trusted publishing/NUGET_USER, remote refs and publication state.',
        'NOT VERIFIED: No development-only imports is supported by the explicit supplied scope and prior RC audit, not proven against Git history.',
        'NOT VERIFIED: Physical packing of all eight packages and generated nuspec ranges; not required for documentation preparation.',
    ]
    report = f'''# Release preflight: {v}

Preparation: PASS. Publication readiness: owner checks and sequencing decision remain.

PASS: strict SemVer/channel; central Version and all eight evaluated versions; package IDs;
required package metadata; net10.0; internal ProjectReference graph; exact changelog heading;
single consistent entry; README pending publication; release notes, package inventory and manual procedure.
PASS: release architecture inspected statically; v{v} matches v*; tagged scripts are used.
PASS: existing version-bump skill complements preparation; no version mutation.

| Package | Version | PackageVersion | Internal dependencies |
|---|---|---|---|
{package_rows}

{chr(10).join(warnings)}

## Scope supplied for this release

{chr(10).join('- ' + c for c in data['scope'])}

No network, credentials, Git commands, packing or publication helpers were used.
Property-only evaluation disables source control manager queries and requests no MSBuild target.
Architecture token checks identify the supported process; agent inspection of complete workflow files
is still required, especially if workflow conditions or scripts change.

## Inspected release architecture SHA-256

{chr(10).join('- `' + k + '`: `' + h + '`' for k, h in evidence.items())}

## Commands actually executed by this preflight

```text
{chr(10).join(subprocess.list2cmdline(c) for c in commands)}
```
'''
    return {root / 'README.md': readme, root / 'CHANGELOG.md': changelog,
            output / 'release-notes-draft.md': notes,
            output / 'manual-release-checklist.md': checklist,
            output / 'release-preflight-report.md': report}


def execute(root, metadata, output, mode, commands=None):
    commands = commands if commands is not None else []
    targets = plan(root, metadata, output, commands)
    changed = [p for p, text in targets.items() if not p.exists() or read(p) != text]
    if mode == 'apply':
        for p in changed:
            p.parent.mkdir(parents=True, exist_ok=True)
            # Preserve existing newline style; write only changed files.
            newline = '\r\n' if p.exists() and b'\r\n' in p.read_bytes() else '\n'
            with p.open('w', encoding='utf-8', newline='') as f:
                f.write(targets[p].replace('\n', newline))
    return changed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--metadata', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--mode', choices=('preview', 'apply'), default='preview')
    args = parser.parse_args()
    try:
        changes = execute(args.root, json.loads(read(args.metadata)), args.output, args.mode)
        print('PASS: ' + args.mode + '; ' + str(len(changes)) + ' file(s) ' + ('updated' if args.mode == 'apply' else 'would change'))
        for p in changes:
            print(p)
        print('WARNING: NuGet/GitHub sequencing decision required; see preflight/runbook.')
        print('NOT VERIFIED: remote refs, environment policy, credentials and public availability.')
        return 0
    except (Blocked, OSError, ET.ParseError, ValueError) as error:
        print('BLOCKED: ' + str(error), file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
