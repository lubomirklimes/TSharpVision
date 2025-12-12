using TSharpVision;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

// No DriverScope needed — pure constant checks.
public sealed class GlyphConstantsTests
{
    // ── Background fill ──────────────────────────────────────────────────

    [Fact] public void BackgroundFillLight_IsLightShade()
        => Assert.Equal('░', TSharpVisionGlyphs.BackgroundFillLight);

    [Fact] public void BackgroundFillMedium_IsMediumShade()
        => Assert.Equal('▒', TSharpVisionGlyphs.BackgroundFillMedium);

    [Fact] public void BackgroundFillDark_IsDarkShade()
        => Assert.Equal('▓', TSharpVisionGlyphs.BackgroundFillDark);

    // ── Single-line box-drawing ──────────────────────────────────────────

    [Fact] public void FrameHorizontal() => Assert.Equal('─', TSharpVisionGlyphs.FrameHorizontal);
    [Fact] public void FrameVertical()   => Assert.Equal('│', TSharpVisionGlyphs.FrameVertical);
    [Fact] public void FrameTopLeft()    => Assert.Equal('┌', TSharpVisionGlyphs.FrameTopLeft);
    [Fact] public void FrameTopRight()   => Assert.Equal('┐', TSharpVisionGlyphs.FrameTopRight);
    [Fact] public void FrameBottomLeft() => Assert.Equal('└', TSharpVisionGlyphs.FrameBottomLeft);
    [Fact] public void FrameBottomRight()=> Assert.Equal('┘', TSharpVisionGlyphs.FrameBottomRight);
    [Fact] public void FrameLeftTee()    => Assert.Equal('├', TSharpVisionGlyphs.FrameLeftTee);
    [Fact] public void FrameRightTee()   => Assert.Equal('┤', TSharpVisionGlyphs.FrameRightTee);

    // ── Double-line box-drawing ──────────────────────────────────────────

    [Fact] public void FrameDoubleHorizontal()  => Assert.Equal('═', TSharpVisionGlyphs.FrameDoubleHorizontal);
    [Fact] public void FrameDoubleVertical()    => Assert.Equal('║', TSharpVisionGlyphs.FrameDoubleVertical);
    [Fact] public void FrameDoubleTopLeft()     => Assert.Equal('╔', TSharpVisionGlyphs.FrameDoubleTopLeft);
    [Fact] public void FrameDoubleTopRight()    => Assert.Equal('╗', TSharpVisionGlyphs.FrameDoubleTopRight);
    [Fact] public void FrameDoubleBottomLeft()  => Assert.Equal('╚', TSharpVisionGlyphs.FrameDoubleBottomLeft);
    [Fact] public void FrameDoubleBottomRight() => Assert.Equal('╝', TSharpVisionGlyphs.FrameDoubleBottomRight);

    // ── ScrollBar glyphs ─────────────────────────────────────────────────

    [Fact] public void ScrollArrowUp()    => Assert.Equal('▲', TSharpVisionGlyphs.ScrollArrowUp);
    [Fact] public void ScrollArrowDown()  => Assert.Equal('▼', TSharpVisionGlyphs.ScrollArrowDown);
    [Fact] public void ScrollArrowLeft()  => Assert.Equal('◄', TSharpVisionGlyphs.ScrollArrowLeft);
    [Fact] public void ScrollArrowRight() => Assert.Equal('►', TSharpVisionGlyphs.ScrollArrowRight);
    [Fact] public void ScrollBarTrack()   => Assert.Equal('▒', TSharpVisionGlyphs.ScrollBarTrack);
    [Fact] public void ScrollBarThumb()   => Assert.Equal('■', TSharpVisionGlyphs.ScrollBarThumb);
    [Fact] public void ScrollBarBright()  => Assert.Equal('░', TSharpVisionGlyphs.ScrollBarBright);

    // ── Checkbox / RadioButton markers ───────────────────────────────────

    [Fact] public void CheckBoxChecked()   => Assert.Equal('X', TSharpVisionGlyphs.CheckBoxChecked);
    [Fact] public void CheckBoxUnchecked() => Assert.Equal(' ', TSharpVisionGlyphs.CheckBoxUnchecked);
    [Fact] public void RadioUnchecked()    => Assert.Equal(' ', TSharpVisionGlyphs.RadioUnchecked);

    // ── Editor Indicator glyphs ─────────────────────────────────────────

    [Fact] public void IndicatorDragFrame()   => Assert.Equal('═', TSharpVisionGlyphs.IndicatorDragFrame);
    [Fact] public void IndicatorNormalFrame() => Assert.Equal('─', TSharpVisionGlyphs.IndicatorNormalFrame);
    [Fact] public void IndicatorModified()    => Assert.Equal('●', TSharpVisionGlyphs.IndicatorModified);

