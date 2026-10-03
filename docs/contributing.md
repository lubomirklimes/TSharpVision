# Contributing

Each packable project tracks its public surface in `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. Shipped entries describe approved released API; add proposed public members to Unshipped, and record changes/removals using the PublicApiAnalyzers baseline format rather than silently rewriting released contracts. Include nullable annotations and keep declarations consistent with the source.

The ordinary build runs PublicApiAnalyzers. Missing, stale or duplicate baseline declarations are build errors (`RS0016`, `RS0017`, `RS0022`, `RS0024`). Review public API changes for compatibility as well as analyzer correctness. When an API is approved for a release, promote its entries from Unshipped to Shipped and reconcile removal entries with the released baseline.

Validate changes from the repository root:

```shell
dotnet build TSharpVision.slnx -c Release
dotnet test TSharpVision.slnx -c Release --no-build
```

For runnable Markdown examples, `tools/docs/validate_snippets.py` restores and builds consumers against an exact local package feed. Supply `--feed` and `--version`; the feed must include upstream dependencies. Its default output is ignored under `obj/`.
