using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

public sealed class ScreenBufferAllocationTests
{
    [Fact]
    public void ScreenBuffer_GetSize_IsManagedCellCount()
    {
        Assert.Equal(1, ScreenBuffer.GetSize());
    }

    [Fact]
    public void Group_GetBuffer_AllocatesOneCellPerScreenPosition()
    {
        var group = new TestGroup(new TRect(0, 0, 4, 3));
        group.state |= Views.sfExposed;

        group.GetBuffer();

        Assert.NotNull(group.buffer);
        Assert.Equal(12, group.buffer.Data.Length);
    }
}
