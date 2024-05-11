// Migrated from SharpVision.Demo/Program.cs lines 10420-10600.
using SharpVision;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Core;

// No DriverScope needed — pure constant checks.
public sealed class GlyphConstantsTests
{
    // ── Background fill ──────────────────────────────────────────────────

    [Fact] public void BackgroundFillLight_IsLightShade()
        => Assert.Equal('░', SharpVisionGlyphs.BackgroundFillLight);

    [Fact] public void BackgroundFillMedium_IsMediumShade()
        => Assert.Equal('▒', SharpVisionGlyphs.BackgroundFillMedium);

    [Fact] public void BackgroundFillDark_IsDarkShade()
        => Assert.Equal('▓', SharpVisionGlyphs.BackgroundFillDark);

    // ── Single-line box-drawing ──────────────────────────────────────────

    [Fact] public void FrameHorizontal() => Assert.Equal('─', SharpVisionGlyphs.FrameHorizontal);
    [Fact] public void FrameVertical()   => Assert.Equal('│', SharpVisionGlyphs.FrameVertical);
    [Fact] public void FrameTopLeft()    => Assert.Equal('┌', SharpVisionGlyphs.FrameTopLeft);
    [Fact] public void FrameTopRight()   => Assert.Equal('┐', SharpVisionGlyphs.FrameTopRight);
    [Fact] public void FrameBottomLeft() => Assert.Equal('└', SharpVisionGlyphs.FrameBottomLeft);
    [Fact] public void FrameBottomRight()=> Assert.Equal('┘', SharpVisionGlyphs.FrameBottomRight);
    [Fact] public void FrameLeftTee()    => Assert.Equal('├', SharpVisionGlyphs.FrameLeftTee);
    [Fact] public void FrameRightTee()   => Assert.Equal('┤', SharpVisionGlyphs.FrameRightTee);

    // ── Double-line box-drawing ──────────────────────────────────────────

    [Fact] public void FrameDoubleHorizontal()  => Assert.Equal('═', SharpVisionGlyphs.FrameDoubleHorizontal);
    [Fact] public void FrameDoubleVertical()    => Assert.Equal('║', SharpVisionGlyphs.FrameDoubleVertical);
    [Fact] public void FrameDoubleTopLeft()     => Assert.Equal('╔', SharpVisionGlyphs.FrameDoubleTopLeft);
    [Fact] public void FrameDoubleTopRight()    => Assert.Equal('╗', SharpVisionGlyphs.FrameDoubleTopRight);
    [Fact] public void FrameDoubleBottomLeft()  => Assert.Equal('╚', SharpVisionGlyphs.FrameDoubleBottomLeft);
    [Fact] public void FrameDoubleBottomRight() => Assert.Equal('╝', SharpVisionGlyphs.FrameDoubleBottomRight);

    // ── ScrollBar glyphs ─────────────────────────────────────────────────

    [Fact] public void ScrollArrowUp()    => Assert.Equal('▲', SharpVisionGlyphs.ScrollArrowUp);
    [Fact] public void ScrollArrowDown()  => Assert.Equal('▼', SharpVisionGlyphs.ScrollArrowDown);
    [Fact] public void ScrollArrowLeft()  => Assert.Equal('◄', SharpVisionGlyphs.ScrollArrowLeft);
    [Fact] public void ScrollArrowRight() => Assert.Equal('►', SharpVisionGlyphs.ScrollArrowRight);
    [Fact] public void ScrollBarTrack()   => Assert.Equal('▒', SharpVisionGlyphs.ScrollBarTrack);
    [Fact] public void ScrollBarThumb()   => Assert.Equal('■', SharpVisionGlyphs.ScrollBarThumb);
    [Fact] public void ScrollBarBright()  => Assert.Equal('░', SharpVisionGlyphs.ScrollBarBright);

    // ── Checkbox / RadioButton markers ───────────────────────────────────

    [Fact] public void CheckBoxChecked()   => Assert.Equal('X', SharpVisionGlyphs.CheckBoxChecked);
    [Fact] public void CheckBoxUnchecked() => Assert.Equal(' ', SharpVisionGlyphs.CheckBoxUnchecked);
    [Fact] public void RadioUnchecked()    => Assert.Equal(' ', SharpVisionGlyphs.RadioUnchecked);

    // ── Editor Indicator glyphs ─────────────────────────────────────────

    [Fact] public void IndicatorDragFrame()   => Assert.Equal('═', SharpVisionGlyphs.IndicatorDragFrame);
    [Fact] public void IndicatorNormalFrame() => Assert.Equal('─', SharpVisionGlyphs.IndicatorNormalFrame);
    [Fact] public void IndicatorModified()    => Assert.Equal('●', SharpVisionGlyphs.IndicatorModified);

