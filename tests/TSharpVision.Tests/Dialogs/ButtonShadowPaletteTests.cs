// TButton shadow cells and the window palettes: the attributes a button actually renders on the NullDriver screen
// inside a gray TDialog and a blue/cyan/gray TWindow, under every application palette. The shadow is cast on the
// owner's surface, so its background must match the cells around the button while the face roles stay unchanged.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class ButtonShadowPaletteTests : IDisposable
{
    private const ushort cmApply = 3001, cmUnavailable = 3002;

    // Standard color application palette (TProgram.cpColor), used only by the classic-appearance anchor tests.
    private const byte FaceNormal = 0x20, FaceDefault = 0x2B, FaceSelected = 0x2F, FaceDisabled = 0x78, Shortcut = 0x2E;
    private const byte GrayShadow = 0x70, BlueShadow = 0x10, CyanShadow = 0x30;

    private readonly DriverScope _driver = new(80, 25);
    private readonly ShadowApp _app;
    private readonly List<TView> _owned = new();

    public ButtonShadowPaletteTests()
    {
        _app = new ShadowApp();
        TView.DisableCommand(cmUnavailable);
    }

    public void Dispose()
    {
        foreach (TView view in _owned) view.ShutDown();
        _app.ShutDown();
        TView.EnableCommand(cmUnavailable);
        _driver.Dispose();
    }

    private sealed class ShadowApp : TProgram
    {
        public ShadowApp()
        {
            Pending = default;
        }

        public TScreenChar Cell(int x, int y) => buffer!.Data[y * size.x + x];
    }

    private T Show<T>(T window) where T : TWindow
    {
        _owned.Add(window);
        _app.DeskTop!.Insert(window);
        window.Select();
        return window;
    }

    private byte Attr(TView view, int x, int y)
    {
        TPoint p = view.MakeGlobal(new TPoint(x, y));
        return (byte)_app.Cell(p.x, p.y).Attr;
    }

    private char Char(TView view, int x, int y)
    {
        TPoint p = view.MakeGlobal(new TPoint(x, y));
        return _app.Cell(p.x, p.y).Character;
    }

    private static int Bg(byte attr) => attr >> 4;
    private static int Fg(byte attr) => attr & Colors.fgMask;

    /// <summary>Normal, default and disabled buttons plus an input line that holds the focus.</summary>
    private sealed class Buttons
    {
        public readonly TInputLine Input = new(new TRect(2, 1, 20, 2), 20);
        public readonly TButton Normal = new(new TRect(2, 3, 12, 5), "~N~ormal", cmApply, ButtonConstants.bfNormal);
        public readonly TButton Default = new(new TRect(13, 3, 23, 5), "~D~efault", Views.cmOK, ButtonConstants.bfDefault);
        public readonly TButton Disabled = new(new TRect(24, 3, 36, 5), "D~i~sabled", cmUnavailable, ButtonConstants.bfNormal);

        public Buttons(TGroup host)
        {
            host.Insert(Input);
            host.Insert(Normal);
            host.Insert(Default);
            host.Insert(Disabled);
            Input.Select();
        }

        public TButton[] All => new[] { Normal, Default, Disabled };
    }

    // Face at row 0 (cell 1 is a blank before the title), shadow at the left blank, the right ▄ and the whole bottom row.
    private void AssertButton(TButton button, byte face, byte shadow)
    {
        int s = button.size.x - 1;
        Assert.Equal(face, Attr(button, 1, 0));
        Assert.Equal(shadow, Attr(button, 0, 0));
        Assert.Equal(shadow, Attr(button, s, 0));
        Assert.Equal('▄', Char(button, s, 0));
        for (int x = 0; x <= s; x++)
            Assert.Equal(shadow, Attr(button, x, 1));
        Assert.Equal('▀', Char(button, s, 1));
    }

    // The cells just outside a button are the parent's surface.
    private void AssertSurroundedBy(TButton button, byte surface)
    {
        int w = button.size.x, h = button.size.y;
        for (int y = -1; y <= h; y++)
        {
            Assert.Equal(surface, Attr(button, -1, y));
            Assert.Equal(surface, Attr(button, w, y));
        }
        for (int x = 0; x < w; x++)
        {
            Assert.Equal(surface, Attr(button, x, -1));
            Assert.Equal(surface, Attr(button, x, h));
        }
    }

    // Every non-face cell of the button: the shadow role's foreground over the background of the surface around it.
    private void AssertShadowOnSurface(TButton button, TGroup owner)
    {
        int s = button.size.x - 1;
        int surfaceBg = Bg(Attr(button, -1, 0));
        int shadowFg = Fg(owner.MapColor(15));                  // dialog role 15 = button shadow
        foreach ((int x, int y) in ShadowCells(button))
        {
            byte attr = Attr(button, x, y);
            Assert.Equal(surfaceBg, Bg(attr));
            Assert.Equal(shadowFg, Fg(attr));
        }
        Assert.Equal('▄', Char(button, s, 0));
        Assert.Equal('▀', Char(button, s, 1));
    }

    private static IEnumerable<(int x, int y)> ShadowCells(TButton button)
    {
        int s = button.size.x - 1;
        yield return (0, 0);
        yield return (s, 0);
        for (int x = 0; x <= s; x++) yield return (x, 1);
    }

    private static TWindow NewParent(ushort? windowPalette)
    {
        if (windowPalette is null)
            return new TDialog(new TRect(0, 0, 40, 8), "Dialog");
        return new TWindow(new TRect(0, 0, 40, 8), "Window", 0) { palette = (short)windowPalette.Value };
    }

    public static IEnumerable<object?[]> Parents()
    {
        foreach (TProgram.AP ap in new[] { TProgram.AP.Color, TProgram.AP.BlackWhite, TProgram.AP.Monochrome })
            foreach (ushort? selector in new ushort?[] { null, Views.wpBlueWindow, Views.wpCyanWindow, Views.wpGrayWindow })
            {
                // Monochrome blue/cyan windows compose a black shadow on a black surface (0x00), which
                // TDrawBuffer.moveChar treats as "keep the cell's attribute"; that case is not covered here.
                if (ap == TProgram.AP.Monochrome && selector is Views.wpBlueWindow or Views.wpCyanWindow)
                    continue;
                yield return new object?[] { ap, selector };
            }
    }

    // ── shadow follows the parent surface, resolved through the palette chain ──

    [Theory]
    [MemberData(nameof(Parents))]
    public void Button_shadow_cells_take_the_parent_surface_background_in_every_state(TProgram.AP ap, ushort? windowPalette)
    {
        _app.AppPalette = ap;
        TWindow parent = NewParent(windowPalette);
        var b = new Buttons(parent);
        Show(parent);
        b.Input.Select();

        foreach (TButton button in b.All)
            AssertShadowOnSurface(button, parent);
        Assert.Equal((byte)b.Normal.GetColor(0x0501), Attr(b.Normal, 1, 0));
        Assert.Equal((byte)b.Default.GetColor(0x0602), Attr(b.Default, 1, 0));
        Assert.Equal((byte)b.Disabled.GetColor(0x0404), Attr(b.Disabled, 1, 0));

        b.Normal.Select();                                       // focused button
        Assert.Equal((byte)b.Normal.GetColor(0x0703), Attr(b.Normal, 1, 0));
        foreach (TButton button in b.All)
            AssertShadowOnSurface(button, parent);
    }

    [Theory]
    [InlineData(TProgram.AP.Color)]
    [InlineData(TProgram.AP.BlackWhite)]
    [InlineData(TProgram.AP.Monochrome)]
    public void Gray_dialog_shadow_is_identical_to_the_shadow_role_attribute(TProgram.AP ap)
    {
        // In a standard dialog the shadow role and the surface share their background, so composing the shadow on the
        // surface reproduces the classic attribute byte for byte.
        _app.AppPalette = ap;
        var dialog = new TDialog(new TRect(0, 0, 40, 8), "Gray");
        var b = new Buttons(dialog);
        Show(dialog);

        byte classic = dialog.MapColor(15);
        foreach (TButton button in b.All)
            foreach ((int x, int y) in ShadowCells(button))
                Assert.Equal(classic, Attr(button, x, y));
    }

    // ── classic appearance with the standard color palette ────────────────

    [Theory]
    [InlineData(null, GrayShadow)]
    [InlineData(Views.wpBlueWindow, BlueShadow)]
    [InlineData(Views.wpCyanWindow, CyanShadow)]
    [InlineData(Views.wpGrayWindow, GrayShadow)]
    public void Standard_color_palette_keeps_the_faces_and_casts_a_black_shadow_on_the_surface(ushort? windowPalette, byte shadow)
    {
        TWindow parent = NewParent(windowPalette);
        var b = new Buttons(parent);
        Show(parent);
        b.Input.Select();
        byte surface = Attr(b.Normal, -1, 0);

        AssertButton(b.Normal, FaceNormal, shadow);
        AssertButton(b.Default, FaceDefault, shadow);
        AssertButton(b.Disabled, FaceDisabled, shadow);
        Assert.Equal(Shortcut, Attr(b.Normal, 2, 0));
        Assert.Equal(FaceDisabled, Attr(b.Disabled, 2, 0));    // a disabled button has no shortcut highlight
        AssertSurroundedBy(b.Normal, surface);
        AssertSurroundedBy(b.Disabled, surface);
        Assert.Equal(surface, Attr(b.Normal, 10, 0));           // the one-cell gap between Normal and Default

        b.Normal.Select();
        AssertButton(b.Normal, FaceSelected, shadow);
        AssertButton(b.Default, FaceNormal, shadow);            // a focused button takes the default role
        b.Input.Select();
        AssertButton(b.Default, FaceDefault, shadow);
    }

    [Fact]
    public void Blue_window_surface_around_and_under_the_buttons_is_blue()
    {
        var window = new TWindow(new TRect(0, 0, 40, 8), "Blue", 0);
        var b = new Buttons(window);
        Show(window);

        Assert.Equal(1, Bg(Attr(b.Normal, -1, 0)));
        foreach (TButton button in b.All)
            foreach ((int x, int y) in ShadowCells(button))
                Assert.Equal(1, Bg(Attr(button, x, y)));
    }

    [Fact]
    public void Pressed_button_shifts_its_face_and_keeps_the_parent_surface_around_it()
    {
        var dialog = new TDialog(new TRect(0, 0, 40, 8), "Gray");
        var window = new TWindow(new TRect(0, 10, 40, 18), "Blue", 0);
        var gray = new Buttons(dialog);
        var blue = new Buttons(window);
        Show(dialog);
        Show(window);

        foreach ((TButton button, byte shadow) in new[] { (gray.Normal, GrayShadow), (blue.Normal, BlueShadow) })
        {
            button.DrawState(true);
            Assert.Equal(shadow, Attr(button, 0, 0));
            Assert.Equal(shadow, Attr(button, 1, 0));
            Assert.Equal(FaceNormal, Attr(button, 2, 0));
            Assert.Equal(FaceNormal, Attr(button, button.size.x - 1, 0));
            for (int x = 0; x < button.size.x; x++)
            {
                Assert.Equal(shadow, Attr(button, x, 1));
                Assert.Equal(' ', Char(button, x, 1));
            }
            button.DrawState(false);
            AssertButton(button, FaceNormal, shadow);
        }
    }

    // ── window palettes ─────────────────────────────────────────────────────

    [Fact]
    public void Window_roles_one_to_eight_are_unchanged()
    {
        string[] classic = { "\x08\x09\x0A\x0B\x0C\x0D\x0E\x0F", "\x10\x11\x12\x13\x14\x15\x16\x17", "\x18\x19\x1A\x1B\x1C\x1D\x1E\x1F" };
        ushort[] selectors = { Views.wpBlueWindow, Views.wpCyanWindow, Views.wpGrayWindow };
        for (int i = 0; i < 3; i++)
        {
            var window = new TWindow(new TRect(0, 0, 20, 6), "W", 0) { palette = (short)selectors[i] };
            TPalette p = window.GetPalette();
            Assert.Equal(32, p.Size);
            for (int e = 1; e <= 8; e++) Assert.Equal((byte)classic[i][e - 1], p[e]);
            window.ShutDown();
        }

        // A scroller and the frame draw exactly as before.
        var win = new TWindow(new TRect(0, 0, 30, 8), "Doc", 1);
        var scroller = new TScroller(new TRect(1, 1, 29, 7), null, null);
        win.Insert(scroller);
        Show(win);
        Assert.Equal(0x1F, Attr(win, 0, 0));
        Assert.Equal(0x1A, Attr(win, 3, 0));                    // close icon
        Assert.Equal(0x1E, Attr(scroller, 0, 0));
        Assert.Equal(0x1E, (byte)scroller.GetColor(1));
        Assert.Equal(0x71, (byte)scroller.GetColor(2));
    }

    [Theory]
    [InlineData(Views.wpBlueWindow)]
    [InlineData(Views.wpCyanWindow)]
    [InlineData(Views.wpGrayWindow)]
    public void Dialog_control_roles_resolve_inside_a_plain_window_without_the_error_attribute(ushort selector)
    {
        var window = new TWindow(new TRect(0, 0, 40, 10), "W", 0) { palette = (short)selector };
        var b = new Buttons(window);
        var checks = new TCheckBoxes(new TRect(2, 6, 20, 7), new TSItem("~X~", null));
        window.Insert(checks);
        Show(window);

        // Role 32 is reserved and maps to application entry 63 (0x00), exactly as in TDialog.
        for (int role = 9; role <= 31; role++)
            Assert.NotEqual(TView.errorAttr, window.MapColor(role));
        var dialog = new TDialog(new TRect(0, 0, 20, 6), "D");
        Assert.Equal(dialog.GetPalette()[32], window.GetPalette()[32]);
        dialog.ShutDown();
        Assert.NotEqual(TView.errorAttr, Attr(b.Input, 1, 0));
        Assert.NotEqual(TView.errorAttr, Attr(checks, 0, 0));
        foreach (TButton button in b.All)
            Assert.NotEqual(TView.errorAttr, Attr(button, 1, 0));
    }

    // ── nested containers ───────────────────────────────────────────────────

    // A surface panel: palette entry 1 is remapped (to the owner's input-normal role), everything else passes through.
    private sealed class Panel : TGroup
    {
        private readonly TPalette _palette;

        public Panel(TRect bounds, byte surfaceRole) : base(bounds)
        {
            var data = new byte[33];
            data[0] = 32;
            for (int i = 1; i <= 32; i++) data[i] = (byte)i;
            data[1] = surfaceRole;
            _palette = new TPalette(data);
            Insert(new TView(GetExtent()));                      // the panel's background, painted with entry 1
        }

        public override TPalette GetPalette() => _palette;
    }

    [Theory]
    [InlineData(false, GrayShadow, 0x70)]
    [InlineData(true, BlueShadow, 0x17)]
    public void Nested_group_translates_through_its_window_without_resetting_to_dialog_gray(
        bool blueWindow, byte shadow, byte background)
    {
        TWindow window = blueWindow ? new TWindow(new TRect(0, 0, 44, 12), "W", 0) : new TDialog(new TRect(0, 0, 44, 12), "D");
        var group = new TGroup(new TRect(1, 1, 43, 11));
        group.Insert(new TView(group.GetExtent()));
        window.Insert(group);
        var b = new Buttons(group);
        Show(window);

        AssertButton(b.Normal, FaceNormal, shadow);
        AssertButton(b.Disabled, FaceDisabled, shadow);
        AssertSurroundedBy(b.Normal, background);
        Assert.Equal(background, Attr(group, 0, 0));
        Assert.Equal(background, Attr(group, 41, 9));
    }

    [Fact]
    public void Nested_panel_with_its_own_surface_role_casts_shadows_on_that_surface()
    {
        var dialog = new TDialog(new TRect(0, 0, 44, 12), "D");
        var panel = new Panel(new TRect(1, 1, 43, 11), 19);      // dialog 19 = input normal: white on blue
        dialog.Insert(panel);
        var b = new Buttons(panel);
        Show(dialog);
        b.Input.Select();

        Assert.Equal(0x1F, Attr(panel, 0, 0));
        AssertButton(b.Normal, FaceNormal, BlueShadow);
        AssertButton(b.Default, FaceDefault, BlueShadow);
        AssertSurroundedBy(b.Normal, 0x1F);
        Assert.Equal(0x1F, Attr(b.Input, 1, 0));                 // the input keeps the dialog's input role
    }

    // ── geometry, clipping, window shadow ──────────────────────────────────

    [Fact]
    public void Moving_a_blue_window_leaves_no_stray_cells_and_keeps_the_button_attributes()
    {
        var window = new TWindow(new TRect(0, 0, 40, 8), "Blue", 0);
        var b = new Buttons(window);
        Show(window);
        b.Input.Select();
        byte desktop = (byte)_app.Cell(79, 22).Attr;

        window.MoveTo(30, 10);

        for (int y = 1; y < 10; y++)
            for (int x = 0; x < 30; x++)
                Assert.Equal(desktop, (byte)_app.Cell(x, y).Attr);
        AssertButton(b.Normal, FaceNormal, BlueShadow);
        AssertButton(b.Default, FaceDefault, BlueShadow);
    }

    [Fact]
    public void Buttons_next_to_the_frame_are_clipped_by_the_window_and_leave_the_frame_intact()
    {
        var window = new TWindow(new TRect(0, 0, 30, 8), "Blue", 0);
        var bottom = new TButton(new TRect(2, 5, 12, 7), "~B~ottom", cmApply, ButtonConstants.bfNormal);
        var overhang = new TButton(new TRect(24, 2, 34, 4), "~O~ver", cmApply, ButtonConstants.bfNormal);
        window.Insert(bottom);
        window.Insert(overhang);
        Show(window);
        overhang.Select();
        byte desktop = (byte)_app.Cell(79, 22).Attr;

        AssertButton(bottom, FaceNormal, BlueShadow);
        for (int x = 1; x < 27; x++)                             // 27-29 hold the resize grip
        {
            Assert.Equal('═', Char(window, x, 7));               // the bottom frame row stays a frame
            Assert.Equal(1, Bg(Attr(window, x, 7)));
        }
        // The overhanging button paints inside the window extent only; the window's own shadow follows.
        Assert.Equal(FaceSelected, Attr(overhang, 1, 0));
        TPoint outside = window.MakeGlobal(new TPoint(32, 2));
        Assert.NotEqual(FaceSelected, (byte)_app.Cell(outside.x, outside.y).Attr);
        Assert.Equal(desktop, (byte)_app.Cell(40, 2).Attr);
    }
}
