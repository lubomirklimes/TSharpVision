using TSharpVision;
using TSharpVision.Drivers;
using Xunit;

namespace TSharpVision.Tests.Drivers;

/// <summary>TV13s: startup fallback policy of ScreenDriverFactory, with fake drivers (no native code).</summary>
public sealed class DriverStartupFallbackTests
{
    private sealed class FakeDriver(string name, Func<Exception?>? failure = null) : IDriver, IDisposable
    {
        private readonly NullDriver _inner = new();
        public string Name { get; } = name;
        public int Initialized, Disposed;

        public void Initialize()
        {
            Initialized++;
            if (failure?.Invoke() is { } ex) throw ex;
        }

        public void Dispose() => Disposed++;
        public void Suspend() { }
        public void Resume() { }
        public void Shutdown() { }
        public ushort GetCols() => 80;
        public ushort GetRows() => 25;
        public TDisplay.SM GetScreenMode() => TDisplay.SM.CO80;
        public void SetScreenMode(TDisplay.SM mode) { }
        public ScreenBuffer AllocateScreenBuffer() => _inner.AllocateScreenBuffer();
        public void ClearScreen(ushort cols, ushort rows) { }
        public ushort GetCursorType() => 0;
        public void SetCursorType(ushort cursorType) { }
        public void SetCaretPosition(int x, int y) { }
        public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf) { }
        public void MakeBeep() { }
        public void PumpMessages() { }
        public bool ReadKeyEvent(out TEvent ev) { ev = default; return false; }
        public bool SupportsMouse => false;
        public bool SupportsTrueColor => false;
        public bool SupportsGraphics => true;
    }

    private static DriverStartupFallbackException GpuUnavailable() =>
        new("SDLGpuDriver initialization failed: [SDLGpu] SDL_CreateGPUDevice failed: no device", "SDLDriver",
            new InvalidOperationException("[SDLGpu] SDL_CreateGPUDevice failed: no device"));

    private sealed class FallbackFactory(Func<string, FakeDriver?> create)
    {
        public readonly List<string> Requested = [];
        public FakeDriver? Created;
        public IDriver? Create(string name)
        {
            Requested.Add(name);
            return Created = create(name);
        }
    }

    // Case 1
    [Fact]
    public void ASuccessfulDriverIsKeptAndNoFallbackIsCreated()
    {
        var gpu = new FakeDriver("gpu");
        var factory = new FallbackFactory(_ => new FakeDriver("sdl"));
        var log = new StringWriter();
        Assert.Same(gpu, ScreenDriverFactory.InitializeWithFallback(gpu, allowFallback: true, factory.Create, log));
        Assert.Equal((1, 0), (gpu.Initialized, gpu.Disposed));
        Assert.Empty(factory.Requested);
        Assert.Equal(string.Empty, log.ToString());
    }

    // Case 2
    [Fact]
    public void AnEligibleFailureUnderAutomaticSelectionStartsTheNamedFallbackOnce()
    {
        var gpu = new FakeDriver("gpu", GpuUnavailable);
        var factory = new FallbackFactory(_ => new FakeDriver("sdl"));
        var log = new StringWriter();
        IDriver started = ScreenDriverFactory.InitializeWithFallback(gpu, allowFallback: true, factory.Create, log);
        Assert.Same(factory.Created, started);
        Assert.Equal(["SDLDriver"], factory.Requested);
        Assert.Equal((1, 1), (gpu.Initialized, gpu.Disposed));
        Assert.Equal((1, 0), (factory.Created!.Initialized, factory.Created.Disposed));
        string text = log.ToString();
        Assert.Contains("FakeDriver could not start: [SDLGpu] SDL_CreateGPUDevice failed: no device", text);
        Assert.Contains("started (fallback from FakeDriver)", text);
    }

    // Case 3
    [Fact]
    public void AnExplicitSelectionFailsFastWithTheOriginalError()
    {
        DriverStartupFallbackException original = GpuUnavailable();
        var gpu = new FakeDriver("gpu", () => original);
        var factory = new FallbackFactory(_ => new FakeDriver("sdl"));
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            ScreenDriverFactory.InitializeWithFallback(gpu, allowFallback: false, factory.Create, null));
        Assert.Equal(original.Message, thrown.Message);
        Assert.Same(original.InnerException, thrown.InnerException);   // the driver's ordinary error, not the signal
        Assert.Empty(factory.Requested);
        Assert.Equal(0, gpu.Disposed);
    }

    // Case 5
    [Fact]
    public void WhenBothFailBothErrorsAreReportedAndTheFallbackIsDisposed()
    {
        var gpu = new FakeDriver("gpu", GpuUnavailable);
        var second = new InvalidOperationException("SDLDriver initialization failed: no video device");
        var factory = new FallbackFactory(_ => new FakeDriver("sdl", () => second));
        var thrown = Assert.Throws<AggregateException>(() =>
            ScreenDriverFactory.InitializeWithFallback(gpu, allowFallback: true, factory.Create, null));
        Assert.Equal(2, thrown.InnerExceptions.Count);
        Assert.IsType<DriverStartupFallbackException>(thrown.InnerExceptions[0]);
        Assert.Same(second, thrown.InnerExceptions[1]);
        Assert.Contains("SDL_CreateGPUDevice failed", thrown.Message);
        Assert.Contains("no video device", thrown.Message);
        Assert.Equal(1, factory.Created!.Disposed);
        Assert.Single(factory.Requested);
    }

    [Fact]
    public void AFallbackThatAsksForAnotherFallbackIsNotRetried()
    {
        var gpu = new FakeDriver("gpu", GpuUnavailable);
        var factory = new FallbackFactory(_ => new FakeDriver("sdl", GpuUnavailable));
        Assert.Throws<AggregateException>(() =>
            ScreenDriverFactory.InitializeWithFallback(gpu, allowFallback: true, factory.Create, null));
        Assert.Single(factory.Requested);
    }

    // Case 6
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(FileNotFoundException))]
    public void IneligibleFailuresPropagateUnchanged(Type type)
    {
        var original = (Exception)Activator.CreateInstance(type, "application or startup failure")!;
        var gpu = new FakeDriver("gpu", () => original);
        var factory = new FallbackFactory(_ => new FakeDriver("sdl"));
        var thrown = Assert.Throws(type, () =>
            ScreenDriverFactory.InitializeWithFallback(gpu, allowFallback: true, factory.Create, null));
        Assert.Same(original, thrown);
        Assert.Empty(factory.Requested);
        Assert.Equal(0, gpu.Disposed);
    }

    [Fact]
    public void AnUnregisteredFallbackRethrowsTheOriginalError()
    {
        DriverStartupFallbackException original = GpuUnavailable();
        var gpu = new FakeDriver("gpu", () => original);
        var factory = new FallbackFactory(_ => null);
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            ScreenDriverFactory.InitializeWithFallback(gpu, allowFallback: true, factory.Create, null));
        Assert.Equal(original.Message, thrown.Message);
    }

    // Cases 3, 4 and 9: which selections may fall back. Exact registered names are explicit choices.
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("sdl", true)]
    [InlineData(" SDL ", true)]
    [InlineData("SDLGpuDriver", false)]
    [InlineData("sdlgpudriver", false)]
    [InlineData("SDLDriver", false)]
    [InlineData("console", false)]
    [InlineData("Win32ConsoleDriver", false)]
    [InlineData("NoSuchDriver", true)]
    public void OnlyAutomaticSelectionAndTheSdlAliasMayFallBack(string? requested, bool allowed)
    {
        ScreenDriverAttribute[] registrations =
        [
            new() { System = Platform.Windows, Driver = "SDLGpuDriver", Priority = 300 },
            new() { System = Platform.Windows, Driver = "SDLDriver", Priority = 200 },
            new() { System = Platform.Windows, Driver = "Win32ConsoleDriver", Priority = 50 },
        ];
        Assert.Equal(allowed, ScreenDriverFactory.AllowsStartupFallback(Platform.Windows, registrations, requested));
    }

    // Case 4: an explicit SDLDriver request selects SDLDriver, so the GPU driver is never constructed.
    [Fact]
    public void AnExplicitSdlRendererRequestNeverSelectsTheGpuDriver()
    {
        (Type, ScreenDriverAttribute)[] registrations =
        [
            (typeof(string), new ScreenDriverAttribute { System = Platform.Windows, Driver = "SDLGpuDriver", Priority = 300 }),
            (typeof(int), new ScreenDriverAttribute { System = Platform.Windows, Driver = "SDLDriver", Priority = 200 }),
        ];
        Assert.Equal(typeof(int), ScreenDriverFactory.ResolveDriverType(Platform.Windows, registrations, "SDLDriver", null));
        Assert.Equal(typeof(string), ScreenDriverFactory.ResolveDriverType(Platform.Windows, registrations, null, null));
    }
}
