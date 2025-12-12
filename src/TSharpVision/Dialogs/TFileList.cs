using System.IO;
using TSharpVision.Constants;
namespace TSharpVision;

// Stream Read/Write/Build deferred. The Win32/POSIX/DJGPP per-platform
// readDirectory branches collapse onto .NET's portable
// System.IO.Directory enumeration; the per-platform attribute-extract
// routine becomes BuildSearchRec.
public class TFileList : TSortedListBox
{
    public new static readonly string Name = "TFileList";

    // Used by GetKey so the shift-state hack (which biases lookup towards
    // FA_DIREC) survives without TGKey::getShiftState.
    public bool ShiftSearchAsDir;

    public TFileList(TRect bounds, TScrollBar aScrollBar)
        : base(bounds, 2, aScrollBar)
    {
        // Upstream switches to 1 column when LFNs are available — that's
        // always true on .NET targets.
        numCols = 1;
    }

    public override void FocusItem(int item)
    {
        base.FocusItem(item);
        if (sortedItems != null && item >= 0 && item < sortedItems.Count
            && owner is TGroup g)
        {
            var rec = (TSearchRec)sortedItems.At(item);
            BroadcastFile(g, Views.cmFileFocused, rec);
        }
    }

    public override void SelectItem(int item)
    {
        if (sortedItems != null && item >= 0 && item < sortedItems.Count
            && owner is TGroup g)
        {
            var rec = (TSearchRec)sortedItems.At(item);
            BroadcastFile(g, Views.cmFileDoubleClicked, rec);
        }
    }

    public override void GetData(ref object rec) { }
    public override void SetData(object rec) { }
    public override ushort DataSize() => 0;

    public override object GetKey(string s)
    {
        var sR = new TSearchRec
        {
            attr = (byte)(ShiftSearchAsDir || (s != null && s.Length > 0 && s[0] == '.')
                ? FileAttr.faDirec : 0),
            name = s ?? string.Empty,
        };
        return sR;
    }

