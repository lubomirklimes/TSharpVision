using System.Text;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Tests.Dialogs;

// TFileDialog Help + Encoding layout, and Help/F1 from modal TFileDialog and TChDirDialog.
[Collection("NonParallel")]
public sealed class FileDialogLayoutAndModalHelpTests : IDisposable
{
    private const ushort DialogTopic = 42;
    private const ushort Open = FileDialogOptions.fdOpenButton, Ok = FileDialogOptions.fdOKButton,
        Help = FileDialogOptions.fdHelpButton, Enc = FileDialogOptions.fdEncodingSelector;

    private readonly ITestOutputHelper _output;
    private readonly StreamableRegistryScope _streams = new();
    private readonly TempDirectory _tmp = new();

    public FileDialogLayoutAndModalHelpTests(ITestOutputHelper output)
    {
        _output = output;
        ComboInput.ResetQueue();
    }

    public void Dispose()
    {
        ComboInput.ResetQueue();
        _tmp.Dispose();
        _streams.Dispose();
    }

    private static TView[] Children(TGroup group)
    {
        var views = new List<TView>();
        group.ForEachView(v => { if (v is not TFrame) views.Add(v); });
        return views.ToArray();
    }

    private static TButton Button(TGroup group, ushort command) =>
        Assert.Single(Children(group).OfType<TButton>(), b => b.Command == command);

    private static string LabelText(TLabel label)
    {
        label.GetText(out string text);
        return text;
    }

    private static TLabel EncodingLabel(TFileDialog dialog) =>
        Assert.Single(Children(dialog).OfType<TLabel>(), l => LabelText(l).Contains("ncoding"));

    private static string Describe(TView v) => v switch
    {
        TButton b => "TButton " + b.Title,
        TLabel l => "TLabel " + LabelText(l),
        _ => v.GetType().Name,
    };

    private static void AssertNoOverlapsInside(TGroup dialog)
    {
        var views = Children(dialog);
        foreach (var view in views)
        {
            var r = view.GetBounds();
            Assert.True(r.a.x >= 1 && r.a.y >= 1 && r.b.x <= dialog.size.x - 1 && r.b.y <= dialog.size.y - 1,
                $"{Describe(view)} [{r.a.x},{r.a.y})-({r.b.x},{r.b.y}) leaves the {dialog.size.x}x{dialog.size.y} interior");
        }
        for (int i = 0; i < views.Length; i++)
            for (int j = i + 1; j < views.Length; j++)
            {
                TRect r = views[i].GetBounds();
                r.Intersect(views[j].GetBounds());
                Assert.True(r.IsEmpty(), $"{Describe(views[i])} overlaps {Describe(views[j])} at [{r.a.x},{r.a.y})-({r.b.x},{r.b.y})");
            }
    }

    public static IEnumerable<object[]> EncodingCombinations() => new[]
    {
        new object[] { (ushort)(Open | Help | Enc) },
        new object[] { (ushort)(Ok | Help | Enc) },
        new object[] { (ushort)(Open | Enc) },
        new object[] { (ushort)(Ok | Enc) },
        new object[] { (ushort)(Open | FileDialogOptions.fdReplaceButton | Help | Enc) },
        new object[] { (ushort)(Open | FileDialogOptions.fdReplaceButton | FileDialogOptions.fdClearButton | Help | Enc) },
        new object[] { (ushort)(Open | Help) },
        new object[] { (ushort)(Ok | Help) },
        new object[] { (ushort)Open },
    };

    [Theory]
    [MemberData(nameof(EncodingCombinations))]
    public void ConstructedControlsNeverOverlapAndStayInside(ushort options)
    {
        using var driver = new DriverScope(160, 50);
        var dialog = new TFileDialog("*.*", "Probe", "~N~ame", options, 100);
        try
        {
            AssertNoOverlapsInside(dialog);
            if ((options & Enc) != 0)
            {
                var label = EncodingLabel(dialog);
                var radios = dialog.encodingSelector!;
                Assert.Same(radios, label.Link);
                Assert.Equal(radios.GetBounds().a.y - 1, label.GetBounds().a.y);
                Assert.Equal(EditorEncodingChoices.BuiltIn.Count, radios.size.y);
                int lastButton = Children(dialog).OfType<TButton>().Max(b => b.GetBounds().b.y);
                Assert.True(label.GetBounds().a.y >= lastButton);
            }
            var min = new TPoint(); var max = new TPoint();
            dialog.SizeLimits(ref min, ref max);
            Assert.Equal(dialog.size, min);
        }
        finally { dialog.ShutDown(); }
    }

