# Contributing

Each packable project tracks its public surface in `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. Shipped entries describe approved released API; add proposed public members to Unshipped, and record changes/removals using the PublicApiAnalyzers baseline format rather than silently rewriting released contracts. Include nullable annotations and keep declarations consistent with the source.

The ordinary build runs PublicApiAnalyzers. Missing, stale or duplicate baseline declarations are build errors (`RS0016`, `RS0017`, `RS0022`, `RS0024`). Review public API changes for compatibility as well as analyzer correctness. When an API is approved for a release, promote its entries from Unshipped to Shipped and reconcile removal entries with the released baseline.

Validate changes from the repository root:

```shell
dotnet build TSharpVision.slnx -c Release
dotnet test TSharpVision.slnx -c Release --no-build --results-directory TestResults/qualification/<run-id>
```

For runnable Markdown examples, `scripts/validate-doc-snippets.py` restores and builds consumers against an exact local package feed. Supply `--feed` and `--version`; the feed must include upstream dependencies. Its default output is ignored under `obj/`.

## Test ownership

`tests/TSharpVision.Tests` owns framework/runtime regressions, including frame geometry, GroupBox behavior and menu command-state rendering. It references only the framework, extensions and `TSharpVision.TestSupport`; it must not reference Testing, TestWorkbench or SampleTarget. Its root-screen copy and narrow menu assertion helpers are local to the test assembly.

Workbench UI, protocol, orchestration, visual/baseline, diagnostics and provider-integration tests now belong to the separate TSharpWorkbench source tree under `src/`. Workbench owns SampleTarget and its build staging. Framework CI and release build only the projects retained in TSharpVision.slnx and need no Workbench checkout.

Run affected framework tests from this repository root:

```shell
dotnet test tests/TSharpVision.Tests/TSharpVision.Tests.csproj -c Release --results-directory TestResults/framework/<run-id>
```

Use a fresh result directory for each run. For Workbench tests and logical capture, use that repository's solution, scripts and documentation.

## Manual NuGet.org publishing

`Publish NuGet` (`.github/workflows/publish-nuget.yml`) runs only through
`workflow_dispatch` on Ubuntu with .NET 10. Ordinary CI never publishes. The job
checks out the dispatch SHA, restores, rebuilds Release with warnings treated as
errors, runs all solution tests, then packs into a fresh `artifacts/nuget-publish`.
No runner-wide terminal settings are needed by current CI.

The existing `scripts/pack-local-packages.ps1` provides shared discovery and
validation for CI and publishing through `-UseRepositoryVersion`. It evaluates
`IsPackable` and `PackageVersion` for solution projects, preserving the governed
allowlist in `Directory.Build.props`. Default local-feed versions still receive
their unique suffix. Release checks cover the exact main/symbol set, IDs, versions,
internal dependencies, target frameworks, repository/license/README metadata,
portable PDBs, and forbidden diagnostics/test/sample/Commander binaries and type
references. Publishing additionally checks repository commit metadata against the
dispatch SHA. Validated packages are retained as artifact `nuget-<version>`.

Current packages are core `TSharpVision`, drivers `TSharpVision.Drivers.Console`,
`TSharpVision.Drivers.SDL`, `TSharpVision.Drivers.Terminal`, and extensions
`TSharpVision.CodeEditor`, `TSharpVision.Graphics`, `TSharpVision.HexView`,
`TSharpVision.TableView`, `TSharpVision.Terminal`: nine `.nupkg` and nine matching
`.snupkg`. All eight depend on core; `TSharpVision.Graphics` depends on core only,
not on the SDL driver package. The validated nuspec dependency graph determines
publication order: core first, then alphabetical peers (CodeEditor, Console driver,
SDL driver, Terminal driver, Graphics, HexView, TableView, Terminal).
Discovery/order/version are dynamic. `TSharpVision.Graphics` is a new package ID in
1.2.0: the NuGet.org trusted-publishing policy must allow it before the first push.

The three command-line tools are .NET tool packages (`PackAsTool`), listed separately
in `Directory.Build.props` (`IsTSharpVisionTool`): `TSharpVision.ResourceCompiler`
(`svrc`), `TSharpVision.HelpCompiler` (`svhc`) and `TSharpVision.ResourceTools`
(`svres`). They share the version, repository, license and README metadata, but not
the library rules: no public API analyzer or baseline, no package validation baseline
and no `.snupkg`. A release therefore has twelve `.nupkg` (nine libraries, three
tools) and nine `.snupkg`. Tools are validated by their own route: one `DotnetTool`
command matching `ToolCommandName`, a framework-dependent payload under
`tools/net10.0/any`, only the tool assembly and the TSharpVision assemblies it
bundles through project references, and no NuGet dependencies. The release manifest
lists the libraries in dependency order, then the tools. All three are new package
IDs in 1.2.0 and need the same trusted-publishing allowance as `TSharpVision.Graphics`.

### Configure once, manually

1. In GitHub repository **Settings → Environments**, create `release`.
2. Under that environment's **Environment variables**, add `NUGET_USER` with your
   NuGet.org **profile username**, not an email. A repository Actions variable of
   the same name also works; the environment variable takes precedence. Do not
   configure a permanent `NUGET_API_KEY` secret.
3. Optionally enable required reviewers on `release` and restrict deployment refs
   to the branches/tags you intend to release.
4. On NuGet.org, sign in as the configured user, choose **Trusted Publishing**, and
   create a policy with **Repository Owner** `<your GitHub repository owner>`,
   **Repository** `TSharpVision`, **Workflow File** `publish-nuget.yml`, and
   **Environment** `release`. Supply only the filename, not `.github/workflows/`.
   Choose the intended package owner (your account or organization) and scopes
   allowing new packages and new versions for the governed package IDs. The user
   must have publishing rights for that owner and those packages.

The job requests only `contents: read` and `id-token: write`. After all validation
and artifact upload, `NuGet/login@v1` exchanges GitHub OIDC for a short-lived key;
only its output is passed to `dotnet nuget push` at
`https://api.nuget.org/v3/index.json`. No stored API key is used. See the official
[Trusted Publishing instructions](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).
The client pushes the matching `.snupkg` with each `.nupkg`; symbols are not pushed
separately. See [symbol publishing](https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg).

