namespace TSharpVision.Drivers.SDL;

/// <summary>
/// Terminal cell size derivation, shared by the Renderer pipeline, the Gpu pipeline and the
/// headless glyph-diagnostic tool. Depends only on SDL_ttf; needs no window or renderer.
/// <para>
/// <c>cellHeight = TTF_GetFontHeight</c> (falling back to line skip, then 20);
/// <c>cellWidth</c> = the largest advance among <c>M W 0 X</c> (falling back to 60 % of
/// <c>cellHeight</c>).
/// </para>
/// </summary>
internal static class SdlFontMetrics
{
    public static void ComputeMetrics(IntPtr font, out int cellWidth, out int cellHeight)
    {
        int lineSkip = SDL3.TTF.GetFontLineSkip(font);
        int fontH    = SDL3.TTF.GetFontHeight(font);
        cellHeight   = fontH > 0 ? fontH : (lineSkip > 0 ? lineSkip : 20);

        int maxAdv = 0;
        foreach (ushort cp in (ushort[])['M', 'W', '0', 'X'])
        {
            if (SDL3.TTF.GetGlyphMetrics(font, cp,
                    out _, out _, out _, out _, out int adv) && adv > maxAdv)
                maxAdv = adv;
        }
        // Fallback: 60 % of cellHeight (typical monospace aspect ratio).
        cellWidth = maxAdv > 0 ? maxAdv : Math.Max(8, (cellHeight * 6 + 9) / 10);
    }
}
