namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// Lightweight aggregating runtime diagnostics for the SDL_GPU proof-of-concept.
///
/// Tracks per-frame timings with emphasis on swapchain acquire latency — the key
/// metric for investigating SDL_GPU vs SDL_Renderer responsiveness on macOS.
///
/// Enabled when TSHARPVISION_SDLGPU_DIAGNOSTICS=1 or verbose.
/// Output goes to stderr to avoid corrupting any terminal UI on stdout.
/// </summary>
internal sealed class SDLGpuPerformanceDiagnostics : IDisposable
{
    private static readonly TimeSpan NormalInterval  = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan VerboseInterval = TimeSpan.FromSeconds(5);

    private readonly Func<DateTime> _clock;
    private readonly TimeSpan       _reportInterval;
    private readonly TextWriter     _output;
    private readonly bool           _enabled;
    private readonly bool           _ownsWriter;

    private DateTime _windowStart;
    private bool     _firstFrameReported;

    // Frame timing
    private int  _frameCount;
    private long _frameTotalUs;
    private long _frameMaxUs;

    // Swapchain acquire — the key latency metric on macOS
    private long _acquireSwapTotalUs;
    private long _acquireSwapMaxUs;
    private int  _acquireSwapSkipped;   // frames where swapchain returned null texture

    // Render pass (begin + end)
    private long _renderPassTotalUs;
    private long _renderPassMaxUs;

    // Command buffer submit
    private long _submitTotalUs;
    private long _submitMaxUs;

    // Vertex build (GPU path: building BgVertex/GlyphVertex arrays in CPU staging buffer)
    private long _vertexBuildTotalUs;
    private long _vertexBuildMaxUs;

    // CPU compositing (CompositeScreen — building the RGBA pixel buffer; CPU fallback path only)
    private long _compositeTotalUs;
    private long _compositeMaxUs;

    // Render-thread wake latency (from SubmitFrame semaphore release to thread wakeup)
    private long _wakeTotalUs;
    private long _wakeMaxUs;

    internal SDLGpuPerformanceDiagnostics(SDLGpuOptions options)
        : this(options, null, null) { }

    internal SDLGpuPerformanceDiagnostics(
        SDLGpuOptions  options,
        Func<DateTime>? clock,
        TextWriter?     output)
    {
        _enabled        = options.DiagnosticsEnabled;
        _clock          = clock ?? (() => DateTime.UtcNow);
        _reportInterval = options.DiagnosticsVerbose ? VerboseInterval : NormalInterval;

        if (output != null)
        {
            _output     = output;
            _ownsWriter = false;
        }
        else if (_enabled)
        {
            // Write to a file — stderr is lost when the TUI takes over the terminal.
            string path = Path.Combine(Path.GetTempPath(), "sdlgpu_diag.log");
            _output     = new StreamWriter(path, append: false) { AutoFlush = true };
            _ownsWriter = true;
            Console.Error.WriteLine($"[SDLGpu] Diagnostics → {path}");
        }
        else
        {
            _output     = TextWriter.Null;
            _ownsWriter = false;
        }

        _windowStart = _clock();
    }

    public void Dispose()
    {
        if (_ownsWriter) _output.Dispose();
    }

    internal void RecordFrame(TimeSpan elapsed)
    {
        if (!_enabled) return;
        long us = ToUs(elapsed);
        _frameCount++;
        _frameTotalUs += us;
        if (us > _frameMaxUs) _frameMaxUs = us;
    }

    internal void RecordAcquireSwapchain(TimeSpan elapsed)
    {
        if (!_enabled) return;
        long us = ToUs(elapsed);
        _acquireSwapTotalUs += us;
        if (us > _acquireSwapMaxUs) _acquireSwapMaxUs = us;
    }

    internal void RecordSwapchainSkipped()
    {
        if (_enabled) _acquireSwapSkipped++;
    }

    internal void RecordRenderPass(TimeSpan elapsed)
    {
        if (!_enabled) return;
        long us = ToUs(elapsed);
        _renderPassTotalUs += us;
        if (us > _renderPassMaxUs) _renderPassMaxUs = us;
    }

    internal void RecordSubmit(TimeSpan elapsed)
    {
        if (!_enabled) return;
        long us = ToUs(elapsed);
        _submitTotalUs += us;
        if (us > _submitMaxUs) _submitMaxUs = us;
    }

    internal void RecordVertexBuild(TimeSpan elapsed)
    {
        if (!_enabled) return;
        long us = ToUs(elapsed);
        _vertexBuildTotalUs += us;
        if (us > _vertexBuildMaxUs) _vertexBuildMaxUs = us;
    }

    internal void RecordComposite(TimeSpan elapsed)
    {
        if (!_enabled) return;
        long us = ToUs(elapsed);
        _compositeTotalUs += us;
        if (us > _compositeMaxUs) _compositeMaxUs = us;
    }

