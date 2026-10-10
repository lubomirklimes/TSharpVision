[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version,
    [string] $AssetDirectory = 'artifacts/tvdemo-release/archives',
    [string] $NotesPath = 'artifacts/github-release/release-notes.md',
    [string] $Commit,
    # Without this switch the script is entirely local: validate assets and checksum.
    [switch] $Publish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if ($Version -notmatch '^[0-9A-Za-z.+-]+$') { throw 'Invalid version for archive filename.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $names = @(
        "TVDemo-$Version-win-x64.zip"
        "TVDemo-$Version-linux-x64.tar.gz"
        "TVDemo-$Version-osx-x64.tar.gz"
        "TVDemo-$Version-osx-arm64.tar.gz"
    ) | Sort-Object
    $items = @(Get-ChildItem -LiteralPath $AssetDirectory -Force)
    # A second invocation may find our own checksum file; nothing else is allowed.
    $unexpected = @($items | Where-Object { $_.PSIsContainer -or $_.Name -cnotin ($names + @('SHA256SUMS.txt')) })
    if ($unexpected.Count) { throw "Unexpected release assets: $($unexpected.Name -join ', ')" }
    $assets = @(foreach ($name in $names) {
        $file = Get-Item -LiteralPath (Join-Path $AssetDirectory $name)
        if ($file.Length -eq 0) { throw "Empty release archive: $name" }
        $file
    })
    $hashes = @{}
    $checksums = @(foreach ($file in $assets) {
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $hashes[$file.Name] = "sha256:$hash"
        "$hash  $($file.Name)"
    })
    $checksumPath = Join-Path $AssetDirectory 'SHA256SUMS.txt'
    # LF and UTF-8 without BOM make this directly usable by sha256sum -c.
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($checksumPath), ($checksums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
    $assets += Get-Item -LiteralPath $checksumPath
    $hashes['SHA256SUMS.txt'] = 'sha256:' + (Get-FileHash $checksumPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Validated exactly four archives; generated $checksumPath."
    if (-not $Publish) { return }

    if (-not $env:GH_REPO -or -not $env:GH_TOKEN) { throw 'GH_REPO and GH_TOKEN are required to publish.' }
    if ($Commit -notmatch '^[0-9a-f]{40}$') { throw 'An exact validated commit SHA is required.' }
    if (-not (Test-Path -LiteralPath $NotesPath)) { throw "Release notes missing: $NotesPath" }
    $tag = "v$Version"
    $prerelease = ($Version.Split('+')[0].Contains('-')).ToString().ToLowerInvariant()
    # List with pagination includes drafts and distinguishes API errors from absence.
    $pages = gh api --paginate --slurp "repos/$env:GH_REPO/releases?per_page=100" | ConvertFrom-Json
    foreach ($page in $pages) {
        foreach ($existing in $page) {
            if ($existing.tag_name -ceq $tag) { throw "Release for '$tag' already exists (including drafts). Manual review required; no assets overwritten." }
        }
    }
    # Recheck the remote tag's peeled commit; never release a moved tag.
    function Assert-RemoteTagCommit {
        $ref = gh api "repos/$env:GH_REPO/git/ref/tags/$tag" | ConvertFrom-Json
        $object = $ref.object
        while ($object.type -eq 'tag') {
            $annotated = gh api "repos/$env:GH_REPO/git/tags/$($object.sha)" | ConvertFrom-Json
            $object = $annotated.object
        }
        if ($object.type -ne 'commit' -or $object.sha -cne $Commit) { throw "Remote tag '$tag' no longer points to validated commit $Commit." }
    }
    Assert-RemoteTagCommit
    gh release create $tag --verify-tag --draft "--prerelease=$prerelease" `
        --title "TSharpVision $Version" --notes-file $NotesPath --target $Commit
    $assetPaths = @($assets.FullName)
    gh release upload $tag @assetPaths
    $release = gh api "repos/$env:GH_REPO/releases/tags/$tag" | ConvertFrom-Json
    if (-not $release.draft) { throw 'Expected an unpublished draft during asset verification.' }
    if ($release.assets.Count -ne $assets.Count) { throw 'Uploaded asset count differs from the five-file allowlist.' }
    foreach ($file in $assets) {
        $remote = @($release.assets | Where-Object { $_.name -ceq $file.Name })
        if ($remote.Count -ne 1 -or $remote[0].state -ne 'uploaded' -or
            $remote[0].size -ne $file.Length -or $remote[0].digest -cne $hashes[$file.Name]) {
            throw "Uploaded asset verification failed: $($file.Name). Release remains a draft for manual review."
        }
    }
    Assert-RemoteTagCommit
    gh release edit $tag --draft=false "--prerelease=$prerelease" --verify-tag
    Write-Host "Published TSharpVision $Version from $Commit with five verified assets."
}
finally { Pop-Location }
