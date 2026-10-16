using Xunit;

namespace TSharpVision.Tests.Streams;

public sealed class PartialReadTests
{
    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    public void ReadBytesFillsRequestedRangeUntilActualEof(bool offsetOverload, int available)
    {
        using var stream = new FragmentedStream(new byte[] { 1, 2, 3, 4 }[..available]);
        var input = new Ipstream(stream);
        byte[] result = { 9, 9, 9, 9, 9, 9 };
        int offset = offsetOverload ? 1 : 0;
        if (offsetOverload) input.ReadBytes(result, offset, 4);
        else input.ReadBytes(result, 4);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }[..available], result[offset..(offset + available)]);
        Assert.All(result[(offset + available)..(offset + 4)], value => Assert.Equal(0, value));
        Assert.Equal(available < 4, input.Eof() != 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroLengthDoesNotReadOrSetEof(bool offsetOverload)
    {
        using var stream = new FragmentedStream(Array.Empty<byte>());
        var input = new Ipstream(stream);
        byte[] result = { 9 };
        if (offsetOverload) input.ReadBytes(result, 1, 0);
        else input.ReadBytes(result, 0);
        Assert.Equal(9, result[0]);
        Assert.Equal(0, input.Eof());
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count)
            => base.Read(buffer, offset, Math.Min(count, 1));
    }
}
