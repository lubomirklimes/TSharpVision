using SharpVision;
namespace SharpVision.Drivers;

public interface IRenderer
{
    void Render(ScreenBuffer screenBuffer, uint regionX, uint regionY, uint regionWidth, uint regionHeight);
}

