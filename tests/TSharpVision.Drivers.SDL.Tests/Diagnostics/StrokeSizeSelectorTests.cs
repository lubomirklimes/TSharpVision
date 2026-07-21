using TSharpVision.Diagnostics.SdlGlyphs;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Tests.Diagnostics;

/// <summary>
/// Covers the diagnostic tool's automatic point-size selection. The measurement function is
/// supplied by the caller, so these tests run without SDL, without a font and without a window.
/// </summary>
public class StrokeSizeSelectorTests
{
    /// <summary>
    /// A plausible monospace face: advance grows linearly with point size, cell height equals it.
    /// Consolas measures 11x20 at 20 pt, so 0.55 is close to real.
    /// </summary>
    private static StrokeSizeSelector.CellMeasurement LinearFont(double widthPerPoint = 0.55) =>
        (int pointSize, out int cellWidth, out int cellHeight) =>
        {
            cellWidth  = (int)Math.Round(pointSize * widthPerPoint);
            cellHeight = pointSize;
            return cellWidth > 0 && cellHeight > 0;
        };

    /// <summary>A measurement that only knows about the sizes in the table; others are unmeasurable.</summary>
    private static StrokeSizeSelector.CellMeasurement TableFont(IReadOnlyDictionary<int, (int W, int H)> table) =>
        (int pointSize, out int cellWidth, out int cellHeight) =>
        {
            if (table.TryGetValue(pointSize, out (int W, int H) cell))
            {
                cellWidth  = cell.W;
                cellHeight = cell.H;
                return true;
            }

            cellWidth = cellHeight = 0;
            return false;
        };

    private static int ThicknessOf(StrokeSizeCandidate c) =>
        CellGlyphGeometry.FromCellSize(c.CellWidth, c.CellHeight).SingleStrokeThickness;

    // ── Finding both runs ─────────────────────────────────────────────────────

    [Fact]
    public void PlanAutomaticRuns_FindsBothRunsForARealisticFont()
    {
        IReadOnlyList<StrokeRunPlan> plans = StrokeRunPlanner.PlanAutomaticRuns(LinearFont());

        Assert.Equal(2, plans.Count);
        Assert.Equal(StrokeRunPlanner.OnePixelLabel, plans[0].Label);
        Assert.Equal(StrokeRunPlanner.TwoPixelLabel, plans[1].Label);
        Assert.All(plans, p => Assert.True(p.Available, p.Selection.Detail));
    }

    [Fact]
    public void SelectedRuns_ActuallySatisfyTheirThicknessTarget()
    {
        foreach (double widthPerPoint in (double[])[0.45, 0.5, 0.55, 0.6, 0.7])
        {
            IReadOnlyList<StrokeRunPlan> plans =
                StrokeRunPlanner.PlanAutomaticRuns(LinearFont(widthPerPoint));

            foreach (StrokeRunPlan plan in plans)
            {
                Assert.True(plan.Available, $"{widthPerPoint}: {plan.Label} not found");

                StrokeSizeCandidate c = plan.Selection.Candidate!.Value;

                Assert.Equal(plan.TargetThickness, c.SingleStrokeThickness);
                Assert.Equal(plan.TargetThickness, ThicknessOf(c));
            }
        }
    }

    [Fact]
    public void SelectionIsDeterministic()
    {
        for (int i = 0; i < 3; i++)
        {
            IReadOnlyList<StrokeRunPlan> a = StrokeRunPlanner.PlanAutomaticRuns(LinearFont());
            IReadOnlyList<StrokeRunPlan> b = StrokeRunPlanner.PlanAutomaticRuns(LinearFont());

            Assert.Equal(a[0].Selection.Candidate, b[0].Selection.Candidate);
            Assert.Equal(a[1].Selection.Candidate, b[1].Selection.Candidate);
        }
    }

    // ── 1 px rules ────────────────────────────────────────────────────────────

    [Fact]
    public void OnePixelRun_PrefersTwentyPointWhenItQualifies()
    {
        StrokeSizeSelection s = StrokeSizeSelector.SelectOnePixelRun(LinearFont());

        Assert.True(s.Found);
        Assert.Equal(StrokeSizeSelector.PreferredPointSize, s.Candidate!.Value.PointSize);
        Assert.Contains("preferred", s.Detail);
    }

    [Fact]
    public void OnePixelRun_FallsBackToTheClosestQualifyingSize()
    {
        // Only 17 pt and 24 pt are measurable; 17 is closer to 20.
        var table = new Dictionary<int, (int, int)>
        {
            [17] = (11, 20),   // min 11 -> thickness 1
            [24] = (11, 20),
        };

        StrokeSizeSelection s = StrokeSizeSelector.SelectOnePixelRun(TableFont(table));

        Assert.True(s.Found);
        Assert.Equal(17, s.Candidate!.Value.PointSize);
    }

    [Fact]
    public void OnePixelRun_ResolvesATieTowardsTheSmallerSize()
    {
        // 15 pt and 25 pt are both 5 away from the preferred 20 pt.
        var table = new Dictionary<int, (int, int)>
        {
            [15] = (11, 18),
            [25] = (11, 30),
        };

        StrokeSizeSelection s = StrokeSizeSelector.SelectOnePixelRun(TableFont(table));

        Assert.True(s.Found);
        Assert.Equal(15, s.Candidate!.Value.PointSize);
        Assert.Contains("smaller side preferred", s.Detail);
    }

