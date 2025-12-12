// Dialog/MsgBox/button/label localization + editor prompt string tests.
//
// Strategy:
//   - Construct dialogs headlessly (no running TProgram).
//   - Use IntlProviderScope + file-local DictStringProvider to inject test values.
//   - Inspect dialog.title and walk the view tree (ViewTreeHelper.ContainsText)
//     to find buttons/labels with expected strings.
//   - Verify default English provider for representative keys.
//   - Verify provider restore after scope disposal.
//   - Editor prompt string tests are pure string checks — no dialog construction needed.
//
// No golden screens. No interactive dialogs. No Demo apps.
using System.Collections.Generic;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Localization;

[Collection("NonParallel")]
public sealed class DialogLocalizationTests : IDisposable
{
    private readonly DriverScope _driver;

    public DialogLocalizationTests()
    {
        _driver = new DriverScope(80, 25);
    }

    public void Dispose() => _driver.Dispose();

    // ── Helper: walk dialog tree looking for exact text ───────────────────────

    private static bool DialogContains(TView root, string text)
    {
        bool Walk(TView v)
        {
            if (v is TWindow tw && tw.title == text) return true;
            if (v is TButton btn && btn.Title == text) return true;
            if (v is TStaticText st) { st.GetText(out string t); if (t == text) return true; }
            if (v is TGroup grp && grp.last != null)
            {
                TView p = grp.last.Next, start = p;
                do { if (Walk(p)) return true; p = p.Next; }
                while (p != null && p != start);
            }
            return false;
        }
        return Walk(root);
    }

    // =========================================================================
    // MsgBox.BuildMessageBox title + buttons
    // =========================================================================

    [Fact]
    public void MsgBox_DefaultTitle_Information_IsEnglish()
    {
        var dlg = MsgBox.BuildMessageBox(
            new TRect(10, 5, 50, 14), "Test",
            MsgBox.mfInformation | MsgBox.mfOKButton);
        Assert.Equal("Information", dlg.title);
    }

    [Fact]
    public void MsgBox_DefaultTitle_Error_IsEnglish()
    {
        var dlg = MsgBox.BuildMessageBox(
            new TRect(10, 5, 50, 14), "Test",
            MsgBox.mfError | MsgBox.mfOKButton);
        Assert.Equal("Error", dlg.title);
    }

    [Fact]
    public void MsgBox_DefaultTitle_Warning_IsEnglish()
    {
        var dlg = MsgBox.BuildMessageBox(
            new TRect(10, 5, 50, 14), "Test",
            MsgBox.mfWarning | MsgBox.mfOKButton);
        Assert.Equal("Warning", dlg.title);
    }

    [Fact]
    public void MsgBox_DefaultTitle_Confirm_IsEnglish()
    {
        var dlg = MsgBox.BuildMessageBox(
            new TRect(10, 5, 50, 14), "Test",
            MsgBox.mfConfirmation | MsgBox.mfOKButton);
        Assert.Equal("Confirm", dlg.title);
    }

