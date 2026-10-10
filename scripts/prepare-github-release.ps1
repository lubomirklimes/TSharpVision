[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Tag,
    [string] $OutputDirectory = 'artifacts/github-release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    # The tag is untrusted input. Accept SemVer with optional prerelease/build metadata.
    $number = '(?:0|[1-9][0-9]*)'
    $identifier = '(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
    if ($Tag -cnotmatch "^v($number\.$number\.$number(?:-$identifier(?:\.$identifier)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?)$") {
        throw "Tag '$Tag' must be v followed by a valid semantic version."
    }
    $version = $Matches[1]
    [xml] $solution = Get-Content TSharpVision.slnx -Raw
    $packages = @(foreach ($project in $solution.SelectNodes('//Project')) {
        $json = dotnet msbuild $project.Path -nologo -getProperty:IsPackable,PackageId,PackageVersion
        $properties = ($json | ConvertFrom-Json).Properties
        if ($properties.IsPackable -eq 'true') {
            if ($properties.PackageVersion -cne $version) {
                throw "Tag version '$version' does not match MSBuild PackageVersion '$($properties.PackageVersion)' for '$($project.Path)'."
            }
            $properties.PackageId
        }
    })
    if ($packages.Count -eq 0) { throw 'No public packages found in the solution.' }

    $lines = @(Get-Content CHANGELOG.md)
    $headings = @(for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -cmatch '^## (.+?)\s*$' -and $Matches[1] -ceq $version) { $i }
    })
    if ($headings.Count -ne 1) { throw "Expected exactly one CHANGELOG heading '## $version'." }
    $start = $headings[0] + 1
    $end = $start
    while ($end -lt $lines.Count -and $lines[$end] -notmatch '^## ') { $end++ }
    $changes = if ($end -gt $start) { ($lines[$start..($end - 1)] -join "`n").Trim() } else { '' }
    if ([string]::IsNullOrWhiteSpace($changes)) { throw "CHANGELOG section '$version' is empty." }

    $commit = (git rev-parse HEAD).Trim()
    $prerelease = ($version.Split('+')[0].Contains('-')).ToString().ToLowerInvariant()
    $packageList = ($packages | Sort-Object | ForEach-Object { "- [$_](https://www.nuget.org/packages/$_/$version)" }) -join "`n"
    $notes = @"
# TSharpVision $version

$changes

## NuGet

Packages are available from NuGet.org, the canonical package distribution location:

$packageList

``````shell
dotnet add package TSharpVision --version $version
``````

## Demo downloads

- Windows x64: TVDemo-$version-win-x64.zip
- Linux x64: TVDemo-$version-linux-x64.tar.gz
- macOS Intel: TVDemo-$version-osx-x64.tar.gz
- macOS Apple Silicon: TVDemo-$version-osx-arm64.tar.gz

Extract the archive and run TSharpVision.Samples.TVDemo.exe on Windows, or
./TSharpVision.Samples.TVDemo from a terminal on Linux/macOS. The distributions
are self-contained and do not require a separately installed .NET runtime.
SHA256SUMS.txt contains SHA-256 checksums for all four demo archives.

macOS binaries are unsigned/not notarized; Gatekeeper may warn. Windows binaries
are unsigned and SmartScreen may warn. Graphical SDL drivers require a compatible
display and system graphics libraries; the bundled configuration selects the console/terminal driver.

Built from tagged commit $commit.
"@
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    Set-Content (Join-Path $OutputDirectory 'release-notes.md') $notes -Encoding utf8NoBOM
    Write-Host "Validated $Tag / PackageVersion $version at commit $commit; prerelease=$prerelease"
    if ($env:GITHUB_OUTPUT) {
        "version=$version", "prerelease=$prerelease", "commit=$commit" | Add-Content $env:GITHUB_OUTPUT
    }
    if ($env:GITHUB_STEP_SUMMARY) {
        "Validated **$Tag** against all $($packages.Count) public MSBuild PackageVersions. Commit: ``$commit``." | Add-Content $env:GITHUB_STEP_SUMMARY
    }
}
finally { Pop-Location }
