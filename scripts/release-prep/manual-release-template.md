# Manual release checklist: TSharpVision {{VERSION}}

OWNER ONLY. None of the commands in this document were executed by release-prep.
Release channel: {{CHANNEL}}. Intended tag: `v{{VERSION}}`.
Run commands from the release checkout in PowerShell. Stop on any failed verification.
Do not paste this entire document into a shell. Each gate requires human inspection.

## 1. Review and supply repository-specific values

The remote name, intended branch and exact release commit have NOT been established by this
no-Git preparation. Owner must supply them; never assume `origin` or `main`.

```powershell
Set-Location '{{ROOT}}'
git remote -v
git branch --show-current
git status --short --branch
git diff
git diff --cached
```

- [ ] Review the approved changes and tests for this release against README, CHANGELOG and
  the generated release-notes draft. Release-prep must add no unrelated runtime feature or API change.
- [ ] Review the current qualification report, filesystem audit and generated preflight evidence.
  Confirm effective Version/PackageVersion for every package in the generated inventory equals {{VERSION}}.
- [ ] Resolve outstanding platform, fallback, appearance and release-owner gates before publication.
  Check the actual evidence for Windows, Linux and macOS; skipped or unexecuted checks are not passes.
  A successful documentation preview does not establish production test or runtime qualification.
Review README, CHANGELOG, release-notes draft, runbook and both skill entry points.
Use the exact heading `## {{VERSION}}` in CHANGELOG; put a date or pending state in its body.

Supply values only after reviewing the outputs above:

```powershell
$releaseRemote = Read-Host 'Verified intended remote name'
$releaseBranch = Read-Host 'Verified intended release branch'
```

Before irreversible publication, inspect repository Settings > Environments > release:
selected branches/tags must permit the intended `v{{VERSION}}` tag (existing `v*` policy, if
configured, must include tags rather than only branch names); check reviewers and approvals.
Confirm NuGet trusted publishing and environment variable NUGET_USER with the owner; do not
print credentials. These hosted settings were NOT VERIFIED locally. Confirm all package
IDs in the generated inventory are owned by the intended publisher and {{VERSION}} is not already published.

## 2. Resolve the publication sequencing risk before pushing a tag

`.github/workflows/release.yml` (GitHub Release) automatically starts on a `v*` tag push.
It does NOT publish NuGet. `.github/workflows/publish-nuget.yml` (Publish NuGet) is manual only
and does NOT require a version tag; it checks out the selected dispatch ref's exact SHA.
`prepare-github-release.ps1` generates notes saying packages are available on NuGet.org,
without waiting for Publish NuGet. This can publish inaccurate notes or leave a public GitHub
Release when NuGet fails. The documentation draft's pending wording is not consumed by CI:
CI extracts CHANGELOG and generates its own notes.

Owner decision required: accept a temporary availability mismatch and coordinate both runs,
or separately authorize a workflow/notes fix before tagging. No workflow was changed here.
A safe existing-configuration option is to dispatch Publish NuGet on the reviewed pushed branch
commit, verify NuGet publication for every package in the generated inventory, and only then push the matching version tag.
That moves phase 6 before phase 4's tag push. The branch must also be allowed by the release
environment. Verify no branch movement between dispatch and tag creation. If branch dispatch
is disallowed and mismatch is unacceptable, STOP for a separate owner decision. Do not invent
new workflow triggers or assume a manual draft gate: successful GitHub Release runs publish automatically.

## 3. Stage, verify, commit and push the branch

Stage only individually reviewed release files. Owner must enumerate paths based on actual
status; no blanket staging command is supplied. Preparation files under Temp may be ignored;
review locally and deliberately choose whether an artifact belongs in the commit.

```powershell
# Repeat with each exact reviewed repository-relative path:
git add -- 'README.md' 'CHANGELOG.md'
git add -- '.agents/skills/tsharpvision-release-prep/SKILL.md' '.claude/skills/tsharpvision-release-prep/SKILL.md'
git add -- 'scripts/release-prep/prepare.py' 'scripts/release-prep/test_prepare.py' 'scripts/release-prep/manual-release-template.md'
# Also stage the approved release source/test/version files after reviewing their actual status.
git diff --cached --stat
git diff --cached
git status --short --branch
```

Gate: staged changes contain exactly intended release content, no secrets, development-only
features, generated binaries, or unrelated changes. The central version and approved runtime changes
must be in the commit, not merely unstaged. Then:

```powershell
git commit -m 'Prepare TSharpVision {{VERSION}} release'
$releaseCommit = (git rev-parse HEAD).Trim()
git show --stat $releaseCommit
git status --short --branch
```

Gate: verify intended branch and exact commit, expected upstream and remote destination,
then push that branch (no force):

```powershell
git push $releaseRemote "${releaseBranch}:${releaseBranch}"
git ls-remote $releaseRemote "refs/heads/$releaseBranch"
```

Confirm remote branch SHA equals $releaseCommit. If choosing NuGet-first sequencing, now do
phase 6 on this branch/ref and verify phase 7 NuGet checks before creating/pushing the tag.

## 4. Create and push the tag

