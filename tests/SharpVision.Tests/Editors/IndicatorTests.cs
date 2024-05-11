using SharpVision;
using SharpVision.Constants;
using Xunit;

namespace SharpVision.Tests.Editors;

// ── file-local helper — mirrors Demo's IndicatorProbe ────────────────────────
file sealed class IndicatorProbe : TIndicator
{
    public int DrawCount;
    public char[] LastChars = System.Array.Empty<char>();

    public IndicatorProbe(TRect bounds) : base(bounds) { }

    public override void DrawView() { DrawCount++; Draw(); }

    public override void WriteLine(int x, int y, int w, int h, TDrawBuffer b)
    {
        var tmp = new char[b.Length];
        for (int i = 0; i < b.Length; i++)
            tmp[i] = b.Data[i].Character;
        LastChars = tmp;
    }

    public void RunDraw() => Draw();
    public char LastChar(int col) => LastChars[col];
}

// ── tests ─────────────────────────────────────────────────────────────────────
public sealed class IndicatorTests
{
    // ── constants ────────────────────────────────────────────────────────────

    [Fact] public void DragFrame_IsHorizontalDoubleBar() =>
        Assert.Equal('═', TIndicator.DragFrame);

    [Fact] public void NormalFrame_IsHorizontalSingleBar() =>
        Assert.Equal('─', TIndicator.NormalFrame);

    [Fact] public void ModifiedStar_IsPrintable() =>
        Assert.True(TIndicator.ModifiedStar >= ' ');

    // ── constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Ctor_GrowMode_LoYHiY()
    {
        var ind = new TIndicator(new TRect(0, 23, 9, 24));
        Assert.Equal((byte)(Views.gfGrowLoY | Views.gfGrowHiY), ind.growMode);
    }

    [Fact]
    public void Ctor_DefaultLocation_One_One()
    {
        var ind = new TIndicator(new TRect(0, 23, 9, 24));
        Assert.Equal(1, ind.location.x);
        Assert.Equal(1, ind.location.y);
    }

    [Fact]
    public void Ctor_NotModified() =>
        Assert.False(new TIndicator(new TRect(0, 0, 9, 1)).modified);

    // ── GetPalette ───────────────────────────────────────────────────────────

    [Fact]
    public void Palette_Size_Two()
    {
        var pal = new TIndicator(new TRect(0, 0, 9, 1)).GetPalette();
        Assert.Equal(2, pal.Size);
    }

    [Fact]
    public void Palette_Entry1_Is_0x02()
    {
        var pal = new TIndicator(new TRect(0, 0, 9, 1)).GetPalette();
        Assert.Equal(0x02, pal[1]);
    }

    [Fact]
    public void Palette_Entry2_Is_0x03()
    {
        var pal = new TIndicator(new TRect(0, 0, 9, 1)).GetPalette();
        Assert.Equal(0x03, pal[2]);
    }

    // ── SetValue change detection ─────────────────────────────────────────────

