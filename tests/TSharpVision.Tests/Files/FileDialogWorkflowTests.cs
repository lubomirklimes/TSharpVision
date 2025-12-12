using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Files;

[Collection("NonParallel")]
public sealed class FileDialogWorkflowTests
{
    // ── local helpers ─────────────────────────────────────────────────────────

    private static TFileList MakeFileList()
        => new TFileList(new TRect(0, 0, 30, 15), null);

    private static List<string> ListItems(TFileList fl)
    {
        var result = new List<string>();
        for (int i = 0; i < fl.range; i++)
        {
            string t = fl.GetText(i, 260) ?? string.Empty;
            if (t.Length > 0
                && (t[t.Length - 1] == Path.DirectorySeparatorChar
                    || t[t.Length - 1] == '/'))
                t = t.Substring(0, t.Length - 1);
            result.Add(t);
        }
        return result;
    }

    // ── 22f-1: *.* shows all files ────────────────────────────────────────────

    [Fact]
    public void ReadDirectory_StarStar_ShowsAllFiles()
    {
        using var tmp = new TempDirectory();
        File.WriteAllText(Path.Combine(tmp.Path, "a.txt"), "a");
        File.WriteAllText(Path.Combine(tmp.Path, "b.cs"),  "b");
        File.WriteAllText(Path.Combine(tmp.Path, "c.bin"), "c");
        var fl = MakeFileList();
        fl.ReadDirectory(tmp.Path + Path.DirectorySeparatorChar + "*.*");
        var items = ListItems(fl);
        Assert.Contains("a.txt", items);
        Assert.Contains("b.cs",  items);
        Assert.Contains("c.bin", items);
    }

    // ── 22f-2: *.txt filters ─────────────────────────────────────────────────

    [Fact]
    public void ReadDirectory_StarTxt_ShowsOnlyTxt()
    {
        using var tmp = new TempDirectory();
        File.WriteAllText(Path.Combine(tmp.Path, "a.txt"), "a");
        File.WriteAllText(Path.Combine(tmp.Path, "b.cs"),  "b");
        File.WriteAllText(Path.Combine(tmp.Path, "c.bin"), "c");
        var fl = MakeFileList();
        fl.ReadDirectory(tmp.Path + Path.DirectorySeparatorChar + "*.txt");
        var items = ListItems(fl);
        Assert.Contains("a.txt", items);
        Assert.DoesNotContain("b.cs",  items);
        Assert.DoesNotContain("c.bin", items);
    }

    // ── 22f-3: file.* matches by name ────────────────────────────────────────

    [Fact]
    public void ReadDirectory_FileStar_MatchesByName()
    {
        using var tmp = new TempDirectory();
        File.WriteAllText(Path.Combine(tmp.Path, "file.txt"),  "a");
        File.WriteAllText(Path.Combine(tmp.Path, "file.bin"),  "b");
        File.WriteAllText(Path.Combine(tmp.Path, "other.txt"), "c");
        var fl = MakeFileList();
        fl.ReadDirectory(tmp.Path + Path.DirectorySeparatorChar + "file.*");
        var items = ListItems(fl);
        Assert.Contains("file.txt",  items);
        Assert.Contains("file.bin",  items);
        Assert.DoesNotContain("other.txt", items);
    }

    // ── 22f-4: multi-mask *.txt;*.cs ─────────────────────────────────────────

    [Fact]
    public void ReadDirectory_MultiMask_FiltersByBothExtensions()
    {
        using var tmp = new TempDirectory();
        File.WriteAllText(Path.Combine(tmp.Path, "a.txt"), "a");
        File.WriteAllText(Path.Combine(tmp.Path, "b.cs"),  "b");
        File.WriteAllText(Path.Combine(tmp.Path, "c.bin"), "c");
        var fl = MakeFileList();
        fl.ReadDirectory(tmp.Path + Path.DirectorySeparatorChar + "*.txt;*.cs");
        var items = ListItems(fl);
        Assert.Contains("a.txt", items);
        Assert.Contains("b.cs",  items);
        Assert.DoesNotContain("c.bin", items);
    }

    // ── 22f-5: invalid path does not throw ───────────────────────────────────

    [Fact]
    public void ReadDirectory_InvalidPath_NoException()
    {
        using var tmp = new TempDirectory();
        var fl = MakeFileList();
        var ex = Record.Exception(() =>
            fl.ReadDirectory(tmp.Path + Path.DirectorySeparatorChar + "\0bad\0"));
        Assert.Null(ex);
    }

    // ── 22f-6: directory navigation ".." entry ───────────────────────────────

