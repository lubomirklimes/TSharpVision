using TSharpVision.Drivers;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using static TSharpVision.Tests.Samples.MonochromeModeQualificationTests;

namespace TSharpVision.Tests.Samples;

// TV12j: the historical CRT entry points (TDisplay.SetCrtMode, TScreen.FixCrtMode / SetVideoMode / Suspend / Resume,
// TProgram.SetScreenMode) map Color / BW / Mono intent onto the logical mode and palette family. Shipped drivers keep
// their physical display (they report CO80), so only the logical mode changes; nothing claims a resolution change.
[Collection("NonParallel")]
public sealed class LegacyCrtModeBridgeTests
{
    [Theory]
    [InlineData((ushort)0x0003, (ushort)0x0003)] // CO80
    [InlineData((ushort)0x0002, (ushort)0x0002)] // BW80
    [InlineData((ushort)0x0007, (ushort)0x0007)] // Mono
    [InlineData((ushort)0x0000, (ushort)0x0003)] // BW40 is not a Turbo Vision mode
    [InlineData((ushort)0x0001, (ushort)0x0003)]
    [InlineData((ushort)0x0013, (ushort)0x0003)] // graphics mode
    [InlineData((ushort)0x00FF, (ushort)0x0003)]
    [InlineData((ushort)0x0103, (ushort)0x0103)] // CO80 | Font8x8
    [InlineData((ushort)0x0107, (ushort)0x0107)] // Mono | Font8x8
    [InlineData((ushort)0x0100, (ushort)0x0103)] // Font8x8 alone keeps the bit, low byte becomes CO80
    public void FixCrtModeNormalizesTheLowByteAndKeepsTheHighByteLikeTurboVision(ushort mode, ushort expected)
    {
        Assert.Equal((TDisplay.SM)expected, TScreen.FixCrtMode(mode));
    }

    [Fact]
    public void SetCrtModeAsksTheDriverAndNeedsAnInitializedDriver()
    {
        using var scope = new DriverScope(80, 25);
        var recording = new RecordingDriver(scope.Driver);
        TDisplay.driver = recording;

        TDisplay.SetCrtMode(TDisplay.SM.Mono);
        Assert.Equal(new[] { TDisplay.SM.Mono }, recording.Requests);
        Assert.Equal(TDisplay.SM.CO80, TDisplay.GetCrtMode()); // the backend keeps its display

        TDisplay.driver = null;
        Assert.Throws<InvalidOperationException>(() => TDisplay.SetCrtMode(TDisplay.SM.CO80));
    }

    [Fact]
    public void SetVideoModeRecordsTheNormalizedLogicalModeWithoutChangingTheGeometry()
    {
        using var scope = new DriverScope(100, 35);
        var recording = new RecordingDriver(scope.Driver);
        TDisplay.driver = recording;
        var saved = TScreen.ScreenMode;
        try
        {
            TScreen.SetVideoMode((ushort)TDisplay.SM.BW80);
            Assert.Equal(TDisplay.SM.BW80, TScreen.ScreenMode);
            Assert.Equal(TDisplay.SM.CO80, TDisplay.GetCrtMode());
            Assert.Equal(100, TScreen.ScreenWidth); Assert.Equal(35, TScreen.ScreenHeight); Assert.True(TScreen.HiResScreen);

            TScreen.SetVideoMode(0x13);
            Assert.Equal(TDisplay.SM.CO80, TScreen.ScreenMode);
            Assert.Equal(new[] { TDisplay.SM.BW80, TDisplay.SM.CO80 }, recording.Requests);
        }
        finally { TScreen.ScreenMode = saved; }
    }

    [Fact]
    public void SuspendAndResumeWithALogicalModeAskTheDriverAndKeepTheApplicationsMode()
    {
        using var scope = new DriverScope(80, 25);
        var recording = new RecordingDriver(scope.Driver);
        TDisplay.driver = recording;
        var saved = (TScreen.ScreenMode, TScreen.StartupMode);
        try
        {
            TScreen.SetCrtData();
            TScreen.StartupMode = TDisplay.GetCrtMode();
            TScreen.SetVideoMode((ushort)TDisplay.SM.Mono);
            recording.Requests.Clear();

            TScreen.Suspend(); // threw NotImplementedException before TV12j whenever the logical mode differed
            TScreen.Resume();

            Assert.Equal(new[] { TDisplay.SM.CO80, TDisplay.SM.Mono }, recording.Requests);
            Assert.Equal(TDisplay.SM.Mono, TScreen.ScreenMode);
            Assert.Equal(TDisplay.SM.CO80, TScreen.StartupMode);
        }
        finally { (TScreen.ScreenMode, TScreen.StartupMode) = saved; }
    }

