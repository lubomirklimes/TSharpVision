using TSharpVision.Constants;

namespace TSharpVision;

public static class FileDialogOptions
{
    public const ushort fdOKButton      = 0x0001;
    public const ushort fdOpenButton    = 0x0002;
    public const ushort fdReplaceButton = 0x0004;
    public const ushort fdClearButton   = 0x0008;
    public const ushort fdHelpButton    = 0x0010;
    public const ushort fdSelectButton  = 0x0020;
    public const ushort fdDoneButton    = 0x0040;
    public const ushort fdAddButton     = 0x0080;
    public const ushort fdNoLoadDir     = 0x0100;
}

// Modal "Open File" dialog. Composed of a TFileInputLine (with history),
// a TFileList scroll-box, a TFileInfoPane footer, and a stack of buttons
// (Open / OK / Add / Select / Replace / Clear / Cancel|Done / Help)
// driven by the fd* options bitmask.
public class TFileDialog : TDialog, IFileDialogContext
{
    public new static readonly string Name = "TFileDialog";

    public string wildCard;
    public TFileInputLine fileName;
    public TFileList      fileList;
    public string         directory;

    public string LastError = string.Empty;

    public string Directory => directory ?? string.Empty;
    public string WildCard  => wildCard  ?? string.Empty;

    public TFileDialog(string aWildCard,
                       string aTitle,
                       string inputName,
                       ushort aOptions,
                       byte   histId)
        : base(new TRect(15, 1, 64, 21), aTitle)
    {
        options |= Views.ofCentered;
        growMode = Views.gfGrowAll;
        flags    = (byte)(flags | Views.wfGrow | Views.wfZoom);
        wildCard = aWildCard ?? "*";

        fileName = new TFileInputLine(new TRect(3, 2, 31, 3), 260);
        fileName.SetData(wildCard);
        fileName.growMode = Views.gfGrowHiX;
        Insert(fileName);

        Insert(new TLabel(new TRect(2, 1, 17, 2), inputName ?? string.Empty, fileName));
        var his = new THistory(new TRect(31, 2, 34, 3), fileName, histId);
        his.growMode = (byte)(Views.gfGrowLoX | Views.gfGrowHiX);
        Insert(his);

        // .NET always supports long file names; we mirror upstream's
        // longNames=1 layout (vertical scroll bar at column 34).
        var sb = new TScrollBar(new TRect(34, 5, 35, 16));
        Insert(sb);
        fileList = new TFileList(new TRect(3, 5, 34, 16), sb);
        fileList.growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        Insert(fileList);
        Insert(new TLabel(new TRect(2, 4, 17, 5), TSharpVisionIntl.Get("File_Label_Files", "~F~iles"), fileList));

        ushort opt = ButtonConstants.bfDefault;
        TRect r = new TRect(35, 2, 46, 4);

        TButton bt;
        // local helper as anonymous lambda — cannot use #define here.
        void AddButton(ushort flag, string name, ushort command)
        {
            if ((aOptions & flag) == 0) return;
            bt = new TButton(r, name, command, (ushort)opt);
            bt.growMode = (byte)(Views.gfGrowLoX | Views.gfGrowHiX);
            Insert(bt);
            opt = ButtonConstants.bfNormal;
            r = new TRect(r.a.x, r.a.y + 2, r.b.x, r.b.y + 2);
        }

        AddButton(FileDialogOptions.fdOpenButton,    TSharpVisionIntl.Get("File_Btn_Open",    "~O~pen"),    Views.cmFileOpen);
        AddButton(FileDialogOptions.fdOKButton,      TSharpVisionIntl.Get("Btn_OK",           "~O~K"),      Views.cmFileOpen);
        AddButton(FileDialogOptions.fdAddButton,     TSharpVisionIntl.Get("File_Btn_Add",     "~A~dd"),     Views.cmFileOpen);
        AddButton(FileDialogOptions.fdSelectButton,  TSharpVisionIntl.Get("File_Btn_Select",  "~S~elect"),  Views.cmFileSelect);
        AddButton(FileDialogOptions.fdReplaceButton, TSharpVisionIntl.Get("File_Btn_Replace", "~R~eplace"), Views.cmFileReplace);
        AddButton(FileDialogOptions.fdClearButton,   TSharpVisionIntl.Get("File_Btn_Clear",   "~C~lear"),   Views.cmFileClear);

        bt = new TButton(r,
            (aOptions & FileDialogOptions.fdDoneButton) != 0
                ? TSharpVisionIntl.Get("Btn_Done", "Done")
                : TSharpVisionIntl.Get("Btn_Cancel", "Cancel"),
            Views.cmCancel, ButtonConstants.bfNormal);
        bt.growMode = (byte)(Views.gfGrowLoX | Views.gfGrowHiX);
        Insert(bt);
        r = new TRect(r.a.x, r.a.y + 2, r.b.x, r.b.y + 2);

        if ((aOptions & FileDialogOptions.fdHelpButton) != 0)
        {
            bt = new TButton(r, TSharpVisionIntl.Get("Btn_Help", "~H~elp"), Views.cmHelp, ButtonConstants.bfNormal);
            bt.growMode = (byte)(Views.gfGrowLoX | Views.gfGrowHiX);
            Insert(bt);
            r = new TRect(r.a.x, r.a.y + 2, r.b.x, r.b.y + 2);
        }

        var fip = new TFileInfoPane(new TRect(1, 16, 48, 19));
        fip.growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY | Views.gfGrowLoY);
        Insert(fip);