### First publication

1. Review, commit and push the RC source and workflow yourself; ensure the workflow
   exists on the default branch so GitHub exposes **Run workflow**.
2. Verify normal CI is green for the intended source commit. Tags do not publish
   NuGet packages; push the release tag only after verifying NuGet publication below.
3. Open **Actions → Publish NuGet → Run workflow**, select the intended branch/tag,
   and dispatch. Check the run's commit SHA before approving `release`, if reviewers
   are configured.
4. Inspect the successful pushes, package versions and symbol processing on NuGet.org.

A repository-wide concurrency group serializes publishing across refs and does
not cancel an active run. Duplicate versions fail loudly: no `--skip-duplicate`.
NuGet publishing is **not transactional** across packages; a failure may leave an
already published subset. Stop and inspect NuGet state before any retry (a full
rerun will fail on duplicates). Recovery is manual; there is no rollback/unlisting
automation. All build, test and package checks finish before the first push.

## GitHub Releases and TVDemo downloads

`GitHub Release` (`.github/workflows/release.yml`) runs only when a `v*` tag is
pushed. It never creates a tag, publishes NuGet packages, or uses GitHub Packages.
Change the version only once per release: NuGet, the tag `v<version>` and the
GitHub Release `TSharpVision <version>` use the same version. Adding the GitHub
Release after NuGet publication does not require another version bump.

Release sequence (performed manually by the maintainer):

1. Finalize the version, matching `## <version>` changelog section and public API.
2. Commit and push the complete release source and workflows.
3. Require green normal CI for the intended release commit.
4. Run **Publish NuGet** on that exact commit and verify all packages on NuGet.org.
5. Create `v<version>` on that same commit and push the tag.
6. Wait for **GitHub Release** to validate and build TVDemo, then verify downloads
   and `SHA256SUMS.txt` on the published release.

NuGet **1.0.0-rc.1 is already published** and remains release history. For rc.2,
commit and push the finalized **1.0.0-rc.2** source, require green CI, then dispatch
**Publish NuGet** on that exact commit/ref. Verify all eight packages on NuGet.org
before creating **v1.0.0-rc.2** on the same commit and pushing the tag. Do not advance
the selected branch between checking its SHA and dispatching the publish workflow.

The workflow checks out the pushed SHA, evaluates every public solution project's
MSBuild `PackageVersion`, and requires exact equality with the tag version. It
extracts only the matching changelog section and discovers public NuGet IDs from
MSBuild. Restore and Release rebuild treat warnings as errors, all solution tests
must pass, and the existing package-validation script checks locally packed
packages and their commit metadata. Those packages never become release assets.
Release tests set process-local `TERM=xterm-256color` so pager/terminal subprocesses
receive a usable terminal description; no runner-wide terminal settings are changed.