    [Fact]
    public void LegacyRoundTripCo80Bw80MonoCo80SelectsTheFamiliesAndRestoresTheExactColorCells()
    {
        RecordingDriver? recording = null;
        using var scope = new DemoTestScope(wrapDriver: inner => recording = new RecordingDriver(inner)); var app = scope.App;
        var saved = (TScreen.ScreenMode, TView.shadowSize, TView.showMarkers);
        try
        {
            BuildSurface(app); app.Redraw(); var color = Snapshot();

            app.SetScreenMode((ushort)TDisplay.SM.BW80);
            Assert.Equal((TDisplay.SM.BW80, TProgram.AP.BlackWhite), (TScreen.ScreenMode, app.AppPalette));
            Assert.Equal(new TPoint(2, 1), TView.shadowSize); Assert.False(TView.showMarkers);
            AssertNeutralScreen("legacy BW80"); AssertIncrementalMatchesFullRedraw(app);

            app.SetScreenMode((ushort)TDisplay.SM.Mono);
            Assert.Equal((TDisplay.SM.Mono, TProgram.AP.Monochrome), (TScreen.ScreenMode, app.AppPalette));
            Assert.Equal(new TPoint(0, 0), TView.shadowSize); Assert.True(TView.showMarkers);
            AssertNeutralScreen("legacy Mono"); AssertIncrementalMatchesFullRedraw(app);

            app.SetScreenMode((ushort)TDisplay.SM.CO80);
            Assert.Equal((TDisplay.SM.CO80, TProgram.AP.Color), (TScreen.ScreenMode, app.AppPalette));
            Assert.Equal(new TPoint(2, 1), TView.shadowSize); Assert.False(TView.showMarkers);
            AssertSameCells(color, "legacy CO80 after BW80 and Mono");

            Assert.Equal(new[] { TDisplay.SM.BW80, TDisplay.SM.Mono, TDisplay.SM.CO80 }, recording!.Requests);
            Assert.Equal((ushort)80, TScreen.ScreenWidth); Assert.Equal((ushort)25, TScreen.ScreenHeight);
        }
        finally { (TScreen.ScreenMode, TView.shadowSize, TView.showMarkers) = saved; }
    }