        SelectNext(false);
        if ((aOptions & FileDialogOptions.fdNoLoadDir) == 0)
            ReadDirectory();
        else
            SetUpCurDir();
    }

    public override void SizeLimits(ref TPoint min, ref TPoint max)
    {
        base.SizeLimits(ref min, ref max);
        min.x = 64 - 15;
        min.y = 21 - 1;
    }

    public override void ShutDown()
    {
        fileName = null;
        fileList = null;
        base.ShutDown();
    }

    public virtual void GetFileName(out string s)
    {
        string buf = (fileName?.Data ?? string.Empty).Trim();
        if (!System.IO.Path.IsPathRooted(buf))
        {
            try { buf = System.IO.Path.Combine(directory ?? string.Empty, buf); }
            catch { s = buf; return; }
        }
        try { buf = System.IO.Path.GetFullPath(buf); }
        catch { /* leave buf as-is — Valid() will surface the error */ }
        s = buf;
    }

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evCommand)
        {
            switch (@event.message.command)
            {
                case Views.cmFileOpen:
                case Views.cmFileReplace:
                case Views.cmFileClear:
                case Views.cmFileSelect:
                    EndModal(@event.message.command);
                    ClearEvent(ref @event);
                    break;
            }
        }
        else if (@event.What == Events.evBroadcast
                 && @event.message.command == Views.cmFileDoubleClicked)
        {
            @event.What = Events.evCommand;
            @event.message.command = Views.cmOK;
            PutEvent(ref @event);
            ClearEvent(ref @event);
        }
    }

    public virtual void ReadDirectory()
    {
        fileList?.ReadDirectory(wildCard);
        SetUpCurDir();
    }

    public virtual void SetUpCurDir()
    {
        string cur = System.IO.Directory.GetCurrentDirectory();
        if (cur.Length > 0
            && cur[cur.Length - 1] != System.IO.Path.DirectorySeparatorChar
            && cur[cur.Length - 1] != '/')
            cur += System.IO.Path.DirectorySeparatorChar;
        directory = cur;
    }

    public virtual void SetData(object rec)
    {
        if (rec is string s && s.Length > 0 && IsWild(s))
        {
            fileName?.SetData(s);
            Valid(Views.cmFileInit);
            fileName?.Select();
        }
        else if (rec is string s2)
        {
            fileName?.SetData(s2);
        }
    }

    public virtual void GetData(out string rec)
    {
        GetFileName(out rec);
    }

    public virtual bool CheckDirectory(string str)
    {
        if (string.IsNullOrEmpty(str)) goto fail;
        try
        {
            if (System.IO.Directory.Exists(str)) return true;
        }
        catch (System.Exception ex)
        {
            if (owner != null)
                MsgBox.MessageBox(owner,
                    string.Format(TSharpVisionIntl.Get(
                        "File_Err_CannotOpenDir", "Cannot open directory: '{0}'"), str)
                    + "\n" + ex.Message,
                    MsgBox.mfError | MsgBox.mfOKButton);
            fileName?.Select();
            return false;
        }
        fail:
        if (owner != null)
            MsgBox.MessageBox(owner,
                TSharpVisionIntl.Get("File_Err_InvalidDir", "Invalid drive or directory."),
                MsgBox.mfError | MsgBox.mfOKButton);
        fileName?.Select();
        return false;
    }

    public override bool Valid(ushort command)
    {
        if (!base.Valid(command)) return false;
        if (command == Views.cmValid || command == Views.cmCancel) return true;

        GetFileName(out string fName);
        if (command == Views.cmFileClear) return true;

        if (IsWild(fName))
        {
            ExpandPath(fName, out string dir, out string name);
            if (CheckDirectory(dir))
            {
                directory = dir;
                wildCard  = name;
                if (command != Views.cmFileInit) fileList?.Select();
                fileList?.ReadDirectory(directory, wildCard);
            }
            return false;
        }
        if (System.IO.Directory.Exists(fName))
        {
            if (CheckDirectory(fName))
            {
                if (fName.Length == 0
                    || (fName[fName.Length - 1] != System.IO.Path.DirectorySeparatorChar
                        && fName[fName.Length - 1] != '/'))
                    fName += System.IO.Path.DirectorySeparatorChar;
                directory = fName;
                if (command != Views.cmFileInit) fileList?.Select();
                fileList?.ReadDirectory(directory, wildCard);
            }
            return false;
        }
        if (IsValidFileName(fName)) return true;

        string errMsg = TSharpVisionIntl.Get("File_Err_InvalidFileName", "Invalid file name.");
        if (owner != null)
            MsgBox.MessageBox(owner, errMsg, MsgBox.mfError | MsgBox.mfOKButton);
        LastError = errMsg;
        return false;
    }

    public static bool IsWild(string s)
        => !string.IsNullOrEmpty(s)
           && (s.IndexOf('*') >= 0 || s.IndexOf('?') >= 0);

    private static void ExpandPath(string path, out string dir, out string name)
    {
        if (string.IsNullOrEmpty(path)) { dir = string.Empty; name = string.Empty; return; }
        int slash = -1;
        for (int i = path.Length - 1; i >= 0; i--)
            if (path[i] == '/' || path[i] == '\\') { slash = i; break; }
        if (slash < 0)
        {
            // No directory component — assume current.
            dir = System.IO.Directory.GetCurrentDirectory();
            if (dir.Length > 0
                && dir[dir.Length - 1] != System.IO.Path.DirectorySeparatorChar)
                dir += System.IO.Path.DirectorySeparatorChar;
            name = path;
        }
        else
        {
            dir  = path.Substring(0, slash + 1);
            name = path.Substring(slash + 1);
        }
    }

    private static bool IsValidFileName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        // Reject strings that still contain wildcard characters.
        if (s.IndexOf('*') >= 0 || s.IndexOf('?') >= 0) return false;
        try
        {
            string full = System.IO.Path.GetFullPath(s);
            if (full.Length == 0) return false;
            if (full.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0) return false;
            // Reject pure directory paths (trailing separator).
            char last = full[full.Length - 1];
            return last != System.IO.Path.DirectorySeparatorChar
                && last != System.IO.Path.AltDirectorySeparatorChar;
        }
        catch { return false; }
    }

    protected TFileDialog(StreamableInit init) : base(init) { }

    // Wire: TDialog base + wildCard(string) + fileName(ptr) + fileList(ptr).
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteString(wildCard);
        os.WritePointer(fileName);
        os.WritePointer(fileList);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        wildCard = isStream.ReadString() ?? string.Empty;
        fileName = (TFileInputLine)isStream.ReadPointer();
        fileList = (TFileList)isStream.ReadPointer();
        ReadDirectory();
        return this;
    }

    public new static TStreamable Build() => new TFileDialog(StreamableInit.streamableInit);
    public static readonly TStreamableClass StreamableClassTFileDialog =
        new TStreamableClass("TFileDialog", () => new TFileDialog(StreamableInit.streamableInit), 0);
}
