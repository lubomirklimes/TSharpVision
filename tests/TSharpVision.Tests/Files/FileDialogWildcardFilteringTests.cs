using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Files;

// TV-FIX-1: TFileDialog.Valid passed "<dir>\<mask>" together with the mask to
// TFileList.ReadDirectory, producing "<dir>\*.twproj*.twproj" and hiding every
// matching file. These tests drive the real dialog (SetData -> Valid -> list).
[Collection("NonParallel")]
public sealed class FileDialogWildcardFilteringTests : IDisposable
{
    private readonly DriverScope _driver = new();
    private readonly TempDirectory _tmp = new();

    public FileDialogWildcardFilteringTests()
    {
        _tmp.CreateFile("alpha.twproj", "{}");
        _tmp.CreateFile("beta.twproj", "{}");
        _tmp.CreateFile("gamma.txt", "g");
        _tmp.CreateFile("application.exe", "x");
        _tmp.CreateFile("run.json", "{}");
        _tmp.CreateFile("my.project.v2.twproj", "{}");
        string nested = _tmp.CreateSubDir("nested");
        File.WriteAllText(Path.Combine(nested, "delta.twproj"), "{}");
        File.WriteAllText(Path.Combine(nested, "epsilon.txt"), "e");
        _tmp.CreateSubDir("empty");
    }

    public void Dispose()
    {
        _tmp.Dispose();
        _driver.Dispose();
    }

    private static TFileDialog OpenDialog(string mask, ushort extra = FileDialogOptions.fdOpenButton)
        => new(mask, "Open", "~N~ame", (ushort)(extra | FileDialogOptions.fdNoLoadDir), 0);

    private static string Dir(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static List<string> Files(TFileDialog dlg)
        => Entries(dlg).Where(e => !e.IsDir).Select(e => e.Name).ToList();

    private static List<string> Dirs(TFileDialog dlg)
        => Entries(dlg).Where(e => e.IsDir).Select(e => e.Name).ToList();

    private static List<(string Name, bool IsDir)> Entries(TFileDialog dlg)
    {
        var fl = Assert.IsType<TFileList>(dlg.fileList);
        var result = new List<(string, bool)>();
        for (int i = 0; i < fl.range; i++)
        {
            // Directory rows are rendered with a trailing separator.
            string text = fl.GetText(i, FileDialogConstants.MaxPathLen) ?? string.Empty;
            bool isDir = text.EndsWith(Path.DirectorySeparatorChar);
            result.Add((isDir ? text[..^1] : text, isDir));
        }
        return result;
    }

    // Focuses a list entry as the user would (list selected, so the name line follows).
    private static void FocusEntry(TFileDialog dlg, string name)
    {
        var fl = Assert.IsType<TFileList>(dlg.fileList);
        fl.Select();
        int index = Entries(dlg).FindIndex(e => e.Name == name);
        Assert.True(index >= 0, $"entry '{name}' not listed");
        fl.FocusItem(index);
    }

    private static void Run(TFileDialog dlg, Action<TFileDialog> body)
    {
        try { body(dlg); }
        finally { dlg.ShutDown(); }
    }

    // ── A. Wildcard filtering ─────────────────────────────────────────────

    [Fact]
    public void SetData_TwprojMask_ListsMatchingFilesOnly()
        => Run(OpenDialog("*.twproj"), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "*.twproj"));