    [Fact]
    public void ModernRoundTripLeavesTheLogicalModeAndTheLegacyPathStillAppliesIt()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var saved = (TScreen.ScreenMode, TView.shadowSize, TView.showMarkers);
        try
        {
            BuildSurface(app); app.Redraw(); var color = Snapshot();
            foreach (var family in new[] { TProgram.AP.BlackWhite, TProgram.AP.Monochrome, TProgram.AP.Color })
            {
                app.SetAppPalette(family);
                Assert.Equal(family, app.AppPalette);
                Assert.Equal(TDisplay.SM.CO80, TScreen.ScreenMode); // the palette API is not a mode change
                if (family != TProgram.AP.Color) AssertNeutralScreen($"modern {family}");
            }
            AssertSameCells(color, "modern Color after BlackWhite and Monochrome");

            // Mixed: a legacy mode, then the modern API over it, then a legacy refresh re-derives from the mode.
            app.SetScreenMode((ushort)TDisplay.SM.Mono);
            app.SetAppPalette(TProgram.AP.Color);
            Assert.Equal(TDisplay.SM.Mono, TScreen.ScreenMode); Assert.True(TView.showMarkers);
            app.SetScreenMode(0);
            Assert.Equal(TProgram.AP.Monochrome, app.AppPalette);
            AssertNeutralScreen("legacy refresh after modern Color");

            app.SetScreenMode((ushort)TDisplay.SM.CO80);
            AssertSameCells(color, "back to CO80");
        }
        finally { (TScreen.ScreenMode, TView.shadowSize, TView.showMarkers) = saved; }
    }

    [Theory]
    [InlineData((ushort)0x0013)] [InlineData((ushort)0x0001)]
    public void UnknownLegacyModeFallsBackToColor(ushort mode)
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var saved = (TScreen.ScreenMode, TView.shadowSize, TView.showMarkers);
        try
        {
            app.SetScreenMode((ushort)TDisplay.SM.Mono);
            app.SetScreenMode(mode);
            Assert.Equal((TDisplay.SM.CO80, TProgram.AP.Color), (TScreen.ScreenMode, app.AppPalette));
            Assert.False(TView.showMarkers);
        }
        finally { (TScreen.ScreenMode, TView.shadowSize, TView.showMarkers) = saved; }
    }

    [Fact]
    public void Font8x8IsALogicalFlagThatOnlyNarrowsShadowsAndKeepsTheRowCount()
    {
        using var scope = new DemoTestScope(); var app = scope.App;
        var saved = (TScreen.ScreenMode, TView.shadowSize, TView.showMarkers);
        try
        {
            app.SetScreenMode((ushort)(TDisplay.SM.CO80 | TDisplay.SM.Font8x8));
            Assert.Equal(TDisplay.SM.CO80 | TDisplay.SM.Font8x8, TScreen.ScreenMode);
            Assert.Equal(TProgram.AP.Color, app.AppPalette);
            Assert.Equal(new TPoint(1, 1), TView.shadowSize);
            Assert.Equal((ushort)25, TScreen.ScreenHeight); Assert.False(TScreen.HiResScreen);
            Assert.Equal(new TPoint(80, 25), app.size);
        }
        finally { (TScreen.ScreenMode, TView.shadowSize, TView.showMarkers) = saved; }
    }

    // TV12j decision record: Borland TV 1.0 and magiblot/tvision both define errorAttr = 0xCF, but this port keeps the
    // original Borland application palettes, whose reserved entries are 0x00 (magiblot filled them in). Visible views
    // resolve through those entries, e.g. TOutline's collapsed-item role in a blue window lands on application entry 15,
    // so 0xCF would paint them light red. errorAttr stays 0x00 until the owner decides on palettes and errorAttr together.
    [Fact]
    public void ErrorAttrStaysZeroBecauseVisibleRolesResolveThroughReservedPaletteEntries()
    {
        Assert.Equal(0, TView.errorAttr);
        Assert.Equal('\x00', TProgram.cpColor[14]); // application entry 15, reserved in Turbo Vision 2.0

        using var scope = new DemoTestScope(); var app = scope.App;
        var win = new TWindow(new TRect(0, 0, 30, 10), "Outline", 1);
        var outline = new TOutline(new TRect(1, 1, 29, 9), null, null, new TNode("Root", new TNode("Child"), null, false));
        win.Insert(outline);
        app.DeskTop!.Insert(win);
        try
        {
            Assert.Equal(TView.errorAttr, outline.MapColor(4)); // collapsed-item role -> window 8 -> application 15
        }
        finally { win.Close(); }
    }

    // Forwards IDriver to the scope's NullDriver and records every SetScreenMode request.
    private sealed class RecordingDriver : IDriver
    {
        private readonly IDriver _inner;
        public RecordingDriver(IDriver inner) => _inner = inner;
        public List<TDisplay.SM> Requests { get; } = new();
        public void Initialize() => _inner.Initialize();
        public void Suspend() => _inner.Suspend();
        public void Resume() => _inner.Resume();
        public void Shutdown() => _inner.Shutdown();
        public ushort GetCols() => _inner.GetCols();
        public ushort GetRows() => _inner.GetRows();
        public TDisplay.SM GetScreenMode() => _inner.GetScreenMode();
        public void SetScreenMode(TDisplay.SM mode) { Requests.Add(mode); _inner.SetScreenMode(mode); }
        public ScreenBuffer AllocateScreenBuffer() => _inner.AllocateScreenBuffer();
        public void ClearScreen(ushort cols, ushort rows) => _inner.ClearScreen(cols, rows);
        public ushort GetCursorType() => _inner.GetCursorType();
        public void SetCursorType(ushort cursorType) => _inner.SetCursorType(cursorType);
        public void SetCaretPosition(int x, int y) => _inner.SetCaretPosition(x, y);
        public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf) => _inner.WriteBuf(x, y, w, h, buf);
        public void MakeBeep() => _inner.MakeBeep();
        public void PumpMessages() => _inner.PumpMessages();
        public bool ReadKeyEvent(out TEvent ev) => _inner.ReadKeyEvent(out ev);
        public bool SupportsMouse => _inner.SupportsMouse;
        public bool SupportsTrueColor => _inner.SupportsTrueColor;
        public bool SupportsGraphics => _inner.SupportsGraphics;
        public KeyboardCapabilities KeyboardCapabilities => _inner.KeyboardCapabilities;
    }
}
