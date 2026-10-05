using System.Diagnostics;

namespace TSharpVision.Diagnostics.Keyboard.Sources;

/// <summary>Where the target's console comes from.</summary>
public enum TargetConsole
{
    /// <summary>The target has its own window (SDL) or takes over the controller's terminal.</summary>
    Inherit,
    /// <summary>A new console in whatever host Windows opens by default (often Windows Terminal).</summary>
    NewDefault,
    /// <summary>A new console in the classic console host, started explicitly.</summary>
    Conhost,
}

/// <summary>The launched target process and its guaranteed cleanup.</summary>
public sealed class TargetProcess : IDisposable
{
    private Process? _process;

    /// <summary>The target's own process id; null when the launched process is a console host that started it.</summary>
    public int? Id { get; private set; }
    public bool Exited => _process is null || _process.HasExited;

    public void Start(TargetLaunch launch, TargetConsole console)
    {
        ProcessStartInfo info;
        switch (console)
        {
            case TargetConsole.NewDefault:
            case TargetConsole.Conhost:
                // A shell-executed console application gets its own console, in whatever host Windows
                // opens by default. "conhost.exe <command line>" instead runs the command in a classic
                // console window regardless of the default terminal; the target is then a child of
                // that host, so its process id is not the launched one and the session token alone
                // proves its identity. Neither may inherit this process's standard handles: a console
                // host that is handed pipes turns itself into a pseudo console.
                bool classic = console == TargetConsole.Conhost;
                info = new ProcessStartInfo(classic
                    ? Path.Combine(Environment.SystemDirectory, "conhost.exe") : launch.FileName)
                {
                    UseShellExecute = true,
                };
                if (classic) info.ArgumentList.Add(launch.FileName);
                foreach (string argument in launch.Arguments) info.ArgumentList.Add(argument);
                // Shell execution cannot carry a private environment, so the child inherits this
                // process's; the session variables are removed again as soon as it has started.
                foreach ((string name, string value) in launch.Environment)
                    Environment.SetEnvironmentVariable(name, value);
                try { _process = Process.Start(info); }
                finally
                {
                    foreach (string name in launch.Environment.Keys) Environment.SetEnvironmentVariable(name, null);
                }
                Id = classic ? null : _process?.Id;
                break;

            default:
                info = new ProcessStartInfo(launch.FileName) { UseShellExecute = false };
                foreach (string argument in launch.Arguments) info.ArgumentList.Add(argument);
                foreach ((string name, string value) in launch.Environment) info.Environment[name] = value;
                _process = Process.Start(info);
                Id = _process?.Id;
                break;
        }

        if (_process is null) throw new InvalidOperationException("The target process did not start.");
    }

    public void Dispose()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited && !_process.WaitForExit(2000)) _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        _process.Dispose();
        _process = null;
    }
}
