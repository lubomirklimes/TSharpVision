namespace TSharpVision.Drivers.SDL.Renderer;

/// <summary>
/// Lightweight aggregating runtime diagnostics for <see cref="TSharpVision.Drivers.SDL.SDLRenderer"/>.
///
/// Disabled by default. Enable via environment variable:
///   TSHARPVISION_SDL_DIAGNOSTICS=1       — enabled, 60-second summary interval
///   TSHARPVISION_SDL_DIAGNOSTICS=verbose — enabled, 5-second summary interval
///   (unset or any other value)           — disabled, no output
///
/// Output goes to stderr to avoid corrupting the terminal UI on stdout.
/// </summary>
internal sealed class SdlRendererPerformanceDiagnostics
{
    // ── Static configuration (read once from env at class init) ───────────────

    public static readonly bool Enabled;
    public static readonly bool Verbose;

    private static readonly TimeSpan NormalInterval  = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan VerboseInterval = TimeSpan.FromSeconds(5);

    static SdlRendererPerformanceDiagnostics()
    {
        string? val = Environment.GetEnvironmentVariable("TSHARPVISION_SDL_DIAGNOSTICS");
        Verbose = string.Equals(val, "verbose", StringComparison.OrdinalIgnoreCase);
        Enabled = val == "1" || Verbose;
    }

    // ── Instance state ────────────────────────────────────────────────────────

    private readonly Func<DateTime> _clock;
    private readonly TimeSpan       _reportInterval;
    private readonly TextWriter     _output;

    private DateTime _windowStart;
    private bool     _firstFrameReported;

    // Frame timing
    private int  _frameCount;
    private long _frameTotalUs;
    private long _frameMaxUs;

    // SDL_RenderPresent
    private long _presentTotalUs;
    private long _presentMaxUs;

    // SDL_SetRenderTarget (tracked for completeness)
    private int  _setRTCount;
    private long _setRTTotalUs;
    private long _setRTMaxUs;

    // SDL_CreateTexture (tracked for completeness)
    private int  _createTexCount;
    private long _createTexTotalUs;
    private long _createTexMaxUs;

    // SDL_CreateTextureFromSurface
    private int  _createFromSurfCount;
    private long _createFromSurfTotalUs;
    private long _createFromSurfMaxUs;

    // Normal glyph cache (Natural + FontCell paths)
    private int _normalCacheHits;
    private int _normalCacheMisses;
    private int _normalGlyphCreated;

    // Generated CP437 B0-DF glyph cache
    private int _generatedCacheHits;
    private int _generatedCacheMisses;
    private int _generatedGlyphCreated;

    // VSync — last known value (int.MinValue = not yet queried)
    private int _vsyncValue = int.MinValue;

    // Idle wait — number of WaitEventTimeout calls
    private int _idleWaitCount;

    // Motion coalescing (per reporting window)
    private int _motionReceived;
    private int _motionCoalesced;
    private int _motionDuringDrag;

    // Events-per-drain-cycle stats
    private int _drainCycles;
    private int _drainEventsTotal;
    private int _drainEventsMax;

    // Per-reason render counts (bitmask from SDLDriver.SdlDirtyReason)
    private const int ReasonInitial      = 1 << 0;
    private const int ReasonKey          = 1 << 1;
    private const int ReasonMouseButton  = 1 << 2;
    private const int ReasonMouseMotion  = 1 << 3;
    private const int ReasonWindowEvent  = 1 << 4;
    private const int ReasonWriteBuf     = 1 << 5;
    private const int ReasonCursorChange = 1 << 6;

    private int _renderReasonInitial;
    private int _renderReasonKey;
    private int _renderReasonMouseButton;
    private int _renderReasonMouseMotion;
    private int _renderReasonWindowEvent;
    private int _renderReasonWriteBuf;
    private int _renderReasonCursorChange;

    // ── Construction ──────────────────────────────────────────────────────────

    internal SdlRendererPerformanceDiagnostics()
        : this(null, null, null) { }

    /// <param name="clock">Clock function for testing; null uses DateTime.UtcNow.</param>
    /// <param name="reportIntervalOverride">Report interval override for testing.</param>
    /// <param name="output">Output writer for testing; null uses Console.Error.</param>
    internal SdlRendererPerformanceDiagnostics(
        Func<DateTime>? clock,
        TimeSpan?       reportIntervalOverride = null,
        TextWriter?     output = null)
    {
        _clock          = clock ?? (() => DateTime.UtcNow);
        _reportInterval = reportIntervalOverride ?? (Verbose ? VerboseInterval : NormalInterval);
        _output         = output ?? Console.Error;
        _windowStart    = _clock();
    }

    // ── Frame ────────────────────────────────────────────────────────────────

    public void RecordFrame(TimeSpan elapsed)
    {
        if (!Enabled) return;
        long us = ToMicroseconds(elapsed);
        _frameCount++;
        _frameTotalUs += us;
        if (us > _frameMaxUs) _frameMaxUs = us;
    }

    // ── SDL_RenderPresent ────────────────────────────────────────────────────

