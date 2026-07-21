namespace TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

internal sealed class AlphaBitmap
{
	public int Width { get; }
	public int Height { get; }
	public byte[] Alpha { get; }

	public AlphaBitmap(int width, int height, byte[] alpha)
	{
		if (width <= 0)
			throw new ArgumentOutOfRangeException(nameof(width));

		if (height <= 0)
			throw new ArgumentOutOfRangeException(nameof(height));

		if (alpha.Length != width * height)
			throw new ArgumentException("Alpha buffer size does not match bitmap dimensions.", nameof(alpha));

		Width = width;
		Height = height;
		Alpha = alpha;
	}

	public byte this[int x, int y]
	{
		get
		{
			if ((uint)x >= Width || (uint)y >= Height)
				return 0;

			return Alpha[y * Width + x];
		}
	}

	public bool HasInk(int x, int y, byte threshold)
	{
		return this[x, y] >= threshold;
	}

	public void SetAlpha(int x, int y, byte alpha)
	{
		if ((uint)x >= Width || (uint)y >= Height)
			return;

		Alpha[y * Width + x] = alpha;
	}

	public static AlphaBitmap CreateEmpty(int width, int height)
	{
		if (width <= 0)
			throw new ArgumentOutOfRangeException(nameof(width));

		if (height <= 0)
			throw new ArgumentOutOfRangeException(nameof(height));

		return new AlphaBitmap(width, height, new byte[width * height]);
	}
}
