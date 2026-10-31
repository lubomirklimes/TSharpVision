// Palette inheritance of the dialog controls: the attributes they actually render on the NullDriver screen inside a
// gray TDialog, a blue/cyan/gray TWindow, a modal blue document dialog, a custom-palette window and nested groups;
// button states and shadows, the cells around them, redraw after resizing and moving, clipping at the frame, and
// keyboard and mouse activation of the TVDemo Control Palette Gallery.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using static TSharpVision.Tests.Dialogs.ComboInput;

namespace TSharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class ControlPaletteInheritanceTests : IDisposable
{
    // Standard color application palette roles (TProgram.cpColor).
    private const byte FaceNormal = 0x20, FaceDefault = 0x2B, FaceSelected = 0x2F, FaceDisabled = 0x78, Shortcut = 0x2E;
    private const byte GrayShadow = 0x70, BlueShadow = 0x10, CyanShadow = 0x30;
    private const byte DialogSurface = 0x7F, BlueSurface = 0x1F, CyanSurface = 0x3F, GraySurface = 0x7F;

    private readonly DriverScope _driver = new(80, 25);
    private readonly ComboApp _app;
    private readonly List<TView> _owned = new();

    public ControlPaletteInheritanceTests()
    {
        ResetQueue();
        _app = new ComboApp();
        TView.DisableCommand(PaletteGalleryCmd.Unavailable);
    }

    public void Dispose()
    {
        foreach (TView view in _owned) view.ShutDown();
        _app.ShutDown();
        ResetQueue();
        _driver.Dispose();
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
        TPoint p = At(view, x, y);
        return (byte)_app.Cell(p.x, p.y).Attr;
    }

    private char Char(TView view, int x, int y)
    {
        TPoint p = At(view, x, y);
        return _app.Cell(p.x, p.y).Character;
    }

    private static int Bg(byte attr) => attr >> 4;

    /// <summary>Normal, default and disabled buttons plus an input line that holds the focus.</summary>
    private sealed class Buttons
    {
        public readonly TInputLine Input = new(new TRect(2, 1, 20, 2), 20);
        public readonly TButton Normal = new(new TRect(2, 3, 12, 5), "~N~ormal", PaletteGalleryCmd.Apply, ButtonConstants.bfNormal);
        public readonly TButton Default = new(new TRect(13, 3, 23, 5), "~D~efault", Views.cmOK, ButtonConstants.bfDefault);
        public readonly TButton Disabled = new(new TRect(24, 3, 36, 5), "D~i~sabled", PaletteGalleryCmd.Unavailable, ButtonConstants.bfNormal);

        public Buttons(TGroup host)
        {
            host.Insert(Input);
            host.Insert(Normal);
            host.Insert(Default);
            host.Insert(Disabled);
            Input.Select();
        }
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

    // Every cell of the button outside its face has the parent's surface background.
    private void AssertNoForeignBackground(TButton button, int surfaceBg)
    {
        int s = button.size.x - 1;
        Assert.Equal(surfaceBg, Bg(Attr(button, 0, 0)));
        Assert.Equal(surfaceBg, Bg(Attr(button, s, 0)));
        for (int x = 0; x <= s; x++) Assert.Equal(surfaceBg, Bg(Attr(button, x, 1)));
    }

    private void AssertStandardButtons(Buttons b, byte shadow, byte surface)
    {
        AssertButton(b.Normal, FaceNormal, shadow);
        AssertButton(b.Default, FaceDefault, shadow);
        AssertButton(b.Disabled, FaceDisabled, shadow);
        Assert.Equal(Shortcut, Attr(b.Normal, 2, 0));
        Assert.Equal(FaceDisabled, Attr(b.Disabled, 2, 0));   // a disabled button has no shortcut highlight
        AssertSurroundedBy(b.Normal, surface);
        AssertSurroundedBy(b.Disabled, surface);
        Assert.Equal(surface, Attr(b.Normal, 10, 0));          // the one-cell gap between Normal and Default

        b.Normal.Select();
        AssertButton(b.Normal, FaceSelected, shadow);
        AssertButton(b.Default, FaceNormal, shadow);           // a focused button takes the default role
        b.Input.Select();
        AssertButton(b.Default, FaceDefault, shadow);
    }

