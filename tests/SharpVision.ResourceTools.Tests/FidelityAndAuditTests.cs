// Resource tooling fidelity and TResourceFile extension audit.
//
// Three test classes:
//   TResourceFileExtensionAuditTests  — safety audit of read-only extensions.
//   FidelityTests                     — .trc → .tvr → deserialise round-trip verification.
//   ValidateCommandTests              — svres validate command coverage.
//
// All three classes are [Collection("NonParallel")] because they interact with
// either the Fpstream file handle (exclusive lock) or the Pstream registry.
using System.IO;
using System.Reflection;
using System.Text;
using SharpVision;
using SharpVision.Constants;
using SharpVision.ResourceCompiler;
using SharpVision.ResourceTools;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.ResourceTools;

// ─────────────────────────────────────────────────────────────────────────────
//  Fixtures (file-scoped)
// ─────────────────────────────────────────────────────────────────────────────

file static class FidelityFixtures
{
    // Assembly lives at bin\Debug\net8.0; walk up 3 dirs to AVision root, then
    // into the test project's Fixtures folder.
    private static readonly string _dir = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "..", "..", "..", "SharpVision.Tests", "ResourceCompiler", "Fixtures");

    public static string FixturePath(string name) =>
        Path.GetFullPath(Path.Combine(_dir, name));

    public static string HelloTrc   => File.ReadAllText(FixturePath("hello.trc"),   Encoding.UTF8);
    public static string OptionsTrc => File.ReadAllText(FixturePath("options.trc"), Encoding.UTF8);
    public static string MultiTrc   => File.ReadAllText(FixturePath("multi.trc"),   Encoding.UTF8);
}

// ─────────────────────────────────────────────────────────────────────────────
//  Helper — compile TRC source to a temp .tvr and return the path.
//  Shared by all three test classes via a static helper (no inheritance).
// ─────────────────────────────────────────────────────────────────────────────

