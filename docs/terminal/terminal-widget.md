# Terminal widget

`TSharpVision.Terminal` supplies `TTerminal`, its emulator, input encoder and byte-stream child sessions. The widget draws terminal output inside the view hierarchy, with scrolling, selection and optional local command editing. `TSharpVision` supplies the core framework; `TSharpVision.Drivers.Terminal` instead hosts the whole application in an outer ANSI terminal. The widget works with any framework driver.

## Ownership and transport

Attach an `ITerminalSession` before starting it so the widget receives its output. The caller owns the session: attaching, detaching and shutting down the widget do not start, stop or dispose it. Stop and dispose real sessions when finished. `InMemoryTerminalSession` records input even while stopped and has no-op disposal for tests and demonstrations.

Sessions exchange bytes. `SendInputAsync` copies input before returning; `TerminalOutputEventArgs.Data` is borrowed only during the output callback and must be copied to retain it. `SendTextAsync` encodes UTF-8. The emulator handles UTF-8 characters and escape sequences split across chunks; string helpers sit above the byte transport.

Output and exit callbacks can run on background threads. The widget parses output under `SyncRoot` and applies view changes on the event-loop thread. Hold `SyncRoot` when inspecting its `Emulator` while a session is attached. Session input must not block the caller on a child that is not reading.

## Child sessions

Use `PtyAvailability` to check native support, then select `ConPtyTerminalSession` on supported Windows hosts or `PosixPtyTerminalSession` on supported POSIX hosts. Options select the executable, arguments, working directory and initial size; POSIX options also allow environment overrides. `ProcessTerminalSession` uses redirected pipes and does not provide a PTY's interactive terminal behavior.

Native sessions support resize, interruption, stopping and exit-code observation. Their input is copied into an ordered queue; input beyond the 4 MiB pending bound, or after the session ends, is dropped. Stop and disposal handle child-process cleanup, including the Windows process tree or POSIX process group, rather than leaving a child running after the view disappears.

ConPTY children inherit the parent environment. For Unix-oriented programs such as Git for Windows `less`, launch with `TERM=xterm-256color` to match the emulator rather than inheriting `TERM=dumb`. Outer-host Kitty/CSI-u negotiation belongs to the ANSI driver and is independent of a child session.