    // ── A: gray dialog, unchanged classic appearance ───────────────────────

    [Fact]
    public void Gray_dialog_buttons_keep_the_classic_faces_and_gray_shadow()
    {
        var dialog = new TDialog(new TRect(0, 0, 40, 8), "Gray");
        var b = new Buttons(dialog);
        Show(dialog);
        b.Input.Select();

        AssertStandardButtons(b, GrayShadow, DialogSurface);
        Assert.Equal(0x1F, Attr(b.Input, 1, 0));                // input normal (TInputLine focused uses the same role)
        b.Normal.Select();
        Assert.Equal(0x1F, Attr(b.Input, 1, 0));
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

    // ── B: blue window ──────────────────────────────────────────────────────

    [Fact]
    public void Blue_window_buttons_keep_their_faces_and_cast_a_black_on_blue_shadow()
    {
        var window = new TWindow(new TRect(0, 0, 40, 8), "Blue", 0);
        var b = new Buttons(window);
        Show(window);
        b.Input.Select();

        AssertStandardButtons(b, BlueShadow, BlueSurface);
        foreach (TButton button in new[] { b.Normal, b.Default, b.Disabled })
            AssertNoForeignBackground(button, 1);
        Assert.Equal(0x70, Attr(b.Input, 1, 0));                // input normal: a gray field on the blue surface
        b.Normal.Select();
        Assert.Equal(0x70, Attr(b.Input, 1, 0));
    }

    [Theory]
    [InlineData(Views.wpCyanWindow, CyanShadow, CyanSurface, 0x1F, 0x70)]
    [InlineData(Views.wpGrayWindow, GrayShadow, GraySurface, 0x1F, 0x30)]
    public void Cyan_and_gray_windows_resolve_the_control_roles_for_their_surface(
        ushort selector, byte shadow, byte surface, byte inputNormal, byte clusterNormal)
    {
        var window = new TWindow(new TRect(0, 0, 40, 10), "W", 0) { palette = (short)selector };
        var b = new Buttons(window);
        var checks = new TCheckBoxes(new TRect(2, 6, 20, 7), new TSItem("~X~", null));
        window.Insert(checks);
        Show(window);
        b.Input.Select();

        AssertStandardButtons(b, shadow, surface);
        b.Normal.Select();
        Assert.Equal(inputNormal, Attr(b.Input, 1, 0));
        Assert.Equal(clusterNormal, Attr(checks, 0, 0));
    }

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
        Assert.Equal(0x1A, Attr(win, 3, 0));                   // close icon
        Assert.Equal(0x1E, Attr(scroller, 0, 0));
        Assert.Equal(0x1E, (byte)scroller.GetColor(1));
        Assert.Equal(0x71, (byte)scroller.GetColor(2));
    }

    [Fact]
    public void Blue_window_inputs_lists_clusters_combo_and_progress_resolve_without_error_attributes()
    {
        var window = new TWindow(new TRect(0, 0, 50, 14), "Blue", 0);
        var input = new TInputLine(new TRect(2, 1, 20, 2), 20);
        var checks = new TCheckBoxes(new TRect(2, 3, 20, 5), new TSItem("~A~", new TSItem("~B~", null)));
        var radio = new TRadioButtons(new TRect(22, 3, 40, 5), new TSItem("~C~", new TSItem("~D~", null)));
        var list = new TListBox(new TRect(2, 6, 20, 10), 1, null);
        var items = new TStringCollection();
        items.Insert("one"); items.Insert("two"); items.Insert("three");
        list.NewList(items);
        var combo = new TComboBox(new TRect(22, 1, 40, 2), new[] { "UTF-8", "UTF-16" });
        var progress = new TProgressBar(new TRect(22, 7, 46, 8)) { Value = 50, ShowPercentage = true };
        foreach (TView v in new TView[] { input, checks, radio, list, combo, progress }) window.Insert(v);
        Show(window);
        input.Select();

        foreach (TView v in new TView[] { input, checks, radio, list, combo, progress })
            for (int y = 0; y < v.size.y; y++)
                for (int x = 0; x < v.size.x; x++)
                    Assert.NotEqual(TView.errorAttr, Attr(v, x, y));

        Assert.Equal(0x70, Attr(input, 1, 0));
        Assert.Equal(0x30, Attr(checks, 0, 0));                // cluster normal
        Assert.Contains(Enumerable.Range(0, checks.size.x), x => Attr(checks, x, 0) == 0x3E); // cluster shortcut
        Assert.Equal(0x30, Attr(list, 0, 2));                  // list normal
        Assert.Equal(0x70, Attr(combo, 1, 0));                 // combo field: the input-normal role of the window
        Assert.Equal(1, Bg(Attr(window, 21, 1)));              // surface between the controls
        Assert.Equal(1, Bg(Attr(window, 45, 4)));
    }

