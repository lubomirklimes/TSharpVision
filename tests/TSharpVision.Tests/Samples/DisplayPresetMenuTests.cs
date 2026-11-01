using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using Xunit;

namespace TSharpVision.Tests.Samples;

// TV12e/TV12f: Options/Physical palette and Options/Display effect exist only when the active driver implements
// IPhysicalPaletteDriver / IDisplayEffectDriver when the menu bar is built; they list the driver's ids,
// mark the active one and apply a selection through the driver. Framework palette stays independent.
[Collection("NonParallel")]
public sealed class DisplayPresetMenuTests
{
    // TV12f: the SDL GPU driver's lists (VGA/CGA removed; Default is the classic VGA mapping).
    private static readonly string[] Palettes = { "Default", "Green", "Amber", "Grayscale" };
    private static readonly string[] Effects = { "Default", "Scanlines", "Glow", "Curvature", "ShadowMask" };

    [Fact]
    public void UnsupportedDriverShowsNoDisplaySubmenusAndKeepsOptionsUnchanged()
    {
        using var scope = new DemoTestScope();
        AssertOptionsWithoutDisplaySubmenus(scope.App);
        AssertNoDisplayCommands(scope.App);
    }

    [Fact]
    public void ForwardingDecoratorWithoutTheCapabilitiesHidesThem()
    {
        using var scope = new DemoTestScope(wrapDriver: d => new ForwardingDriver(new CapableDriver(d)));
        Assert.IsNotAssignableFrom<IPhysicalPaletteDriver>(TDisplay.driver);
        AssertOptionsWithoutDisplaySubmenus(scope.App);
        AssertNoDisplayCommands(scope.App);
    }

    [Fact]
    public void CapableDriverAddsBothSubmenusAfterFrameworkPaletteWithDistinctAccelerators()
    {
        using var scope = new DemoTestScope(wrapDriver: d => new CapableDriver(d));
        var items = Named(OptionsMenu(scope.App));
        Assert.Equal(new[] { "UI colors...", "Framework palette", "Physical palette", "Display effect" },
            items.Select(i => i.Name!.Replace("~", "")));
        Assert.Equal(new[] { 'C', 'F', 'P', 'E' }, items.Select(i => i.HotChar()));
        Assert.All(items.Skip(1), i => Assert.True(i.IsSubMenu));
        Assert.Null(TVDemoMenuNavigationTests.Items(OptionsMenu(scope.App)).ElementAt(2).Name); // separator

        Assert.Equal(Palettes, Ids(PaletteItems(scope.App)));
        Assert.Equal(Effects, Ids(EffectItems(scope.App)));
        Assert.Equal(new[] { 'D', 'G', 'A', 'R' }, PaletteItems(scope.App).Select(i => i.HotChar()));
        Assert.Equal(new[] { 'D', 'S', 'G', 'C', 'H' }, EffectItems(scope.App).Select(i => i.HotChar()));
        Assert.Equal(Enumerable.Range(360, 4).Select(i => (ushort)i), PaletteItems(scope.App).Select(i => i.Command));
        Assert.Equal(new ushort[] { 370, 371, 372, 373, 374 }, EffectItems(scope.App).Select(i => i.Command));
        Assert.All(PaletteItems(scope.App).Concat(EffectItems(scope.App)), i => Assert.Equal(Keys.kbNoKey, i.KeyCode));
        AssertMarked(PaletteItems(scope.App), "Default");
        AssertMarked(EffectItems(scope.App), "Default");
        AssertFrameworkMarked(scope.App, 'C');
    }

    [Fact]
    public void SelectingCallsTheDriverUpdatesTheMarkAndDefaultReturns()
    {
        var driver = (CapableDriver)null!;
        using var scope = new DemoTestScope(wrapDriver: d => driver = new CapableDriver(d)); var app = scope.App;

        app.RunCommand(Command(PaletteItems(app), "Amber"));
        Assert.Equal("Amber", driver.PhysicalPalette); AssertMarked(PaletteItems(app), "Amber");
        app.RunCommand(Command(EffectItems(app), "Scanlines"));
        Assert.Equal("Scanlines", driver.DisplayEffect); AssertMarked(EffectItems(app), "Scanlines");
        AssertMarked(PaletteItems(app), "Amber");

        app.RunCommand(Command(PaletteItems(app), "Default"));
        app.RunCommand(Command(EffectItems(app), "Default"));
        Assert.Equal(new[] { "Amber", "Default" }, driver.PaletteCalls);
        Assert.Equal(new[] { "Scanlines", "Default" }, driver.EffectCalls);
        AssertMarked(PaletteItems(app), "Default"); AssertMarked(EffectItems(app), "Default");
        Assert.Equal(TProgram.AP.Color, app.AppPalette);
    }

