using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class CursorVisibilityTests : IDisposable
{
    private readonly DriverScope _driverScope = new(80, 25);

    public void Dispose() => _driverScope.Dispose();

    [Fact]
    public void ResetCursor_HidesCursor_WhenLocalCursorIsAboveView()
    {
        var v = new TView(new TRect(10, 5, 30, 15));
        v.state |= (ushort)(Views.sfVisible | Views.sfFocused | Views.sfCursorVis);
        v.SetCursor(3, -1);

        Assert.Equal(0, _driverScope.Driver.GetCursorType());
    }

    [Fact]
    public void ResetCursor_ShowsCursor_WhenLocalCursorIsInsideView()
    {
        var v = new TView(new TRect(10, 5, 30, 15));
        v.state |= (ushort)(Views.sfVisible | Views.sfFocused | Views.sfCursorVis);
        v.SetCursor(3, 2);

        Assert.NotEqual(0, _driverScope.Driver.GetCursorType());
    }
}