Native builds publish `samples/TSharpVision.Samples.TVDemo/TSharpVision.Samples.TVDemo.csproj`
with `SelfContained=true`, `PublishTrimmed=false`, `PublishSingleFile=false` and an
apphost. Runner mapping: `win-x64` → `windows-2025`, `linux-x64` → `ubuntu-24.04`,
`osx-x64` → `macos-15-intel`, `osx-arm64` → `macos-15` (Apple Silicon).
See the [GitHub-hosted runner reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).
SDL native packages supply both macOS architectures; committed SPIR-V shaders
avoid needing the Windows DXC tool on Unix. Native libraries, driver assemblies,
help/config files, LICENSE and a short README stay together in each distribution.

Windows downloads are `TVDemo-<version>-win-x64.zip`; Unix downloads are
`TVDemo-<version>-<rid>.tar.gz`, preserving executable permissions. Extract and
run `TSharpVision.Samples.TVDemo.exe` on Windows or `./TSharpVision.Samples.TVDemo`
from the extracted directory in a Linux/macOS terminal. No separately installed
.NET runtime is needed. The bundled configuration selects console/terminal;
SDL graphics additionally need a compatible display and system graphics libraries.
macOS binaries are unsigned/not notarized and Gatekeeper may warn. Windows
binaries are unsigned and SmartScreen may warn; no signing credentials are used.

Only the four demo archives and `SHA256SUMS.txt` are uploaded, alongside GitHub's
automatic source archives. NuGet.org remains the canonical package registry;
`.nupkg`/`.snupkg` are never attached. Checksums cover only demo archives. Verify
with `sha256sum -c SHA256SUMS.txt` on Linux, `shasum -a 256 -c SHA256SUMS.txt` on
macOS, or `Get-FileHash <archive> -Algorithm SHA256` on Windows.

Build jobs use only `contents: read`; the final job uses `contents: write`, with
no NuGet credentials, release environment or OIDC permissions. Same-tag runs
are serialized without cancelling an active release. An existing release,
including a draft, causes a clear failure requiring manual review. After all
builds succeed, the job checks the exact asset allowlist, creates a draft, uploads
all five files, and verifies their names, sizes, uploaded state and server SHA-256
digests before publishing. It rechecks the remote tag's commit before creation
and publication. Any failure after draft creation leaves the draft for manual
inspection; reruns never overwrite it. See the official CLI documentation for
[draft creation](https://cli.github.com/manual/gh_release_create) and
[publication](https://cli.github.com/manual/gh_release_edit).
Semantic versions with a prerelease suffix produce a Pre-release; stable versions
produce a normal release. There is no manual branch-based release dispatch.

For local checks, `scripts/prepare-github-release.ps1 -Tag v<version>` validates
versions and writes notes without requiring the tag to exist locally.
`scripts/publish-tvdemo-release.ps1 -Version <version> -RuntimeIdentifier <rid>`
publishes and archives into a fresh directory. Use `-OutputDirectory` for a
separate scratch directory when repeating a publish. After collecting all four
archives, `scripts/complete-github-release.ps1 -Version <version>` checks the
allowlist and generates checksums locally. Its `-Publish` switch is exclusively
for the final Actions job; do not use it during local validation.

## Stable 1.x releases

The next release target is **1.2.0**, directly stable; 1.1.0 is the latest published release. A preview or RC cycle is optional and requires an explicit owner decision. All nine library packages and the three tool packages use the repository version. Package validation compares the eight previously released library packages with released 1.1.0 (`PackageValidationBaselineVersion` in `Directory.Build.props`). `TSharpVision.Graphics` is released for the first time and has no baseline yet (its project sets the baseline empty); after 1.2.0 is published, set its baseline to the first published Graphics version. Promote approved additive APIs into Shipped for release; keep all released declarations intact.

Release scope: 1.2.0 is the display and graphics release (palette families, SDL GPU physical palettes and display effects, `TSharpVision.Graphics`, localization, Help search, the session file-dialog directory).

Internal driver attachment, output tracing and pixel-readback seams remain in the driver assemblies because they exercise production rendering. Friend assembly grants to external Workbench diagnostics are intentional; they do not make Workbench projects, scenarios or orchestration part of this repository. The CPU-only atlas uses the same density-aware sizing as production; use its instance `Size` for its actual side length.

For the current framework structure and extension boundaries, see [the architecture overview](architecture/overview.md).
