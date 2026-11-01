# TSharpVision release preparation

`prepare.py` prepares the release documentation of one version and writes a preflight report, offline. It needs
only **Python 3.10+** (standard library) and the **.NET 10 SDK** on `PATH`; `pwsh` (PowerShell 7) is needed for the
package checks below. It never runs Git, packs, publishes, tags or changes versions.

## What it checks

* The release architecture it was written for still exists (workflow and script signatures, this README).
* `Directory.Build.props` has exactly one `<Version>`, and the requested version equals it and every package's
  evaluated `Version`/`PackageVersion` (property-only `dotnet msbuild -getProperty`, no targets).
* The package inventory is exactly the reviewed nine library IDs (`EXPECTED_LIBRARY_IDS` in `prepare.py`) and three
  .NET tool packages with their commands (`EXPECTED_TOOLS`), twelve in total, each with repository URL, licence, readme,
  description and `net10.0`; internal dependencies are `ProjectReference`s only. A tool bundles the libraries it
  references and has no package dependencies.
* SemVer and channel agree (`-` prerelease ⇔ `prerelease`; build metadata is rejected).
* CHANGELOG has a single `## <version>` section, the README has one `Package metadata currently defaults to …`
  sentence, and no excluded claim appears in the release text.
* The publication order, the same order `pack-local-packages.ps1` writes to its release manifest: the libraries in
  dependency order, then the tools.

## Metadata

A JSON object; `scripts/release-prep/release-1.2.0.json` is the 1.2.0 example.

| Field | Required | Meaning |
|---|---|---|
| `version` | yes | SemVer without build metadata; must equal `Directory.Build.props` |
| `channel` | yes | `stable` or `prerelease` (must agree with the version) |
| `summary`, `compatibility`, `readme_summary` | yes | non-empty text |
| `changes`, `limitations`, `scope` | yes | non-empty lists of text |
| `excluded_claims` | no | phrases that must not appear in the release text |
| `date` | no | ISO `YYYY-MM-DD`; otherwise the generated changelog body says "Pending release." |
| `changelog` | no | `generate` (default): build the `## <version>` section from `changes`, or require that exact text. `existing`: keep a maintained `## <version>` section, which must exist. |

Anything else (for example an action field) is rejected.

## Commands

Windows PowerShell, from the repository root:

```powershell
# 1. Version: edit the single <Version> in Directory.Build.props by hand (and, at a release, set
#    PackageValidationBaselineVersion to the previously released version; see docs/contributing.md).
# 2. Unit tests (offline, fixtures only):
python scripts/release-prep/test_prepare.py
# 3. Preview (writes nothing; lists the files apply would change):
python scripts/release-prep/prepare.py --metadata scripts/release-prep/release-1.2.0.json --output Temp/release-1.2.0 --mode preview
# 4. Apply (writes README.md, CHANGELOG.md when generated, and Temp/release-1.2.0/*.md):
python scripts/release-prep/prepare.py --metadata scripts/release-prep/release-1.2.0.json --output Temp/release-1.2.0 --mode apply
# 5. Packages, without publishing (temporary folder; never the global NuGet cache):
$feed = Join-Path ([IO.Path]::GetTempPath()) 'tsv-release-feed'
dotnet restore TSharpVision.slnx
dotnet pack TSharpVision.slnx -c Release --no-restore -warnaserror -o $feed
pwsh ./scripts/pack-local-packages.ps1 -UseRepositoryVersion -ValidateOnly -OutputDirectory $feed -ReleaseManifestPath (Join-Path $feed 'manifest.json')
```

Portable (Linux/macOS shell): the same commands, with `python3` and `feed="${TMPDIR:-/tmp}/tsv-release-feed"`.
An optional check runs a real-repository preview and proves it leaves the files and the Git index untouched:
`TSV_RELEASE_PREP_REAL_REPO=1 python scripts/release-prep/test_prepare.py`.

Exit codes: `0` PASS; `2` BLOCKED (the message says what to fix; no file was written); `1` unexpected error.
`--output` must be inside the checkout's `Temp/` folder. Generated text contains no absolute paths, timestamps
or user names except the optional metadata `date`.

## Inspecting the output

`--mode apply` writes `Temp/<name>/release-notes-draft.md`, `manual-release-checklist.md` (from
`manual-release-template.md`) and `release-preflight-report.md` (versions, inventory, dependency order, the executed
commands and the SHA-256 of the inspected release files). Review them and the README/CHANGELOG diff.

## What stays manual (owner only)

Committing, pushing, the pull request into `main`, CI, nuget.org package-ID ownership and trusted-publishing
policy (including new IDs), dispatching **Publish NuGet**, creating and pushing the `v<version>` tag, the GitHub
release, and moving the validation baseline after publication. `manual-release-checklist.md` lists them in order.