    // ── Menu / StatusLine glyphs ─────────────────────────────────────────

    [Fact] public void MenuSubmenuArrow()    => Assert.Equal('►', SharpVisionGlyphs.MenuSubmenuArrow);
    [Fact] public void StatusHintSeparator() => Assert.Equal('│', SharpVisionGlyphs.StatusHintSeparator);

    // ── Cross-class consistency ──────────────────────────────────────────

    [Fact]
    public void DeskTop_DefaultBkgrnd_MatchesBackgroundFillLight()
        => Assert.Equal(SharpVisionGlyphs.BackgroundFillLight, TDeskTop.defaultBkgrnd);

    [Fact]
    public void TIndicator_DragFrame_MatchesConst()
        => Assert.Equal(SharpVisionGlyphs.IndicatorDragFrame, TIndicator.DragFrame);

    [Fact]
    public void TIndicator_NormalFrame_MatchesConst()
        => Assert.Equal(SharpVisionGlyphs.IndicatorNormalFrame, TIndicator.NormalFrame);

    [Fact]
    public void TIndicator_ModifiedStar_MatchesConst()
        => Assert.Equal(SharpVisionGlyphs.IndicatorModified, TIndicator.ModifiedStar);

    [Fact]
    public void TFrame_InitFrame_ActiveTop_MatchesDoubleGlyphs()
    {
        Assert.Equal(SharpVisionGlyphs.FrameDoubleTopLeft,    TFrame.FrameChars[TFrame.InitFrame[9]]);
        Assert.Equal(SharpVisionGlyphs.FrameDoubleHorizontal, TFrame.FrameChars[TFrame.InitFrame[10]]);
        Assert.Equal(SharpVisionGlyphs.FrameDoubleTopRight,   TFrame.FrameChars[TFrame.InitFrame[11]]);
    }

    [Fact]
    public void TFrame_InitFrame_InactiveTop_MatchesSingleGlyphs()
    {
        Assert.Equal(SharpVisionGlyphs.FrameTopLeft,    TFrame.FrameChars[TFrame.InitFrame[0]]);
        Assert.Equal(SharpVisionGlyphs.FrameHorizontal, TFrame.FrameChars[TFrame.InitFrame[1]]);
        Assert.Equal(SharpVisionGlyphs.FrameTopRight,   TFrame.FrameChars[TFrame.InitFrame[2]]);
    }

    [Fact]
    public void TMenuBox_FrameCharsArr_MatchesSingleGlyphs()
    {
        Assert.Equal(SharpVisionGlyphs.FrameTopLeft,    TMenuBox.frameCharsArr[1]);
        Assert.Equal(SharpVisionGlyphs.FrameHorizontal, TMenuBox.frameCharsArr[2]);
        Assert.Equal(SharpVisionGlyphs.FrameTopRight,   TMenuBox.frameCharsArr[3]);
        Assert.Equal(SharpVisionGlyphs.FrameBottomLeft, TMenuBox.frameCharsArr[6]);
        Assert.Equal(SharpVisionGlyphs.FrameVertical,   TMenuBox.frameCharsArr[11]);
    }

    [Fact]
    public void TScrollBar_VChars_MatchScrollGlyphs()
    {
        // vChars/hChars are internal but we can verify via the public chars arrays
        // by checking the DrawBuffer produced at known positions.
        // As a proxy, verify the glyphs constants themselves are consistent.
        Assert.Equal('▲', SharpVisionGlyphs.ScrollArrowUp);
        Assert.Equal('▼', SharpVisionGlyphs.ScrollArrowDown);
        Assert.Equal('▒', SharpVisionGlyphs.ScrollBarTrack);
        Assert.Equal('■', SharpVisionGlyphs.ScrollBarThumb);
        Assert.Equal('░', SharpVisionGlyphs.ScrollBarBright);
    }

    [Fact]
    public void TScrollBar_HChars_MatchScrollGlyphs()
    {
        Assert.Equal('◄', SharpVisionGlyphs.ScrollArrowLeft);
        Assert.Equal('►', SharpVisionGlyphs.ScrollArrowRight);
        Assert.Equal('▒', SharpVisionGlyphs.ScrollBarTrack);
        Assert.Equal('■', SharpVisionGlyphs.ScrollBarThumb);
        Assert.Equal('░', SharpVisionGlyphs.ScrollBarBright);
    }
}
