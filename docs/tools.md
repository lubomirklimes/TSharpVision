# Command-line tools

Three command-line tools ship as .NET tool packages with the same version as the libraries. They need the .NET 10 runtime.

| Package | Command | Purpose |
|---|---|---|
| `TSharpVision.ResourceCompiler` | `svrc` | Compiles `.trc` resource scripts into `.tvr` resource files |
| `TSharpVision.HelpCompiler` | `svhc` | Compiles help source text into a `.hlp` help file and a C# file of help-context constants |
| `TSharpVision.ResourceTools` | `svres` | Lists, shows, dumps and validates the resources in a `.tvr` file |

## Install

Global installation puts the commands on your `PATH`:

```shell
dotnet tool install --global TSharpVision.ResourceCompiler --version 1.2.0
dotnet tool install --global TSharpVision.HelpCompiler --version 1.2.0
dotnet tool install --global TSharpVision.ResourceTools --version 1.2.0
```

To pin the tools to one project, install them into a local tool manifest instead. Run the commands with `dotnet`, for example `dotnet svrc`; after a clone, `dotnet tool restore` installs the versions recorded in `.config/dotnet-tools.json`:

```shell
dotnet new tool-manifest
dotnet tool install TSharpVision.ResourceCompiler --version 1.2.0
dotnet tool install TSharpVision.HelpCompiler --version 1.2.0
dotnet tool install TSharpVision.ResourceTools --version 1.2.0
```

Each tool prints its usage with `--help` or `-h`, and its name and version with `--version`; for example `svrc --version` prints `TSharpVision ResourceCompiler 1.2.0`.

## Compile and inspect resources

Given `app_en.trc`:

```text
resource strings "app.strings" {
  App_Title = "My application";
  Menu_File = "~F~ile";
}
```

compile it and check the result:

```shell
svrc compile app_en.trc app_en.tvr
svres validate app_en.tvr
svres list app_en.tvr
svres show app_en.tvr app.strings
svres dump app_en.tvr app.strings --hex
```

`svrc format <input.trc>` prints a `.trc` in canonical style; `--inplace` rewrites the file. Both tools print their usage when run without arguments. They exit with `0` on success and `1` on a usage or input error, for example a `.trc` syntax error, a missing file or an unknown resource key. On an error `svrc` writes no output file. `svres` reports a file that is not a `.tvr`, or is truncated or damaged, as an error instead of an empty resource list.

See [Localization](localization.md) for the string tables an application loads from `.tvr` files.

## Compile help

`svhc` compiles help source text into a `.hlp` file and a C# file with one `hc…` constant per topic:

```text
.topic Index=1
Welcome. See {Editing:Editing}.

.topic Editing=2
Editing help.
```

```shell
svhc help.txt help.hlp help.cs
```

The output and symbol file names default to the input name with `.hlp` and `.cs`. `--format v2` (the default) writes UTF-16 help; `--format v1` writes the Borland byte format, and `--encoding` names its code page. `--warn-as-error`, `--quiet` and `--no-color` control diagnostics. An unresolved cross-reference is a warning, and an error with `--warn-as-error`. A compile error or a missing input file exits with `1`. Both output files are written only when the compilation succeeds: after a failure, existing `.hlp` and `.cs` files are left unchanged and no partial file remains.

Open the result with `THelpFile`; see [help collections and search](views/help-search.md).
