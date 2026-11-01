using TSharpVision.Constants;

namespace TSharpVision.Graphics;

/// <summary>
/// The on-screen part of a view, as non-overlapping rectangles in absolute screen cells: the view's bounds clipped
/// by every owner's extent, minus every visible sibling above it (and that sibling's shadow) at each level of the
/// owner chain. Empty when the view or an owner is hidden, or the chain does not end at an application.
/// Reads only public view state, so it needs no core change.
/// </summary>
internal static class VisibleRegion
{
    // Corrupt sibling lists must not hang the UI thread.
    private const int MaxSiblings = 1 << 16;

    internal static void Compute(TView view, List<TRect> rects, List<TRect> scratch)
    {
        rects.Clear();
        if ((view.state & Views.sfVisible) == 0 || view.size.x <= 0 || view.size.y <= 0) return;

        // Screen origin of the view's owner, accumulated up the chain (MakeGlobal would do the same walk).
        int parentX = 0, parentY = 0;
        for (TGroup? g = view.owner; g != null; g = g.owner) { parentX += g.origin.x; parentY += g.origin.y; }
        int x = parentX + view.origin.x, y = parentY + view.origin.y;
        rects.Add(new TRect(x, y, x + view.size.x, y + view.size.y));

        TView current = view;
        while (current.owner is TGroup group)
        {
            if ((group.state & Views.sfVisible) == 0) { rects.Clear(); return; }

            // The group's own extent, in screen cells (the persistent clip; TGroup.clip narrows during redraws).
            ClipTo(rects, parentX, parentY, parentX + group.size.x, parentY + group.size.y);
            if (rects.Count == 0) return;

            // Siblings in front of current: the circular list runs from last.Next (front) towards current.
            TView? sibling = group.last?.Next;
            for (int guard = 0; sibling != null && sibling != current && guard < MaxSiblings; guard++)
            {
                if ((sibling.state & Views.sfVisible) != 0)
                {
                    int sx = parentX + sibling.origin.x, sy = parentY + sibling.origin.y;
                    Subtract(rects, scratch, sx, sy, sx + sibling.size.x, sy + sibling.size.y);
                    if ((sibling.state & Views.sfShadow) != 0)
                    {
                        // The shadow is the bounds shifted by shadowSize, minus the bounds already removed.
                        TPoint s = TView.shadowSize;
                        Subtract(rects, scratch, sx + s.x, sy + s.y, sx + sibling.size.x + s.x, sy + sibling.size.y + s.y);
                    }
                    if (rects.Count == 0) return;
                }
                sibling = sibling.Next;
            }

            parentX -= group.origin.x;
            parentY -= group.origin.y;
            current = group;
        }

        // Only a view tree rooted at an application is on screen; a detached window is not.
        if (current is not TProgram) rects.Clear();
    }

    private static void ClipTo(List<TRect> rects, int x1, int y1, int x2, int y2)
    {
        for (int i = rects.Count - 1; i >= 0; i--)
        {
            TRect r = rects[i];
            int cx1 = Math.Max(r.a.x, x1), cy1 = Math.Max(r.a.y, y1);
            int cx2 = Math.Min(r.b.x, x2), cy2 = Math.Min(r.b.y, y2);
            if (cx1 < cx2 && cy1 < cy2) rects[i] = new TRect(cx1, cy1, cx2, cy2);
            else rects.RemoveAt(i);
        }
    }

    private static void Subtract(List<TRect> rects, List<TRect> scratch, int x1, int y1, int x2, int y2)
    {
        scratch.Clear();
        foreach (TRect r in rects)
        {
            int ix1 = Math.Max(r.a.x, x1), iy1 = Math.Max(r.a.y, y1);
            int ix2 = Math.Min(r.b.x, x2), iy2 = Math.Min(r.b.y, y2);
            if (ix1 >= ix2 || iy1 >= iy2) { scratch.Add(r); continue; }
            if (r.a.y < iy1) scratch.Add(new TRect(r.a.x, r.a.y, r.b.x, iy1)); // above
            if (iy2 < r.b.y) scratch.Add(new TRect(r.a.x, iy2, r.b.x, r.b.y)); // below
            if (r.a.x < ix1) scratch.Add(new TRect(r.a.x, iy1, ix1, iy2));     // left
            if (ix2 < r.b.x) scratch.Add(new TRect(ix2, iy1, r.b.x, iy2));     // right
        }
        rects.Clear();
        rects.AddRange(scratch);
    }
}
