using System;
using System.IO;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Files;

[Collection("NonParallel")]
public sealed class FileDialogUncErrorTests
{
    [Fact]
    public void DirListBox_UncRoot_DoesNotEmitEmptySegment()
    {
        string remaining = TDirListBox.SplitRootForDisplay(
            "//server/share/dir",
            out string root);

        Assert.Equal("//server/share/", root);
        Assert.Equal("dir", remaining);
    }

    [Fact]
    public void DirListBox_UncRoot_TreeRendersSingleSyntheticRoot()
    {
        string normalized = @"\\server\share\dir".Replace('\\', '/');

        string remaining = TDirListBox.SplitRootForDisplay(
            normalized,
            out string root);

        Assert.Equal("//server/share/", root);
        Assert.Equal("dir", remaining);
    }

    [Fact]
    public void FileList_MissingDir_LastErrorIsNotFound()
    {
        using var tmp = new TempDirectory();
        string missing = Path.Combine(tmp.Path, "missing");
        var list = new TFileList(new TRect(0, 0, 40, 10), null);

        list.ReadDirectory(Path.Combine(missing, "*.*"));

        Assert.Equal(
            TSharpVisionIntl.Get("File_Err_NotFound", "File or directory not found."),
            list.LastError);
    }

    [Fact]
    public void FileDialog_PropagatesFileListLastError()
    {
        using var tmp = new TempDirectory();
        string missing = Path.Combine(tmp.Path, "missing");
        var dlg = new TFileDialog(
            Path.Combine(missing, "*.*"),
            "Open",
            "Name",
            (ushort)(FileDialogOptions.fdOpenButton | FileDialogOptions.fdNoLoadDir),
            0);
        try
        {
            dlg.ReadDirectory();

            Assert.Equal(
                TSharpVisionIntl.Get("File_Err_NotFound", "File or directory not found."),
                dlg.LastError);
        }
        finally
        {
            dlg.ShutDown();
        }
    }

    [Fact]
    public void FileList_Success_ClearsLastError()
    {
        using var tmp = new TempDirectory();
        string missing = Path.Combine(tmp.Path, "missing");
        var list = new TFileList(new TRect(0, 0, 40, 10), null);
        list.ReadDirectory(Path.Combine(missing, "*.*"));
        Assert.NotEmpty(list.LastError);

        list.ReadDirectory(Path.Combine(tmp.Path, "*.*"));

        Assert.Equal(string.Empty, list.LastError);
    }

    [Fact]
    public void LocalizedKeys_Exist()
    {
        var saved = TSharpVisionIntl.Current;
        int misses = 0;
        EventHandler<MissingLocalizationKeyEventArgs> handler = (_, _) => misses++;
        try
        {
            TSharpVisionIntl.Current = new DefaultEnglishStringProvider();
            TSharpVisionIntl.MissingKey += handler;

            Assert.Equal("Path is too long.",
                TSharpVisionIntl.Get("File_Err_PathTooLong", "fallback"));
            Assert.Equal("Network location is unavailable.",
                TSharpVisionIntl.Get("File_Err_NetworkUnavailable", "fallback"));
            Assert.Equal("File or directory not found.",
                TSharpVisionIntl.Get("File_Err_NotFound", "fallback"));
            Assert.Equal(0, misses);
        }
        finally
        {
            TSharpVisionIntl.MissingKey -= handler;
            TSharpVisionIntl.Current = saved;
        }
    }

    // Real UNC integration is intentionally not exercised here: unavailable
    // network providers/admin shares can block for a long time. The tree model
    // tests above cover deterministic UNC parsing; manual/CI-specific smoke
    // tests can opt into real shares separately.
}
