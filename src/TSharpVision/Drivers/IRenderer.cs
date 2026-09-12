using TSharpVision;
namespace TSharpVision.Drivers;

/// <summary>Renders regions of a character-cell screen buffer to a display backend.</summary>
public interface IRenderer
{

    /// <summary>Renders the specified region of the screen buffer; the origin and dimensions are measured in character cells.</summary>
    void Render(ScreenBuffer screenBuffer, uint regionX, uint regionY, uint regionWidth, uint regionHeight);
}