    [Theory]
    [InlineData((ushort)(Open | Enc))]
    [InlineData((ushort)(Open | Help))]
    [InlineData((ushort)Open)]
    [InlineData((ushort)(Ok | Enc))]
    public void LayoutWithoutTheConflictIsUnchanged(ushort options)
    {
        using var driver = new DriverScope();
        var dialog = new TFileDialog("*.*", "Probe", "~N~ame", options, 100);
        try
        {
            bool enc = (options & Enc) != 0;
            Assert.Equal(enc ? new TPoint(65, 20) : new TPoint(49, 20), dialog.size);
            Assert.Equal(new TRect(3, 5, 34, 16), dialog.fileList!.GetBounds());
            // With encoding the info pane spans the 65-column inner width; without it, 49 columns as before.
            Assert.Equal(new TRect(1, 16, enc ? 64 : 48, 19), Assert.Single(Children(dialog).OfType<TFileInfoPane>()).GetBounds());
            if (enc)
            {
                Assert.Equal(new TRect(36, 6, 50, 7), EncodingLabel(dialog).GetBounds());
                Assert.Equal(new TRect(36, 7, 64, 15), dialog.encodingSelector!.GetBounds());
            }
            if ((options & Help) != 0) Assert.Equal(new TRect(35, 6, 46, 8), Button(dialog, Views.cmHelp).GetBounds());
        }
        finally { dialog.ShutDown(); }
    }

    [Fact]
    public void HelpAndEncodingStackBelowTheButtonsAndGrowTheDialogByOneRow()
    {
        using var driver = new DriverScope();
        var dialog = new TFileDialog("*.*", "Probe", "~N~ame", (ushort)(Open | Help | Enc), 100);
        try
        {
            Assert.Equal(new TPoint(65, 21), dialog.size);
            Assert.Equal(new TRect(35, 6, 46, 8), Button(dialog, Views.cmHelp).GetBounds());
            Assert.Equal(new TRect(36, 8, 50, 9), EncodingLabel(dialog).GetBounds());
            Assert.Equal(new TRect(36, 9, 64, 17), dialog.encodingSelector!.GetBounds());
            Assert.Equal(new TRect(3, 5, 34, 17), dialog.fileList!.GetBounds());
            Assert.Equal(new TRect(1, 17, 64, 20), Assert.Single(Children(dialog).OfType<TFileInfoPane>()).GetBounds());
        }
        finally { dialog.ShutDown(); }
    }

    // The info pane spans the inner width, and a blank column separates the list's scroll bar from the
    // encoding choices.
    [Theory]
    [MemberData(nameof(EncodingCombinations))]
    public void InfoPaneFillsTheInnerWidthAndEncodingKeepsAGapColumn(ushort options)
    {
        using var driver = new DriverScope(160, 50);
        var dialog = new TFileDialog("*.*", "Probe", "~N~ame", options, 100);
        try
        {
            var info = Assert.Single(Children(dialog).OfType<TFileInfoPane>());
            Assert.Equal(1, info.GetBounds().a.x);
            Assert.Equal(dialog.size.x - 1, info.GetBounds().b.x);
            if ((options & Enc) != 0)
            {
                int scrollRight = dialog.fileList!.vScrollBar!.GetBounds().b.x;
                int gap = scrollRight; // the first column right of the scroll bar
                foreach (TView v in new TView[] { EncodingLabel(dialog), dialog.encodingSelector! })
                    Assert.True(v.GetBounds().a.x >= gap + 1, $"{Describe(v)} starts at {v.GetBounds().a.x}; gap column {gap} must stay empty");
                Assert.Equal(dialog.size.x - 1, dialog.encodingSelector!.GetBounds().b.x);
                foreach (TView v in Children(dialog))
                {
                    var r = v.GetBounds();
                    if (r.a.y >= EncodingLabel(dialog).GetBounds().a.y && r.a.y < dialog.encodingSelector.GetBounds().b.y
                        && r.a.x <= gap && r.b.x > gap)
                        Assert.Fail($"{Describe(v)} covers gap column {gap}");
                }
            }

            // Still the inner width after the dialog grows.
            dialog.ChangeBounds(new TRect(0, 0, dialog.size.x + 20, dialog.size.y + 4));
            Assert.Equal(dialog.size.x - 1, info.GetBounds().b.x);
        }
        finally { dialog.ShutDown(); }
    }

