using System;
using SharpVision;
using SharpVision.Constants;

namespace SharpVision.Tests.Infrastructure;

/// <summary>
/// Saves and restores TEditor static clipboard state and the editor dialog stubs
/// that are shared across the whole assembly.
/// </summary>
public sealed class EditorClipboardScope : IDisposable
{
    private readonly TEditor _savedClipboard;
    private readonly TEditor.TEditorDialog _savedDialog;
    private readonly ushort _savedFlags;
    private readonly byte[] _savedFindStr;
    private readonly byte[] _savedReplaceStr;

    public EditorClipboardScope()
    {
        _savedClipboard = TEditor.clipboard;
        _savedDialog    = TEditor.editorDialog;
        _savedFlags     = TEditor.editorFlags;

        _savedFindStr    = (byte[])TEditor.findStr.Clone();
        _savedReplaceStr = (byte[])TEditor.replaceStr.Clone();

        // Reset to safe defaults.
        TEditor.clipboard    = null;
        TEditor.editorDialog = (_, _) => Views.cmCancel;
        TEditor.editorFlags  = (ushort)(Views.efBackupFiles | Views.efPromptOnReplace);
        Array.Clear(TEditor.findStr,    0, TEditor.findStr.Length);
        Array.Clear(TEditor.replaceStr, 0, TEditor.replaceStr.Length);
    }

    public void Dispose()
    {
        TEditor.clipboard    = _savedClipboard;
        TEditor.editorDialog = _savedDialog;
        TEditor.editorFlags  = _savedFlags;
        Array.Copy(_savedFindStr,    TEditor.findStr,    _savedFindStr.Length);
        Array.Copy(_savedReplaceStr, TEditor.replaceStr, _savedReplaceStr.Length);
    }
}