    [Fact]
    public void Blue_window_combo_and_progress_match_their_gray_dialog_roles()
    {
        var dialog = Show(new TDialog(new TRect(0, 0, 40, 6), "Gray"));
        var window = Show(new TWindow(new TRect(0, 8, 40, 14), "Blue", 0));
        TComboBox Combo(TGroup host)
        {
            var c = new TComboBox(new TRect(2, 1, 20, 2), new[] { "UTF-8" });
            host.Insert(c);
            return c;
        }
        TProgressBar Bar(TGroup host)
        {
            var p = new TProgressBar(new TRect(2, 3, 30, 4)) { Value = 50 };
            host.Insert(p);
            return p;
        }
        TComboBox grayCombo = Combo(dialog), blueCombo = Combo(window);
        TProgressBar grayBar = Bar(dialog), blueBar = Bar(window);

        dialog.Select();
        Assert.Equal(0x2F, Attr(grayCombo, 1, 0));             // focused field: the input-selected role
        Assert.Equal(0x70, Attr(blueCombo, 1, 0));             // blue window: the window's input-normal role
        window.Select();
        Assert.Equal(0x1F, Attr(grayCombo, 1, 0));             // gray dialog: input normal, unchanged
        Assert.Equal(0x2F, Attr(blueCombo, 1, 0));
        Assert.Equal(Attr(grayCombo, grayCombo.size.x - 1, 0), Attr(blueCombo, blueCombo.size.x - 1, 0)); // arrow
        Assert.Equal(dialog.MapColor(9), Attr(grayBar, 0, 0)); // gray dialog: highlighted text
        Assert.Equal(window.MapColor(9), Attr(blueBar, 0, 0)); // blue window: highlighted text
        Assert.Equal(dialog.MapColor(7), Attr(grayBar, 27, 0));
        Assert.Equal(window.MapColor(7), Attr(blueBar, 27, 0));
        Assert.NotEqual(Attr(grayBar, 0, 0), Attr(grayBar, 27, 0));
        Assert.NotEqual(Attr(blueBar, 0, 0), Attr(blueBar, 27, 0));
    }

    // ── C: modal blue document window ───────────────────────────────────────

    private ushort RunModal(BlueDocumentDialog dialog)
    {
        ushort result = _app.DeskTop!.ExecView(dialog);
        Assert.False(_app.TimedOut, "the script did not close the modal window");
        Assert.Equal(0, _app.StepsLeft);
        dialog.ShutDown();
        return result;
    }

    [Fact]
    public void Modal_blue_document_window_keeps_its_palette_and_Enter_accepts()
    {
        var dialog = new BlueDocumentDialog();
        _app.Step(() =>
        {
            Assert.True(dialog.GetState(Views.sfModal));
            dialog.List.Select();
            AssertButton(dialog.Default, FaceDefault, BlueShadow);
            AssertButton(dialog.Disabled, FaceDisabled, BlueShadow);
            AssertButton(dialog.Cancel, FaceNormal, BlueShadow);
            AssertSurroundedBy(dialog.Cancel, BlueSurface);
            TView label = Label(dialog);
            Assert.Equal(0x17, Attr(label, 2, 0));             // label normal
            Assert.Equal(0x1E, Attr(label, 1, 0));             // label shortcut
            Assert.Equal(0x70, Attr(dialog.Find, 1, 0));
            Assert.Equal(0x2F, Attr(dialog.List, 0, 0));       // focused list item
            dialog.Find.Select();
            Assert.Equal(0x1F, Attr(label, 2, 0));             // label of the focused control
            Send(Key(Keys.kbEnter));
        });
        Assert.Equal(Views.cmOK, RunModal(dialog));
    }

