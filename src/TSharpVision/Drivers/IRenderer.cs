using TSharpVision;
namespace TSharpVision.Drivers;

public interface IRenderer
{
    void Render(ScreenBuffer screenBuffer, uint regionX, uint regionY, uint regionWidth, uint regionHeight);
}

