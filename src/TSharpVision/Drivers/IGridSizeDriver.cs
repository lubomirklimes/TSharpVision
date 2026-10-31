namespace TSharpVision.Drivers;

/// <summary>Optional owner-thread character-grid request for windowed backends.</summary>
/// <remarks>Before Initialize, configures the initial grid. After Initialize, changes the actual
/// native window and routes the normal resize notification. The host may reject the request;
/// callers must validate actual geometry. Does not establish drawing or presentation completion.</remarks>
public interface IGridSizeDriver
{
    /// <summary>Requests at least two columns and one row; throws on invalid or rejected geometry.</summary>
    void SetGridSize(ushort columns, ushort rows);
}
