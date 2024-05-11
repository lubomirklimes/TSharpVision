using System;

namespace SharpVision.Tests.Infrastructure;

/// <summary>
/// Saves and restores Environment.CurrentDirectory around a test.
/// </summary>
public sealed class CwdScope : IDisposable
{
    private readonly string _saved;

    public CwdScope()
    {
        _saved = Environment.CurrentDirectory;
    }

    public CwdScope(string newCwd) : this()
    {
        Environment.CurrentDirectory = newCwd;
    }

    public void Dispose()
    {
        try { Environment.CurrentDirectory = _saved; }
        catch { /* best-effort restore */ }
    }
}
