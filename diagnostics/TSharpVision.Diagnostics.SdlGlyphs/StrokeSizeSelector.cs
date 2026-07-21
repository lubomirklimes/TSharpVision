using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Diagnostics.SdlGlyphs;

/// <summary>One candidate point size, together with everything it implies.</summary>
public readonly record struct StrokeSizeCandidate(
    int PointSize,
    int CellWidth,
    int CellHeight,
    int SingleStrokeThickness,
    int DoubleStrokeThickness)
{
    public override string ToString() =>
        $"{PointSize} pt -> cell {CellWidth}x{CellHeight}, " +
        $"single {SingleStrokeThickness} px, double {DoubleStrokeThickness} px";
}

/// <summary>Result of searching for a point size that yields a given stroke thickness.</summary>
/// <param name="TargetThickness">The <see cref="CellGlyphGeometry.SingleStrokeThickness"/> asked for.</param>
/// <param name="Candidate">The chosen size, or <c>null</c> when the search failed.</param>
/// <param name="Detail">Human-readable explanation, always populated.</param>
public sealed record StrokeSizeSelection(
    int                   TargetThickness,
    StrokeSizeCandidate?  Candidate,
    string                Detail)
{
    public bool Found => Candidate is not null;
}

/// <summary>
/// Chooses, per font, the point sizes whose <b>generated</b> cell geometry produces a given
/// single-stroke thickness.
/// <para>
/// The search is driven entirely by <see cref="CellGlyphGeometry.FromCellSize"/>: a candidate
/// point size is measured with SDL_ttf, the resulting cell size is fed to the geometry, and the
/// geometry's own <see cref="CellGlyphGeometry.SingleStrokeThickness"/> decides whether the size
/// qualifies. Nothing is hard-coded per font — a font whose advances differ will land on a
/// different point size, which is the point of the exercise.
/// </para>
/// <para>
/// Pure and headless: the caller supplies the measurement function, so this type can be tested
/// without SDL.
/// </para>
/// </summary>
public static class StrokeSizeSelector
{
    /// <summary>Smallest point size considered. Below this SDL_ttf metrics stop being useful.</summary>
    public const int DefaultMinPointSize = 6;

    /// <summary>
    /// Largest point size considered. 96 pt is far beyond any terminal use and is comfortably
    /// past the 3 px stroke threshold for every monospace face tried, so a failure to find a
    /// 2 px size inside this range is a real finding rather than a truncated search.
    /// </summary>
    public const int DefaultMaxPointSize = 96;

    /// <summary>
    /// The size the 1 px run prefers, matching <c>SDLRenderer.DefaultFontPtSize</c> so the 1 px
    /// diagnostics show what the driver would actually render today.
    /// </summary>
    public const int PreferredPointSize = 20;

    /// <summary>
    /// Measures the production cell size for one point size.
    /// Returns <c>false</c> when the font cannot be opened or measured at that size, in which
    /// case the size is skipped rather than failing the whole search.
    /// </summary>
    public delegate bool CellMeasurement(int pointSize, out int cellWidth, out int cellHeight);

    /// <summary>
    /// Finds the point size closest to <paramref name="preferred"/> whose geometry has
    /// <paramref name="targetThickness"/>. <paramref name="preferred"/> itself wins outright when
    /// it qualifies; otherwise the search fans out one point size at a time and, on a tie,
    /// prefers the <b>smaller</b> size.
    /// </summary>
    public static StrokeSizeSelection SelectClosestTo(
        int             targetThickness,
        int             preferred,
        CellMeasurement measure,
        int             minPointSize = DefaultMinPointSize,
        int             maxPointSize = DefaultMaxPointSize)
    {
        ArgumentNullException.ThrowIfNull(measure);
        ValidateRange(minPointSize, maxPointSize);

        // Distance 0 is the preferred size itself; then pt-1, pt+1, pt-2, pt+2, ... so a tie
        // always resolves to the smaller size.
        int span = Math.Max(preferred - minPointSize, maxPointSize - preferred);

        for (int distance = 0; distance <= span; distance++)
        {
            foreach (int pointSize in Neighbours(preferred, distance))
            {
                if (pointSize < minPointSize || pointSize > maxPointSize)
                    continue;

                if (TryEvaluate(pointSize, targetThickness, measure, out StrokeSizeCandidate candidate))
                {
                    string how = distance == 0
                        ? $"preferred {preferred} pt qualifies"
                        : $"closest qualifying size to {preferred} pt (distance {distance}" +
                          (pointSize < preferred ? ", smaller side preferred on ties)" : ")");

                    return new StrokeSizeSelection(targetThickness, candidate, $"{candidate}; {how}");
                }
            }
        }

        return NotFound(targetThickness, minPointSize, maxPointSize,
            $"no point size in {minPointSize}-{maxPointSize} pt yields a {targetThickness} px single stroke");
    }