    internal void RecordWakeLatency(TimeSpan elapsed)
    {
        if (!_enabled) return;
        long us = ToUs(elapsed);
        _wakeTotalUs += us;
        if (us > _wakeMaxUs) _wakeMaxUs = us;
    }

    internal void MaybeReportSummary()
    {
        if (!_enabled) return;

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
        double windowSec        = (_clock() - _windowStart).TotalSeconds;
        double avgFrameMs       = Avg(_frameTotalUs, _frameCount);
        double maxFrameMs       = _frameMaxUs / 1000.0;
        double acquireAvgMs     = Avg(_acquireSwapTotalUs, _frameCount);
        double acquireMaxMs     = _acquireSwapMaxUs / 1000.0;
        double renderPassAvgMs  = Avg(_renderPassTotalUs, _frameCount);
        double renderPassMaxMs  = _renderPassMaxUs / 1000.0;
        double submitAvgMs      = Avg(_submitTotalUs, _frameCount);
        double submitMaxMs      = _submitMaxUs / 1000.0;
        double vertexBuildAvgMs = Avg(_vertexBuildTotalUs, _frameCount);
        double vertexBuildMaxMs = _vertexBuildMaxUs / 1000.0;
        double compositeAvgMs   = Avg(_compositeTotalUs, _frameCount);
        double compositeMaxMs   = _compositeMaxUs / 1000.0;
        double wakeAvgMs        = Avg(_wakeTotalUs, _frameCount);
        double wakeMaxMs        = _wakeMaxUs / 1000.0;

        bool gpuPath = _vertexBuildTotalUs > 0;
        _output.WriteLine(
            $"[SDLGpu] perf ({(gpuPath ? "GPU-pipeline" : "CPU-fallback")}) " +
            $"window={windowSec:F1}s " +
            $"frames={_frameCount} skipped={_acquireSwapSkipped} " +
            $"frame.avg={avgFrameMs:F3}ms frame.max={maxFrameMs:F3}ms " +
            $"wake.avg={wakeAvgMs:F3}ms wake.max={wakeMaxMs:F3}ms " +
            (gpuPath
                ? $"vertexBuild.avg={vertexBuildAvgMs:F3}ms vertexBuild.max={vertexBuildMaxMs:F3}ms "
                : $"composite.avg={compositeAvgMs:F3}ms composite.max={compositeMaxMs:F3}ms ") +
            $"acquireSwap.avg={acquireAvgMs:F3}ms acquireSwap.max={acquireMaxMs:F3}ms " +
            $"renderPass.avg={renderPassAvgMs:F3}ms renderPass.max={renderPassMaxMs:F3}ms " +
            $"submit.avg={submitAvgMs:F3}ms submit.max={submitMaxMs:F3}ms");

        ResetWindow();
    }

    internal void ResetWindow()
    {
        _windowStart           = _clock();
        _frameCount            = 0;
        _frameTotalUs          = 0;
        _frameMaxUs            = 0;
        _acquireSwapTotalUs    = 0;
        _acquireSwapMaxUs      = 0;
        _acquireSwapSkipped    = 0;
        _renderPassTotalUs     = 0;
        _renderPassMaxUs       = 0;
        _submitTotalUs         = 0;
        _submitMaxUs           = 0;
        _vertexBuildTotalUs    = 0;
        _vertexBuildMaxUs      = 0;
        _compositeTotalUs      = 0;
        _compositeMaxUs        = 0;
        _wakeTotalUs           = 0;
        _wakeMaxUs             = 0;
    }

    // Accessors for unit tests
    internal bool Enabled              => _enabled;
    internal int  FrameCount           => _frameCount;
    internal long FrameTotalUs         => _frameTotalUs;
    internal long FrameMaxUs           => _frameMaxUs;
    internal long AcquireSwapTotalUs   => _acquireSwapTotalUs;
    internal long AcquireSwapMaxUs     => _acquireSwapMaxUs;
    internal int  AcquireSwapSkipped   => _acquireSwapSkipped;
    internal long SubmitTotalUs        => _submitTotalUs;
    internal long SubmitMaxUs          => _submitMaxUs;
    internal long VertexBuildTotalUs   => _vertexBuildTotalUs;
    internal long VertexBuildMaxUs     => _vertexBuildMaxUs;
    internal long CompositeTotalUs     => _compositeTotalUs;
    internal long CompositeMaxUs       => _compositeMaxUs;
    internal long WakeTotalUs          => _wakeTotalUs;
    internal long WakeMaxUs            => _wakeMaxUs;
    internal bool FirstFrameReported   => _firstFrameReported;

    private static double Avg(long totalUs, int count) =>
        count > 0 ? totalUs / (double)count / 1000.0 : 0.0;

    private static long ToUs(TimeSpan ts) => (long)ts.TotalMicroseconds;
}
