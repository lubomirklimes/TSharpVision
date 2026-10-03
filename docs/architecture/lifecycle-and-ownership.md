# Lifecycle and ownership

Managed lifetime and Turbo Vision ownership are separate concerns.

## Remove

`TGroup.Remove` detaches a view, updates group ordering/current state, and clears the ownership relationship. The view remains a live managed object and may be inserted again. Remove is a non-destructive hierarchy operation.

## TWindow.Close

For a non-modal window, `Close` first validates the focus move. On success it sends the `cmClosingWindow` notification through the ownership chain, invokes ownership-tree `ShutDown`, detaches the window, and clears its frame convenience reference. A veto leaves the hierarchy unchanged. Repeated or reentrant completion does not emit another closing notification.

A modal `cmClose` follows modal semantics by ending with cancellation; it does not perform the non-modal detach path at that point. `Close` does not automatically call `Dispose`, and a C# reference to the closed window can remain valid as an object reference.

## ShutDown

`ShutDown` performs framework lifecycle cleanup and detachment through the owned view tree. It is the managed adaptation of Turbo Vision teardown responsibilities, not a claim that C++ deletion occurs.

`TProgram.ExecuteDialog` takes lifecycle ownership of a dialog for one modal use. It calls `ShutDown` when execution ends, including cancellation and exceptions, without calling `Dispose`. When passed a compatible `TDataRecord`, it initializes the dialog from that record and copies values back only after an accepted result; cancellation leaves the caller's record unchanged.

## Dispose

`Dispose` releases managed/unmanaged resources for types that implement `IDisposable`. It is not synonymous with Close or Remove. A caller that owns disposable external resources remains responsible for the appropriate disposal policy after framework detachment.

Program/runtime shutdown also restores the selected host driver, console/terminal state, and registered services where supported.
