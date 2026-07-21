using SDL3;
using TSharpVision.Diagnostics.SdlGlyphs;
using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Rendering.Fonts;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

// ─────────────────────────────────────────────────────────────────────────────
// TSharpVision.Diagnostics.SdlGlyphs
//
// Headless renderer for the generated CP437 B0-DF glyphs. Creates no window, no SDL_Renderer,
// no GPU device and no swapchain; SDL_ttf is initialised only to measure the requested fonts
// and to rasterise the comparison glyphs that fall outside B0-DF.
//
// Default behaviour: for each font, two runs are produced at automatically selected point sizes —
// one whose generated geometry has a 1 px single stroke and one with a 2 px single stroke.
// Passing --pt pins a single size instead.
//
// Every generated pixel comes from the production types in
// TSharpVision.Drivers.SDL.Rendering.GlyphComposition — this project contains no glyph geometry.
//
// Exit codes:  0 = every requested run was produced and all checks passed
//              1 = a check failed, or a requested run could not be produced
//              2 = usage or configuration error
// ─────────────────────────────────────────────────────────────────────────────

const int ExitOk     = 0;
const int ExitFailed = 1;
const int ExitUsage  = 2;

const int MaxFonts = 5;

string outputRoot   = Path.Combine(Directory.GetCurrentDirectory(), "diagnostics");
int?   explicitPt   = null;
const int minPointSize = StrokeSizeSelector.DefaultMinPointSize;
const int maxPointSize = StrokeSizeSelector.DefaultMaxPointSize;

// PhasedDither is the shading mode this project has chosen, so it is the default.
var modes    = new List<ShadingMode> { ShadingMode.PhasedDither };
var fontArgs = new List<string>();

// ── Command line ─────────────────────────────────────────────────────────────

for (int i = 0; i < args.Length; i++)
{
    string a = args[i];

    switch (a)
    {
        case "--out":
        case "-o":
            if (++i >= args.Length) return Usage("--out requires a directory.");
            outputRoot = Path.GetFullPath(args[i]);
            break;

        case "--pt":
        case "--size":
            if (++i >= args.Length) return Usage("--pt requires a point size.");
            if (!int.TryParse(args[i], out int pinned) || pinned <= 0)
                return Usage($"Invalid point size '{args[i]}'.");
            explicitPt = pinned;
            break;

        case "--shading":
            if (++i >= args.Length) return Usage("--shading requires dither|uniform|both.");
            switch (args[i].ToLowerInvariant())
            {
                case "dither":  modes = [ShadingMode.PhasedDither]; break;
                case "uniform": modes = [ShadingMode.Uniform]; break;
                case "both":    modes = [ShadingMode.PhasedDither, ShadingMode.Uniform]; break;
                default:        return Usage($"Unknown shading mode '{args[i]}'.");
            }
            break;

        case "--help":
        case "-h":
            PrintUsage();
            return ExitOk;

        default:
            if (a.StartsWith('-')) return Usage($"Unknown option '{a}'.");
            fontArgs.Add(a);
            break;
    }
}

if (fontArgs.Count == 0)
    return Usage("At least one font must be given.");

if (fontArgs.Count > MaxFonts)
    return Usage($"At most {MaxFonts} fonts may be given; got {fontArgs.Count}.");

// ── SDL_ttf (no window, no renderer, no GPU device) ──────────────────────────

SDL.Init((SDL.InitFlags)0);   // subsystem-less init; SDL_ttf brings up what it needs

if (!TTF.Init())
{
    Console.Error.WriteLine($"SDL_ttf could not be initialised: {SDL.GetError()}");
    return ExitUsage;
}

Directory.CreateDirectory(outputRoot);

Console.WriteLine($"Output    : {outputRoot}");
Console.WriteLine(explicitPt is int fixedPt
    ? $"Sizes     : explicit {fixedPt} pt (single run per font)"
    : $"Sizes     : automatic 1 px and 2 px stroke runs, searching {minPointSize}-{maxPointSize} pt");
Console.WriteLine($"Shading   : {string.Join(", ", modes)}");
Console.WriteLine();

var targets = new List<FontTarget>();

