//             TSortedListBox / TFileInfoPane tests.
// No stream round-trips here; driver scope used defensively for view construction.
using SharpVision;
using SharpVision.Constants;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Files;

// Duplicated from Demo/Program.cs (duplicate-first rule — demo must not change).
// Exposes the text formatter used by TSortedListBox.GetText().
file sealed class MyTextSortedListBox : TSortedListBox
{
    public MyTextSortedListBox(TRect b, ushort n, TScrollBar s) : base(b, n, s) { }
    protected override string GetItemText(object item)
        => item is TSearchRec r ? r.name : (item?.ToString() ?? string.Empty);
}

public sealed class FileCollectionTests : IDisposable
{
    private readonly DriverScope _driver;
    public FileCollectionTests() => _driver = new DriverScope();
    public void Dispose() => _driver.Dispose();

    // ── TSearchRec / FileAttr ─────────────────────────────────────────────

    [Fact]
    public void FileAttr_faDirec_Is0x10()
    {
        Assert.Equal(0x10, FileAttr.faDirec);
    }

    [Fact]
    public void FileAttr_faArch_Is0x20()
    {
        Assert.Equal(0x20, FileAttr.faArch);
    }

    [Fact]
    public void TSearchRec_StoresNameAndSize()
    {
        var rec = new TSearchRec { name = "readme.txt", attr = 0, size = 12 };
        Assert.Equal("readme.txt", rec.name);
        Assert.Equal(12, rec.size);
    }

    // ── TDirEntry ─────────────────────────────────────────────────────────

    [Fact]
    public void TDirEntry_TextDirOffset()
    {
        var e = new TDirEntry("My Docs", "C:\\Users\\me", 3);
        Assert.Equal("My Docs", e.Text());
        Assert.Equal("C:\\Users\\me", e.Dir());
        Assert.Equal(3, e.Offset());
    }

    // ── TDirCollection ────────────────────────────────────────────────────

    [Fact]
    public void TDirCollection_Insert_Count()
    {
        var col = new TDirCollection();
        col.Insert(new TDirEntry("a", "A"));
        col.Insert(new TDirEntry("b", "B"));
        col.Insert(null); // null entries are ignored
        Assert.Equal(2, col.Count);
    }

    [Fact]
    public void TDirCollection_Indexer()
    {
        var col = new TDirCollection();
        col.Insert(new TDirEntry("a", "A"));
        col.Insert(new TDirEntry("b", "B"));
        Assert.Equal("b", col[1].Text());
    }

    // ── TFileCollection comparator ────────────────────────────────────────

    [Fact]
    public void TFileCollection_DirsLast_DirAfterFile()
    {
        var fc   = new TFileCollection();
        var dir  = new TSearchRec { name = "subdir", attr = FileAttr.faDirec };
        var file = new TSearchRec { name = "alpha.txt", attr = 0 };
        Assert.True(fc.Compare(dir, file) > 0);
    }

    [Fact]
    public void TFileCollection_DirsLast_FileBeforeDir()
    {
        var fc   = new TFileCollection();
        var dir  = new TSearchRec { name = "subdir", attr = FileAttr.faDirec };
        var file = new TSearchRec { name = "alpha.txt", attr = 0 };
        Assert.True(fc.Compare(file, dir) < 0);
    }

    [Fact]
    public void TFileCollection_ParentLast_DotDotAfterOtherDirs()
    {
        var fc     = new TFileCollection();
        var dir    = new TSearchRec { name = "subdir", attr = FileAttr.faDirec };
        var parent = new TSearchRec { name = "..", attr = FileAttr.faDirec };
        Assert.True(fc.Compare(parent, dir) > 0);
    }

    [Fact]
    public void TFileCollection_AlphabeticalTieBreak()
    {
        var fc    = new TFileCollection();
        var alpha = new TSearchRec { name = "alpha.txt", attr = 0 };
        var beta  = new TSearchRec { name = "beta.txt",  attr = 0 };
        Assert.True(fc.Compare(alpha, beta) < 0);
    }

    [Fact]
    public void TFileCollection_EqualNames_CompareZero()
    {
        var fc   = new TFileCollection();
        var file = new TSearchRec { name = "alpha.txt", attr = 0 };
        Assert.Equal(0, fc.Compare(file, new TSearchRec { name = "alpha.txt" }));
    }

    [Fact]
    public void TFileCollection_InsertedOrder()
    {
        var fc     = new TFileCollection();
        var file   = new TSearchRec { name = "alpha.txt", attr = 0 };
        var dir    = new TSearchRec { name = "subdir", attr = FileAttr.faDirec };
        var other  = new TSearchRec { name = "beta.txt",  attr = 0 };
        var parent = new TSearchRec { name = "..", attr = FileAttr.faDirec };
        fc.Insert(file); fc.Insert(dir); fc.Insert(other); fc.Insert(parent);

        Assert.Equal("alpha.txt", ((TSearchRec)fc.At(0)).name);
        Assert.Equal("..",        ((TSearchRec)fc.At(fc.Count - 1)).name);
    }

