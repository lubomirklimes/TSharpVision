using TSharpVision.Constants;

namespace TSharpVision;

// Tree-style listbox for directories. Holds a TDirCollection of TDirEntry
// records (the upstream `list()` accessor) instead of TListBox's
// TStringCollection.
/// <summary>Directory browser displaying ancestors and immediate subdirectories as an indented tree.</summary>
public class TDirListBox : TListBox
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TDirListBox";

    // Single BMP state markers: the current path is expanded; its immediate
    // children are collapsed. Each default prefix is one glyph and one space;
    // nesting indentation is separate, and filename offsets follow prefix length.
    /// <summary>Branch prefix used for ancestor path entries.</summary>
    public static string PathDir   = "\u229F ";
    /// <summary>Branch prefix used for the first directory entry.</summary>
    public static string FirstDir  = "\u229E ";
    /// <summary>Branch prefix used for intermediate directory entries.</summary>
    public static string MiddleDir = "\u229E ";
    /// <summary>Branch prefix used for the final directory entry.</summary>
    public static string LastDir   = "\u229E ";
    /// <summary>Characters used to recognize directory-tree decoration rather than filename text.</summary>
    public static string Graphics  = "\u229E\u229F";

    // Current path (full directory name), index of the entry to render
    // with sfSelected, and the incremental-search cursor
    /// <summary>Current directory path used to populate the tree.</summary>
    public string dir = string.Empty;
    /// <summary>Index of the directory entry displayed as selected, distinct from keyboard focus.</summary>
    public int    cur;
    /// <summary>Character position used to place the incremental-search caret in the focused entry.</summary>
    public int    incPos;

    // The actual TDirCollection lives here (the base TListBox.items field
    // remains null — we override every accessor that touches it).
    private TDirCollection? _dirs;
    private int _longestRow;
    private bool _updatingHorizontal;

    /// <summary>Creates a directory tree at owner-relative cell bounds with separate vertical and horizontal scrollbars.</summary>
    public TDirListBox(TRect bounds, TScrollBar aVScrollBar, TScrollBar aHScrollBar)
        : base(bounds, 1, aVScrollBar)
    {
        // TListBox supplies the vertical association; add the horizontal one.
        hScrollBar = aHScrollBar;
        cur = 0;
        incPos = 0;
        HideCursor();
        UpdateHorizontalScrollBar();
    }

    /// <inheritdoc />
    public override string GetText(int item, int maxChars)
    {
        if (_dirs == null || item < 0 || item >= _dirs.Count) return string.Empty;
        string s = _dirs.At(item).Text() ?? string.Empty;
        if (s.Length > maxChars) s = s.Substring(0, maxChars);
        return s;
    }

    /// <inheritdoc />
    public override bool IsSelected(int item) => item == cur;

    /// <summary>Returns the current directory-entry collection, which may be null before loading.</summary>
    public new TDirCollection? List() => _dirs;

    /// <summary>Keeps the directory-selection caret hidden; focus is indicated by the selected row.</summary>
    public void UpdateCursorPos() => HideCursor();

    /// <inheritdoc />
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
        bool horizontalChanged = @event.What == Events.evBroadcast
            && @event.message.command == Views.cmScrollBarChanged
            && ReferenceEquals(@event.message.infoPtr, hScrollBar);
        int oldScroll = hScrollBar?.value ?? 0;
        int oldFocused = focused;
        base.HandleEvent(ref @event);
        if (oldFocused != focused)
        {
            incPos = 0;
            UpdateCursorPos();
        }
        else if (horizontalChanged || oldScroll != (hScrollBar?.value ?? 0)) UpdateCursorPos();
    }

    /// <summary>Loads a directory tree for the supplied path and focuses its current-directory entry.</summary>
    public virtual void NewDirectory(string str)
    {
        dir = str ?? string.Empty;
        var dirs = new TDirCollection();
        // .NET handles forward and back slashes interchangeably on
        // Windows; we keep the upstream backslash → forward conversion
        // for code paths that build display strings off `dir`.
        if (System.IO.Path.DirectorySeparatorChar == '\\'
            || (dir.Length >= 2 && dir[0] == '\\' && dir[1] == '\\'))
        {
            dir = dir.Replace('\\', '/');
        }
        const string drives = "Drives";
        if (HasDriveLetters && !IsUncPath(dir))
        {
            dirs.Insert(new TDirEntry(TSharpVisionIntl.Get("DirList_Drives", "Drives"), drives));
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
        UpdateCursorPos();
    }

    /// <summary>References a new directory collection, resets the range and focus, and redraws the list.</summary>
    public virtual void NewList(TDirCollection aList)
    {
        _dirs = aList;
        _longestRow = 0;
        if (aList != null)
            for (int i = 0; i < aList.Count; i++) _longestRow = Math.Max(_longestRow, aList.At(i).Text().Length);
        SetRange(aList?.Count ?? 0);
        if (range > 0) FocusItem(0);
        UpdateHorizontalScrollBar(reset: true);
        DrawView();
        UpdateCursorPos();
    }

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        // This is a selection-only tree; do not let selection/redraw restore a text caret.
        // Other state flags, including focus and row selection, retain their normal behavior.
        if (enable) aState = (ushort)(aState & ~Views.sfCursorVis);
        base.SetState(aState, enable);
        if ((aState & (Views.sfActive | Views.sfSelected | Views.sfVisible)) != 0) UpdateHorizontalScrollBar();
        if ((aState & Views.sfFocused) != 0)
            Message(owner, Events.evCommand, Views.cmDirSelection,
                    enable ? this : null);
    }

    /// <inheritdoc />
    public override void ChangeBounds(TRect bounds)
    {
        base.ChangeBounds(bounds);
        UpdateHorizontalScrollBar();
        DrawView();
        UpdateCursorPos();
    }

    private void UpdateHorizontalScrollBar(bool reset = false)
    {
        var bar = hScrollBar;
        if (bar == null || _updatingHorizontal) return;
        _updatingHorizontal = true;
        try
        {
            // Also normalize restored scrollbars that retain the old growth flags.
            // This viewport is the sole authority for horizontal-bar geometry.
            bar.growMode = 0;
            // TListViewer reserves cell zero as its leading selection margin.
            int viewport = Math.Max(0, size.x - 1);
            if (owner != null && ReferenceEquals(bar.owner, owner))
            {
                var bounds = new TRect(origin.x, origin.y + size.y, origin.x + size.x, origin.y + size.y + 1);
                var old = bar.GetBounds();
                if (old.a != bounds.a || old.b != bounds.b) bar.ChangeBounds(bounds);
            }
            int maximum = Math.Max(0, _longestRow - viewport);
            bar.SetParams(reset ? 0 : bar.value, 0, maximum, Math.Max(1, viewport), 1);
            // Keep overflow navigation available when focus moves to a button/input.
            if (maximum > 0 && GetState((ushort)(Views.sfActive | Views.sfVisible))) bar.Show();
            else bar.Hide();
        }
        finally { _updatingHorizontal = false; }
    }

    // -------------------------------------------------------------------
    // Tree builders
    // -------------------------------------------------------------------

    /// <summary>Appends the current path's ancestors and immediate subdirectories to the supplied display collection.</summary>
    public virtual void ShowDirs(TDirCollection dirs)
    {
        const int indentSize = 2;
        int indent = indentSize;
        int lenSep = PathDir.Length;
        // The first node renders the drive (or '/') prefix.
        string drivePrefix = SplitRootForDisplay(dir, out string drive);
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

    /// <summary>Appends available drive-letter roots on platforms that support them; otherwise does nothing.</summary>
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

    /// <summary>Separates the display root from the remaining path, including drive-letter and UNC roots.</summary>
    public static string SplitRootForDisplay(string path, out string drive)
    {
        if (string.IsNullOrEmpty(path))
        {
            drive = "/";
            return string.Empty;
        }
        if (IsUncPath(path))
        {
            int serverStart = 2;
            int serverEnd = path.IndexOf('/', serverStart);
            if (serverEnd < 0)
            {
                drive = path.EndsWith("/") ? path : path + "/";
                return string.Empty;
            }

            int shareStart = serverEnd + 1;
            int shareEnd = path.IndexOf('/', shareStart);
            if (shareEnd < 0)
            {
                drive = path.EndsWith("/") ? path : path + "/";
                return string.Empty;
            }

            // Treat //server/share/ as a single synthetic root. This avoids
            // rendering an empty segment for the doubled leading separator.
            drive = path.Substring(0, shareEnd + 1);
            return path.Substring(shareEnd + 1);
        }
        if (HasDriveLetters && path.Length >= 3 && path[1] == ':')
        {
            drive = path.Substring(0, 3);   // "C:/"
            return path.Substring(3);
        }
        drive = "/";
        return path.Length > 0 && path[0] == '/' ? path.Substring(1) : path;
    }

    private static bool IsUncPath(string path)
        => !string.IsNullOrEmpty(path)
        && path.Length >= 2
        && path[0] == '/'
        && path[1] == '/';

    /// <inheritdoc />
    public override ushort DataSize() => 0;
    /// <inheritdoc />
    public override void GetData(ref object rec) { }
    /// <inheritdoc />
    public override void SetData(object rec) { }

    /// <inheritdoc />
    public override object Read(Ipstream isStream) { base.Read(isStream); return this; }
    /// <inheritdoc />
    public override void Write(Opstream os) { base.Write(os); }
    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TDirListBox(StreamableInit.streamableInit);
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTDirListBox =
        new TStreamableClass("TDirListBox", () => new TDirListBox(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TDirListBox(StreamableInit init) : base(init) { }
}
