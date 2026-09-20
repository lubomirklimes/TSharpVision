using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
// Original: TVIEW.CPP owns one shadowSize and TPROGRAM.CPP updates it for the screen mode.
// Corrected: every managed view reads the same screen-scoped value, including after mode changes.
public sealed class ShadowStateTests
{
    [Fact]
    public void ExistingAndNewViewsObserveEveryScreenModeTransition()
    {
        using var context = new ScreenContext(TDisplay.SM.CO80);
        var before = new ShadowProbe();
        var program = new TProgram();
        try
        {
            Assert.Equal(new TPoint(2, 1), before.ObservedShadow);

            TScreen.ScreenMode = TDisplay.SM.Mono;
            program.InitScreen();
            var afterMono = new ShadowProbe();
            Assert.Equal(new TPoint(0, 0), before.ObservedShadow);
            Assert.Equal(before.ObservedShadow, afterMono.ObservedShadow);
            Assert.Equal(TProgram.AP.Monochrome, program.AppPalette);
            Assert.True(TView.showMarkers);

            TScreen.ScreenMode = TDisplay.SM.BW80;
            program.InitScreen();
            var afterBlackWhite = new ShadowProbe();
            Assert.Equal(new TPoint(2, 1), before.ObservedShadow);
            Assert.Equal(before.ObservedShadow, afterBlackWhite.ObservedShadow);
            Assert.Equal(TProgram.AP.BlackWhite, program.AppPalette);
            Assert.False(TView.showMarkers);

            TScreen.ScreenMode = TDisplay.SM.CO80 | TDisplay.SM.Font8x8;
            program.InitScreen();
            Assert.Equal(new TPoint(1, 1), before.ObservedShadow);
            Assert.Equal(TProgram.AP.Color, program.AppPalette);

            TScreen.ScreenMode = TDisplay.SM.CO80;
            program.InitScreen();
            Assert.Equal(new TPoint(2, 1), before.ObservedShadow);
        }
        finally
        {
            program.ShutDown();
        }
    }

    [Fact]
    public void RenderingUsesUpdatedSharedShadowGeometry()
    {
        using var context = new ScreenContext(TDisplay.SM.CO80);
        var program = new TProgram();
        try
        {
            var root = new TGroup(new TRect(0, 0, 12, 2));
            root.buffer = new ScreenBuffer(12 * 2 * ScreenBuffer.GetSize());
            root.state |= Views.sfExposed;
            var background = new FillView(new TRect(0, 0, 12, 2), 'B', 0x1A);
            var shadowed = new FillView(new TRect(2, 0, 8, 1), 'S', 0x1F);
            shadowed.state |= Views.sfShadow;
            root.Insert(background);
            root.Insert(shadowed);

            shadowed.DrawView();
            background.DrawView();
            Assert.Equal((TColorAttr)0x08, root.buffer.Data[12 + 4].Attr);

            TScreen.ScreenMode = TDisplay.SM.Mono;
            program.InitScreen();
            root.buffer.Clear();
            shadowed.DrawView();
            background.DrawView();
            Assert.Equal((TColorAttr)0x1A, root.buffer.Data[12 + 4].Attr);
        }
        finally
        {
            program.ShutDown();
        }
    }

    private sealed class ShadowProbe : TView
    {
        internal ShadowProbe() : base(new TRect(0, 0, 1, 1)) { }
        internal TPoint ObservedShadow => shadowSize;
    }

    private sealed class FillView : TView
    {
        private readonly char _character;
        private readonly ushort _color;

        internal FillView(TRect bounds, char character, ushort color) : base(bounds)
        {
            _character = character;
            _color = color;
            state |= Views.sfExposed;
        }

        public override void Draw()
        {
            Span<TScreenChar> cells = stackalloc TScreenChar[size.x];
            var buffer = new TDrawBuffer(cells);
            buffer.moveChar(0, _character, _color, size.x);
            WriteLine(0, 0, size.x, size.y, buffer);
        }
    }

    private sealed class ScreenContext : IDisposable
    {
        private readonly DriverScope _driver = new(80, 25);
        private readonly TDisplay.SM _mode = TScreen.ScreenMode;
        private readonly ushort _width = TScreen.ScreenWidth;
        private readonly ushort _height = TScreen.ScreenHeight;
        private readonly TPoint _shadow = TView.shadowSize;
        private readonly bool _markers = TView.showMarkers;

        internal ScreenContext(TDisplay.SM mode)
        {
            TScreen.ScreenWidth = 80;
            TScreen.ScreenHeight = 25;
            TScreen.ScreenMode = mode;
        }

        public void Dispose()
        {
            TScreen.ScreenMode = _mode;
            TScreen.ScreenWidth = _width;
            TScreen.ScreenHeight = _height;
            TView.shadowSize = _shadow;
            TView.showMarkers = _markers;
            _driver.Dispose();
        }
    }
}