    // ── 2 px rules ────────────────────────────────────────────────────────────

    [Fact]
    public void TwoPixelRun_TakesTheSmallestQualifyingSize()
    {
        var table = new Dictionary<int, (int, int)>
        {
            [30] = (11, 30),   // thickness 1
            [40] = (24, 40),   // thickness 2  <- smallest qualifying
            [50] = (28, 50),   // thickness 2
            [60] = (30, 60),   // thickness 2
        };

        StrokeSizeSelection s = StrokeSizeSelector.SelectTwoPixelRun(TableFont(table));

        Assert.True(s.Found);
        Assert.Equal(40, s.Candidate!.Value.PointSize);
        Assert.Equal(2, s.Candidate!.Value.SingleStrokeThickness);
        Assert.Contains("smallest qualifying", s.Detail);
    }

    [Fact]
    public void TwoPixelRun_IsAlwaysAtLeastAsLargeAsTheOnePixelRun()
    {
        IReadOnlyList<StrokeRunPlan> plans = StrokeRunPlanner.PlanAutomaticRuns(LinearFont());

        int onePx = plans[0].Selection.Candidate!.Value.PointSize;
        int twoPx = plans[1].Selection.Candidate!.Value.PointSize;

        Assert.True(twoPx > onePx, $"expected the 2 px run ({twoPx} pt) to be larger than the 1 px run ({onePx} pt)");
    }

    // ── Failure handling ──────────────────────────────────────────────────────

    [Fact]
    public void MissingTarget_IsReportedRatherThanSubstituted()
    {
        // A face so narrow that the cell never reaches the 24 px minimum dimension that a 2 px
        // stroke needs, anywhere in the search range.
        StrokeSizeSelection s = StrokeSizeSelector.SelectTwoPixelRun(
            LinearFont(), minPointSize: 6, maxPointSize: 20);

        Assert.False(s.Found);
        Assert.Null(s.Candidate);
        Assert.Contains("no point size in 6-20 pt", s.Detail);
        Assert.Contains("2 px", s.Detail);
    }

    [Fact]
    public void PlanAutomaticRuns_StillReturnsBothEntriesWhenOneCannotBeSatisfied()
    {
        IReadOnlyList<StrokeRunPlan> plans =
            StrokeRunPlanner.PlanAutomaticRuns(LinearFont(), minPointSize: 6, maxPointSize: 20);

        Assert.Equal(2, plans.Count);
        Assert.True(plans[0].Available);            // 1 px is fine
        Assert.False(plans[1].Available);           // 2 px is not reachable
        Assert.Equal(StrokeRunPlanner.TwoPixelLabel, plans[1].Label);
        Assert.Equal(2, plans[1].TargetThickness);  // the target is preserved, not rewritten
    }

    [Fact]
    public void UnmeasurableSizes_AreSkippedNotFatal()
    {
        // Only one size in the whole range can be measured at all.
        var table = new Dictionary<int, (int, int)> { [31] = (11, 31) };

        StrokeSizeSelection s = StrokeSizeSelector.SelectOnePixelRun(TableFont(table));

        Assert.True(s.Found);
        Assert.Equal(31, s.Candidate!.Value.PointSize);
    }

    [Fact]
    public void NoMeasurableSize_ReportsFailure()
    {
        StrokeSizeSelection s = StrokeSizeSelector.SelectOnePixelRun(
            TableFont(new Dictionary<int, (int, int)>()));

        Assert.False(s.Found);
    }

    [Fact]
    public void ZeroSizedMeasurements_AreTreatedAsUnmeasurable()
    {
        var table = new Dictionary<int, (int, int)> { [20] = (0, 0), [21] = (11, 21) };

        StrokeSizeSelection s = StrokeSizeSelector.SelectOnePixelRun(TableFont(table));

        Assert.True(s.Found);
        Assert.Equal(21, s.Candidate!.Value.PointSize);
    }

    // ── Argument validation ───────────────────────────────────────────────────

    [Fact]
    public void InvalidRanges_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StrokeSizeSelector.SelectOnePixelRun(LinearFont(), minPointSize: 0, maxPointSize: 10));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StrokeSizeSelector.SelectOnePixelRun(LinearFont(), minPointSize: 30, maxPointSize: 10));

        Assert.Throws<ArgumentNullException>(() =>
            StrokeSizeSelector.SelectOnePixelRun(null!));
    }

    [Fact]
    public void SearchNeverLeavesTheRequestedRange()
    {
        var probed = new List<int>();

        StrokeSizeSelector.CellMeasurement recording =
            (int pointSize, out int cellWidth, out int cellHeight) =>
            {
                probed.Add(pointSize);
                cellWidth = cellHeight = 0;
                return false;
            };

        StrokeSizeSelector.SelectOnePixelRun(recording, minPointSize: 10, maxPointSize: 30);

        Assert.NotEmpty(probed);
        Assert.All(probed, pt => Assert.InRange(pt, 10, 30));
    }
}