    [Theory]
    [InlineData(80, 25)]
    [InlineData(120, 36)]
    [InlineData(140, 40)]
    public void RenderedGapColumnIsBlankAndInfoPaneReachesTheFrame(int width, int height)
    {
        foreach (ushort options in new[] { (ushort)(Open | Help | Enc), (ushort)(Ok | Help | Enc) })
        {
            using var driver = new DriverScope((ushort)width, (ushort)height);
            var app = new ModalHelpApp(null);
            var dialog = new TFileDialog("*.*", "Probe", "~N~ame", options, 100);
            try
            {
                app.DeskTop!.Insert(dialog);
                app.Redraw();
                TPoint o = dialog.MakeGlobal(new TPoint(0, 0));
                char Cell(int x, int y) => TScreen.ScreenBuffer!.Data[(o.y + y) * width + o.x + x].Character;
                int gap = dialog.fileList!.vScrollBar!.GetBounds().b.x;
                var label = EncodingLabel(dialog).GetBounds();
                var radios = dialog.encodingSelector!.GetBounds();
                _output.WriteLine($"0x{options:X4} {width}x{height}: gap column {gap}, label [{label.a.x},{label.a.y})-({label.b.x},{label.b.y}), " +
                    $"radios [{radios.a.x},{radios.a.y})-({radios.b.x},{radios.b.y}), info right {Assert.Single(Children(dialog).OfType<TFileInfoPane>()).GetBounds().b.x}");
                for (int y = label.a.y; y < radios.b.y; y++)
                    Assert.True(Cell(gap, y) == ' ', $"row {y}: gap column {gap} shows '{Cell(gap, y)}'");
                // The info pane paints up to the frame: its last interior column is drawn by the pane (its background), not by the dialog.
                var info = Assert.Single(Children(dialog).OfType<TFileInfoPane>());
                int infoRow = info.GetBounds().a.y;
                Assert.Equal(dialog.size.x - 1, info.GetBounds().b.x);
                var paneAttr = TScreen.ScreenBuffer!.Data[(o.y + infoRow) * width + o.x + 2].Attr;
                Assert.Equal(paneAttr, TScreen.ScreenBuffer.Data[(o.y + infoRow) * width + o.x + dialog.size.x - 2].Attr);
                Assert.Equal('║', Cell(dialog.size.x - 1, infoRow));
            }
            finally
            {
                dialog.ShutDown();
                app.ShutDown();
            }
        }
    }

    [Theory]
    [InlineData(80, 25)]
    [InlineData(120, 36)]
    [InlineData(140, 40)]
    [InlineData(160, 50)]
    public void RenderedDialogShowsHelpAndEveryEncoding(int width, int height)
    {
        foreach (ushort options in new[] { (ushort)(Open | Help | Enc), (ushort)(Ok | Help | Enc) })
        {
            using var driver = new DriverScope((ushort)width, (ushort)height);
            var app = new ModalHelpApp(null);
            var dialog = new TFileDialog("*.*", "Probe", "~N~ame", options, 100);
            try
            {
                app.DeskTop!.Insert(dialog);
                app.Redraw();
                TPoint o = dialog.MakeGlobal(new TPoint(0, 0));
                Assert.True(o.x >= 0 && o.y >= 1 && o.x + dialog.size.x <= width && o.y + dialog.size.y <= height - 1);
                var rows = Enumerable.Range(o.y, dialog.size.y).Select(y => new string(Enumerable.Range(o.x, dialog.size.x)
                    .Select(x => TScreen.ScreenBuffer!.Data[y * width + x].Character).ToArray())).ToArray();
                _output.WriteLine($"0x{options:X4} {width}x{height} origin=({o.x},{o.y}) size=({dialog.size.x},{dialog.size.y})");
                foreach (string row in rows) _output.WriteLine("  |" + new string(row.Select(c => c < ' ' ? ' ' : c).ToArray()) + "|");
                var help = Button(dialog, Views.cmHelp).GetBounds();
                Assert.Contains("Help", rows[help.a.y].Substring(help.a.x, help.b.x - help.a.x));
                Assert.Contains("Encoding", rows[EncodingLabel(dialog).GetBounds().a.y]);
                foreach (var choice in EditorEncodingChoices.BuiltIn)
                    Assert.Contains(rows, r => r.Contains(choice.Label));
            }
            finally
            {
                dialog.ShutDown();
                app.ShutDown();
            }
        }
    }