    [Fact]
    public void ReadDirectory_ChildDir_HasDotDotEntry()
    {
        using var tmp = new TempDirectory();
        string child = Path.Combine(tmp.Path, "child");
        Directory.CreateDirectory(child);
        var fl = MakeFileList();
        fl.ReadDirectory(child + Path.DirectorySeparatorChar + "*.*");
        var items = ListItems(fl);
        Assert.Contains("..", items);
    }

    [Fact]
    public void ReadDirectory_RootDir_NoException()
    {
        string root = Path.GetPathRoot(Path.GetTempPath()) ?? Path.GetTempPath();
        var fl = MakeFileList();
        var ex = Record.Exception(() =>
            fl.ReadDirectory(root + "*.*"));
        Assert.Null(ex);
    }

    // ── 22f-7: non-existing directory ────────────────────────────────────────

    [Fact]
    public void ReadDirectory_MissingDir_NoException()
    {
        using var tmp = new TempDirectory();
        string missing = Path.Combine(tmp.Path, "does_not_exist_xyz");
        var fl = MakeFileList();
        var ex = Record.Exception(() =>
            fl.ReadDirectory(missing + Path.DirectorySeparatorChar + "*.*"));
        Assert.Null(ex);
    }

    // ── 22f-8: null-byte path ─────────────────────────────────────────────────

    [Fact]
    public void ReadDirectory_NullBytePath_NoException()
    {
        var fl = MakeFileList();
        var ex = Record.Exception(() => fl.ReadDirectory("\0\x01\x02" + "*.*"));
        Assert.Null(ex);
    }

    // ── 22f-9: TFileDialog.IsWild ─────────────────────────────────────────────

    [Fact]
    public void IsWild_StarStar_True()
        => Assert.True(TFileDialog.IsWild("*.*"));

    [Fact]
    public void IsWild_StarTxt_True()
        => Assert.True(TFileDialog.IsWild("*.txt"));

    [Fact]
    public void IsWild_Question_True()
        => Assert.True(TFileDialog.IsWild("foo?.txt"));

    [Fact]
    public void IsWild_PlainName_False()
        => Assert.False(TFileDialog.IsWild("foo.txt"));

    [Fact]
    public void IsWild_Null_False()
        => Assert.False(TFileDialog.IsWild(null));

    [Fact]
    public void IsWild_Empty_False()
        => Assert.False(TFileDialog.IsWild(string.Empty));

    // ── 22f-10: OverwriteConfirm injectable ──────────────────────────────────

    [Fact]
    public void OverwriteConfirm_Injectable_Yes()
    {
        var saved = TEditorDialogHelper.OverwriteConfirm;
        try
        {
            TEditorDialogHelper.OverwriteConfirm = (_, _) => Views.cmYes;
            ushort result = TEditorDialogHelper.OverwriteConfirm(null, "test.txt");
            Assert.Equal(Views.cmYes, result);
        }
        finally { TEditorDialogHelper.OverwriteConfirm = saved; }
    }

    [Fact]
    public void OverwriteConfirm_Injectable_No()
    {
        var saved = TEditorDialogHelper.OverwriteConfirm;
        try
        {
            TEditorDialogHelper.OverwriteConfirm = (_, _) => Views.cmNo;
            ushort result = TEditorDialogHelper.OverwriteConfirm(null, "test.txt");
            Assert.Equal(Views.cmNo, result);
        }
        finally { TEditorDialogHelper.OverwriteConfirm = saved; }
    }

    [Fact]
    public void OverwriteConfirm_Default_NullDeskTop_CmCancel()
    {
        var saved = TEditorDialogHelper.OverwriteConfirm;
        try
        {
            TEditorDialogHelper.OverwriteConfirm = TEditorDialogHelper.OverwriteConfirmDefault;
            ushort result = TEditorDialogHelper.OverwriteConfirm(null, "test.txt");
            Assert.Equal(Views.cmCancel, result);
        }
        finally { TEditorDialogHelper.OverwriteConfirm = saved; }
    }

    // ── 22f-11: edSaveAs overwrite Yes → file saved ───────────────────────────

