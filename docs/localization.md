# Localization

TSharpVision draws its own user-interface text — button captions, message-box titles, file and directory dialogs, editor prompts, standard menu and status-line items — through one string provider, `TSharpVisionIntl.Current`. The `TSharpVision` package ships translations of that text for eight languages, and an application selects one with a single configuration line. Application-specific text uses the same mechanism through its own provider.

There is no `.resx` and no gettext: strings are keyed tables in TSharpVision's own `.tvr` resource format, written as `.trc` text and compiled by the `svrc` resource compiler (the `TSharpVision.ResourceCompiler` .NET tool; see [Command-line tools](tools.md)).

## Built-in languages

| Code | Language |
|---|---|
| `en` | English (built in as `DefaultEnglishStringProvider`; the fallback for every other language) |
| `cs` | Czech |
| `de` | German |
| `es` | Spanish |
| `fr` | French |
| `it` | Italian |
| `pl` | Polish |
| `sk` | Slovak |

Every translation covers every key of the English inventory (118 keys), with the same `~X~` hotkey markers and the same `{0}` placeholders. The test suite enforces this; see [Adding or changing a translation](#adding-or-changing-a-translation).

Strings that a component passes with an inline English fallback but that are not in the English inventory are not translated by the built-in tables; they show the English fallback in every language.

## Selecting the language in configuration

Put the language in the `[localization]` section of a configuration file:

```ini
[localization]
language=cs
```

The configuration files are the ones the runtime already reads: the user's `~/.tsvision.cfg` and `<AssemblyName>.cfg` beside the executable, which overrides the user file key by key (see [Drivers](drivers.md)). `TSharpVisionRuntime.Run` and `TSharpVisionRuntime.Configure` load them and apply the language before the application object is constructed, so menus, status lines and dialogs built in the constructor are already translated. No code is needed:

```csharp
using TSharpVision;

return TSharpVisionRuntime.Run(() => new MyApp());
```

The value is matched by its primary subtag, case-insensitively: `cs`, `CS`, `cs-CZ` and `cs_CZ.UTF-8` all select Czech. A missing or empty value, `en`, or a language without a translation keeps English; an unknown language also writes one warning line to standard error before the driver starts.

The language never comes from the operating system: `CultureInfo.CurrentUICulture`, `LANG` and the Windows display language are not consulted. An application that wants to follow the OS chooses that itself, for example with `TSharpVisionIntl.UseBuiltInLanguage(CultureInfo.CurrentUICulture.Name)` (see [Switching language at run time](#switching-language-at-run-time)).

The language is applied only by `TSharpVisionRuntime.Configure` (which `Run` calls). An application that constructs `TApplication` directly without calling `Configure` does not read the configuration files at all, for the language or for the driver. Assigning `ScreenDriverFactory.Configuration` in code does not apply a language either; call `Configure` first, as [Drivers](drivers.md) shows for overriding settings in code.

## Precedence

When `Configure` runs, the first rule that applies decides `TSharpVisionIntl.Current`:

1. **The application's provider wins.** If the application has assigned any provider to `TSharpVisionIntl.Current` — including a new `DefaultEnglishStringProvider`, which is an explicit choice of English, and any provider installed by `TSharpVisionIntl.UseBuiltInLanguage` — configuration leaves it alone. An application that assigns a provider after `Configure` (for example in its constructor) replaces the configured one in the ordinary way. Configuration never silently replaces an application's provider.
2. **Configured language.** Otherwise the configured language installs a provider chain, consulted in order:
   1. `tsharpvision_<lang>.tvr` in the application directory (`AppContext.BaseDirectory`), if present — an optional override, see below;
   2. the translation embedded in `TSharpVision.dll`;
   3. the built-in English provider.

   A key missing from a translation therefore shows in English, and a key unknown even to English shows the caller's inline fallback and raises `TSharpVisionIntl.MissingKey`.
3. **English.** With no usable language, `Current` stays (or returns to) the built-in English provider.

Calling `Configure` again re-evaluates the language: a provider that configuration installed earlier is replaced or removed, an application's provider still is not.

## Where the translations live

The source of each translation is `resources/localization/tsharpvision_<lang>.trc`. Beside it, `tsharpvision_<lang>.tvr` is its compiled form, produced by `svrc` and committed to the repository. `src/TSharpVision/TSharpVision.csproj` embeds the eight `.tvr` files in `TSharpVision.dll` as manifest resources named `TSharpVision.Localization.tsharpvision_<lang>.tvr`, and fails the build if a `.trc` has no `.tvr`.

Why embedded, and why committed:

- **Deployment.** The package layout stays `lib/net10.0/TSharpVision.dll`; there is no content folder, no build-transitive copy step and no file probing. Translations reach every consumer — direct or through a package that depends on `TSharpVision` — on Windows, Linux and macOS, and survive `dotnet publish`, single-file publishing and copying the output folder elsewhere. The cost is about 55 KB in the assembly.
- **Build ordering.** `svrc` references `TSharpVision` (it writes `.tvr` files through TSharpVision's stream classes), so compiling the `.trc` files while building `TSharpVision` itself would be a project cycle. The committed `.tvr` files break the cycle, and you regenerate them with an explicit build target (see [Adding or changing a translation](#adding-or-changing-a-translation)), much as the help files committed for TVDemo are recompiled by hand. `svrc` output is deterministic, and `TSharpVision.ResourceCompiler.Tests` recompiles every `.trc` on each test run and fails if a committed `.tvr` differs. A clean clone builds in any configuration without running the compiler first.

## Overriding strings for one application

To change some framework strings for one application without rebuilding TSharpVision — or to add a language that TSharpVision does not ship — place `tsharpvision_<lang>.tvr` next to the executable. Its keys take precedence over the embedded translation; keys it does not contain fall through to the embedded translation and then to English. Write it as a `.trc` file with the `sharpvision.intl` string table:

```text
resource strings "sharpvision.intl" {
  Btn_Cancel = "~S~torno";
}
```

and compile it with `svrc compile tsharpvision_cs.trc tsharpvision_cs.tvr`. Copy it to the output in the project file:

```xml
<ItemGroup>
  <None Include="tsharpvision_cs.tvr" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
</ItemGroup>
```

The file is opened read-only and must be a plain `svrc` output (resource header at offset 0). An unreadable or invalid file is ignored.

## Switching language at run time

To change the framework language from code — a language menu, a per-user setting, a command-line option — call `TSharpVisionIntl.UseBuiltInLanguage`:

```csharp
using TSharpVision;

if (!TSharpVisionIntl.UseBuiltInLanguage("de"))
{
    // TSharpVision has no such translation; TSharpVisionIntl.Current is unchanged.
}
```

`TSharpVisionIntl.BuiltInLanguages` lists the codes it accepts (`cs`, `de`, `en`, `es`, `fr`, `it`, `pl`, `sk`). The translations come from `TSharpVision.dll` itself, so the application ships no `.tvr` files for them.

| Question | Behavior |
|---|---|
| Language matching | Same as configuration: primary subtag, case-insensitive, so `cs`, `CS`, `cs-CZ` and `cs_CZ.UTF-8` select Czech. `CultureInfo` and the OS language are never consulted. |
| Provider installed | The same chain configuration installs: optional `tsharpvision_<lang>.tvr` in `AppContext.BaseDirectory` (see [Overriding strings](#overriding-strings-for-one-application)), the embedded translation, then English. A key missing from the translation shows in English. |
| Current provider | Replaced, whatever it is — an application provider, a configuration-installed one or English — because the application asked for it. Configuration, in contrast, never replaces an application's provider. |
| Later `TSharpVisionRuntime.Configure` | Treats the runtime choice as the application's provider and leaves it alone (rule 1 of [Precedence](#precedence)). |
| `en` | Installs English as an explicit choice (a new chain over the built-in English provider, with an optional `tsharpvision_en.tvr` override). Like an app-assigned `DefaultEnglishStringProvider`, a later `Configure` does not replace it. |
| Unknown, blank or malformed language | Returns `false`, leaves `Current` unchanged and writes nothing. The application decides what to do; configuration, which has no caller to report to, falls back to English with a warning instead. |
| Views already on screen | Not re-translated. Views copy their text when they are constructed, so open dialogs, windows, menu bars and status lines keep their captions; text looked up later (new dialogs, message boxes, rebuilt menus) uses the new language. Rebuild the menu bar and status line yourself, and reopen dialogs, if they must change. |
| Threading | `TSharpVisionIntl.Current` is a process-wide static. Call this on the UI thread, before the event loop starts or between events, never concurrently with drawing. |

### Combining with application strings

An application's own strings use the same keys-and-fallback calls as the framework:

```csharp
using static TSharpVision.TInternationalizationExtensions;

string title = Loc("MyApp_Title", "My Application");
```

To translate them, put the application's table in front of a framework translation with `TSharpVisionStringProviderChain`. `TSharpVisionIntl.CreateBuiltInProvider` returns the same provider `UseBuiltInLanguage` would install, without installing it, or `null` for an unknown language:

```csharp
using TSharpVision;

string dir = AppContext.BaseDirectory;
var appStrings = TResourceStringProvider.TryLoad(
    LocalizedResourceResolver.Resolve(Path.Combine(dir, "app.tvr"), ".tvr", "de"));   // app_de.tvr, else app_en.tvr, else app.tvr

TSharpVisionIntl.Current = new TSharpVisionStringProviderChain(
    appStrings,
    TSharpVisionIntl.CreateBuiltInProvider("de") ?? new DefaultEnglishStringProvider());
```

`TSharpVisionStringProviderChain` skips `null` entries, so a missing file simply drops out of the chain. Because an assigned provider always wins over configuration, the application then decides the framework strings too.

The TVIntl sample does exactly this. It starts in the configured language and switches among all eight languages (and a pseudo-translation) from its Language menu, then rebuilds the menu bar and status line; windows already open keep their text until they are reopened. It ships its own `app_*.tvr` and help files, but no framework `.tvr` files.

Before `CreateBuiltInProvider` and `UseBuiltInLanguage` existed, switching at run time meant copying the framework `.tvr` files next to the executable and loading them:

```csharp
// Before: ship tsharpvision_<lang>.tvr next to the app and build the chain yourself.
var framework = TResourceStringProvider.TryLoad(
    Path.Combine(AppContext.BaseDirectory, "tsharpvision_" + language + ".tvr"));
TSharpVisionIntl.Current = new TSharpVisionStringProviderChain(
    framework, new DefaultEnglishStringProvider());

// Now: the embedded translation, no files to ship.
if (!TSharpVisionIntl.UseBuiltInLanguage(language))
    throw new ArgumentException($"TSharpVision has no '{language}' translation.", nameof(language));
```

That older code still works, and a file it loads still behaves as before.

## Adding or changing a translation

1. Edit or add `resources/localization/tsharpvision_<lang>.trc`. Keep every key of `DefaultEnglishStringProvider` (`src/TSharpVision/System/TSharpVisionIntl.cs`), exactly one `~X~` hotkey marker wherever the English text has one, every `{0}`-style placeholder of the English text, and the leading line break of `Help_NoContext`. Do not translate a key you cannot translate reliably; leave it out and record it in `KnownMissingKeys` in `tests/TSharpVision.Tests/Localization/TranslationParityTests.cs`, so it falls back to English visibly rather than silently.
2. Recompile the committed images from the repository root. This works the same on Windows, Linux and macOS:

   ```text
   dotnet build tools/TSharpVision.ResourceCompiler -c Release -t:CompileFrameworkLocalization
   ```

   The `CompileFrameworkLocalization` target builds `svrc` and compiles every `resources/localization/tsharpvision_*.trc`, the same way the sample projects compile their help with an `svhc` step. Each source is compiled into the project's `obj` folder first. The `.tvr` files in `resources/localization` are replaced only when every source compiled. A duplicate key or a malformed entry fails the build with the compiler's `TRC` diagnostics and leaves the committed files untouched. A new `.trc` that has no `.tvr` yet gets one: while the target builds `svrc`, the library skips its missing-translation check. The output is deterministic, so an unchanged `.trc` produces the same bytes.

   To compile into another folder, for example to compare with the committed files without touching them, add `-p:FrameworkLocalizationOutputDir=<folder>`. A single file can still be compiled directly with `dotnet run --project tools/TSharpVision.ResourceCompiler -c Release -- compile <input.trc> <output.tvr>`.

   The tests never write these files. They fail when a committed `.tvr` differs from a fresh compile and name the command above.

3. For a new language, also add its code to `TSharpVisionLocalization.BuiltInLanguages` (`src/TSharpVision/System/TSharpVisionLocalization.cs`) and to the language lists in `LocalizationResourceTests` and `TranslationParityTests`.
4. Run the checks:

   ```text
   dotnet test tests/TSharpVision.ResourceCompiler.Tests --filter FullyQualifiedName~LocalizationResourceTests
   dotnet test tests/TSharpVision.Tests --filter FullyQualifiedName~TSharpVision.Tests.Localization
   ```

   They verify that every `.trc` compiles without diagnostics (duplicate and malformed entries are compiler errors), that each committed `.tvr` is byte-identical to a fresh compile, that the assembly embeds exactly the expected languages, that each translation has the English key set apart from documented gaps, with matching hotkey markers and placeholders, and that the embedded English table equals `DefaultEnglishStringProvider`.

A new framework string needs a key in `DefaultEnglishStringProvider` and in every `.trc`; until a translator supplies it, record it as a known gap for each language that lacks it.

## Checking a NuGet consumer

An opt-in test packs `TSharpVision` into a private temporary feed, packs a library that depends on it, publishes a console application that references only that library, and runs the published application from outside the repository with `language=cs`, `language=de` and no configuration. The application also lists `TSharpVisionIntl.BuiltInLanguages` and switches to Polish with `TSharpVisionIntl.UseBuiltInLanguage`:

```text
TSV_RUN_PACKAGE_CONSUMER_TESTS=1 dotnet test tests/TSharpVision.Tests --filter FullyQualifiedName~PackageConsumerLocalizationTests
```

The consumer needs nothing beyond the package reference and its `.cfg` file:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <!-- The first TSharpVision version that embeds the translations, or later. -->
    <TSharpVisionVersion>...</TSharpVisionVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TSharpVision" Version="$(TSharpVisionVersion)" />
    <PackageReference Include="TSharpVision.Drivers.Terminal" Version="$(TSharpVisionVersion)" />
    <None Update="MyApp.cfg" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

with `MyApp.cfg` (named after the assembly) containing `[localization]` and `language=cs`. Package versions without the built-in translations read the setting but ignore it.