    [Fact]
    public void Modal_blue_document_window_cancels_with_Esc_and_ignores_its_disabled_button()
    {
        var dialog = new BlueDocumentDialog();
        _app.Step(() => Click(At(dialog.Disabled, 3, 0)));
        _app.Step(() =>
        {
            Assert.True(dialog.GetState(Views.sfModal));       // the disabled button did nothing
            Send(Key(Keys.kbEsc));
        });
        Assert.Equal(Views.cmCancel, RunModal(dialog));
    }

    [Fact]
    public void Modal_blue_document_window_closes_from_its_close_icon_and_mouse_presses_OK()
    {
        var first = new BlueDocumentDialog();
        _app.Step(() => Click(At(first, 3, 0)));
        Assert.Equal(Views.cmCancel, RunModal(first));

        var second = new BlueDocumentDialog();
        _app.Step(() => Click(At(second.Default, 3, 0)));
        Assert.Equal(Views.cmOK, RunModal(second));
    }

    private static TView Label(TGroup group)
    {
        TView? found = null;
        group.ForEachView(v => { if (v is TLabel) found = v; });
        return found!;
    }

    // ── D: custom palette ───────────────────────────────────────────────────

    [Fact]
    public void Custom_palette_window_controls_follow_its_logical_roles()
    {
        var gallery = TVDemoApp.BuildPaletteGallery();
        _owned.Add(gallery.Gray);
        _owned.Add(gallery.Blue);
        var window = Show(gallery.Custom);
        PaletteGallerySet set = window.Set;
        set.Input.Select();

        AssertButton(set.Default, FaceDefault, CyanShadow);
        AssertButton(set.Disabled, FaceDisabled, CyanShadow);
        AssertSurroundedBy(set.Normal, CyanSurface);
        Assert.Equal(0x2F, Attr(set.Input, 1, 0));
        TView label = Label(window);
        Assert.Equal(0x3F, Attr(label, 2, 0));                 // label of the focused input: label selected
        set.List.Select();
        Assert.Equal(0x37, Attr(label, 2, 0));                 // label normal
        Assert.Equal(0x1F, Attr(set.Input, 1, 0));
        Assert.Equal(0x70, Attr(set.Checks, 0, 0));
        Assert.Equal(0x3E, Attr(window, 2, 1));                // static text
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
            if (surfaceRole != 0) data[1] = surfaceRole;
            _palette = new TPalette(data);
            Insert(new TView(GetExtent()));                     // the panel's background, painted with entry 1
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

        AssertStandardButtons(b, shadow, background);
        Assert.Equal(background, Attr(group, 0, 0));
        Assert.Equal(background, Attr(group, 41, 9));
    }

    [Fact]
    public void Nested_panel_with_its_own_surface_role_casts_shadows_on_that_surface()
    {
        var dialog = new TDialog(new TRect(0, 0, 44, 12), "D");
        var panel = new Panel(new TRect(1, 1, 43, 11), 19);     // dialog 19 = input normal: white on blue
        dialog.Insert(panel);
        var b = new Buttons(panel);
        Show(dialog);
        b.Input.Select();

        Assert.Equal(0x1F, Attr(panel, 0, 0));
        AssertButton(b.Normal, FaceNormal, BlueShadow);
        AssertButton(b.Default, FaceDefault, BlueShadow);
        AssertSurroundedBy(b.Normal, 0x1F);
        Assert.Equal(0x1F, Attr(b.Input, 1, 0));                // the input keeps the dialog's input role
    }