Gate: confirm version, exact commit, branch push, environment rules, no pre-existing local or
remote tag, and resolved sequencing decision. Do not move/reuse a previously published tag.

```powershell
git tag --list 'v{{VERSION}}'
git ls-remote --tags $releaseRemote 'refs/tags/v{{VERSION}}' 'refs/tags/v{{VERSION}}^{}'
git show --no-patch $releaseCommit
```

Both tag queries must show absence before creation. Verify the commit contains the intended
scripts/workflows: checkout is pinned to the tagged commit, not the latest scripts on another branch.

```powershell
git tag -a 'v{{VERSION}}' $releaseCommit -m 'TSharpVision {{VERSION}}'
git rev-parse 'v{{VERSION}}^{commit}'
```

Gate: peeled local tag SHA equals $releaseCommit; remote tag is still absent. Pushing triggers
automatic public release processing:

```powershell
git push $releaseRemote 'refs/tags/v{{VERSION}}:refs/tags/v{{VERSION}}'
git ls-remote --tags $releaseRemote 'refs/tags/v{{VERSION}}' 'refs/tags/v{{VERSION}}^{}'
```

Confirm the remote peeled SHA equals $releaseCommit.

## 5. Observe GitHub Release

In Actions, open **GitHub Release** (`.github/workflows/release.yml`) for `v{{VERSION}}`.
Validate tagged release evaluates package versions/changelog, restores/builds/tests the solution
and validates all packages in the release manifest using pack-local-packages.ps1. Its validation packages are not
uploaded/published. The release-notes artifact is uploaded.

TVDemo matrix runs publish-tvdemo-release.ps1 for win-x64, linux-x64, osx-x64, osx-arm64.
Publish complete GitHub Release runs complete-github-release.ps1 locally for checksums and then
with -Publish. It creates a draft, uploads exactly four demo archives plus SHA256SUMS.txt,
verifies sizes/digests and remote tag commit, then automatically changes draft=false.
No nupkg/snupkg is attached to the GitHub Release; NuGet is their canonical location.

Do not run those helpers from release-prep: prepare-github-release invokes Git and the publish
path uses GitHub APIs. CI is the existing owner-triggered execution path.

## 6. Dispatch Publish NuGet, if not already successfully published

Gate: inspect NuGet first for ALL IDs in the generated inventory at {{VERSION}}. If all exist, verify provenance and
skip dispatch. If only some exist, STOP: publication is nontransactional and duplicates fail.
Do not retry the entire workflow blindly; owner must investigate partial publication separately.

In Actions select **Publish NuGet** (`.github/workflows/publish-nuget.yml`), then Run workflow.
Use the intended immutable tag `v{{VERSION}}` when allowed by the environment. If using the
NuGet-first option in phase 2, choose the reviewed pushed branch and verify its exact SHA before
and after dispatch. The manual workflow has no version input and publishes evaluated repository
versions; do not dispatch a default branch accidentally. Confirm the run's head SHA equals
$releaseCommit before approving the release environment job.

The workflow restores/builds/tests, packs and validates all packages in the release manifest, uploads nupkg/snupkg
workflow artifacts, logs in with NuGet trusted publishing, and pushes in manifest dependency order.
Only nupkg is explicitly pushed; dotnet also pushes its matching snupkg. Duplicates fail.
Check completion and every inventory package/version on the feed; do not assume GitHub Release success implies NuGet success.

## 7. Verify publication and complete only if necessary

Package inventory to check individually on NuGet.org at {{VERSION}}:

{{PACKAGES}}

Verify ID/version, dependencies aligned to {{VERSION}}, net10.0 payload, license/readme,
symbol packages and repository commit metadata. Download/inspect package metadata if necessary.
- [ ] Reconcile the generated inventory with the current release package manifest and local
  validation evidence for every nupkg and matching snupkg, including dependencies and symbols.
  Release-prep evaluates metadata; it does not pack packages or run production tests.
  Local package validation does not establish NuGet publication.
- [ ] Verify final package and distribution provenance against the exact reviewed release commit
  ($releaseCommit), including repository/SourceLink metadata, ExpectedCommit checks and checksums.
  Historical qualification of uncommitted source does not establish final committed-SHA provenance.

On GitHub verify tag v{{VERSION}}, title TSharpVision {{VERSION}}, target commit $releaseCommit,
release channel {{CHANNEL}} (RC/preview must have prerelease=true; stable false), draft=false,
four correctly named TVDemo archives and SHA256SUMS.txt. Download assets and verify checksums.
The generated notes' NuGet links must resolve for every inventory package before declaring completion.
Unsigned Windows/macOS and native graphics restrictions remain documented limitations.

A draft is unpublished; successful CI completes publication automatically. If failure left a
draft, first verify all five assets, checksums/digests, tag/commit, notes and prerelease state,
and NuGet outcome. Owner may finish through the GitHub Releases UI only after resolving the
failure; do not blindly rerun a workflow whose completion helper refuses existing drafts.
A rerun uses scripts from the original tagged commit and cannot pick up a later branch fix.
Never move a published tag; separately decide remediation for any tagged-script defect.

Record workflow run URLs, SHAs, NuGet checks and the published release URL. Only after actual
publication update any pending-release README wording or installation examples as a separate review.
