// All tests touch global Pstream registry → NonParallel collection.
using System.IO;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Streams;

[Collection("NonParallel")]
public sealed class StreamableRegistrationTests : IDisposable
{
    private readonly DriverScope _driver;

    public StreamableRegistrationTests()
    {
        _driver = new DriverScope();
    }

    public void Dispose()
    {
        _driver.Dispose();
        // Leave registry in a clean registered state for subsequent test classes.
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }

    // ── 23s.1 — Registry populated by RegisterAll() ───────────────────────

    [Fact]
    public void RegisterAll_EmptiesBeforePopulating()
    {
        Pstream.DeInitTypes();
        int before = Pstream.types.Count;
        StreamableRegistration.RegisterAll();
        int after = Pstream.types.Count;

        Assert.Equal(0, before);
        Assert.True(after >= 40, $"Expected >=40 types, got {after}");
    }

    [Fact]
    public void RegisterAll_CoreTypesPresent()
    {
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        Assert.NotNull(Pstream.types.Lookup("TDialog"));
        Assert.NotNull(Pstream.types.Lookup("TStatusLine"));
        Assert.NotNull(Pstream.types.Lookup("TFilterValidator"));
    }

    // ── 23s.2 — TDialog byte roundtrip ────────────────────────────────────

    [Fact]
    public void TDialog_StreamRoundTrip()
    {
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var ms = new MemoryStream();
        var dlg = new TDialog(new TRect(5, 5, 45, 15), "23s Dialog");
        var btn = new TButton(new TRect(1, 7, 11, 9), "~O~K", Views.cmOK,
            ButtonConstants.bfDefault);
        dlg.Insert(btn);

        var os = new Opstream(ms);
        os.WriteObject(dlg);
        os.Flush();

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        ms.Position = 0;
        var dlgBack = (TDialog)TDialog.Build();
        var ip = new Ipstream(ms);
        ip.ReadObject(dlgBack);

        int childCount = 0;
        TButton btnBack = null;
        dlgBack.ForEachView(v => { childCount++; if (v is TButton b) btnBack = b; });

        Assert.Equal("23s Dialog", dlgBack.title);
        Assert.Equal(2, childCount); // TFrame + TButton
        Assert.NotNull(btnBack);
        Assert.Equal("~O~K", btnBack.Title);

        dlg.ShutDown();
        dlgBack.ShutDown();
    }

    // ── 23s.3 — TInputLine + TFilterValidator pointer roundtrip ──────────

    [Fact]
    public void TInputLine_FilterValidator_PointerRoundTrip()
    {
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        var ms = new MemoryStream();
        var dlg = new TDialog(new TRect(0, 0, 50, 10), "ValidatorTest");
        var inp = new TInputLine(new TRect(2, 3, 28, 4), 80);
        inp.Validator = new TFilterValidator("0123456789");
        dlg.Insert(inp);

        var os = new Opstream(ms);
        os.WriteObject(dlg);
        os.Flush();

        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        ms.Position = 0;
        var dlgBack = (TDialog)TDialog.Build();
        var ip = new Ipstream(ms);
        ip.ReadObject(dlgBack);

        TInputLine inpBack = null;
        dlgBack.ForEachView(v => { if (v is TInputLine il) inpBack = il; });

        Assert.NotNull(inpBack);
        var fvBack = Assert.IsType<TFilterValidator>(inpBack.Validator);
        Assert.True(fvBack.IsValid("1234"));
        Assert.False(fvBack.IsValid("abc"));

        dlg.ShutDown();
        dlgBack.ShutDown();
    }

    // ── 23s.4 — TResourceFile Put/Get of TStatusLine ─────────────────────

    [Fact]
    public void TResourceFile_PutGet_TStatusLine()
    {
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();

        string path = Path.Combine(Path.GetTempPath(),
            "sv-23s4-" + Guid.NewGuid().ToString("N") + ".tvr");
        try
        {
            var items = new TStatusItem("~F10~ Menu", Keys.kbF10, Views.cmMenu, null);
            var defs  = new TStatusDef(0, 0xFFFF, items, null);
            var sl    = new TStatusLine(new TRect(0, 24, 80, 25), defs);

            var fp = new Fpstream(path);
            var rf = new TResourceFile(fp);
            rf.Put(sl, "statusline");
            rf.Flush();
            fp.Close();

            Pstream.DeInitTypes();
            StreamableRegistration.RegisterAll();

            var fpR = new Fpstream(path);
            var rfR = new TResourceFile(fpR);
            Assert.Equal(1, rfR.Count());
            var slBack = Assert.IsType<TStatusLine>(rfR.Get("statusline"));
            Assert.Equal("statusline", rfR.KeyAt(0));
            slBack.ShutDown();
            fpR.Close();

            sl.ShutDown();
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    // ── 23s.5 — RegisterAll() is idempotent ──────────────────────────────

    [Fact]
    public void RegisterAll_IsIdempotent()
    {
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
        int c1 = Pstream.types.Count;
        StreamableRegistration.RegisterAll();
        int c2 = Pstream.types.Count;
        Assert.Equal(c1, c2);
    }
}