    public void RecordRenderPresent(TimeSpan elapsed)
    {
        if (!Enabled) return;
        long us = ToMicroseconds(elapsed);
        _presentTotalUs += us;
        if (us > _presentMaxUs) _presentMaxUs = us;
    }

    // ── SDL_SetRenderTarget ──────────────────────────────────────────────────

    public void RecordSetRenderTarget(TimeSpan elapsed)
    {
        if (!Enabled) return;
        _setRTCount++;
        long us = ToMicroseconds(elapsed);
        _setRTTotalUs += us;
        if (us > _setRTMaxUs) _setRTMaxUs = us;
    }

    // ── SDL_CreateTexture ────────────────────────────────────────────────────

    public void RecordCreateTexture(TimeSpan elapsed)
    {
        if (!Enabled) return;
        _createTexCount++;
        long us = ToMicroseconds(elapsed);
        _createTexTotalUs += us;
        if (us > _createTexMaxUs) _createTexMaxUs = us;
    }

    // ── SDL_CreateTextureFromSurface ─────────────────────────────────────────

    public void RecordCreateTextureFromSurface(TimeSpan elapsed)
    {
        if (!Enabled) return;
        _createFromSurfCount++;
        long us = ToMicroseconds(elapsed);
        _createFromSurfTotalUs += us;
        if (us > _createFromSurfMaxUs) _createFromSurfMaxUs = us;
    }

    // ── Cache ────────────────────────────────────────────────────────────────

    public void RecordNormalCacheHit()     { if (Enabled) _normalCacheHits++; }
    public void RecordNormalCacheMiss()    { if (Enabled) _normalCacheMisses++; }
    public void RecordNormalGlyphCreated() { if (Enabled) _normalGlyphCreated++; }
    public void RecordGeneratedCacheHit()        { if (Enabled) _generatedCacheHits++; }
    public void RecordGeneratedCacheMiss()       { if (Enabled) _generatedCacheMisses++; }
    public void RecordGeneratedGlyphCreated() { if (Enabled) _generatedGlyphCreated++; }

    // ── VSync ────────────────────────────────────────────────────────────────

    public void SetVSyncValue(int value) { _vsyncValue = value; }

    // ── Idle wait ────────────────────────────────────────────────────────────

    public void RecordIdleWait() { if (Enabled) _idleWaitCount++; }

    // ── Render reasons ───────────────────────────────────────────────────────

    public void RecordRenderWithReasons(int reasonMask)
    {
        if (!Enabled) return;
        if ((reasonMask & ReasonInitial)      != 0) _renderReasonInitial++;
        if ((reasonMask & ReasonKey)          != 0) _renderReasonKey++;
        if ((reasonMask & ReasonMouseButton)  != 0) _renderReasonMouseButton++;
        if ((reasonMask & ReasonMouseMotion)  != 0) _renderReasonMouseMotion++;
        if ((reasonMask & ReasonWindowEvent)  != 0) _renderReasonWindowEvent++;
        if ((reasonMask & ReasonWriteBuf)     != 0) _renderReasonWriteBuf++;
        if ((reasonMask & ReasonCursorChange) != 0) _renderReasonCursorChange++;
    }

    // ── Motion coalescing ────────────────────────────────────────────────────

    public void RecordDrainCycle(int events, int motionReceived, int motionCoalesced, int motionDuringDrag)
    {
        if (!Enabled) return;
        _drainCycles++;
        _drainEventsTotal  += events;
        if (events > _drainEventsMax) _drainEventsMax = events;
        _motionReceived    += motionReceived;
        _motionCoalesced   += motionCoalesced;
        _motionDuringDrag  += motionDuringDrag;
    }

    // ── Reporting ────────────────────────────────────────────────────────────

    public void MaybeReportSummary()
    {
        if (!Enabled) return;

        if (!_firstFrameReported)
        {
            _firstFrameReported = true;
            ReportSummary();
            return;
        }

        if (_clock() - _windowStart >= _reportInterval)
            ReportSummary();
    }