    // ── redraw, resizing, moving, clipping ──────────────────────────────────

    [Fact]
    public void Resized_blue_gallery_window_moves_its_bottom_buttons_and_repaints_the_old_cells()
    {
        var gallery = TVDemoApp.BuildPaletteGallery();
        _owned.Add(gallery.Gray);
        _owned.Add(gallery.Custom);
        var window = Show(gallery.Blue);
        window.MoveTo(0, 0);
        PaletteGallerySet set = window.Set;
        TPoint oldCorner = At(set.CloseButton, 0, 1);

        TRect r = window.GetBounds();
        r.b.x += 10;
        r.b.y += 4;
        window.Locate(r);

        Assert.Equal(new TPoint(34, 17), set.CloseButton.GetBounds().a);
        foreach (TButton button in new[] { set.Default, set.Normal, set.CloseButton, window.ModalButton })
            AssertNoForeignBackground(button, 1);
        AssertButton(set.Disabled, FaceDisabled, BlueShadow);
        Assert.Equal(BlueSurface, (byte)_app.Cell(oldCorner.x, oldCorner.y).Attr);
        Assert.Equal(' ', _app.Cell(oldCorner.x, oldCorner.y).Character);
        Assert.Equal(34, set.Progress.size.x);                 // grew with the window
        Assert.Equal(8, set.List.size.y);
    }

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
        AssertStandardButtons(b, BlueShadow, BlueSurface);
    }

    [Fact]
    public void Buttons_next_to_the_frame_are_clipped_by_the_window_and_leave_the_frame_intact()
    {
        var window = new TWindow(new TRect(0, 0, 30, 8), "Blue", 0);
        var bottom = new TButton(new TRect(2, 5, 12, 7), "~B~ottom", PaletteGalleryCmd.Apply, ButtonConstants.bfNormal);
        var overhang = new TButton(new TRect(24, 2, 34, 4), "~O~ver", PaletteGalleryCmd.Apply, ButtonConstants.bfNormal);
        window.Insert(bottom);
        window.Insert(overhang);
        Show(window);
        overhang.Select();
        byte desktop = (byte)_app.Cell(79, 22).Attr;

        AssertButton(bottom, FaceNormal, BlueShadow);
        for (int x = 1; x < 27; x++)                            // 27-29 hold the resize grip
        {
            Assert.Equal('═', Char(window, x, 7));        // the bottom frame row stays a frame
            Assert.Equal(1, Bg(Attr(window, x, 7)));
        }
        // The overhanging button paints inside the window extent only; the window's own shadow follows.
        Assert.Equal(FaceSelected, Attr(overhang, 1, 0));
        TPoint outside = At(window, 32, 2);
        Assert.NotEqual(FaceSelected, (byte)_app.Cell(outside.x, outside.y).Attr);
        Assert.Equal(desktop, (byte)_app.Cell(40, 2).Attr);
    }

    // ── TVDemo gallery: keyboard and mouse ──────────────────────────────────

    [Fact]
    public void Gallery_windows_render_each_parent_with_its_own_shadow()
    {
        var gallery = TVDemoApp.BuildPaletteGallery();
        foreach ((TWindow window, PaletteGallerySet set, byte shadow) in new (TWindow, PaletteGallerySet, byte)[]
                 {
                     (gallery.Gray, gallery.Gray.Set, GrayShadow),
                     (gallery.Blue, gallery.Blue.Set, BlueShadow),
                     (gallery.Custom, gallery.Custom.Set, CyanShadow),
                 })
        {
            Show(window);
            window.MoveTo(0, 0);
            set.Input.Select();
            AssertButton(set.Default, FaceDefault, shadow);
            AssertButton(set.Normal, FaceNormal, shadow);
            AssertButton(set.Disabled, FaceDisabled, shadow);
            AssertButton(set.CloseButton, FaceNormal, shadow);
            Assert.True(set.Disabled.GetState(Views.sfDisabled));
            for (int y = 0; y < window.size.y; y++)
                for (int x = 0; x < window.size.x; x++)
                    Assert.NotEqual(TView.errorAttr, Attr(window, x, y));
            window.Hide();
        }
    }