            var files = Files(dlg);
            Assert.Contains("alpha.twproj", files);
            Assert.Contains("beta.twproj", files);
            Assert.Contains("my.project.v2.twproj", files);
            Assert.DoesNotContain("gamma.txt", files);
            Assert.DoesNotContain("run.json", files);
            Assert.Equal(string.Empty, dlg.LastError);
        });

    [Fact]
    public void SetData_ExeMask_ListsExecutable()
        => Run(OpenDialog("*.exe"), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "*.exe"));
            Assert.Equal(new[] { "application.exe" }, Files(dlg));
        });

    [Fact]
    public void Valid_LiteralRunJsonFilter_ListsRunJson()
        => Run(OpenDialog("run.json"), dlg =>
        {
            dlg.fileList!.ReadDirectory(Dir(_tmp.Path), dlg.WildCard);
            Assert.Equal(new[] { "run.json" }, Files(dlg));
        });

    [Fact]
    public void SetData_UnmatchedMask_ListsNoFilesButKeepsDirectories()
        => Run(OpenDialog("*.none"), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "*.none"));
            Assert.Empty(Files(dlg));
            Assert.Contains("nested", Dirs(dlg));
            Assert.Contains("..", Dirs(dlg));
        });

    [Fact]
    public void SetData_MultipleMasks_AppliesEachOnce()
        => Run(OpenDialog("*.twproj;*.exe"), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "*.twproj;*.exe"));
            var files = Files(dlg);
            Assert.Contains("alpha.twproj", files);
            Assert.Contains("application.exe", files);
            Assert.DoesNotContain("gamma.txt", files);
        });

    [Fact]
    public void SetData_SplitsDirectoryAndMask()
        => Run(OpenDialog("*.twproj"), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "*.twproj"));
            Assert.Equal("*.twproj", dlg.WildCard);
            Assert.Equal(Dir(_tmp.Path), dlg.Directory);
        });

    // ── B. Navigation ─────────────────────────────────────────────────────

    [Fact]
    public void InitialLoad_FromWorkingDirectory_ListsMatchingFiles()
    {
        string saved = Environment.CurrentDirectory;
        Environment.CurrentDirectory = _tmp.Path;
        try
        {
            Run(new TFileDialog("*.twproj", "Open", "~N~ame", FileDialogOptions.fdOpenButton, 0), dlg =>
            {
                Assert.Contains("alpha.twproj", Files(dlg));
                Assert.DoesNotContain("gamma.txt", Files(dlg));
            });
        }
        finally { Environment.CurrentDirectory = saved; }
    }

    [Fact]
    public void RelativeWildcard_ResolvesAgainstDialogDirectory()
    {
        string saved = Environment.CurrentDirectory;
        Environment.CurrentDirectory = _tmp.Path;
        try
        {
            Run(OpenDialog("*.twproj"), dlg =>
            {
                dlg.SetData(Path.Combine("nested", "*.twproj"));
                Assert.Equal(new[] { "delta.twproj" }, Files(dlg));
                Assert.Equal(Dir(Path.Combine(Environment.CurrentDirectory, "nested")), dlg.Directory);
            });
        }
        finally { Environment.CurrentDirectory = saved; }
    }

    [Fact]
    public void NavigateIntoDirectory_RefreshesWithSameMask()
        => Run(OpenDialog("*.twproj"), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "*.twproj"));
            FocusEntry(dlg, "nested");
            Assert.Equal("nested" + Path.DirectorySeparatorChar + "*.twproj", dlg.fileName!.Data);

            Assert.False(dlg.Valid(Views.cmOK));

            Assert.Equal(new[] { "delta.twproj" }, Files(dlg));
            Assert.Equal("*.twproj", dlg.WildCard);
            Assert.Equal(Dir(Path.Combine(_tmp.Path, "nested")), dlg.Directory);
        });

    [Fact]
    public void NavigateToParent_RestoresMatchingFiles()
        => Run(OpenDialog("*.twproj"), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "nested", "*.twproj"));
            FocusEntry(dlg, "..");
            Assert.False(dlg.Valid(Views.cmOK));

            Assert.Equal(Dir(_tmp.Path), dlg.Directory);
            Assert.Contains("alpha.twproj", Files(dlg));
            Assert.Contains("nested", Dirs(dlg));
        });

    [Fact]
    public void RepeatedNavigation_NeverDuplicatesMask()
        => Run(OpenDialog("*.twproj"), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "*.twproj"));
            for (int i = 0; i < 3; i++)
            {
                FocusEntry(dlg, "nested");
                Assert.False(dlg.Valid(Views.cmOK));
                Assert.Equal(new[] { "delta.twproj" }, Files(dlg));
                FocusEntry(dlg, "..");
                Assert.False(dlg.Valid(Views.cmOK));
                Assert.Contains("beta.twproj", Files(dlg));
            }
            Assert.Equal("*.twproj", dlg.WildCard);
        });

    [Fact]
    public void TypedDirectoryPath_ListsWithCurrentMask()
        => Run(OpenDialog("*.twproj"), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "*.twproj"));
            dlg.fileName!.SetData(Path.Combine(_tmp.Path, "nested"));
            Assert.False(dlg.Valid(Views.cmOK));
            Assert.Equal(new[] { "delta.twproj" }, Files(dlg));
        });

    // ── C. Filename operations ────────────────────────────────────────────

    [Fact]
    public void SelectingListedFile_ProducesFullPathWithoutMask()
        => Run(OpenDialog("*.twproj"), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "nested", "*.twproj"));
            FocusEntry(dlg, "delta.twproj");

            Assert.True(dlg.Valid(Views.cmFileOpen));
            dlg.GetData(out string selected);

            Assert.Equal(Path.Combine(_tmp.Path, "nested", "delta.twproj"), selected);
            Assert.False(TFileDialog.IsWild(selected));
        });

    [Fact]
    public void SaveAs_AcceptsNewFileNameInCurrentDirectory()
        => Run(OpenDialog("*.twproj", FileDialogOptions.fdOKButton), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "*.twproj"));
            dlg.fileName!.SetData("brand-new.twproj");

            Assert.True(dlg.Valid(Views.cmOK));
            dlg.GetFileName(out string selected);

            Assert.Equal(Path.Combine(_tmp.Path, "brand-new.twproj"), selected);
            Assert.False(File.Exists(selected));
        });

    // ── D. Regression safety ──────────────────────────────────────────────

    [Fact]
    public void EmptyDirectory_DoesNotCrashAndOffersParent()
        => Run(OpenDialog("*.twproj"), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "empty", "*.twproj"));
            Assert.Empty(Files(dlg));
            Assert.Equal(new[] { ".." }, Dirs(dlg));
            Assert.Equal(string.Empty, dlg.LastError);
        });

    [Fact]
    public void MissingDirectoryWildcard_IsRejectedWithoutChangingState()
        => Run(OpenDialog("*.twproj"), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "*.twproj"));
            dlg.fileName!.SetData(Path.Combine(_tmp.Path, "missing", "*.twproj"));

            Assert.False(dlg.Valid(Views.cmOK));
            Assert.Equal(Dir(_tmp.Path), dlg.Directory);
            Assert.Contains("alpha.twproj", Files(dlg));
        });

    [Fact]
    public void WildcardInput_IsNeverAcceptedAsFileName()
        => Run(OpenDialog("*.twproj"), dlg =>
        {
            dlg.SetData(Path.Combine(_tmp.Path, "*.twproj"));
            Assert.False(dlg.Valid(Views.cmFileOpen));
            Assert.True(dlg.Valid(Views.cmCancel));
        });

    [Fact]
    public void DriveOrFilesystemRoot_ListsRootDirectoriesWithoutParent()
        => Run(OpenDialog("*"), dlg =>
        {
            string root = Path.GetPathRoot(_tmp.Path)!;
            dlg.SetData(Path.Combine(root, "*"));

            Assert.Equal(root, dlg.Directory);
            Assert.DoesNotContain("..", Dirs(dlg));
            string? expected = Directory.EnumerateDirectories(root)
                .Select(Path.GetFileName)
                .FirstOrDefault(n => n is not null && !n.StartsWith('.'));
            if (expected is not null)
                Assert.Contains(expected, Dirs(dlg));
        });
}
