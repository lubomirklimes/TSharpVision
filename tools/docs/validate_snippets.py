"""Compile consumer Markdown examples against an exact local NuGet feed.

Usage: python tools/docs/validate_snippets.py --feed PATH --version VERSION
The feed must contain the four TSharpVision nupkg files and their upstream
dependencies. Output defaults to ignored obj/docs-validation and must be fresh.
No repository ProjectReferences or network package sources are used.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--feed', required=True, type=Path)
    parser.add_argument('--version', required=True)
    parser.add_argument('--output', type=Path, default=ROOT / 'obj/docs-validation')
    args = parser.parse_args()
    feed, output = args.feed.resolve(), args.output.resolve()
    if output.exists():
        raise SystemExit('Use a fresh output directory; existing results are never deleted.')
    if not feed.is_dir():
        raise SystemExit('Local package feed does not exist.')
    output.mkdir(parents=True)
    for file in ['Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props']:
        (output / file).write_text('<Project />\n')
    config = output / 'NuGet.Config'
    config.write_text('<configuration><packageSources><clear/><add key="local" value="' +
                     escape(str(feed), {'"': '&quot;'}) + '"/></packageSources>' +
                     '<config><add key="globalPackagesFolder" value="' +
                     escape(str(output / 'packages'), {'"': '&quot;'}) +
                     '"/></config></configuration>')
    specs = [('README.md', ['TSharpVision.Drivers.Console']),
             ('docs/getting-started.md', ['TSharpVision.Drivers.Console']),
             ('docs/drivers.md', ['TSharpVision.Drivers.Console', 'TSharpVision.Drivers.Terminal',
                                  'TSharpVision.Drivers.SDL', 'TSharpVision']),
             ('docs/dialogs-and-controls.md', ['TSharpVision'] * 6)]
    examples = []
    for document, packages in specs:
        blocks = re.findall(r'```csharp\r?\n(.*?)```', (ROOT / document).read_text(encoding='utf-8'), re.S)
        if len(blocks) != len(packages):
            raise SystemExit(f'Update validation coverage for {document}: unexpected C# block count')
        examples += [(document, i + 1, package, code) for i, (package, code) in enumerate(zip(packages, blocks))]
    sdl = examples[4]
    examples.append((sdl[0], 'gpu-variant', sdl[2], sdl[3].replace('"SDLDriver"', '"SDLGpuDriver"')))
    # A separate bounded probe runs the documented Console bootstrap in a
    # noninteractive host. It is not claimed as execution of the interactive snippet.
    probe = examples[2][3].replace('new TApplication()', 'new ConsoleSmoke()') + '''
sealed class ConsoleSmoke : TApplication
{
    public override void Idle()
    {
        base.Idle();
        if (TDisplay.driver?.GetType().Name != "Win32ConsoleDriver")
            throw new InvalidOperationException("Unexpected driver");
        EndModal(TSharpVision.Constants.Views.cmQuit);
    }
}
'''
    examples.append(('validation probe', 'console-headless', 'TSharpVision.Drivers.Console', probe))
    results = []
    for number, (document, block, package, code) in enumerate(examples):
        directory = output / f'example-{number + 1}'
        directory.mkdir()
        (directory / 'Program.cs').write_text(code, encoding='utf-8')
        composition = document == 'docs/dialogs-and-controls.md'
        output_type = 'Library' if composition else 'Exe'
        (directory / 'Consumer.csproj').write_text(
            f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>{output_type}</OutputType>'
            '<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
            '<Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>'
            f'<PackageReference Include="{escape(package)}" Version="[{escape(args.version)}]"/>'
            '</ItemGroup></Project>')
        def run(label, command):
            result = subprocess.run(command, cwd=directory, capture_output=True, text=True, timeout=120)
            (directory / f'{label}.log').write_text(result.stdout + result.stderr, encoding='utf-8')
            if result.returncode:
                raise RuntimeError(f'{directory.name} {label} failed: see its log')
        run('restore', ['dotnet', 'restore', '--configfile', str(config)])
        run('build', ['dotnet', 'build', '-c', 'Release', '--no-restore'])
        assets = json.loads((directory / 'obj/project.assets.json').read_text())
        assert all(item['type'] == 'package' for item in assets['libraries'].values())
        sources = assets['project']['restore']['sources']
        assert str(feed) in sources and all(Path(source).is_absolute() for source in sources)
        # The SDK may add its local library-packs directory. Every resolved
        # NuGet package must nevertheless match an archive in our supplied feed.
        for key, item in assets['libraries'].items():
            package_id, version = key.split('/')
            archive = f'{package_id.lower()}.{version}.nupkg'
            original = next((p for p in feed.glob('*.nupkg') if p.name.lower() == archive), None)
            assert original is not None, key
            cached = output / 'packages' / item['path'] / archive
            assert cached.read_bytes() == original.read_bytes(), key
        assert f'{package}/{args.version}' in assets['libraries']
        assert f'TSharpVision/{args.version}' in assets['libraries']
        runtime = ('Not run: composition helper requires an active application' if composition
                   else 'Not run: requires interactive matching host')
        if (package == 'TSharpVision' and not composition) or (block == 'console-headless' and os.name == 'nt'):
            run('run', ['dotnet', 'run', '-c', 'Release', '--no-build', '--no-restore'])
            runtime = 'Passed: headless lifecycle'
        results.append({'document': document, 'block': block, 'package': package,
                        'sourceSha256': hashlib.sha256(code.encode()).hexdigest(),
                        'compile': 'passed', 'runtime': runtime,
                        'graph': list(assets['libraries'])})
        print(f'{document} #{block}: compiled; {runtime}', flush=True)
    (output / 'results.json').write_text(json.dumps(results, indent=2) + '\n')


if __name__ == '__main__':
    main()
