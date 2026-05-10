using System;
using System.Collections.Generic;
using System.IO;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Files;

[Collection("NonParallel")]
public sealed class FileDialogPathPolishTests
{
    private static TFileList MakeFileList()
        => new TFileList(new TRect(0, 0, 40, 15), null);

    private static List<string> ListItems(TFileList fl)
    {
        var result = new List<string>();
        for (int i = 0; i < fl.range; i++)
        {
            string text = fl.GetText(i, FileDialogConstants.MaxPathLen) ?? string.Empty;
            if (text.Length > 0
                && (text[text.Length - 1] == Path.DirectorySeparatorChar
                    || text[text.Length - 1] == '/'))
            {
                text = text.Substring(0, text.Length - 1);
            }
            result.Add(text);
        }
        return result;
    }

    private static void CreateFile(string directory, string name)
        => File.WriteAllText(Path.Combine(directory, name), "x");

    private static void AssertListed(string directory, string mask, string fileName)
    {
        var fl = MakeFileList();
        fl.ReadDirectory(Path.Combine(directory, mask));
        Assert.Contains(fileName, ListItems(fl));
    }

    [Fact]
    public void MaxLen_FileDialog_AtLeast4096()
    {
        var dlg = new TFileDialog(
            "*.*",
            "Open",
            "Name",
            (ushort)(FileDialogOptions.fdOpenButton | FileDialogOptions.fdNoLoadDir),
            0);
        try
        {
            Assert.True(dlg.fileName.MaxLen >= FileDialogConstants.MaxPathLen - 1);
        }
        finally
        {
            dlg.ShutDown();
        }
    }

    [Fact]
    public void MaxLen_ChDirDialog_AtLeast4096()
    {
        var dlg = new TChDirDialog(ChDirDialogOptions.cdNoLoadDir, 0);
        try
        {
            Assert.True(dlg.dirInput.MaxLen >= FileDialogConstants.MaxPathLen - 1);
        }
        finally
        {
            dlg.ShutDown();
        }
    }

    [Fact]
    public void Unicode_Diacritics_FileListed()
    {
        using var tmp = new TempDirectory();
        CreateFile(tmp.Path, "café.txt");

        AssertListed(tmp.Path, "*.*", "café.txt");
    }

    [Fact]
    public void Unicode_Czech_FileListed()
    {
        using var tmp = new TempDirectory();
        CreateFile(tmp.Path, "dvořák.cs");

        AssertListed(tmp.Path, "*.*", "dvořák.cs");
    }

    [Fact]
    public void Unicode_CJK_FileListed()
    {
        using var tmp = new TempDirectory();
        CreateFile(tmp.Path, "日本語.txt");

        AssertListed(tmp.Path, "*.*", "日本語.txt");
    }

    [Fact]
    public void Unicode_Emoji_FileListed()
    {
        using var tmp = new TempDirectory();
        CreateFile(tmp.Path, "🚀.bin");

        AssertListed(tmp.Path, "*.*", "🚀.bin");
    }

    [Fact]
    public void Wildcard_StarTxt_MatchesUnicodeName()
    {
        using var tmp = new TempDirectory();
        CreateFile(tmp.Path, "日本語.txt");
        CreateFile(tmp.Path, "日本語.bin");

        var fl = MakeFileList();
        fl.ReadDirectory(Path.Combine(tmp.Path, "*.txt"));
        var items = ListItems(fl);

        Assert.Contains("日本語.txt", items);
        Assert.DoesNotContain("日本語.bin", items);
    }

    [Fact]
    public void Unicode_FileName_GetDataRoundTrip()
    {
        using var tmp = new TempDirectory();
        string fileName = "日本語.txt";
        string path = Path.Combine(tmp.Path, fileName);
        CreateFile(tmp.Path, fileName);
        var dlg = new TFileDialog(
            "*.*",
            "Open",
            "Name",
            (ushort)(FileDialogOptions.fdOpenButton | FileDialogOptions.fdNoLoadDir),
            0);
        try
        {
            dlg.SetData(path);
            dlg.GetData(out string selected);

            Assert.EndsWith(fileName, selected, StringComparison.Ordinal);
        }
        finally
        {
            dlg.ShutDown();
        }
    }

    [Fact]
    public void SaveAs_Unicode_OverwriteYes_Updates()
    {
        using var tmp = new TempDirectory();
        string path = Path.Combine(tmp.Path, "日本語.txt");
        File.WriteAllText(path, "old");

        var savedDialog = TEditor.editorDialog;
        var savedOverwrite = TEditorDialogHelper.OverwriteConfirm;
        try
        {
            TEditorDialogHelper.OverwriteConfirm = (_, _) => Views.cmYes;
            TEditor.editorDialog = (int dialog, object info) =>
            {
                if (dialog == Views.edSaveAs && info is TFileEditor editor)
                {
                    string initial = editor.fileName;
                    editor.fileName = path;
                    if (File.Exists(editor.fileName))
                    {
                        ushort confirm = TEditorDialogHelper.OverwriteConfirm(null, editor.fileName);
                        if (confirm != Views.cmYes)
                        {
                            editor.fileName = initial;
                            return Views.cmCancel;
                        }
                    }
                    return Views.cmFileOpen;
                }
                return Views.cmCancel;
            };

            var ed = new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, null);
            byte[] content = System.Text.Encoding.ASCII.GetBytes("new");
            ed.InsertText(content, (uint)content.Length, false);

            Assert.True(ed.SaveAs());
            Assert.Equal("new", File.ReadAllText(path));
        }
        finally
        {
            TEditor.editorDialog = savedDialog;
            TEditorDialogHelper.OverwriteConfirm = savedOverwrite;
        }
    }

    [Fact]
    public void LongPath_DeepTree_Enumerates()
    {
        using var tmp = new TempDirectory();
        string current = tmp.Path;
        try
        {
            int index = 0;
            while (current.Length <= 280)
            {
                current = Path.Combine(current, "segment_" + index.ToString("D2") + "_long_path");
                Directory.CreateDirectory(current);
                index++;
            }

            CreateFile(current, "deep.txt");
        }
        catch (Exception ex) when (ex is PathTooLongException || ex is DirectoryNotFoundException)
        {
            // Platform/runtime long-path support is disabled here; this phase
            // only verifies the dialog code no longer imposes the old 260 cap.
            return;
        }

        AssertListed(current, "*.*", "deep.txt");
    }
}
