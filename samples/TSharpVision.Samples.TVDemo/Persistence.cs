using TSharpVision.Constants;
namespace TSharpVision.Samples.TVDemo;
public partial class TVDemoApp
{
    private static string DesktopPath => Path.Combine(AppContext.BaseDirectory, "tvdemo-desktop.tvr");

    private void SaveDesktopState()
    {
        try
        {
            var windows = new List<TWindow>();
            int skipped = 0;
            DeskTop.ForEachView(v => { if (v is TWindow w) { if (DesktopState.Supports(w)) windows.Add(w); else skipped++; } });
            DesktopState.Save(DesktopPath, windows, GetPalette().Data);
            MsgBox.MessageBox(DeskTop, $"Saved {windows.Count} demonstration windows and palette.\nSkipped {skipped} unsupported windows (games/accessories/viewers).",
                MsgBox.mfInformation | MsgBox.mfOKButton);
        }
        catch (Exception ex) { PersistenceError(ex); }
    }

    private void LoadDesktopState()
    {
        try
        {
            var snapshot = DesktopState.Load(DesktopPath, GetPalette().Data.Length);
            var existing = new List<TWindow>();
            DeskTop.ForEachView(v => { if (v is TWindow w && DesktopState.Supports(w)) existing.Add(w); });
            foreach (var window in existing) window.ShutDown();
            for (int i = snapshot.Windows.Count - 1; i >= 0; i--) DeskTop.Insert(snapshot.Windows[i]);
            Array.Copy(snapshot.Palette, GetPalette().Data, snapshot.Palette.Length);
            _nextWinNum = (ushort)(snapshot.Windows.Select(w => (int)w.number).DefaultIfEmpty(0).Max() + 1);
            DrawView();
            MsgBox.MessageBox(DeskTop, $"Restored {snapshot.Windows.Count} demonstration windows and palette.\nOther windows remain open.",
                MsgBox.mfInformation | MsgBox.mfOKButton);
        }
        catch (Exception ex) { PersistenceError(ex); }
    }

    private void LoadPalette()
    {
        if (!File.Exists(DesktopPath)) return;
        try
        {
            var snapshot = DesktopState.Load(DesktopPath, GetPalette().Data.Length);
            foreach (var window in snapshot.Windows) window.ShutDown();
            Array.Copy(snapshot.Palette, GetPalette().Data, snapshot.Palette.Length);
            DrawView();
        }
        catch (Exception ex) { PersistenceError(ex); }
    }

    private void PersistenceError(Exception ex) => MsgBox.MessageBox(DeskTop,
        $"Persistence failed:\n{ex.Message}", MsgBox.mfError | MsgBox.mfOKButton);

    private void LoadResourceDialog()
    {
        try
        {
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
            dialog.MoveTo((DeskTop.size.x - dialog.size.x) / 2, (DeskTop.size.y - dialog.size.y) / 2);
            dialog.helpCtx = DemoHelpCtx.ResourceDialog;
            DeskTop.ExecView(dialog);
        }
        catch (Exception ex) { PersistenceError(ex); }
    }
}

