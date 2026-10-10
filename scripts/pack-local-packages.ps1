<#!
.SYNOPSIS
Builds a uniquely versioned set of TSharpVision packages for a local NuGet feed.

.DESCRIPTION
Discovers the packable projects in TSharpVision.slnx, builds them with one local
version, packs them into artifacts/local-nuget, and validates package identity and
internal TSharpVision dependency versions. Generated versions use UTC.

TSharpCommander workflow:
1. Run this script and copy the emitted version.
2. Configure artifacts/local-nuget as a NuGet source for TSharpCommander.
3. Update TSharpCommander's TSharpVision package references to the emitted version.
4. Restore and build TSharpCommander.

.PARAMETER Version
An explicit NuGet/SemVer version to use exactly. Existing packages are never
overwritten. If omitted, a unique version is derived from the repository version.

.PARAMETER Configuration
The build configuration. Defaults to Release.

.PARAMETER OutputDirectory
The local feed directory. Relative paths are resolved from the repository root.
Defaults to artifacts/local-nuget.

.PARAMETER SkipBuild
Skips restore and rebuild. This assumes compatible binaries already exist for the
selected configuration and version.

.PARAMETER UseRepositoryVersion
Uses the evaluated MSBuild PackageVersion unchanged and validates release metadata
and payloads. CI and publishing share this mode; default local-feed behavior stays
unchanged.

.PARAMETER ValidateOnly
Validates every package already present in OutputDirectory without building or packing.
Requires UseRepositoryVersion.

.PARAMETER ReleaseManifestPath
Writes the validated version and dependency-ordered nupkg filenames to JSON.

.PARAMETER ExpectedCommit
Requires the nuspec repository commit to match this exact commit in release mode.

.EXAMPLE
pwsh ./scripts/pack-local-packages.ps1

.EXAMPLE
pwsh ./scripts/pack-local-packages.ps1 -Version 0.1.0-preview.1.local.test1
#>
[CmdletBinding()]
param(
    [string] $Version,
    [ValidateNotNullOrEmpty()]
    [string] $Configuration = 'Release',
    [string] $OutputDirectory,
    [switch] $SkipBuild,
    # Release/CI mode keeps the evaluated PackageVersion, without a local suffix.
    [switch] $UseRepositoryVersion,
    # Validate an already packed directory; never builds or packs in this mode.
    [switch] $ValidateOnly,
    [string] $ReleaseManifestPath,
    [string] $ExpectedCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
. (Join-Path $PSScriptRoot 'verify-release-package.ps1')

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'TSharpVision.slnx'

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts/local-nuget'
}
elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot $OutputDirectory
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

function Get-ProjectProperties {
    param([Parameter(Mandatory)][string] $ProjectPath)

    $json = & dotnet msbuild $ProjectPath -nologo `
        -getProperty:IsPackable `
        -getProperty:PackageId `
        -getProperty:Version `
        -getProperty:PackageVersion `
        -getProperty:TargetFramework `
        -getProperty:TargetFrameworks `
        -getProperty:RepositoryUrl `
        -getProperty:RepositoryType `
        -getProperty:PackageLicenseExpression `
        -getProperty:PackageReadmeFile `
        -getProperty:IncludeSymbols `
        -getProperty:SymbolPackageFormat

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to evaluate MSBuild properties for '$ProjectPath'."
    }

    return ($json | ConvertFrom-Json).Properties
}

function Assert-NuGetVersion {
    param([Parameter(Mandatory)][string] $Value)

    # NuGet accepts SemVer 2.0.0. Build metadata is intentionally rejected because
    # it does not create a distinct precedence and is unsafe for local package caches.
    $identifier = '(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
    $pattern = "^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-(?:$identifier)(?:\.(?:$identifier))*)?$"
    if ($Value -notmatch $pattern) {
        throw "'$Value' is not a supported NuGet/SemVer version. Use three numeric components and optional valid prerelease identifiers; build metadata is not allowed."
    }
}

