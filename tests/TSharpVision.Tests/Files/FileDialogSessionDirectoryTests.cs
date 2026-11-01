using System.IO;
using TSharpVision.Constants;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Tests.Samples;
using Xunit;

namespace TSharpVision.Tests.Files;

// TV12k: an Open file dialog that ends with an accepted file name makes later file dialogs of the same program start
// in that file's directory. In memory, per program; the process working directory is never changed. Every dialog
// here runs the real modal loop (ExecView -> Execute -> Valid) inside a running TVDemo program.
[Collection("NonParallel")]
public sealed class FileDialogSessionDirectoryTests
{
    private const ushort Open = FileDialogOptions.fdOpenButton;
    private const ushort Save = FileDialogOptions.fdOKButton;

    private static string WithSeparator(string dir) => Path.TrimEndingDirectorySeparator(dir) + Path.DirectorySeparatorChar;

    // Runs a file dialog modally; inModal sees it open, then ends it (the step must queue an ending command or key).
    private static ushort Run(ScriptedDemoApp app, TFileDialog dlg, Action<TFileDialog> inModal)
    {
        app.Step(() => inModal(dlg));
        try { return app.DeskTop!.ExecView(dlg); }
        finally { dlg.ShutDown(); }
    }

    private static ushort Accept(ScriptedDemoApp app, string file, ushort options = Open, ushort command = Views.cmFileOpen)
        => Run(app, new TFileDialog("*.*", "Open", "~N~ame", options, 0), d =>
        {
            d.fileName!.SetData(file);
            ScriptedDemoApp.SendCommand(command);
        });

