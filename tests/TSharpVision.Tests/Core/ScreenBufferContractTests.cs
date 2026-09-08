using Xunit;

namespace TSharpVision.Tests.Core;

public sealed class ScreenBufferContractTests
{
    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(0u, 3u)]
    [InlineData(3u, 0u)]
    [InlineData(1u, 1u)]
    [InlineData(80u, 25u)]
    public void DimensionsAreCounts(uint width, uint height)
    {
        var buffer = new ScreenBuffer(width, height);
        Assert.Equal(width, buffer.Width);
        Assert.Equal(height, buffer.Height);
        Assert.Equal((int)(width * height), buffer.Size);
        Assert.Equal(buffer.Size, buffer.GetSpan().Length);
        Assert.Equal(1, ScreenBuffer.GetSize());
        foreach (var cell in buffer.Data) Assert.Equal(' ', cell.Character);
    }

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, 1u)]
    [InlineData(12, 1u)]
    public void LinearConstructorHasValidDimensions(int size, uint height)
    {
        var buffer = new ScreenBuffer(size);
        Assert.Equal((uint)size, buffer.Width);
        Assert.Equal(height, buffer.Height);
        Assert.Equal(size, buffer.Size);
        foreach (var cell in buffer.Data) Assert.Equal(' ', cell.Character);
    }

    [Fact]
    public void DrawSpanSharesRowMajorCells()
    {
        var buffer = new ScreenBuffer(2, 2);
        var span = buffer.GetSpan();
        span.Data[3].Character = 'X';
        Assert.Equal('X', buffer.GetChar(1, 1).Character);
        buffer.SetChar(0, 1, new TScreenChar('Y', ConsoleColor.White, ConsoleColor.Black));
        Assert.Equal('Y', span.Data[2].Character);
        buffer.Clear();
        Assert.Equal(' ', span.Data[3].Character);
    }

    [Fact]
    public void InvalidDimensionsAndCoordinatesCannotWrapOrAliasAnotherRow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenBuffer(-1));
        Assert.Throws<OverflowException>(() => new ScreenBuffer(uint.MaxValue, 2));
        var buffer = new ScreenBuffer(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetChar(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetChar(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetChar(uint.MaxValue, 0, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenBuffer(0).GetChar(0, 0));
    }
}