    [Fact]
    public void TabMouseAndHotkeysReachHelpAndEncoding()
    {
        using var driver = new DriverScope();
        var app = new ModalHelpApp(null);
        var dialog = new TFileDialog("*.*", "Probe", "~N~ame", (ushort)(Open | Help | Enc), 100);
        try
        {
            var help = Button(dialog, Views.cmHelp);
            var radios = dialog.encodingSelector!;
            var seen = new List<TView>();
            app.Step(() =>
            {
                for (int i = 0; i < 12; i++)
                {
                    seen.Add(dialog.current!);
                    dialog.SelectNext(false);
                }
                Assert.Contains(help, seen);
                Assert.Contains(radios, seen);
                Assert.True(seen.IndexOf(help) < seen.IndexOf(radios));
                // Alt+E (label hotkey) focuses the encoding choices.
                dialog.fileName!.Select();
                ComboInput.Send(ComboInput.Key(Keys.kbAltE));
            });
            app.Step(() =>
            {
                Assert.Same(radios, dialog.current);
                // A mouse click on the fourth choice selects it.
                TPoint item = radios.MakeGlobal(new TPoint(2, 3));
                ComboInput.Send(ComboInput.Mouse(Events.evMouseDown, item), ComboInput.Mouse(Events.evMouseUp, item));
            });
            app.Step(() =>
            {
                Assert.Equal(3u, radios.value);
                // A click inside the Help button now reaches it (no help: the dialog stays open).
                TPoint inside = help.MakeGlobal(new TPoint(3, 0));
                Assert.Same(help, FindAt(app, inside));
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
            Assert.Equal(Views.cmCancel, app.DeskTop!.ExecView(dialog));
        }
        finally
        {
            dialog.ShutDown();
            app.ShutDown();
        }
    }

    private static TView? FindAt(TGroup root, TPoint global)
    {
        TView? hit = null;
        void Walk(TGroup g)
        {
            TView? top = null;
            g.ForEachView(v =>
            {
                if (top == null && (v.state & Views.sfVisible) != 0 && v.MouseInView(global)) top = v;
            });
            if (top == null) return;
            hit = top;
            if (top is TGroup child) Walk(child);
        }
        Walk(root);
        return hit;
    }

    // ── Modal Help ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HelpButtonAndF1OpenTheDialogTopicAndReturnToTheSameDialogState(bool useF1)
    {
        using var help = BuildHelpFile();
        using var driver = new DriverScope();
        var app = new ModalHelpApp(help.File);
        var dialog = new TFileDialog("*.*", "Probe", "~N~ame", (ushort)(Open | Help | Enc), 100) { helpCtx = DialogTopic };
        try
        {
            app.Step(() =>
            {
                dialog.fileName!.SetData("draft.txt");
                dialog.encodingSelector!.SetData((ushort)4);
                dialog.encodingSelector.Select();
                if (useF1) ComboInput.Send(ComboInput.Key(Keys.kbF1));
                else Button(dialog, Views.cmHelp).Press();
            });
            app.Step(() =>
            {
                var window = Assert.IsType<THelpWindow>(TView.ModalTopView);
                Assert.Contains("File dialog topic", THelpCollection.PlainText(window.Viewer!.topic));
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
            app.Step(() =>
            {
                Assert.Same(dialog, TView.ModalTopView);
                Assert.Equal("draft.txt", dialog.fileName!.Data);
                Assert.Equal(4u, dialog.encodingSelector!.value);
                if (useF1) Assert.Same(dialog.encodingSelector, dialog.current);
                Assert.Empty(ScriptedHelpWindows(app));
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
            Assert.Equal(Views.cmCancel, app.DeskTop!.ExecView(dialog));
            Assert.Equal(0, app.NonModalHelpCount);
        }
        finally
        {
            dialog.ShutDown();
            app.ShutDown();
        }
    }

    [Fact]
    public void ChangeDirectoryHelpButtonOpensItsTopic()
    {
        using var help = BuildHelpFile();
        using var driver = new DriverScope();
        var app = new ModalHelpApp(help.File);
        var dialog = new TChDirDialog(ChDirDialogOptions.cdHelpButton | ChDirDialogOptions.cdNoLoadDir, 100) { helpCtx = DialogTopic };
        try
        {
            app.Step(() => Button(dialog, Views.cmHelp).Press());
            app.Step(() =>
            {
                var window = Assert.IsType<THelpWindow>(TView.ModalTopView);
                Assert.Contains("File dialog topic", THelpCollection.PlainText(window.Viewer!.topic));
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
            app.Step(() =>
            {
                Assert.Same(dialog, TView.ModalTopView);
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
            Assert.Equal(Views.cmCancel, app.DeskTop!.ExecView(dialog));
        }
        finally
        {
            dialog.ShutDown();
            app.ShutDown();
        }
    }

    [Fact]
    public void WithoutHelpTheButtonDoesNothingAndTheDialogStaysUsable()
    {
        using var driver = new DriverScope();
        var app = new ModalHelpApp(null);
        var dialog = new TFileDialog("*.*", "Probe", "~N~ame", (ushort)(Open | Help | Enc), 100) { helpCtx = DialogTopic };
        try
        {
            app.Step(() => Button(dialog, Views.cmHelp).Press());
            app.Step(() =>
            {
                Assert.Same(dialog, TView.ModalTopView);
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
            Assert.Equal(Views.cmCancel, app.DeskTop!.ExecView(dialog));
            Assert.Empty(ScriptedHelpWindows(app));
        }
        finally
        {
            dialog.ShutDown();
            app.ShutDown();
        }
    }

    [Fact]
    public void NonModalDialogStillUsesTheProgramsHelpWindow()
    {
        using var help = BuildHelpFile();
        using var driver = new DriverScope();
        var app = new ModalHelpApp(help.File);
        var dialog = new TFileDialog("*.*", "Probe", "~N~ame", (ushort)(Open | Help), 100) { helpCtx = DialogTopic };
        try
        {
            app.DeskTop!.Insert(dialog);
            var ev = new TEvent { What = Events.evCommand };
            ev.message.command = Views.cmHelp;
            app.HandleEvent(ref ev);
            Assert.Equal(Events.evNothing, ev.What);
            Assert.Equal(1, app.NonModalHelpCount);
        }
        finally
        {
            dialog.ShutDown();
            app.ShutDown();
        }
    }

    private static THelpWindow[] ScriptedHelpWindows(TProgram app)
    {
        var list = new List<THelpWindow>();
        app.DeskTop!.ForEachView(v => { if (v is THelpWindow w) list.Add(w); });
        return list.ToArray();
    }

    private HelpFileHandle BuildHelpFile()
    {
        THelpFile.RegisterStreamableTypes();
        string path = Path.Combine(_tmp.Path, Guid.NewGuid().ToString("N") + ".hlp");
        var fpw = new Fpstream(path);
        var hfw = new THelpFile(fpw);
        foreach (var (ctx, text) in new[] { (THelpViewer.IndexContext, "Index topic.\n"), (DialogTopic, "File dialog topic.\n") })
        {
            var topic = new THelpTopic();
            byte[] bytes = Encoding.Latin1.GetBytes(text);
            topic.AddParagraph(new TParagraph { text = bytes, size = (ushort)bytes.Length, wrap = false });
            hfw.RecordPositionInIndex(ctx);
            hfw.PutTopic(topic);
        }
        hfw.Flush();
        fpw.Close();
        var fpr = new Fpstream(path);
        return new HelpFileHandle(fpr, new THelpFile(fpr));
    }

    private sealed class HelpFileHandle : IDisposable
    {
        private readonly Fpstream _stream;
        public HelpFileHandle(Fpstream stream, THelpFile file) { _stream = stream; File = file; }
        public THelpFile File { get; }
        public void Dispose() => _stream.Close();
    }

    // A real application loop with a status line binding F1; steps run once queued input is delivered.
    private sealed class ModalHelpApp : TApplication
    {
        private readonly Queue<Action> _steps = new();
        private readonly THelpFile? _help;
        private int _empty;

        public ModalHelpApp(THelpFile? help) { _help = help; Pending = default; }
        public int NonModalHelpCount { get; private set; }
        public void Step(Action step) => _steps.Enqueue(step);
        public override THelpFile? GetHelpFile() => _help;
        protected override int GetIdleWaitTimeout() => 0;

        protected override void ExecuteHelp(THelpWindow window)
        {
            NonModalHelpCount++;
            base.ExecuteHelp(window);
        }

        public override TStatusLine InitStatusLine(TRect r)
        {
            r.a.y = r.b.y - 1;
            return new TStatusLine(r, new TStatusDef(0, 0xFFFF) + new TStatusItem("~F1~ Help", Keys.kbF1, Views.cmHelp));
        }

        public override void GetEvent(ref TEvent ev)
        {
            base.GetEvent(ref ev);
            if (ev.What != Events.evNothing) { _empty = 0; return; }
            if (_steps.Count > 0) { _empty = 0; _steps.Dequeue()(); return; }
            if (++_empty > 100) throw new InvalidOperationException("Script exhausted with " + TView.ModalTopView?.GetType().Name + " open");
        }
    }
}