    // Opens a dialog, records where it started and what it lists, and closes it with Escape.
    private static (string Directory, List<string> Items, string Error) Probe(ScriptedDemoApp app, TFileDialog? dlg = null)
    {
        dlg ??= new TFileDialog("*.*", "Open", "~N~ame", Open, 0);
        (string, List<string>, string) seen = default;
        Run(app, dlg, d =>
        {
            var items = Enumerable.Range(0, d.fileList!.range).Select(i => d.fileList.GetText(i, 260) ?? "").ToList();
            seen = (d.Directory, items, d.LastError);
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        return seen;
    }

    [Theory]
    [InlineData(Views.cmFileOpen)] // Open button
    [InlineData(Views.cmOK)]       // double-click / OK
    public void AcceptedOpenMakesTheNextDialogStartInThatDirectoryWithoutChangingTheWorkingDirectory(ushort command)
    {
        using var cwd = new TempDirectory(); using var other = new TempDirectory();
        string file = other.CreateFile("picked.txt", "x");
        cwd.CreateFile("in-cwd.txt");
        using var scope = new DemoTestScope(); var app = scope.App;
        Directory.SetCurrentDirectory(cwd.Path);

        PathAssertions.Equal(WithSeparator(cwd.Path), Probe(app).Directory); // a fresh program keeps the original start

        Assert.Equal(command, Accept(app, file, command: command));
        PathAssertions.Equal(WithSeparator(other.Path), app.FileDialogSessionDirectory);

        var next = Probe(app);
        PathAssertions.Equal(WithSeparator(other.Path), next.Directory);
        Assert.Contains("picked.txt", next.Items);
        Assert.DoesNotContain("in-cwd.txt", next.Items);
        PathAssertions.Equal(cwd.Path, Directory.GetCurrentDirectory());
    }

    [Fact]
    public void CancelEscapeBrowsingAndSaveDialogsLeaveTheRememberedDirectory()
    {
        using var cwd = new TempDirectory(); using var first = new TempDirectory(); using var browsed = new TempDirectory();
        string file = first.CreateFile("a.txt");
        string saved = browsed.CreateFile("b.txt");
        using var scope = new DemoTestScope(); var app = scope.App;
        Directory.SetCurrentDirectory(cwd.Path);
        Accept(app, file);

        // Browse into another directory (a mask navigates, it does not accept), then cancel with Escape.
        ushort result = Run(app, new TFileDialog("*.*", "Open", "~N~ame", Open, 0), d =>
        {
            d.fileName!.SetData(Path.Combine(browsed.Path, "*.txt"));
            ScriptedDemoApp.SendCommand(Views.cmFileOpen);
            app.Step(() =>
            {
                PathAssertions.Equal(WithSeparator(browsed.Path), d.Directory); // navigation happened, the dialog stays open
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
        });
        Assert.Equal(Views.cmCancel, result);
        PathAssertions.Equal(WithSeparator(first.Path), app.FileDialogSessionDirectory);

        // The Cancel button.
        Run(app, new TFileDialog("*.*", "Open", "~N~ame", Open, 0), d =>
        {
            d.fileName!.SetData(saved);
            ScriptedDemoApp.SendCommand(Views.cmCancel);
        });
        PathAssertions.Equal(WithSeparator(first.Path), app.FileDialogSessionDirectory);

        // A Save-style dialog (OK button, no Open button) starts in the remembered directory but does not move it.
        Assert.Equal(Views.cmFileOpen, Accept(app, saved, Save));
        PathAssertions.Equal(WithSeparator(first.Path), app.FileDialogSessionDirectory);
        PathAssertions.Equal(WithSeparator(first.Path), Probe(app, new TFileDialog("*.*", "Save", "~N~ame", Save, 0)).Directory);
    }

    [Fact]
    public void RejectedNamesKeepTheDialogOpenAndRememberNothing()
    {
        using var cwd = new TempDirectory(); using var dir = new TempDirectory();
        using var scope = new DemoTestScope(); var app = scope.App;
        Directory.SetCurrentDirectory(cwd.Path);

        ushort result = Run(app, new TFileDialog("*.*", "Open", "~N~ame", Open, 0), d =>
        {
            d.fileName!.SetData(WithSeparator(dir.Path) + "*"); // a mask: navigation only
            ScriptedDemoApp.SendCommand(Views.cmFileOpen);
            app.Step(() =>
            {
                d.fileName!.SetData(dir.Path); // an existing directory: navigation only
                ScriptedDemoApp.SendCommand(Views.cmFileOpen);
                app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc)));
            });
        });
        Assert.Equal(Views.cmCancel, result);
        Assert.Null(app.FileDialogSessionDirectory);
    }

    [Fact]
    public void ExplicitStartLocationsWinOverTheRememberedDirectory()
    {
        using var cwd = new TempDirectory(); using var remembered = new TempDirectory(); using var chosen = new TempDirectory();
        string file = remembered.CreateFile("r.txt");
        chosen.CreateFile("c.txt");
        using var scope = new DemoTestScope(); var app = scope.App;
        Directory.SetCurrentDirectory(cwd.Path);
        Accept(app, file);

        // SetData with a mask that has a directory part.
        var viaMask = new TFileDialog("*.*", "Open", "~N~ame", Open, 0);
        viaMask.SetData(Path.Combine(chosen.Path, "*.txt"));
        PathAssertions.Equal(WithSeparator(chosen.Path), Probe(app, viaMask).Directory);

        // SetData with a full file name (Save As passes the current file name this way).
        var viaFile = new TFileDialog("*.*", "Save", "~N~ame", Save, 0);
        viaFile.SetData(Path.Combine(chosen.Path, "c.txt"));
        PathAssertions.Equal(WithSeparator(cwd.Path), Probe(app, viaFile).Directory); // the listing keeps its original start

        // The directory field assigned before the dialog is shown.
        var viaField = new TFileDialog("*.*", "Open", "~N~ame", Open, 0) { directory = WithSeparator(chosen.Path) };
        PathAssertions.Equal(WithSeparator(chosen.Path), Probe(app, viaField).Directory);

        // A plain file name has no directory part: the remembered directory still applies and resolves it.
        var plain = new TFileDialog("*.*", "Save", "~N~ame", Save, 0);
        plain.SetData("new.txt");
        string resolved = "";
        Run(app, plain, d => { d.GetData(out resolved); ComboInput.Send(ComboInput.Key(Keys.kbEsc)); });
        PathAssertions.Equal(Path.Combine(remembered.Path, "new.txt"), resolved);
    }

    [Fact]
    public void DeletedRememberedDirectoryFallsBackToTheOriginalStartSilently()
    {
        using var cwd = new TempDirectory();
        var gone = new TempDirectory();
        string file = gone.CreateFile("x.txt");
        using var scope = new DemoTestScope(); var app = scope.App;
        Directory.SetCurrentDirectory(cwd.Path);
        Accept(app, file);
        gone.Dispose();
        Assert.False(Directory.Exists(gone.Path));

        var probe = Probe(app);
        PathAssertions.Equal(WithSeparator(cwd.Path), probe.Directory);
        Assert.Equal(string.Empty, probe.Error);
    }

    [Fact]
    public void NoLoadDirDialogTakesTheDirectoryWithoutListingIt()
    {
        using var cwd = new TempDirectory(); using var dir = new TempDirectory();
        string file = dir.CreateFile("x.txt");
        using var scope = new DemoTestScope(); var app = scope.App;
        Directory.SetCurrentDirectory(cwd.Path);
        Accept(app, file);

        var probe = Probe(app, new TFileDialog("*.*", "Open", "~N~ame", (ushort)(Open | FileDialogOptions.fdNoLoadDir), 0));
        PathAssertions.Equal(WithSeparator(dir.Path), probe.Directory);
        Assert.DoesNotContain("x.txt", probe.Items);
    }

    [Fact]
    public void ProgramsDoNotShareTheRememberedDirectory()
    {
        using var cwd = new TempDirectory(); using var dir = new TempDirectory();
        string file = dir.CreateFile("x.txt");
        using (var first = new DemoTestScope())
        {
            Directory.SetCurrentDirectory(cwd.Path);
            Accept(first.App, file);
            PathAssertions.Equal(WithSeparator(dir.Path), first.App.FileDialogSessionDirectory);
        }
        using var second = new DemoTestScope();
        Directory.SetCurrentDirectory(cwd.Path);
        Assert.Null(second.App.FileDialogSessionDirectory);
        PathAssertions.Equal(WithSeparator(cwd.Path), Probe(second.App).Directory);
    }

    [Fact]
    public void DialogOutsideAProgramUsesTheOriginalBehavior()
    {
        using var cwd = new TempDirectory();
        using var driver = new DriverScope();
        string saved = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(cwd.Path);
            var group = new TGroup(new TRect(0, 0, 80, 25));
            var dlg = new TFileDialog("*.*", "Open", "~N~ame", Open, 0);
            group.Insert(dlg);
            PathAssertions.Equal(WithSeparator(cwd.Path), dlg.Directory);
            dlg.ShutDown();
        }
        finally { Directory.SetCurrentDirectory(saved); }
    }

