[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string] $RuntimeIdentifier,
    [string] $OutputDirectory = 'artifacts/tvdemo-release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if ($Version -notmatch '^[0-9A-Za-z.+-]+$') { throw 'Invalid version for archive filename.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $publishDirectory = [IO.Path]::GetFullPath((Join-Path $OutputDirectory $RuntimeIdentifier))
    $archiveDirectory = [IO.Path]::GetFullPath((Join-Path $OutputDirectory 'archives'))
    if (Test-Path $publishDirectory) { throw "Publish directory must be fresh: $publishDirectory" }
    New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null
    $extension = if ($RuntimeIdentifier.StartsWith('win-')) { 'zip' } else { 'tar.gz' }
    $archive = Join-Path $archiveDirectory "TVDemo-$Version-$RuntimeIdentifier.$extension"
    if (Test-Path $archive) { throw "Archive already exists: $archive" }

    dotnet publish samples/TSharpVision.Samples.TVDemo/TSharpVision.Samples.TVDemo.csproj `
        -c Release -r $RuntimeIdentifier --self-contained true `
        -p:PublishTrimmed=false -p:PublishSingleFile=false -p:UseAppHost=true `
        -p:TreatWarningsAsErrors=true -warnaserror -v minimal -o $publishDirectory

    $executable = 'TSharpVision.Samples.TVDemo'
    if ($extension -eq 'zip') { $executable += '.exe' }
    # Check the apphost, .NET runtime, drivers, help/config and SDL native payload.
    $nativeFiles = switch -Wildcard ($RuntimeIdentifier) {
        'win-*' { 'coreclr.dll'; 'SDL3.dll'; 'SDL3_ttf.dll'; 'SDL3_shadercross.dll' }
        'linux-*' { 'libcoreclr.so'; 'libSDL3.so*'; 'libSDL3_ttf.so*'; 'libSDL3_shadercross.so*' }
        'osx-*' { 'libcoreclr.dylib'; 'libSDL3*.dylib'; 'libSDL3_ttf*.dylib'; 'libSDL3_shadercross*.dylib' }
    }
    $required = @($executable, 'TSharpVision.Samples.TVDemo.cfg', 'Help/tvdemo.hlp', 'Help/tvdemo.txt',
        'TSharpVision.Drivers.Console.dll', 'TSharpVision.Drivers.Terminal.dll', 'TSharpVision.Drivers.SDL.dll') + @($nativeFiles)
    foreach ($file in $required) {
        if (-not (Test-Path (Join-Path $publishDirectory $file))) { throw "Missing publish payload: $file ($RuntimeIdentifier)" }
    }
    $forbidden = @(Get-ChildItem $publishDirectory -Recurse | Where-Object {
        $_.Name -in @('obj', 'bin') -or $_.Name -match '\.(s?nupkg)$'
    })
    if ($forbidden.Count) { throw 'Publish output contains forbidden intermediate/package files.' }
    Copy-Item LICENSE $publishDirectory
    @"
TVDemo $Version ($RuntimeIdentifier)

Run $executable from this extracted directory (prefix ./ on Linux/macOS).
No separate .NET runtime is required. The configuration selects console/terminal.
Keep the driver DLLs, native libraries, configuration and Help directory together.
SDL graphics require compatible system graphics libraries and a display.
Windows binaries are unsigned; macOS binaries are unsigned/not notarized.
See the GitHub Release notes for package links and SHA-256 checksums.
"@ | Set-Content (Join-Path $publishDirectory 'README.txt') -Encoding utf8NoBOM
    if ($extension -eq 'zip') {
        Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archive
    }
    else {
        # Native Unix runners preserve executable bits inside tar, before artifact upload.
        chmod +x (Join-Path $publishDirectory $executable)
        tar -czf $archive -C $publishDirectory .
    }
    Write-Host "Created $archive"
}
finally { Pop-Location }
