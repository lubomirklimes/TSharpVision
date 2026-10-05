using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class KeyboardDiagnosticsTests
{
    private const KeyboardCapabilities Full = KeyboardCapabilities.KeyReleaseEvents
        | KeyboardCapabilities.StandaloneModifierTransitions | KeyboardCapabilities.DistinctNumericKeypad;
    private static KeyboardDiagnosticsView.KeyCell Cell(IEnumerable<KeyboardDiagnosticsView.KeyCell> cells, string label)
        => cells.First(c => c.Label == label && c.Group != KeyboardDiagnosticsView.KeyGroup.Lock);

    [Fact]
    public void KeypadCapabilityShowsCompleteNumpadWithExplicitIdentities()
    {
        var view = new KeyboardDiagnosticsView(Full);
        try
        {
            Assert.True(view.ShowsKeypad);
            var keypad = view.Cells.Where(c => c.Group == KeyboardDiagnosticsView.KeyGroup.Keypad).ToArray();
            Assert.Equal(17, keypad.Length);
            Assert.All(keypad, c => Assert.True(c.X >= KeyboardDiagnosticsView.KeypadX));
            var fields = typeof(Keys).GetFields().Where(f => f.Name.StartsWith("kbKeypad", StringComparison.Ordinal)
                || f.Name is "kbNumLock" or "kbGrayMinus" or "kbGrayPlus").ToArray();
            Assert.Equal(17, fields.Length);
            foreach (var field in fields)
            {
                ushort code = (ushort)field.GetRawConstantValue()!;
                string label = KeyboardDiagnosticsState.MapKey(code)!;
                var cells = keypad.Where(c => c.Label == label).ToArray();
                // Every key, including the tall + and Enter, is one cell with one caption.
                Assert.Single(cells);
                Assert.Equal(label is "KP+" or "KPEnter" ? 3 : 1, cells[0].Height);
                view.Model.Record(Key(Events.evKeyDown, code), Full);
                Assert.All(cells, c => Assert.Equal(KeyboardDiagnosticsView.PaletteRole.HeldKey, view.CellRole(c)));
                view.Model.Record(Key(Events.evKeyUp, code), Full);
            }
            // Standard grid: Num / * - over 7 8 9, 4 5 6, 1 2 3 and a double-width 0.
            string[][] rows = { new[] { "NumLock", "KPDivide", "KPMultiply", "KP-" }, new[] { "KP7", "KP8", "KP9", "KP+" },
                new[] { "KP4", "KP5", "KP6" }, new[] { "KP1", "KP2", "KP3", "KPEnter" }, new[] { "KP0", "KPDecimal" } };
            for (int row = 0; row < rows.Length; row++)
                Assert.Equal(rows[row], keypad.Where(c => c.Y == 2 + row * 2).OrderBy(c => c.X).Select(c => c.Label));
            Assert.True(Cell(keypad, "KP0").Width > 2 * Cell(keypad, "KP1").Width);
            Assert.Equal(Cell(keypad, "KP3").X, Cell(keypad, "KPDecimal").X);
            // The tall keys sit in the right column beside the two rows they span.
            Assert.Equal(Cell(keypad, "KP-").X, Cell(keypad, "KP+").X);
            Assert.Equal(Cell(keypad, "KP+").X, Cell(keypad, "KPEnter").X);
            Assert.Equal(Cell(keypad, "KP6").Y, Cell(keypad, "KP+").Y + Cell(keypad, "KP+").Height - 1);
            Assert.Equal(Cell(keypad, "KPDecimal").Y, Cell(keypad, "KPEnter").Y + Cell(keypad, "KPEnter").Height - 1);
        }
        finally { view.ShutDown(); }
    }

    [Fact]
    public void MainKeyAndKeypadHighlightIndependently()
    {
        var view = new KeyboardDiagnosticsView(Full);
        try
        {
            var main1 = Cell(view.Cells, "1");
            var kp1 = Cell(view.Cells, "KP1");
            var enter = Cell(view.Cells, "Enter");
            var kpEnter = Cell(view.Cells, "KPEnter");
            var alias = Key(Events.evKeyDown, '1', Keys.kbNumState);
            alias.keyDown.text = "1";
            view.Model.Record(alias, Full);
            Assert.Equal(KeyboardDiagnosticsView.PaletteRole.HeldKey, view.CellRole(main1));
            Assert.Equal(KeyboardDiagnosticsView.PaletteRole.NormalKey, view.CellRole(kp1));
            Assert.True(view.Model.ModifierActive(Keys.kbNumState));
            view.Model.Record(Key(Events.evKeyUp, '1'), Full);
            view.Model.Record(Key(Events.evKeyDown, Keys.kbKeypadEnter), Full);
            Assert.Equal(KeyboardDiagnosticsView.PaletteRole.HeldKey, view.CellRole(kpEnter));
            Assert.Equal(KeyboardDiagnosticsView.PaletteRole.NormalKey, view.CellRole(enter));
            view.Model.Record(Key(Events.evKeyUp, Keys.kbKeypadEnter), Full);
            view.Model.Record(Key(Events.evKeyDown, Keys.kbEnter), Full);
            Assert.Equal(KeyboardDiagnosticsView.PaletteRole.HeldKey, view.CellRole(enter));
            Assert.Equal(KeyboardDiagnosticsView.PaletteRole.NormalKey, view.CellRole(kpEnter));
        }
        finally { view.ShutDown(); }
    }

    [Fact]
    public void SeparateKeypadTextCommitDoesNotHoldTheMainKey()
    {
        // SDL reports the keypad identity, then commits its text as a second event.
        var view = new KeyboardDiagnosticsView(Full);
        try
        {
            var state = view.Model;
            state.Record(Key(Events.evKeyDown, Keys.kbKeypad1, Keys.kbNumState), Full);
            var text = Key(Events.evKeyDown, '1', Keys.kbNumState);
            text.keyDown.text = "1";
            state.Record(text, Full);
            Assert.Equal(new[] { "KP1" }, state.Pressed);
            Assert.Equal("1", state.LastEvent.keyDown.text);
            Assert.Equal(KeyboardDiagnosticsView.PaletteRole.NormalKey, view.CellRole(Cell(view.Cells, "1")));
            state.Record(Key(Events.evKeyUp, Keys.kbKeypad1, Keys.kbNumState), Full);
            Assert.Empty(state.Pressed);
            // A keypad event that carries its own text (Console, Kitty) awaits nothing.
            var combined = Key(Events.evKeyDown, Keys.kbKeypad1, Keys.kbNumState);
            combined.keyDown.text = "1";
            state.Record(combined, Full);
            state.Record(text, Full);
            Assert.Equal(new[] { "1", "KP1" }, state.Pressed.Order());
        }
        finally { view.ShutDown(); }
    }

    [Theory]
    [InlineData(KeyboardCapabilities.None)]
    [InlineData(KeyboardCapabilities.KeyReleaseEvents | KeyboardCapabilities.StandaloneModifierTransitions)]
    public void WithoutKeypadCapabilityNumpadIsAbsentAndLayoutIsCompact(KeyboardCapabilities caps)
    {
        var view = new KeyboardDiagnosticsView(caps);
        try
        {
            Assert.False(view.ShowsKeypad);
            Assert.DoesNotContain(view.Cells, c => c.Group == KeyboardDiagnosticsView.KeyGroup.Keypad);
            Assert.DoesNotContain(view.Cells, c => KeyboardDiagnosticsState.IsKeypad(c.Label));
            // No keypad-shaped gap: the view ends at the navigation cluster and fits 80 columns.
            Assert.Equal(view.Cells.Max(c => c.X + c.Width), view.size.x);
            Assert.Equal(KeyboardDiagnosticsView.BaseWidth, view.size.x);
            Assert.True(view.size.x + 2 <= 80);
            // Ordinary input still renders, and the Num lock state is still reported.
            var alias = Key(Events.evKeyDown, '1', Keys.kbNumState);
            alias.keyDown.text = "1";
            view.Model.Record(alias, caps);
            Assert.NotEqual(KeyboardDiagnosticsView.PaletteRole.NormalKey, view.CellRole(Cell(view.Cells, "1")));
            Assert.Equal(KeyboardDiagnosticsView.PaletteRole.ActiveModifier,
                view.CellRole(view.Cells.Single(c => c.Modifier == Keys.kbNumState)));
            Assert.EndsWith("Distinct keypad: No", view.DetailLine(16));
        }
        finally { view.ShutDown(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandardPcLayoutGeometry(bool keypad)
    {
        var cells = KeyboardDiagnosticsView.CreateCells(keypad);
        int width = keypad ? KeyboardDiagnosticsView.KeypadWidth : KeyboardDiagnosticsView.BaseWidth;
        foreach (var cell in cells)
        {
            Assert.InRange(cell.Y, 0, 12);
            Assert.True(cell.X >= 0 && cell.X + cell.Width <= width);
            Assert.True((cell.Caption ?? cell.Label).Length <= cell.Width);
            Assert.True(cell.Y + cell.Height - 1 <= 12);
            foreach (var other in cells.Where(c => c != cell && c.Y < cell.Y + cell.Height && cell.Y < c.Y + c.Height))
                Assert.True(cell.X + cell.Width <= other.X || other.X + other.Width <= cell.X);
        }
        // Standard rows, in order, with the usual stagger.
        string Row(int y) => string.Concat(cells.Where(c => c.Y == y && c.Group == KeyboardDiagnosticsView.KeyGroup.Typing
            && c.Modifier == 0 && c.Label.Length == 1).OrderBy(c => c.X).Select(c => c.Label));
        Assert.Equal("`1234567890-=", Row(2));
        Assert.Equal("QWERTYUIOP[]\\", Row(4));
        Assert.Equal("ASDFGHJKL;'", Row(6));
        Assert.Equal("ZXCVBNM,./", Row(8));
        Assert.True(Cell(cells, "1").X < Cell(cells, "Q").X);
        Assert.True(Cell(cells, "Q").X < Cell(cells, "A").X);
        Assert.True(Cell(cells, "A").X < Cell(cells, "Z").X);
        // Every main row ends on the same right edge.
        foreach (int y in new[] { 2, 4, 6, 8, 10 })
            Assert.Equal(KeyboardDiagnosticsView.MainWidth,
                cells.Where(c => c.Y == y && c.X < KeyboardDiagnosticsView.MainWidth).Max(c => c.X + c.Width));

        // Esc, F1-F4, F5-F8 and F9-F12 are four separated groups.
        var functions = cells.Where(c => c.Group == KeyboardDiagnosticsView.KeyGroup.Function).OrderBy(c => c.X).ToArray();
        Assert.Equal(new[] { "Esc" }.Concat(Enumerable.Range(1, 12).Select(i => $"F{i}")), functions.Select(c => c.Label));
        Assert.All(functions, c => Assert.Equal(0, c.Y));
        int Gap(int i) => functions[i + 1].X - functions[i].X - functions[i].Width;
        foreach (int i in new[] { 0, 4, 8 }) Assert.True(Gap(i) > Gap(1));
        foreach (int i in new[] { 1, 2, 3, 5, 6, 7, 9, 10, 11 }) Assert.Equal(Gap(1), Gap(i));

        // Special keys are wider than ordinary keys and Space is the widest of all.
        int ordinary = Cell(cells, "A").Width;
        foreach (string label in new[] { "Back", "Tab", "Caps", "Enter", "Shift", "Ctrl", "Alt", "Space" })
            Assert.All(cells.Where(c => c.Label == label && c.Y != 12), c => Assert.True(c.Width > ordinary));
        var space = Cell(cells, "Space");
        Assert.All(cells.Where(c => c.Label != "Space"), c => Assert.True(c.Width < space.Width));
        // Left and right Shift, Ctrl and Alt are drawn as separate keys.
        foreach (uint modifier in new[] { Keys.kbShift, Keys.kbCtrlShift, Keys.kbAltShift })
        {
            var sides = cells.Where(c => c.Modifier == modifier).OrderBy(c => c.X).ToArray();
            Assert.Equal(2, sides.Length);
            Assert.True(sides[0].X < space.X && sides[1].X > space.X);
        }

        // Navigation: Ins Home PgUp over Del End PgDn, then an inverted-T arrow block.
        var navigation = cells.Where(c => c.Group == KeyboardDiagnosticsView.KeyGroup.Navigation).ToArray();
        Assert.Equal(10, navigation.Length);
        Assert.All(navigation, c => Assert.True(c.X >= KeyboardDiagnosticsView.NavigationX));
        string[] Line(int y) => navigation.Where(c => c.Y == y).OrderBy(c => c.X).Select(c => c.Label).ToArray();
        Assert.Equal(new[] { "Ins", "Home", "PgUp" }, Line(2));
        Assert.Equal(new[] { "Del", "End", "PgDn" }, Line(4));
        Assert.Equal(new[] { "Up" }, Line(8));
        Assert.Equal(new[] { "Left", "Down", "Right" }, Line(10));
        foreach (var (top, bottom) in new[] { ("Ins", "Del"), ("Home", "End"), ("PgUp", "PgDn"), ("Home", "Up"), ("Up", "Down"), ("Ins", "Left"), ("PgUp", "Right") })
            Assert.Equal(Cell(cells, top).X, Cell(cells, bottom).X);

        // Compact lock indicators.
        var locks = cells.Where(c => c.Y == 12).OrderBy(c => c.X).ToArray();
        Assert.Equal(new[] { "Caps", "Num", "Scroll" }, locks.Select(c => c.Label));
        Assert.Equal(new[] { Keys.kbCapsState, Keys.kbNumState, Keys.kbScrollState }, locks.Select(c => c.Modifier));
        Assert.Equal(keypad, cells.Any(c => c.Group == KeyboardDiagnosticsView.KeyGroup.Keypad));
    }

    [Fact]
    public void DiagnosticFieldsUseClearLabels()
    {
        var view = new KeyboardDiagnosticsView(Full);
        try
        {
            var ev = Key(Events.evKeyDown, Keys.kbKeypad1, Keys.kbNumState);
            ev.keyDown.charScan = new CharScanType(Keys.kbKeypad1);
            ev.keyDown.raw_scanCode = 0x4F;
            ev.keyDown.text = "1";
            view.Model.Record(ev, Full);
            Assert.Equal("Event: KeyDown  Key: KP1  Text: \"1\"", view.DetailLine(14));
            Assert.Equal("Key code: 0x9100  Legacy scan: 0x91  Raw scan: 0x4F  State: 0x00000020", view.DetailLine(15));
            Assert.EndsWith("Distinct keypad: Yes", view.DetailLine(16));
            foreach (int y in new[] { 14, 15, 16 }) Assert.True(view.DetailLine(y)!.Length <= KeyboardDiagnosticsView.BaseWidth);
        }
        finally { view.ShutDown(); }
    }

    [Fact]
    public void DialogFollowsCapabilityChangesWithoutInspectingTheDriverType()
    {
        using var driver = new DriverScope();
        var caps = KeyboardCapabilities.None;
        var root = new ButtonHost();
        var dialog = new KeyboardDiagnosticsDialog(() => caps);
        root.Insert(dialog);
        try
        {
            Assert.Equal(80, dialog.size.x);
            Assert.False(dialog.View.ShowsKeypad);
            TButton? close = null;
            dialog.ForEachView(v => { if (v is TButton b) close = b; });
            void AssertCloseIsBottomRight()
            {
                Assert.Equal(dialog.size.x - 2, close!.origin.x + close.size.x);
                Assert.Equal(dialog.size.y - 1, close.origin.y + close.size.y);
            }
            AssertCloseIsBottomRight();
            // Kitty negotiation completing while the dialog is open.
            caps = Full;
            var ev = Key(Events.evKeyDown, Keys.kbKeypad5);
            dialog.HandleEvent(ref ev);
            Assert.Equal(Events.evNothing, ev.What);
            Assert.True(dialog.View.ShowsKeypad);
            Assert.Equal(KeyboardDiagnosticsView.KeypadWidth, dialog.View.size.x);
            Assert.Equal(KeyboardDiagnosticsView.KeypadWidth + 2, dialog.size.x);
            AssertCloseIsBottomRight();
            Assert.Equal(KeyboardDiagnosticsView.PaletteRole.HeldKey,
                dialog.View.CellRole(dialog.View.Cells.Single(c => c.Label == "KP5")));
            caps = KeyboardCapabilities.None;
            ev = Key(Events.evKeyDown, 'A');
            dialog.HandleEvent(ref ev);
            Assert.False(dialog.View.ShowsKeypad);
            Assert.Equal(80, dialog.size.x);
            AssertCloseIsBottomRight();
            Assert.DoesNotContain(dialog.View.Cells, c => c.Group == KeyboardDiagnosticsView.KeyGroup.Keypad);
        }
        finally { dialog.ShutDown(); root.ShutDown(); }
    }

    [Fact]
    public void PaletteRolesDistinguishHeldLastPressModifiersAndLocks()
    {
        using var driver = new DriverScope();
        var view = new KeyboardDiagnosticsView(KeyboardCapabilities.None);
        try
        {
            var a = view.Cells.Single(c => c.Label == "A");
            Assert.Equal(KeyboardDiagnosticsView.PaletteRole.NormalKey, view.CellRole(a));
            view.Model.Record(Key(Events.evKeyDown, 'A'), KeyboardCapabilities.KeyReleaseEvents);
            Assert.Equal(KeyboardDiagnosticsView.PaletteRole.HeldKey, view.CellRole(a));
            view.Model.Record(Key(Events.evKeyUp, 'A'), KeyboardCapabilities.KeyReleaseEvents);
            Assert.Equal(KeyboardDiagnosticsView.PaletteRole.LastKey, view.CellRole(a));
            view.Model.Record(Key(Events.evKeyDown, 'A', Keys.kbShift | Keys.kbCapsState), KeyboardCapabilities.None);
            Assert.Equal(KeyboardDiagnosticsView.PaletteRole.LastKey, view.CellRole(a));
            // Logical modifiers light both the left and the right key.
            Assert.All(view.Cells.Where(c => c.Modifier == Keys.kbShift || c.Modifier == Keys.kbCapsState),
                c => Assert.Equal(KeyboardDiagnosticsView.PaletteRole.ActiveModifier, view.CellRole(c)));
            Assert.All(view.Cells.Where(c => c.Modifier == Keys.kbAltShift || c.Modifier == Keys.kbNumState),
                c => Assert.Equal(KeyboardDiagnosticsView.PaletteRole.InactiveModifier, view.CellRole(c)));
        }
        finally { view.ShutDown(); }
    }

    private sealed class ButtonHost : TGroup
    {
        public Queue<TEvent> Input { get; } = new();
        public ButtonHost() : base(new TRect(0, 0, 132, 25)) { }
        public override void GetEvent(ref TEvent ev) => ev = Input.Count > 0 ? Input.Dequeue() : default;
        public override void PutEvent(ref TEvent ev) => Input.Enqueue(ev);
    }
    private sealed class CaptureApp : TVDemoApp
    {
        public Queue<TEvent> Input { get; } = new();
        public List<TEvent> Received { get; } = new();
        public KeyboardDiagnosticsDialog? Dialog { get; private set; }
        public bool Scripted { get; set; } = true;
        public override void GetEvent(ref TEvent ev)
        {
            if (!Scripted) { base.GetEvent(ref ev); return; }
            Dialog ??= Assert.IsType<KeyboardDiagnosticsDialog>(DeskTop!.current);
            if (Received.Count > 0)
                Assert.Equal(Received[^1].keyDown.keyCode, Dialog.View.Model.LastEvent.keyDown.keyCode);
            Assert.NotEmpty(Input);
            var next = Input.Dequeue();
            PutEvent(ref next);
            base.GetEvent(ref ev);
            Assert.Equal(next.What, ev.What);
            if (ev.What == Events.evKeyDown) Received.Add(ev);
        }
    }

    [Fact]
    public void ModalCaptureBypassesStatusShortcutsAndRestoresApplicationState()
    {
        using var driver = new DriverScope();
        var app = new CaptureApp();
        var status = app.StatusLine;
        try
        {
            ushort[] codes = { Keys.kbF1, Keys.kbF2, Keys.kbF3, Keys.kbF4, Keys.kbF5,
                Keys.kbF6, Keys.kbF7, Keys.kbF8, Keys.kbF9, Keys.kbF10, Keys.kbF11,
                Keys.kbF12, Keys.kbAltX, Keys.kbAltF3, Keys.kbAlt1,
                Keys.kbEsc, Keys.kbEnter, Keys.kbTab };
            foreach (ushort code in codes) app.Input.Enqueue(Key(Events.evKeyDown, code));
            var cancel = new TEvent { What = Events.evCommand };
            cancel.message.command = Views.cmCancel;
            app.Input.Enqueue(cancel);
            var open = new TEvent { What = Events.evCommand };
            open.message.command = TVDemoCmd.cmKeyboardDlg;
            app.HandleEvent(ref open);
            Assert.Equal(codes, app.Received.Select(e => e.keyDown.keyCode));
            Assert.Same(status, app.StatusLine);
            Assert.Null(app.Dialog!.owner);
            Assert.Null(app.Dialog.View.owner);
            Assert.NotSame(app.Dialog, app.DeskTop!.current);
            // The normal F10 status shortcut works again after capture closes.
            var f10 = Key(Events.evKeyDown, Keys.kbF10);
            app.PutEvent(ref f10);
            TEvent after = default;
            app.Scripted = false;
            app.GetEvent(ref after);
            Assert.Equal(Events.evCommand, after.What);
            Assert.Equal(Views.cmMenu, after.message.command);
            after.What = Events.evNothing;
            app.PutEvent(ref after);
        }
        finally { app.ShutDown(); }
    }

    private static TEvent Key(ushort what, ushort code, uint modifiers = 0)
    {
        var ev = new TEvent { What = what };
        ev.keyDown.keyCode = code;
        ev.keyDown.controlKeyState = modifiers;
        return ev;
    }

    [Theory]
    [InlineData(Keys.kbF1, "F1")]
    [InlineData(Keys.kbF10, "F10")]
    [InlineData(Keys.kbF11, "F11")]
    [InlineData(Keys.kbF12, "F12")]
    [InlineData(Keys.kbShiftF11, "F11")]
    [InlineData(Keys.kbCtrlF5, "F5")]
    [InlineData(Keys.kbAltF12, "F12")]
    [InlineData(Keys.kbCtrlShiftIns, "Ins")]
    [InlineData(Keys.kbCtrlShiftDel, "Del")]
    [InlineData(Keys.kbAltA, "A")]
    [InlineData(Keys.kbCtrlA, "A")]
    [InlineData(Keys.kbShiftTab, "Tab")]
    [InlineData((ushort)'A', "A")]
    [InlineData((ushort)'a', "A")]
    [InlineData((ushort)'?', "/")]
    [InlineData((ushort)'!', "1")]
    public void MapsCurrentIdentitiesAndAliases(ushort code, string label) =>
        Assert.Equal(label, KeyboardDiagnosticsState.MapKey(code));

    [Fact]
    public void HoldsMultipleKeysUntilRealReleaseAndUpdatesLogicalModifiers()
    {
        var state = new KeyboardDiagnosticsState();
        var caps = KeyboardCapabilities.KeyReleaseEvents | KeyboardCapabilities.StandaloneModifierTransitions;
        state.Record(Key(Events.evKeyDown, (ushort)'A', Keys.kbShift), caps);
        state.Record(Key(Events.evKeyDown, Keys.kbCtrlF5, Keys.kbCtrlShift), caps);
        Assert.Contains("A", state.Pressed);
        Assert.Contains("F5", state.Pressed);
        state.Record(Key(Events.evKeyUp, (ushort)'A'), caps);
        Assert.DoesNotContain("A", state.Pressed);
        Assert.Contains("F5", state.Pressed);
        state.Record(Key(Events.evModifierChanged, 0, Keys.kbAltShift | Keys.kbCtrlShift), caps);
        Assert.True(state.ModifierActive(Keys.kbAltShift));
        Assert.True(state.ModifierActive(Keys.kbCtrlShift));
        Assert.False(state.ModifierActive(Keys.kbShift));
        Assert.Contains("F5", state.Pressed);
        state.Record(Key(Events.evKeyUp, Keys.kbCtrlF5), caps);
        Assert.Empty(state.Pressed);
    }

    [Fact]
    public void WithoutReleaseCapabilityOnlyLastPressIsHighlighted()
    {
        var state = new KeyboardDiagnosticsState();
        state.Record(Key(Events.evKeyDown, (ushort)'A'), KeyboardCapabilities.None);
        state.Record(Key(Events.evKeyDown, (ushort)'B'), KeyboardCapabilities.None);
        state.Record(Key(Events.evKeyUp, (ushort)'B'), KeyboardCapabilities.None);
        Assert.Equal(new[] { "B" }, state.Pressed);
        Assert.False(state.Releases);
    }

    [Fact]
    public void DialogConsumesAllKeyboardEventsBeforeDefaultHandling()
    {
        using var driver = new DriverScope();
        var dialog = new KeyboardDiagnosticsDialog();
        try
        {
            foreach (ushort code in new[] { Keys.kbEsc, Keys.kbEnter, Keys.kbTab, Keys.kbShiftTab,
                Keys.kbF1, Keys.kbF2, Keys.kbF3, Keys.kbF4, Keys.kbF5, Keys.kbF6,
                Keys.kbF7, Keys.kbF8, Keys.kbF9, Keys.kbF10, Keys.kbF11, Keys.kbF12,
                Keys.kbAltX, Keys.kbAltF3, Keys.kbCtrlA, Keys.kbLeft, Keys.kbPgDn, Keys.kbKeypadEnter })
            {
                var ev = Key(Events.evKeyDown, code);
                dialog.HandleEvent(ref ev);
                Assert.Equal(Events.evNothing, ev.What);
                Assert.Equal(code, dialog.View.Model.LastEvent.keyDown.keyCode);
            }
            foreach (ushort what in new[] { Events.evKeyUp, Events.evModifierChanged })
            {
                var ev = Key(what, 0, Keys.kbShift);
                dialog.HandleEvent(ref ev);
                Assert.Equal(Events.evNothing, ev.What);
                Assert.Equal(what, dialog.View.Model.LastEvent.What);
            }
        }
        finally { dialog.ShutDown(); }
    }

    [Fact]
    public void KeyboardMouseCloseReturnsCancelAndRestoresModalOwner()
    {
        using var driver = new DriverScope();
        var root = new ButtonHost();
        var dialog = new KeyboardDiagnosticsDialog();
        TButton? close = null;
        dialog.ForEachView(v => { if (v is TButton b) { close = b; b.SetState(Views.sfDisabled, false); } });
        Assert.NotNull(close);
        var down = new TEvent { What = Events.evMouseDown };
        down.mouse.where = close.MakeGlobal(new TPoint(2, 0));
        down.mouse.buttons = (byte)Events.mbLeftButton;
        var up = down;
        up.What = Events.evMouseUp;
        up.mouse.buttons = 0;
        root.Input.Enqueue(down);
        root.Input.Enqueue(up);
        try
        {
            Assert.Equal(Views.cmCancel, root.ExecView(dialog));
            Assert.Null(dialog.owner);
            Assert.Equal(0, dialog.state & Views.sfModal);
        }
        finally { dialog.ShutDown(); root.ShutDown(); }
    }

    [Fact]
    public void MouseButtonTargetClosesAndDetachesAllChildrenAndCanReopen()
    {
        using var driver = new DriverScope();
        var root = new ButtonHost();
        var commands = new TCommandSet();
        TView.GetCommands(commands);
        TView.EnableCommand(Views.cmClose);
        try
        {
            for (int i = 0; i < 2; i++)
            {
                var dialog = new MouseDialog();
                root.Insert(dialog);
                TButton? button = null;
                dialog.ForEachView(v => { if (v is TButton b) button = b; });
                Assert.NotNull(button);
                // This bare host has no application to broadcast command-set changes.
                button.SetState(Views.sfDisabled, false);
                var mouse = new TEvent { What = Events.evMouseDown };
                mouse.mouse.where = button.MakeGlobal(new TPoint(2, 0));
                mouse.mouse.buttons = (byte)Events.mbLeftButton;
                var up = mouse;
                up.What = Events.evMouseUp;
                up.mouse.buttons = 0;
                root.Input.Enqueue(up);
                button.HandleEvent(ref mouse);
                Assert.Equal(Events.evNothing, mouse.What);
                TEvent ev = default;
                root.GetEvent(ref ev);
                Assert.Equal(Events.evCommand, ev.What);
                Assert.Equal(Views.cmClose, ev.message.command);
                Assert.Same(button, ev.message.infoPtr);
                dialog.HandleEvent(ref ev);
                Assert.Equal(Events.evNothing, ev.What);
                Assert.Null(dialog.owner);
                Assert.Null(button.owner);
                Assert.Null(dialog.View.owner);
                Assert.Null(dialog.frame);
                root.Input.Clear(); // Closing notification.
            }
        }
        finally { root.ShutDown(); TView.SetCommands(commands); }
    }
    [Theory]
    [InlineData("ě")]
    [InlineData("š")]
    [InlineData("č")]
    [InlineData("ř")]
    public void UnicodeTextHasNoGuessedHighlight(string text)
    {
        foreach (var caps in new[] { KeyboardCapabilities.None, KeyboardCapabilities.KeyReleaseEvents })
        {
            var state = new KeyboardDiagnosticsState(caps);
            var ev = Key(Events.evKeyDown, 0);
            ev.keyDown.text = text;
            state.Record(ev, caps);
            Assert.Equal(text, state.LastEvent.keyDown.text);
            Assert.Empty(state.Pressed);
            Assert.Null(KeyboardDiagnosticsState.MapKey(0));
        }
    }

    [Theory]
    [InlineData((ushort)0x0161)]
    [InlineData((ushort)0x010D)]
    [InlineData((ushort)0x0159)]
    public void UnmatchedExtendedIdentityIsNotMasked(ushort code) =>
        Assert.Null(KeyboardDiagnosticsState.MapKey(code));

    [Fact]
    public void RealEscapeAndExplicitIdentityRemainValid()
    {
        Assert.Equal("Esc", KeyboardDiagnosticsState.MapKey(Keys.kbEsc));
        var state = new KeyboardDiagnosticsState();
        var ev = Key(Events.evKeyDown, '2');
        ev.keyDown.text = "ě";
        state.Record(ev, KeyboardCapabilities.None);
        Assert.Equal(new[] { "2" }, state.Pressed);
        Assert.Equal("ě", state.LastEvent.keyDown.text);
    }
    [Theory]
    [InlineData("ě")]
    [InlineData("š")]
    [InlineData("č")]
    [InlineData("ř")]
    public void DialogConsumesUnicodeAndRetainsTextWithoutHighlight(string text)
    {
        using var driver = new DriverScope();
        var dialog = new KeyboardDiagnosticsDialog();
        try
        {
            var ev = Key(Events.evKeyDown, 0);
            ev.keyDown.text = text;
            dialog.HandleEvent(ref ev);
            Assert.Equal(Events.evNothing, ev.What);
            Assert.Equal(text, dialog.View.Model.LastEvent.keyDown.text);
            Assert.Empty(dialog.View.Model.Pressed);
        }
        finally { dialog.ShutDown(); }
    }
}