    [Fact]
    public void SaveAs_OverwriteYes_FileContentUpdated()
    {
        using var tmp = new TempDirectory();
        string path = Path.Combine(tmp.Path, "ow.txt");
        File.WriteAllText(path, "old");

        var savedDialog = TEditor.editorDialog;
        var savedOverwrite = TEditorDialogHelper.OverwriteConfirm;
        try
        {
            TEditorDialogHelper.OverwriteConfirm = (_, _) => Views.cmYes;
            TEditor.editorDialog = (int dialog, object info) =>
            {
                if (dialog == Views.edSaveAs && info is TFileEditor fe)
                {
                    string initial = fe.fileName;
                    fe.fileName = path;
                    if (File.Exists(fe.fileName))
                    {
                        ushort c = TEditorDialogHelper.OverwriteConfirm(null, fe.fileName);
                        if (c != Views.cmYes) { fe.fileName = initial; return Views.cmCancel; }
                    }
                    return Views.cmFileOpen;
                }
                return Views.cmCancel;
            };
            var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, null);
            byte[] content = System.Text.Encoding.ASCII.GetBytes("new");
            ed.InsertText(content, (uint)content.Length, false);
            bool saved = ed.SaveAs();
            Assert.True(saved);
            Assert.Equal("new", File.ReadAllText(path));
        }
        finally
        {
            TEditor.editorDialog = savedDialog;
            TEditorDialogHelper.OverwriteConfirm = savedOverwrite;
        }
    }

    // ── 22f-12: edSaveAs overwrite No → file unchanged ───────────────────────

    [Fact]
    public void SaveAs_OverwriteNo_FileUnchanged()
    {
        using var tmp = new TempDirectory();
        string path = Path.Combine(tmp.Path, "ow2.txt");
        File.WriteAllText(path, "old");

        var savedDialog = TEditor.editorDialog;
        var savedOverwrite = TEditorDialogHelper.OverwriteConfirm;
        try
        {
            TEditorDialogHelper.OverwriteConfirm = (_, _) => Views.cmNo;
            TEditor.editorDialog = (int dialog, object info) =>
            {
                if (dialog == Views.edSaveAs && info is TFileEditor fe)
                {
                    string initial = fe.fileName;
                    fe.fileName = path;
                    if (File.Exists(fe.fileName))
                    {
                        ushort c = TEditorDialogHelper.OverwriteConfirm(null, fe.fileName);
                        if (c != Views.cmYes) { fe.fileName = initial; return Views.cmCancel; }
                    }
                    return Views.cmFileOpen;
                }
                return Views.cmCancel;
            };
            var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, null);
            byte[] content = System.Text.Encoding.ASCII.GetBytes("new");
            ed.InsertText(content, (uint)content.Length, false);
            bool saved = ed.SaveAs();
            Assert.False(saved);
            Assert.Equal("old", File.ReadAllText(path));
        }
        finally
        {
            TEditor.editorDialog = savedDialog;
            TEditorDialogHelper.OverwriteConfirm = savedOverwrite;
        }
    }

    // ── 22f-13: TFileEditor for missing file — valid and empty ───────────────

    [Fact]
    public void TFileEditor_MissingFile_IsValid()
    {
        using var tmp = new TempDirectory();
        string missing = Path.Combine(tmp.Path, "missing.txt");
        var savedDialog = TEditor.editorDialog;
        try
        {
            TEditor.editorDialog = (_, _) => Views.cmCancel;
            var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, missing);
            Assert.True(ed.isValid);
        }
        finally { TEditor.editorDialog = savedDialog; }
    }

    [Fact]
    public void TFileEditor_MissingFile_EmptyBuffer()
    {
        using var tmp = new TempDirectory();
        string missing = Path.Combine(tmp.Path, "missing2.txt");
        var savedDialog = TEditor.editorDialog;
        try
        {
            TEditor.editorDialog = (_, _) => Views.cmCancel;
            var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, missing);
            Assert.Equal(0u, ed.bufLen);
        }
        finally { TEditor.editorDialog = savedDialog; }
    }

    // ── 22f-14: TFileEditor.SaveFile to new path ─────────────────────────────

    [Fact]
    public void TFileEditor_SaveFile_CreatesFile()
    {
        using var tmp = new TempDirectory();
        string newPath = Path.Combine(tmp.Path, "new14.txt");
        var savedDialog = TEditor.editorDialog;
        try
        {
            TEditor.editorDialog = (_, _) => Views.cmCancel;
            var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, null);
            ed.fileName = newPath;
            bool saved = ed.SaveFile();
            Assert.True(saved);
            Assert.True(File.Exists(newPath));
        }
        finally { TEditor.editorDialog = savedDialog; }
    }

    // ── 22f-15: TChDirDialog.Valid with invalid dir ───────────────────────────

    [Fact]
    public void ChDirDialog_Valid_InvalidDir_ReturnsFalse()
    {
        var dlg = new TChDirDialog(ChDirDialogOptions.cdNoLoadDir, 0);
        if (dlg.dirInput != null)
            dlg.dirInput.SetData("\0invalid\0");
        bool valid = true;
        var ex = Record.Exception(() => valid = dlg.Valid(Views.cmOK));
        Assert.Null(ex);
        Assert.False(valid);
        dlg.ShutDown();
    }
}
