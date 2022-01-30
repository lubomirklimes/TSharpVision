using SharpVision.Constants;

namespace SharpVision;

// Tree-style listbox for directories. Holds a TDirCollection of TDirEntry
// records (the upstream `list()` accessor) instead of TListBox's
// TStringCollection.
public class TDirListBox : TListBox
{
    public new static readonly string Name = "TDirListBox";

    // Tree-drawing primitives. Upstream uses CP437 box-drawing characters;
    // ASCII fallbacks keep the same column width (3 chars per separator)
    // so the offset arithmetic is preserved.
    public static string PathDir   = "+- ";
    public static string FirstDir  = "+- ";
    public static string MiddleDir = " +-";
    public static string LastDir   = " +-";
    public static string Graphics  = "-++";

    // Current path (full directory name), index of the entry to render
    // with sfSelected, and the incremental-search cursor
    public string dir = string.Empty;
    public int    cur;
    public int    incPos;

    // The actual TDirCollection lives here (the base TListBox.items field
    // remains null — we override every accessor that touches it).
    private TDirCollection _dirs;

    public TDirListBox(TRect bounds, TScrollBar aVScrollBar, TScrollBar aHScrollBar)
        : base(bounds, 1, aVScrollBar)
    {
        // TListBox(bounds, 1, aScrollBar) wires aScrollBar as the H bar
        // because of its single-scroll-bar signature; rewire so that
        // aHScrollBar is the horizontal one and aVScrollBar drives focus.
        hScrollBar = aHScrollBar;
        cur = 0;
        incPos = 0;
        ShowCursor();
    }

    public override string GetText(int item, int maxChars)
    {
        if (_dirs == null || item < 0 || item >= _dirs.Count) return string.Empty;
        string s = _dirs.At(item).Text() ?? string.Empty;
        if (s.Length > maxChars) s = s.Substring(0, maxChars);
        return s;
    }

    public override bool IsSelected(int item) => item == cur;

    public TDirCollection List() => _dirs;

    public void UpdateCursorPos()
    {
        if (_dirs == null || focused < 0 || focused >= _dirs.Count) return;
        int x = _dirs.At(focused).Offset() + 1;
        if (incPos > 1) x += incPos - 1;
        if (hScrollBar != null) x -= hScrollBar.value;
        if (x <= 0) HideCursor();
        else { SetCursor(x, focused - topItem); ShowCursor(); }
    }

    public override void HandleEvent(ref TEvent @event)
    {
        if (@event.What == Events.evMouseDown && @event.mouse.doubleClick)
        {
            @event.What = Events.evCommand;
            @event.message.command = Views.cmChangeDir;
            PutEvent(ref @event);
            ClearEvent(ref @event);
            return;
        }
        // SET-added incremental directory search is deferred (see header).
        int oldFocused = focused;
        base.HandleEvent(ref @event);
        if (oldFocused != focused)
        {
            incPos = 0;
            UpdateCursorPos();
        }
    }

    public virtual void NewDirectory(string str)
    {
        dir = str ?? string.Empty;
        var dirs = new TDirCollection();
        // .NET handles forward and back slashes interchangeably on
        // Windows; we keep the upstream backslash → forward conversion
        // for code paths that build display strings off `dir`.
        if (System.IO.Path.DirectorySeparatorChar == '\\')
            dir = dir.Replace('\\', '/');
        const string drives = "Drives";
        if (HasDriveLetters)
        {
            dirs.Insert(new TDirEntry(drives, drives));
            if (string.Equals(dir, drives, System.StringComparison.Ordinal))
                ShowDrives(dirs);
            else
                ShowDirs(dirs);
        }
        else
        {
            ShowDirs(dirs);
        }
        NewList(dirs);
        FocusItem(cur);
    }