    [Fact]
    public void Gallery_blue_window_buttons_respond_to_keyboard_and_mouse_and_Close_closes_only_the_window()
    {
        var gallery = TVDemoApp.BuildPaletteGallery();
        _owned.Add(gallery.Gray);
        _owned.Add(gallery.Custom);
        BlueGalleryWindow window = gallery.Blue;
        _app.DeskTop!.Insert(window);
        PaletteGallerySet set = window.Set;

        _app.Step(() => Click(At(set.Normal, 3, 0)));
        _app.Step(() =>
        {
            Assert.Equal("Apply pressed", set.LastAction);
            Assert.Equal(60, set.Progress.Value);
            AssertButton(set.Normal, FaceNormal, BlueShadow);    // redrawn released, still on the blue surface
            set.Default.Select();
            Send(Key(Keys.kbEnter));
        });
        _app.Step(() =>
        {
            Assert.Equal("OK pressed", set.LastAction);
            Send(Key(Keys.kbTab));
        });
        _app.Step(() =>
        {
            Assert.NotSame(set.Default, window.current);
            Send(Key(Keys.kbShiftTab));
        });
        _app.Step(() =>
        {
            Assert.Same(set.Default, window.current);
            Send(Key(' '));
        });
        _app.Step(() =>
        {
            Assert.Equal("OK pressed", set.LastAction);
            set.Report("-");
            Click(At(set.Disabled, 3, 0));
        });
        _app.Step(() =>
        {
            Assert.Equal("-", set.LastAction);
            Click(At(set.CloseButton, 3, 0));
        });
        _app.Step(() =>
        {
            Assert.Null(window.owner);
            _app.EndModal(Views.cmQuit);
        });

        _app.Execute();
        Assert.Equal(0, _app.StepsLeft);
        Assert.False(_app.TimedOut);
    }

    [Fact]
    public void Gallery_blue_window_runs_the_modal_window_and_reports_its_result()
    {
        var gallery = TVDemoApp.BuildPaletteGallery();
        _owned.Add(gallery.Gray);
        _owned.Add(gallery.Custom);
        BlueGalleryWindow window = gallery.Blue;
        _owned.Add(window);
        _app.DeskTop!.Insert(window);

        _app.Step(() => Click(At(window.ModalButton, 3, 0)));
        _app.Step(() =>
        {
            BlueDocumentDialog? modal = null;
            _app.DeskTop!.ForEachView(v => { if (v is BlueDocumentDialog d) modal = d; });
            Assert.NotNull(modal);
            Assert.True(modal!.GetState(Views.sfModal));
            AssertButton(modal.Default, modal.Default.GetState(Views.sfSelected) ? FaceSelected : FaceDefault, BlueShadow);
            Send(Key(Keys.kbEsc));
        });
        _app.Step(() =>
        {
            Assert.Equal("Modal: cancelled", window.Set.LastAction);
            Assert.Same(_app.DeskTop, window.owner);
            _app.EndModal(Views.cmQuit);
        });

        _app.Execute();
        Assert.Equal(0, _app.StepsLeft);
        Assert.False(_app.TimedOut);
    }
}

[Collection("NonParallel")]
public sealed class ControlPaletteGalleryMenuTests
{
    [Fact]
    public void Gallery_command_opens_three_parents_without_closing_the_application()
    {
        using var driver = new DriverScope();
        using var registry = new StreamableRegistryScope();
        var app = new TVDemoApp();
        try
        {
            var ev = new TEvent { What = Events.evCommand };
            ev.message.command = ShowcaseCmd.PaletteGallery;
            app.HandleEvent(ref ev);
            Assert.Equal(Events.evNothing, ev.What);
            var titles = new List<string?>();
            app.DeskTop!.ForEachView(v => { if (v is TWindow w) titles.Add(w.title); });
            Assert.Contains("Gray TDialog", titles);
            Assert.Contains("Blue TWindow", titles);
            Assert.Contains("Custom palette", titles);
        }
        finally
        {
            app.ShutDown();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