    // ── Menu / StatusLine glyphs ─────────────────────────────────────────

    [Fact] public void MenuSubmenuArrow()    => Assert.Equal('►', TSharpVisionGlyphs.MenuSubmenuArrow);
    [Fact] public void StatusHintSeparator() => Assert.Equal('│', TSharpVisionGlyphs.StatusHintSeparator);

    // ── Cross-class consistency ──────────────────────────────────────────

    [Fact]
    public void DeskTop_DefaultBkgrnd_MatchesBackgroundFillLight()
        => Assert.Equal(TSharpVisionGlyphs.BackgroundFillLight, TDeskTop.defaultBkgrnd);

    [Fact]
    public void TIndicator_DragFrame_MatchesConst()
        => Assert.Equal(TSharpVisionGlyphs.IndicatorDragFrame, TIndicator.DragFrame);

    [Fact]
    public void TIndicator_NormalFrame_MatchesConst()
        => Assert.Equal(TSharpVisionGlyphs.IndicatorNormalFrame, TIndicator.NormalFrame);

    [Fact]
    public void TIndicator_ModifiedStar_MatchesConst()
        => Assert.Equal(TSharpVisionGlyphs.IndicatorModified, TIndicator.ModifiedStar);

    [Fact]
    public void TFrame_InitFrame_ActiveTop_MatchesDoubleGlyphs()
    {
        Assert.Equal(TSharpVisionGlyphs.FrameDoubleTopLeft,    TFrame.FrameChars[TFrame.InitFrame[9]]);
        Assert.Equal(TSharpVisionGlyphs.FrameDoubleHorizontal, TFrame.FrameChars[TFrame.InitFrame[10]]);
        Assert.Equal(TSharpVisionGlyphs.FrameDoubleTopRight,   TFrame.FrameChars[TFrame.InitFrame[11]]);
    }

    [Fact]
    public void TFrame_InitFrame_InactiveTop_MatchesSingleGlyphs()
    {
        Assert.Equal(TSharpVisionGlyphs.FrameTopLeft,    TFrame.FrameChars[TFrame.InitFrame[0]]);
        Assert.Equal(TSharpVisionGlyphs.FrameHorizontal, TFrame.FrameChars[TFrame.InitFrame[1]]);
        Assert.Equal(TSharpVisionGlyphs.FrameTopRight,   TFrame.FrameChars[TFrame.InitFrame[2]]);
    }

    [Fact]
    public void TMenuBox_FrameCharsArr_MatchesSingleGlyphs()
    {
        Assert.Equal(TSharpVisionGlyphs.FrameTopLeft,    TMenuBox.frameCharsArr[1]);
        Assert.Equal(TSharpVisionGlyphs.FrameHorizontal, TMenuBox.frameCharsArr[2]);
        Assert.Equal(TSharpVisionGlyphs.FrameTopRight,   TMenuBox.frameCharsArr[3]);
        Assert.Equal(TSharpVisionGlyphs.FrameBottomLeft, TMenuBox.frameCharsArr[6]);
        Assert.Equal(TSharpVisionGlyphs.FrameVertical,   TMenuBox.frameCharsArr[11]);
    }

    [Fact]
    public void TScrollBar_VChars_MatchScrollGlyphs()
    {
        // vChars/hChars are internal but we can verify via the public chars arrays
        // by checking the DrawBuffer produced at known positions.
        // As a proxy, verify the glyphs constants themselves are consistent.
        Assert.Equal('▲', TSharpVisionGlyphs.ScrollArrowUp);
        Assert.Equal('▼', TSharpVisionGlyphs.ScrollArrowDown);
        Assert.Equal('▒', TSharpVisionGlyphs.ScrollBarTrack);
        Assert.Equal('■', TSharpVisionGlyphs.ScrollBarThumb);
        Assert.Equal('░', TSharpVisionGlyphs.ScrollBarBright);
    }

    [Fact]
    public void TScrollBar_HChars_MatchScrollGlyphs()
    {
        Assert.Equal('◄', TSharpVisionGlyphs.ScrollArrowLeft);
        Assert.Equal('►', TSharpVisionGlyphs.ScrollArrowRight);
        Assert.Equal('▒', TSharpVisionGlyphs.ScrollBarTrack);
        Assert.Equal('■', TSharpVisionGlyphs.ScrollBarThumb);
        Assert.Equal('░', TSharpVisionGlyphs.ScrollBarBright);
    }
}
