using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

/// <summary>
/// Guards the one invariant <see cref="Cp437GlyphGenerator"/> is supposed to make impossible:
/// glyph data that is not exactly the terminal cell size.
/// <para>
/// Throwing rather than clipping, clamping or scaling is deliberate: any of those would defeat
/// the pixel-exact seams the generator exists to guarantee, and in the GPU atlas a wrong-length
/// copy corrupts neighbouring slots rather than just drawing one glyph badly.
/// </para>
/// </summary>
internal static class GeneratedGlyphValidator
{
    /// <summary>
    /// Validates an <see cref="AlphaBitmap"/> handed to the SDL_Renderer texture-upload path.
    /// </summary>
    public static void EnsureExpectedSize(
        AlphaBitmap bitmap, int expectedWidth, int expectedHeight, char ch)
    {
        if (bitmap.Width == expectedWidth && bitmap.Height == expectedHeight)
            return;

        throw new InvalidOperationException(
            $"Generated glyph '{ch}' (U+{(int)ch:X4}) is {bitmap.Width}x{bitmap.Height}, " +
            $"expected {expectedWidth}x{expectedHeight}.");
    }

    /// <summary>
    /// Validates a flat alpha array handed to the GPU atlas, which copies it row by row into a
    /// shared buffer.
    /// </summary>
    /// <param name="alpha">Alpha mask, expected to be exactly <c>width * height</c> bytes.</param>
    /// <param name="expectedWidth">Terminal cell width in pixels.</param>
    /// <param name="expectedHeight">Terminal cell height in pixels.</param>
    /// <param name="codepoint">Codepoint, for the diagnostic message.</param>
    public static void EnsureExpectedLength(
        byte[] alpha, int expectedWidth, int expectedHeight, uint codepoint)
    {
        int expected = expectedWidth * expectedHeight;
        if (alpha.Length == expected)
            return;

        throw new InvalidOperationException(
            $"Glyph alpha for '{(char)codepoint}' (U+{codepoint:X4}) is {alpha.Length} byte(s), " +
            $"expected {expected} ({expectedWidth}x{expectedHeight}).");
    }
}
