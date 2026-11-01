namespace TSharpVision.Drivers;

/// <summary>
/// The point where pixel-graphics layers are brought in line with the view tree before a frame is presented.
/// </summary>
/// <remarks>
/// A view opened, moved, hidden or closed over a graphics view does not redraw it, so the graphics view's clip region
/// must be recomputed before the driver presents the next frame. <see cref="TProgram.GetEvent"/> runs every
/// registered synchronizer, on the UI thread, immediately before it lets the driver present
/// (<see cref="IDriver.PumpMessages"/>), and passes itself: the <b>presenting program</b>. <see cref="TScreen.GetEvent"/>,
/// which presents again later in the same pass (after posted events ran), passes the same program. This covers the
/// application loop, modal <see cref="TGroup.ExecView"/> loops and menus. A synchronizer must only touch views of the
/// program it is given, so programs that coexist in one process never draw into each other's driver. With nothing
/// registered the cost is one field read; a <see cref="TScreen.GetEvent"/> call outside any program loop runs nothing.
/// <para>A synchronizer must only bring layers up to date (read view state, call
/// <see cref="IPixelGraphicsDriver.SetGraphicsLayer"/>/<see cref="IPixelGraphicsDriver.RemoveGraphicsLayer"/>); it must
/// not run event loops or draw views. Exceptions propagate like exceptions from <c>HandleEvent</c>. A synchronizer that
/// registers or unregisters synchronizers takes effect from the next present.</para>
/// </remarks>
public static class PixelGraphicsSynchronization
{
    private static Action<TProgram>[] s_synchronizers = [];

    // The program of the last TProgram.GetEvent present, for the second present in TScreen.GetEvent. Weak, so a
    // finished program is not kept alive by it.
    private static readonly WeakReference<TProgram?> s_presenting = new(null);

    /// <summary>Adds a synchronizer; adding one already registered does nothing. UI thread.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="synchronize"/> is null.</exception>
    public static void Register(Action<TProgram> synchronize)
    {
        ArgumentNullException.ThrowIfNull(synchronize);
        Action<TProgram>[] current = s_synchronizers;
        if (Array.IndexOf(current, synchronize) >= 0) return;
        s_synchronizers = [.. current, synchronize];
    }

    /// <summary>Removes a synchronizer; does nothing when it is not registered. UI thread.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="synchronize"/> is null.</exception>
    public static void Unregister(Action<TProgram> synchronize)
    {
        ArgumentNullException.ThrowIfNull(synchronize);
        Action<TProgram>[] current = s_synchronizers;
        int i = Array.IndexOf(current, synchronize);
        if (i < 0) return;
        s_synchronizers = [.. current[..i], .. current[(i + 1)..]];
    }

    // Diagnostics and tests.
    internal static int Count => s_synchronizers.Length;

    // TProgram.GetEvent, right before its present. The array is replaced, never mutated, so changes made by a
    // synchronizer do not disturb this pass.
    internal static void BeforePresent(TProgram program)
    {
        s_presenting.SetTarget(program);
        Action<TProgram>[] current = s_synchronizers;
        for (int i = 0; i < current.Length; i++) current[i](program);
    }

    // TScreen.GetEvent, right before its present: same pass as the last program present, if that program still exists.
    internal static void BeforePresent()
    {
        Action<TProgram>[] current = s_synchronizers;
        if (current.Length == 0 || !s_presenting.TryGetTarget(out TProgram? program) || program == null) return;
        for (int i = 0; i < current.Length; i++) current[i](program);
    }
}
