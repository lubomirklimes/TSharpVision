namespace TSharpVision.Drivers.SDL.Graphics;

/// <summary>Half-open rectangle <c>[X0, X1) x [Y0, Y1)</c> in device (drawable) pixels.</summary>
internal readonly record struct DeviceRect(int X0, int Y0, int X1, int Y1)
{
    internal bool IsEmpty => X1 <= X0 || Y1 <= Y0;

    internal DeviceRect Intersect(DeviceRect other) => new(
        Math.Max(X0, other.X0), Math.Max(Y0, other.Y0), Math.Min(X1, other.X1), Math.Min(Y1, other.Y1));
}

/// <summary>
/// Logical-to-device pixel mapping: logical cell <c>LogicalWidth x LogicalHeight</c> covers device cell
/// <c>DeviceWidth x DeviceHeight</c>. A logical coordinate v maps to <c>floor(v * Device / Logical)</c>, so every
/// cell boundary lands exactly on the device cell boundary the text renderer uses.
/// </summary>
internal readonly record struct PixelScale(int LogicalWidth, int LogicalHeight, int DeviceWidth, int DeviceHeight)
{
    internal static PixelScale Identity(int cellWidth, int cellHeight) => new(cellWidth, cellHeight, cellWidth, cellHeight);

    internal int X(int v) => (int)Math.Floor((double)v * DeviceWidth / LogicalWidth);
    internal int Y(int v) => (int)Math.Floor((double)v * DeviceHeight / LogicalHeight);

    /// <summary>Device rectangle covering logical <c>[x0, x1) x [y0, y1)</c>.</summary>
    internal DeviceRect Rect(int x0, int y0, int x1, int y1) => new(X(x0), Y(y0), X(x1), Y(y1));
}

/// <summary>
/// The one translation of <see cref="GraphicsPrimitive"/>s into device rectangles that every SDL backend draws
/// (GPU pass, CPU-composite fallback, SDL renderer), so all of them produce the same pixels. A line is rasterised in
/// logical pixels with the exact integer formula of <see cref="GraphicsPrimitiveKind.Line"/> (the GPU shader
/// evaluates the same formula) and emitted as coalesced axis-aligned runs, one device rectangle per run, each
/// logical pixel covering its scaled device area (thicker lines at higher pixel density, like the text grid).
/// </summary>
/// <remarks>
/// Line pixel set, with |dx| ≥ |dy| (x-major; y-major is the same with the axes swapped): the end points are ordered
/// so that x1 ≤ x2, dx = x2 − x1, dy = y2 − y1 (signed), and every integer x in [x1, x2] gets
/// <c>y = y1 + floorDiv(2(x − x1)dy + dx, 2dx)</c>; dx = 0 is the single pixel (x1, y1). The ordering makes the set
/// independent of the end-point order, both end points are included, and consecutive pixels are 8-connected.
/// Lines with |dx| or |dy| above <see cref="GraphicsPrimitive.MaxLineExtent"/> are skipped; below it every
/// intermediate fits int32.
/// </remarks>
internal static class PixelGraphicsGeometry
{
    /// <summary>Lines whose |dx| or |dy| (logical pixels) exceeds this are skipped as degenerate input.</summary>
    internal const int MaxLineLength = GraphicsPrimitive.MaxLineExtent;

    /// <summary>
    /// Appends the device rectangles covering a <see cref="GraphicsPrimitiveKind.Line"/> or
    /// <see cref="GraphicsPrimitiveKind.FillRect"/> to the caller's (reused) list; returns how many. Images produce
    /// no rectangles (they are drawn by sampling, see <see cref="PixelGraphicsRasterizer"/>). Allocation-free once
    /// the list has grown.
    /// </summary>
    internal static int AppendRects(in GraphicsPrimitive primitive, PixelScale scale, List<DeviceRect> output)
    {
        int before = output.Count;
        switch (primitive.Kind)
        {
            case GraphicsPrimitiveKind.FillRect:
                if (primitive.X2 > primitive.X1 && primitive.Y2 > primitive.Y1)
                {
                    DeviceRect r = scale.Rect(primitive.X1, primitive.Y1, primitive.X2, primitive.Y2);
                    if (!r.IsEmpty) output.Add(r);
                }
                break;
            case GraphicsPrimitiveKind.Line:
                AppendLine(primitive.X1, primitive.Y1, primitive.X2, primitive.Y2, scale, output);
                break;
        }
        return output.Count - before;
    }

