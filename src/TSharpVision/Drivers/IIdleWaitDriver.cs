namespace TSharpVision.Drivers;

/// <summary>Optional efficient idle waiting capability; input pumping remains nonblocking.</summary>
public interface IIdleWaitDriver
{
    /// <summary>Waits for native input, framework wake, or the next required deadline.</summary>
    /// <param name="timeoutMilliseconds">Maximum wait in milliseconds, or -1 for no application deadline.</param>
    /// <param name="wakeSignal">Non-null framework-owned signal, borrowed for this call. Do not reset, dispose, or retain it after returning.</param>
    /// <remarks>Called only on the UI thread after an Idle pass. Return immediately for pending native or
    /// rendering work. Do not dispatch application callbacks. Native failures must throw rather than spin.
    /// Driver deadlines may shorten the timeout; shutdown must interrupt an active wait.
    /// Returning does not guarantee input is available; the caller pumps and checks again.</remarks>
    void WaitForEvent(int timeoutMilliseconds, WaitHandle wakeSignal);
}