    // Exact path spelling (case, Unicode) is kept; on case-sensitive file systems differently cased names stay distinct.
    [Fact]
    public void UnicodeAndCaseOfTheAcceptedDirectoryAreKeptExactly()
    {
        using var cwd = new TempDirectory(); using var root = new TempDirectory();
        string dir = root.CreateSubDir("Žluťoučký Kůň");
        string file = Path.Combine(dir, "Dokument ěščř.txt");
        File.WriteAllText(file, "x");
        bool caseSensitive = !Directory.Exists(Path.Combine(root.Path, "žluťoučký kůň"));
        string? lower = null;
        if (caseSensitive) { lower = root.CreateSubDir("žluťoučký kůň"); File.WriteAllText(Path.Combine(lower, "other.txt"), "y"); }
        using var scope = new DemoTestScope(); var app = scope.App;
        Directory.SetCurrentDirectory(cwd.Path);

        Accept(app, file);
        Assert.Equal(WithSeparator(dir), app.FileDialogSessionDirectory, StringComparer.Ordinal);
        var probe = Probe(app);
        Assert.Equal(WithSeparator(dir), probe.Directory, StringComparer.Ordinal);
        Assert.Contains("Dokument ěščř.txt", probe.Items);
        if (caseSensitive) Assert.DoesNotContain("other.txt", probe.Items);
    }

    [Fact]
    public void LongAcceptedDirectoryIsRemembered()
    {
        using var cwd = new TempDirectory(); using var root = new TempDirectory();
        string dir = root.Path;
        while (dir.Length < 300) dir = Path.Combine(dir, new string('d', 40));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "long.txt");
        File.WriteAllText(file, "x");
        using var scope = new DemoTestScope(); var app = scope.App;
        Directory.SetCurrentDirectory(cwd.Path);

        Accept(app, file);
        PathAssertions.Equal(WithSeparator(dir), Probe(app).Directory);
    }
}