try
{
    for (int i = 0; i < fontArgs.Count; i++)
    {
        var target = new FontTarget
        {
            Index    = i + 1,
            Argument = fontArgs[i],
        };

        target.SearchMinPointSize = minPointSize;
        target.SearchMaxPointSize = maxPointSize;
        target.OutputDirectory    = Path.Combine(outputRoot, target.Index.ToString());
        Directory.CreateDirectory(target.OutputDirectory);

        targets.Add(target);
        ProcessFont(target, modes, explicitPt, minPointSize, maxPointSize);
    }

    File.WriteAllText(
        Path.Combine(outputRoot, "summary.txt"),
        Reports.BuildSummary(targets, modes, minPointSize, maxPointSize, explicitPt));
}
finally
{
    TTF.Quit();
    SDL.Quit();
}

int totalFailures   = targets.Sum(static t => t.CheckFailures);
int totalChecks     = targets.Sum(static t => t.ChecksRun);
int incompleteFonts = targets.Count(static t => !t.Complete);

Console.WriteLine();
Console.WriteLine(totalChecks == 0
    ? "No diagnostic checks were run."
    : totalFailures == 0
        ? $"All {totalChecks} diagnostic checks passed."
        : $"{totalFailures} of {totalChecks} diagnostic check(s) FAILED.");

if (incompleteFonts > 0)
    Console.WriteLine($"{incompleteFonts} font(s) did not produce a complete run set.");

return totalFailures == 0 && incompleteFonts == 0 ? ExitOk : ExitFailed;

// ─────────────────────────────────────────────────────────────────────────────

void ProcessFont(
    FontTarget target,
    IReadOnlyList<ShadingMode> shadingModes,
    int? pinnedPointSize,
    int  searchMin,
    int  searchMax)
{
    Console.WriteLine($"[{target.Index}] {target.Argument}");

    string? path = File.Exists(target.Argument)
        ? Path.GetFullPath(target.Argument)
        : SdlFontLocator.ProbeFontPathByName(target.Argument);

    target.ResolvedPath = path;

    if (path == null)
    {
        target.Status = FontStatus.NotFound;
        target.Error  = "no font file matched this name or path on this machine";
        Console.WriteLine($"      NOT FOUND — {target.Error}");
        File.WriteAllText(Path.Combine(target.OutputDirectory, "selection.txt"), Reports.BuildSelection(target));
        return;
    }

    // Probe once so an unopenable file is reported as such rather than as a failed search.
    IntPtr probe = TTF.OpenFont(path, pinnedPointSize ?? StrokeSizeSelector.PreferredPointSize);
    if (probe == IntPtr.Zero)
    {
        target.Status = FontStatus.OpenFailed;
        target.Error  = SDL.GetError();
        Console.WriteLine($"      OPEN FAILED — {target.Error}");
        File.WriteAllText(Path.Combine(target.OutputDirectory, "selection.txt"), Reports.BuildSelection(target));
        return;
    }
    TTF.CloseFont(probe);

    target.Status = FontStatus.Ok;
    Console.WriteLine($"      {path}");

    // ── Decide which runs to produce ──────────────────────────────────────────

    if (pinnedPointSize is int pt)
    {
        var run = new DiagnosticRun
        {
            Kind            = RunKind.Explicit,
            Label           = $"pt{pt}",
            TargetThickness = 0,
        };
        run.PointSize = pt;
        target.Runs.Add(run);
    }
    else
    {
        // Measure this font at a candidate size and hand the cell size to CellGlyphGeometry;
        // the geometry's own SingleStrokeThickness decides whether the size qualifies.
        bool Measure(int pointSize, out int cellWidth, out int cellHeight)
        {
            cellWidth = cellHeight = 0;

            IntPtr f = TTF.OpenFont(path, pointSize);
            if (f == IntPtr.Zero) return false;

            try
            {
                SdlFontMetrics.ComputeMetrics(f, out cellWidth, out cellHeight);
                return cellWidth > 0 && cellHeight > 0;
            }
            finally
            {
                TTF.CloseFont(f);
            }
        }

        foreach (StrokeRunPlan plan in StrokeRunPlanner.PlanAutomaticRuns(Measure, searchMin, searchMax))
        {
            RunKind kind = plan.Label == StrokeRunPlanner.OnePixelLabel
                ? RunKind.OnePixel
                : RunKind.TwoPixel;

            target.Runs.Add(MakeRun(kind, plan.Label, plan.TargetThickness, plan.Selection));
        }

        foreach (DiagnosticRun r in target.Runs)
        {
            Console.WriteLine(r.PointSize > 0
                ? $"      {r.Label}: {r.PointSize} pt  ({r.Selection!.Detail})"
                : $"      {r.Label}: NOT AVAILABLE — {r.Error}");
        }
    }

    // ── Produce each run ──────────────────────────────────────────────────────

    foreach (DiagnosticRun run in target.Runs)
    {
        if (run.PointSize <= 0)
            continue;   // selection already failed and was recorded

        run.Directory = Path.Combine(target.OutputDirectory, run.Label);
        Directory.CreateDirectory(run.Directory);
        ProduceRun(target, run, path, shadingModes);
    }

    File.WriteAllText(Path.Combine(target.OutputDirectory, "selection.txt"), Reports.BuildSelection(target));

    static DiagnosticRun MakeRun(RunKind kind, string label, int target_, StrokeSizeSelection selection)
    {
        var run = new DiagnosticRun
        {
            Kind            = kind,
            Label           = label,
            TargetThickness = target_,
        };

        run.Selection = selection;

        if (selection.Candidate is StrokeSizeCandidate c)
        {
            run.PointSize = c.PointSize;
        }
        else
        {
            run.Produced = false;
            run.Error    = selection.Detail;
        }

        return run;
    }
}

