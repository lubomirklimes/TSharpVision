using TSharpVision.Constants;
using TSharpVision;
namespace TSharpVision.Drivers;

// ============================================================================
// TVWrite — low-level screen rendering pipeline.
//
// OVERVIEW
// --------
// C# port of tvision/source/tvwrite.asm.  Copies one draw-buffer row into the
// owner's ScreenBuffer, handling coordinate translation, clipping, z-order
// occlusion, shadow-attribute overlays, and multi-level buffer propagation.
//
// COORDINATE SPACES
// -----------------
// View-local : (0,0) = top-left of this view's drawing area (what Draw() passes).
// Owner      : parent group's space.  L10 adds view.origin to translate.
// Screen     : absolute position in TScreen.ScreenBuffer / root buffer.
//
// SOURCE OFFSET — THE L50 FIX
// ----------------------------
// wOffset tracks the original left edge of the source span in owner space,
// growing by origin.x each L10 call (same as X but without clamping).
// In L50:  srcOffset = Max(0, X - wOffset)
// This equals the number of left-side cells removed by clip.  Without this fix
// (original bug: srcOffset = wOffset), X after left-clip indexed the wrong
// source cell.
//
// DESTINATION INDEX
// -----------------
//   idx = Y * owner.size.x + X
// owner.size.x is the buffer ROW STRIDE (owner's width), not the view width.
// len = Count - X.
//
// Z-ORDER
// -------
// TGroup.last    = BOTTOMMOST view (inserted first; drawn first).
// TGroup.First() = TOPMOST view  (inserted last;  drawn last).
// L20 starts at owner.last and iterates Next until Target is found.  Views
// before Target are above it (can occlude it).
//
// SHADOW (edx FIX)
// ----------------
// sfShadow views cast a shadow of size shadowSize (default 2x1).
// L20 sets edx++ for shadow areas; L50 applies applyShadow() (0x08) instead
// of the source attribute.  edx is decremented after the shadow L30 call.
//
// L40 PASS-THROUGH
// ----------------
// If owner.buffer==null and lockFlag==0, L40 re-enters L10(owner) to bubble
// the write up through buffer-less intermediate groups to the root buffer.
// ============================================================================
public ref struct TVWrite
{
    // Persistent "register" state across the L0→L10→L20→L30/L40→L50 chain.
    private TView Target;   // view currently being rendered
    private int X;          // left edge (inclusive) of remaining range, owner space
    private int Y;          // current row in owner space
    private int Count;      // right edge (exclusive) of remaining range, owner space
    private int wOffset;    // unclipped left edge of source span in owner space;
                            //   srcOffset = Max(0, X - wOffset) in L50
    private int edx;        // 0 = copy normally; >0 = apply shadow attribute
    private bool bufIsShort;
    private ReadOnlySpan<ushort> ShortBuffer;
    private ReadOnlySpan<TScreenChar> CellBuffer;

    // L0 — entry point; x, y, count are in VIEW-LOCAL coordinates.
    // Called once per source row from WriteView (via WriteBuf/WriteLine).
    public void L0(TView dest, int x, int y, int count, ReadOnlySpan<TScreenChar> buffer)
    {
        bufIsShort = false;
        CellBuffer = buffer;

        Target = dest;
        X = x; Y = y;
        // Represent the row as the half-open interval [X, Count).
        // Count = x + count so that each L10 can uniformly add origin.x to
        // both X and Count to translate into the owner's space.
        wOffset = X;       // unclipped left edge; grows in parallel with X
        Count = X + count; // right edge (exclusive) in view-local space
        edx = 0;           // no shadow overlay at the start

        // Vertical clip: reject rows outside this view's extent.
        if (Y < 0 || Y >= dest.size.y) return;

        // Horizontal clip against this view's own width.
        // wOffset is NOT clamped: it must remain the unclipped origin.
        if (X < 0) X = 0;
        if (Count > dest.size.x) Count = dest.size.x;
        if (X < Count)
            L10(dest);
    }

    // L10 — translate from dest's local space into its owner's space, then
    // clip against the owner's clip rectangle.  Mirrors C++ lab10.
    //
    // Each L10 call adds dest.origin to X, Count, wOffset (and Y for rows).
    // After N levels, X,Y are in screen/root-buffer coordinates.
    // wOffset grows by the same origin.x as X (no clamping), so in L50:
    //   srcOffset = Max(0, X - wOffset)  =  number of left-clipped cells.
    private void L10(TView dest)
    {
        Target = dest;   // C++ sets target = _view at lab10
        var owner = dest.owner;
        if ((dest.state & Views.sfVisible) != 0 && owner != null)
        {
            // Translate row and column range into owner (parent) space.
            Y       += dest.origin.y;
            X       += dest.origin.x;
            Count   += dest.origin.x;
            wOffset += dest.origin.x; // sync with X but NOT clamped

            // Clip Y against the owner's clip rectangle.
            if (owner.clip.a.y <= Y && Y < owner.clip.b.y)
            {
                // Clip the column range.  wOffset is intentionally NOT clamped
                // so srcOffset = Max(0, X - wOffset) in L50 remains correct.
                if (X < owner.clip.a.x) X = owner.clip.a.x;
                if (Count > owner.clip.b.x) Count = owner.clip.b.x;

                if (X < Count)
                    L20(owner.last); // start z-order occlusion walk at bottommost view
            }
        }
    }

    // L20 — walk the z-order list looking for views that occlude Target.
    //
    // Called with owner.last (bottommost) and iterates via Next until Target
    // is found.  Views encountered before Target are ABOVE it in z-order.
    // For each sibling `next` that overlaps the current row:
    //   [X, sibling.left)            → rendered via L30 (left unoccluded strip)
    //   [sibling.left, sibling.right) → skipped (occluded by sibling)
    //   [sibling.right, sibling.right+shadowSize.x) → rendered with shadow attr
    //                                   when edx++ is set around the L30 call.
    // Bottom-shadow-only rows are handled by the else-if branch below.
    // When next == Target: call L40 to commit the write.
    private void L20(TView current)
    {
        TView next = current.Next;
        if (next == Target)
        {
            L40(next);
        }
        else
        {
            if ((next.state & Views.sfVisible) != 0 && next.origin.y <= Y)
            {
                int endY = next.origin.y + next.size.y;
                if (Y < endY)
                {
                    int sx = next.origin.x;
                    do
                    {
                        if (X < sx)
                        {
                            if (Count > sx) { L30(next, sx); }
                            else break;
                        }
                        int ex = sx + next.size.x;
                        if (X < ex)
                        {
                            if (Count > ex) X = ex;
                            else return;
                        }
                        if ((next.state & Views.sfShadow) != 0
                            && next.origin.y + next.shadowSize.y <= Y)
                        {
                            // Right-shadow strip: columns [ex, sx2) are in shadow.
                            // Matches upstream lab26: in_shadow++; render; in_shadow--.
                            int sx2 = ex + next.shadowSize.x;
                            if (X < sx2)
                            {
                                edx++;          // enter shadow
                                if (Count > sx2) { L30(next, sx2); edx--; }
                                else break;     // [X,Count) all in shadow; edx stays for L50
                            }
                            // else X >= sx2: past shadow, fall through with edx=0
                        }
                        else break;
                    } while (false);
                }
                else if ((next.state & Views.sfShadow) != 0 && Y < endY + next.shadowSize.y)
                {
                    // Bottom-shadow-only row: columns [sx, exShadow) are in shadow.
                    // Matches upstream lab22/lab26:
                    //   [origin.x, sx)         → rendered normally via L30
                    //   [sx, sx + size.x)      → rendered with shadow attribute
                    int sx = next.origin.x + next.shadowSize.x;
                    int exShadow = sx + next.size.x;

                    // Render [X, sx) normally (the narrow strip left of the shadow).
                    if (X < sx && Count > sx)
                        L30(next, sx);          // after L30: X = sx
                    // (if Count <= sx: [X,Count) is entirely left of shadow; skip shadow)

                    // Process shadow area [sx, exShadow):
                    if (X >= sx && X < exShadow)
                    {
                        edx++;                  // enter shadow
                        if (Count > exShadow) { L30(next, exShadow); edx--; }
                        // else [X,Count) in shadow; edx stays set for L20(next) / L50
                    }
                }
            }
            L20(next);
        }
    }

    // L30 — render the left sub-segment [X, splitPoint) then resume at splitPoint.
    //
    // Mirrors C++ call30(x): narrows Count to splitPoint, re-enters L20 for
    // that strip, then restores all saved registers and sets X = splitPoint
    // so the caller continues with the right-hand sub-segment.
    //
    // splitPoint: right edge of the strip to render now (= left edge of a
    // sibling or shadow boundary that begins the blocked region).
    //
    // The L30 fix: before this fix Count was set to X (= 0) instead of
    // splitPoint, so the inner L20 rendered zero cells and the left strip
    // remained as a black bar (the original L30 bug).
    private void L30(TView view, int splitPoint)
    {
        var saveT     = Target;
        var saveOff   = wOffset;
        var saveEdx   = edx;
        var saveCount = Count;
        var saveY     = Y;
        // X is NOT saved: after the inner L20 it is set to splitPoint (the
        // right edge of the rendered strip) so the caller picks up from there.

        Count = splitPoint;   // render only [X, splitPoint)
        L20(view);            // walk z-order for this sub-strip

        // Restore all registers (Y can change if L10 is re-entered inside).
        Target  = saveT;
        wOffset = saveOff;
        edx     = saveEdx;
        Count   = saveCount;  // restore original right boundary
        Y       = saveY;
        X       = splitPoint; // advance X past the just-rendered strip
    }

    // L40 — commit the write or propagate to the next owner level.
    //
    // If owner has a buffer: write via L50, then propagate up via L10(owner)
    // when not locked so ancestor buffers (up to the root ScreenBuffer) also
    // receive the update.
    //
    // Pass-through (the L40 fix): if owner has no buffer and lockFlag==0,
    // skip L50 and call L10(owner) directly.  This bubbles the write through
    // buffer-less intermediate groups (e.g. TDeskTop) up to the next ancestor
    // that has a buffer.  Without this, any view inside a buffer-less group
    // could never reach TProgram's root ScreenBuffer.
    private void L40(TView view)
    {
        var owner = view.owner;
        if (owner?.buffer != null)
        {
            // owner has a buffer: copy cells into it.
            bool direct = ReferenceEquals(owner.buffer, TScreen.ScreenBuffer);
            if (!direct)
                L50(owner);
            else
            {
                // Writing directly to the hardware screen buffer.
                // TODO: TMouse.Hide() / TMouse.Show() around hardware writes (upstream).
                L50(owner);
            }

            // Propagate: when not locked, update ancestor buffers too.
            if (owner.lockFlag == 0)
                L10(owner);
        }
        else if (owner != null && owner.lockFlag == 0)
        {
            // Pass-through: owner is buffer-less and unlocked — go up one level.
            // Mirrors C++: buffer==null && lockFlag==0 → goto lab10.
            L10(owner);
        }
        // If owner is null or lockFlag != 0, the write is suppressed.
    }

    // L50 — copy cells from the source span into the owner's flat ScreenBuffer.
    //
    // Destination index:  idx = Y * owner.size.x + X
    //   owner.size.x is the buffer ROW STRIDE (owner width), not the view width.
    //   len = Count - X cells are written starting at dst[idx].
    //
    // Source offset (the L50 fix):
    //   srcOffset = Max(0, X - wOffset)
    //   wOffset accumulated the same origin.x additions as X but without
    //   clamping, so X - wOffset equals the number of source cells removed by
    //   left-clip.  Without this correction, left-clipped rows would read from
    //   the wrong position in the source span.
    //
    // Shadow mode (edx > 0): each cell's attribute is replaced by applyShadow()
    // (0x08 = dark gray on black); the character is preserved from the source.
    private void L50(TGroup owner)
    {
        Span<TScreenChar> dst = owner.buffer.Data;

        // Flat destination index: row * stride + column (both in owner space).
        int idx = Y * owner.size.x + X;
        int len = Count - X;  // number of cells to write

        // Safety guard: skip if range falls outside the buffer.
        // Can happen when a partially off-screen view's clip leaves X or Count
        // at a value that would access past the buffer end.
        if (idx < 0 || len <= 0 || idx + len > dst.Length) return;
        if (bufIsShort)
        {
            // Legacy ushort-packed path (low byte = char, high byte = attr).
            int srcOffset = Math.Max(0, X - wOffset);
            if (srcOffset + len > ShortBuffer.Length) return;
            var src = ShortBuffer.Slice(srcOffset, len);
            if (!ReferenceEquals(owner.buffer, TScreen.ScreenBuffer))
                copyShort2Cell(dst.Slice(idx, len), src);
            else
            {
                copyShort(dst.Slice(idx, len), src);
                TScreen.ScreenWrite(X, Y, dst.Slice(idx, len), len);
            }
        }
        else
        {
            // TScreenChar path (primary path for all modern views).
            // srcOffset = number of source cells removed by left-clip in L10.
            int srcOffset = Math.Max(0, X - wOffset);
            if (srcOffset + len > CellBuffer.Length) return;
            var src = CellBuffer.Slice(srcOffset, len);
            if (edx == 0)
            {
                copyCell(dst.Slice(idx, len), src); // normal: copy attributes as-is
            }
            else
            {
                // Shadow mode: replace attribute with 0x08 (dark gray on black).
                // The character comes from the source so the shadow shows the
                // content underneath dimly.
                for (int i = 0; i < len; i++)
                {
                    var cell = src[i];
                    cell.Attr = applyShadow(getAttr(cell));
                    dst[idx + i] = cell;
                }
            }
            // Push to hardware display when writing into the root screen buffer.
            if (ReferenceEquals(owner.buffer, TScreen.ScreenBuffer))
                TScreen.ScreenWrite(X, Y, dst.Slice(idx, len), len);
        }
    }

    // Unpack a ushort-encoded cell and write into TScreenChar (legacy path).
    private void copyShort(Span<TScreenChar> dst, ReadOnlySpan<ushort> src)
    {
        for (int i = 0; i < src.Length; i++)
        {
            ushort w = src[i];
            dst[i].Character = (char)(w & 0xFF);
            dst[i].Attr = (TColorAttr)((w >> 8) & 0xFF);
        }
    }

    // Direct cell-to-cell copy without any attribute transform.
    private void copyCell(Span<TScreenChar> dst, ReadOnlySpan<TScreenChar> src)
        => src.CopyTo(dst);

    // Stub for ushort → TScreenChar copy into non-hardware buffers.
    private void copyShort2Cell(Span<TScreenChar> dst, ReadOnlySpan<ushort> src)
    {
        // TODO: implement once the ushort-packed path is exercised.
    }

    // Shadow attribute: 0x08 = dark gray on black.  Matches upstream tvwrite.asm:
    //   uchar shadowAttr = 0x08; s[1] = shadowAttr;
    // The character is preserved from the source cell; only the colour changes.
    private TColorAttr applyShadow(TColorAttr a) => (TColorAttr)0x08;

    // Extract the raw colour attribute from a TScreenChar (for shadow computation).
    private TColorAttr getAttr(TScreenChar cell) => cell.Attr;
}
