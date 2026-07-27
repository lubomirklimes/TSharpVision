using TSharpVision.Constants;
namespace TSharpVision.Samples.TVDemo;

public partial class TVDemoApp
{
    private void OpenTetris()
    {
        if (DeskTop == null) return;

        // Window is 36 wide × 22 tall → inner board area 34 × 20 (10×20 Tetris board)
        int winW = 36, winH = 22;
        int x = (DeskTop.size.x - winW) / 2;
        int y = Math.Max(0, (DeskTop.size.y - winH) / 2);
        var r = new TRect(x, y, x + winW, y + winH);

        var win = new TTetrisWindow(r);
        if (ValidView(win) != null)
            DeskTop.Insert(win);
    }
}