    // ── Alphabetical mode ─────────────────────────────────────────────────

    [Fact]
    public void TFileCollection_AlphabeticalMode_FileBeforeDir()
    {
        uint saved = TFileCollection.SortOptions;
        TFileCollection.SortOptions = FileCollectionOptions.fcolAlphabetical
                                    | FileCollectionOptions.fcolCaseInsensitive;
        try
        {
            var fc = new TFileCollection();
            var d = new TSearchRec { name = "zoo",   attr = FileAttr.faDirec };
            var f = new TSearchRec { name = "alpha",  attr = 0 };
            Assert.True(fc.Compare(f, d) < 0);
        }
        finally { TFileCollection.SortOptions = saved; }
    }

    // ── TSortedCollection.Search ──────────────────────────────────────────

    [Fact]
    public void Search_Hit()
    {
        var fc = new TFileCollection();
        fc.Insert(new TSearchRec { name = "alpha" });
        fc.Insert(new TSearchRec { name = "bravo" });
        fc.Insert(new TSearchRec { name = "charlie" });

        bool hit = fc.Search(new TSearchRec { name = "bravo" }, out int idx);
        Assert.True(hit);
        Assert.Equal("bravo", ((TSearchRec)fc.At(idx)).name);
    }

    [Fact]
    public void Search_Miss_ReturnsInsertionPoint()
    {
        var fc = new TFileCollection();
        fc.Insert(new TSearchRec { name = "alpha" });
        fc.Insert(new TSearchRec { name = "bravo" });
        fc.Insert(new TSearchRec { name = "charlie" });

        bool miss = fc.Search(new TSearchRec { name = "delta" }, out int idx2);
        Assert.False(miss);
        Assert.Equal(fc.Count, idx2);
    }

    // ── TSortedListBox ────────────────────────────────────────────────────

    [Fact]
    public void TSortedListBox_Ctor_SearchPosMaxValue()
    {
        var lb = new TSortedListBox(new TRect(0, 0, 20, 5), 1, null);
        Assert.Equal(0xFFFF, lb.searchPos);
    }

    [Fact]
    public void TSortedListBox_Ctor_SfCursorVisSet()
    {
        var lb = new TSortedListBox(new TRect(0, 0, 20, 5), 1, null);
        Assert.NotEqual(0, lb.state & Views.sfCursorVis);
    }

    [Fact]
    public void TSortedListBox_NewList_SetRange()
    {
        var fc = new TFileCollection();
        fc.Insert(new TSearchRec { name = "alpha.txt" });
        fc.Insert(new TSearchRec { name = "beta.txt" });
        var lb = new MyTextSortedListBox(new TRect(0, 0, 20, 5), 1, null);
        lb.NewList(fc);
        Assert.Equal(2, lb.range);
    }

    [Fact]
    public void TSortedListBox_GetText_ReadsFromCollection()
    {
        var fc = new TFileCollection();
        fc.Insert(new TSearchRec { name = "alpha.txt" });
        fc.Insert(new TSearchRec { name = "beta.txt" });
        var lb = new MyTextSortedListBox(new TRect(0, 0, 20, 5), 1, null);
        lb.NewList(fc);
        Assert.Equal("alpha.txt", lb.GetText(0, 80));
        Assert.Equal("beta.txt",  lb.GetText(1, 80));
    }

    // ── TFileInfoPane ─────────────────────────────────────────────────────

    [Fact]
    public void TFileInfoPane_Ctor_EventMaskHasBroadcast()
    {
        var ip = new TFileInfoPane(new TRect(0, 0, 60, 6));
        Assert.NotEqual(0, ip.eventMask & Events.evBroadcast);
    }

    [Fact]
    public void TFileInfoPane_Ctor_PaletteEntry1Is0x1E()
    {
        var ip = new TFileInfoPane(new TRect(0, 0, 60, 6));
        Assert.Equal(0x1E, ip.GetPalette()[1]);
    }

    [Fact]
    public void TFileInfoPane_Ctor_FileBlockNameEmpty()
    {
        var ip = new TFileInfoPane(new TRect(0, 0, 60, 6));
        Assert.True(string.IsNullOrEmpty(ip.FileBlock.name));
    }

    [Fact]
    public void TFileInfoPane_cmFileFocused_UpdatesFileBlock()
    {
        var ip  = new TFileInfoPane(new TRect(0, 0, 60, 6));
        var rec = new TSearchRec { name = "data.dat", size = 1024, attr = 0 };
        var ev  = new TEvent { What = Events.evBroadcast };
        ev.message.command = Views.cmFileFocused;
        ev.message.infoPtr = rec;
        ip.HandleEvent(ref ev);

        Assert.True(ReferenceEquals(ip.FileBlock, rec));
        Assert.Equal("data.dat", ip.FileBlock.name);
    }
}
