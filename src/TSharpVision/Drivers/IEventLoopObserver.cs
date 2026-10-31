namespace TSharpVision.Drivers;

/// <summary>Optional driver instrumentation at an empty logical event-loop probe.</summary>
/// <remarks>
/// <para><b>When.</b> <see cref="TProgram.GetEvent"/> calls the active driver once per pass that found no
/// input, after <see cref="TProgram.Idle"/> and before the loop's idle wait, and only when the pending
/// slot, the input queue and the posted-event queue are empty. Owning and nested modal loops are observed;
/// a direct <c>GetEvent</c> probe outside any loop, a shut-down program, or a loop whose termination was
/// accepted is not. A custom <c>GetEvent</c> override that bypasses the base method bypasses the observer.
/// This is a logical boundary: not global asynchronous quiescence, renderer completion or presentation.</para>
/// <para><b>Thread and reentrancy.</b> Always the UI thread. The callback is never reentered: a modal loop
/// started from inside it runs unobserved until the callback returns.</para>
/// <para><b>Allowed synchronous work.</b> Short, bounded UI-thread work: reading or copying view and screen
/// state, calling owner-thread native rendering or readback that the backend requires on this thread
/// (one frame), starting asynchronous work, and queueing input with <see cref="TEventQueue.Enqueue"/>,
/// <c>PutEvent</c> or posts. Any queued work or <see cref="TEventQueue.WakeEventLoop"/> call made here
/// prevents the loop from sleeping in this pass.</para>
/// <para><b>Forbidden.</b> Waiting for asynchronous completion — GPU fences, worker or task joins,
/// transport I/O, timers or sleeps. Complete such work off the UI thread instead. Do not retain borrowed
/// screen buffers past the call.</para>
/// <para><b>Failures and shutdown.</b> Exceptions are not caught: they propagate out of <c>GetEvent</c> to
/// the running loop exactly like an exception from <c>HandleEvent</c>, and the next pass may observe again.
/// Implementations that must survive their own failures convert them before returning.</para>
/// </remarks>
public interface IEventLoopObserver
{
    /// <summary>Observes a synchronous, empty-input boundary on the active application.</summary>
    void OnIdleBoundary(TProgram application);
}