function Read-Nuspec {
    param([Parameter(Mandatory)][string] $PackagePath)

    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
        if ($nuspecEntries.Count -ne 1) {
            throw "Package '$PackagePath' contains $($nuspecEntries.Count) nuspec files; expected exactly one."
        }

        $reader = [IO.StreamReader]::new($nuspecEntries[0].Open())
        try {
            [xml] $nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    if ($null -eq $metadata) {
        throw "Package '$PackagePath' has no nuspec metadata element."
    }

    [pscustomobject]@{
        Id           = $metadata.SelectSingleNode("*[local-name()='id']").InnerText
        Version      = $metadata.SelectSingleNode("*[local-name()='version']").InnerText
        Dependencies = @($metadata.SelectNodes(".//*[local-name()='dependency']"))
        Metadata     = $metadata
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

[xml] $solution = Get-Content -LiteralPath $solutionPath -Raw
$projectPaths = @(
    $solution.SelectNodes("//*[local-name()='Project']") |
        ForEach-Object { [IO.Path]::GetFullPath((Join-Path $repositoryRoot $_.Path)) }
)

$packages = @()
foreach ($projectPath in $projectPaths) {
    $properties = Get-ProjectProperties -ProjectPath $projectPath
    if ($properties.IsPackable -eq 'true') {
        $packages += [pscustomobject]@{
            ProjectPath          = $projectPath
            PackageId            = $properties.PackageId
            RepositoryVersion    = $properties.PackageVersion
            Properties           = $properties
            ProducesSymbolPackage = ($properties.IncludeSymbols -eq 'true' -and $properties.SymbolPackageFormat -eq 'snupkg')
            InternalDependencies = @()
        }
    }
}

if ($packages.Count -eq 0) {
    throw 'No packable projects were discovered in the solution.'
}

$duplicateIds = @($packages | Group-Object PackageId | Where-Object Count -gt 1)
if ($duplicateIds.Count -gt 0) {
    throw "Duplicate package IDs were discovered: $($duplicateIds.Name -join ', ')."
}

$repositoryVersions = @($packages.RepositoryVersion | Sort-Object -Unique)
if ($repositoryVersions.Count -ne 1) {
    throw "Packable projects do not share one repository version: $($repositoryVersions -join ', ')."
}
$repositoryVersion = $repositoryVersions[0]
Assert-NuGetVersion -Value $repositoryVersion

if ($UseRepositoryVersion -and $Version) {
    throw 'UseRepositoryVersion cannot be combined with an explicit Version.'
}
if ($ValidateOnly -and -not $UseRepositoryVersion) {
    throw 'ValidateOnly requires UseRepositoryVersion.'
}
if ($UseRepositoryVersion) {
    $packageVersion = $repositoryVersion
}
elseif ($Version) {
    Assert-NuGetVersion -Value $Version
    $packageVersion = $Version
}
else {
    # Prefix the time and fractional-second identifiers so midnight values such as
    # 001205 do not become invalid SemVer numeric identifiers with leading zeroes.
    $utcSuffix = [DateTime]::UtcNow.ToString("yyyyMMdd.'t'HHmmss.'r'fffffff", [Globalization.CultureInfo]::InvariantCulture)
    if ($repositoryVersion.Contains('-')) {
        $packageVersion = "$repositoryVersion.local.$utcSuffix"
    }
    else {
        $packageVersion = "$repositoryVersion-local.$utcSuffix"
    }
    Assert-NuGetVersion -Value $packageVersion
}

$packageByProject = @{}
$packageIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($package in $packages) {
    $packageByProject[$package.ProjectPath] = $package.PackageId
    [void] $packageIds.Add($package.PackageId)
}

foreach ($package in $packages) {
    [xml] $project = Get-Content -LiteralPath $package.ProjectPath -Raw
    $dependencies = @()
    foreach ($reference in $project.SelectNodes("//*[local-name()='ProjectReference']")) {
        # MSBuild accepts backslashes on Unix; System.IO does not normalize them.
        $referencePath = $reference.Include.Replace('\', '/')
        $referencedPath = [IO.Path]::GetFullPath((Join-Path (Split-Path $package.ProjectPath) $referencePath))
        if ($packageByProject.ContainsKey($referencedPath)) {
            $dependencies += $packageByProject[$referencedPath]
        }
    }
    $package.InternalDependencies = @($dependencies | Sort-Object -Unique)
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$expectedArtifacts = @()
foreach ($package in $packages) {
    $expectedArtifacts += Join-Path $OutputDirectory "$($package.PackageId).$packageVersion.nupkg"
    if ($package.ProducesSymbolPackage) {
        $expectedArtifacts += Join-Path $OutputDirectory "$($package.PackageId).$packageVersion.snupkg"
    }
}

$collisions = @($expectedArtifacts | Where-Object { Test-Path -LiteralPath $_ })
if (-not $ValidateOnly -and $collisions.Count -gt 0) {
    throw "Refusing to overwrite existing package artifacts for version '$packageVersion':`n  $($collisions -join "`n  ")"
}

$artifactsBefore = @(
    Get-ChildItem -LiteralPath $OutputDirectory -File |
        Where-Object Extension -in '.nupkg', '.snupkg' |
        ForEach-Object FullName
)
if ($UseRepositoryVersion -and -not $ValidateOnly -and $artifactsBefore.Count) {
    throw 'Repository-version packing requires a directory with no existing packages.'
}

if (-not $ValidateOnly) {
    Push-Location $repositoryRoot
    try {
        if (-not $SkipBuild) {
            & dotnet restore $solutionPath
            & dotnet build $solutionPath -c $Configuration -t:Rebuild --no-restore -v minimal `
                "-p:Version=$packageVersion" "-p:PackageVersion=$packageVersion"
        }

        $packArguments = @()
        if ($UseRepositoryVersion) { $packArguments += '-warnaserror' }
        & dotnet pack $solutionPath -c $Configuration --no-build --no-restore -v minimal @packArguments `
            "-p:Version=$packageVersion" "-p:PackageVersion=$packageVersion" `
            -o $OutputDirectory
    }
    finally {
        Pop-Location
    }
}

$artifactsAfter = @(
    Get-ChildItem -LiteralPath $OutputDirectory -File |
        Where-Object Extension -in '.nupkg', '.snupkg' |
        ForEach-Object FullName
)
$producedArtifacts = @($artifactsAfter | Where-Object { $_ -notin $artifactsBefore })
if ($ValidateOnly) {
    $producedArtifacts = $artifactsAfter
}

$missingArtifacts = @($expectedArtifacts | Where-Object { $_ -notin $producedArtifacts })
$unexpectedArtifacts = @($producedArtifacts | Where-Object { $_ -notin $expectedArtifacts })
if ($missingArtifacts.Count -gt 0 -or $unexpectedArtifacts.Count -gt 0) {
    $details = @()
    if ($missingArtifacts.Count -gt 0) { $details += "Missing:`n  $($missingArtifacts -join "`n  ")" }
    if ($unexpectedArtifacts.Count -gt 0) { $details += "Unexpected:`n  $($unexpectedArtifacts -join "`n  ")" }
    throw "The produced package set did not match the discovered packable projects.`n$($details -join "`n")"
}

foreach ($artifact in $producedArtifacts) {
    $metadata = Read-Nuspec -PackagePath $artifact
    if (-not $packageIds.Contains($metadata.Id)) {
        throw "Package '$artifact' has unexpected ID '$($metadata.Id)'."
    }
    if ($metadata.Version -ne $packageVersion) {
        throw "Package '$artifact' has version '$($metadata.Version)', expected '$packageVersion'."
    }
    $expectedName = "$($metadata.Id).$packageVersion$([IO.Path]::GetExtension($artifact))"
    if ([IO.Path]::GetFileName($artifact) -ne $expectedName) {
        throw "Package filename does not match nuspec identity: '$artifact'."
    }
    if ($UseRepositoryVersion) {
        $package = $packages | Where-Object PackageId -eq $metadata.Id
        if (-not $package.ProducesSymbolPackage) {
            throw "Release package '$($package.PackageId)' must produce a matching snupkg."
        }
        Assert-ReleasePackage -Package $package -Path $artifact -ExpectedCommit $ExpectedCommit
    }

    # Symbol packages can contain dependency groups too, so validate every archive.
    foreach ($dependency in $metadata.Dependencies) {
        if ($UseRepositoryVersion -and $dependency.id -match '^TSharpVision(?:\.|$)' -and
            -not $packageIds.Contains($dependency.id)) {
            throw "Ungoverned internal dependency '$($dependency.id)' in '$artifact'."
        }
        if ($packageIds.Contains($dependency.id) -and $dependency.version -ne $packageVersion) {
            throw "Package '$($metadata.Id)' depends on internal package '$($dependency.id)' version '$($dependency.version)', expected '$packageVersion'."
        }
    }
}

foreach ($package in $packages) {
    $mainPackagePath = Join-Path $OutputDirectory "$($package.PackageId).$packageVersion.nupkg"
    $metadata = Read-Nuspec -PackagePath $mainPackagePath
    foreach ($expectedDependency in $package.InternalDependencies) {
        $actualDependency = @($metadata.Dependencies | Where-Object id -eq $expectedDependency)
        if ($actualDependency.Count -ne 1) {
            throw "Package '$($package.PackageId)' should contain exactly one dependency on '$expectedDependency'; found $($actualDependency.Count)."
        }
        if ($actualDependency[0].version -ne $packageVersion) {
            throw "Package '$($package.PackageId)' depends on '$expectedDependency' version '$($actualDependency[0].version)', expected '$packageVersion'."
        }
    }
}

if ($ReleaseManifestPath) {
    if (-not $UseRepositoryVersion) { throw 'ReleaseManifestPath requires UseRepositoryVersion.' }
    # Topological sort of validated nuspec dependencies, with alphabetical peers.
    $ordered = @()
    $remaining = @($packages | Sort-Object PackageId)
    while ($remaining.Count) {
        $ready = @($remaining | Where-Object {
            $metadata = Read-Nuspec -PackagePath (Join-Path $OutputDirectory "$($_.PackageId).$packageVersion.nupkg")
            @($metadata.Dependencies | Where-Object {
                $packageIds.Contains($_.id) -and $_.id -notin @($ordered | ForEach-Object PackageId)
            }).Count -eq 0
        })
        if (-not $ready.Count) { throw 'Cycle in internal package dependencies.' }
        $ordered += $ready
        $remaining = @($remaining | Where-Object PackageId -notin @($ordered | ForEach-Object PackageId))
    }
    [pscustomobject]@{
        Version = $packageVersion
        Packages = @($ordered | ForEach-Object { "$($_.PackageId).$packageVersion.nupkg" })
    } | ConvertTo-Json | Set-Content -LiteralPath $ReleaseManifestPath -Encoding utf8
    if ($env:GITHUB_OUTPUT) {
        "version=$packageVersion" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
    }
}

Write-Host ''
Write-Host 'TSharpVision packages validated successfully.'
Write-Host ''
Write-Host 'Version:'
Write-Host "  $packageVersion"
Write-Host ''
Write-Host 'Package directory:'
Write-Host "  $OutputDirectory"
Write-Host ''
Write-Host 'Packages:'
foreach ($package in $packages | Sort-Object PackageId) {
    Write-Host "  $($package.PackageId)"
}
Write-Host ''
if (-not $UseRepositoryVersion) {
Write-Host 'Use this version in TSharpCommander:'
Write-Host "  $packageVersion"
Write-Host ''
Write-Host 'Add the feed to TSharpCommander if needed:'
Write-Host "  dotnet nuget add source `"$OutputDirectory`" --name TSharpVisionLocal"
}
