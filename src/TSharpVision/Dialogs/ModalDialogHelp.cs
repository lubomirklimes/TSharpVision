using TSharpVision.Constants;

namespace TSharpVision;

// Routes a modal standard dialog's Help button or F1 (cmHelp) to its program's help. Used by TFileDialog and
// TChDirDialog; a non-modal dialog leaves cmHelp to TProgram.HandleEvent as before.
internal static class ModalDialogHelp
{
    internal static void Handle(TGroup dialog, ref TEvent ev)
    {
        if (ev.What != Events.evCommand || ev.message.command != Views.cmHelp
            || (dialog.state & Views.sfModal) == 0 || dialog.owner is not TGroup host)
            return;
        TGroup? program = host;
        while (program != null && program is not TProgram) program = program.owner;
        // Without a help provider the event stays unhandled, exactly as before.
        if (program is TProgram p && p.ExecuteModalHelp(host, dialog.GetHelpCtx()))
            dialog.ClearEvent(ref ev);
    }
}