    public virtual void NewList(TDirCollection aList)
    {
        _dirs = aList;
        SetRange(aList?.Count ?? 0);
        if (range > 0) FocusItem(0);
        DrawView();
    }

    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if ((aState & Views.sfFocused) != 0)
            Message(owner, Events.evCommand, Views.cmDirSelection,
                    enable ? this : null);
    }

    // -------------------------------------------------------------------
    // Tree builders
    // -------------------------------------------------------------------

    // Build the indented tree of ancestor directories plus the immediate sub-directories of `dir`.
    public virtual void ShowDirs(TDirCollection dirs)
    {
        const int indentSize = 2;
        int indent = indentSize;
        int lenSep = PathDir.Length;
        // The first node renders the drive (or '/') prefix.
        string drivePrefix = SkipDriveName(dir, out string drive);
        // Ensure trailing separator so the segment walker reaches the
        // deepest folder.
        if (drivePrefix.Length > 0 && drivePrefix[drivePrefix.Length - 1] != '/')
            drivePrefix += "/";
        dirs.Insert(new TDirEntry(PathDir + drive, drive, lenSep));

        // Walk the path one segment at a time (using '/' as the
        // separator after the dir-string normalisation in NewDirectory).
        string remaining = drivePrefix;
        string accumulated = drive;
        while (true)
        {
            int sep = remaining.IndexOf('/');
            if (sep < 0) break;
            string segment = remaining.Substring(0, sep);
            if (segment.Length > 0)
            {
                accumulated += segment + "/";
                string display = new string(' ', indent) + PathDir + segment;
                dirs.Insert(new TDirEntry(display, accumulated, indent + lenSep));
                indent += indentSize;
            }
            remaining = remaining.Substring(sep + 1);
        }
        cur = dirs.Count - 1;

        // Enumerate sub-directories of `accumulated` and append them as
        // children of the deepest node.
        string fsPath = accumulated.Replace('/', System.IO.Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(fsPath)) fsPath = "/";
        var children = new System.Collections.Generic.List<string>();
        try
        {
            foreach (string sub in System.IO.Directory.EnumerateDirectories(fsPath))
            {
                string name = System.IO.Path.GetFileName(sub);
                if (!string.IsNullOrEmpty(name) && name[0] != '.')
                    children.Add(name);
            }
        }
        catch (System.IO.IOException)               { }
        catch (System.UnauthorizedAccessException)  { }
        children.Sort(System.StringComparer.OrdinalIgnoreCase);

        bool isFirst = true;
        for (int i = 0; i < children.Count; i++)
        {
            string sep1 = isFirst ? FirstDir : MiddleDir;
            isFirst = false;
            string display = new string(' ', indent) + sep1 + children[i];
            string fullPath = accumulated + children[i] + "/";
            dirs.Insert(new TDirEntry(display, fullPath, indent + lenSep));
        }

        if (children.Count > 0)
        {
            int lastIdx = dirs.Count - 1;
            var lastEntry = dirs.At(lastIdx);
            string text = lastEntry.Text();
            // Replace the leading run of spaces+sep with LastDir.
            int sepStart = indent;
            if (text.Length >= sepStart + LastDir.Length)
            {
                string patched = text.Substring(0, sepStart) + LastDir
                               + text.Substring(sepStart + LastDir.Length);
                dirs.Items[lastIdx] = new TDirEntry(patched, lastEntry.Dir(),
                                                   lastEntry.Offset());
            }
        }
    }

    // Only meaningful on platforms with drive letters (Windows).
    // Enumerates A:..Z: and inserts each valid one.
    public virtual void ShowDrives(TDirCollection dirs)
    {
        if (!HasDriveLetters) return;
        var drives = new System.Collections.Generic.List<string>();
        foreach (var d in System.IO.DriveInfo.GetDrives())
        {
            string root = d.Name;            // e.g. "C:\"
            if (root.Length >= 1) drives.Add(root.Substring(0, 1).ToLowerInvariant());
        }
        drives.Sort(System.StringComparer.Ordinal);
        int lenStr = FirstDir.Length;
        bool isFirst = true;
        for (int i = 0; i < drives.Count; i++)
        {
            string letter = drives[i];
            string sepStr = (i == drives.Count - 1) ? LastDir
                           : (isFirst ? FirstDir : MiddleDir);
            isFirst = false;
            string display = sepStr + letter;
            string dirStr  = letter + ":" + System.IO.Path.DirectorySeparatorChar;
            dirs.Insert(new TDirEntry(display, dirStr, lenStr));
            // Track the entry that corresponds to the current drive.
            string cwd = System.IO.Directory.GetCurrentDirectory();
            if (cwd.Length > 0 &&
                char.ToLowerInvariant(cwd[0]).ToString() == letter)
                cur = dirs.Count - 1;
        }
        if (hScrollBar != null) hScrollBar.SetRange(0, 0);
        incPos = 0;
    }

    private static bool HasDriveLetters =>
        System.IO.Path.DirectorySeparatorChar == '\\';

    // Returns the path portion after the drive marker and outputs the drive string itself.
    private static string SkipDriveName(string path, out string drive)
    {
        if (string.IsNullOrEmpty(path))
        {
            drive = "/";
            return string.Empty;
        }
        if (HasDriveLetters && path.Length >= 3 && path[1] == ':')
        {
            drive = path.Substring(0, 3);   // "C:/"
            return path.Substring(3);
        }
        drive = "/";
        return path.Length > 0 && path[0] == '/' ? path.Substring(1) : path;
    }

    public override ushort DataSize() => 0;
    public override void GetData(ref object rec) { rec = null; }
    public override void SetData(object rec) { }

    public override object Read(Ipstream isStream) { base.Read(isStream); return this; }
    public override void Write(Opstream os) { base.Write(os); }
    public new static TStreamable Build() => new TDirListBox(StreamableInit.streamableInit);
    public static readonly TStreamableClass StreamableClassTDirListBox =
        new TStreamableClass("TDirListBox", () => new TDirListBox(StreamableInit.streamableInit), 0);

    protected TDirListBox(StreamableInit init) : base(init) { }
}
