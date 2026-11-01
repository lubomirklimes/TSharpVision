namespace TSharpVision.Drivers;

/// <summary>Rectangle in logical pixels of the cell grid, used for <see cref="IPixelGraphicsDriver"/> clip regions.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="W">Width; zero or negative means empty.</param>
/// <param name="H">Height; zero or negative means empty.</param>
public readonly record struct PixelRect(int X, int Y, int W, int H)
{
    /// <summary>True when the rectangle covers no pixel.</summary>
    public bool IsEmpty => W <= 0 || H <= 0;
}