    [Fact]
    public void MsgBox_CustomProvider_Title_Swapped()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["MsgTitle_Information"] = "INFO_TEST",
                ["Btn_OK"] = "OK_TEST",
            }));
        var dlg = MsgBox.BuildMessageBox(
            new TRect(10, 5, 50, 14), "Test",
            MsgBox.mfInformation | MsgBox.mfOKButton);
        Assert.Equal("INFO_TEST", dlg.title);
    }

    [Fact]
    public void MsgBox_CustomProvider_OKButton_Swapped()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["MsgTitle_Information"] = "INFO_TEST",
                ["Btn_OK"] = "OK_TEST",
            }));
        var dlg = MsgBox.BuildMessageBox(
            new TRect(10, 5, 50, 14), "Test",
            MsgBox.mfInformation | MsgBox.mfOKButton);
        Assert.True(DialogContains(dlg, "OK_TEST"));
    }

    [Fact]
    public void MsgBox_CustomProvider_Restored_Title()
    {
        using (new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string> { ["MsgTitle_Error"] = "ERR_SWAP" })))
        { /* scope */ }
        var dlg = MsgBox.BuildMessageBox(
            new TRect(10, 5, 50, 14), "Test",
            MsgBox.mfError | MsgBox.mfOKButton);
        Assert.Equal("Error", dlg.title);
    }

    [Fact]
    public void MsgBox_YesNoButtons_RouteThrough_Provider()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["MsgTitle_Confirm"] = "C",
                ["Btn_Yes"] = "YES_T",
                ["Btn_No"]  = "NO_T",
            }));
        var dlg = MsgBox.BuildMessageBox(
            new TRect(10, 5, 50, 14), "Test",
            MsgBox.mfConfirmation | MsgBox.mfYesButton | MsgBox.mfNoButton);
        Assert.True(DialogContains(dlg, "YES_T"));
        Assert.True(DialogContains(dlg, "NO_T"));
    }

    [Fact]
    public void MsgBox_CancelButton_RoutesThrough_Provider()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["MsgTitle_Warning"] = "W",
                ["Btn_OK"]     = "OK_T",
                ["Btn_Cancel"] = "CANCEL_T",
            }));
        var dlg = MsgBox.BuildMessageBox(
            new TRect(10, 5, 50, 14), "Test",
            MsgBox.mfWarning | MsgBox.mfOKButton | MsgBox.mfCancelButton);
        Assert.True(DialogContains(dlg, "CANCEL_T"));
    }

    // =========================================================================
    // MsgBox.BuildInputBox OK + Cancel buttons
    // =========================================================================

    [Fact]
    public void BuildInputBox_DefaultOKButton_IsEnglish()
    {
        var dlg = MsgBox.BuildInputBox(
            new TRect(5, 5, 45, 12), "Test", "~N~ame", "v", 40);
        Assert.True(DialogContains(dlg, "~O~K"));
    }

    [Fact]
    public void BuildInputBox_DefaultCancelButton_IsEnglish()
    {
        var dlg = MsgBox.BuildInputBox(
            new TRect(5, 5, 45, 12), "Test", "~N~ame", "v", 40);
        Assert.True(DialogContains(dlg, "Cancel"));
    }

    [Fact]
    public void BuildInputBox_CustomProvider_OKButton_Swapped()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["Btn_OK"] = "OK_TEST",
            }));
        var dlg = MsgBox.BuildInputBox(
            new TRect(5, 5, 45, 12), "Test", "~N~ame", "v", 40);
        Assert.True(DialogContains(dlg, "OK_TEST"));
    }

    [Fact]
    public void BuildInputBox_CustomProvider_CancelButton_Swapped()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["Btn_Cancel"] = "CANCEL_TEST",
            }));
        var dlg = MsgBox.BuildInputBox(
            new TRect(5, 5, 45, 12), "Test", "~N~ame", "v", 40);
        Assert.True(DialogContains(dlg, "CANCEL_TEST"));
    }

    [Fact]
    public void BuildInputBox_CustomProvider_Restored()
    {
        using (new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["Btn_OK"] = "GONE",
                ["Btn_Cancel"] = "ALSO_GONE",
            })))
        { /* scope */ }
        var dlg = MsgBox.BuildInputBox(
            new TRect(5, 5, 45, 12), "Test", "~N~ame", "v", 40);
        Assert.True(DialogContains(dlg, "~O~K"));
        Assert.True(DialogContains(dlg, "Cancel"));
    }

    // =========================================================================
    // TFileDialog labels + buttons
    // =========================================================================

    [Fact]
    public void TFileDialog_DefaultOpenButton_IsEnglish()
    {
        var fdlg = new TFileDialog("*.*", "Open", "~N~ame",
            FileDialogOptions.fdOpenButton, 0);
        Assert.True(DialogContains(fdlg, "~O~pen"));
    }

    [Fact]
    public void TFileDialog_DefaultFilesLabel_IsEnglish()
    {
        var fdlg = new TFileDialog("*.*", "Open", "~N~ame",
            FileDialogOptions.fdOpenButton, 0);
        Assert.True(DialogContains(fdlg, "~F~iles"));
    }

    [Fact]
    public void TFileDialog_DefaultCancelButton_IsEnglish()
    {
        var fdlg = new TFileDialog("*.*", "Open", "~N~ame",
            FileDialogOptions.fdOpenButton, 0);
        Assert.True(DialogContains(fdlg, "Cancel"));
    }

    [Fact]
    public void TFileDialog_CustomProvider_FilesLabel_Swapped()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["File_Label_Files"] = "FILELABEL",
                ["File_Btn_Open"]    = "OPENBTN",
                ["Btn_Cancel"]       = "CANCELBTN",
            }));
        var fdlg = new TFileDialog("*.*", "Open", "~N~ame",
            FileDialogOptions.fdOpenButton, 0);
        Assert.True(DialogContains(fdlg, "FILELABEL"));
    }

    [Fact]
    public void TFileDialog_CustomProvider_OpenButton_Swapped()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["File_Label_Files"] = "FILELABEL",
                ["File_Btn_Open"]    = "OPENBTN",
                ["Btn_Cancel"]       = "CANCELBTN",
            }));
        var fdlg = new TFileDialog("*.*", "Open", "~N~ame",
            FileDialogOptions.fdOpenButton, 0);
        Assert.True(DialogContains(fdlg, "OPENBTN"));
    }

    [Fact]
    public void TFileDialog_CustomProvider_CancelButton_Swapped()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["File_Label_Files"] = "FILELABEL",
                ["File_Btn_Open"]    = "OPENBTN",
                ["Btn_Cancel"]       = "CANCELBTN",
            }));
        var fdlg = new TFileDialog("*.*", "Open", "~N~ame",
            FileDialogOptions.fdOpenButton, 0);
        Assert.True(DialogContains(fdlg, "CANCELBTN"));
    }

    [Fact]
    public void TFileDialog_CustomProvider_Restored_OpenButton()
    {
        using (new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["File_Btn_Open"] = "GONE",
            })))
        { /* scope */ }
        var fdlg = new TFileDialog("*.*", "Open", "~N~ame",
            FileDialogOptions.fdOpenButton, 0);
        Assert.True(DialogContains(fdlg, "~O~pen"));
    }

    // =========================================================================
    // TChDirDialog title + buttons
    // =========================================================================

    [Fact]
    public void TChDirDialog_DefaultTitle_IsEnglish()
    {
        var cd = new TChDirDialog(ChDirDialogOptions.cdNoLoadDir, 0);
        Assert.Equal("Change Directory", cd.title);
    }

    [Fact]
    public void TChDirDialog_DefaultChdirButton_IsEnglish()
    {
        var cd = new TChDirDialog(ChDirDialogOptions.cdNoLoadDir, 0);
        Assert.True(DialogContains(cd, "~C~hdir"));
    }

    [Fact]
    public void TChDirDialog_DefaultRevertButton_IsEnglish()
    {
        var cd = new TChDirDialog(ChDirDialogOptions.cdNoLoadDir, 0);
        Assert.True(DialogContains(cd, "~R~evert"));
    }

    [Fact]
    public void TChDirDialog_CustomProvider_Title_Swapped()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["ChDir_Title"]      = "CHDIRTITLE",
                ["ChDir_Btn_Chdir"]  = "CHDIRBT",
                ["ChDir_Btn_Revert"] = "REVERTBT",
                ["Btn_Help"]         = "HELPBT",
            }));
        var cd = new TChDirDialog(
            ChDirDialogOptions.cdNoLoadDir | ChDirDialogOptions.cdHelpButton, 0);
        Assert.Equal("CHDIRTITLE", cd.title);
    }

    [Fact]
    public void TChDirDialog_CustomProvider_ChdirButton_Swapped()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["ChDir_Title"]      = "CHDIRTITLE",
                ["ChDir_Btn_Chdir"]  = "CHDIRBT",
                ["ChDir_Btn_Revert"] = "REVERTBT",
                ["Btn_Help"]         = "HELPBT",
            }));
        var cd = new TChDirDialog(
            ChDirDialogOptions.cdNoLoadDir | ChDirDialogOptions.cdHelpButton, 0);
        Assert.True(DialogContains(cd, "CHDIRBT"));
    }

    [Fact]
    public void TChDirDialog_CustomProvider_RevertButton_Swapped()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["ChDir_Title"]      = "CHDIRTITLE",
                ["ChDir_Btn_Chdir"]  = "CHDIRBT",
                ["ChDir_Btn_Revert"] = "REVERTBT",
                ["Btn_Help"]         = "HELPBT",
            }));
        var cd = new TChDirDialog(
            ChDirDialogOptions.cdNoLoadDir | ChDirDialogOptions.cdHelpButton, 0);
        Assert.True(DialogContains(cd, "REVERTBT"));
    }

    [Fact]
    public void TChDirDialog_CustomProvider_HelpButton_Swapped()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["ChDir_Title"]      = "CHDIRTITLE",
                ["ChDir_Btn_Chdir"]  = "CHDIRBT",
                ["ChDir_Btn_Revert"] = "REVERTBT",
                ["Btn_Help"]         = "HELPBT",
            }));
        var cd = new TChDirDialog(
            ChDirDialogOptions.cdNoLoadDir | ChDirDialogOptions.cdHelpButton, 0);
        Assert.True(DialogContains(cd, "HELPBT"));
    }

    [Fact]
    public void TChDirDialog_CustomProvider_Restored_Title()
    {
        using (new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string> { ["ChDir_Title"] = "GONE" })))
        { /* scope */ }
        var cd = new TChDirDialog(ChDirDialogOptions.cdNoLoadDir, 0);
        Assert.Equal("Change Directory", cd.title);
    }

    // =========================================================================
    // TColorDialog title + labels + buttons
    // =========================================================================

    // Helper: build a minimal TColorGroup/TColorItem for TColorDialog ctor.
    private static (TPalette pal, TColorGroup grp) MakeColorSetup()
    {
        byte[] palData = new byte[33]; palData[0] = 32;
        for (int i = 1; i <= 32; i++) palData[i] = (byte)i;
        var grp = new TColorGroup("System",
            new TColorItem("Normal", 1, new TColorItem("Highlight", 2)));
        return (new TPalette(palData), grp);
    }

    [Fact]
    public void TColorDialog_DefaultTitle_IsColors()
    {
        var (pal, grp) = MakeColorSetup();
        var cdlg = new TColorDialog(pal, grp);
        Assert.Equal("Colors", cdlg.title);
    }

    [Fact]
    public void TColorDialog_DefaultGroupLabel_IsEnglish()
    {
        var (pal, grp) = MakeColorSetup();
        var cdlg = new TColorDialog(pal, grp);
        Assert.True(DialogContains(cdlg, "~G~roup"));
    }

    [Fact]
    public void TColorDialog_DefaultItemLabel_IsEnglish()
    {
        var (pal, grp) = MakeColorSetup();
        var cdlg = new TColorDialog(pal, grp);
        Assert.True(DialogContains(cdlg, "~I~tem"));
    }

    [Fact]
    public void TColorDialog_CustomProvider_Title_Swapped()
    {
        var (pal, grp) = MakeColorSetup();
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["Color_Title"]     = "COLORTITLE",
                ["Color_Lbl_Group"] = "GROUPLBL",
                ["Color_Lbl_Item"]  = "ITEMLBL",
                ["Color_Btn_Try"]   = "TRYBTN",
            }));
        var cdlg = new TColorDialog(pal, grp);
        Assert.Equal("COLORTITLE", cdlg.title);
    }

    [Fact]
    public void TColorDialog_CustomProvider_GroupLabel_Swapped()
    {
        var (pal, grp) = MakeColorSetup();
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["Color_Title"]     = "COLORTITLE",
                ["Color_Lbl_Group"] = "GROUPLBL",
                ["Color_Lbl_Item"]  = "ITEMLBL",
                ["Color_Btn_Try"]   = "TRYBTN",
            }));
        var cdlg = new TColorDialog(pal, grp);
        Assert.True(DialogContains(cdlg, "GROUPLBL"));
    }

    [Fact]
    public void TColorDialog_CustomProvider_ItemLabel_Swapped()
    {
        var (pal, grp) = MakeColorSetup();
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["Color_Title"]     = "COLORTITLE",
                ["Color_Lbl_Group"] = "GROUPLBL",
                ["Color_Lbl_Item"]  = "ITEMLBL",
                ["Color_Btn_Try"]   = "TRYBTN",
            }));
        var cdlg = new TColorDialog(pal, grp);
        Assert.True(DialogContains(cdlg, "ITEMLBL"));
    }

    [Fact]
    public void TColorDialog_CustomProvider_TryButton_Swapped()
    {
        var (pal, grp) = MakeColorSetup();
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["Color_Title"]     = "COLORTITLE",
                ["Color_Lbl_Group"] = "GROUPLBL",
                ["Color_Lbl_Item"]  = "ITEMLBL",
                ["Color_Btn_Try"]   = "TRYBTN",
            }));
        var cdlg = new TColorDialog(pal, grp);
        Assert.True(DialogContains(cdlg, "TRYBTN"));
    }

    [Fact]
    public void TColorDialog_CustomProvider_Restored_Title()
    {
        var (pal, grp) = MakeColorSetup();
        using (new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string> { ["Color_Title"] = "GONE" })))
        { /* scope */ }
        var cdlg = new TColorDialog(pal, grp);
        Assert.Equal("Colors", cdlg.title);
    }

    // =========================================================================
    // Editor prompt strings via TSharpVisionIntl.Get()
    // =========================================================================

    [Fact]
    public void EditorPrompt_Default_SaveModify_Present()
    {
        Assert.Equal("{0} has been modified. Save?",
            TSharpVisionIntl.Get("Edit_SaveModify", "?"));
    }

    [Fact]
    public void EditorPrompt_Default_SaveModify_FormatsWithFilename()
    {
        string fmt = TSharpVisionIntl.Get("Edit_SaveModify", "{0} has been modified. Save?");
        Assert.Equal("notes.txt has been modified. Save?",
            string.Format(fmt, "notes.txt"));
    }

    [Fact]
    public void EditorPrompt_Default_ErrRead_Present()
    {
        Assert.Equal("Error reading file '{0}'.",
            TSharpVisionIntl.Get("Edit_Err_Read", "?"));
    }

    [Fact]
    public void EditorPrompt_Default_ErrRead_FormatsWithFilename()
    {
        string fmt = TSharpVisionIntl.Get("Edit_Err_Read", "Error reading file '{0}'.");
        Assert.Equal("Error reading file 'foo.txt'.",
            string.Format(fmt, "foo.txt"));
    }

    [Fact]
    public void EditorPrompt_Default_ErrWrite_FormatsWithFilename()
    {
        string fmt = TSharpVisionIntl.Get("Edit_Err_Write", "Error writing file '{0}'.");
        Assert.Equal("Error writing file 'bar.txt'.",
            string.Format(fmt, "bar.txt"));
    }

    [Fact]
    public void EditorPrompt_Default_SaveUntitled_Present()
    {
        Assert.Equal("Save untitled file?",
            TSharpVisionIntl.Get("Edit_SaveUntitled", "?"));
    }

    [Fact]
    public void EditorPrompt_Default_ErrOOM_Present()
    {
        Assert.Equal("Not enough memory for editor buffer.",
            TSharpVisionIntl.Get("Edit_Err_OOM", "?"));
    }

    [Fact]
    public void EditorPrompt_Default_FileTitleSaveAs_Present()
    {
        Assert.Equal("Save File As",
            TSharpVisionIntl.Get("File_Title_SaveAs", "?"));
    }

    [Fact]
    public void EditorPrompt_CustomProvider_SaveModify_Swapped()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["Edit_SaveModify"] = "Soubor '{0}' byl změněn. Uložit?",
                ["Edit_Err_Read"]   = "Chyba čtení souboru '{0}'.",
            }));
        string fmt = TSharpVisionIntl.Get("Edit_SaveModify", "{0} has been modified. Save?");
        Assert.Equal("Soubor 'test.txt' byl změněn. Uložit?",
            string.Format(fmt, "test.txt"));
    }

    [Fact]
    public void EditorPrompt_CustomProvider_ErrRead_Swapped()
    {
        using var scope = new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["Edit_Err_Read"] = "Chyba čtení souboru '{0}'.",
            }));
        string fmt = TSharpVisionIntl.Get("Edit_Err_Read", "Error reading file '{0}'.");
        Assert.Equal("Chyba čtení souboru 'x.txt'.",
            string.Format(fmt, "x.txt"));
    }

    [Fact]
    public void EditorPrompt_CustomProvider_Restored_SaveModify()
    {
        using (new IntlProviderScope(new DictStringProvider(
            new Dictionary<string, string>
            {
                ["Edit_SaveModify"] = "GONE",
            })))
        { /* scope */ }
        Assert.Equal("{0} has been modified. Save?",
            TSharpVisionIntl.Get("Edit_SaveModify", "?"));
    }

    [Fact]
    public void EditorPrompt_Default_ErrCreate_Present()
    {
        Assert.Equal("Error creating file '{0}'.",
            TSharpVisionIntl.Get("Edit_Err_Create", "?"));
    }

    [Fact]
    public void EditorPrompt_Default_SearchFailed_Present()
    {
        Assert.Equal("Search string not found.",
            TSharpVisionIntl.Get("Edit_SearchFailed", "?"));
    }

    [Fact]
    public void EditorPrompt_Default_ReplacePrompt_Present()
    {
        Assert.Equal("Replace this occurrence?",
            TSharpVisionIntl.Get("Edit_ReplacePrompt", "?"));
    }

    [Fact]
    public void EditorPrompt_Default_Untitled_Present()
    {
        Assert.Equal("Untitled", TSharpVisionIntl.Get("Edit_Untitled", "?"));
    }

    [Fact]
    public void EditorPrompt_Default_Clipboard_Present()
    {
        Assert.Equal("Clipboard", TSharpVisionIntl.Get("Edit_Clipboard", "?"));
    }

    [Fact]
    public void EditorPrompt_MissingKey_ReturnsFallback()
    {
        Assert.Equal("MY_FALLBACK",
            TSharpVisionIntl.Get("Edit_Key_That_Does_Not_Exist_XYZ", "MY_FALLBACK"));
    }

    [Fact]
    public void EditorPrompt_Default_HelpWindowTitle_Present()
    {
        Assert.Equal("Help", TSharpVisionIntl.Get("Help_WindowTitle", "?"));
    }

    [Fact]
    public void EditorPrompt_Default_HelpNoContext_Present()
    {
        Assert.Contains("No help available", TSharpVisionIntl.Get("Help_NoContext", "?"));
    }
}

// ── File-local test helpers ────────────────────────────────────────────────────

file sealed class DictStringProvider(Dictionary<string, string> dict)
    : ITSharpVisionStringProvider
{
    public string Get(string key, string fallback)
        => dict.TryGetValue(key, out var v) ? v : fallback;
}