void ProduceRun(
    FontTarget target,
    DiagnosticRun run,
    string fontPath,
    IReadOnlyList<ShadingMode> shadingModes)
{
    IntPtr font = TTF.OpenFont(fontPath, run.PointSize);
    if (font == IntPtr.Zero)
    {
        run.Produced = false;
        run.Error    = $"SDL_ttf could not open the font at {run.PointSize} pt: {SDL.GetError()}";
        Console.WriteLine($"      {run.Label}: FAILED — {run.Error}");
        return;
    }

    try
    {
        run.Ascent     = TTF.GetFontAscent(font);
        run.Descent    = TTF.GetFontDescent(font);
        run.FontHeight = TTF.GetFontHeight(font);
        run.LineSkip   = TTF.GetFontLineSkip(font);
        run.AdvanceM   = Advance(font, 'M');
        run.AdvanceW   = Advance(font, 'W');
        run.Advance0   = Advance(font, '0');
        run.AdvanceX   = Advance(font, 'X');

        // The one and only cell-size computation, shared with both SDL back-ends.
        SdlFontMetrics.ComputeMetrics(font, out int cellWidth, out int cellHeight);
        run.CellWidth  = cellWidth;
        run.CellHeight = cellHeight;

        CellGlyphGeometry geometry = CellGlyphGeometry.FromCellSize(cellWidth, cellHeight);
        run.SingleStrokeThickness = geometry.SingleStrokeThickness;
        run.DoubleStrokeThickness = geometry.DoubleStrokeThickness;

        var rasterizer = new FontGlyphRasterizer(font, run.Ascent, cellWidth, cellHeight);

        var generators = new Dictionary<ShadingMode, Cp437GlyphGenerator>
        {
            [ShadingMode.PhasedDither] = new(geometry, Cp437GlyphGeneratorOptions.Default),
            [ShadingMode.Uniform]      = new(geometry, Cp437GlyphGeneratorOptions.UniformShading),
        };

        Cp437GlyphGenerator primary = generators[shadingModes[0]];

        File.WriteAllText(
            Path.Combine(run.Directory, "font-info.txt"),
            Reports.BuildFontInfo(target, run, geometry, primary, rasterizer, font));

        // ── Checks ───────────────────────────────────────────────────────────
        var byMode = new List<(ShadingMode, IReadOnlyList<GeneratedGlyphCheckResult>)>();
        foreach (ShadingMode mode in shadingModes)
            byMode.Add((mode, GeneratedGlyphChecks.RunAll(generators[mode])));

        string checksText = Reports.BuildChecks(target, run, byMode, out int failures);
        run.CheckFailures = failures;
        run.ChecksRun     = byMode.Sum(static m => m.Item2.Count);
        File.WriteAllText(Path.Combine(run.Directory, "checks.txt"), checksText);

        // ── ASCII preview ────────────────────────────────────────────────────
        foreach (ShadingMode mode in shadingModes)
            File.WriteAllText(
                Path.Combine(run.Directory, $"glyph-preview-{Suffix(mode)}.txt"),
                Reports.BuildGlyphPreview(generators[mode]));

        // ── BMP scenes ───────────────────────────────────────────────────────
        // Boxes and blocks contain no shading characters, so they are mode-independent and
        // written once, using the primary shading mode's generator.
        {
            var renderer = new SceneRenderer(new CellGlyphBitmapCache(primary), rasterizer);
            Write(run, "cp437-boxes.bmp",  renderer.Render(Scenes.Boxes()));
            Write(run, "cp437-blocks.bmp", renderer.Render(Scenes.Blocks()));
        }

        foreach (ShadingMode mode in shadingModes)
        {
            Cp437GlyphGenerator gen   = generators[mode];
            var                 cache = new CellGlyphBitmapCache(gen);
            var                 sr    = new SceneRenderer(cache, rasterizer);
            string              sfx   = Suffix(mode);

            Write(run, $"cp437-shading-{sfx}.bmp",       sr.Render(Scenes.Shading()));
            Write(run, $"cp437-button-shadow-{sfx}.bmp", sr.Render(Scenes.ButtonShadow()));

            // Scrollbars: the full render on top, the generated-glyphs-only render below, so it
            // is obvious which cells (▲ ▼ ◄ ► ■) still come from the font.
            ArgbBitmap full          = sr.Render(Scenes.Scrollbars());
            ArgbBitmap generatedOnly = sr.Render(Scenes.Scrollbars(), includeFontGlyphs: false);
            Write(run, $"cp437-scrollbars-{sfx}.bmp",
                SceneRenderer.StackVertically([full, generatedOnly], gap: 6, gapColor: 0xFF303030));

            Write(run, $"cp437-all-b0-df-{sfx}.bmp",       OverviewRenderer.Render(cache));
            Write(run, $"cp437-all-b0-df-tiled-{sfx}.bmp", sr.Render(Scenes.AllGraphicsTiled()));

            if (sr.Skipped.Count > 0)
                Console.WriteLine($"      {run.Label}: note — font could not render {string.Concat(sr.Skipped)}");
        }

        run.Produced = true;

        string met = run.TargetThickness == 0
            ? ""
            : run.MetTarget ? "  target met" : "  TARGET MISSED";

        Console.WriteLine(
            $"      {run.Label}: {run.PointSize} pt  cell {cellWidth}x{cellHeight}  " +
            $"stroke {run.SingleStrokeThickness}/{run.DoubleStrokeThickness} px  " +
            $"checks {run.ChecksRun - failures}/{run.ChecksRun}{met}");
    }
    finally
    {
        TTF.CloseFont(font);
    }
}