    [Fact]
    public void FrameworkPaletteAndPhysicalPaletteStayIndependent()
    {
        var driver = (CapableDriver)null!;
        using var scope = new DemoTestScope(wrapDriver: d => driver = new CapableDriver(d)); var app = scope.App;

        app.RunCommand(Command(PaletteItems(app), "Green"));
        Assert.Equal(TProgram.AP.Color, app.AppPalette); AssertFrameworkMarked(app, 'C');

        app.RunCommand(ShowcaseCmd.PaletteMonochrome);
        Assert.Equal(TProgram.AP.Monochrome, app.AppPalette); AssertFrameworkMarked(app, 'M');
        Assert.Equal("Green", driver.PhysicalPalette); AssertMarked(PaletteItems(app), "Green");
        Assert.Equal(new[] { "Green" }, driver.PaletteCalls);
        Assert.Empty(driver.EffectCalls);

        app.RunCommand(ShowcaseCmd.PaletteColor);
        Assert.Equal("Green", driver.PhysicalPalette); AssertFrameworkMarked(app, 'C');
    }

    [Fact]
    public void KeyboardPathThroughOptionsMenuSelectsAPhysicalPalette()
    {
        var driver = (CapableDriver)null!;
        using var scope = new DemoTestScope(wrapDriver: d => driver = new CapableDriver(d)); var app = scope.App;
        app.Step(() => ComboInput.Send(ComboInput.Typed("p"))); // Options -> Physical palette
        app.Step(() =>
        {
            var box = app.Modal<TMenuBox>();
            Assert.Contains(PaletteItems(app), i => i == box.Current);
            ComboInput.Send(ComboInput.Typed("r")); // G~r~ayscale
        });
        var key = ComboInput.Key(Keys.kbAltO); app.HandleEvent(ref key);
        Assert.Null(TView.ModalTopView);
        DispatchMenuResult(app);
        Assert.Equal("Grayscale", driver.PhysicalPalette); AssertMarked(PaletteItems(app), "Grayscale");
    }

    [Theory]
    [InlineData("s", "Scanlines")]
    [InlineData("g", "Glow")]
    [InlineData("c", "Curvature")]
    [InlineData("h", "ShadowMask")] // S~h~adowMask
    public void KeyboardPathThroughOptionsMenuSelectsADisplayEffect(string hotKey, string expected)
    {
        var driver = (CapableDriver)null!;
        using var scope = new DemoTestScope(wrapDriver: d => driver = new CapableDriver(d)); var app = scope.App;
        app.Step(() => ComboInput.Send(ComboInput.Typed("e"))); // Options -> Display effect
        app.Step(() => ComboInput.Send(ComboInput.Typed(hotKey)));
        var key = ComboInput.Key(Keys.kbAltO); app.HandleEvent(ref key);
        Assert.Null(TView.ModalTopView);
        DispatchMenuResult(app);
        Assert.Equal(expected, driver.DisplayEffect); AssertMarked(EffectItems(app), expected);
        Assert.Equal(new[] { expected }, driver.EffectCalls);
    }

    [Fact]
    public void EffectsAreExclusiveSelectingOneReplacesTheOther()
    {
        var driver = (CapableDriver)null!;
        using var scope = new DemoTestScope(wrapDriver: d => driver = new CapableDriver(d)); var app = scope.App;
        app.RunCommand(Command(EffectItems(app), "Glow"));
        app.RunCommand(Command(EffectItems(app), "ShadowMask"));
        Assert.Equal("ShadowMask", driver.DisplayEffect); AssertMarked(EffectItems(app), "ShadowMask");
        app.RunCommand(Command(EffectItems(app), "Curvature"));
        Assert.Equal("Curvature", driver.DisplayEffect); AssertMarked(EffectItems(app), "Curvature");
        app.RunCommand(Command(EffectItems(app), "Default"));
        Assert.Equal(new[] { "Glow", "ShadowMask", "Curvature", "Default" }, driver.EffectCalls);
        AssertMarked(EffectItems(app), "Default");
    }

    [Fact]
    public void ChangeMadeOutsideTheMenuIsReflectedOnIdle()
    {
        var driver = (CapableDriver)null!;
        using var scope = new DemoTestScope(wrapDriver: d => driver = new CapableDriver(d)); var app = scope.App;
        driver.SetPhysicalPalette("grayscale"); driver.SetDisplayEffect(" shadowmask ");
        app.Idle();
        AssertMarked(PaletteItems(app), "Grayscale"); AssertMarked(EffectItems(app), "ShadowMask");
    }