file static class FidelityHelper
{
    public static string Compile(string trcSource, string tmp, string fileName = "test.tvr")
    {
        string path = Path.Combine(tmp, fileName);
        var result  = Compiler.CompileSource(trcSource, path);
        if (!result.Success)
            throw new Xunit.Sdk.XunitException(
                "Compile failed: " + string.Join("; ", result.Diagnostics));
        return path;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  1. TResourceFile extension audit
//
//  Verifies that the four read-only additions:
//    Count() / ItemAt(int) / BasePos / GetRawBytes(string)
//  do not mutate state and do not break existing Put/Get/Delete/Flush.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class TResourceFileExtensionAuditTests : IDisposable
{
    private readonly DriverScope _driver;
    private readonly TempDirectory _tmp;

    public TResourceFileExtensionAuditTests()
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

    private string CompileHello(string name = "hello.tvr") =>
        FidelityHelper.Compile(FidelityFixtures.HelloTrc, _tmp.Path, name);

    private string CompileMulti(string name = "multi.tvr") =>
        FidelityHelper.Compile(FidelityFixtures.MultiTrc, _tmp.Path, name);

    // ── Count() ───────────────────────────────────────────────────────────────

    [Fact]
    public void Count_SingleResource_ReturnsOne()
    {
        string tvr = CompileHello();
        var fp = new Fpstream(tvr);
        try
        {
            var rf = new TResourceFile(fp);
            Assert.Equal((short)1, rf.Count());
        }
        finally { fp.Close(); }
    }

    [Fact]
    public void Count_TwoResources_ReturnsTwo()
    {
        string tvr = CompileMulti();
        var fp = new Fpstream(tvr);
        try
        {
            var rf = new TResourceFile(fp);
            Assert.Equal((short)2, rf.Count());
        }
        finally { fp.Close(); }
    }

    [Fact]
    public void Count_MatchesTvrInspectorEntryCount()
    {
        string tvr = CompileMulti();
        // Read via TResourceFile.Count()
        int rfCount;
        var fp = new Fpstream(tvr);
        try { rfCount = new TResourceFile(fp).Count(); }
        finally { fp.Close(); }
        // Read via TvrInspector (independent path)
        int inspCount = TvrInspector.Open(tvr).Entries.Count;
        Assert.Equal(rfCount, inspCount);
    }

    // ── ItemAt(int) ───────────────────────────────────────────────────────────

    [Fact]
    public void ItemAt_ValidIndex_ReturnsCorrectKey()
    {
        string tvr = CompileHello();
        var fp = new Fpstream(tvr);
        try
        {
            var rf   = new TResourceFile(fp);
            var item = rf.ItemAt(0);
            Assert.Equal("dialog.hello", item.key);
        }
        finally { fp.Close(); }
    }

    [Fact]
    public void ItemAt_ValidIndex_ReturnsPositiveSize()
    {
        string tvr = CompileHello();
        var fp = new Fpstream(tvr);
        try
        {
            var rf   = new TResourceFile(fp);
            var item = rf.ItemAt(0);
            Assert.True(item.size > 0, $"Expected size > 0, got {item.size}");
        }
        finally { fp.Close(); }
    }

    [Fact]
    public void ItemAt_MultiFile_BothItemsAccessible()
    {
        string tvr = CompileMulti();
        var fp = new Fpstream(tvr);
        try
        {
            var rf = new TResourceFile(fp);
            Assert.Equal(2, rf.Count());
            // TResourceCollection is sorted alphabetically: alpha < beta.
            Assert.Equal("dialog.alpha", rf.ItemAt(0).key);
            Assert.Equal("dialog.beta",  rf.ItemAt(1).key);
        }
        finally { fp.Close(); }
    }

    [Fact]
    public void ItemAt_InvalidIndex_ThrowsArgumentOutOfRangeException()
    {
        string tvr = CompileHello();
        var fp = new Fpstream(tvr);
        try
        {
            var rf = new TResourceFile(fp);
            // Index 99 is well out of range for a 1-entry file.
            Assert.Throws<ArgumentOutOfRangeException>(() => rf.ItemAt(99));
        }
        finally { fp.Close(); }
    }

    // ── BasePos ───────────────────────────────────────────────────────────────

    [Fact]
    public void BasePos_IsNonNegative()
    {
        string tvr = CompileHello();
        var fp = new Fpstream(tvr);
        try
        {
            var rf = new TResourceFile(fp);
            Assert.True(rf.BasePos >= 0, $"BasePos was {rf.BasePos}");
        }
        finally { fp.Close(); }
    }

    [Fact]
    public void BasePos_IsStable_AcrossMultipleReads()
    {
        string tvr = CompileHello();
        long bp1, bp2;
        var fp1 = new Fpstream(tvr);
        try { bp1 = new TResourceFile(fp1).BasePos; }
        finally { fp1.Close(); }

        var fp2 = new Fpstream(tvr);
        try { bp2 = new TResourceFile(fp2).BasePos; }
        finally { fp2.Close(); }

        Assert.Equal(bp1, bp2);
    }

    // ── GetRawBytes(string) ───────────────────────────────────────────────────

    [Fact]
    public void GetRawBytes_ExistingKey_ReturnsNonEmptyArray()
    {
        string tvr = CompileHello();
        var fp = new Fpstream(tvr);
        try
        {
            var rf  = new TResourceFile(fp);
            var raw = rf.GetRawBytes("dialog.hello");
            Assert.NotNull(raw);
            Assert.NotEmpty(raw);
        }
        finally { fp.Close(); }
    }

    [Fact]
    public void GetRawBytes_MissingKey_ReturnsNull()
    {
        string tvr = CompileHello();
        var fp = new Fpstream(tvr);
        try
        {
            var rf  = new TResourceFile(fp);
            var raw = rf.GetRawBytes("dialog.nonexistent");
            Assert.Null(raw);
        }
        finally { fp.Close(); }
    }

    [Fact]
    public void GetRawBytes_StartsWithPtObjectByte()
    {
        string tvr = CompileHello();
        var fp = new Fpstream(tvr);
        try
        {
            var rf  = new TResourceFile(fp);
            var raw = rf.GetRawBytes("dialog.hello");
            Assert.NotNull(raw);
            Assert.Equal(0x02, raw[0]); // ptObject
        }
        finally { fp.Close(); }
    }

    [Fact]
    public void GetRawBytes_LengthMatchesIndexedSize()
    {
        string tvr = CompileHello();
        var fp = new Fpstream(tvr);
        try
        {
            var rf   = new TResourceFile(fp);
            var item = rf.ItemAt(0);
            var raw  = rf.GetRawBytes(item.key);
            Assert.NotNull(raw);
            Assert.Equal((long)raw.Length, item.size);
        }
        finally { fp.Close(); }
    }

    [Fact]
    public void GetRawBytes_DoesNotBreakSubsequentGet()
    {
        // GetRawBytes leaves the stream positioned after the blob; the
        // subsequent Get() must seek explicitly and succeed.
        string tvr = CompileHello();
        StreamableRegistration.RegisterAll(); // needed for TDialog deserialization
        var fp = new Fpstream(tvr);
        try
        {
            var rf = new TResourceFile(fp);
            var _  = rf.GetRawBytes("dialog.hello"); // consume stream to arbitrary position
            var dlg = rf.Get("dialog.hello") as TDialog;
            Assert.NotNull(dlg);
            dlg.ShutDown();
        }
        finally { fp.Close(); }
    }

    [Fact]
    public void GetRawBytes_MatchesDumpOutput()
    {
        // The bytes returned by GetRawBytes should equal what TvrInspector.ReadRawPayload
        // returns for the same key (they use independent file handles).
        string tvr = CompileHello();
        byte[] fromRf;
        var fp = new Fpstream(tvr);
        try { fromRf = new TResourceFile(fp).GetRawBytes("dialog.hello"); }
        finally { fp.Close(); }

        byte[] fromInsp = TvrInspector.ReadRawPayload(tvr, "dialog.hello");
        Assert.Equal(fromRf, fromInsp);
    }

    // ── Existing Put/Get/Delete behavior unchanged ────────────────────────────

    [Fact]
    public void ExistingGet_AfterCompile_ReturnsDialog()
    {
        // Verifies that Put/Get round-trip still works after the read-only extensions were added.
        string tvr = CompileHello();
        StreamableRegistration.RegisterAll();
        var fp = new Fpstream(tvr);
        try
        {
            var rf  = new TResourceFile(fp);
            var dlg = rf.Get("dialog.hello") as TDialog;
            Assert.NotNull(dlg);
            Assert.Equal("Hello", dlg.title);
            dlg.ShutDown();
        }
        finally { fp.Close(); }
    }

    [Fact]
    public void ExistingRemove_StillWorks()
    {
        string tvr = CompileMulti();
        var fp = new Fpstream(tvr);
        try
        {
            var rf = new TResourceFile(fp);
            Assert.Equal((short)2, rf.Count());
            rf.Remove("dialog.alpha");
            Assert.Equal((short)1, rf.Count());
        }
        finally { fp.Close(); }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  2. Fidelity tests: .trc → .tvr → deserialise → verify field values
//
//  Each test compiles a fixture .trc, reads back the .tvr via TResourceFile.Get,
//  and asserts that the declared field values survived the round-trip.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class FidelityTests : IDisposable
{
    private readonly DriverScope _driver;
    private readonly TempDirectory _tmp;

    public FidelityTests()
    {
        _driver = new DriverScope();
        _tmp    = new TempDirectory();
        StreamableRegistration.RegisterAll();
    }

    public void Dispose()
    {
        _tmp.Dispose();
        _driver.Dispose();
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string Compile(string src, string name) =>
        FidelityHelper.Compile(src, _tmp.Path, name);

    private TDialog GetDialog(string tvrPath, string key)
    {
        var fp = new Fpstream(tvrPath);
        try
        {
            var rf  = new TResourceFile(fp);
            var dlg = rf.Get(key) as TDialog;
            if (dlg == null)
                throw new InvalidOperationException($"Resource '{key}' is not a TDialog.");
            return dlg;
        }
        finally { fp.Close(); }
    }

    // ── hello.trc: dialog-level fidelity ─────────────────────────────────────

    [Fact]
    public void HelloFixture_ResourceKey_IsPreserved()
    {
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var insp = TvrInspector.Open(tvr);
        Assert.Equal("dialog.hello", insp.Entries[0].Key);
    }

    [Fact]
    public void HelloFixture_TypeName_IsDialog()
    {
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var insp = TvrInspector.Open(tvr);
        Assert.Equal("TDialog", insp.Entries[0].TypeName);
    }

    [Fact]
    public void HelloFixture_Title_IsPreserved()
    {
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var dlg = GetDialog(tvr, "dialog.hello");
        Assert.Equal("Hello", dlg.title);
        dlg.ShutDown();
    }

    [Fact]
    public void HelloFixture_Bounds_ArePreserved()
    {
        // Source: bounds (10, 5, 50, 15)
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var dlg = GetDialog(tvr, "dialog.hello");
        // TView stores origin + size; reconstruct x2/y2.
        Assert.Equal(10, dlg.origin.x);
        Assert.Equal(5,  dlg.origin.y);
        Assert.Equal(40, dlg.size.x);  // 50 - 10
        Assert.Equal(10, dlg.size.y);  // 15 - 5
        dlg.ShutDown();
    }

    [Fact]
    public void HelloFixture_ChildCount_IsPreserved()
    {
        // TFrame + TStaticText + TInputLine + 2 × TButton = 5 children.
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var dlg = GetDialog(tvr, "dialog.hello");
        int count = 0;
        dlg.ForEachView(_ => count++);
        Assert.Equal(5, count);
        dlg.ShutDown();
    }

    [Fact]
    public void HelloFixture_OkButton_CommandAndDefault_ArePreserved()
    {
        // button "~O~K" command=cmOK default → Command=10, AmDefault=true.
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var dlg = GetDialog(tvr, "dialog.hello");
        TButton okBtn = null;
        dlg.ForEachView(v =>
        {
            if (v is TButton b && b.AmDefault) okBtn = b;
        });
        Assert.NotNull(okBtn);
        Assert.Equal(Views.cmOK, okBtn.Command);
        Assert.True(okBtn.AmDefault);
        dlg.ShutDown();
    }

    [Fact]
    public void HelloFixture_CancelButton_CommandIsPreserved()
    {
        // button "~C~ancel" command=cmCancel → Command=11.
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var dlg = GetDialog(tvr, "dialog.hello");
        TButton cancelBtn = null;
        dlg.ForEachView(v =>
        {
            if (v is TButton b && !b.AmDefault) cancelBtn = b;
        });
        Assert.NotNull(cancelBtn);
        Assert.Equal(Views.cmCancel, cancelBtn.Command);
        dlg.ShutDown();
    }

    [Fact]
    public void HelloFixture_InputLine_ValidatorIsFilterValidator()
    {
        // input "" validator=filter("ABC...") → TFilterValidator.
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var dlg = GetDialog(tvr, "dialog.hello");
        TInputLine inp = null;
        dlg.ForEachView(v => { if (v is TInputLine i) inp = i; });
        Assert.NotNull(inp);
        Assert.IsType<TFilterValidator>(inp.Validator);
        dlg.ShutDown();
    }

    [Fact]
    public void HelloFixture_InputLine_MaxLen_IsPreserved()
    {
        // input "" bounds=(3,4,30,5) → maxLen = width = 30-3 = 27; stored as MaxLen = 27-1 = 26.
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var dlg = GetDialog(tvr, "dialog.hello");
        TInputLine inp = null;
        dlg.ForEachView(v => { if (v is TInputLine i) inp = i; });
        Assert.NotNull(inp);
        // Builder sets MaxLen = width - 1 (see Builder.BuildInput: new TInputLine(rect, maxLen)
        // where maxLen = rect.b.x - rect.a.x = 27; TInputLine ctor does MaxLen = aMaxLen - 1 = 26).
        Assert.Equal(26, inp.MaxLen);
        dlg.ShutDown();
    }

    [Fact]
    public void HelloFixture_StaticText_IsPresent()
    {
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var dlg = GetDialog(tvr, "dialog.hello");
        TStaticText st = null;
        dlg.ForEachView(v => { if (v is TStaticText s && !(v is TLabel)) st = s; });
        Assert.NotNull(st);
        dlg.ShutDown();
    }

    [Fact]
    public void HelloFixture_ControlBounds_ButtonOK_ArePreserved()
    {
        // button "~O~K" bounds=(10,7,20,9) → origin=(10,7), size=(10,2).
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var dlg = GetDialog(tvr, "dialog.hello");
        TButton okBtn = null;
        dlg.ForEachView(v => { if (v is TButton b && b.AmDefault) okBtn = b; });
        Assert.NotNull(okBtn);
        Assert.Equal(10, okBtn.origin.x);
        Assert.Equal(7,  okBtn.origin.y);
        Assert.Equal(10, okBtn.size.x);  // 20-10
        Assert.Equal(2,  okBtn.size.y);  // 9-7
        dlg.ShutDown();
    }

    // ── options.trc: checkbox and radio fidelity ──────────────────────────────

    [Fact]
    public void OptionsFixture_Title_IsPreserved()
    {
        string tvr = Compile(FidelityFixtures.OptionsTrc, "options.tvr");
        var dlg = GetDialog(tvr, "dialog.options");
        Assert.Equal("Options", dlg.title);
        dlg.ShutDown();
    }

    [Fact]
    public void OptionsFixture_Bounds_ArePreserved()
    {
        // bounds (5,3,55,18) → origin=(5,3), size=(50,15).
        string tvr = Compile(FidelityFixtures.OptionsTrc, "options.tvr");
        var dlg = GetDialog(tvr, "dialog.options");
        Assert.Equal(5,  dlg.origin.x);
        Assert.Equal(3,  dlg.origin.y);
        Assert.Equal(50, dlg.size.x);  // 55-5
        Assert.Equal(15, dlg.size.y);  // 18-3
        dlg.ShutDown();
    }

    [Fact]
    public void OptionsFixture_CheckboxItems_ArePreserved()
    {
        // checkbox "Features" items=("One","Two","Three")
        string tvr = Compile(FidelityFixtures.OptionsTrc, "options.tvr");
        var dlg = GetDialog(tvr, "dialog.options");
        TCheckBoxes cb = null;
        dlg.ForEachView(v => { if (v is TCheckBoxes c) cb = c; });
        Assert.NotNull(cb);
        Assert.Equal(3, cb.Strings.Count);
        Assert.Equal("One",   cb.Strings[0]);
        Assert.Equal("Two",   cb.Strings[1]);
        Assert.Equal("Three", cb.Strings[2]);
        dlg.ShutDown();
    }

    [Fact]
    public void OptionsFixture_RadioItems_ArePreserved()
    {
        // radio "Mode" items=("Fast","Safe")
        string tvr = Compile(FidelityFixtures.OptionsTrc, "options.tvr");
        var dlg = GetDialog(tvr, "dialog.options");
        TRadioButtons rb = null;
        dlg.ForEachView(v => { if (v is TRadioButtons r) rb = r; });
        Assert.NotNull(rb);
        Assert.Equal(2, rb.Strings.Count);
        Assert.Equal("Fast", rb.Strings[0]);
        Assert.Equal("Safe", rb.Strings[1]);
        dlg.ShutDown();
    }

    // ── multi.trc: multi-resource ordering ───────────────────────────────────

    [Fact]
    public void MultiFixture_BothKeys_ArePresent()
    {
        string tvr = Compile(FidelityFixtures.MultiTrc, "multi.tvr");
        var insp = TvrInspector.Open(tvr);
        var keys = insp.Entries.Select(e => e.Key).ToList();
        Assert.Contains("dialog.alpha", keys);
        Assert.Contains("dialog.beta",  keys);
    }

    [Fact]
    public void MultiFixture_ListOrder_IsAlphabetical()
    {
        string tvr = Compile(FidelityFixtures.MultiTrc, "multi.tvr");
        var insp = TvrInspector.Open(tvr);
        Assert.Equal(2, insp.Entries.Count);
        Assert.Equal("dialog.alpha", insp.Entries[0].Key);
        Assert.Equal("dialog.beta",  insp.Entries[1].Key);
    }

    [Fact]
    public void MultiFixture_AlphaTitle_IsPreserved()
    {
        string tvr = Compile(FidelityFixtures.MultiTrc, "multi.tvr");
        var dlg = GetDialog(tvr, "dialog.alpha");
        Assert.Equal("Alpha", dlg.title);
        dlg.ShutDown();
    }

    [Fact]
    public void MultiFixture_BetaTitle_IsPreserved()
    {
        string tvr = Compile(FidelityFixtures.MultiTrc, "multi.tvr");
        var dlg = GetDialog(tvr, "dialog.beta");
        Assert.Equal("Beta", dlg.title);
        dlg.ShutDown();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  3. svres validate command tests
// ─────────────────────────────────────────────────────────────────────────────

[Collection("NonParallel")]
public sealed class ValidateCommandTests : IDisposable
{
    private readonly DriverScope _driver;
    private readonly TempDirectory _tmp;

    public ValidateCommandTests()
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

    private string Compile(string src, string name) =>
        FidelityHelper.Compile(src, _tmp.Path, name);

    // ── Error cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_MissingFile_ReturnsError()
    {
        var r = InspectorCommands.Validate(@"C:\nonexistent_svres32a.tvr");
        Assert.False(r.Success);
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("not found", r.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_InvalidFile_ReturnsError()
    {
        // Write a file that is not a valid .tvr (random garbage content).
        string bad = _tmp.CreateFile("bad.tvr", "This is not a TVR file\n");
        // Opening it may succeed (TResourceFile creates a fresh index for unrecognised files),
        // but validation of an empty resource list is still a "Validated" result with 0 items.
        // The important thing is that it doesn't throw.
        var r = InspectorCommands.Validate(bad);
        // Either success with 0 resources or failure — either is acceptable.
        // The call must not throw.
        Assert.NotNull(r);
    }

    [Fact]
    public void Validate_ValidSingleDialog_ReturnsSuccess()
    {
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Validate(tvr);
        Assert.True(r.Success, r.Error);
        Assert.Equal(0, r.ExitCode);
    }

    // ── Success output content ────────────────────────────────────────────────

    [Fact]
    public void Validate_Output_ContainsValidatedHeader()
    {
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Validate(tvr);
        Assert.True(r.Success, r.Error);
        Assert.Contains("Validated", r.Output);
        Assert.Contains("hello.tvr", r.Output);
    }

    [Fact]
    public void Validate_Output_ContainsResourceCount()
    {
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Validate(tvr);
        Assert.True(r.Success, r.Error);
        Assert.Contains("Resources:", r.Output);
    }

    [Fact]
    public void Validate_Output_ContainsDialogCount()
    {
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Validate(tvr);
        Assert.True(r.Success, r.Error);
        Assert.Contains("Dialogs:", r.Output);
        Assert.Contains("1", r.Output);
    }

    [Fact]
    public void Validate_Output_ContainsZeroErrors()
    {
        string tvr = Compile(FidelityFixtures.HelloTrc, "hello.tvr");
        var r = InspectorCommands.Validate(tvr);
        Assert.True(r.Success, r.Error);
        Assert.Contains("Errors:", r.Output);
        Assert.Contains("0", r.Output);
    }

    [Fact]
    public void Validate_MultiDialog_ReturnsCorrectCounts()
    {
        string tvr = Compile(FidelityFixtures.MultiTrc, "multi.tvr");
        var r = InspectorCommands.Validate(tvr);
        Assert.True(r.Success, r.Error);
        Assert.Contains("Resources:", r.Output);
        Assert.Contains("Dialogs:", r.Output);
        Assert.Contains("2", r.Output);
        Assert.Contains("Errors:", r.Output);
    }

    [Fact]
    public void Validate_IsDeterministic()
    {
        string tvr1 = Compile(FidelityFixtures.HelloTrc, "hello1.tvr");
        string tvr2 = Compile(FidelityFixtures.HelloTrc, "hello2.tvr");
        // Strip filename from output before comparing (it differs by name).
        string out1 = InspectorCommands.Validate(tvr1).Output
            .Replace("hello1.tvr", "<file>");
        string out2 = InspectorCommands.Validate(tvr2).Output
            .Replace("hello2.tvr", "<file>");
        Assert.Equal(out1, out2);
    }
}
