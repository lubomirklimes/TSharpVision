using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Diagnostics.SdlGlyphs;

/// <summary>How a requested font fared.</summary>
internal enum FontStatus
{
    /// <summary>Resolved and opened; at least one run was attempted.</summary>
    Ok,

    /// <summary>The name or path could not be resolved to a font file on this machine.</summary>
    NotFound,

    /// <summary>The file was found but SDL_ttf refused to open it.</summary>
    OpenFailed,
}

/// <summary>Which stroke-thickness target a run is for.</summary>
internal enum RunKind
{
    /// <summary>Automatically selected size whose geometry gives a 1 px single stroke.</summary>
    OnePixel,

    /// <summary>Automatically selected size whose geometry gives a 2 px single stroke.</summary>
    TwoPixel,

    /// <summary>A point size the user pinned with <c>--pt</c>; no thickness target.</summary>
    Explicit,
}

/// <summary>
/// One diagnostic run: a single font at a single point size, with everything measured for it and
/// the directory its artifacts were written to.
/// </summary>
internal sealed class DiagnosticRun
{
    public required RunKind Kind  { get; init; }

    /// <summary>Directory name and report label: <c>1px</c>, <c>2px</c>, or <c>pt&lt;n&gt;</c>.</summary>
    public required string Label { get; init; }

    /// <summary>Target <see cref="CellGlyphGeometry.SingleStrokeThickness"/>, or 0 for an explicit size.</summary>
    public required int TargetThickness { get; init; }

    /// <summary>Outcome of the automatic search; <c>null</c> for an explicit size.</summary>
    public StrokeSizeSelection? Selection { get; set; }

    public bool    Produced  { get; set; }
    public string? Error     { get; set; }
    public string  Directory { get; set; } = string.Empty;

    public int PointSize  { get; set; }
    public int CellWidth  { get; set; }
    public int CellHeight { get; set; }

    public int SingleStrokeThickness { get; set; }
    public int DoubleStrokeThickness { get; set; }

    public int Ascent     { get; set; }
    public int Descent    { get; set; }
    public int FontHeight { get; set; }
    public int LineSkip   { get; set; }

    public int AdvanceM { get; set; }
    public int AdvanceW { get; set; }
    public int Advance0 { get; set; }
    public int AdvanceX { get; set; }

    public int ChecksRun     { get; set; }
    public int CheckFailures { get; set; }

    /// <summary>True when the run was produced and its geometry hit the requested thickness.</summary>
    public bool MetTarget =>
        Produced && (TargetThickness == 0 || SingleStrokeThickness == TargetThickness);
}

/// <summary>
/// One font requested on the command line. Maps to one numbered output directory, which in the
/// default two-size mode contains a <c>1px</c> and a <c>2px</c> subdirectory.
/// </summary>
internal sealed class FontTarget
{
    public required int    Index    { get; init; }   // 1-based; also the output directory name
    public required string Argument { get; init; }   // exactly what the user typed

    public string?    ResolvedPath { get; set; }
    public FontStatus Status       { get; set; } = FontStatus.NotFound;
    public string?    Error        { get; set; }

    public string OutputDirectory { get; set; } = string.Empty;

    public List<DiagnosticRun> Runs { get; } = [];

    public int SearchMinPointSize { get; set; }
    public int SearchMaxPointSize { get; set; }

    /// <summary>True when every attempted run produced artifacts and met its thickness target.</summary>
    public bool Complete => Status == FontStatus.Ok && Runs.Count > 0 && Runs.All(static r => r.MetTarget);

    public int CheckFailures => Runs.Sum(static r => r.CheckFailures);
    public int ChecksRun     => Runs.Sum(static r => r.ChecksRun);
}