    [Fact]
    public void RejectedSelectionKeepsTheDriverChoiceAndItsMark()
    {
        var driver = (CapableDriver)null!;
        using var scope = new DemoTestScope(wrapDriver: d => driver = new CapableDriver(d, rejectedPalette: "Green")); var app = scope.App;
        app.RunCommand(Command(PaletteItems(app), "Amber"));
        app.RunCommand(Command(PaletteItems(app), "Green"));
        Assert.Equal("Amber", driver.PhysicalPalette); AssertMarked(PaletteItems(app), "Amber");
    }

    [Fact]
    public void PaletteOnlyDriverShowsOnlyThePhysicalPaletteSubmenu()
    {
        using var scope = new DemoTestScope(wrapDriver: d => new PaletteOnlyDriver(d));
        Assert.Equal(new[] { "UI colors...", "Framework palette", "Physical palette" },
            Named(OptionsMenu(scope.App)).Select(i => i.Name!.Replace("~", "")));
        Assert.Equal(Palettes, Ids(PaletteItems(scope.App)));
        // No effect menu: its command is not handled and stays unconsumed.
        var ev = ScriptedDemoApp.CommandEvent(DisplayPresetCmd.DisplayEffectFirst); scope.App.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
    }

    [Fact]
    public void DisplaySubmenusFitIn80ColumnsAnd25Rows()
    {
        using var scope = new DemoTestScope(wrapDriver: d => new CapableDriver(d));
        var bar = Named(scope.App.MenuBar!.Menu!);
        Assert.True(1 + bar.Sum(i => i.Name!.Replace("~", "").Length + 2) <= 80);
        foreach (var menu in new[] { OptionsMenu(scope.App), Submenu(scope.App, 'P'), Submenu(scope.App, 'E') })
        {
            Assert.Equal(Named(menu).Length, Named(menu).Select(i => i.HotChar()).Distinct().Count());
            // Options opens under the bar; its submenus open to the right of it (worst case: right edge).
            var box = new TMenuBox(new TRect(1, 1, 80, 24), menu, null);
            try
            {
                Assert.True(box.origin.x >= 0 && box.origin.x + box.size.x <= 80);
                Assert.True(box.origin.y + box.size.y <= 24);
            }
            finally { box.ShutDown(); }
        }
        Assert.All(Named(OptionsMenu(scope.App)), i => Assert.True(i.Name!.Replace("~", "").Length <= 20));
    }

    // ---- helpers ----

    private static void AssertOptionsWithoutDisplaySubmenus(ScriptedDemoApp app)
    {
        var all = TVDemoMenuNavigationTests.Items(OptionsMenu(app)).ToArray();
        Assert.Equal(new[] { "UI ~c~olors...", "~F~ramework palette" }, all.Select(i => i.Name));
        Assert.Equal(new[] { 'C', 'F' }, all.Select(i => i.HotChar()));
        Assert.Equal(ShowcaseCmd.Colors, all[0].Command);
        Assert.Equal(3, TVDemoMenuNavigationTests.Items(all[1].SubMenu!).Count());
        AssertFrameworkMarked(app, 'C');
    }

    private static void AssertNoDisplayCommands(ScriptedDemoApp app)
    {
        var commands = Leaves(app.MenuBar!.Menu!).Select(i => i.Command).ToArray();
        Assert.DoesNotContain(commands, c => c >= 360 && c < 380);
        Assert.Equal(55, commands.Length); // includes Help > Search (TV12l), STYX graphics (TV12n), Graphics gallery and Live chart (TV12o)
    }

    private static IEnumerable<TMenuItem> Leaves(TMenu menu) =>
        TVDemoMenuNavigationTests.Items(menu).Where(i => !string.IsNullOrEmpty(i.Name))
            .SelectMany(i => i.IsSubMenu ? Leaves(i.SubMenu) : new[] { i });

    private static TMenuItem[] Named(TMenu menu) =>
        TVDemoMenuNavigationTests.Items(menu).Where(i => !string.IsNullOrEmpty(i.Name)).ToArray();

    private static TMenu OptionsMenu(ScriptedDemoApp app) =>
        Named(app.MenuBar!.Menu!).Single(i => i.HotChar() == 'O').SubMenu!;