    [Fact]
    public void SetValue_NoChange_SkipsRedraw()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 9, 1));
        probe.DrawCount = 0;
        probe.SetValue(new TPoint(1, 1), false);    // same as default
        Assert.Equal(0, probe.DrawCount);
    }

    [Fact]
    public void SetValue_LocationChange_TriggersDrawView()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 9, 1));
        probe.DrawCount = 0;
        probe.SetValue(new TPoint(3, 5), false);
        Assert.Equal(1, probe.DrawCount);
    }

    [Fact]
    public void SetValue_UpdatesLocation()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 9, 1));
        probe.SetValue(new TPoint(3, 5), false);
        Assert.Equal(3, probe.location.x);
        Assert.Equal(5, probe.location.y);
    }

    [Fact]
    public void SetValue_ModifiedChange_TriggersDrawView()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 9, 1));
        probe.SetValue(new TPoint(3, 5), false);    // now at (3,5)
        probe.DrawCount = 0;
        probe.SetValue(new TPoint(3, 5), true);     // only modified changed
        Assert.Equal(1, probe.DrawCount);
    }

    [Fact]
    public void SetValue_SetsModifiedFlag()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 9, 1));
        probe.SetValue(new TPoint(3, 5), true);
        Assert.True(probe.modified);
    }

    // ── SetState(sfDragging) ─────────────────────────────────────────────────

    [Fact]
    public void SetState_Dragging_SetsStateFlag()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 9, 1));
        probe.SetState(Views.sfDragging, true);
        Assert.NotEqual(0, probe.state & Views.sfDragging);
    }

    [Fact]
    public void SetState_Dragging_CallsDrawView()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 9, 1));
        probe.DrawCount = 0;
        probe.SetState(Views.sfDragging, true);
        Assert.Equal(1, probe.DrawCount);
    }

    [Fact]
    public void SetState_Active_DoesNotForceDrawView()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 9, 1));
        probe.DrawCount = 0;
        probe.SetState(Views.sfActive, true);
        Assert.Equal(0, probe.DrawCount);
    }

    // ── Draw output (location = 0,0 → "1:1") ─────────────────────────────────

    [Fact]
    public void Draw_ColonAlignedAtColumn8()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 16, 1));
        probe.SetValue(new TPoint(0, 0), false);    // (1:1)
        probe.RunDraw();
        Assert.Equal(':', probe.LastChar(8));
    }

    [Fact]
    public void Draw_FrameChar_AtColumn0_WhenNotDragging()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 16, 1));
        probe.SetValue(new TPoint(0, 0), false);
        probe.RunDraw();
        Assert.Equal(TIndicator.DragFrame, probe.LastChar(0));
    }

    [Fact]
    public void Draw_RowDigit_AtColumn7()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 16, 1));
        probe.SetValue(new TPoint(0, 0), false);
        probe.RunDraw();
        Assert.Equal('1', probe.LastChar(7));    // y+1 = 1
    }

    [Fact]
    public void Draw_ColDigit_AtColumn9()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 16, 1));
        probe.SetValue(new TPoint(0, 0), false);
        probe.RunDraw();
        Assert.Equal('1', probe.LastChar(9));    // x+1 = 1
    }

    [Fact]
    public void Draw_LeadingSpace_AtColumn6()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 16, 1));
        probe.SetValue(new TPoint(0, 0), false);
        probe.RunDraw();
        Assert.Equal(' ', probe.LastChar(6));
    }

    [Fact]
    public void Draw_TrailingSpace_AtColumn10()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 16, 1));
        probe.SetValue(new TPoint(0, 0), false);
        probe.RunDraw();
        Assert.Equal(' ', probe.LastChar(10));
    }

    // ── Draw output (multi-digit: location = 11,41 → "42:12") ────────────────

    [Fact]
    public void Draw_MultiDigit_ColonAtColumn8()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 16, 1));
        probe.SetValue(new TPoint(11, 41), false);  // y+1=42, x+1=12 → " 42:12 "
        probe.RunDraw();
        Assert.Equal(':', probe.LastChar(8));
    }

    [Fact]
    public void Draw_MultiDigit_TensOfRow_AtColumn6()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 16, 1));
        probe.SetValue(new TPoint(11, 41), false);
        probe.RunDraw();
        Assert.Equal('4', probe.LastChar(6));
    }

    [Fact]
    public void Draw_MultiDigit_UnitsOfRow_AtColumn7()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 16, 1));
        probe.SetValue(new TPoint(11, 41), false);
        probe.RunDraw();
        Assert.Equal('2', probe.LastChar(7));
    }

    [Fact]
    public void Draw_MultiDigit_TensOfCol_AtColumn9()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 16, 1));
        probe.SetValue(new TPoint(11, 41), false);
        probe.RunDraw();
        Assert.Equal('1', probe.LastChar(9));
    }

    [Fact]
    public void Draw_MultiDigit_UnitsOfCol_AtColumn10()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 16, 1));
        probe.SetValue(new TPoint(11, 41), false);
        probe.RunDraw();
        Assert.Equal('2', probe.LastChar(10));
    }

    // ── Modified star and sfDragging glyph swap ───────────────────────────────

    [Fact]
    public void Draw_ModifiedFlag_InjectsModifiedStarAtColumn0()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 16, 1));
        probe.SetValue(new TPoint(0, 0), true);
        probe.RunDraw();
        Assert.Equal(TIndicator.ModifiedStar, probe.LastChar(0));
    }

    [Fact]
    public void Draw_SfDragging_NormalFrame_AtColumn1()
    {
        var probe = new IndicatorProbe(new TRect(0, 0, 16, 1));
        probe.SetState(Views.sfDragging, true);
        probe.RunDraw();
        Assert.Equal(TIndicator.NormalFrame, probe.LastChar(1));
    }
}
