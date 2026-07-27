using TSharpVision.Constants;

namespace TSharpVision.Samples.TVTerm;

/// <summary>
/// Result of a Run Command... dialog if confirmed.
/// </summary>
public sealed class RunCommandResult
{
    public string FileName { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    /// <summary>0 = Pipe, 1 = PTY.</summary>
    public uint ModeIndex { get; set; }

    public SessionKind Kind => ModeIndex == 1 ? SessionKind.Pty : SessionKind.Pipe;
}

/// <summary>
/// Dialog for File → Run Command…. Returns user input via <see cref="ShowDialog"/>.
/// </summary>
public static class RunCommandDialog
{
    public static RunCommandResult? ShowDialog(TGroup parent)
    {
        var dlg = new TDialog(new TRect(0, 0, 60, 14), "Run Command");
        dlg.options |= Views.ofCentered;

        var fileInput = new TInputLine(new TRect(15, 2, 56, 3), 260);
        dlg.Insert(fileInput);
        dlg.Insert(new TLabel(new TRect(2, 2, 14, 3), "~P~rogram:", fileInput));

        var argsInput = new TInputLine(new TRect(15, 4, 56, 5), 260);
        dlg.Insert(argsInput);
        dlg.Insert(new TLabel(new TRect(2, 4, 14, 5), "~A~rguments:", argsInput));

        var cwdInput = new TInputLine(new TRect(15, 6, 56, 7), 260);
        dlg.Insert(cwdInput);
        dlg.Insert(new TLabel(new TRect(2, 6, 14, 7), "~C~wd:", cwdInput));

        var mode = new TRadioButtons(
            new TRect(15, 8, 35, 10),
            new TSItem("P~i~pe",
            new TSItem("P~T~Y", null)));
        mode.value = 1u;
        dlg.Insert(mode);
        dlg.Insert(new TLabel(new TRect(2, 8, 14, 9), "~M~ode:", mode));

        dlg.Insert(new TButton(new TRect(15, 11, 27, 13), "O~K~", Views.cmOK, ButtonConstants.bfDefault));
        dlg.Insert(new TButton(new TRect(30, 11, 42, 13), "Cancel", Views.cmCancel, ButtonConstants.bfNormal));

        dlg.SelectNext(false);

        ushort res = parent.ExecView(dlg);
        if (res != Views.cmOK) return null;

        var r = new RunCommandResult
        {
            FileName = fileInput.Data?.Trim() ?? string.Empty,
            Arguments = argsInput.Data?.Trim() ?? string.Empty,
            WorkingDirectory = cwdInput.Data?.Trim() ?? string.Empty,
            ModeIndex = mode.value,
        };
        if (string.IsNullOrEmpty(r.FileName)) return null;
        return r;
    }
}