    /// <summary>
    /// Finds the smallest point size in the range whose geometry has
    /// <paramref name="targetThickness"/>. Deterministic by construction.
    /// </summary>
    public static StrokeSizeSelection SelectSmallest(
        int             targetThickness,
        CellMeasurement measure,
        int             minPointSize = DefaultMinPointSize,
        int             maxPointSize = DefaultMaxPointSize)
    {
        ArgumentNullException.ThrowIfNull(measure);
        ValidateRange(minPointSize, maxPointSize);

        for (int pointSize = minPointSize; pointSize <= maxPointSize; pointSize++)
        {
            if (TryEvaluate(pointSize, targetThickness, measure, out StrokeSizeCandidate candidate))
                return new StrokeSizeSelection(
                    targetThickness, candidate,
                    $"{candidate}; smallest qualifying size in {minPointSize}-{maxPointSize} pt");
        }

        return NotFound(targetThickness, minPointSize, maxPointSize,
            $"no point size in {minPointSize}-{maxPointSize} pt yields a {targetThickness} px single stroke");
    }

    /// <summary>The 1 px run: closest to <see cref="PreferredPointSize"/>, ties go smaller.</summary>
    public static StrokeSizeSelection SelectOnePixelRun(
        CellMeasurement measure,
        int             minPointSize = DefaultMinPointSize,
        int             maxPointSize = DefaultMaxPointSize) =>
        SelectClosestTo(1, PreferredPointSize, measure, minPointSize, maxPointSize);

    /// <summary>The 2 px run: the smallest qualifying size, so the step up is as small as possible.</summary>
    public static StrokeSizeSelection SelectTwoPixelRun(
        CellMeasurement measure,
        int             minPointSize = DefaultMinPointSize,
        int             maxPointSize = DefaultMaxPointSize) =>
        SelectSmallest(2, measure, minPointSize, maxPointSize);

    // ─────────────────────────────────────────────────────────────────────────

    private static IEnumerable<int> Neighbours(int preferred, int distance)
    {
        if (distance == 0)
        {
            yield return preferred;
            yield break;
        }

        yield return preferred - distance;   // smaller side first: ties resolve downwards
        yield return preferred + distance;
    }

    private static bool TryEvaluate(
        int pointSize, int targetThickness, CellMeasurement measure,
        out StrokeSizeCandidate candidate)
    {
        candidate = default;

        if (!measure(pointSize, out int cellWidth, out int cellHeight))
            return false;   // unmeasurable at this size — skip, do not abort the search

        if (cellWidth <= 0 || cellHeight <= 0)
            return false;

        CellGlyphGeometry geometry = CellGlyphGeometry.FromCellSize(cellWidth, cellHeight);

        if (geometry.SingleStrokeThickness != targetThickness)
            return false;

        candidate = new StrokeSizeCandidate(
            pointSize,
            geometry.CellWidth,
            geometry.CellHeight,
            geometry.SingleStrokeThickness,
            geometry.DoubleStrokeThickness);

        return true;
    }

    private static StrokeSizeSelection NotFound(int target, int min, int max, string detail) =>
        new(target, null, detail);

    private static void ValidateRange(int min, int max)
    {
        if (min < 1)
            throw new ArgumentOutOfRangeException(nameof(min), min, "Minimum point size must be >= 1.");
        if (max < min)
            throw new ArgumentOutOfRangeException(nameof(max), max, "Maximum point size must be >= the minimum.");
    }
}

/// <summary>One planned run: the output sub-directory label, the thickness it targets, and the
/// search result that produced it.</summary>
public readonly record struct StrokeRunPlan(
    string              Label,
    int                 TargetThickness,
    StrokeSizeSelection Selection)
{
    /// <summary>True when a qualifying point size was found.</summary>
    public bool Available => Selection.Found;
}

/// <summary>
/// Turns a font's measurement function into the fixed set of runs the diagnostic produces:
/// one <c>1px</c> run and one <c>2px</c> run, in that order.
/// </summary>
public static class StrokeRunPlanner
{
    /// <summary>Output sub-directory for the 1 px stroke run.</summary>
    public const string OnePixelLabel = "1px";

    /// <summary>Output sub-directory for the 2 px stroke run.</summary>
    public const string TwoPixelLabel = "2px";

    /// <summary>
    /// Plans both automatic runs. Always returns two entries in a stable order, even when one of
    /// them could not be satisfied — an unavailable run is reported, never dropped and never
    /// silently replaced by a different size.
    /// </summary>
    public static IReadOnlyList<StrokeRunPlan> PlanAutomaticRuns(
        StrokeSizeSelector.CellMeasurement measure,
        int minPointSize = StrokeSizeSelector.DefaultMinPointSize,
        int maxPointSize = StrokeSizeSelector.DefaultMaxPointSize)
    {
        ArgumentNullException.ThrowIfNull(measure);

        return
        [
            new StrokeRunPlan(OnePixelLabel, 1,
                StrokeSizeSelector.SelectOnePixelRun(measure, minPointSize, maxPointSize)),
            new StrokeRunPlan(TwoPixelLabel, 2,
                StrokeSizeSelector.SelectTwoPixelRun(measure, minPointSize, maxPointSize)),
        ];
    }
}
