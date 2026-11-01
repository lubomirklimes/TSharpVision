using TSharpVision.Constants;

namespace TSharpVision.Graphics;

/// <summary>
/// A <see cref="TWindow"/> holding one <see cref="GView"/> over its whole client area, exposed as <see cref="View"/>.
/// Graphics objects are added with <see cref="Insert(GObject)"/>.
/// </summary>
public class GWindow : TWindow
{
    /// <summary>The client-area graphics view.</summary>
    public GView View { get; }

    /// <summary>Creates the window and its graphics view.</summary>
    public GWindow(TRect bounds, string title, ushort number)
        : base(bounds, title, number)
    {
        TRect r = GetExtent();
        r.Grow(-1, -1);
        View = new GView(r)
        {
            // Track the window's right/bottom edges so graphics stay inside on resize.
            growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY),
        };
        Insert(View);
    }

    /// <summary>Adds an object to the view's display list.</summary>
    public void Insert(GObject obj) => View.Insert(obj);

    /// <summary>Removes an object from the view's display list.</summary>
    public void Delete(GObject obj) => View.Delete(obj);
}
