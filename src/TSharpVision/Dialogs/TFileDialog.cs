using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>Flags choosing file-dialog buttons, initial directory loading, and encoding controls.</summary>
public static class FileDialogOptions
{
    /// <summary>Includes an OK acceptance button.</summary>
    public const ushort fdOKButton      = 0x0001;
    /// <summary>Includes an Open action button.</summary>
    public const ushort fdOpenButton    = 0x0002;
    /// <summary>Includes a Replace action button.</summary>
    public const ushort fdReplaceButton = 0x0004;
    /// <summary>Includes a Clear action button.</summary>
    public const ushort fdClearButton   = 0x0008;
    /// <summary>Includes a Help action button.</summary>
    public const ushort fdHelpButton    = 0x0010;
    /// <summary>Includes a Select action button.</summary>
    public const ushort fdSelectButton  = 0x0020;
    /// <summary>Labels the dismissal button Done instead of Cancel.</summary>
    public const ushort fdDoneButton    = 0x0040;
    /// <summary>Includes an Add action button.</summary>
    public const ushort fdAddButton     = 0x0080;
    /// <summary>Defers initial directory enumeration.</summary>
    public const ushort fdNoLoadDir     = 0x0100;
    /// <summary>Includes a choice of editor text encodings.</summary>
    public const ushort fdEncodingSelector = 0x0200;
}

/// <summary>File chooser with wildcard filtering, input history, file metadata, and optional text-encoding selection.</summary>
public class TFileDialog : TDialog, IFileDialogContext
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TFileDialog";

    /// <summary>Current filename wildcard used to filter directory entries.</summary>
    public string? wildCard;
    /// <summary>Owned filename input control.</summary>
    public TFileInputLine? fileName;
    /// <summary>Owned list of matching files and navigable directories.</summary>
    public TFileList? fileList;
    /// <summary>Optional owned encoding-choice control; null when omitted by dialog options.</summary>
    public TRadioButtons?  encodingSelector;
    /// <summary>Current directory used to resolve relative input paths.</summary>
    public string? directory;

    /// <summary>Most recent directory or filename error message; empty when none is recorded.</summary>
    public string LastError = string.Empty;

    /// <summary>Current directory path, returning an empty string when the backing field is null.</summary>
    public string Directory => directory ?? string.Empty;
    /// <summary>Current wildcard filter, returning an empty string when the backing field is null.</summary>
    public string WildCard  => wildCard  ?? string.Empty;
    /// <summary>Encoding selected by the optional control, or the first built-in choice when absent or invalid.</summary>
    public EditorTextEncoding SelectedEncoding =>
        EncodingAt(encodingSelector == null ? 0 : (int)encodingSelector.value);

    private static EditorTextEncoding EncodingAt(int index)
    {
        if (index < 0 || index >= EditorEncodingChoices.BuiltIn.Count)
            index = 0;
        return EditorEncodingChoices.BuiltIn[index].Encoding;
    }

    /// <summary>Creates a centered file chooser with an initial wildcard, title, input label, option flags, and history ID.</summary>
    public TFileDialog(string aWildCard,
                       string aTitle,
                       string inputName,
                       ushort aOptions,
                       byte   histId)
        : base(DialogBounds(aOptions), aTitle)
    {
        options |= Views.ofCentered;
        growMode = Views.gfGrowAll;
        flags    = (byte)(flags | Views.wfGrow | Views.wfZoom);
        wildCard = aWildCard ?? "*";

        fileName = new TFileInputLine(
            new TRect(3, 2, 31, 3),
            FileDialogConstants.MaxPathLen);
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

        if ((aOptions & FileDialogOptions.fdEncodingSelector) != 0)
        {
            Insert(new TLabel(
                new TRect(35, 6, 49, 7),
                TSharpVisionIntl.Get("File_Label_Encoding", "~E~ncoding"),
                null));
            encodingSelector = new TRadioButtons(
                new TRect(35, 7, 64, 15),
                EncodingChoiceItems());
            encodingSelector.growMode = (byte)(Views.gfGrowLoX | Views.gfGrowHiX);
            encodingSelector.SetData((ushort)0);
            Insert(encodingSelector);
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

    private static TRect DialogBounds(ushort options)
        => (options & FileDialogOptions.fdEncodingSelector) != 0
            ? new TRect(8, 1, 73, 21)
            : new TRect(15, 1, 64, 21);

    private static TSItem EncodingChoiceItems()
    {
        TSItem? head = null;
        TSItem? tail = null;
        foreach (var choice in EditorEncodingChoices.BuiltIn)
        {
            var item = new TSItem(choice.Label, null);
            if (head == null)
            {
                head = item;
                tail = item;
            }
            else
            {
                if (tail == null)
                    throw new InvalidOperationException("The encoding choice chain is incomplete.");
                tail.Next = item;
                tail = item;
            }
        }
        return head ?? throw new InvalidOperationException("No editor encoding choices are configured.");
    }

    /// <inheritdoc />
    public override void SizeLimits(ref TPoint min, ref TPoint max)
    {
        base.SizeLimits(ref min, ref max);
        min.x = encodingSelector == null ? 64 - 15 : 73 - 8;
        min.y = 21 - 1;
    }

    /// <inheritdoc />
    public override void ShutDown()
    {
        fileName = null;
        fileList = null;
        encodingSelector = null;
        base.ShutDown();
    }

    /// <summary>Resolves trimmed input against the dialog directory and expands it to a full path when possible.</summary>
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

    /// <inheritdoc />
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

    /// <summary>Reloads matching file entries, records any enumeration error, and refreshes the current-directory field.</summary>
    public virtual void ReadDirectory()
    {
        fileList?.ReadDirectory(wildCard ?? string.Empty);
        LastError = fileList?.LastError ?? string.Empty;
        SetUpCurDir();
    }

    /// <summary>Stores the process working directory with a trailing separator.</summary>
    public virtual void SetUpCurDir()
    {
        string cur = System.IO.Directory.GetCurrentDirectory();
        if (cur.Length > 0
            && cur[cur.Length - 1] != System.IO.Path.DirectorySeparatorChar
            && cur[cur.Length - 1] != '/')
            cur += System.IO.Path.DirectorySeparatorChar;
        directory = cur;
    }

    /// <inheritdoc />
    public override void SetData(object rec)
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

    /// <summary>Returns the resolved filename represented by the current input.</summary>
    public virtual void GetData(out string rec)
    {
        GetFileName(out rec);
    }

    /// <summary>Tests whether a directory exists and records an error message when it does not.</summary>
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

    /// <inheritdoc />
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
                fileList?.ReadDirectory(fName, wildCard ?? string.Empty);
                LastError = fileList?.LastError ?? string.Empty;
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
                fileList?.ReadDirectory(fName, wildCard ?? string.Empty);
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

    /// <summary>Returns whether the string contains an asterisk or question-mark wildcard.</summary>
    public static bool IsWild(string? s)
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

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TFileDialog(StreamableInit init) : base(init) { }

    // Wire: TDialog base + wildCard(string) + fileName(ptr) + fileList(ptr).
    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteString(wildCard);
        os.WritePointer(fileName);
        os.WritePointer(fileList);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        wildCard = isStream.ReadString() ?? string.Empty;
        fileName = isStream.ReadPointer() as TFileInputLine;
        fileList = isStream.ReadPointer() as TFileList;
        ReadDirectory();
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TFileDialog(StreamableInit.streamableInit);
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTFileDialog =
        new TStreamableClass("TFileDialog", () => new TFileDialog(StreamableInit.streamableInit), 0);
}
