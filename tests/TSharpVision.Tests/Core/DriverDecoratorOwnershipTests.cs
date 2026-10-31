using System.Reflection;
using TSharpVision.Drivers;
using Xunit;

namespace TSharpVision.Tests.Core;

/// <summary>Lifecycle ownership across ScreenDriverFactory decoration failures and success.</summary>
[Collection("NonParallel")]
public sealed class DriverDecoratorOwnershipTests : IDisposable
{
    public DriverDecoratorOwnershipTests() => Providers.Reset();
    public void Dispose() => Providers.Reset();

    private static string Name<T>() => typeof(T).AssemblyQualifiedName!;

    [Fact]
    public void SuccessfulDecorationTransfersOwnershipWithoutTouchingTheBackend()
    {
        var backend = new CountingDriver();
        IDriver result = ScreenDriverFactory.Decorate(backend, " " + Name<WrappingProvider>() + " ", "NullDriver");
        var wrapper = Assert.IsType<Wrapper>(result);
        Assert.Same(backend, wrapper.Backend);
        Assert.Equal("NullDriver", Providers.LastRequested);
        Assert.Equal(0, backend.Disposals);
        Assert.Equal(0, backend.Shutdowns);
        Assert.Equal(0, backend.Initializations);
    }

    [Fact]
    public void ReturningTheBackendLeavesItUndecoratedAndOwnedByTheCaller()
    {
        var backend = new CountingDriver();
        Assert.Same(backend, ScreenDriverFactory.Decorate(backend, Name<PassThroughProvider>(), null));
        Assert.Equal(0, backend.Disposals);
    }

    public static TheoryData<string, Type> Failures => new()
    {
        { "Missing.Provider, Missing.Assembly", typeof(FileNotFoundException) },
        { "TSharpVision.Tests.Core.NoSuchProvider, TSharpVision.Tests", typeof(TypeLoadException) },
        { Name<ThrowingConstructorProvider>(), typeof(TargetInvocationException) },
        { Name<NotAProvider>(), typeof(InvalidOperationException) },
        { Name<ThrowingProvider>(), typeof(IOException) },
        { Name<NullProvider>(), typeof(InvalidOperationException) },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void FailedDecorationDisposesTheUninitializedBackendExactlyOnce(string provider, Type expected)
    {
        var backend = new CountingDriver();
        Exception error = Assert.ThrowsAny<Exception>(() => ScreenDriverFactory.Decorate(backend, provider, null));
        Assert.IsAssignableFrom(expected, error);
        Assert.Equal(1, backend.Disposals);
        Assert.Equal(0, backend.Shutdowns);
        Assert.Equal(0, backend.Initializations);
    }

    [Fact]
    public void CleanupFailurePreservesBothErrors()
    {
        var backend = new CountingDriver { FailDispose = true };
        var error = Assert.Throws<AggregateException>(() => ScreenDriverFactory.Decorate(backend, Name<ThrowingProvider>(), null));
        Assert.Collection(error.InnerExceptions,
            first => Assert.Equal("decorate failed", first.Message),
            second => Assert.Equal("dispose failed", second.Message));
        Assert.Equal(1, backend.Disposals);
    }

    [Fact]
    public void NonDisposableBackendIsSimplyReleasedOnFailure()
    {
        var backend = new NullDriver();
        Assert.Throws<IOException>(() => ScreenDriverFactory.Decorate(backend, Name<ThrowingProvider>(), null));
    }

    internal static class Providers
    {
        internal static string? LastRequested;
        internal static void Reset() => LastRequested = null;
    }

    public sealed class WrappingProvider : IDriverDecorator
    {
        public IDriver Decorate(IDriver backend, string? requestedBackend)
        {
            Providers.LastRequested = requestedBackend;
            return new Wrapper(backend);
        }
    }
    public sealed class PassThroughProvider : IDriverDecorator
    {
        public IDriver Decorate(IDriver backend, string? requestedBackend) => backend;
    }
    public sealed class ThrowingProvider : IDriverDecorator
    {
        public IDriver Decorate(IDriver backend, string? requestedBackend) => throw new IOException("decorate failed");
    }
    public sealed class NullProvider : IDriverDecorator
    {
        public IDriver Decorate(IDriver backend, string? requestedBackend) => null!;
    }
    public sealed class ThrowingConstructorProvider : IDriverDecorator
    {
        public ThrowingConstructorProvider() => throw new IOException("constructor failed");
        public IDriver Decorate(IDriver backend, string? requestedBackend) => backend;
    }
    public sealed class NotAProvider { }

    private sealed class Wrapper(IDriver backend) : Delegating
    {
        internal IDriver Backend { get; } = backend;
    }

    private sealed class CountingDriver : Delegating, IDisposable
    {
        internal int Disposals, Shutdowns, Initializations;
        internal bool FailDispose;
        public override void Initialize() { Initializations++; base.Initialize(); }
        public override void Shutdown() { Shutdowns++; base.Shutdown(); }
        public void Dispose()
        {
            Disposals++;
            if (FailDispose) throw new IOException("dispose failed");
        }
    }

    private class Delegating : IDriver
    {
        private readonly NullDriver _inner = new();
        public virtual void Initialize() => _inner.Initialize();
        public void Suspend() => _inner.Suspend();
        public void Resume() => _inner.Resume();
        public virtual void Shutdown() => _inner.Shutdown();
        public ushort GetCols() => _inner.GetCols();
        public ushort GetRows() => _inner.GetRows();
        public TDisplay.SM GetScreenMode() => _inner.GetScreenMode();
        public void SetScreenMode(TDisplay.SM mode) => _inner.SetScreenMode(mode);
        public ScreenBuffer AllocateScreenBuffer() => _inner.AllocateScreenBuffer();
        public void ClearScreen(ushort cols, ushort rows) { }
        public ushort GetCursorType() => _inner.GetCursorType();
        public void SetCursorType(ushort type) => _inner.SetCursorType(type);
        public void SetCaretPosition(int x, int y) { }
        public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf) { }
        public void MakeBeep() { }
        public void PumpMessages() { }
        public bool ReadKeyEvent(out TEvent ev) { ev = default; return false; }
        public bool SupportsMouse => false;
        public bool SupportsTrueColor => false;
        public bool SupportsGraphics => false;
    }
}