    private static TMenu Submenu(ScriptedDemoApp app, char hot) =>
        Named(OptionsMenu(app)).Single(i => i.HotChar() == hot).SubMenu!;

    private static TMenuItem[] PaletteItems(ScriptedDemoApp app) => Named(Submenu(app, 'P'));
    private static TMenuItem[] EffectItems(ScriptedDemoApp app) => Named(Submenu(app, 'E'));

    // "(x) ~G~reen" -> "Green"
    private static IEnumerable<string> Ids(TMenuItem[] items) => items.Select(i => i.Name!.Substring(4).Replace("~", ""));

    private static ushort Command(TMenuItem[] items, string id) =>
        items.Single(i => i.Name!.Substring(4).Replace("~", "") == id).Command;

    private static void AssertMarked(TMenuItem[] items, string id)
    {
        foreach (var item in items)
        {
            bool on = item.Name!.Substring(4).Replace("~", "") == id;
            Assert.StartsWith(on ? $"({TSharpVisionGlyphs.RadioChecked}) " : "( ) ", item.Name);
        }
        Assert.Single(items, i => i.Name!.StartsWith($"({TSharpVisionGlyphs.RadioChecked})"));
    }

    private static void AssertFrameworkMarked(ScriptedDemoApp app, char hot)
    {
        var items = Named(Submenu(app, 'F'));
        Assert.Equal(new ushort[] { ShowcaseCmd.PaletteColor, ShowcaseCmd.PaletteBlackWhite, ShowcaseCmd.PaletteMonochrome },
            items.Select(i => i.Command));
        foreach (var item in items)
            Assert.StartsWith(item.HotChar() == hot ? $"({TSharpVisionGlyphs.RadioChecked})" : "( )", item.Name);
    }

    private static void DispatchMenuResult(ScriptedDemoApp app)
    {
        TEvent command = default; app.GetEvent(ref command);
        Assert.Equal(Events.evCommand, command.What);
        app.HandleEvent(ref command);
    }

    // ---- test drivers around the scope's NullDriver ----

    // A decorator that forwards IDriver only, like one that does not opt into the optional capabilities.
    private class ForwardingDriver : IDriver
    {
        private readonly IDriver _inner;
        public ForwardingDriver(IDriver inner) => _inner = inner;
        public void Initialize() => _inner.Initialize();
        public void Suspend() => _inner.Suspend();
        public void Resume() => _inner.Resume();
        public void Shutdown() => _inner.Shutdown();
        public ushort GetCols() => _inner.GetCols();
        public ushort GetRows() => _inner.GetRows();
        public TDisplay.SM GetScreenMode() => _inner.GetScreenMode();
        public void SetScreenMode(TDisplay.SM mode) => _inner.SetScreenMode(mode);
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

    // Follows the IPhysicalPaletteDriver contract: canonical ids, case-insensitive trimmed matching,
    // null -> ArgumentNullException, unknown (or the configured rejected id) -> ArgumentException, no change.
    private class PaletteOnlyDriver : ForwardingDriver, IPhysicalPaletteDriver
    {
        private readonly string? _rejectedPalette;
        public PaletteOnlyDriver(IDriver inner, string? rejectedPalette = null) : base(inner) => _rejectedPalette = rejectedPalette;
        public List<string> PaletteCalls { get; } = new();
        public IReadOnlyList<string> PhysicalPalettes => Palettes;
        public string PhysicalPalette { get; private set; } = DisplayPresetNames.Default;
        public void SetPhysicalPalette(string palette)
        {
            PaletteCalls.Add(palette);
            string id = Resolve(Palettes, palette, nameof(palette));
            if (id == _rejectedPalette) throw new ArgumentException("rejected", nameof(palette));
            PhysicalPalette = id;
        }

        protected static string Resolve(string[] ids, string value, string name)
        {
            ArgumentNullException.ThrowIfNull(value, name);
            return ids.FirstOrDefault(i => string.Equals(i, value.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown '{value}'.", name);
        }
    }

    private sealed class CapableDriver : PaletteOnlyDriver, IDisplayEffectDriver
    {
        public CapableDriver(IDriver inner, string? rejectedPalette = null) : base(inner, rejectedPalette) { }
        public List<string> EffectCalls { get; } = new();
        public IReadOnlyList<string> DisplayEffects => Effects;
        public string DisplayEffect { get; private set; } = DisplayPresetNames.Default;
        public void SetDisplayEffect(string effect)
        {
            EffectCalls.Add(effect);
            DisplayEffect = Resolve(Effects, effect, nameof(effect));
        }
    }
}