    internal void ReportSummary()
    {
        double windowSec      = (_clock() - _windowStart).TotalSeconds;
        double avgFrameMs     = _frameCount           > 0 ? _frameTotalUs           / (double)_frameCount           / 1000.0 : 0.0;
        double maxFrameMs     = _frameMaxUs            / 1000.0;
        double presentAvgMs   = _frameCount           > 0 ? _presentTotalUs         / (double)_frameCount           / 1000.0 : 0.0;
        double presentMaxMs   = _presentMaxUs          / 1000.0;
        double setRTAvgMs     = _setRTCount           > 0 ? _setRTTotalUs           / (double)_setRTCount           / 1000.0 : 0.0;
        double setRTMaxMs     = _setRTMaxUs            / 1000.0;
        double createTexAvgMs = _createTexCount       > 0 ? _createTexTotalUs       / (double)_createTexCount       / 1000.0 : 0.0;
        double createTexMaxMs = _createTexMaxUs        / 1000.0;
        double surfAvgMs      = _createFromSurfCount  > 0 ? _createFromSurfTotalUs  / (double)_createFromSurfCount  / 1000.0 : 0.0;
        double surfMaxMs      = _createFromSurfMaxUs   / 1000.0;

        string vsyncStr  = _vsyncValue == int.MinValue ? "?" : _vsyncValue.ToString();
        string vsyncEnv  = Environment.GetEnvironmentVariable("TSHARPVISION_SDL_VSYNC") ?? "(unset)";

        double avgEventsPerDrain = _drainCycles > 0
            ? (double)_drainEventsTotal / _drainCycles : 0.0;

        _output.WriteLine(
            $"[SDLRenderer] perf " +
            $"renderMode=dirty " +
            $"window={windowSec:F1}s " +
            $"renders={_frameCount} idleWaits={_idleWaitCount} " +
            $"avgFrame={avgFrameMs:F2}ms maxFrame={maxFrameMs:F2}ms " +
            $"present.avg={presentAvgMs:F2}ms present.max={presentMaxMs:F2}ms " +
            $"vsync={vsyncStr} vsync.env={vsyncEnv}");

        _output.WriteLine(
            $"[SDLRenderer] perf.reasons " +
            $"initial={_renderReasonInitial} " +
            $"key={_renderReasonKey} " +
            $"button={_renderReasonMouseButton} " +
            $"motion={_renderReasonMouseMotion} " +
            $"window={_renderReasonWindowEvent} " +
            $"writeBuf={_renderReasonWriteBuf} " +
            $"cursor={_renderReasonCursorChange}");

        _output.WriteLine(
            $"[SDLRenderer] perf.motion " +
            $"received={_motionReceived} " +
            $"coalesced={_motionCoalesced} " +
            $"duringDrag={_motionDuringDrag} " +
            $"drains={_drainCycles} " +
            $"eventsPerDrain.avg={avgEventsPerDrain:F1} " +
            $"eventsPerDrain.max={_drainEventsMax}");

        _output.WriteLine(
            $"[SDLRenderer] perf.textures " +
            $"setRT.count={_setRTCount} setRT.avg={setRTAvgMs:F2}ms setRT.max={setRTMaxMs:F2}ms " +
            $"createTex.count={_createTexCount} createTex.avg={createTexAvgMs:F2}ms createTex.max={createTexMaxMs:F2}ms " +
            $"createFromSurface.count={_createFromSurfCount} " +
            $"createFromSurface.avg={surfAvgMs:F2}ms createFromSurface.max={surfMaxMs:F2}ms " +
            $"normalCache hit/miss={_normalCacheHits}/{_normalCacheMisses} " +
            $"generatedCache hit/miss={_generatedCacheHits}/{_generatedCacheMisses}");

        ResetWindow();
    }

    internal void ResetWindow()
    {
        _windowStart           = _clock();
        _frameCount            = 0;
        _frameTotalUs          = 0;
        _frameMaxUs            = 0;
        _presentTotalUs        = 0;
        _presentMaxUs          = 0;
        _setRTCount            = 0;
        _setRTTotalUs          = 0;
        _setRTMaxUs            = 0;
        _createTexCount        = 0;
        _createTexTotalUs      = 0;
        _createTexMaxUs        = 0;
        _createFromSurfCount   = 0;
        _createFromSurfTotalUs = 0;
        _createFromSurfMaxUs   = 0;
        _normalCacheHits       = 0;
        _normalCacheMisses     = 0;
        _normalGlyphCreated    = 0;
        _generatedCacheHits          = 0;
        _generatedCacheMisses        = 0;
        _generatedGlyphCreated    = 0;
        _idleWaitCount         = 0;
        _motionReceived        = 0;
        _motionCoalesced       = 0;
        _motionDuringDrag      = 0;
        _drainCycles           = 0;
        _drainEventsTotal      = 0;
        _drainEventsMax        = 0;
        _renderReasonInitial      = 0;
        _renderReasonKey          = 0;
        _renderReasonMouseButton  = 0;
        _renderReasonMouseMotion  = 0;
        _renderReasonWindowEvent  = 0;
        _renderReasonWriteBuf     = 0;
        _renderReasonCursorChange = 0;
    }

    // ── Internal state accessors (for unit tests) ─────────────────────────────

    internal bool FirstFrameReported   => _firstFrameReported;
    internal int  FrameCount           => _frameCount;
    internal long FrameTotalUs         => _frameTotalUs;
    internal long FrameMaxUs           => _frameMaxUs;
    internal int  CreateFromSurfCount  => _createFromSurfCount;
    internal long CreateFromSurfMaxUs  => _createFromSurfMaxUs;
    internal int  NormalCacheHits      => _normalCacheHits;
    internal int  NormalCacheMisses    => _normalCacheMisses;
    internal int  GeneratedCacheHits         => _generatedCacheHits;
    internal int  GeneratedCacheMisses       => _generatedCacheMisses;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static long ToMicroseconds(TimeSpan ts) => (long)ts.TotalMicroseconds;
}
