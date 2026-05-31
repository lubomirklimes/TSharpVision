// Modal "Change Directory" dialog. Composed of a TInputLine + history,
// a TDirListBox, and OK/Chdir/Revert buttons.
//
// Deferred:
//   * The optional Help button (cdHelpButton) is wired as a plain
//     command-emitting TButton; the help system itself is deferred.
//   * The upstream `valid()` calls messageBox on failure; here we surface
//     the error via the optional `LastError` property when no host is
//     available to display it.
using TSharpVision.Constants;

namespace TSharpVision;

public static class ChDirDialogOptions
{
    public const ushort cdNormal     = 0x0000;
    public const ushort cdNoLoadDir  = 0x0001;
    public const ushort cdHelpButton = 0x0002;
}

public class TChDirDialog : TDialog
{
    public new static readonly string Name = "TChDirDialog";

    public TInputLine  dirInput;
    public TDirListBox dirList;
    public TButton     okButton;
    public TButton     chDirButton;

    public string LastError = string.Empty;

    public TChDirDialog(ushort opts, ushort histId)
        : base(new TRect(16, 2, 64, 21), TSharpVisionIntl.Get("ChDir_Title", "Change Directory"))
    {
        options |= Views.ofCentered;

        dirInput = new TInputLine(
            new TRect(3, 3, 30, 4),
            FileDialogConstants.MaxPathLen);
        Insert(dirInput);
        Insert(new TLabel(
            new TRect(2, 2, 17, 3),
            TSharpVisionIntl.Get("ChDir_Label_DirectoryName", "Directory ~n~ame"),
            dirInput));
        Insert(new THistory(new TRect(30, 3, 33, 4), dirInput, histId));

        var sbv = new TScrollBar(new TRect(32, 6, 33, 16));
        Insert(sbv);
        var sbh = new TScrollBar(new TRect(3, 16, 32, 17));
        sbh.SetRange(0, 260);
        sbh.SetStep(28, 1);
        Insert(sbh);

        dirList = new TDirListBox(new TRect(3, 6, 32, 16), sbv, sbh);
        Insert(dirList);
        Insert(new TLabel(
            new TRect(2, 5, 17, 6),
            TSharpVisionIntl.Get("ChDir_Label_DirectoryTree", "Directory ~t~ree"),
            dirList));

        okButton = new TButton(new TRect(35, 6, 45, 8),
                               TSharpVisionIntl.Get("Btn_OK", "~O~K"), Views.cmOK, ButtonConstants.bfDefault);
        Insert(okButton);
        chDirButton = new TButton(new TRect(35, 9, 45, 11),
                                  TSharpVisionIntl.Get("ChDir_Btn_Chdir", "~C~hdir"), Views.cmChangeDir, ButtonConstants.bfNormal);
        Insert(chDirButton);
        Insert(new TButton(new TRect(35, 12, 45, 14),
                           TSharpVisionIntl.Get("ChDir_Btn_Revert", "~R~evert"), Views.cmRevert, ButtonConstants.bfNormal));
        if ((opts & ChDirDialogOptions.cdHelpButton) != 0)
            Insert(new TButton(new TRect(35, 15, 45, 17),
                               TSharpVisionIntl.Get("Btn_Help", "~H~elp"), Views.cmHelp, ButtonConstants.bfNormal));

        if ((opts & ChDirDialogOptions.cdNoLoadDir) == 0)
            SetUpDialog();
        SelectNext(false);
    }

    public override ushort DataSize() => 0;

    public override void ShutDown()
    {
        dirList     = null;
        dirInput    = null;
        okButton    = null;
        chDirButton = null;
        base.ShutDown();
    }

