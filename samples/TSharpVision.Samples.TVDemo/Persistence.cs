using TSharpVision.Constants;
namespace TSharpVision.Samples.TVDemo;
public partial class TVDemoApp
{
    // Tests point persistence at a private file; null means the file next to the executable.
    internal static string? DesktopPathOverride { get; set; }
    internal static string DesktopPath => DesktopPathOverride ?? Path.Combine(AppContext.BaseDirectory, "tvdemo-desktop.tvr");

    private static readonly AP[] PaletteFamilies = { AP.Color, AP.BlackWhite, AP.Monochrome };

    // The three family tables are process-wide statics shared by every TProgram. TVDemo owns them while it runs: it
    // copies them when it starts (before reading the snapshot) and puts that copy back when it shuts down, so a
    // snapshot or a color edit never outlives this program in the process. Nothing is saved automatically.
    private byte[][]? _paletteTablesAtStart;

    private void CapturePaletteTablesAtStart() =>
        _paletteTablesAtStart = PaletteFamilies.Select(m => PaletteTable(m).Data.ToArray()).ToArray();

    // Called from ShutDown (Help.cs holds the single TVDemoApp.ShutDown override).
    private void RestorePaletteTablesAtStart()
    {
        if (_paletteTablesAtStart == null) return;
        for (int i = 0; i < PaletteFamilies.Length; i++)
            Array.Copy(_paletteTablesAtStart[i], PaletteTable(PaletteFamilies[i]).Data, _paletteTablesAtStart[i].Length);
        _paletteTablesAtStart = null;
    }

    // The live table of a family (TProgram.GetPalette resolves the table of AppPalette), read without repainting.
    internal TPalette PaletteTable(AP mode)
    {
        AP active = AppPalette;
        AppPalette = mode;
        try { return GetPalette(); }
        finally { AppPalette = active; }
    }

    // Copies of all three live tables and the active family: the family's own table is never written into another.
    internal DesktopPalettes CapturePalettes() => new(AppPalette,
        PaletteTable(AP.Color).Data.ToArray(), PaletteTable(AP.BlackWhite).Data.ToArray(), PaletteTable(AP.Monochrome).Data.ToArray());

    // Applies an already validated snapshot: each recorded table into its own family, then the family itself.
    private void ApplyPalettes(DesktopPalettes palettes)
    {
        foreach (var mode in PaletteFamilies)
            if (palettes.Table(mode) is byte[] table) Array.Copy(table, PaletteTable(mode).Data, table.Length);
        bool switching = AppPalette != palettes.Mode;
        SetAppPalette(palettes.Mode); // repaints when the family changes
        UpdatePaletteModeIndicators();
        if (!switching) Redraw();
    }

    // Save and load act only from the desktop. A modal dialog, such as Options > UI colors with a pending Try
    // preview, owns the live palette until it closes; while one runs the menu cannot reach these commands, and a
    // command sent by any other route is refused, so a preview that is later cancelled is never saved.
    private bool PersistenceBusy()
    {
        if (TopView() is not { } top || top == this) return false;
        MsgBox.MessageBox(DeskTop, "Close the open dialog before saving or loading the desktop state.",
            MsgBox.mfInformation | MsgBox.mfOKButton);
        return true;
    }

    private void SaveDesktopState()
    {
        try
        {
            TDeskTop? desktop = DeskTop;
            if (desktop == null || PersistenceBusy()) return;
            var windows = new List<TWindow>();
            int skipped = 0;
            desktop.ForEachView(v => { if (v is TWindow w) { if (DesktopState.Supports(w)) windows.Add(w); else skipped++; } });
            DesktopState.Save(DesktopPath, windows, CapturePalettes());
            MsgBox.MessageBox(DeskTop, $"Saved {windows.Count} demonstration windows and palette ({PaletteModeName(AppPalette)}).\nSkipped {skipped} unsupported windows (games/accessories/viewers).",
                MsgBox.mfInformation | MsgBox.mfOKButton);
        }
        catch (Exception ex) { PersistenceError(ex); }
    }

    private void LoadDesktopState()
    {
        try
        {
            TDeskTop? desktop = DeskTop;
            if (desktop == null || PersistenceBusy()) return;
            if (!File.Exists(DesktopPath))
            {
                MsgBox.MessageBox(DeskTop, "No desktop state has been saved yet.", MsgBox.mfInformation | MsgBox.mfOKButton);
                return;
            }
            // Load validates the whole file first; nothing below runs unless it succeeded.
            var snapshot = DesktopState.Load(DesktopPath);
            var existing = new List<TWindow>();
            desktop.ForEachView(v => { if (v is TWindow w && DesktopState.Supports(w)) existing.Add(w); });
            foreach (var window in existing) window.ShutDown();
            for (int i = snapshot.Windows.Count - 1; i >= 0; i--) desktop.Insert(snapshot.Windows[i]);
            ApplyPalettes(snapshot.Palettes);
            _nextWinNum = (ushort)(snapshot.Windows.Select(w => (int)w.number).DefaultIfEmpty(0).Max() + 1);
            MsgBox.MessageBox(DeskTop, $"Restored {snapshot.Windows.Count} demonstration windows and palette ({PaletteModeName(AppPalette)}{(snapshot.Legacy ? ", older snapshot" : "")}).\nOther windows remain open.",
                MsgBox.mfInformation | MsgBox.mfOKButton);
        }
        catch (Exception ex) { PersistenceError(ex); }
    }

    // Startup: only the palettes and the family are restored; the saved windows are discarded.
    private void LoadPalette()
    {
        if (!File.Exists(DesktopPath)) return;
        try
        {
            if (DeskTop == null) return;
            var snapshot = DesktopState.Load(DesktopPath);
            foreach (var window in snapshot.Windows) window.ShutDown();
            ApplyPalettes(snapshot.Palettes);
        }
        catch (Exception ex) { PersistenceError(ex); }
    }

    private static string PaletteModeName(AP mode) => mode switch
    {
        AP.BlackWhite => "black and white",
        AP.Monochrome => "monochrome",
        _ => "color",
    };

    private void PersistenceError(Exception ex) => MsgBox.MessageBox(DeskTop,
        $"Persistence failed:\n{ex.Message}", MsgBox.mfError | MsgBox.mfOKButton);

    private void LoadResourceDialog()
    {
        try
        {
            TDeskTop? desktop = DeskTop;
            if (desktop == null) return;
            StreamableRegistration.RegisterAll();
            string path = Path.Combine(AppContext.BaseDirectory, "tvdemo-dialog.tvr");
            TDialog dialog;
            using (var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                var stream = new Fpstream(file);
                var resources = new TResourceFile(stream);
                if (file.Length == 0)
                {
                    var original = BuildResourceSampleDialog();
                    resources.Put(original, "dialog");
                    resources.Flush();
                    original.ShutDown();
                }
                dialog = resources.Get("dialog") as TDialog ?? throw new InvalidDataException("Resource dialog is missing.");
                if (stream.In.Fail() != 0 || stream.Out.Fail() != 0) throw new InvalidDataException("Resource stream failed.");
            }
            CompactLegacyResourceDialog(dialog);
            dialog.MoveTo((desktop.size.x - dialog.size.x) / 2, (desktop.size.y - dialog.size.y) / 2);
            DemoButtonLayout.CenterRows(dialog);
            dialog.helpCtx = DemoHelpCtx.ResourceDialog;
            desktop.ExecView(dialog);
        }
        catch (Exception ex) { PersistenceError(ex); }
    }
}