    /// <summary>True when the line is within <see cref="GraphicsPrimitive.MaxLineExtent"/> and is drawn.</summary>
    internal static bool IsDrawableLine(int x1, int y1, int x2, int y2) =>
        Math.Abs((long)x2 - x1) <= GraphicsPrimitive.MaxLineExtent && Math.Abs((long)y2 - y1) <= GraphicsPrimitive.MaxLineExtent;

    /// <summary>
    /// The minor coordinate of the line pixel at major coordinate <paramref name="m"/>, for end points already
    /// ordered (<paramref name="m1"/> ≤ m2, <paramref name="majorDelta"/> = m2 − m1 &gt; 0,
    /// <paramref name="minorDelta"/> signed with |minorDelta| ≤ majorDelta): <c>n1 + floorDiv(2(m − m1)dn + dm, 2dm)</c>.
    /// </summary>
    internal static int LineMinor(int m, int m1, int n1, int majorDelta, int minorDelta) =>
        n1 + FloorDiv(2 * (m - m1) * minorDelta + majorDelta, 2 * majorDelta);

    /// <summary>Integer division rounding toward negative infinity (<paramref name="b"/> &gt; 0).</summary>
    internal static int FloorDiv(int a, int b)
    {
        int q = a / b;
        return (a % b != 0 && a < 0) ? q - 1 : q;
    }

    private static void AppendLine(int x1, int y1, int x2, int y2, PixelScale scale, List<DeviceRect> output)
    {
        if (!IsDrawableLine(x1, y1, x2, y2)) return;
        bool xMajor = Math.Abs((long)x2 - x1) >= Math.Abs((long)y2 - y1);
        // Major axis m, minor axis n, ordered so that m1 <= m2.
        int m1 = xMajor ? x1 : y1, n1 = xMajor ? y1 : x1, m2 = xMajor ? x2 : y2, n2 = xMajor ? y2 : x2;
        if (m1 > m2) (m1, n1, m2, n2) = (m2, n2, m1, n1);
        int dm = m2 - m1, dn = n2 - n1;
        if (dm == 0)
        {
            AddRun(m1, m1, n1, xMajor, scale, output);
            return;
        }

        // Walk the major axis; a run ends where the minor coordinate changes. Incremental form of the floorDiv:
        // num = 2(m - m1)dn + dm is split into q * den + r with 0 <= r < den, den = 2dm, stepping num by 2dn.
        int den = 2 * dm, step = 2 * dn;
        int q = 0, r = dm; // m = m1: floorDiv(dm, 2dm) = 0, remainder dm
        int runStart = m1, runMinor = n1;
        for (int i = 1; i <= dm; i++) // counted, so m2 == int.MaxValue cannot overflow the loop
        {
            int m = m1 + i;
            r += step;
            // |step| <= den, so at most one carry or borrow per step.
            if (r >= den) { r -= den; q++; }
            else if (r < 0) { r += den; q--; }
            int minor = n1 + q;
            if (minor != runMinor)
            {
                AddRun(runStart, m - 1, runMinor, xMajor, scale, output);
                runStart = m;
                runMinor = minor;
            }
        }
        AddRun(runStart, m2, runMinor, xMajor, scale, output);
    }

    // A run of logical pixels lo..hi (inclusive) along the major axis at minor coordinate c.
    private static void AddRun(int lo, int hi, int c, bool horizontal, PixelScale scale, List<DeviceRect> output)
    {
        // hi + 1 and c + 1 saturate at int.MaxValue: such pixels are far outside any drawable grid anyway.
        int end = hi == int.MaxValue ? hi : hi + 1, c1 = c == int.MaxValue ? c : c + 1;
        DeviceRect rect = horizontal ? scale.Rect(lo, c, end, c1) : scale.Rect(c, lo, c1, end);
        if (!rect.IsEmpty) output.Add(rect);
    }
}
