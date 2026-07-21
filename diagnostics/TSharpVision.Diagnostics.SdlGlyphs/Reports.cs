using System.Text;
using TSharpVision.Drivers.SDL.Rendering.Fonts;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Diagnostics.SdlGlyphs;

/// <summary>
/// Writes the text artifacts: <c>selection.txt</c> per font, and <c>font-info.txt</c>,
/// <c>checks.txt</c> and <c>glyph-preview-*.txt</c> per run.
/// </summary>
internal static class Reports
{
    /// <summary>Glyphs whose font rasterisation is worth comparing against the generated form.</summary>
    private static readonly char[] ProblemGlyphs = ['░', '▒', '▓', '█', '▀', '▄', '▌', '▐'];

    // ─────────────────────────────────────────────────────────────────────────
    // selection.txt  (one per font)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Explains how the point size for each run was chosen, so a reviewer can see that the
    /// thickness target was met by measurement rather than by a hard-coded size.
    /// </summary>
    public static string BuildSelection(FontTarget target)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== Stroke-size selection ===");
        sb.AppendLine($"index                : {target.Index}");
        sb.AppendLine($"font argument        : {target.Argument}");
        sb.AppendLine($"resolved font path   : {target.ResolvedPath ?? "(NOT FOUND)"}");
        sb.AppendLine($"font status          : {target.Status}");
        if (target.Error != null)
            sb.AppendLine($"font error           : {target.Error}");
        sb.AppendLine($"search range         : {target.SearchMinPointSize}-{target.SearchMaxPointSize} pt (inclusive)");
        sb.AppendLine($"preferred size (1px) : {StrokeSizeSelector.PreferredPointSize} pt");
        sb.AppendLine($"selection criterion  : CellGlyphGeometry.SingleStrokeThickness");
        sb.AppendLine();

        if (target.Runs.Count == 0)
        {
            sb.AppendLine("No runs were attempted for this font.");
            return sb.ToString();
        }