    protected override string GetItemText(object item)
    {
        if (item is TSearchRec f)
        {
            string n = f.name ?? string.Empty;
            if ((f.attr & FileAttr.faDirec) != 0)
                n += Path.DirectorySeparatorChar;
            return n;
        }
        return string.Empty;
    }

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What != Events.evKeyDown) return;

        if (@event.keyDown.keyCode == Keys.kbLeft)
        {
            ClearEvent(ref @event);
            var trec = new TSearchRec { attr = FileAttr.faDirec, name = ".." };
            if (owner is TGroup g)
            {
                BroadcastFile(g, Views.cmFileFocused, trec);
                BroadcastFile(g, Views.cmFileDoubleClicked, trec);
            }
        }
        else if (@event.keyDown.keyCode == Keys.kbRight)
        {
            ClearEvent(ref @event);
            if (sortedItems != null && focused >= 0 && focused < sortedItems.Count)
            {
                var tp = (TSearchRec)sortedItems.At(focused);
                if ((tp.attr & FileAttr.faDirec) != 0 && owner is TGroup g)
                    BroadcastFile(g, Views.cmFileDoubleClicked, tp);
            }
        }
    }

    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if (aState == Views.sfFocused && enable
            && sortedItems != null && focused >= 0 && focused < sortedItems.Count
            && owner is TGroup g)
        {
            BroadcastFile(g, Views.cmFileFocused, (TSearchRec)sortedItems.At(focused));
        }
    }

    public virtual void ReadDirectory(string dir, string wildCard)
        => ReadDirectory((dir ?? string.Empty) + (wildCard ?? string.Empty));

    // Walks the directory using System.IO.Directory: dirs first, then
    // files matching the wildcard. The fcolHide* and root-skip-".."
    // tweaks from upstream are ported.
    public virtual void ReadDirectory(string path)
    {
        var fc = new TFileCollection();
        string dirPart;
        string pattern;

        try
        {
            if (string.IsNullOrEmpty(path))
            {
                dirPart = ".";
                pattern = "*";
            }
            else
            {
                int sep = -1;
                for (int i = path.Length - 1; i >= 0; i--)
                {
                    char c = path[i];
                    if (c == '/' || c == '\\') { sep = i; break; }
                }
                if (sep < 0)
                {
                    dirPart = ".";
                    pattern = path;
                }
                else
                {
                    dirPart = path.Substring(0, sep);
                    if (dirPart.Length == 0) dirPart = Path.DirectorySeparatorChar.ToString();
                    pattern = path.Substring(sep + 1);
                }
            }
            if (string.IsNullOrEmpty(pattern)) pattern = "*";

            bool removeParent = false;
            try
            {
                string full = Path.GetFullPath(dirPart);
                // Drive root ("X:\") on Windows or "/" on POSIX → skip "..".
                if (Path.GetPathRoot(full) is string root
                    && !string.IsNullOrEmpty(root)
                    && string.Equals(full.TrimEnd(Path.DirectorySeparatorChar,
                                                  Path.AltDirectorySeparatorChar),
                                     root.TrimEnd(Path.DirectorySeparatorChar,
                                                  Path.AltDirectorySeparatorChar),
                                     StringComparison.OrdinalIgnoreCase))
                    removeParent = true;
            }
            catch { /* invalid path: leave parent visible */ }

            if (Directory.Exists(dirPart))
            {
                // Directories first (".", ".." and real subdirs), unfiltered
                // by the wildcard — matches upstream DOS/Win32/POSIX paths.
                foreach (var d in EnumerateDirs(dirPart))
                {
                    string name = Path.GetFileName(d);
                    if (name == ".") continue;
                    if (removeParent && name == "..") continue;
                    if (ExcludeSpecial(name)) continue;
                    fc.Insert(BuildSearchRec(d, name, true));
                }
                if (!removeParent)
                {
                    // System.IO never surfaces ".." in directory listings;
                    // always synthesise it for non-root directories.
                    fc.Insert(new TSearchRec
                    {
                        name = "..",
                        attr = FileAttr.faDirec,
                        size = 0,
                        time = 0,
                    });
                }

                foreach (var f in EnumerateFiles(dirPart, pattern))
                {
                    string name = Path.GetFileName(f);
                    if (ExcludeSpecial(name)) continue;
                    fc.Insert(BuildSearchRec(f, name, false));
                }
            }
        }
        catch
        {
            // Safe fallback: leave fc empty — unknown/invalid path.
        }
        NewList(fc);
        if (owner is TGroup g)
        {
            if (fc.Count > 0)
                BroadcastFile(g, Views.cmFileFocused, (TSearchRec)fc.At(0));
            else
                BroadcastFile(g, Views.cmFileFocused, new TSearchRec());
        }
    }

    protected static bool ExcludeSpecial(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        uint o = TFileCollection.SortOptions;
        int len = name.Length;
        if ((o & FileCollectionOptions.fcolHideEndTilde) != 0 && name[len - 1] == '~')
            return true;
        if ((o & FileCollectionOptions.fcolHideEndBkp) != 0 && len > 4
            && string.Equals(name.Substring(len - 4), ".bkp",
                              StringComparison.OrdinalIgnoreCase))
            return true;
        if ((o & FileCollectionOptions.fcolHideStartDot) != 0 && name[0] == '.')
            return true;
        return false;
    }

    private static IEnumerable<string> EnumerateDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch { return Array.Empty<string>(); }
    }

    // Enumerates files matching a pattern that may contain multiple masks
    // separated by ';' or ','.  Each sub-mask is tried independently so that
    // e.g. "*.txt;*.cs" works correctly.  Invalid or unsupported patterns are
    // caught and treated as empty matches, so the caller never throws.
    private static IEnumerable<string> EnumerateFiles(string dir, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) pattern = "*";

        // Split multi-mask  ("*.txt;*.cs"  or  "*.txt,*.cs").
        string[] masks = pattern.Split(new[] { ';', ',' },
                                       StringSplitOptions.RemoveEmptyEntries);

        if (masks.Length <= 1)
        {
            string m = pattern.Trim();
            if (string.IsNullOrEmpty(m)) m = "*";
            try { return Directory.EnumerateFiles(dir, m); }
            catch { return Array.Empty<string>(); }
        }

        // Multiple masks — gather all matches and deduplicate by file name.
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (string raw in masks)
        {
            string m = raw.Trim();
            if (string.IsNullOrEmpty(m)) continue;
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir, m))
                    if (seen.Add(Path.GetFileName(f)))
                        result.Add(f);
            }
            catch { /* ignore individual mask failures */ }
        }
        return result;
    }

    private static TSearchRec BuildSearchRec(string fullPath, string name, bool isDir)
    {
        var rec = new TSearchRec { name = name };
        try
        {
            var info = isDir
                ? (FileSystemInfo)new DirectoryInfo(fullPath)
                : new FileInfo(fullPath);
            rec.attr = isDir ? FileAttr.faDirec : FileAttr.faArch;
            if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                rec.attr |= FileAttr.faReadOnly;
            if ((info.Attributes & FileAttributes.Hidden) != 0)
                rec.attr |= FileAttr.faHidden;
            if ((info.Attributes & FileAttributes.System) != 0)
                rec.attr |= FileAttr.faSystem;
            rec.size = isDir ? 0 : ((FileInfo)info).Length;
            rec.time = new DateTimeOffset(info.LastWriteTime).ToUnixTimeSeconds();
        }
        catch
        {
            rec.attr = isDir ? FileAttr.faDirec : (byte)0;
        }
        return rec;
    }

    // Helper: synchronous broadcast (matches the existing `Message`
    // extension used by other dialogs in the port).
    private static void BroadcastFile(TGroup group, ushort cmd, TSearchRec rec)
    {
        var ev = new TEvent { What = Events.evBroadcast };
        ev.message.command = cmd;
        ev.message.infoPtr = rec;
        group.HandleEvent(ref ev);
    }

    protected TFileList(StreamableInit init) : base(init) { }
    public override object Read(Ipstream isStream) { base.Read(isStream); return this; }
    public override void Write(Opstream os) { base.Write(os); }
    public new static TStreamable Build() => new TFileList(StreamableInit.streamableInit);
    public static readonly TStreamableClass StreamableClassTFileList =
        new TStreamableClass("TFileList", () => new TFileList(StreamableInit.streamableInit), 0);
}