    public virtual void GetData(object _) { }
    public override void SetData(object _) { }

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What != Events.evCommand) return;

        string curDir = string.Empty;
        switch (@event.message.command)
        {
            case Views.cmRevert:
                curDir = NormaliseDir(System.IO.Directory.GetCurrentDirectory());
                break;
            case Views.cmChangeDir:
            {
                if (dirList == null || dirList.List() == null) return;
                if (dirList.focused < 0 || dirList.focused >= dirList.List().Count)
                    return;
                var entry = dirList.List().At(dirList.focused);
                curDir = entry.Dir() ?? string.Empty;
                if (string.Equals(curDir, "Drives", System.StringComparison.Ordinal))
                    break;  // upstream: fall through to NewDirectory("Drives")
                if (curDir.Length > 0 &&
                    curDir[curDir.Length - 1] != System.IO.Path.DirectorySeparatorChar &&
                    curDir[curDir.Length - 1] != '/')
                    curDir += System.IO.Path.DirectorySeparatorChar;
                break;
            }
            case Views.cmDirSelection:
                if (chDirButton != null)
                    chDirButton.MakeDefault(@event.message.infoPtr != null);
                return;
            default:
                return;
        }
        if (dirList != null) dirList.NewDirectory(curDir);
        if (dirInput != null)
        {
            // Trim trailing dirsep except for "X:/"  (CLY_HaveDriveLetters).
            string display = curDir;
            int len = display.Length;
            if (len > 3 && (display[len - 1] == System.IO.Path.DirectorySeparatorChar
                          || display[len - 1] == '/'))
                display = display.Substring(0, len - 1);
            dirInput.SetData(display);
            dirInput.DrawView();
        }
        dirList?.Select();
        ClearEvent(ref @event);
    }

    public virtual void SetUpDialog()
    {
        if (dirList == null) return;
        string curDir = NormaliseDir(System.IO.Directory.GetCurrentDirectory());
        dirList.NewDirectory(curDir);
        if (dirInput != null)
        {
            string display = curDir;
            int len = display.Length;
            if (len > 3 && (display[len - 1] == System.IO.Path.DirectorySeparatorChar
                          || display[len - 1] == '/'))
                display = display.Substring(0, len - 1);
            dirInput.SetData(display);
            dirInput.DrawView();
        }
    }

    public override bool Valid(ushort command)
    {
        if (command != Views.cmOK) return true;
        string target = dirInput?.Data ?? string.Empty;
        try
        {
            System.IO.Directory.SetCurrentDirectory(target);
            return true;
        }
        catch (System.UnauthorizedAccessException ex)
        {
            LastError = ex.Message;
            if (owner != null)
                MsgBox.MessageBox(owner,
                    string.Format(
                        TSharpVisionIntl.Get("File_Err_AccessDenied", "Access denied: '{0}'"),
                        target),
                    MsgBox.mfError | MsgBox.mfOKButton);
            return false;
        }
        catch (System.Exception ex)
        {
            LastError = ex.Message;
            if (owner != null)
                MsgBox.MessageBox(owner,
                    TSharpVisionIntl.Get("File_Err_InvalidDir", "Invalid drive or directory."),
                    MsgBox.mfError | MsgBox.mfOKButton);
            return false;
        }
    }

    private static string NormaliseDir(string p)
    {
        if (string.IsNullOrEmpty(p)) return string.Empty;
        char last = p[p.Length - 1];
        if (last != System.IO.Path.DirectorySeparatorChar && last != '/')
            p += System.IO.Path.DirectorySeparatorChar;
        return p;
    }

    protected TChDirDialog(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WritePointer(dirList);
        os.WritePointer(dirInput);
        os.WritePointer(okButton);
        os.WritePointer(chDirButton);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        dirList     = (TDirListBox)isStream.ReadPointer();
        dirInput    = (TInputLine)isStream.ReadPointer();
        okButton    = (TButton)isStream.ReadPointer();
        chDirButton = (TButton)isStream.ReadPointer();
        SetUpDialog();
        return this;
    }

    public new static TStreamable Build() => new TChDirDialog(StreamableInit.streamableInit);
    public static readonly TStreamableClass StreamableClassTChDirDialog =
        new TStreamableClass("TChDirDialog", () => new TChDirDialog(StreamableInit.streamableInit), 0);
}
