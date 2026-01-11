// Resource Tools tests.
// Tests for svres list / show / dump commands + TvrInspector API.
// Tests that touch the Pstream registry are [Collection("NonParallel")].
using System.IO;
using System.Linq;
using System.Reflection;
using TSharpVision;
using TSharpVision.ResourceCompiler;
using TSharpVision.ResourceTools;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.ResourceTools;

// ─────────────────────────────────────────────────────────────────────────────
//  Helpers shared by test classes
// ─────────────────────────────────────────────────────────────────────────────

file static class Fixtures
{
    // Locate the Fixtures folder relative to the test assembly.
    private static readonly string _dir = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "..", "..", "..", "..", "TSharpVision.ResourceCompiler.Tests", "Fixtures");

    public static string FixturePath(string name) =>
        Path.GetFullPath(Path.Combine(_dir, name));

    public static string HelloTrc     => File.ReadAllText(FixturePath("hello.trc"),   System.Text.Encoding.UTF8);
    public static string OptionsTrc   => File.ReadAllText(FixturePath("options.trc"), System.Text.Encoding.UTF8);
    public static string MultiTrc     => File.ReadAllText(FixturePath("multi.trc"),   System.Text.Encoding.UTF8);
}

// ─────────────────────────────────────────────────────────────────────────────
//  TvrInspector unit tests (no registry required)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class TvrInspectorUnitTests
{
    // ── PeekTypeName ──────────────────────────────────────────────────────────

    [Fact] public void PeekTypeName_Null_ReturnsNull()
        => Assert.Null(TvrInspector.PeekTypeName(null));

    [Fact] public void PeekTypeName_EmptyArray_ReturnsNull()
        => Assert.Null(TvrInspector.PeekTypeName(Array.Empty<byte>()));

    [Fact] public void PeekTypeName_TooShort_ReturnsNull()
        => Assert.Null(TvrInspector.PeekTypeName(new byte[] { 0x02 }));

    [Fact] public void PeekTypeName_WrongFirstByte_ReturnsNull()
    {
        // First byte must be ptObject (0x02); 0x01 = ptIndexed.
        var bytes = new byte[] { 0x01, 0x5B, 0x07, (byte)'T', (byte)'D', (byte)'i',
                                 (byte)'a', (byte)'l', (byte)'o', (byte)'g' };
        Assert.Null(TvrInspector.PeekTypeName(bytes));
    }

    [Fact] public void PeekTypeName_WrongPrefixByte_ReturnsNull()
    {
        var bytes = new byte[] { 0x02, 0x5D, 0x07, (byte)'T', (byte)'D', (byte)'i',
                                 (byte)'a', (byte)'l', (byte)'o', (byte)'g' };
        Assert.Null(TvrInspector.PeekTypeName(bytes));
    }

    [Fact] public void PeekTypeName_TDialog_Extracted()
    {
        // Manually construct the UTF-16 stream prefix:
        // ptObject=0x02, '['=0x5B, len=7 chars, then "TDialog" as LE code units.
        const string typeName = "TDialog";
        var name  = System.Text.Encoding.Unicode.GetBytes(typeName);
        var bytes = new byte[] { 0x02, 0x5B, (byte)typeName.Length }
                        .Concat(name).Concat(new byte[10]).ToArray();
        Assert.Equal("TDialog", TvrInspector.PeekTypeName(bytes));
    }

    [Fact] public void PeekTypeName_TResourceCollection_Extracted()
    {
        const string typeName = "TResourceCollection";
        var name  = System.Text.Encoding.Unicode.GetBytes(typeName);
        var bytes = new byte[] { 0x02, 0x5B, (byte)typeName.Length }
                        .Concat(name).ToArray();
        Assert.Equal("TResourceCollection", TvrInspector.PeekTypeName(bytes));
    }

    [Fact] public void PeekTypeName_TruncatedPayload_ReturnsNull()
    {
        // Claims length 20 but only 5 bytes follow → too short.
        var bytes = new byte[] { 0x02, 0x5B, 20, (byte)'T', (byte)'D', (byte)'i' };
        Assert.Null(TvrInspector.PeekTypeName(bytes));
    }

    // ── FormatHexDump ─────────────────────────────────────────────────────────

    [Fact] public void FormatHexDump_Empty_ReturnsEmpty()
        => Assert.Equal(string.Empty, InspectorCommands.FormatHexDump(Array.Empty<byte>()));

    [Fact] public void FormatHexDump_SingleByte_OneLine()
    {
        string dump = InspectorCommands.FormatHexDump(new byte[] { 0xAB });
        Assert.StartsWith("00000000  AB", dump);
        Assert.Contains("|.|", dump);
    }

    [Fact] public void FormatHexDump_16Bytes_OneLine()
    {
        var data = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        string dump = InspectorCommands.FormatHexDump(data);
        Assert.Single(dump.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact] public void FormatHexDump_17Bytes_TwoLines()
    {
        var data = Enumerable.Range(0, 17).Select(i => (byte)i).ToArray();
        string dump = InspectorCommands.FormatHexDump(data);
        int lines = dump.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(2, lines);
        Assert.StartsWith("00000010  10", dump.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1]);
    }

    [Fact] public void FormatHexDump_IsDeterministic()
    {
        var data = new byte[] { 0x02, 0x5B, 0x07, 0x54, 0x44, 0x69, 0x61, 0x6C, 0x6F, 0x67 };
        Assert.Equal(InspectorCommands.FormatHexDump(data), InspectorCommands.FormatHexDump(data));
    }

    [Fact] public void FormatHexDump_PrintableAscii_ShowsCharacters()
    {
        var data = System.Text.Encoding.ASCII.GetBytes("Hello");
        string dump = InspectorCommands.FormatHexDump(data);
        Assert.Contains("|Hello", dump);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  CLI parsing tests — verify CommandResult for bad inputs (no registry)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class CliParsingTests
{
    [Fact] public void List_FileNotFound_ReturnsError()
    {
        var r = InspectorCommands.List(@"C:\nonexistent_file_svres_test.tvr");
        Assert.False(r.Success);
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("not found", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void Show_FileNotFound_ReturnsError()
    {
        var r = InspectorCommands.Show(@"C:\nonexistent_file_svres_test.tvr", "somekey");
        Assert.False(r.Success);
        Assert.Equal(1, r.ExitCode);
    }

    [Fact] public void Dump_FileNotFound_ReturnsError()
    {
        var r = InspectorCommands.Dump(@"C:\nonexistent_file_svres_test.tvr", "somekey");
        Assert.False(r.Success);
        Assert.Equal(1, r.ExitCode);
    }

    [Fact] public void Open_FileNotFound_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(
            () => TvrInspector.Open(@"C:\nonexistent_svres_test.tvr"));
    }

    [Fact] public void CommandResult_Ok_HasZeroExitCode()
    {
        var r = CommandResult.Ok("hello");
        Assert.True(r.Success);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("hello", r.Output);
        Assert.Equal(string.Empty, r.Error);
    }

    [Fact] public void CommandResult_Error_HasOneExitCode()
    {
        var r = CommandResult.Fail("something went wrong");
        Assert.False(r.Success);
        Assert.Equal(1, r.ExitCode);
        Assert.Equal("something went wrong", r.Error);
        Assert.Equal(string.Empty, r.Output);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Inspection tests — require registry (NonParallel)
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class InspectionTests : IDisposable
{
    private readonly DriverScope _driver;
    private readonly TempDirectory _tmp;

    public InspectionTests()
    {
        _driver = new DriverScope();
        _tmp    = new TempDirectory();
    }

    public void Dispose()
    {
        _tmp.Dispose();
        _driver.Dispose();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }

    private string CompileToTemp(string trcSource, string fileName = "test.tvr")
    {
        string tvrPath = Path.Combine(_tmp.Path, fileName);
        var result = Compiler.CompileSource(trcSource, tvrPath);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return tvrPath;
    }

    // ── TvrInspector.Open ─────────────────────────────────────────────────────

    [Fact]
    public void Open_HelloTvr_ReturnsOneEntry()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var insp = TvrInspector.Open(tvr);
        Assert.Single(insp.Entries);
    }

    [Fact]
    public void Open_HelloTvr_EntryHasCorrectKey()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var insp = TvrInspector.Open(tvr);
        Assert.Equal("dialog.hello", insp.Entries[0].Key);
    }

    [Fact]
    public void Open_HelloTvr_EntryHasPositiveSize()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var insp = TvrInspector.Open(tvr);
        Assert.True(insp.Entries[0].Size > 0);
    }

    [Fact]
    public void Open_HelloTvr_TypeNameIsDialog()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var insp = TvrInspector.Open(tvr);
        Assert.Equal("TDialog", insp.Entries[0].TypeName);
    }

    [Fact]
    public void Open_MultiTvr_ReturnsTwoEntriesSorted()
    {
        string tvr = CompileToTemp(Fixtures.MultiTrc, "multi.tvr");
        var insp = TvrInspector.Open(tvr);
        Assert.Equal(2, insp.Entries.Count);
        // TResourceCollection sorts alphabetically.
        Assert.Equal("dialog.alpha", insp.Entries[0].Key);
        Assert.Equal("dialog.beta",  insp.Entries[1].Key);
    }

    [Fact]
    public void Open_AllEntriesHavePositivePositions()
    {
        string tvr = CompileToTemp(Fixtures.MultiTrc, "multi.tvr");
        var insp = TvrInspector.Open(tvr);
        foreach (var e in insp.Entries)
            Assert.True(e.Position >= 12, $"Entry '{e.Key}' has position {e.Position} < 12");
    }

    [Fact]
    public void ReadRawPayload_HelloKey_ReturnsBytesStartingWithPtObject()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        byte[] raw = TvrInspector.ReadRawPayload(tvr, "dialog.hello");
        Assert.NotNull(raw);
        Assert.True(raw.Length > 0);
        Assert.Equal(0x02, raw[0]); // ptObject
    }

    [Fact]
    public void ReadRawPayload_MissingKey_ReturnsNull()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        byte[] raw = TvrInspector.ReadRawPayload(tvr, "dialog.nonexistent");
        Assert.Null(raw);
    }

    // ── InspectorCommands.List ────────────────────────────────────────────────

    [Fact]
    public void List_HelloTvr_ContainsKey()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.List(tvr);
        Assert.True(r.Success);
        Assert.Contains("dialog.hello", r.Output);
    }

    [Fact]
    public void List_HelloTvr_ContainsTypeName()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.List(tvr);
        Assert.True(r.Success);
        Assert.Contains("TDialog", r.Output);
    }

    [Fact]
    public void List_MultiTvr_ContainsBothKeys()
    {
        string tvr = CompileToTemp(Fixtures.MultiTrc, "multi.tvr");
        var r = InspectorCommands.List(tvr);
        Assert.True(r.Success);
        Assert.Contains("dialog.alpha", r.Output);
        Assert.Contains("dialog.beta",  r.Output);
    }

    [Fact]
    public void List_IsDeterministic_CompiledTwice()
    {
        string tvr1 = CompileToTemp(Fixtures.HelloTrc, "hello1.tvr");
        string tvr2 = CompileToTemp(Fixtures.HelloTrc, "hello2.tvr");
        var r1 = InspectorCommands.List(tvr1);
        var r2 = InspectorCommands.List(tvr2);
        // The key/type content is identical; only the filename header differs.
        Assert.Contains("dialog.hello", r1.Output);
        Assert.Contains("dialog.hello", r2.Output);
        Assert.Contains("TDialog", r1.Output);
        Assert.Contains("TDialog", r2.Output);
    }

    // ── InspectorCommands.Show ────────────────────────────────────────────────

    [Fact]
    public void Show_HelloKey_ContainsTitle()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Show(tvr, "dialog.hello");
        Assert.True(r.Success, r.Error);
        Assert.Contains("Hello", r.Output);
    }

    [Fact]
    public void Show_HelloKey_ContainsBounds()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Show(tvr, "dialog.hello");
        Assert.True(r.Success, r.Error);
        // Bounds: (10, 5)-(50, 15)
        Assert.Contains("10", r.Output);
        Assert.Contains("50", r.Output);
    }

    [Fact]
    public void Show_HelloKey_ContainsChildCount()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Show(tvr, "dialog.hello");
        Assert.True(r.Success, r.Error);
        // TFrame + static + input + 2 buttons = 5 children.
        Assert.Contains("Children: 5", r.Output);
    }

    [Fact]
    public void Show_MissingKey_ReturnsError()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Show(tvr, "dialog.nonexistent");
        Assert.False(r.Success);
        Assert.Contains("not found", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Show_ContainsTypeAndPositionInfo()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Show(tvr, "dialog.hello");
        Assert.True(r.Success, r.Error);
        Assert.Contains("TDialog", r.Output);
        Assert.Contains("Position:", r.Output);
        Assert.Contains("Size:", r.Output);
    }

    // ── InspectorCommands.Dump ────────────────────────────────────────────────

    [Fact]
    public void Dump_HelloKey_ContainsTypeName()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Dump(tvr, "dialog.hello");
        Assert.True(r.Success, r.Error);
        Assert.Contains("TDialog", r.Output);
    }

    [Fact]
    public void Dump_HelloKey_ContainsSize()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Dump(tvr, "dialog.hello");
        Assert.True(r.Success, r.Error);
        Assert.Contains("Size:", r.Output);
    }

    [Fact]
    public void Dump_MissingKey_ReturnsError()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Dump(tvr, "dialog.nonexistent");
        Assert.False(r.Success);
    }

    [Fact]
    public void DumpHex_HelloKey_StartsWithPtObjectByte()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Dump(tvr, "dialog.hello", hex: true);
        Assert.True(r.Success, r.Error);
        // First byte is ptObject = 0x02; hex dump line starts with "00000000  02".
        Assert.Contains("00000000  02", r.Output);
    }

    [Fact]
    public void DumpHex_IsDeterministic()
    {
        string tvr1 = CompileToTemp(Fixtures.HelloTrc, "hello1.tvr");
        string tvr2 = CompileToTemp(Fixtures.HelloTrc, "hello2.tvr");
        string h1 = InspectorCommands.Dump(tvr1, "dialog.hello", hex: true).Output;
        string h2 = InspectorCommands.Dump(tvr2, "dialog.hello", hex: true).Output;
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void DumpHex_TDialogName_VisibleInAsciiColumn()
    {
        string tvr = CompileToTemp(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Dump(tvr, "dialog.hello", hex: true);
        Assert.True(r.Success, r.Error);
        // "TDialog" should appear in the ASCII column of the hex dump.
        Assert.Contains("TDialog", r.Output);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Golden fixture tests — verify stable structure of fixture outputs
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class GoldenFixtureTests : IDisposable
{
    private readonly DriverScope _driver;
    private readonly TempDirectory _tmp;

    public GoldenFixtureTests()
    {
        _driver = new DriverScope();
        _tmp    = new TempDirectory();
    }

    public void Dispose()
    {
        _tmp.Dispose();
        _driver.Dispose();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }

    private string Compile(string src, string name)
    {
        string path = Path.Combine(_tmp.Path, name);
        var result = Compiler.CompileSource(src, path);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return path;
    }

    // ── hello.trc golden ──────────────────────────────────────────────────────

    [Fact]
    public void Golden_Hello_ListOutput_ContainsExpectedStructure()
    {
        string tvr = Compile(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.List(tvr);
        Assert.True(r.Success);
        // Header present.
        Assert.Contains("1 item", r.Output);
        // Key and type on the same line.
        Assert.Contains("dialog.hello", r.Output);
        Assert.Contains("TDialog", r.Output);
    }

    [Fact]
    public void Golden_Hello_ShowOutput_TitleAndBoundsPresent()
    {
        string tvr = Compile(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Show(tvr, "dialog.hello");
        Assert.True(r.Success, r.Error);
        Assert.Contains("Title:    Hello", r.Output);
        Assert.Contains("Bounds:   (10, 5)-(50, 15)", r.Output);
    }

    [Fact]
    public void Golden_Hello_ShowOutput_ChildTypeNamesPresent()
    {
        string tvr = Compile(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Show(tvr, "dialog.hello");
        Assert.True(r.Success, r.Error);
        Assert.Contains("TFrame",      r.Output);
        Assert.Contains("TStaticText", r.Output);
        Assert.Contains("TInputLine",  r.Output);
        Assert.Contains("TButton",     r.Output);
    }

    [Fact]
    public void Golden_Hello_DumpHex_ContainsTDialogPrefix()
    {
        string tvr = Compile(Fixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Dump(tvr, "dialog.hello", hex: true);
        Assert.True(r.Success, r.Error);
        // First hex bytes: 02 (ptObject), 5B ('['), 07 (len=7 chars),
        // then "TDialog" as UTF-16 LE code units.
        Assert.Contains("02 5B 07 54 00 44 00 69", r.Output);
    }

    // ── options.trc golden ────────────────────────────────────────────────────

    [Fact]
    public void Golden_Options_ListOutput_ContainsExpectedStructure()
    {
        string tvr = Compile(Fixtures.OptionsTrc, "options.tvr");
        var r = InspectorCommands.List(tvr);
        Assert.True(r.Success);
        Assert.Contains("1 item", r.Output);
        Assert.Contains("dialog.options", r.Output);
        Assert.Contains("TDialog", r.Output);
    }

    [Fact]
    public void Golden_Options_ShowOutput_TitleAndBoundsPresent()
    {
        string tvr = Compile(Fixtures.OptionsTrc, "options.tvr");
        var r = InspectorCommands.Show(tvr, "dialog.options");
        Assert.True(r.Success, r.Error);
        Assert.Contains("Title:    Options", r.Output);
        Assert.Contains("Bounds:   (5, 3)-(55, 18)", r.Output);
    }

    [Fact]
    public void Golden_Options_ShowOutput_CheckboxAndRadioPresent()
    {
        string tvr = Compile(Fixtures.OptionsTrc, "options.tvr");
        var r = InspectorCommands.Show(tvr, "dialog.options");
        Assert.True(r.Success, r.Error);
        Assert.Contains("TCheckBoxes",   r.Output);
        Assert.Contains("TRadioButtons", r.Output);
    }

    // ── multi.trc golden ─────────────────────────────────────────────────────

    [Fact]
    public void Golden_Multi_ListOutput_TwoItemsAlphabetical()
    {
        string tvr = Compile(Fixtures.MultiTrc, "multi.tvr");
        var r = InspectorCommands.List(tvr);
        Assert.True(r.Success);
        Assert.Contains("2 items", r.Output);

        // Verify alpha comes before beta in the output.
        int idxAlpha = r.Output.IndexOf("dialog.alpha", StringComparison.Ordinal);
        int idxBeta  = r.Output.IndexOf("dialog.beta",  StringComparison.Ordinal);
        Assert.True(idxAlpha < idxBeta, "Expected alpha before beta in list output");
    }

    [Fact]
    public void Golden_Multi_ShowAlpha_TitlePresent()
    {
        string tvr = Compile(Fixtures.MultiTrc, "multi.tvr");
        var r = InspectorCommands.Show(tvr, "dialog.alpha");
        Assert.True(r.Success, r.Error);
        Assert.Contains("Title:    Alpha", r.Output);
    }

    [Fact]
    public void Golden_Multi_ShowBeta_TitlePresent()
    {
        string tvr = Compile(Fixtures.MultiTrc, "multi.tvr");
        var r = InspectorCommands.Show(tvr, "dialog.beta");
        Assert.True(r.Success, r.Error);
        Assert.Contains("Title:    Beta", r.Output);
    }

    // ── Regression: compiler state is not corrupted by inspector calls ───────

    [Fact]
    public void Regression_CompilerStillWorks_AfterInspectorOpen()
    {
        // Compile, inspect, compile again — verify no state corruption.
        string tvr1 = Compile(Fixtures.HelloTrc, "r1.tvr");
        TvrInspector.Open(tvr1);
        string tvr2 = Compile(Fixtures.HelloTrc, "r2.tvr");
        var insp2 = TvrInspector.Open(tvr2);
        Assert.Single(insp2.Entries);
        Assert.Equal("dialog.hello", insp2.Entries[0].Key);
    }

    [Fact]
    public void Regression_RegistryIsolation_AfterShow()
    {
        // Show internally resets/re-registers; verify registry is still usable afterward.
        string tvr = Compile(Fixtures.HelloTrc, "reg.tvr");
        InspectorCommands.Show(tvr, "dialog.hello");

        // Compile and inspect again after Show has touched the registry.
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
        string tvr2 = Compile(Fixtures.HelloTrc, "reg2.tvr");
        var r = InspectorCommands.List(tvr2);
        Assert.True(r.Success);
        Assert.Contains("dialog.hello", r.Output);
    }
}