        foreach (DiagnosticRun run in target.Runs)
        {
            sb.AppendLine($"--- run '{run.Label}' ---");

            string rule = run.Kind switch
            {
                RunKind.OnePixel => $"closest point size to {StrokeSizeSelector.PreferredPointSize} pt with a 1 px single stroke (ties resolve to the smaller size)",
                RunKind.TwoPixel => "smallest point size in range with a 2 px single stroke",
                _                => "point size pinned with --pt (no automatic search)",
            };

            sb.AppendLine($"target thickness     : {(run.TargetThickness == 0 ? "(none)" : run.TargetThickness + " px")}");
            sb.AppendLine($"rule                 : {rule}");

            if (run.Selection != null)
                sb.AppendLine($"search result        : {run.Selection.Detail}");

            if (!run.Produced)
            {
                sb.AppendLine($"outcome              : NOT PRODUCED — {run.Error ?? "unknown reason"}");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine($"selected point size  : {run.PointSize} pt");
            sb.AppendLine($"measured cell size   : {run.CellWidth} x {run.CellHeight}");
            sb.AppendLine($"SingleStrokeThickness: {run.SingleStrokeThickness} px");
            sb.AppendLine($"DoubleStrokeThickness: {run.DoubleStrokeThickness} px");
            sb.AppendLine($"target met           : {(run.MetTarget ? "yes" : "NO")}");
            sb.AppendLine($"output directory     : {run.Label}/");
            sb.AppendLine();
        }

        var missing = target.Runs.Where(static r => !r.MetTarget).ToList();
        sb.AppendLine(missing.Count == 0
            ? "RESULT: every requested run was produced and met its thickness target."
            : "RESULT: INCOMPLETE — " + string.Join("; ",
                missing.Select(static r => $"'{r.Label}' {(r.Produced ? "did not meet its target" : "was not produced")}")));

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // font-info.txt  (one per run)
    // ─────────────────────────────────────────────────────────────────────────

    public static string BuildFontInfo(
        FontTarget           target,
        DiagnosticRun        run,
        CellGlyphGeometry    geometry,
        Cp437GlyphGenerator  generator,
        FontGlyphRasterizer? rasterizer,
        IntPtr               font)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== Font ===");
        sb.AppendLine($"index                   : {target.Index}");
        sb.AppendLine($"argument                : {target.Argument}");
        sb.AppendLine($"resolved path           : {target.ResolvedPath ?? "(NOT FOUND)"}");
        sb.AppendLine($"font status             : {target.Status}");
        sb.AppendLine();

        sb.AppendLine("=== Run ===");
        sb.AppendLine($"run                     : {run.Label}");
        sb.AppendLine($"target single stroke    : {(run.TargetThickness == 0 ? "(none — explicit --pt)" : run.TargetThickness + " px")}");
        sb.AppendLine($"selected point size     : {run.PointSize}");
        sb.AppendLine($"shading mode            : {generator.Options.ShadingMode}");
        sb.AppendLine();

        if (font == IntPtr.Zero)
        {
            sb.AppendLine("The font could not be opened at this size, so no metrics or glyphs were produced.");
            if (run.Error != null)
                sb.AppendLine($"reason: {run.Error}");
            return sb.ToString();
        }

        sb.AppendLine("=== SDL_ttf metrics ===");
        sb.AppendLine($"ascent                  : {run.Ascent}");
        sb.AppendLine($"descent                 : {run.Descent}");
        sb.AppendLine($"font height             : {run.FontHeight}");
        sb.AppendLine($"line skip               : {run.LineSkip}");
        sb.AppendLine($"advance 'M'             : {run.AdvanceM}");
        sb.AppendLine($"advance 'W'             : {run.AdvanceW}");
        sb.AppendLine($"advance '0'             : {run.Advance0}");
        sb.AppendLine($"advance 'X'             : {run.AdvanceX}");
        sb.AppendLine();

        sb.AppendLine("=== Production cell (SdlFontMetrics.ComputeMetrics) ===");
        sb.AppendLine($"cellWidth               : {run.CellWidth}");
        sb.AppendLine($"cellHeight              : {run.CellHeight}");
        sb.AppendLine();

        sb.AppendLine("=== Generated geometry (CellGlyphGeometry.FromCellSize) ===");
        sb.AppendLine($"SingleStrokeThickness   : {geometry.SingleStrokeThickness}   <-- run target: " +
                      $"{(run.TargetThickness == 0 ? "n/a" : run.TargetThickness + " px")}" +
                      $"{(run.TargetThickness != 0 ? (geometry.SingleStrokeThickness == run.TargetThickness ? "  (MET)" : "  (NOT MET)") : "")}");
        sb.AppendLine($"DoubleStrokeThickness   : {geometry.DoubleStrokeThickness}");
        sb.AppendLine($"single horizontal band  : rows {geometry.SingleBandY}..{geometry.SingleBandY + geometry.SingleStrokeThickness - 1}");
        sb.AppendLine($"single vertical band    : cols {geometry.SingleBandX}..{geometry.SingleBandX + geometry.SingleStrokeThickness - 1}");
        sb.AppendLine($"double horizontal bands : rows {geometry.DoubleBandY1}..{geometry.DoubleBandY1 + geometry.DoubleStrokeThickness - 1}" +
                      $" and {geometry.DoubleBandY2}..{geometry.DoubleBandY2 + geometry.DoubleStrokeThickness - 1}" +
                      $"  (supported={geometry.DoubleHorizontalSupported})");
        sb.AppendLine($"double vertical bands   : cols {geometry.DoubleBandX1}..{geometry.DoubleBandX1 + geometry.DoubleStrokeThickness - 1}" +
                      $" and {geometry.DoubleBandX2}..{geometry.DoubleBandX2 + geometry.DoubleStrokeThickness - 1}" +
                      $"  (supported={geometry.DoubleVerticalSupported})");
        sb.AppendLine($"upper/lower block split : {geometry.TopHeight} / {geometry.BottomHeight} rows  (▀ rows 0..{geometry.TopHeight - 1}, ▄ rows {geometry.TopHeight}..{geometry.CellHeight - 1})");
        sb.AppendLine($"left/right block split  : {geometry.LeftWidth} / {geometry.RightWidth} cols  (▌ cols 0..{geometry.LeftWidth - 1}, ▐ cols {geometry.LeftWidth}..{geometry.CellWidth - 1})");
        sb.AppendLine();

        sb.AppendLine("=== Uniform shading alphas (reference; this run uses " + generator.Options.ShadingMode + ") ===");
        sb.AppendLine($"░ B0                    : {Cp437ShadingLattice.UniformLightAlpha}");
        sb.AppendLine($"▒ B1                    : {Cp437ShadingLattice.UniformMediumAlpha}");
        sb.AppendLine($"▓ B2                    : {Cp437ShadingLattice.UniformDarkAlpha}");
        sb.AppendLine();

        sb.AppendLine("=== SDL_ttf rasterisation of the problem glyphs (what the generator replaces) ===");
        sb.AppendLine("  ch  CP  U+     minX maxX minY maxY  adv  surfW surfH   inkRows(cell)  inkCols(cell)");

        if (rasterizer == null)
        {
            sb.AppendLine("  (font rasterizer unavailable)");
        }
        else
        {
            foreach (char ch in ProblemGlyphs)
            {
                FontGlyphMeasurement m = rasterizer.Measure(ch);
                Cp437GraphicsCharacters.TryGet(ch, out Cp437GraphicsCharacter entry);

                string rows = m.CellInkTop  < 0 ? "   (none)" : $"{m.CellInkTop,3}..{m.CellInkBottom,-3}";
                string cols = m.CellInkLeft < 0 ? "   (none)" : $"{m.CellInkLeft,3}..{m.CellInkRight,-3}";

                sb.AppendLine(
                    $"  {ch}   {entry.Code:X2}  {(int)ch:X4}   {m.MinX,4} {m.MaxX,4} {m.MinY,4} {m.MaxY,4} {m.Advance,4}  " +
                    $"{m.SurfaceWidth,5} {m.SurfaceHeight,5}   {rows}      {cols}");
            }

            sb.AppendLine();
            sb.AppendLine($"  (cell is {run.CellWidth}x{run.CellHeight}; ink that stops short of row " +
                          $"{run.CellHeight - 1} or column {run.CellWidth - 1} is a seam in the current driver)");
        }

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // checks.txt  (one per run)
    // ─────────────────────────────────────────────────────────────────────────

    public static string BuildChecks(
        FontTarget    target,
        DiagnosticRun run,
        IReadOnlyList<(ShadingMode Mode, IReadOnlyList<GeneratedGlyphCheckResult> Results)> byMode,
        out int failures)
    {
        var sb = new StringBuilder();
        failures = 0;

        sb.AppendLine($"Font  : [{target.Index}] {target.Argument}  ->  {target.ResolvedPath ?? "(NOT FOUND)"}");
        sb.AppendLine($"Run   : {run.Label}  @ {run.PointSize} pt");
        sb.AppendLine($"Cell  : {run.CellWidth} x {run.CellHeight}   " +
                      $"single stroke {run.SingleStrokeThickness} px, double stroke {run.DoubleStrokeThickness} px");
        sb.AppendLine();

        foreach ((ShadingMode mode, IReadOnlyList<GeneratedGlyphCheckResult> results) in byMode)
        {
            sb.AppendLine($"--- shading mode: {mode} ---");
            foreach (GeneratedGlyphCheckResult r in results)
            {
                sb.AppendLine("  " + r);
                if (!r.Passed) failures++;
            }
            sb.AppendLine();
        }

        sb.AppendLine(failures == 0
            ? "RESULT: all checks passed"
            : $"RESULT: {failures} check(s) FAILED");

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // glyph-preview.txt  (one per run and shading mode)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An ASCII rendering of every generated glyph, so the geometry can be reviewed in a terminal
    /// without opening the BMPs. <c>#</c> = full ink, <c>+</c> = partial alpha, <c>.</c> = empty.
    /// </summary>
    public static string BuildGlyphPreview(Cp437GlyphGenerator generator)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Cell {generator.CellWidth} x {generator.CellHeight}   " +
                      $"shading={generator.Options.ShadingMode}   " +
                      $"single stroke={generator.Geometry.SingleStrokeThickness} px   " +
                      $"double stroke={generator.Geometry.DoubleStrokeThickness} px");
        sb.AppendLine("'#' = alpha 255, '+' = partial alpha, '.' = empty");
        sb.AppendLine();

        foreach (Cp437GraphicsCharacter entry in Cp437GraphicsCharacters.All)
        {
            sb.AppendLine($"--- {entry.Code:X2}  '{entry.Character}'  U+{(int)entry.Character:X4}  {entry.Name} ---");

            AlphaBitmap b = generator.Generate(entry.Character);
            AppendBitmap(sb, b);
            sb.AppendLine();
        }

        // Tiled 2x2 previews make the shading phase behaviour visible directly.
        var composer = new CellSceneComposer(generator);
        foreach (char ch in (char[])['░', '▒', '▓'])
        {
            sb.AppendLine($"--- 2x2 tiled '{ch}' (phase-correct placement) ---");
            AppendBitmap(sb, composer.ComposeFill(ch, 2, 2));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendBitmap(StringBuilder sb, AlphaBitmap b)
    {
        for (int y = 0; y < b.Height; y++)
        {
            sb.Append("  ");
            for (int x = 0; x < b.Width; x++)
            {
                byte a = b[x, y];
                sb.Append(a == 0 ? '.' : a == 255 ? '#' : '+');
            }
            sb.AppendLine();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // summary.txt
    // ─────────────────────────────────────────────────────────────────────────

    public static string BuildSummary(
        IReadOnlyList<FontTarget>   targets,
        IReadOnlyList<ShadingMode>  modes,
        int                         minPointSize,
        int                         maxPointSize,
        int?                        explicitPointSize)
    {
        var sb = new StringBuilder();

        sb.AppendLine("TSharpVision.Diagnostics.SdlGlyphs — summary");
        sb.AppendLine($"mode       : {(explicitPointSize is int pt ? $"explicit {pt} pt (single run per font)" : "automatic 1 px / 2 px stroke runs")}");
        if (explicitPointSize is null)
            sb.AppendLine($"search     : {minPointSize}-{maxPointSize} pt, criterion CellGlyphGeometry.SingleStrokeThickness");
        sb.AppendLine($"shading    : {string.Join(", ", modes)}");
        sb.AppendLine($"generated  : CP437 0x{Cp437GraphicsCharacters.FirstCode:X2}-0x{Cp437GraphicsCharacters.LastCode:X2} " +
                      $"({Cp437GraphicsCharacters.Count} characters)");
        sb.AppendLine();

        sb.AppendLine("dir  run   status       pt    cell      stroke      checks     font");
        sb.AppendLine("---  ----  -----------  ----  --------  ----------  ---------  ----------------------------------------");

        foreach (FontTarget t in targets)
        {
            if (t.Status != FontStatus.Ok || t.Runs.Count == 0)
            {
                sb.AppendLine(
                    $"{t.Index,-3}  {"-",-4}  {t.Status,-11}  {"-",-4}  {"-",-8}  {"-",-10}  {"-",-9}  " +
                    $"{t.Argument} -> {t.ResolvedPath ?? "(not found)"}");
                continue;
            }

            foreach (DiagnosticRun r in t.Runs)
            {
                string status = r.Produced ? (r.MetTarget ? "Ok" : "TargetMissed") : "NotProduced";
                string ptText = r.Produced ? r.PointSize.ToString() : "-";
                string cell   = r.Produced ? $"{r.CellWidth}x{r.CellHeight}" : "-";
                string stroke = r.Produced ? $"{r.SingleStrokeThickness}/{r.DoubleStrokeThickness} px" : "-";
                string checks = r.Produced ? $"{r.ChecksRun - r.CheckFailures}/{r.ChecksRun}" : "-";

                sb.AppendLine(
                    $"{t.Index,-3}  {r.Label,-4}  {status,-11}  {ptText,-4}  {cell,-8}  {stroke,-10}  {checks,-9}  " +
                    $"{t.Argument} -> {t.ResolvedPath ?? "(not found)"}");
            }
        }

        sb.AppendLine();

        foreach (FontTarget t in targets.Where(static t => t.Status != FontStatus.Ok))
            sb.AppendLine($"[{t.Index}] {t.Argument}: {t.Error}");

        foreach (FontTarget t in targets)
            foreach (DiagnosticRun r in t.Runs.Where(static r => !r.MetTarget))
                sb.AppendLine($"[{t.Index}] run '{r.Label}': {r.Error ?? r.Selection?.Detail ?? "target not met"}");

        int failures   = targets.Sum(static t => t.CheckFailures);
        int incomplete = targets.Count(static t => !t.Complete);

        sb.AppendLine();
        sb.AppendLine(failures == 0 && incomplete == 0
            ? "RESULT: all runs produced, all checks passed"
            : $"RESULT: {failures} check failure(s), {incomplete} font(s) with an incomplete run set");

        return sb.ToString();
    }
}
