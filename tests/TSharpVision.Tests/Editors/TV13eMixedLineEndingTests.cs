using System.Collections.Generic;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

// TV13e: a file loaded with mixed line endings is not normalised silently. The first save asks through
// editorDialog(edMixedLineEndings) and the choice is remembered until reload or reset.
[Collection("NonParallel")]
public sealed class TV13eMixedLineEndingTests : IDisposable
{
    private const string Mixed = "a\r\nb\nc\r\nd";
    private readonly TEditor.TEditorDialog _savedDialog = TEditor.editorDialog;
    private readonly List<(int Dialog, object? Info)> _calls = new();
    private Func<EditorLineEndingPromptInfo, ushort> _answer = _ => Views.cmCancel;

    public TV13eMixedLineEndingTests()
    {
        TEditor.editorDialog = (d, i) =>
        {
            _calls.Add((d, i));
            return d == Views.edMixedLineEndings && i is EditorLineEndingPromptInfo info ? _answer(info) : Views.cmOK;
        };
    }

    public void Dispose() => TEditor.editorDialog = _savedDialog;

    private static TFileEditor Open(TempDirectory temp, string content, out string path)
    {
        path = Path.Combine(temp.Path, "mixed.txt");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(content));
        return new TFileEditor(new TRect(0, 0, 80, 25), null, null, null, path);
    }

    private static string Disk(string path) => Encoding.ASCII.GetString(File.ReadAllBytes(path));

    [Fact]
    public void Prompt_CarriesFileNameCountsAndSuggestion()
    {
        using var temp = new TempDirectory();
        var ed = Open(temp, "a\r\nb\nc\r\nd\re", out string path);
        Assert.True(ed.IsMixedLineEndingChoicePending);
        EditorLineEndingPromptInfo? seen = null;
        _answer = info => { seen = info; return Views.cmCancel; };

        Assert.False(ed.SaveFile());

        Assert.NotNull(seen);
        Assert.Equal(path, seen!.FileName);
        Assert.Equal(2, seen.CrLfCount);
        Assert.Equal(1, seen.LfCount);
        Assert.Equal(1, seen.CrCount);
        Assert.Equal(LineEndingKind.CrLf, seen.Suggested);
        Assert.Equal(LineEndingKind.CrLf, seen.Choice);
        Assert.Equal(new[] { LineEndingKind.Lf, LineEndingKind.CrLf, LineEndingKind.Cr }, seen.Choices);
    }

    [Theory]
    [InlineData(LineEndingKind.Lf, "a\nb\nc\nd")]
    [InlineData(LineEndingKind.CrLf, "a\r\nb\r\nc\r\nd")]
    [InlineData(LineEndingKind.Cr, "a\rb\rc\rd")]
    public void EachChoice_WritesThatStyle(LineEndingKind choice, string expected)
    {
        using var temp = new TempDirectory();
        var ed = Open(temp, Mixed, out string path);
        _answer = info => { info.Choice = choice; return Views.cmOK; };

        Assert.True(ed.SaveFile());

        Assert.Equal(expected, Disk(path));
        Assert.Single(_calls);
        Assert.Equal(choice, ed.SaveLineEnding);
        Assert.False(ed.IsMixedLineEndingChoicePending);
    }

    [Fact]
    public void YesAnswer_AlsoAcceptsAndUnchangedChoiceUsesSuggestion()
    {
        using var temp = new TempDirectory();
        var ed = Open(temp, "a\nb\nc\r\nd", out string path);
        _answer = _ => Views.cmYes;

        Assert.True(ed.SaveFile());
        Assert.Equal("a\nb\nc\nd", Disk(path));
    }

    [Theory]
    [InlineData(Views.cmCancel)]
    [InlineData(Views.cmNo)]
    [InlineData((ushort)0)]
    public void Cancel_WritesNothingAndReturnsFalse(ushort answer)
    {
        using var temp = new TempDirectory();
        var ed = Open(temp, Mixed, out string path);
        ed.SetSelect(0, 0, false);
        ed.InsertText("x");
        _answer = _ => answer;

        Assert.False(ed.SaveFile());

        Assert.Equal(Mixed, Disk(path));
        Assert.Single(Directory.GetFiles(temp.Path));
        Assert.True(ed.modified);
        Assert.True(ed.IsMixedLineEndingChoicePending);
        Assert.Single(_calls);
    }

    [Fact]
    public void Choice_IsRememberedForLaterSaves()
    {
        using var temp = new TempDirectory();
        var ed = Open(temp, Mixed, out string path);
        _answer = info => { info.Choice = LineEndingKind.Lf; return Views.cmOK; };
        Assert.True(ed.SaveFile());

        ed.SetSelect(ed.bufLen, ed.bufLen, false);
        ed.InsertText("\ne");
        Assert.True(ed.SaveFile());

        Assert.Single(_calls);
        Assert.Equal("a\nb\nc\nd\ne", Disk(path));
    }

    [Fact]
    public void Reload_AsksAgain()
    {
        using var temp = new TempDirectory();
        var ed = Open(temp, Mixed, out string path);
        _answer = _ => Views.cmCancel;
        Assert.False(ed.SaveFile());
        _answer = info => { info.Choice = LineEndingKind.Cr; return Views.cmOK; };
        Assert.True(ed.SaveFile());
        Assert.Equal("a\rb\rc\rd", Disk(path));

        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(Mixed));
        Assert.True(ed.LoadFile());
        Assert.True(ed.IsMixedLineEndingChoicePending);
        Assert.Equal(LineEndingKind.CrLf, ed.SaveLineEnding);
        _answer = info => { info.Choice = LineEndingKind.Lf; return Views.cmOK; };
        Assert.True(ed.SaveFile());

        Assert.Equal(3, _calls.Count);
        Assert.Equal("a\nb\nc\nd", Disk(path));
    }

    [Fact]
    public void ReloadOfNonMixedContent_ClearsPendingChoice()
    {
        using var temp = new TempDirectory();
        var ed = Open(temp, Mixed, out string path);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("a\nb\n"));
        Assert.True(ed.LoadFile());

        Assert.False(ed.IsMixedLineEndingChoicePending);
        Assert.True(ed.SaveFile());
        Assert.Empty(_calls);
        Assert.Equal("a\nb\n", Disk(path));
    }

    [Fact]
    public void ResetMixedLineEndingChoice_AsksAgain()
    {
        using var temp = new TempDirectory();
        var ed = Open(temp, Mixed, out string path);
        _answer = info => { info.Choice = LineEndingKind.Lf; return Views.cmOK; };
        Assert.True(ed.SaveFile());

        ed.ResetMixedLineEndingChoice();
        Assert.True(ed.IsMixedLineEndingChoicePending);
        _answer = info => { info.Choice = LineEndingKind.CrLf; return Views.cmOK; };
        Assert.True(ed.SaveFile());

        Assert.Equal(2, _calls.Count);
        Assert.Equal("a\r\nb\r\nc\r\nd", Disk(path));
    }

    [Fact]
    public void AssigningSaveLineEnding_IsAnExplicitChoice()
    {
        using var temp = new TempDirectory();
        var ed = Open(temp, Mixed, out string path);
        ed.SaveLineEnding = LineEndingKind.Lf;

        Assert.False(ed.IsMixedLineEndingChoicePending);
        Assert.True(ed.SaveFile());
        Assert.Empty(_calls);
        Assert.Equal("a\nb\nc\nd", Disk(path));
    }

    [Theory]
    [InlineData("a\r\nb\r\n")]
    [InlineData("a\nb\n")]
    [InlineData("a\rb\r")]
    [InlineData("single line")]
    public void NonMixed_NeverPromptsAndPreservesStyle(string content)
    {
        using var temp = new TempDirectory();
        var ed = Open(temp, content, out string path);
        Assert.False(ed.IsMixedLineEndingChoicePending);
        ed.ResetMixedLineEndingChoice();
        Assert.False(ed.IsMixedLineEndingChoicePending);

        Assert.True(ed.SaveFile());
        Assert.True(ed.SaveFile());

        Assert.Empty(_calls);
        Assert.Equal(content, Disk(path));
    }

    [Fact]
    public void DefaultEditorDialog_CancelsMixedSave()
    {
        using var temp = new TempDirectory();
        var ed = Open(temp, Mixed, out string path);
        TEditor.editorDialog = (_, _) => Views.cmCancel;

        Assert.False(ed.SaveFile());
        Assert.Equal(Mixed, Disk(path));
    }

    [Fact]
    public void PromptInfo_RejectsInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditorLineEndingPromptInfo("f", -1, 0, 0, LineEndingKind.Lf));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditorLineEndingPromptInfo("f", 0, -1, 0, LineEndingKind.Lf));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditorLineEndingPromptInfo("f", 0, 0, -1, LineEndingKind.Lf));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditorLineEndingPromptInfo("f", 1, 1, 0, LineEndingKind.Mixed));
        var info = new EditorLineEndingPromptInfo(null!, 1, 1, 0, LineEndingKind.Lf);
        Assert.Equal(string.Empty, info.FileName);
        Assert.Throws<ArgumentOutOfRangeException>(() => info.Choice = LineEndingKind.Unknown);
        Assert.Throws<ArgumentOutOfRangeException>(() => info.Choice = LineEndingKind.Mixed);
        Assert.Equal(LineEndingKind.Lf, info.Choice);
    }
}
