using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class FunctionKeyDiagnosticsTests
{
    private static readonly ushort[] Plain = { Keys.kbF1, Keys.kbF2, Keys.kbF3, Keys.kbF4, Keys.kbF5,
        Keys.kbF6, Keys.kbF7, Keys.kbF8, Keys.kbF9, Keys.kbF10 };
    private static readonly ushort[] Shifted = { Keys.kbShiftF1, Keys.kbShiftF2, Keys.kbShiftF3, Keys.kbShiftF4, Keys.kbShiftF5,
        Keys.kbShiftF6, Keys.kbShiftF7, Keys.kbShiftF8, Keys.kbShiftF9, Keys.kbShiftF10 };
    private static readonly ushort[] Control = { Keys.kbCtrlF1, Keys.kbCtrlF2, Keys.kbCtrlF3, Keys.kbCtrlF4, Keys.kbCtrlF5,
        Keys.kbCtrlF6, Keys.kbCtrlF7, Keys.kbCtrlF8, Keys.kbCtrlF9, Keys.kbCtrlF10 };
    private static readonly ushort[] Alternate = { Keys.kbAltF1, Keys.kbAltF2, Keys.kbAltF3, Keys.kbAltF4, Keys.kbAltF5,
        Keys.kbAltF6, Keys.kbAltF7, Keys.kbAltF8, Keys.kbAltF9, Keys.kbAltF10 };

    private static TEvent Key(ushort what, ushort code, uint modifiers = 0)
    {
        var ev = new TEvent { What = what };
        ev.keyDown.keyCode = code;
        ev.keyDown.charScan = new CharScanType(code);
        ev.keyDown.controlKeyState = modifiers;
        return ev;
    }

    private static TEvent Down(ushort code, uint modifiers = 0) => Key(Events.evKeyDown, code, modifiers);

    private static IEnumerable<(int Number, FunctionKeyColumn Column)> Marks(FunctionKeyDiagnosticsState state)
    {
        for (int number = 1; number <= FunctionKeyDiagnosticsState.Rows; number++)
            foreach (FunctionKeyColumn column in Enum.GetValues<FunctionKeyColumn>())
                if (state.IsMarked(number, column)) yield return (number, column);
    }

    // 1-5: rows F1-F10 and the Plain, Shift, Ctrl, Alt and Cmd columns, each exactly one cell.
    // Meta has no key-code family: Meta+F5 is kbF5 with kbMetaShift in the state.
    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, Keys.kbShift)]
    [InlineData(2, Keys.kbCtrlShift)]
    [InlineData(3, Keys.kbAltShift)]
    [InlineData(4, Keys.kbMetaShift)]
    public void EachSingleModifierKeyMarksExactlyItsOwnCell(int columnIndex, uint modifiers)
    {
        var column = (FunctionKeyColumn)columnIndex;
        ushort[] codes = column switch
        {
            FunctionKeyColumn.Plain or FunctionKeyColumn.Cmd => Plain, FunctionKeyColumn.Shift => Shifted,
            FunctionKeyColumn.Ctrl => Control, _ => Alternate,
        };
        for (int i = 0; i < codes.Length; i++)
        {
            var state = new FunctionKeyDiagnosticsState();
            state.Record(Down(codes[i], modifiers));
            Assert.Equal(new[] { (i + 1, column) }, Marks(state));
            Assert.Empty(state.Other);
            Assert.Equal(1, state.MarkedCount);
        }
    }

    [Fact]
    public void RowMappingCoversF1ThroughF10AndRecognizesF11AndF12()
    {
        for (int i = 0; i < 10; i++)
            foreach (var table in new[] { Plain, Shifted, Control, Alternate })
            {
                Assert.True(FunctionKeyDiagnosticsState.TryGetFunctionKey(table[i], out int number, out _));
                Assert.Equal(i + 1, number);
            }
        Assert.True(FunctionKeyDiagnosticsState.TryGetFunctionKey(Keys.kbCtrlF12, out int twelve, out uint implied));
        Assert.Equal(12, twelve);
        Assert.Equal(Keys.kbCtrlShift, implied);
        foreach (ushort other in new[] { Keys.kbEsc, Keys.kbTab, (ushort)'D', Keys.kbAltD, (ushort)0 })
            Assert.False(FunctionKeyDiagnosticsState.TryGetFunctionKey(other, out _, out _));
    }

    // 6: the Cmd column is real: kbMetaShift marks it, and nothing else is guessed to be Cmd.
    [Fact]
    public void CmdColumnIsMarkedByTheMetaBitOnly()
    {
        string meta = OperatingSystem.IsMacOS() ? "Cmd" : "Meta";
        Assert.Equal(new[] { "Plain", "Shift", "Ctrl", "Alt/Opt", meta }, FunctionKeyDiagnosticsState.ColumnTitles);
        Assert.Equal("Cmd", FunctionKeyDiagnosticsState.MetaName(macOS: true));
        Assert.Equal("Meta", FunctionKeyDiagnosticsState.MetaName(macOS: false));
        Assert.Equal(50, FunctionKeyDiagnosticsState.Cells);
        var state = new FunctionKeyDiagnosticsState();
        // Locks and unknown high bits are ignored, not guessed to be Cmd.
        state.Record(Down(Keys.kbF5, Keys.kbCapsState | Keys.kbNumState | Keys.kbScrollState | 0x0200 | 0x8000_0000));
        Assert.Equal(new[] { (5, FunctionKeyColumn.Plain) }, Marks(state));
        state.Record(Down(Keys.kbF1, Keys.kbMetaShift | Keys.kbCapsState));
        state.Record(Down(Keys.kbF10, Keys.kbMetaShift));
        Assert.Equal(new[] { (1, FunctionKeyColumn.Cmd), (5, FunctionKeyColumn.Plain), (10, FunctionKeyColumn.Cmd) },
            Marks(state).OrderBy(m => m.Number));
        Assert.Empty(state.Other);
    }

    [Fact]
    public void ModifierTextNamesMetaForThePlatform()
    {
        Assert.Equal("Cmd", FunctionKeyDiagnosticsState.ModifierText(Keys.kbMetaShift, "Cmd"));
        Assert.Equal("Shift+Cmd", FunctionKeyDiagnosticsState.ModifierText(Keys.kbShift | Keys.kbMetaShift, "Cmd"));
        Assert.Equal("Ctrl+Cmd", FunctionKeyDiagnosticsState.ModifierText(Keys.kbCtrlShift | Keys.kbMetaShift, "Cmd"));
        Assert.Equal("Alt+Cmd", FunctionKeyDiagnosticsState.ModifierText(Keys.kbAltShift | Keys.kbMetaShift, "Cmd"));
        Assert.Equal("Shift+Ctrl+Cmd",
            FunctionKeyDiagnosticsState.ModifierText(Keys.kbShift | Keys.kbCtrlShift | Keys.kbMetaShift, "Cmd"));
        Assert.Equal("Shift+Ctrl+Alt+Meta", FunctionKeyDiagnosticsState.ModifierText(
            Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift | Keys.kbMetaShift, "Meta"));
        Assert.Equal("none", FunctionKeyDiagnosticsState.ModifierText(Keys.kbCapsState, "Cmd"));
    }

    // 7: several modifiers never mark a single-modifier cell, whichever key code the driver chose.
    [Theory]
    [InlineData(Keys.kbCtrlF5, Keys.kbCtrlShift | Keys.kbShift, "Shift+Ctrl+F5")]
    [InlineData(Keys.kbAltF1, Keys.kbAltShift | Keys.kbShift, "Shift+Alt+F1")]
    [InlineData(Keys.kbAltF10, Keys.kbAltShift | Keys.kbCtrlShift, "Ctrl+Alt+F10")]
    [InlineData(Keys.kbAltF3, Keys.kbAltShift | Keys.kbCtrlShift | Keys.kbShift, "Shift+Ctrl+Alt+F3")]
    // A key code that names a modifier the state omits still counts that modifier.
    [InlineData(Keys.kbShiftF7, Keys.kbCtrlShift, "Shift+Ctrl+F7")]
    [InlineData(Keys.kbF11, 0u, "F11")]
    [InlineData(Keys.kbCtrlF12, Keys.kbCtrlShift, "Ctrl+F12")]
    // Meta with any other modifier is a combination, never the Cmd cell or the other modifier's cell.
    [InlineData(Keys.kbCtrlF5, Keys.kbCtrlShift | Keys.kbMetaShift, "Ctrl+{0}+F5")]
    [InlineData(Keys.kbShiftF5, Keys.kbShift | Keys.kbMetaShift, "Shift+{0}+F5")]
    [InlineData(Keys.kbAltF5, Keys.kbAltShift | Keys.kbMetaShift, "Alt+{0}+F5")]
    [InlineData(Keys.kbCtrlF1, Keys.kbShift | Keys.kbCtrlShift | Keys.kbMetaShift, "Shift+Ctrl+{0}+F1")]
    [InlineData(Keys.kbF11, Keys.kbMetaShift, "{0}+F11")]
    public void CombinationsOutsideTheMatrixAreListedNotMisfiled(ushort code, uint modifiers, string format)
    {
        string text = string.Format(format, FunctionKeyDiagnosticsState.Meta);
        var state = new FunctionKeyDiagnosticsState();
        state.Record(Down(code, modifiers));
        state.Record(Down(code, modifiers));
        Assert.Empty(Marks(state));
        Assert.Equal(new[] { text }, state.Other);
    }

    [Fact]
    public void KeyCodeAloneStillClassifiesWhenADriverOmitsTheState()
    {
        var state = new FunctionKeyDiagnosticsState();
        state.Record(Down(Keys.kbAltF4));
        Assert.Equal(new[] { (4, FunctionKeyColumn.Alt) }, Marks(state));
    }

    // 8: Reset clears marks, other combinations, the live details and the count — nothing else.
    [Fact]
    public void ResetClearsEverythingThisDiagnosticOwns()
    {
        using var driver = new DriverScope();
        var dialog = new FunctionKeyDiagnosticsDialog(() => driver.Driver);
        try
        {
            foreach (var ev in new[] { Down(Keys.kbF1), Down(Keys.kbCtrlF5, Keys.kbCtrlShift),
                Down(Keys.kbF3, Keys.kbMetaShift), Down(Keys.kbCtrlF5, Keys.kbCtrlShift | Keys.kbShift) })
            {
                var copy = ev;
                dialog.HandleEvent(ref copy);
            }
            var model = dialog.View.Model;
            Assert.Equal(3, model.MarkedCount);
            Assert.True(model.IsMarked(3, FunctionKeyColumn.Cmd));
            Assert.Single(model.Other);
            Assert.Equal(4, model.EventCount);
            var reset = new TEvent { What = Events.evCommand };
            reset.message.command = TVDemoCmd.cmFunctionKeysReset;
            dialog.HandleEvent(ref reset);
            Assert.Equal(Events.evNothing, reset.What);
            Assert.Equal(0, model.MarkedCount);
            Assert.Empty(model.Other);
            Assert.False(model.HasEvent);
            Assert.Equal(0, model.EventCount);
            Assert.Empty(Marks(model));
            Assert.Same(driver.Driver, TDisplay.driver);
            Assert.False(model.IsMarked(3, FunctionKeyColumn.Cmd));
            Assert.Contains("0/50", dialog.View.LegendLine());
            // The Reset button sends exactly that command.
            Assert.Equal(TVDemoCmd.cmFunctionKeysReset, dialog.ResetButton.Command);
        }
        finally { dialog.ShutDown(); }
    }

    // 9: the live details follow the last event of any kind and show the real event type.
    [Fact]
    public void LastEventDetailsUpdate()
    {
        using var driver = new DriverScope();
        var view = new FunctionKeyDiagnosticsView(() => driver.Driver);
        try
        {
            Assert.Equal("Event:     -", view.DetailLine(6)!.TrimEnd());
            Assert.Equal("Driver:    Null", view.DetailLine(1));
            view.Model.Record(Down(Keys.kbCtrlF5, Keys.kbCtrlShift));
            Assert.Equal("Event:     KeyDown", view.DetailLine(6));
            Assert.Equal("Last key:  F5", view.DetailLine(7));
            Assert.Equal("Modifiers: Ctrl", view.DetailLine(8));
            Assert.Equal("Key code:  0x6200", view.DetailLine(9));
            Assert.Equal("State:     0x00000004", view.DetailLine(11));
            view.Model.Record(Key(Events.evKeyUp, Keys.kbCtrlF5, Keys.kbCtrlShift));
            Assert.Equal("Event:     KeyUp", view.DetailLine(6));
            view.Model.Record(Key(Events.evModifierChanged, 0, Keys.kbShift | Keys.kbCtrlShift));
            Assert.Equal("Event:     ModifierChanged", view.DetailLine(6));
            Assert.Equal("Modifiers: Shift+Ctrl", view.DetailLine(8));
            Assert.Equal("Events:    3", view.DetailLine(13));
            view.Model.Record(Down(Keys.kbCtrlF5, Keys.kbShift | Keys.kbCtrlShift | Keys.kbMetaShift | Keys.kbNumState));
            Assert.Equal($"Modifiers: Shift+Ctrl+{FunctionKeyDiagnosticsState.Meta}", view.DetailLine(8));
            Assert.Equal("State:     0x00000127", view.DetailLine(11));
            view.Model.Record(Key(Events.evModifierChanged, 0, Keys.kbMetaShift));
            Assert.Equal($"Modifiers: {FunctionKeyDiagnosticsState.Meta}", view.DetailLine(8));
            Assert.Equal("Last key:  (modifier only)", view.DetailLine(7));
            // A release or a modifier transition is not a received combination.
            Assert.Equal(1, view.Model.MarkedCount);
        }
        finally { view.ShutDown(); }
    }

    [Fact]
    public void DriverNameComesFromTheRunningDriver()
    {
        using var driver = new DriverScope();
        Assert.Equal("Null", FunctionKeyDiagnosticsView.DriverName(driver.Driver));
        Assert.Equal("none", FunctionKeyDiagnosticsView.DriverName(null));
        Assert.Equal("-", FunctionKeyDiagnosticsView.HostName(null));
    }

    // 20: modifiers are event-local; nothing stays held after the event that reported it.
    [Fact]
    public void NoStuckModifiers()
    {
        var state = new FunctionKeyDiagnosticsState();
        state.Record(Key(Events.evModifierChanged, 0, Keys.kbShift));
        state.Record(Down(Keys.kbCtrlF2, Keys.kbCtrlShift));
        state.Record(Down(Keys.kbF2));
        state.Record(Key(Events.evModifierChanged, 0, Keys.kbAltShift));
        state.Record(Down(Keys.kbF3));
        Assert.Equal(new[] { (2, FunctionKeyColumn.Plain), (2, FunctionKeyColumn.Ctrl), (3, FunctionKeyColumn.Plain) },
            Marks(state).OrderBy(m => m.Number).ThenBy(m => m.Column));
        Assert.Empty(state.Other);
        Assert.Equal(0u, FunctionKeyDiagnosticsState.EventModifiers(state.LastEvent));
    }

    // 10, 11: every F1 and F10 combination, and every other key, stops at the focused diagnostic.
    [Fact]
    public void DialogConsumesEveryFunctionKeyCombination()
    {
        using var driver = new DriverScope();
        var dialog = new FunctionKeyDiagnosticsDialog(() => driver.Driver);
        try
        {
            uint[] states = { 0, Keys.kbShift, Keys.kbCtrlShift, Keys.kbAltShift, Keys.kbMetaShift,
                Keys.kbShift | Keys.kbCtrlShift, Keys.kbCtrlShift | Keys.kbAltShift, Keys.kbCtrlShift | Keys.kbMetaShift };
            foreach (var table in new[] { Plain, Shifted, Control, Alternate })
                foreach (ushort code in table.Concat(new[] { Keys.kbF11, Keys.kbF12 }))
                    foreach (uint modifiers in states)
                    {
                        var ev = Down(code, modifiers);
                        dialog.HandleEvent(ref ev);
                        Assert.Equal(Events.evNothing, ev.What);
                        Assert.Equal(code, dialog.View.Model.LastEvent.keyDown.keyCode);
                    }
            Assert.Equal(50, dialog.View.Model.MarkedCount);
            // Esc, Enter and letters are recorded only: a stray escape sequence cannot close or reset.
            foreach (ushort code in new[] { Keys.kbEsc, Keys.kbEnter, (ushort)'r', (ushort)'c', Keys.kbAltX })
            {
                var ev = Down(code);
                dialog.HandleEvent(ref ev);
                Assert.Equal(Events.evNothing, ev.What);
            }
            Assert.Equal(50, dialog.View.Model.MarkedCount);
            foreach (ushort what in new[] { Events.evKeyUp, Events.evModifierChanged })
            {
                var ev = Key(what, Keys.kbF1, Keys.kbShift);
                dialog.HandleEvent(ref ev);
                Assert.Equal(Events.evNothing, ev.What);
                Assert.Equal(what, dialog.View.Model.LastEvent.What);
            }
        }
        finally { dialog.ShutDown(); }
    }

    // 14: the buttons own focus; Tab moves between them and focus never decides capture.
    [Fact]
    public void FocusStartsOnResetMovesWithTabAndNeverAffectsCapture()
    {
        using var driver = new DriverScope();
        var dialog = new FunctionKeyDiagnosticsDialog(() => driver.Driver);
        try
        {
            Assert.Equal(0, dialog.View.options & Views.ofSelectable);
            Assert.Same(dialog.ResetButton, dialog.current);
            // A bare test host has no application to broadcast the enabled command set.
            dialog.ResetButton.SetState(Views.sfDisabled, false);
            dialog.CloseButton.SetState(Views.sfDisabled, false);
            foreach (var expected in new TView[] { dialog.CloseButton, dialog.ResetButton })
            {
                var tab = Down(Keys.kbTab);
                dialog.HandleEvent(ref tab);
                Assert.Same(expected, dialog.current);
                var f1 = Down(Keys.kbF1);
                dialog.HandleEvent(ref f1);
                Assert.Equal(Events.evNothing, f1.What);
            }
            Assert.True(FunctionKeyDiagnosticsDialog.ReachesControls(Down(Keys.kbShiftTab, Keys.kbShift)));
            Assert.True(FunctionKeyDiagnosticsDialog.ReachesControls(Down(Keys.kbSpace)));
            Assert.False(FunctionKeyDiagnosticsDialog.ReachesControls(Down(Keys.kbSpace, Keys.kbMetaShift)));
            Assert.False(FunctionKeyDiagnosticsDialog.ReachesControls(Key(Events.evKeyUp, Keys.kbTab)));
        }
        finally { dialog.ShutDown(); }
    }

    // 16: menu and button mnemonics are unique where they are offered.
    [Fact]
    public void MnemonicsDoNotCollide()
    {
        using var driver = new DriverScope();
        using var registry = new StreamableRegistryScope();
        var app = new TVDemoApp();
        try
        {
            TMenuItem? diagnostics = null;
            for (var item = app.MenuBar!.Menu!.Items; item != null; item = item.Next)
                if (item.Name == "D~i~agnostics") diagnostics = item;
            Assert.NotNull(diagnostics);
            var names = new List<string>();
            var hotkeys = new List<char>();
            for (var item = diagnostics.SubMenu.Items; item != null; item = item.Next)
                if (item.Name != null) { names.Add(item.Name); hotkeys.Add(char.ToUpperInvariant(item.HotChar())); }
            Assert.Equal(new[] { "~C~haracter Map...", "~K~eyboard...", "~F~unction keys...", "~M~ouse...", "~H~eap / memory" }, names);
            Assert.Equal(hotkeys.Count, hotkeys.Distinct().Count());
            var buttons = new[] { "~R~eset", "~C~lose" };
            var dialog = new FunctionKeyDiagnosticsDialog(() => driver.Driver);
            try
            {
                Assert.Equal(buttons, new[] { dialog.ResetButton.Title, dialog.CloseButton.Title });
                // Alt+R and Alt+C reach the buttons; the window's own keys are not taken.
                Assert.True(FunctionKeyDiagnosticsDialog.ReachesControls(Down(Keys.kbAltR)));
                Assert.True(FunctionKeyDiagnosticsDialog.ReachesControls(Down(Keys.kbAltC)));
            }
            finally { dialog.ShutDown(); }
        }
        finally { app.ShutDown(); }
    }

    // 15: everything fits an 80x25 screen without clipping.
    [Fact]
    public void FitsEightyByTwentyFive()
    {
        using var driver = new DriverScope(80, 25);
        using var registry = new StreamableRegistryScope();
        var app = new TVDemoApp();
        var dialog = new FunctionKeyDiagnosticsDialog(() => driver.Driver);
        try
        {
            Assert.True(dialog.size.x <= app.DeskTop!.size.x);
            Assert.True(dialog.size.y <= app.DeskTop.size.y);
            var view = dialog.View;
            Assert.Equal(new TRect(1, 1, dialog.size.x - 1, 1 + FunctionKeyDiagnosticsView.ViewHeight), view.GetBounds());
            // Buttons sit below the view, inside the frame, with their shadow row.
            foreach (var button in new[] { dialog.ResetButton, dialog.CloseButton })
            {
                var b = button.GetBounds();
                Assert.True(b.a.y > view.GetBounds().b.y - 1);
                Assert.True(b.b.y <= dialog.size.y - 1);
                Assert.True(b.a.x >= 1 && b.b.x <= dialog.size.x - 1);
            }
            // Fill everything to its widest and check no segment runs past the view.
            const uint all = Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift | Keys.kbMetaShift;
            view.Model.Record(Key(Events.evModifierChanged, 0, all));
            for (int i = 0; i < 12; i++)
                view.Model.Record(Down(Keys.kbAltF1, all));
            foreach (var table in new[] { Plain, Shifted, Control, Alternate })
                foreach (ushort code in table)
                    view.Model.Record(Down(code));
            foreach (ushort code in Plain)
                view.Model.Record(Down(code, Keys.kbMetaShift));
            foreach (ushort code in new[] { Keys.kbAltF1, Keys.kbAltF2, Keys.kbAltF3, Keys.kbAltF4, Keys.kbAltF5,
                Keys.kbAltF6, Keys.kbAltF7, Keys.kbAltF8, Keys.kbAltF9, Keys.kbAltF10, Keys.kbAltF11, Keys.kbAltF12 })
                view.Model.Record(Down(code, all));
            for (int y = 0; y < FunctionKeyDiagnosticsView.ViewHeight; y++)
                foreach (var segment in view.Segments(y))
                    Assert.True(segment.X + segment.Text.Length <= FunctionKeyDiagnosticsView.ViewWidth, $"row {y}: {segment.Text}");
            // Header shows every column, all ten rows are visible at once, and the legend shows the count.
            string header = view.RowText(FunctionKeyDiagnosticsView.HeaderY);
            int last = -1;
            foreach (string title in FunctionKeyDiagnosticsState.ColumnTitles)
            {
                int at = header.IndexOf(title, StringComparison.Ordinal);
                Assert.True(at > last, title);
                last = at;
            }
            for (int n = 1; n <= 10; n++)
                Assert.StartsWith($"  F{n} ", view.RowText(FunctionKeyDiagnosticsView.FirstRowY + n - 1));
            Assert.Contains("[X]", view.RowText(FunctionKeyDiagnosticsView.FirstRowY));
            for (int n = 1; n <= 10; n++)
            {
                string row = view.RowText(FunctionKeyDiagnosticsView.FirstRowY + n - 1);
                Assert.Equal(5, CountOf(row, "[X]"));
                Assert.DoesNotContain("n/a", row);
            }
            Assert.Contains("50/50", view.RowText(FunctionKeyDiagnosticsView.LegendY));
            Assert.StartsWith("  Other: (+", view.RowText(FunctionKeyDiagnosticsView.OtherY));
            Assert.Contains($"Shift+Ctrl+Alt+{FunctionKeyDiagnosticsState.Meta}+F12", view.RowText(FunctionKeyDiagnosticsView.OtherY + 1));
            Assert.Contains($"{FunctionKeyDiagnosticsState.Meta} key:", view.RowText(5));
            // Cells are centred in their columns and never overlap the divider.
            Assert.True(FunctionKeyDiagnosticsView.ColumnX(FunctionKeyColumn.Cmd) + FunctionKeyDiagnosticsView.CellWidth
                <= FunctionKeyDiagnosticsView.DividerX);
        }
        finally { dialog.ShutDown(); app.ShutDown(); }
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + 1, StringComparison.Ordinal))
            count++;
        return count;
    }

    private sealed class CaptureApp : TVDemoApp
    {
        public Queue<TEvent> Input { get; } = new();
        public List<TEvent> Received { get; } = new();
        public FunctionKeyDiagnosticsDialog? Dialog { get; private set; }
        public bool Scripted { get; set; } = true;
        public override void GetEvent(ref TEvent ev)
        {
            if (!Scripted) { base.GetEvent(ref ev); return; }
            Dialog ??= Assert.IsType<FunctionKeyDiagnosticsDialog>(DeskTop!.current);
            Assert.NotEmpty(Input);
            var next = Input.Dequeue();
            PutEvent(ref next);
            base.GetEvent(ref ev);
            // With capture on, status shortcuts are not converted into commands.
            Assert.Equal(next.What, ev.What);
            if (ev.What == Events.evKeyDown) Received.Add(ev);
        }
    }

    // 10-13: inside the running application F1/F10 are recorded, then work normally after closing.
    [Fact]
    public void ModalCaptureRecordsF1AndF10ThenRestoresHelpAndMenu()
    {
        using var driver = new DriverScope();
        using var registry = new StreamableRegistryScope();
        var app = new CaptureApp();
        var status = app.StatusLine;
        try
        {
            var script = new List<TEvent>();
            foreach (uint modifiers in new[] { 0u, Keys.kbShift, Keys.kbCtrlShift, Keys.kbAltShift, Keys.kbMetaShift })
            {
                script.Add(Down(Keys.kbF1, modifiers));
                script.Add(Down(Keys.kbF10, modifiers));
            }
            script.Add(Down(Keys.kbShiftF1, Keys.kbShift));
            script.Add(Down(Keys.kbCtrlF10, Keys.kbCtrlShift));
            script.Add(Down(Keys.kbAltF10, Keys.kbAltShift));
            script.Add(Down(Keys.kbAltF3, Keys.kbAltShift));
            script.Add(Down(Keys.kbF5));
            script.Add(Down(Keys.kbCtrlF5, Keys.kbCtrlShift));
            script.Add(Down(Keys.kbCtrlF10, Keys.kbCtrlShift | Keys.kbMetaShift));
            script.Add(Down(Keys.kbEsc)); // recorded, does not close
            foreach (var ev in script) app.Input.Enqueue(ev);
            var cancel = new TEvent { What = Events.evCommand };
            cancel.message.command = Views.cmCancel;
            app.Input.Enqueue(cancel);
            var open = new TEvent { What = Events.evCommand };
            open.message.command = TVDemoCmd.cmFunctionKeysDlg;
            app.HandleEvent(ref open);
            Assert.Equal(Events.evNothing, open.What);
            Assert.Equal(script.Select(e => e.keyDown.keyCode), app.Received.Select(e => e.keyDown.keyCode));
            var model = app.Dialog!.View.Model;
            foreach (int n in new[] { 1, 10 })
                foreach (FunctionKeyColumn column in Enum.GetValues<FunctionKeyColumn>())
                    Assert.True(model.IsMarked(n, column), $"F{n} {column}");
            Assert.Contains($"Ctrl+{FunctionKeyDiagnosticsState.Meta}+F10", model.Other);
            Assert.Null(app.Dialog.owner);
            Assert.Same(status, app.StatusLine);
            // No Help window and no other window was opened by the recorded keys.
            int windows = 0;
            app.DeskTop!.ForEachView(v => { if (v is TWindow) windows++; });
            Assert.Equal(1, windows); // the welcome window

            app.Scripted = false;
            foreach (var (code, command) in new[] { (Keys.kbF10, Views.cmMenu), (Keys.kbF1, Views.cmHelp) })
            {
                var key = Down(code);
                app.PutEvent(ref key);
                TEvent after = default;
                app.GetEvent(ref after);
                Assert.Equal(Events.evCommand, after.What);
                Assert.Equal(command, after.message.command);
            }
            var nothing = new TEvent();
            app.PutEvent(ref nothing);
        }
        finally { app.ShutDown(); }
    }

    // 17: the Keyboard diagnostic is still its own, unchanged entry.
    [Fact]
    public void ExistingKeyboardDiagnosticIsUnchanged()
    {
        using var driver = new DriverScope();
        Assert.Equal((ushort)309, TVDemoCmd.cmKeyboardDlg);
        Assert.NotEqual(TVDemoCmd.cmKeyboardDlg, TVDemoCmd.cmFunctionKeysDlg);
        var keyboard = new KeyboardDiagnosticsDialog(() => KeyboardCapabilities.None);
        try
        {
            Assert.Equal("Keyboard Diagnostics", keyboard.title);
            Assert.Equal(KeyboardDiagnosticsView.BaseWidth + 2, keyboard.size.x);
            Assert.Equal(22, keyboard.size.y);
            Assert.DoesNotContain("Function", keyboard.title);
        }
        finally { keyboard.ShutDown(); }
    }
}
