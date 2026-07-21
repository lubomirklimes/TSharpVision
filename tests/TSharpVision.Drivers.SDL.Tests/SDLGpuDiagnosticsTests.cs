using System.Text;
using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class SDLGpuDiagnosticsTests
{
    private static SDLGpuOptions MakeOptions(bool enabled = true, bool verbose = false) =>
        new()
        {
            DiagnosticsEnabled    = enabled,
            DiagnosticsVerbose    = verbose,
            Continuous            = false,
            Backend               = null,
            AllowedFramesInFlight = null,
            PresentMode           = null,
        };

    private static (SDLGpuPerformanceDiagnostics diag, StringBuilder output, List<DateTime> ticks)
        Make(bool enabled = true, bool verbose = false)
    {
        var output = new StringBuilder();
        var ticks  = new List<DateTime> { DateTime.UtcNow };
        int idx    = 0;
        Func<DateTime> clock = () => idx < ticks.Count ? ticks[idx++] : ticks[^1];

        var diag = new SDLGpuPerformanceDiagnostics(
            MakeOptions(enabled, verbose),
            clock,
            new StringWriter(output));

        return (diag, output, ticks);
    }

    // ── Disabled diagnostics ─────────────────────────────────────────────────

    [Fact]
    public void Disabled_RecordFrame_NoAccumulation()
    {
        var (diag, _, _) = Make(enabled: false);
        diag.RecordFrame(TimeSpan.FromMilliseconds(5));
        Assert.Equal(0, diag.FrameCount);
    }

    [Fact]
    public void Disabled_MaybeReportSummary_NoOutput()
    {
        var (diag, output, ticks) = Make(enabled: false);
        ticks.Add(ticks[0].AddMinutes(2));  // past report interval
        diag.RecordFrame(TimeSpan.FromMilliseconds(1));
        diag.MaybeReportSummary();
        Assert.Empty(output.ToString());
    }

    // ── Enabled diagnostics ──────────────────────────────────────────────────

    [Fact]
    public void Enabled_RecordFrame_Accumulates()
    {
        var (diag, _, _) = Make();
        diag.RecordFrame(TimeSpan.FromMilliseconds(2));
        diag.RecordFrame(TimeSpan.FromMilliseconds(4));
        Assert.Equal(2, diag.FrameCount);
        Assert.Equal(6000, diag.FrameTotalUs);
        Assert.Equal(4000, diag.FrameMaxUs);
    }

    [Fact]
    public void Enabled_RecordAcquireSwapchain_Accumulates()
    {
        var (diag, _, _) = Make();
        diag.RecordAcquireSwapchain(TimeSpan.FromMilliseconds(0.5));
        diag.RecordAcquireSwapchain(TimeSpan.FromMilliseconds(1.5));
        Assert.Equal(2000, diag.AcquireSwapTotalUs);
        Assert.Equal(1500, diag.AcquireSwapMaxUs);
    }

    [Fact]
    public void Enabled_RecordSwapchainSkipped_Increments()
    {
        var (diag, _, _) = Make();
        diag.RecordSwapchainSkipped();
        diag.RecordSwapchainSkipped();
        Assert.Equal(2, diag.AcquireSwapSkipped);
    }

    [Fact]
    public void Enabled_RecordSubmit_Accumulates()
    {
        var (diag, _, _) = Make();
        diag.RecordSubmit(TimeSpan.FromMilliseconds(0.1));
        diag.RecordSubmit(TimeSpan.FromMilliseconds(0.3));
        Assert.Equal(400, diag.SubmitTotalUs);
        Assert.Equal(300, diag.SubmitMaxUs);
    }

    // ── MaybeReportSummary ───────────────────────────────────────────────────

    [Fact]
    public void MaybeReportSummary_FirstCall_WritesOutput()
    {
        var (diag, output, _) = Make();
        diag.RecordFrame(TimeSpan.FromMilliseconds(1));
        diag.MaybeReportSummary();
        Assert.Contains("[SDLGpu] perf", output.ToString());
        Assert.True(diag.FirstFrameReported);
    }

    [Fact]
    public void MaybeReportSummary_BeforeInterval_NoRepeat()
    {
        var (diag, output, ticks) = Make();
        // First call always writes
        diag.MaybeReportSummary();
        output.Clear();
        // Second call within the interval window — no output
        ticks.Add(ticks[0].AddSeconds(5));
        diag.MaybeReportSummary();
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void ResetWindow_ClearsAllCounters()
    {
        var (diag, _, _) = Make();
        diag.RecordFrame(TimeSpan.FromMilliseconds(5));
        diag.RecordAcquireSwapchain(TimeSpan.FromMilliseconds(1));
        diag.RecordSubmit(TimeSpan.FromMilliseconds(0.2));
        diag.RecordSwapchainSkipped();

        diag.ResetWindow();

        Assert.Equal(0, diag.FrameCount);
        Assert.Equal(0, diag.AcquireSwapTotalUs);
        Assert.Equal(0, diag.SubmitTotalUs);
        Assert.Equal(0, diag.AcquireSwapSkipped);
    }

    // ── ReportSummary output format ──────────────────────────────────────────

    [Fact]
    public void ReportSummary_ContainsKeyFields()
    {
        var (diag, output, _) = Make();
        diag.RecordFrame(TimeSpan.FromMilliseconds(2));
        diag.RecordAcquireSwapchain(TimeSpan.FromMilliseconds(0.3));
        diag.RecordSubmit(TimeSpan.FromMilliseconds(0.1));
        diag.ReportSummary();

        string text = output.ToString();
        Assert.Contains("frames=",           text);
        Assert.Contains("acquireSwap.avg=",  text);
        Assert.Contains("submit.avg=",       text);
        Assert.Contains("renderPass.avg=",   text);
    }
}