static void Write(DiagnosticRun run, string fileName, ArgbBitmap bitmap) =>
    ArgbBitmapBmpWriter.WriteToFile(bitmap, Path.Combine(run.Directory, fileName));

static string Suffix(ShadingMode mode) => mode == ShadingMode.Uniform ? "uniform" : "dither";

static int Advance(IntPtr font, char ch) =>
    TTF.GetGlyphMetrics(font, ch, out _, out _, out _, out _, out int advance) ? advance : 0;


int Usage(string message)
{
    Console.Error.WriteLine("error: " + message);
    Console.Error.WriteLine();
    PrintUsage();
    return ExitUsage;
}

void PrintUsage()
{
    Console.Error.WriteLine($"""
        TSharpVision.Diagnostics.SdlGlyphs — headless CP437 B0-DF glyph diagnostics

          TSharpVision.Diagnostics.SdlGlyphs [options] <font1> [font2] ... [font5]

        By default each font produces TWO runs at automatically selected point sizes:
          1px  the size closest to {StrokeSizeSelector.PreferredPointSize} pt whose generated geometry has a 1 px single stroke
               (ties resolve to the smaller size)
          2px  the smallest size in the search range whose geometry has a 2 px single stroke
        The criterion is CellGlyphGeometry.SingleStrokeThickness, evaluated from the font's own
        measured cell size — no point size is hard-coded per font.

        Options:
          --out, -o <dir>          Output directory (default: ./diagnostics)
          --pt, --size <n>         Pin one point size; produces a single run per font instead
          --shading <mode>         dither (default) | uniform | both
                                   uniform/both are a diagnostic comparison mode, not a
                                   rendering choice — the renderers always use dither
          --help, -h               This message

        Fonts may be file paths or names resolved with the production font locator.
        Between 1 and 5 fonts may be given; each gets its own numbered output directory.

        Exit codes: 0 = every run produced and all checks passed,
                    1 = a check failed or a run could not be produced,
                    2 = usage error.
        """);
}
