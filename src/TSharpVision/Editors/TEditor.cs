using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using TSharpVision.Constants;
using TSharpVision.Text;

namespace TSharpVision;

/// <summary>Editable UTF-16 text view with selection, undo, searching, clipboard operations, and optional scrollbars and status indicator.</summary>
public class TEditor : TView
{
    /// <summary>Shared compatibility buffer for clipboard text; the clipboard editor handles normal copy and paste.</summary>
    public static char[] clipboardBuffer = Array.Empty<char>();
    /// <summary>Shared editor storing the in-process clipboard selection.</summary>
    public static TEditor? clipboard;
    /// <summary>Shared search, replacement, and file-backup option bits.</summary>
    public static ushort editorFlags = (ushort)(Views.efBackupFiles | Views.efPromptOnReplace);
    /// <summary>Shared null-terminated UTF-16 search pattern buffer.</summary>
    public static char[] findStr = new char[Views.maxFindStrLen];
    /// <summary>Shared null-terminated UTF-16 replacement text buffer.</summary>
    public static char[] replaceStr = new char[Views.maxReplaceStrLen];
    /// <summary>Distance between visual tab stops in columns; column calculations treat zero as one.</summary>
    public static uint tabSize = 8;

    // editorDialog callback — upstream `TEditorDialog`. Default returns
    // cmCancel, mirroring `defEditorDialog`
    /// <summary>Callback for editor prompts; receives a dialog identifier and payload and returns a response command.</summary>
    public delegate ushort TEditorDialog(int dialog, object? info);
    /// <summary>Shared prompt callback; the default cancels every prompt.</summary>
    public static TEditorDialog editorDialog =
        (int _dlg, object? _info) => Views.cmCancel;

    /// <summary>Associated horizontal scrollbar; ownership remains with its containing group.</summary>
    public TScrollBar? hScrollBar;
    /// <summary>Associated vertical scrollbar; ownership remains with its containing group.</summary>
    public TScrollBar? vScrollBar;
    /// <summary>Associated caret-position and modification indicator, owned by its containing group.</summary>
    public TIndicator? indicator;
    /// <summary>Physical UTF-16 storage including the gap; use BufChar for logical document indexing.</summary>
    public char[]? buffer;
    /// <summary>Capacity of the physical buffer in UTF-16 code units.</summary>
    public uint bufSize;
    /// <summary>Logical document length in UTF-16 code units, excluding the gap.</summary>
    public uint bufLen;
    /// <summary>Number of unused code-unit slots between the physical text segments.</summary>
    public uint gapLen;
    /// <summary>Inclusive logical UTF-16 offset of the selection.</summary>
    public uint selStart;
    /// <summary>Exclusive logical UTF-16 offset of the selection.</summary>
    public uint selEnd;
    /// <summary>Logical UTF-16 insertion offset and start of the buffer gap.</summary>
    public uint curPtr;
    /// <summary>Zero-based visual caret column and line; tabs expand to configured column stops.</summary>
    public TPoint curPos;
    /// <summary>Viewport origin in visual columns and document lines.</summary>
    public TPoint delta;
    /// <summary>Scrollable content extent in columns and lines.</summary>
    public TPoint limit;
    /// <summary>Document line associated with the cached drawing offset.</summary>
    public int drawLine;
    /// <summary>Logical UTF-16 offset cached for line drawing.</summary>
    public uint drawPtr;
    /// <summary>
    /// UTF-16 code units removed by the undo group that typing or deleting can still extend; zero once a caret move,
    /// Undo or Redo closes that group. Informational: undo text is kept in the editor's history, not in the buffer gap.
    /// </summary>
    public uint delCount;
    /// <summary>
    /// UTF-16 code units inserted by the undo group that typing or deleting can still extend; zero once a caret move,
    /// Undo or Redo closes that group. Informational, like <see cref="delCount"/>.
    /// </summary>
    public uint insCount;
    /// <summary>Whether editor initialization and buffer setup succeeded.</summary>
    public bool isValid;
    /// <summary>
    /// Whether edits are recorded for Undo. An edit made while this is false cannot be undone and clears the history,
    /// because the recorded offsets no longer describe the text.
    /// </summary>
    public bool canUndo = true;
    /// <summary>
    /// Whether the text differs from the loaded or last saved text. Edits set it; <see cref="MarkUnmodified"/>,
    /// loading and saving clear it; Undo and Redo set it from the history's saved position. Writing it directly is
    /// honoured until the next Undo or Redo, except that a document marked modified while at the saved position stays
    /// modified until it is marked unmodified again.
    /// </summary>
    public bool modified;
    /// <summary>Whether caret movement extends the current selection.</summary>
    public bool selecting;
    /// <summary>Whether typed characters replace existing characters instead of inserting before them.</summary>
    public bool overwrite;
    /// <summary>Whether inserting a new line copies the preceding line's leading whitespace.</summary>
    public bool autoIndent;
    /// <summary>Pending uf-prefixed redraw and status-update requests.</summary>
    public byte updateFlags;
    /// <summary>Nesting count deferring pending redraw and status updates.</summary>
    public int lockCount;
    /// <summary>Keyboard-prefix state: zero for normal input, one for Ctrl+Q, and two for Ctrl+K.</summary>
    public int keyState;
    /// <summary>Additional text describing the most recent editor or file error.</summary>
    public string errorInfo = string.Empty;

    /// <summary>Default maximum number of undo groups kept by an editor.</summary>
    public const int DefaultUndoGroupLimit = 1000;
    /// <summary>Default maximum number of UTF-16 code units of removed and inserted text kept by an editor's undo history.</summary>
    public const int DefaultUndoTextLimit = 4 * 1024 * 1024;

    private EditorUndoHistory? undoHistory;
    private int longestLine;
    private bool explicitlyModified;
    private bool replayingUndo;

    private const string CpEditor = "\x06\x07";
    private static readonly TPalette Palette = new TPalette(CpEditor, CpEditor.Length);
    private const int WheelStep = 3;

    // keyMap[0]=firstKeys, keyMap[1]=quickKeys (Ctrl-Q), keyMap[2]=blockKeys (Ctrl-K)
    private const int KeyStateQuick = 1; // Ctrl-Q prefix active
    private const int KeyStateBlock = 2; // Ctrl-K prefix active

    /// <summary>Creates an empty editor at owner-relative cell bounds with a code-unit buffer capacity and optional associated controls.</summary>
    public TEditor(TRect bounds, TScrollBar? aHScrollBar, TScrollBar? aVScrollBar,
                   TIndicator? aIndicator, uint aBufSize)
        : base(bounds)
    {
        hScrollBar = aHScrollBar;
        vScrollBar = aVScrollBar;
        indicator  = aIndicator;
        bufSize    = aBufSize;
        growMode   = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        options    = (ushort)(options | Views.ofSelectable);
        eventMask  = (ushort)(Events.evMouseDown | Events.evKeyDown
                              | Events.evCommand | Events.evBroadcast
                              | Events.evMouseWheel);
        ShowCursor();
        InitBuffer();
        if (buffer != null)
            isValid = true;
        else
        {
            bufSize = 0;
            isValid = false;
        }
        SetBufLen(0);
    }

    /// <inheritdoc />
    public override void ShutDown()
    {
        DoneBuffer();
        base.ShutDown();
    }

    /// <inheritdoc />
    public override void ChangeBounds(TRect bounds)
    {
        SetBounds(bounds);
        delta.x = Math.Max(0, Math.Min(delta.x, limit.x - size.x));
        delta.y = Math.Max(0, Math.Min(delta.y, limit.y - size.y));
        Update(Views.ufView);
    }

    /// <summary>Returns a UTF-16 code unit at a valid logical document offset, skipping the storage gap.</summary>
    public char BufChar(uint p)
    {
        if (p >= curPtr) p += gapLen;
        return Buf[p];
    }

    /// <summary>Converts a logical document offset to a physical buffer index across the gap.</summary>
    public uint BufPtr(uint p) => p < curPtr ? p : p + gapLen;

    /// <summary>
    /// Computes the visual column of <paramref name="target"/> counted from the logical origin <paramref name="p"/>
    /// (normally a line start). Grapheme clusters are laid out by <see cref="TSharpVision.Text.TextLayout"/> with the
    /// default width profile: a CJK character or an emoji takes two columns, a cluster of only combining or format
    /// characters one, and tabs expand to <see cref="tabSize"/> stops counted from <paramref name="p"/>. A target
    /// inside a cluster gives the cluster's column. Under the <see cref="TSharpVision.Text.TextLayout.LegacyCellWidthSwitch"/>
    /// switch every UTF-16 code unit is one column, as in TSharpVision 1.2.
    /// </summary>
    public int CharPos(uint p, uint target)
    {
        p = Math.Min(p, bufLen);
        target = Math.Min(target, bufLen);
        if (target <= p) return 0;
        GraphemeLine line = LineForOrigin(p);
        uint end = LineEndBefore(line, target);
        int column = ColumnAt(line, end);
        // Past the line end (an unusual call): every code unit, the LF included, is one column, tabs expand.
        if (end < target) column = CountColumns(column, end, target);
        return column;
    }

    /// <summary>
    /// Maps a visual column on the line starting at the logical origin <paramref name="p"/> to the start of the
    /// grapheme cluster that covers it (a column on the second cell of a wide character or inside a tab gives the
    /// character's start), or to the line end for a column past it. Under the
    /// <see cref="TSharpVision.Text.TextLayout.LegacyCellWidthSwitch"/> switch columns are UTF-16 code units and the
    /// result is the last code-unit boundary not exceeding the column, as in TSharpVision 1.2.
    /// </summary>
    public uint CharPtr(uint p, int target)
    {
        p = Math.Min(p, bufLen);
        if (target <= 0) return p;
        GraphemeLine line = LineForOrigin(p);
        LayoutCluster cluster = ClusterAtColumn(line, target);
        if (cluster.AtLineEnd || cluster.Column >= target) return cluster.Start;
        // Legacy: a text cluster is one column per code unit, so the column falls on one of its units.
        return LegacyLayout && cluster.Kind == TextClusterKind.Text ? cluster.Start + (uint)(target - cluster.Column) : cluster.Start;
    }

    // Columns from `column` at `from` up to `to`, one per code unit with tab stops: the pre-Unicode rule, used only
    // for targets beyond the end of a line.
    private int CountColumns(int column, uint from, uint to)
    {
        long stop = LayoutTabSize, x = column;
        for (uint q = from; q < to; q++)
            x = BufChar(q) == '\t' ? (x / stop + 1) * stop : x + 1;
        return (int)Math.Min(x, int.MaxValue);
    }

    /// <summary>Returns the next LF offset at or after the supplied position, or the document length. The buffer is LF-only: a CR is an ordinary character.</summary>
    public uint LineEnd(uint p)
    {
        return FindLineBreak(Math.Min(p, bufLen), bufLen, false) ?? bufLen;
    }

    /// <summary>Returns the position after the last LF before the supplied offset, or zero.</summary>
    public uint LineStart(uint p)
    {
        uint? terminator = FindLineBreak(0, Math.Min(p, bufLen), true);
        return terminator.HasValue ? terminator.Value + 1 : 0;
    }

    // Search physical segments with the runtime span primitives; never scan gap contents.
    private uint? FindLineBreak(uint start, uint end, bool lastMatch)
    {
        while (start < end)
        {
            uint segmentStart = lastMatch && end > curPtr ? Math.Max(start, curPtr) : start;
            uint segmentEnd = !lastMatch && start < curPtr ? Math.Min(end, curPtr) : end;
            var segment = buffer.AsSpan((int)BufPtr(segmentStart), (int)(segmentEnd - segmentStart));
            int index = lastMatch ? segment.LastIndexOf('\n') : segment.IndexOf('\n');
            if (index >= 0) return segmentStart + (uint)index;
            if (lastMatch) end = segmentStart;
            else start = segmentEnd;
        }
        return null;
    }

    /// <summary>Advances one UTF-16 code unit without passing the document end.</summary>
    public uint NextChar(uint p)
    {
        return p < bufLen ? p + 1 : bufLen;
    }

    /// <summary>Moves back one UTF-16 code unit after clamping to the document, stopping at zero.</summary>
    public uint PrevChar(uint p)
    {
        uint offset = Math.Min(p, bufLen);
        return offset > 0 ? offset - 1 : 0;
    }

    /// <summary>Returns the position immediately after the next LF, bounded by the document end.</summary>
    public uint NextLine(uint p)
    {
        p = Math.Min(p, bufLen);
        GraphemeLine? line = FindCachedLine(p);
        return NextChar(line != null ? EnsureLineEnd(line, p) : LineEnd(p));
    }

    /// <summary>Returns the start of the line containing the code unit before the supplied offset.</summary>
    public uint PrevLine(uint p)
    {
        uint q = PrevChar(p);
        return FindCachedLine(q)?.Start ?? LineStart(q);
    }

    // ── user-perceived characters and visual columns ─────────────────────────
    //
    // Caret moves, deletions, overwrite and hit-testing step over extended grapheme clusters (UAX #29, through .NET's
    // StringInfo), so a surrogate pair, a base with its combining marks, an emoji with modifiers or ZWJ joins, or a
    // regional-indicator flag is one step. An LF is always a cluster of its own; a CR before it is a separate one-column
    // character. Positions stay UTF-16 offsets, and NextChar/PrevChar stay single code-unit steps.
    //
    // Boundaries are exact: they are those of forward segmentation from the start of the line. Segmentation may restart
    // at any boundary it produced, so a query segments forward from the nearest known boundary (a checkpoint) before
    // the offset, never from an arbitrary code unit. Checkpoints are recorded about GraphemeCheckpointSpacing units
    // apart while segmenting, for the lines used most recently (at least the visible ones); each line also remembers
    // how far it is known to contain no LF, and its end once seen. The text is read in chunks copied from the buffer
    // (the gap is skipped, never moved), and a chunk grows only while a single cluster fills it.
    //
    // Visual columns (TV13o): every checkpoint also holds the column at which it lies and the number of tabs before
    // it, so offset -> column, column -> offset, caret columns and the visible window of a line are found by walking
    // from the nearest checkpoint, never from the line start. Widths come from TextLayout with LayoutOptions (default
    // profile, tabSize stops, zero-width clusters one column so the caret can stop on them); under the legacy switch
    // every code unit is one column. A change of tabSize or of the profile drops the cache.
    //
    // Edits: checkpoints before an edit stay (a boundary depends only on the text before it and the scalar at it);
    // those in the unchanged text after it are shifted and become candidates. A walk from the last kept checkpoint
    // re-verifies them: once it produces a boundary exactly at a candidate, segmentation after it is the old one
    // (restart property), so every later candidate is a boundary whose column moved by the same amount - except across
    // a tab when that amount is not a multiple of the tab size, where one checkpoint interval is measured again. An
    // edit that adds or removes an LF keeps only the checkpoints before it. Load, clear and stream reads drop all.
    //
    // Cost: the first query in a line segments from the line start (or the last checkpoint) to the offset; later
    // queries in the same part of the line read at most about one checkpoint interval plus the cluster itself. A
    // cluster longer than the interval holds no checkpoints, so a query inside it reads it whole. An edit re-reads
    // about one interval on each side of it, unless it really changes boundaries far away (a regional-indicator run
    // whose pairing flips), which is then re-read up to where the text was known.

    private const int GraphemeMinCachedLines = 4;
    private const int GraphemeMaxCachedLines = 128;
    private const uint GraphemeCheckpointSpacing = 64;
    private const int GraphemeChunk = 256;
    private const uint GraphemeEditMargin = 4;
    private const int MaxLayoutTabSize = 1 << 20;

    private sealed class GraphemeLine
    {
        public uint Start;                                       // offset 0 or just after an LF
        public uint Clear;                                       // [Start, Clear) contains no LF
        public uint End = uint.MaxValue;                         // offset of the line's LF or the document end, once known
        public int EndColumn = -1;                               // column at End, once known
        public int EndTabs;
        public long LastUse;
        // Ascending verified boundaries (the first is Start), with their columns and the tabs before them.
        public uint[] Offsets = new uint[8];
        public int[] Columns = new int[8];
        public int[] Tabs = new int[8];
        public int Count = 1;

        public GraphemeLine(uint start)
        {
            Start = start;
            Clear = start;
            Offsets[0] = start;
        }

        public uint LastOffset => Offsets[Count - 1];

        public void Add(uint offset, int column, int tabs)
        {
            if (Count == Offsets.Length)
            {
                int size = Count * 2;
                Array.Resize(ref Offsets, size);
                Array.Resize(ref Columns, size);
                Array.Resize(ref Tabs, size);
            }
            Offsets[Count] = offset;
            Columns[Count] = column;
            Tabs[Count] = tabs;
            Count++;
        }

        // The last checkpoint at or before offset p (p >= Start).
        public int CheckpointAtOffset(uint p)
        {
            int lo = 0, hi = Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (Offsets[mid] <= p) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        // The last checkpoint at or before visual column c.
        public int CheckpointAtColumn(int c)
        {
            int lo = 0, hi = Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (Columns[mid] <= c) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        public void Shift(long delta)
        {
            Start = (uint)(Start + delta);
            Clear = (uint)(Clear + delta);
            if (End != uint.MaxValue) End = (uint)(End + delta);
            for (int i = 0; i < Count; i++) Offsets[i] = (uint)(Offsets[i] + delta);
        }
    }

    // A cluster found by column, or the line end (AtLineEnd, Start = end offset, Column = end column).
    private readonly record struct LayoutCluster(uint Start, uint End, int Column, int Width, TextClusterKind Kind, bool AtLineEnd);

    private GraphemeLine?[]? graphemeLines;
    private long graphemeClock;
    private uint graphemeTextLength;
    private uint graphemeTabSize;
    private TextWidthProfile graphemeProfile;
    // Candidate checkpoints during an edit (reused).
    private uint[] pendingOffsets = new uint[16];
    private int[] pendingColumns = new int[16];
    private int[] pendingTabs = new int[16];

    // Code units read by boundary searches, column walks, line scans and line measurement; test instrumentation only.
    internal long GraphemeUnitsRead;

    private static int LayoutTabSize => (int)Math.Clamp(tabSize, 1u, (uint)MaxLayoutTabSize);

    // Layout of editor text: the default profile, tabSize stops, zero-width clusters one column (a caret stop).
    private static TextLayoutOptions LayoutOptions => new() { TabSize = LayoutTabSize, ZeroWidthAsOne = true };

    private static bool LegacyLayout => TextLayout.DefaultProfile == TextWidthProfile.Legacy;

    // Drops every checkpoint: called when the whole text is replaced, and when the layout changes.
    private void InvalidateGraphemeCache()
    {
        if (graphemeLines != null) Array.Clear(graphemeLines);
        graphemeTextLength = bufLen;
        graphemeTabSize = tabSize;
        graphemeProfile = TextLayout.DefaultProfile;
    }

    private void ValidateGraphemeCache()
    {
        // Every edit goes through InsertBuffer or SetBufLen; a length change seen here is a text change made otherwise.
        if (graphemeTextLength != bufLen || graphemeTabSize != tabSize || graphemeProfile != TextLayout.DefaultProfile)
            InvalidateGraphemeCache();
        int capacity = Math.Clamp(size.y + 4, GraphemeMinCachedLines, GraphemeMaxCachedLines);
        if (graphemeLines == null)
            graphemeLines = new GraphemeLine?[capacity];
        else if (graphemeLines.Length != capacity)
        {
            var lines = new GraphemeLine?[capacity];
            int n = 0;
            foreach (GraphemeLine? line in graphemeLines)
                if (line != null && n < capacity) lines[n++] = line;
            graphemeLines = lines;
        }
    }

    private GraphemeLine Touch(GraphemeLine line)
    {
        line.LastUse = ++graphemeClock;
        return line;
    }

    // Caches a new line in a free slot or in place of the least recently used one.
    private GraphemeLine AddLine(GraphemeLine line)
    {
        GraphemeLine?[] lines = graphemeLines!;
        int slot = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i] == null) { slot = i; break; }
            if (lines[i]!.LastUse < lines[slot]!.LastUse) slot = i;
        }
        lines[slot] = line;
        return Touch(line);
    }

    // The cached line known to contain p (p may be its end), or null; never scans the text.
    private GraphemeLine? FindCachedLine(uint p)
    {
        ValidateGraphemeCache();
        GraphemeLine? line = null;
        foreach (GraphemeLine? candidate in graphemeLines!)
            if (candidate != null && candidate.Start <= p && (line == null || candidate.Start > line.Start))
                line = candidate;
        if (line == null) return null;
        bool contains = line.End != uint.MaxValue ? p <= line.End : p <= line.Clear;
        return contains ? Touch(line) : null;
    }

    // The line containing p (p <= bufLen; p may be the line's LF), cached.
    private GraphemeLine GraphemeLineOf(uint p)
    {
        if (FindCachedLine(p) is { } known) return known;
        GraphemeLine? line = null;
        foreach (GraphemeLine? candidate in graphemeLines!)
            if (candidate != null && candidate.Start <= p && (line == null || candidate.Start > line.Start))
                line = candidate;
        if (line != null && line.End == uint.MaxValue)
        {
            GraphemeUnitsRead += p - line.Clear;
            uint? lf = FindLineBreak(line.Clear, p, false);
            if (lf == null)
            {
                line.Clear = p;
                return Touch(line);
            }
            line.End = lf.Value;
            line.Clear = lf.Value;
        }
        uint start = LineStart(p);
        GraphemeUnitsRead += p - start;
        return AddLine(new GraphemeLine(start) { Clear = p });
    }

    // The cached line that starts at `start`, which must be a line start.
    private GraphemeLine LineFromStart(uint start)
    {
        ValidateGraphemeCache();
        foreach (GraphemeLine? candidate in graphemeLines!)
            if (candidate != null && candidate.Start == start) return Touch(candidate);
        return AddLine(new GraphemeLine(start));
    }

    private bool IsLineStart(uint p) => p == 0 || (p <= bufLen && BufChar(p - 1) == '\n');

    // The layout origin for CharPos/CharPtr: the cached line at a line start, otherwise an uncached line that starts
    // at p (columns and tab stops count from p, as they always have).
    private GraphemeLine LineForOrigin(uint p)
    {
        if (IsLineStart(p)) return LineFromStart(p);
        ValidateGraphemeCache();
        return new GraphemeLine(p);
    }

    private uint CachedLineStart(uint p) => GraphemeLineOf(Math.Min(p, bufLen)).Start;

    private uint CachedLineEnd(uint p)
    {
        p = Math.Min(p, bufLen);
        return EnsureLineEnd(GraphemeLineOf(p), p);
    }

    // The end of a line that contains p, scanning for it once.
    private uint EnsureLineEnd(GraphemeLine line, uint p)
    {
        if (line.End != uint.MaxValue) return line.End;
        uint from = Math.Max(line.Clear, p);
        uint end = FindLineBreak(from, bufLen, false) ?? bufLen;
        GraphemeUnitsRead += end - from;
        line.End = end;
        line.Clear = end;
        return end;
    }

    // `target` if the line has no LF before it, otherwise the line's LF.
    private uint LineEndBefore(GraphemeLine line, uint target)
    {
        if (line.End != uint.MaxValue) return Math.Min(line.End, target);
        if (target <= line.Clear) return target;
        GraphemeUnitsRead += target - line.Clear;
        uint? lf = FindLineBreak(line.Clear, target, false);
        if (lf == null)
        {
            line.Clear = target;
            return target;
        }
        line.End = lf.Value;
        line.Clear = lf.Value;
        return lf.Value;
    }

    // Column of offset p of the line (Start <= p <= its end). An offset inside a cluster gives the cluster's column;
    // under the legacy profile it counts the code units before it, as before.
    private int ColumnAt(GraphemeLine line, uint p)
    {
        if (line.EndColumn >= 0 && p >= line.End) return line.EndColumn;
        int index = line.CheckpointAtOffset(p);
        if (line.Offsets[index] == p) return line.Columns[index];
        Span<char> scratch = stackalloc char[GraphemeChunk];
        var walker = new ClusterWalker(this, line, index, scratch);
        try
        {
            while (walker.MoveNext())
                if (p < walker.End)
                    return p == walker.Start || !LegacyLayout
                        ? walker.ClusterColumn
                        : walker.ClusterColumn + (int)(p - walker.Start);
            return walker.Column;
        }
        finally { walker.Dispose(); }
    }

    // The cluster of the line that covers visual column c, or the line end.
    private LayoutCluster ClusterAtColumn(GraphemeLine line, int c)
    {
        if (line.EndColumn >= 0 && c >= line.EndColumn) return new LayoutCluster(line.End, line.End, line.EndColumn, 0, TextClusterKind.LineBreak, true);
        Span<char> scratch = stackalloc char[GraphemeChunk];
        var walker = new ClusterWalker(this, line, line.CheckpointAtColumn(c), scratch);
        try
        {
            while (walker.MoveNext())
                if (c < walker.ClusterColumn + walker.Width)
                    return new LayoutCluster(walker.Start, walker.End, walker.ClusterColumn, walker.Width, walker.Kind, false);
            return new LayoutCluster(walker.Position, walker.Position, walker.Column, 0, TextClusterKind.LineBreak, true);
        }
        finally { walker.Dispose(); }
    }

    // Columns of the whole line.
    private int LineWidth(GraphemeLine line) =>
        line.EndColumn >= 0 ? line.EndColumn : ClusterAtColumn(line, int.MaxValue).Column;

    // Start and end of the cluster containing code unit p (p < bufLen).
    private (uint Start, uint End) GraphemeAt(uint p)
    {
        if (BufChar(p) == '\n') return (p, p + 1);
        GraphemeLine line = GraphemeLineOf(p);
        Span<char> scratch = stackalloc char[GraphemeChunk];
        var walker = new ClusterWalker(this, line, line.CheckpointAtOffset(p), scratch);
        try
        {
            while (walker.MoveNext())
                if (p < walker.End) return (walker.Start, walker.End);
        }
        finally { walker.Dispose(); }
        return (p, p + 1);                                       // not reached: p lies before the line end
    }

    // Updates the cache after the text [edit, edit + removed) was replaced by `inserted` units; `structural` when an
    // LF was removed or inserted. Called by InsertBuffer once the new length is committed.
    private void AdjustGraphemeCache(uint edit, uint removed, uint inserted, bool structural)
    {
        if (graphemeLines == null || graphemeTabSize != tabSize || graphemeProfile != TextLayout.DefaultProfile
            || (long)graphemeTextLength + inserted - removed != bufLen)
        {
            InvalidateGraphemeCache();
            return;
        }
        graphemeTextLength = bufLen;
        long delta = (long)inserted - removed;
        uint oldEnd = edit + removed;
        GraphemeLine?[] lines = graphemeLines;
        for (int i = 0; i < lines.Length; i++)
        {
            GraphemeLine? line = lines[i];
            if (line == null) continue;
            if (line.Start > oldEnd) line.Shift(delta);                        // after the edit: unchanged text
            else if (line.Start > edit) lines[i] = null;                       // its LF was removed
            else if (line.End == uint.MaxValue || line.End >= edit)            // ends before the edit: unchanged
                ReviseLine(line, edit, oldEnd, delta, structural);
        }
    }

    private void ReviseLine(GraphemeLine line, uint edit, uint oldEnd, long delta, bool structural)
    {
        // Checkpoints before the edit stay; the first one, the line start, always does.
        int keep = 1;
        {
            int lo = 1, hi = line.Count;                                       // first index too close to the edit
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if ((long)line.Offsets[mid] + GraphemeEditMargin <= edit) lo = mid + 1; else hi = mid;
            }
            keep = lo;
        }

        // Checkpoints in the unchanged text after the edit, and the line end, become candidates.
        int pending = 0;
        bool pendingEnd = false;
        if (!structural)
        {
            int from = keep;
            while (from < line.Count && line.Offsets[from] < oldEnd) from++;
            for (int k = from; k < line.Count; k++)
                AddPending(ref pending, (uint)(line.Offsets[k] + delta), line.Columns[k], line.Tabs[k]);
            if (line.EndColumn >= 0)
            {
                AddPending(ref pending, (uint)(line.End + delta), line.EndColumn, line.EndTabs);
                pendingEnd = true;
            }
        }

        line.Count = keep;
        line.EndColumn = -1;
        if (structural || line.End == uint.MaxValue) line.End = uint.MaxValue;
        else line.End = (uint)(line.End + delta);                               // the LF moved with the text after it
        line.Clear = !structural && line.Clear >= oldEnd ? (uint)(line.Clear + delta) : Math.Min(line.Clear, edit);

        if (pending == 0) return;
        Resynchronize(line, pending, pendingEnd);
    }

    private void AddPending(ref int count, uint offset, int column, int tabs)
    {
        if (count == pendingOffsets.Length)
        {
            Array.Resize(ref pendingOffsets, count * 2);
            Array.Resize(ref pendingColumns, count * 2);
            Array.Resize(ref pendingTabs, count * 2);
        }
        pendingOffsets[count] = offset;
        pendingColumns[count] = column;
        pendingTabs[count] = tabs;
        count++;
    }

    // Walks from the last kept checkpoint until a produced boundary equals a candidate, then restores the candidates
    // from there on with their columns corrected.
    private void Resynchronize(GraphemeLine line, int pending, bool pendingEnd)
    {
        Span<char> scratch = stackalloc char[GraphemeChunk];
        int j = 0;
        int column = 0, tabs = 0;
        bool found = false;
        var walker = new ClusterWalker(this, line, line.Count - 1, scratch);
        try
        {
            while (walker.MoveNext())
            {
                while (j < pending && pendingOffsets[j] < walker.End) j++;
                if (j == pending) break;
                if (pendingOffsets[j] == walker.End)
                {
                    column = walker.Column;
                    tabs = walker.Tabs;
                    found = true;
                    break;
                }
            }
        }
        finally { walker.Dispose(); }
        if (!found) return;

        int stop = LayoutTabSize;
        int dColumn = column - pendingColumns[j];
        int dTabs = tabs - pendingTabs[j];
        int tabsAtShift = pendingTabs[j];
        for (int k = j; k < pending; k++)
        {
            bool isEnd = pendingEnd && k == pending - 1;
            if (dColumn % stop != 0 && pendingTabs[k] != tabsAtShift)
            {
                // A tab lies between the previous candidate (now the last checkpoint) and this one: the shift after it
                // is a multiple of the tab size; measure it on this interval.
                var again = new ClusterWalker(this, line, line.Count - 1, scratch);
                bool exact;
                int measured;
                try
                {
                    while (again.MoveNext() && again.End < pendingOffsets[k]) { }
                    exact = again.Position == pendingOffsets[k];
                    measured = again.Column;
                }
                finally { again.Dispose(); }
                if (!exact) return;                                             // not expected; keep what is verified
                dColumn = measured - pendingColumns[k];
                tabsAtShift = pendingTabs[k];
            }
            int newColumn = pendingColumns[k] + dColumn, newTabs = pendingTabs[k] + dTabs;
            if (isEnd)
            {
                line.End = pendingOffsets[k];
                line.EndColumn = newColumn;
                line.EndTabs = newTabs;
                line.Clear = Math.Max(line.Clear, line.End);
            }
            else if (pendingOffsets[k] > line.LastOffset)
                line.Add(pendingOffsets[k], newColumn, newTabs);
        }
    }

    // Forward walk over the clusters of a line from one of its checkpoints, with their columns. Reads the text in
    // chunks copied from the buffer, never past the line's LF; records checkpoints and the line end as it goes.
    private ref struct ClusterWalker
    {
        private readonly TEditor _editor;
        private readonly GraphemeLine _line;
        private readonly TextLayoutOptions _options;
        private readonly TextWidthProfile _profile;
        private Span<char> _chunk;
        private char[]? _rented;
        private uint _at;                                        // offset of _chunk[0], a verified boundary
        private int _length;                                     // units of the chunk in use
        private int _next;                                       // start of the next cluster in the chunk
        private bool _lineEnds;                                  // the chunk reaches the line end
        private bool _loaded;
        private int _textStart, _textLength;

        public int Column;                                       // column after the current cluster
        public int Tabs;                                         // tabs up to the current cluster's end
        public uint Start, End;                                  // the current cluster
        public int ClusterColumn, Width;
        public TextClusterKind Kind;

        public ClusterWalker(TEditor editor, GraphemeLine line, int checkpoint, Span<char> scratch)
        {
            _editor = editor;
            _line = line;
            _options = LayoutOptions;
            _profile = _options.EffectiveProfile;
            _chunk = scratch;
            _rented = null;
            _at = line.Offsets[checkpoint];
            _length = _next = 0;
            _lineEnds = _loaded = false;
            _textStart = _textLength = 0;
            Column = line.Columns[checkpoint];
            Tabs = line.Tabs[checkpoint];
            Start = End = _at;
            ClusterColumn = Column;
            Width = 0;
            Kind = TextClusterKind.Text;
        }

        // Offset just past the current cluster (the walk position).
        public readonly uint Position => _at + (uint)_next;

        // The current cluster's code units.
        public readonly ReadOnlySpan<char> Text => _chunk.Slice(_textStart, _textLength);

        public bool MoveNext()
        {
            while (true)
            {
                if (_loaded && _next < _length)
                {
                    ReadOnlySpan<char> chunk = _chunk.Slice(0, _length);
                    int step = TextLayout.NextClusterLength(chunk, _next);
                    // A boundary depends on the code point after it: a cluster reaching the chunk end may continue.
                    if (_next + step < _length || _lineEnds)
                    {
                        TextClusterKind kind = TextClusterKind.Text;
                        if (step != 1 || !TextLayout.TrySimple(chunk, _next, _options, _profile, out int width))
                            width = TextLayout.ClusterWidth(chunk.Slice(_next, step), Column, _options, _profile, out kind);
                        Start = _at + (uint)_next;
                        End = Start + (uint)step;
                        ClusterColumn = Column;
                        Width = width;
                        Kind = kind;
                        _textStart = _next;
                        _textLength = step;
                        Column += width;
                        if (kind == TextClusterKind.Tab) Tabs++;
                        _next += step;
                        if (End >= _line.LastOffset + GraphemeCheckpointSpacing) _line.Add(End, Column, Tabs);
                        return true;
                    }
                }
                else if (_loaded && _lineEnds)
                {
                    // At the line end.
                    Start = End = _at + (uint)_length;
                    ClusterColumn = Column;
                    Width = 0;
                    _textLength = 0;
                    _line.End = Start;
                    _line.EndColumn = Column;
                    _line.EndTabs = Tabs;
                    return false;
                }
                Refill();
            }
        }

        private void Refill()
        {
            if (_loaded)
            {
                if (_next == 0)
                {
                    // One cluster fills the chunk: read it again with twice the room.
                    int size = _chunk.Length * 2;
                    char[] bigger = ArrayPool<char>.Shared.Rent(size);
                    if (_rented != null) ArrayPool<char>.Shared.Return(_rented);
                    _rented = bigger;
                    _chunk = bigger.AsSpan(0, size);
                }
                else
                {
                    _at += (uint)_next;
                }
            }

            // Copy [_at, _at + chunk) across the gap, then stop before an LF or the document end.
            TEditor editor = _editor;
            char[] buffer = editor.Buf;
            uint limit = (uint)Math.Min((long)_at + _chunk.Length, editor.bufLen);
            int count = (int)(limit - _at);
            uint gapStart = editor.curPtr;
            if (limit <= gapStart)
                buffer.AsSpan((int)_at, count).CopyTo(_chunk);
            else if (_at >= gapStart)
                buffer.AsSpan((int)(_at + editor.gapLen), count).CopyTo(_chunk);
            else
            {
                int head = (int)(gapStart - _at);
                buffer.AsSpan((int)_at, head).CopyTo(_chunk);
                buffer.AsSpan((int)(gapStart + editor.gapLen), count - head).CopyTo(_chunk.Slice(head));
            }
            int lf = _chunk.Slice(0, count).IndexOf('\n');
            int length = lf >= 0 ? lf : count;
            bool lineEnds = lf >= 0 || limit == editor.bufLen;
            editor.GraphemeUnitsRead += length;
            _line.Clear = Math.Max(_line.Clear, _at + (uint)length);
            if (lineEnds && _line.End == uint.MaxValue) _line.End = _at + (uint)length;
            // A chunk never ends inside a surrogate pair: every cluster in it is then exact except possibly the last.
            if (!lineEnds && length > 0 && char.IsHighSurrogate(_chunk[length - 1])) length--;
            _length = length;
            _lineEnds = lineEnds;
            _next = 0;
            _loaded = true;
        }

        public void Dispose()
        {
            if (_rented != null) ArrayPool<char>.Shared.Return(_rented);
            _rented = null;
        }
    }

    /// <summary>
    /// Returns the offset after the user-perceived character (extended grapheme cluster) at or containing
    /// <paramref name="p"/>, or the document length. An LF is a character of its own. Unlike <see cref="NextChar"/>
    /// this never stops inside a surrogate pair or a combining sequence.
    /// </summary>
    public uint NextGraphemeBoundary(uint p) => p >= bufLen ? bufLen : GraphemeAt(p).End;

    /// <summary>
    /// Returns the start of the user-perceived character (extended grapheme cluster) before <paramref name="p"/>, or
    /// zero. Unlike <see cref="PrevChar"/> this never stops inside a surrogate pair or a combining sequence.
    /// </summary>
    public uint PrevGraphemeBoundary(uint p)
    {
        p = Math.Min(p, bufLen);
        return p == 0 ? 0 : GraphemeAt(p - 1).Start;
    }

    // Moves an offset that falls inside a cluster to the cluster's start, or its end when forward is set.
    private uint SnapToGrapheme(uint p, bool forward)
    {
        if (p == 0 || p >= bufLen) return Math.Min(p, bufLen);
        (uint start, uint end) = GraphemeAt(p);
        return start == p ? p : forward ? end : start;
    }

    // The Unicode scalar at code unit p (either half of a pair names the pair), or null for an unpaired surrogate.
    private Rune? RuneAt(uint p)
    {
        char c = BufChar(p);
        if (char.IsHighSurrogate(c))
            return p + 1 < bufLen && char.IsLowSurrogate(BufChar(p + 1)) ? new Rune(c, BufChar(p + 1)) : null;
        if (char.IsLowSurrogate(c))
            return p > 0 && char.IsHighSurrogate(BufChar(p - 1)) ? new Rune(BufChar(p - 1), c) : null;
        return new Rune(c);
    }

    // Word characters: letters, digits and underscore, of any plane, and the combining marks that attach to them.
    private bool IsWordAt(uint p)
    {
        if (RuneAt(p) is not Rune rune) return false;
        return Rune.IsLetterOrDigit(rune) || rune.Value == '_'
            || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    /// <summary>
    /// Finds the next word start from a valid character offset; letters, digits and underscores of any plane, with
    /// their combining marks, form words. The result is never inside a user-perceived character.
    /// </summary>
    public uint NextWord(uint p)
    {
        // At the end of the document there is no character to classify: BufChar(bufLen) is the gap, or past the buffer.
        if (p >= bufLen) return bufLen;
        if (IsWordAt(p))
            while (p < bufLen && IsWordAt(p))
                p = NextChar(p);
        else if (p < bufLen)
            p = NextChar(p);
        while (p < bufLen && !IsWordAt(p))
            p = NextChar(p);
        return SnapToGrapheme(p, forward: true);
    }

    /// <summary>
    /// Finds the preceding word start, treating letters, digits and underscores of any plane, with their combining
    /// marks, as word characters. The result is never inside a user-perceived character.
    /// </summary>
    public uint PrevWord(uint p)
    {
        p = Math.Min(p, bufLen);
        while (p > 0 && !IsWordAt(PrevChar(p)))
            p = PrevChar(p);
        while (p > 0 && IsWordAt(PrevChar(p)))
            p = PrevChar(p);
        return SnapToGrapheme(p, forward: false);
    }

    // The word under a mouse offset: the run of word characters containing p, or ending at p when p is a line or
    // document end; otherwise the one user-perceived character at p, or an empty range at a line or document end.
    private (uint Start, uint End) WordAt(uint p)
    {
        p = SnapToGrapheme(Math.Min(p, bufLen), forward: false);
        uint start = p, end = p;
        bool atLineEnd = p >= bufLen || BufChar(p) == '\n';
        if (!atLineEnd && IsWordAt(p) || atLineEnd && p > 0 && IsWordAt(p - 1))
        {
            while (start > 0 && IsWordAt(start - 1)) start--;
            while (end < bufLen && IsWordAt(end)) end++;
            start = SnapToGrapheme(start, forward: false);
            end = SnapToGrapheme(end, forward: true);
        }
        else if (!atLineEnd)
            end = NextGraphemeBoundary(p);
        return (start, end);
    }

    // Typed and pasted text must be well-formed UTF-16: an unpaired surrogate becomes U+FFFD, so no keystroke or paste
    // can leave half a character in the document. Returns the input itself when it is already well formed.
    private static char[] ReplaceUnpairedSurrogates(char[] text, int length)
    {
        char[]? fixedText = null;
        for (int i = 0; i < length; i++)
        {
            char c = text[i];
            if (!char.IsSurrogate(c)) continue;
            if (char.IsHighSurrogate(c) && i + 1 < length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
            fixedText ??= (char[])text.Clone();
            fixedText[i] = '�';
        }
        return fixedText ?? text;
    }

    /// <summary>Selects the whole text and places the caret at its end.</summary>
    public void SelectAll()
    {
        selecting = false;
        SetSelect(0, bufLen, false);
    }

    /// <summary>Moves a signed number of lines while retaining the visual column where the destination line permits.</summary>
    public uint LineMove(uint p, int count)
    {
        uint i = p;
        p = CachedLineStart(p);
        int pos = CharPos(p, i);
        while (count != 0)
        {
            i = p;
            if (count < 0) { p = PrevLine(p); count++; }
            else           { p = NextLine(p); count--; }
        }
        if (p != i) p = CharPtr(p, pos);
        return p;
    }

    /// <summary>Tests whether the logical selection range is nonempty.</summary>
    public bool HasSelection() => selStart != selEnd;

    /// <summary>Collapses the selection at the insertion offset and disables selection extension.</summary>
    public void HideSelect()
    {
        selecting = false;
        SetSelect(curPtr, curPtr, false);
    }

    /// <summary>Tests whether the visual caret position lies inside the viewport.</summary>
    public bool CursorVisible()
        => curPos.x >= delta.x && curPos.x < delta.x + size.x
           && curPos.y >= delta.y && curPos.y < delta.y + size.y;

    private static int CountLines(char[] buf, uint offset, uint count)
    {
        int n = 0;
        for (uint i = 0; i < count; i++)
            if (buf[offset + i] == '\n') n++;
        return n;
    }

    /// <summary>Sets a half-open logical UTF-16 selection and places the caret at its start when curStart is true, otherwise at its end.</summary>
    public void SetSelect(uint newStart, uint newEnd, bool curStart)
    {
        uint p = curStart ? newStart : newEnd;
        byte flags = Views.ufUpdate;
        if (newStart != selStart || newEnd != selEnd)
            if (newStart != newEnd || selStart != selEnd)
                flags = Views.ufView;

        if (p != curPtr)
        {
            if (p > curPtr)
            {
                uint l = p - curPtr;
                Array.Copy(Buf, curPtr + gapLen, Buf, curPtr, l);
                curPos.y += CountLines(Buf, curPtr, l);
                curPtr = p;
            }
            else
            {
                uint l = curPtr - p;
                curPtr = p;
                curPos.y -= CountLines(Buf, curPtr, l);
                Array.Copy(Buf, curPtr, Buf, curPtr + gapLen, l);
            }
            drawLine = curPos.y;
            drawPtr  = CachedLineStart(p);
            curPos.x = CharPos(drawPtr, p);
            // A caret move ends the undo group that typing could extend; the history itself stays.
            delCount = 0;
            insCount = 0;
            undoHistory?.Close();
            SetBufSize(bufLen);
        }
        selStart = newStart;
        selEnd   = newEnd;
        Update(flags);
    }

    /// <summary>Moves the logical insertion offset, extending or expanding selection according to the sm-prefixed mode flags.</summary>
    public void SetCurPtr(uint p, byte selectMode)
    {
        uint anchor;
        if ((selectMode & Views.smExtend) == 0)
            anchor = p;
        else if (curPtr == selStart)
            anchor = selEnd;
        else
            anchor = selStart;

        if (p < anchor)
        {
            if ((selectMode & Views.smDouble) != 0)
            {
                p = PrevLine(NextLine(p));
                anchor = NextLine(PrevLine(anchor));
            }
            SetSelect(p, anchor, true);
        }
        else
        {
            if ((selectMode & Views.smDouble) != 0)
            {
                p = NextLine(p);
                anchor = PrevLine(NextLine(anchor));
            }
            SetSelect(anchor, p, false);
        }
    }

    /// <summary>Clamps the viewport origin in columns and lines to the content extent and schedules a redraw.</summary>
    public void ScrollTo(int x, int y)
    {
        x = Math.Max(0, Math.Min(x, limit.x - size.x));
        y = Math.Max(0, Math.Min(y, limit.y - size.y));
        if (x != delta.x || y != delta.y)
        {
            delta.x = x;
            delta.y = y;
            Update(Views.ufView);
        }
    }

    /// <summary>Scrolls to expose the caret, optionally centering its line vertically.</summary>
    public void TrackCursor(bool center)
    {
        if (center)
            ScrollTo(curPos.x - size.x + 1, curPos.y - size.y / 2);
        else
            ScrollTo(
                Math.Max(curPos.x - size.x + 1, Math.Min(delta.x, curPos.x)),
                Math.Max(curPos.y - size.y + 1, Math.Min(delta.y, curPos.y)));
    }

    /// <summary>Collapses any existing selection and enables extension from the current insertion point.</summary>
    public void StartSelect()
    {
        HideSelect();
        selecting = true;
    }

    /// <summary>Toggles insertion versus overwrite mode and the corresponding caret shape.</summary>
    public void ToggleInsMode()
    {
        overwrite = !overwrite;
        SetState(Views.sfCursorIns, !GetState(Views.sfCursorIns));
    }

    /// <summary>
    /// Allocates physical UTF-16 storage using the configured buffer capacity. Editor construction and stream
    /// restoration call this hook; overrides that retain the standard managed buffer should normally call base.
    /// </summary>
    public virtual void InitBuffer() => buffer = new char[bufSize];

    /// <summary>
    /// Releases the editor's reference to its text buffer during <see cref="ShutDown"/>. Overrides that add buffer
    /// lifecycle work should normally call base and must tolerate the editor's deterministic shutdown contract.
    /// </summary>
    public virtual void DoneBuffer() => buffer = null;

    // Text storage is present exactly while the editor is valid: InitBuffer()
    // allocates it during construction/Read and DoneBuffer() releases it in
    // ShutDown(). Editing operations run only on a valid editor, so this
    // accessor states that invariant once instead of guarding every access.
    private protected char[] Buf =>
        buffer ?? throw new InvalidOperationException(
            "TEditor text buffer is unavailable; the editor is not valid.");

    /// <summary>Replaces the selection with a source-array slice, optionally recording undo and selecting inserted text; returns false if capacity cannot be obtained.</summary>
    public bool InsertBuffer(char[] p, uint offset, uint length,
                             bool allowUndo, bool selectText)
    {
        selecting = false;
        uint selLen = selEnd - selStart;
        if (selLen == 0 && length == 0) return true;

        // Undo text lives in the history, so the counters reserve no buffer capacity; they are restated below.
        uint savedDelCount = delCount, savedInsCount = insCount;
        delCount = 0;
        insCount = 0;
        uint newSize = bufLen - selLen + length;
        if (newSize > bufLen)
            if (!SetBufSize(newSize))
            {
                delCount = savedDelCount;
                insCount = savedInsCount;
                return false;
            }

        bool record = allowUndo && canUndo && !IsClipboard();
        uint editOffset = selStart;
        string removed = record ? GetText(selStart, selLen) : string.Empty;

        // Snapshot the input so that, if `p` aliases our own buffer
        // (insertFrom uses the source editor's buffer directly), the
        // subsequent in-place memmoves cannot corrupt it.
        char[]? src = null;
        if (length > 0)
        {
            src = new char[length];
            Array.Copy(p, (int)offset, src, 0, (int)length);
        }

        int selLines = CountLines(Buf, BufPtr(selStart), selLen);
        if (curPtr == selEnd)
        {
            curPtr = selStart;
            curPos.y -= selLines;
        }
        if (delta.y > curPos.y)
        {
            delta.y -= selLines;
            if (delta.y < curPos.y) delta.y = curPos.y;
        }

        if (length > 0 && src != null)
            Array.Copy(src, 0, Buf, (int)curPtr, (int)length);

        int lines = length > 0 ? CountLines(Buf, curPtr, length) : 0;
        curPtr += length;
        curPos.y += lines;

        // Commit the new length before anything reads the text: LineStart and CharPos clamp to bufLen, so with the
        // pre-insertion length they would stop short of an insertion that ends past the old end of the document.
        bufLen += length - selLen;
        gapLen -= length - selLen;
        bool structural = selLines != 0 || lines != 0;
        AdjustGraphemeCache(editOffset, selLen, length, structural);

        drawLine = curPos.y;
        drawPtr  = CachedLineStart(curPtr);
        curPos.x = CharPos(drawPtr, curPtr);
        if (!selectText) selStart = curPtr;
        selEnd = curPtr;
        limit.y += lines - selLines;
        delta.y = Math.Max(0, Math.Min(delta.y, limit.y - size.y));
        // The extent only grows while editing: a shortened line leaves it as it was until the text is reloaded.
        longestLine = Math.Max(longestLine, structural
            ? LongestLine(CachedLineStart(editOffset), CachedLineEnd(curPtr))
            : LineWidth(LineFromStart(drawPtr)));
        limit.x = HorizontalLimit();
        // A document already marked modified while at its saved position was marked so explicitly.
        if (record && modified && UndoHistory.IsAtSavedPosition) explicitlyModified = true;
        if (!IsClipboard()) modified = true;
        SetBufSize(bufLen);
        if (record)
        {
            UndoHistory.Record(editOffset, removed, length > 0 ? new string(src!) : string.Empty);
            RestateUndoCounters();
        }
        else if (!replayingUndo)
        {
            // An unrecorded edit invalidates every recorded offset.
            undoHistory?.Clear(saved: false);
        }
        Update(selLines == 0 && lines == 0 ? Views.ufLine : Views.ufView);
        return true;
    }

    private EditorUndoHistory UndoHistory =>
        undoHistory ??= new EditorUndoHistory(DefaultUndoGroupLimit, DefaultUndoTextLimit);

    // delCount/insCount describe the group typing can still extend.
    private void RestateUndoCounters()
    {
        delCount = 0;
        insCount = 0;
        if (undoHistory?.OpenGroup is not { } group) return;
        foreach (var operation in group.Operations)
        {
            delCount += (uint)operation.Removed.Length;
            insCount += (uint)operation.Inserted.Length;
        }
    }

    // Columns of the widest line in [start, end); start must be a line start. Measures every line whole, as the
    // editor lays it out, without caching anything.
    private int LongestLine(uint start, uint end)
    {
        TextLayoutOptions options = LayoutOptions;
        long widest = 0;
        uint p = start;
        while (true)
        {
            uint lineEnd = FindLineBreak(p, end, false) ?? end;
            widest = Math.Max(widest, MeasureRange(p, lineEnd, options));
            if (lineEnd >= end) break;
            p = lineEnd + 1;
        }
        return (int)Math.Min(widest, int.MaxValue - 1);
    }

    // Columns of the logical text [start, end), which holds no LF, laid out from column 0.
    private int MeasureRange(uint start, uint end, in TextLayoutOptions options)
    {
        if (end <= start) return 0;
        GraphemeUnitsRead += end - start;
        char[] buf = Buf;
        int length = (int)(end - start);
        if (end <= curPtr) return TextLayout.Measure(buf.AsSpan((int)start, length), options);
        if (start >= curPtr) return TextLayout.Measure(buf.AsSpan((int)(start + gapLen), length), options);
        ReadOnlySpan<char> first = buf.AsSpan((int)start, (int)(curPtr - start));
        ReadOnlySpan<char> second = buf.AsSpan((int)(curPtr + gapLen), (int)(end - curPtr));
        int column = 0;
        foreach (TextCluster cluster in TextLayout.EnumerateClusters(first, second, options, 0))
            column = cluster.EndColumn;
        return column;
    }

    // Horizontal scroll extent: at least the historic maxLineLength, and room for the caret after the widest line.
    private int HorizontalLimit() => Math.Max(Views.maxLineLength, longestLine + 1);

    // Logical text [start, start + length) across the gap.
    private string GetText(uint start, uint length)
    {
        if (length == 0) return string.Empty;
        var chars = new char[length];
        for (uint i = 0; i < length; i++) chars[i] = BufChar(start + i);
        return new string(chars);
    }

    /// <summary>
    /// Replaces the selection with the source editor's selected text, using this editor's undo policy. Clipboard
    /// copy/paste routes through this hook; overrides may adapt insertion and normally call base for standard editing.
    /// </summary>
    public virtual bool InsertFrom(TEditor editor)
        => InsertBuffer(editor.Buf,
                        editor.BufPtr(editor.selStart),
                        editor.selEnd - editor.selStart,
                        canUndo, IsClipboard());

    /// <summary>Replaces the selection with the first length UTF-16 code units, optionally selecting the insertion; returns whether insertion succeeded.</summary>
    public bool InsertText(char[] text, uint length, bool selectText)
        => InsertBuffer(text ?? Array.Empty<char>(), 0, length, canUndo, selectText);

    /// <summary>Replaces the selection with text without selecting the inserted content; null or empty text deletes the selection.</summary>
    public bool InsertText(string text) => InsertText(text, false);

    /// <summary>Replaces the selection with text, optionally selecting it; null or empty text deletes the selection.</summary>
    public bool InsertText(string text, bool selectText)
    {
        char[] chars = string.IsNullOrEmpty(text) ? Array.Empty<char>() : text.ToCharArray();
        return InsertText(chars, (uint)chars.Length, selectText);
    }

    /// <summary>Replaces the selection with byte values mapped directly to equal-valued Unicode characters, optionally selecting the insertion.</summary>
    public bool InsertText(byte[] text, uint length, bool selectText)
    {
        if (text == null || length == 0)
            return InsertText(Array.Empty<char>(), 0, selectText);

        var chars = new char[length];
        for (uint i = 0; i < length; i++)
            chars[i] = (char)text[i];
        return InsertText(chars, length, selectText);
    }

    /// <summary>Tests whether this instance is the shared in-process clipboard editor.</summary>
    public bool IsClipboard() => ReferenceEquals(clipboard, this);

    /// <summary>Deletes the logical range around the caret, or the current selection when delSelect is true and a selection exists.</summary>
    public void DeleteRange(uint startPtr, uint endPtr, bool delSelect)
    {
        BeginUndoGroup(EditorUndoKind.Other);
        try
        {
            if (HasSelection() && delSelect)
                DeleteSelect();
            else
            {
                SetSelect(curPtr, endPtr, true);
                DeleteSelect();
                SetSelect(startPtr, curPtr, false);
                DeleteSelect();
            }
        }
        finally { EndUndoGroup(); }
    }

    /// <summary>Removes the selected text using the editor's undo policy.</summary>
    public void DeleteSelect() => InsertText(Array.Empty<char>(), 0, false);

    /// <summary>Inserts LF at the selection and optionally copies leading spaces and tabs from the current line.</summary>
    public void NewLine()
    {
        BeginUndoGroup(EditorUndoKind.Other);
        try
        {
            uint p = CachedLineStart(curPtr);
            uint i = p;
            while (i < curPtr && (Buf[BufPtr(i)] == ' ' || Buf[BufPtr(i)] == '\t'))
                i++;
            InsertText(new char[] { '\n' }, 1, false);
            if (autoIndent)
            {
                uint indentLen = i - p;
                var indent = new char[indentLen];
                for (uint k = 0; k < indentLen; k++)
                    indent[k] = Buf[BufPtr(p + k)];
                InsertText(indent, indentLen, false);
            }
        }
        finally { EndUndoGroup(); }
    }

    // Undo grouping: every edit between Begin and End is one undo group; calls nest.
    private void BeginUndoGroup(EditorUndoKind kind) => UndoHistory.Begin(kind);

    private void EndUndoGroup()
    {
        UndoHistory.End();
        RestateUndoCounters();
    }

    /// <summary>Copies selection to the in-process clipboard and attempts to mirror it to the host clipboard; host failure does not invalidate the internal copy.</summary>
    public bool ClipCopy()
    {
        bool res = false;
        if (clipboard != null && clipboard != this)
        {
            // Capture the selected slice as a Latin-1 string before the
            // internal copy runs (selStart/selEnd are not modified by
            // InsertFrom, but capturing first keeps the contract clear).
            string mirrored = SelectionAsClipboardString();
            res = clipboard.InsertFrom(this);
            selecting = false;
            Update(Views.ufUpdate);
            if (res && mirrored.Length > 0)
            {
                try { ClipboardService.Current?.SetText(mirrored); }
                catch { /* best-effort — never fail editor copy on OS error */ }
            }
        }
        return res;
    }

    /// <summary>Deletes the selection only after a successful in-process clipboard copy.</summary>
    public void ClipCut()
    {
        if (ClipCopy()) DeleteSelect();
    }

    /// <summary>Pastes nonempty host clipboard text when available, otherwise falling back to the in-process clipboard selection.</summary>
    public void ClipPaste()
    {
        var svc = ClipboardService.Current;
        if (svc != null && svc.IsAvailable && svc.TryGetText(out string text))
        {
            char[] converted = ClipboardEncoding.ClipboardStringToChars(text);
            char[] chars = ReplaceUnpairedSurrogates(converted, converted.Length);
            if (chars.Length > 0)
            {
                InsertText(chars, (uint)chars.Length, false);
                return;
            }
            // Empty payload from the OS clipboard counts as a no-op rather
            // than falling through to the internal clipboard, matching
            // platform expectations ("paste returned nothing").
            if (text != null && text.Length > 0)
            {
                // Source had only unsupported chars / oversized — still
                // do not fall through; the user asked for OS paste.
                return;
            }
        }
        if (clipboard != null && clipboard != this)
            InsertFrom(clipboard);
    }

    // Extract the current selection as a Latin-1 string
    // suitable for passing to IClipboardService.SetText. Newlines remain LF;
    // OS-specific normalisation is the service's responsibility.
    private string SelectionAsClipboardString()
    {
        if (selStart >= selEnd) return string.Empty;
        var sb = new System.Text.StringBuilder((int)(selEnd - selStart));
        for (uint p = selStart; p < selEnd; p++)
            sb.Append((char)BufChar(p));
        return sb.ToString();
    }

    /// <summary>
    /// Reverses the most recent undo group and selects the text it restores. Undo is multi-level; <see cref="Redo"/>
    /// reapplies what was undone until the next edit.
    /// </summary>
    public void Undo()
    {
        if (undoHistory is not { CanUndo: true } history) return;
        bool wasExplicit = modified && history.IsAtSavedPosition;
        EditorUndoGroup group = history.TakeUndo();
        ReplayUndo(() =>
        {
            for (int i = group.Operations.Count - 1; i >= 0; i--)
            {
                EditorUndoOperation operation = group.Operations[i];
                SetSelect(operation.Offset, operation.Offset + (uint)operation.Inserted.Length, false);
                char[] text = operation.Removed.ToString().ToCharArray();
                InsertBuffer(text, 0, (uint)text.Length, false, true);
            }
        }, wasExplicit);
    }

    /// <summary>Reapplies the undo group most recently undone, leaving the caret after the text it inserts.</summary>
    public void Redo()
    {
        if (undoHistory is not { CanRedo: true } history) return;
        bool wasExplicit = modified && history.IsAtSavedPosition;
        EditorUndoGroup group = history.TakeRedo();
        ReplayUndo(() =>
        {
            foreach (EditorUndoOperation operation in group.Operations)
            {
                SetSelect(operation.Offset, operation.Offset + (uint)operation.Removed.Length, false);
                char[] text = operation.Inserted.ToString().ToCharArray();
                InsertBuffer(text, 0, (uint)text.Length, false, false);
            }
        }, wasExplicit);
    }

    /// <summary>Whether <see cref="Undo"/> has a group to reverse.</summary>
    public bool HasUndo => undoHistory is { CanUndo: true };

    /// <summary>Whether <see cref="Redo"/> has a group to reapply.</summary>
    public bool HasRedo => undoHistory is { CanRedo: true };

    /// <summary>
    /// Maximum number of undo groups kept; the oldest are dropped first. Zero or less disables the history and clears it.
    /// Defaults to <see cref="DefaultUndoGroupLimit"/>.
    /// </summary>
    public int UndoGroupLimit
    {
        get => UndoHistory.GroupLimit;
        set { UndoHistory.GroupLimit = value; UpdateUndoState(); }
    }

    /// <summary>
    /// Maximum number of UTF-16 code units of removed plus inserted text kept by the undo history; the oldest groups
    /// are dropped first, and a single group larger than the limit is not kept. Zero or less disables the history and
    /// clears it. Defaults to <see cref="DefaultUndoTextLimit"/>.
    /// </summary>
    public int UndoTextLimit
    {
        get => UndoHistory.TextLimit;
        set { UndoHistory.TextLimit = value; UpdateUndoState(); }
    }

    /// <summary>Forgets every undo and redo group; <see cref="modified"/> is unchanged.</summary>
    public void ClearUndoHistory()
    {
        UndoHistory.Clear(saved: !modified);
        UpdateUndoState();
    }

    // Replays history without recording it, then derives the modified state from the saved position.
    private void ReplayUndo(Action replay, bool wasExplicit)
    {
        explicitlyModified |= wasExplicit;
        Lock();
        replayingUndo = true;
        try { replay(); }
        finally { replayingUndo = false; }
        modified = explicitlyModified || !undoHistory!.IsAtSavedPosition;
        UpdateUndoState();
        Unlock();
    }

    private void UpdateUndoState()
    {
        RestateUndoCounters();
        Update(Views.ufUpdate);
    }

    // Length of a null-terminated upstream `findStr` / `replaceStr` char
    // buffer (the TEditor statics keep the C-string convention).
    private static uint CharStrLen(char[] s)
    {
        if (s == null) return 0;
        for (uint i = 0; i < s.Length; i++)
            if (s[i] == 0) return i;
        return (uint)s.Length;
    }

    private static uint Scan(TEditor ed, uint startPos, char[] needle)
    {
        uint nlen = CharStrLen(needle);
        if (nlen == 0) return Views.sfSearchFailed;
        if (ed.bufLen < nlen) return Views.sfSearchFailed;
        uint last = ed.bufLen - nlen;
        for (uint i = startPos; i <= last; i++)
        {
            uint j = 0;
            while (j < nlen && ed.BufChar(i + j) == needle[j])
                j++;
            if (j == nlen) return i - startPos;
        }
        return Views.sfSearchFailed;
    }

    private static uint IScan(TEditor ed, uint startPos, char[] needle)
    {
        uint nlen = CharStrLen(needle);
        if (nlen == 0) return Views.sfSearchFailed;
        if (ed.bufLen < nlen) return Views.sfSearchFailed;
        uint last = ed.bufLen - nlen;
        for (uint i = startPos; i <= last; i++)
        {
            uint j = 0;
            while (j < nlen
                   && ToUpperChar(ed.BufChar(i + j))
                      == ToUpperChar(needle[j]))
                j++;
            if (j == nlen) return i - startPos;
        }
        return Views.sfSearchFailed;
    }

    private static char ToUpperChar(char ch)
        => char.ToUpperInvariant(ch);

    /// <summary>Searches forward from the caret using a null-terminated pattern and case/whole-word flags; selects and exposes a match and returns true.</summary>
    public bool Search(char[] needle, ushort opts)
    {
        uint pos = curPtr;
        uint i;
        uint nlen = CharStrLen(needle);
        do
        {
            i = ((opts & Views.efCaseSensitive) != 0)
                ? Scan(this, pos, needle)
                : IScan(this, pos, needle);

            if (i != Views.sfSearchFailed)
            {
                i += pos;
                bool wholeWordsOnly = (opts & Views.efWholeWordsOnly) != 0;
                bool boundaryViolated = wholeWordsOnly && (
                       (i != 0 && IsWordAt(i - 1))
                    || (i + nlen != bufLen && IsWordAt(i + nlen))
                );
                if (!boundaryViolated)
                {
                    Lock();
                    SetSelect(i, i + nlen, false);
                    TrackCursor(!CursorVisible());
                    Unlock();
                    return true;
                }
                pos = i + 1;
            }
        } while (i != Views.sfSearchFailed);
        return false;
    }

    /// <summary>Searches forward after mapping pattern bytes directly to equal-valued Unicode characters.</summary>
    public bool Search(byte[] needle, ushort opts)
    {
        if (needle == null) return Search(Array.Empty<char>(), opts);
        var chars = new char[needle.Length];
        for (int i = 0; i < needle.Length; i++) chars[i] = (char)needle[i];
        return Search(chars, opts);
    }

    /// <summary>Runs search or replacement using shared patterns and flags, invoking configured prompts as needed.</summary>
    public void DoSearchReplace()
    {
        // All replacements of one search-and-replace run undo together.
        BeginUndoGroup(EditorUndoKind.Other);
        try { SearchReplaceLoop(); }
        finally { EndUndoGroup(); }
    }

    private void SearchReplaceLoop()
    {
        ushort i;
        do
        {
            i = Views.cmCancel;
            if (!Search(findStr, editorFlags))
            {
                if ((editorFlags & (Views.efReplaceAll | Views.efDoReplace))
                    != (Views.efReplaceAll | Views.efDoReplace))
                    editorDialog(Views.edSearchFailed, null);
            }
            else if ((editorFlags & Views.efDoReplace) != 0)
            {
                i = Views.cmYes;
                if ((editorFlags & Views.efPromptOnReplace) != 0)
                {
                    var c = MakeGlobal(cursor);
                    i = editorDialog(Views.edReplacePrompt, c);
                }
                if (i == Views.cmYes)
                {
                    Lock();
                    InsertText(replaceStr, CharStrLen(replaceStr), false);
                    TrackCursor(false);
                    Unlock();
                }
            }
        } while (i != Views.cmCancel
                 && (editorFlags & Views.efReplaceAll) != 0);
    }

    /// <summary>Prompts for search criteria and starts searching when the prompt is accepted.</summary>
    public void Find()
    {
        var rec = new TFindDialogRec(findStr, editorFlags);
        if (editorDialog(Views.edFind, rec) != Views.cmCancel)
        {
            CopyToFixedBuffer(rec.Find, findStr);
            editorFlags = (ushort)(rec.Options & ~Views.efDoReplace);
            DoSearchReplace();
        }
    }

    /// <summary>Prompts for search and replacement criteria and starts replacement when accepted.</summary>
    public void Replace()
    {
        var rec = new TReplaceDialogRec(findStr, replaceStr, editorFlags);
        if (editorDialog(Views.edReplace, rec) != Views.cmCancel)
        {
            CopyToFixedBuffer(rec.Find, findStr);
            CopyToFixedBuffer(rec.Replace, replaceStr);
            editorFlags = (ushort)(rec.Options | Views.efDoReplace);
            DoSearchReplace();
        }
    }

    private static void CopyToFixedBuffer(char[] src, char[] dst)
    {
        Array.Clear(dst, 0, dst.Length);
        if (src == null) return;
        int n = Math.Min(src.Length, dst.Length - 1);
        // A pattern cut at the buffer size keeps whole surrogate pairs.
        if (n > 0 && n < src.Length && char.IsHighSurrogate(src[n - 1]) && char.IsLowSurrogate(src[n])) n--;
        Array.Copy(src, 0, dst, 0, n);
    }


    /// <summary>Checks whether the requested capacity fits the current buffer; derived editors may reallocate and return success.</summary>
    public virtual bool SetBufSize(uint newSize) => newSize <= bufSize;

    /// <summary>Sets logical length within the allocated capacity and resets caret, selection, viewport, undo, and modification state.</summary>
    public void SetBufLen(uint length)
    {
        bufLen   = length;
        gapLen   = bufSize - length;
        InvalidateGraphemeCache();
        selStart = 0;
        selEnd   = 0;
        curPtr   = 0;
        delta    = default;
        curPos   = delta;
        longestLine = buffer != null ? LongestLine(0, bufLen) : 0;
        limit.x  = HorizontalLimit();
        limit.y  = (buffer != null && bufLen > 0
                       ? CountLines(Buf, gapLen, bufLen)
                       : 0) + 1;
        drawLine = 0;
        drawPtr  = 0;
        delCount = 0;
        insCount = 0;
        modified = false;
        explicitlyModified = false;
        UndoHistory.Clear(saved: true);
        Update(Views.ufView);
    }

    /// <summary>Clears <see cref="modified"/> and records the current undo position as the unmodified state, as a successful save does.</summary>
    public void MarkUnmodified()
    {
        modified = false;
        explicitlyModified = false;
        UndoHistory.SavedPosition = UndoHistory.Position;
        Update(Views.ufUpdate);
    }

    /// <inheritdoc />
    public override bool Valid(ushort _) => isValid;
    
    /// <summary>Defers editor redraw and status updates until matching unlocks release the nesting count.</summary>
    public void Lock() => lockCount++;

    /// <summary>Releases one update lock and performs pending updates when the count reaches zero.</summary>
    public void Unlock()
    {
        if (lockCount > 0)
        {
            lockCount--;
            if (lockCount == 0) DoUpdate();
        }
    }

    /// <summary>Adds uf-prefixed update requests and processes them immediately when unlocked.</summary>
    public void Update(byte aFlags)
    {
        updateFlags |= aFlags;
        if (lockCount == 0) DoUpdate();
    }

    /// <summary>Applies pending drawing, caret, scrollbar, indicator, and command-state updates.</summary>
    public void DoUpdate()
    {
        if (updateFlags != 0)
        {
            SetCursor(curPos.x - delta.x, curPos.y - delta.y);
            if ((updateFlags & Views.ufView) != 0)
                DrawView();
            else if ((updateFlags & Views.ufLine) != 0)
                DrawLines(curPos.y - delta.y, 1, CachedLineStart(curPtr));
            hScrollBar?.SetParams(delta.x, 0,
                limit.x - size.x, size.x / 2, 1);
            vScrollBar?.SetParams(delta.y, 0,
                limit.y - size.y, size.y - 1, 1);
            indicator?.SetValue(curPos, modified);
            if ((state & Views.sfActive) != 0) UpdateCommands();
            updateFlags = 0;
        }
    }

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        switch (aState)
        {
            case Views.sfActive:
                hScrollBar?.SetState(Views.sfVisible, enable);
                vScrollBar?.SetState(Views.sfVisible, enable);
                indicator?.SetState(Views.sfVisible, enable);
                UpdateCommands();
                break;
            case Views.sfExposed:
                if (enable) Unlock();
                break;
        }
    }

    /// <summary>Updates enabled editor commands from active, selection, clipboard, and undo state.</summary>
    public virtual void UpdateCommands()
    {
        SetCmdState(Views.cmUndo, HasUndo);
        SetCmdState(Views.cmRedo, HasRedo);
        if (!IsClipboard())
        {
            SetCmdState(Views.cmCut,  HasSelection());
            SetCmdState(Views.cmCopy, HasSelection());
            SetCmdState(Views.cmPaste,
                clipboard != null && clipboard.HasSelection());
        }
        SetCmdState(Views.cmClear,        HasSelection());
        SetCmdState(Views.cmSelectAll,    true);
        SetCmdState(Views.cmFind,         true);
        SetCmdState(Views.cmReplace,      true);
        SetCmdState(Views.cmSearchAgain,  true);
    }

    /// <summary>Enables a command only when requested and this editor is active; otherwise disables it globally.</summary>
    public void SetCmdState(ushort command, bool enable)
    {
        // Single-command equivalents of EnableCommands/DisableCommands over a one-element
        // set — same effect on curCommandSet and on commandSetChanged, without allocating a
        // TCommandSet on every UpdateCommands() pass.
        if (enable && (state & Views.sfActive) != 0)
            EnableCommand(command);
        else
            DisableCommand(command);
    }

    /// <inheritdoc />
    public override void Draw()
    {
        if (drawLine != delta.y)
        {
            drawPtr = LineMove(drawPtr, delta.y - drawLine);
            drawLine = delta.y;
        }
        DrawLines(0, size.y, drawPtr);
    }

    private const int MaxStackRowCells = 512;

    /// <summary>Draws count lines beginning at a logical text offset into local viewport row y, applying horizontal scrolling.</summary>
    public void DrawLines(int y, int count, uint linePtr)
    {
        ushort color = GetColor(0x0201);
        // Only the visible window is formatted: the row holds size.x cells however far the view is scrolled.
        int width = Math.Max(size.x, 1);
        Span<TScreenChar> row = width <= MaxStackRowCells ? stackalloc TScreenChar[width] : new TScreenChar[width];
        while (count-- > 0)
        {
            FormatLine(row, linePtr, delta.x, (byte)color, (byte)(color >> 8), default);
            WriteLine(0, y, size.x, 1, row);
            linePtr = NextLine(linePtr);
            y++;
        }
    }

    /// <summary>
    /// Formats the visual columns [<paramref name="firstColumn"/>, firstColumn + cells.Length) of the line that starts
    /// at <paramref name="linePtr"/> into <paramref name="cells"/>, as <see cref="DrawLines"/> draws them.
    /// </summary>
    /// <remarks>
    /// <para>A grapheme cluster becomes a lead cell and, when it is wide, trail cells (see
    /// <see cref="TScreenChar.CreateCluster(System.ReadOnlySpan{char}, int, TColorAttr)"/>); a wide cluster cut by either edge of the window is drawn as blanks, never
    /// as half a glyph. Tabs expand to blanks up to the next <see cref="tabSize"/> stop and a CR is a blank column.
    /// Under the <see cref="TSharpVision.Text.TextLayout.LegacyCellWidthSwitch"/> switch every UTF-16 code unit is one
    /// cell, as in TSharpVision 1.2.</para>
    /// <para>Selected text uses <paramref name="selected"/>; other text uses the attribute of the run that contains the
    /// first code unit of its cluster, or <paramref name="normal"/>. Runs are offsets from <paramref name="linePtr"/>,
    /// sorted and not overlapping. Only the window is formatted: the line is entered at the nearest column
    /// checkpoint, not walked from its start, and nothing is allocated once the line is known.</para>
    /// </remarks>
    /// <param name="cells">Destination cells; their count is the window width.</param>
    /// <param name="linePtr">Logical offset of the line start.</param>
    /// <param name="firstColumn">Visual column shown in the first cell.</param>
    /// <param name="normal">Attribute of unselected text without a run.</param>
    /// <param name="selected">Attribute of selected text.</param>
    /// <param name="runs">Attributes for ranges of the line, for example syntax colours.</param>
    public void FormatLine(Span<TScreenChar> cells, uint linePtr, int firstColumn, ushort normal, ushort selected,
                           ReadOnlySpan<EditorTextRun> runs)
    {
        int first = Math.Max(0, firstColumn);
        long end = (long)first + cells.Length;
        int x = first;                                           // the next column to fill
        if (linePtr < bufLen && cells.Length > 0)
        {
            bool legacy = LegacyLayout;
            int run = 0;
            GraphemeLine line = LineForOrigin(linePtr);
            Span<char> scratch = stackalloc char[GraphemeChunk];
            var walker = new ClusterWalker(this, line, line.CheckpointAtColumn(first), scratch);
            try
            {
                while (x < end && walker.MoveNext())
                {
                    int column = walker.ClusterColumn;
                    long next = (long)column + walker.Width;
                    if (next <= first) continue;
                    int from = Math.Max(column, first), to = (int)Math.Min(next, end);
                    ReadOnlySpan<char> text = walker.Text;
                    if (legacy && walker.Kind != TextClusterKind.Tab)
                    {
                        // One cell per code unit, each with its own attribute, as before.
                        for (int k = from - column; k < text.Length && column + k < to; k++)
                        {
                            uint unit = walker.Start + (uint)k;
                            char c = text[k];
                            cells[column + k - first] = new TScreenChar(c == '\r' ? ' ' : c,
                                UnitAttribute(unit, unit + 1, linePtr, normal, selected, runs, ref run));
                        }
                    }
                    else
                    {
                        TColorAttr attr = UnitAttribute(walker.Start, walker.End, linePtr, normal, selected, runs, ref run);
                        switch (walker.Kind)
                        {
                            case TextClusterKind.Tab:
                                Blank(cells, from - first, to - from, attr);
                                break;
                            case TextClusterKind.Control:
                                // A CR is an ordinary one-column character of the line (the buffer is LF-only), drawn
                                // blank; other control characters are drawn as they are, as before.
                                cells[from - first] = new TScreenChar(text[0] == '\r' ? ' ' : text[0], attr);
                                break;
                            case TextClusterKind.Malformed:
                                cells[from - first] = new TScreenChar('\uFFFD', attr);
                                break;
                            default:
                                if (column < first || next > end)
                                {
                                    Blank(cells, from - first, to - from, attr);   // cut by the window edge
                                    break;
                                }
                                int width = walker.Width;
                                int lead = Math.Min(width, TScreenChar.MaxCellWidth);
                                int at = column - first;
                                cells[at] = text.Length == 1 && lead == 1
                                    ? new TScreenChar(text[0], attr)
                                    : TScreenChar.CreateCluster(text, lead, attr);
                                for (int k = 1; k < lead; k++) cells[at + k] = TScreenChar.CreateTrail(attr);
                                Blank(cells, at + lead, width - lead, attr);
                                break;
                        }
                    }
                    x = to;
                }
            }
            finally { walker.Dispose(); }
        }
        if (x < end) Blank(cells, x - first, (int)(end - x), normal);
    }

    private static void Blank(Span<TScreenChar> cells, int from, int count, TColorAttr attr)
    {
        for (int k = 0; k < count; k++) cells[from + k] = new TScreenChar(' ', attr);
    }

    // Attribute of the code units [start, end) of a line: selected when any of them is selected, otherwise the run
    // containing the first one (runs is scanned forward with `run`, as clusters come in order).
    private TColorAttr UnitAttribute(uint start, uint end, uint linePtr, ushort normal, ushort selected,
                                     ReadOnlySpan<EditorTextRun> runs, ref int run)
    {
        if (selStart < end && selEnd > start && selStart < selEnd) return selected;
        if (runs.IsEmpty) return normal;
        long index = (long)start - linePtr;
        while (run < runs.Length && runs[run].End <= index) run++;
        return run < runs.Length && runs[run].Start <= index ? runs[run].Attribute : normal;
    }

    /// <summary>
    /// Maps a screen-cell mouse position, clamped to the viewport, to a logical UTF-16 document offset; a cell inside a
    /// user-perceived character maps to that character's start.
    /// </summary>
    public uint GetMousePtr(TPoint m)
    {
        TPoint mouse = MakeLocal(m);
        mouse.x = Math.Max(0, Math.Min(mouse.x, size.x - 1));
        mouse.y = Math.Max(0, Math.Min(mouse.y, size.y - 1));
        return SnapToGrapheme(CharPtr(LineMove(drawPtr, mouse.y + delta.y - drawLine),
            mouse.x + delta.x), forward: false);
    }

    /// <summary>Copies a matching scrollbar notification's value into a viewport offset and schedules redraw when it changes.</summary>
    public void CheckScrollBar(ref TEvent ev, TScrollBar? p, ref int d)
    {
        if (p != null && ev.message.infoPtr == p && p.value != d)
        {
            d = p.value;
            Update(Views.ufView);
        }
    }

    // full Ctrl-K / Ctrl-Q prefix state machine added.
    // keyState == 0  → normal key lookup (firstKeys)
    // keyState == KeyStateQuick (1) → Ctrl-Q prefix (quickKeys)
    // keyState == KeyStateBlock (2) → Ctrl-K prefix (blockKeys)
    /// <summary>
    /// Translates editor keyboard bindings and Ctrl+Q/Ctrl+K prefixes into command events. <see cref="HandleEvent"/>
    /// calls this before command execution; overrides may intercept or transform events and should call base when the
    /// standard key map is still desired.
    /// </summary>
    public virtual void ConvertEvent(ref TEvent ev)
    {
        if (ev.What != Events.evKeyDown) return;
        ushort key = ev.keyDown.keyCode;
        byte charCode = ev.keyDown.charScan.charCode;

        if (keyState != 0)
        {
            // In prefix mode: normalise the incoming key to an uppercase letter
            // for secondary-table lookup. 
            // convertEvent — kbCtA..kbCtZ are shifted to kbA..kbZ before scan.
            byte letter;
            if (key >= Keys.kbCtrlA && key <= Keys.kbCtrlZ)
                letter = (byte)('A' + (key - Keys.kbCtrlA));
            else
            {
                letter = ev.keyDown.charScan.charCode;
                if (letter >= 'a' && letter <= 'z')
                    letter = (byte)(letter - 32);
            }

            int savedState = keyState;
            keyState = 0; // always clear prefix state regardless of outcome

            if (key == Keys.kbEsc)
            {
                // Esc cancels prefix mode silently.
                ClearEvent(ref ev);
                return;
            }

            ushort cmd = savedState == KeyStateQuick
                ? MapQuickKey(letter)
                : savedState == KeyStateBlock
                    ? MapBlockKey(letter)
                    : (ushort)0;

            if (cmd != 0)
            {
                ev.What = Events.evCommand;
                ev.message.command = cmd;
            }
            else
            {
                // Unrecognised prefix sequence — consume safely (no insert).
                // v1 intentionally deviates from upstream which passes through;
                // the spec requires buffer unchanged on unrecognised sequences.
                ClearEvent(ref ev);
            }
            return;
        }

        // Ctrl+Backspace is kbCtrlBack (0x0E7F): its low byte is DEL, which the printable test below would take for
        // a character. It is the "delete the word before the caret" key of every current editor.
        if (key == Keys.kbCtrlBack)
        {
            ev.What = Events.evCommand;
            ev.message.command = Views.cmDelWordLeft;
            return;
        }

        if (charCode == 9 || (charCode >= 32 && charCode < 255))
            return;

        // keyState == 0: normal key mapping.
        // firstKeys table (abridged — only the movement/editing keys that are
        // not intercepted by a higher layer such as the status line or menu bar).
        switch (key)
        {
            case Keys.kbCtrlK:
                // Enter Ctrl-K prefix (blockKeys). Source: firstKeys kbCtK→0xFF02
                keyState = KeyStateBlock;
                ClearEvent(ref ev);
                return;
            case Keys.kbCtrlQ:
                // Enter Ctrl-Q prefix (quickKeys). Source: firstKeys kbCtQ→0xFF01
                keyState = KeyStateQuick;
                ClearEvent(ref ev);
                return;
            // Modern editor shortcuts. These are intentionally
            // only matched when keyState == 0; the Ctrl-K / Ctrl-Q prefix
            // cases above run first, so a Ctrl-K followed by Ctrl+C still
            // dispatches as the prefix sequence (block paste), not as an
            // OS-style Copy.
            case Keys.kbCtrlC:
                ev.What = Events.evCommand; ev.message.command = Views.cmCopy; break;
            case Keys.kbCtrlV:
                ev.What = Events.evCommand; ev.message.command = Views.cmPaste; break;
            case Keys.kbCtrlX:
                ev.What = Events.evCommand; ev.message.command = Views.cmCut; break;
            // Ctrl+Z undoes; Ctrl+Y and Ctrl+Shift+Z redo.
            case Keys.kbCtrlZ:
                ev.What = Events.evCommand;
                ev.message.command = (ev.keyDown.controlKeyState & Keys.kbShift) != 0 ? Views.cmRedo : Views.cmUndo;
                break;
            case Keys.kbCtrlY:
                ev.What = Events.evCommand; ev.message.command = Views.cmRedo; break;
            case Keys.kbCtrlA:
                ev.What = Events.evCommand; ev.message.command = Views.cmSelectAll; break;
            // The CUA clipboard keys of the original firstKeys table.
            case Keys.kbCtrlIns:
                ev.What = Events.evCommand; ev.message.command = Views.cmCopy; break;
            case Keys.kbShiftIns:
                ev.What = Events.evCommand; ev.message.command = Views.cmPaste; break;
            case Keys.kbShiftDel:
                ev.What = Events.evCommand; ev.message.command = Views.cmCut; break;
            case Keys.kbLeft:      ev.What = Events.evCommand; ev.message.command = Views.cmCharLeft;  break;
            case Keys.kbRight:     ev.What = Events.evCommand; ev.message.command = Views.cmCharRight; break;
            case Keys.kbCtrlLeft:  ev.What = Events.evCommand; ev.message.command = Views.cmWordLeft;  break;
            case Keys.kbCtrlRight: ev.What = Events.evCommand; ev.message.command = Views.cmWordRight; break;
            case Keys.kbHome:      ev.What = Events.evCommand; ev.message.command = Views.cmLineStart; break;
            case Keys.kbEnd:       ev.What = Events.evCommand; ev.message.command = Views.cmLineEnd;   break;
            case Keys.kbUp:        ev.What = Events.evCommand; ev.message.command = Views.cmLineUp;    break;
            case Keys.kbDown:      ev.What = Events.evCommand; ev.message.command = Views.cmLineDown;  break;
            case Keys.kbPgUp:      ev.What = Events.evCommand; ev.message.command = Views.cmPageUp;    break;
            case Keys.kbPgDn:      ev.What = Events.evCommand; ev.message.command = Views.cmPageDown;  break;
            case Keys.kbCtrlPgUp:  ev.What = Events.evCommand; ev.message.command = Views.cmTextStart; break;
            case Keys.kbCtrlPgDn:  ev.What = Events.evCommand; ev.message.command = Views.cmTextEnd;   break;
            case Keys.kbCtrlHome:  ev.What = Events.evCommand; ev.message.command = Views.cmTextStart; break;
            case Keys.kbCtrlEnd:   ev.What = Events.evCommand; ev.message.command = Views.cmTextEnd;   break;
            case Keys.kbCtrlDel:   ev.What = Events.evCommand; ev.message.command = Views.cmDelWord;   break;
            case Keys.kbIns:       ev.What = Events.evCommand; ev.message.command = Views.cmInsMode;   break;
            case Keys.kbDel:       ev.What = Events.evCommand; ev.message.command = Views.cmDelChar;   break;
            case Keys.kbBack:      ev.What = Events.evCommand; ev.message.command = Views.cmBackSpace;  break;
            case Keys.kbEnter:     ev.What = Events.evCommand; ev.message.command = Views.cmNewLine;    break;
        }
    }

    // Secondary key map for Ctrl-Q prefix.
    private static ushort MapQuickKey(byte letter) => letter switch
    {
        (byte)'A' => Views.cmReplace,    // Ctrl-Q A → Replace
        (byte)'C' => Views.cmTextEnd,    // Ctrl-Q C → move to end of file
        (byte)'D' => Views.cmLineEnd,    // Ctrl-Q D → move to end of line
        (byte)'F' => Views.cmFind,       // Ctrl-Q F → Find
        (byte)'H' => Views.cmDelStart,   // Ctrl-Q H → delete to start of line
        (byte)'R' => Views.cmTextStart,  // Ctrl-Q R → move to start of file
        (byte)'S' => Views.cmLineStart,  // Ctrl-Q S → move to start of line
        (byte)'Y' => Views.cmDelEnd,     // Ctrl-Q Y → delete to end of line
        _ => 0
    };

    // Secondary key map for Ctrl-K prefix.
    private static ushort MapBlockKey(byte letter) => letter switch
    {
        (byte)'B' => Views.cmStartSelect, // Ctrl-K B → mark block beginning
        (byte)'C' => Views.cmPaste,       // Ctrl-K C → insert block (paste from clipboard)
        (byte)'H' => Views.cmHideSelect,  // Ctrl-K H → hide/show selection
        (byte)'K' => Views.cmCopy,        // Ctrl-K K → mark block end (copy to clipboard)
        (byte)'Y' => Views.cmCut,         // Ctrl-K Y → delete/cut selected block
        _ => 0
    };

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);

        // Shift held with a movement key extends the selection from its anchor, as in every current editor. Read
        // before ConvertEvent turns the key into a command; a key completing a Ctrl+K / Ctrl+Q prefix is a letter of
        // that sequence, whatever its case, and extends nothing. The key code itself carries at most one modifier
        // (Ctrl+Shift+Left is kbCtrlLeft), so the Shift state is the only place this is known.
        bool shiftHeld = keyState == 0
            && ((ev.What == Events.evKeyDown && (ev.keyDown.controlKeyState & Keys.kbShift) != 0)
                || (ev.What == Events.evMouseDown && (ev.mouse.controlKeyState & Keys.kbShift) != 0));
        ConvertEvent(ref ev);
        bool centerCursor = !CursorVisible();
        byte selectMode = 0;
        if (selecting || shiftHeld) selectMode = Views.smExtend;

        switch (ev.What)
        {
            case Events.evMouseDown:
            {
                // A double click selects the word under the mouse; dragging on extends the selection by whole words.
                bool byWord = ev.mouse.doubleClick;
                bool first = true;
                uint wordStart = 0, wordEnd = 0;
                do
                {
                    Lock();
                    if (ev.What == Events.evMouseAuto)
                    {
                        TPoint mouse = MakeLocal(ev.mouse.where);
                        TPoint d = delta;
                        if (mouse.x < 0) d.x--;
                        if (mouse.x >= size.x) d.x++;
                        if (mouse.y < 0) d.y--;
                        if (mouse.y >= size.y) d.y++;
                        ScrollTo(d.x, d.y);
                    }
                    uint p = GetMousePtr(ev.mouse.where);
                    if (byWord)
                    {
                        (uint start, uint end) = WordAt(p);
                        if (first) { wordStart = start; wordEnd = end; SetSelect(start, end, false); }
                        else if (start < wordStart) SetSelect(start, wordEnd, true);
                        else SetSelect(wordStart, Math.Max(wordEnd, end), false);
                    }
                    else
                    {
                        SetCurPtr(p, selectMode);
                        selectMode |= Views.smExtend;
                    }
                    first = false;
                    Unlock();
                } while (MouseEvent(ref ev,
                    (ushort)(Events.evMouseMove | Events.evMouseAuto)));
                break;
            }

            case Events.evKeyDown:
                string text = KeyText.PrintableText(ev.keyDown, includeTab: true);
                if (text.Length > 0)
                {
                    Lock();
                    BeginUndoGroup(EditorUndoKind.Typing);
                    try
                    {
                        if (overwrite && !HasSelection())
                            if (curPtr < bufLen && BufChar(curPtr) != '\n')
                                selEnd = NextGraphemeBoundary(curPtr);
                        char[] typed = text.ToCharArray();
                        typed = ReplaceUnpairedSurrogates(typed, typed.Length);
                        InsertText(typed, (uint)typed.Length, false);
                    }
                    finally { EndUndoGroup(); }
                    TrackCursor(centerCursor);
                    Unlock();
                }
                else return;
                break;

            case Events.evCommand:
                switch (ev.message.command)
                {
                    case Views.cmInsertText:
                        if (ev.message.infoPtr is TextInfo ti)
                            InsertText(ReplaceUnpairedSurrogates(ti.Text, ti.Text.Length), (uint)ti.Text.Length, false);
                        break;
                    default:
                        Lock();
                        // One command is one undo group; Backspace and Delete without a selection extend a run.
                        BeginUndoGroup(ev.message.command switch
                        {
                            Views.cmBackSpace when !HasSelection() => EditorUndoKind.DeleteBackward,
                            Views.cmDelChar when !HasSelection() => EditorUndoKind.DeleteForward,
                            _ => EditorUndoKind.Other,
                        });
                        try
                        {
                            switch (ev.message.command)
                            {
                                case Views.cmCut:        ClipCut(); break;
                                case Views.cmCopy:       ClipCopy(); break;
                                case Views.cmPaste:      ClipPaste(); break;
                                case Views.cmUndo:       Undo(); break;
                                case Views.cmRedo:       Redo(); break;
                                case Views.cmSelectAll:  SelectAll(); break;
                                case Views.cmClear:      DeleteSelect(); break;
                                case Views.cmCharLeft:
                                    SetCurPtr(PrevGraphemeBoundary(curPtr), selectMode); break;
                                case Views.cmCharRight:
                                    SetCurPtr(NextGraphemeBoundary(curPtr), selectMode); break;
                                case Views.cmWordLeft:
                                    SetCurPtr(PrevWord(curPtr), selectMode); break;
                                case Views.cmWordRight:
                                    SetCurPtr(NextWord(curPtr), selectMode); break;
                                case Views.cmLineStart:
                                    SetCurPtr(CachedLineStart(curPtr), selectMode); break;
                                case Views.cmLineEnd:
                                    SetCurPtr(CachedLineEnd(curPtr), selectMode); break;
                                case Views.cmLineUp:
                                    SetCurPtr(SnapToGrapheme(LineMove(curPtr, -1), false), selectMode); break;
                                case Views.cmLineDown:
                                    SetCurPtr(SnapToGrapheme(LineMove(curPtr, 1), false), selectMode); break;
                                case Views.cmPageUp:
                                    SetCurPtr(SnapToGrapheme(LineMove(curPtr, -(size.y - 1)), false), selectMode); break;
                                case Views.cmPageDown:
                                    SetCurPtr(SnapToGrapheme(LineMove(curPtr, size.y - 1), false), selectMode); break;
                                case Views.cmTextStart:
                                    SetCurPtr(0, selectMode); break;
                                case Views.cmTextEnd:
                                    SetCurPtr(bufLen, selectMode); break;
                                case Views.cmNewLine:
                                    NewLine(); break;
                                case Views.cmBackSpace:
                                    DeleteRange(PrevGraphemeBoundary(curPtr), curPtr, true); break;
                                case Views.cmDelChar:
                                    DeleteRange(curPtr, NextGraphemeBoundary(curPtr), true); break;
                                case Views.cmDelWord:
                                    DeleteRange(curPtr, NextWord(curPtr), true); break;
                                case Views.cmDelWordLeft:
                                    DeleteRange(PrevWord(curPtr), curPtr, true); break;
                                case Views.cmDelStart:
                                    DeleteRange(CachedLineStart(curPtr), curPtr, false); break;
                                case Views.cmDelEnd:
                                    DeleteRange(curPtr, CachedLineEnd(curPtr), false); break;
                                case Views.cmDelLine:
                                    DeleteRange(CachedLineStart(curPtr), NextLine(curPtr), false); break;
                                case Views.cmInsMode:
                                    ToggleInsMode(); break;
                                case Views.cmStartSelect:
                                    StartSelect(); break;
                                case Views.cmHideSelect:
                                    HideSelect(); break;
                                case Views.cmIndentMode:
                                    autoIndent = !autoIndent; break;
                                case Views.cmFind:
                                    Find(); break;
                                case Views.cmReplace:
                                    Replace(); break;
                                case Views.cmSearchAgain:
                                    DoSearchReplace(); break;
                                default:
                                    Unlock(); return;
                            }
                        }
                        finally { EndUndoGroup(); }
                        TrackCursor(centerCursor);
                        Unlock();
                        break;
                }
                break;

            case Events.evBroadcast:
                switch (ev.message.command)
                {
                    case Views.cmScrollBarChanged:
                        CheckScrollBar(ref ev, hScrollBar, ref delta.x);
                        CheckScrollBar(ref ev, vScrollBar, ref delta.y);
                        break;
                    default:
                        return;
                }
                break;

            case Events.evMouseWheel:
            {
                // Wheel scrolls the viewport without moving the
                // cursor or touching the buffer contents.
                bool up = (ev.mouse.eventFlags & Events.meWheelUp) != 0;
                bool down = (ev.mouse.eventFlags & Events.meWheelDown) != 0;
                if (!up && !down) return;
                ScrollTo(delta.x, delta.y + (up ? -WheelStep : WheelStep));
                break;
            }
        }
        ClearEvent(ref ev);
    }

    /// <inheritdoc />
    public override TPalette GetPalette() => Palette;

    // Wire: TView base + hScrollBar(ptr) + vScrollBar(ptr) + indicator(ptr)
    //       + bufSize(uint32) + canUndo(short).
    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TEditor(StreamableInit init) : base(init) { }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WritePointer(hScrollBar);
        os.WritePointer(vScrollBar);
        os.WritePointer(indicator);
        os.WriteInt(bufSize);
        os.WriteShort((ushort)(canUndo ? 1 : 0));
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        hScrollBar = isStream.ReadPointer() as TScrollBar;
        vScrollBar = isStream.ReadPointer() as TScrollBar;
        indicator  = isStream.ReadPointer() as TIndicator;
        bufSize    = isStream.ReadInt();
        canUndo    = isStream.ReadShort() != 0;
        selecting  = false;
        overwrite  = false;
        autoIndent = false;
        lockCount  = 0;
        keyState   = 0;
        InitBuffer();
        if (buffer != null)
            isValid = true;
        else
        {
            editorDialog(Views.edOutOfMemory, null);
            bufSize = 0;
        }
        lockCount = 0;
        Lock();
        SetBufLen(0);
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TEditor(StreamableInit.streamableInit);
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTEditor =
        new TStreamableClass("TEditor", () => new TEditor(StreamableInit.streamableInit), 0);
}

// Carrier for cmInsertText evCommand payload. The upstream `infoPtr` was a
// raw `void *` to a C string; our IInfo marker requires a typed wrapper.
/// <summary>Text payload for a cmInsertText command event.</summary>
public sealed class TextInfo : IInfo
{
    /// <summary>UTF-16 code units carried by the insertion command.</summary>
    public char[] Text;
    /// <summary>References the supplied text array; null becomes an empty array.</summary>
    public TextInfo(char[]? text) { Text = text ?? Array.Empty<char>(); }
    /// <summary>Copies the supplied string into a text payload; null becomes empty text.</summary>
    public TextInfo(string? text) { Text = (text ?? string.Empty).ToCharArray(); }
    /// <summary>Creates text by mapping each byte directly to the same-valued Unicode character.</summary>
    public TextInfo(byte[]? bytes)
    {
        if (bytes == null) { Text = Array.Empty<char>(); return; }
        Text = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) Text[i] = (char)bytes[i];
    }
}

/// <summary>Mutable search-dialog data carrying a pattern and search flags.</summary>
public sealed class TFindDialogRec : IInfo
{
    /// <summary>Null-terminated UTF-16 pattern edited by the search dialog.</summary>
    public char[] Find;
    /// <summary>ef-prefixed search option bits.</summary>
    public ushort Options;
    /// <summary>Copies a search pattern and stores its option flags.</summary>
    public TFindDialogRec(char[] f, ushort opts)
    {
        Find = (char[])f.Clone();
        Options = opts;
    }
    /// <summary>Maps byte pattern values directly to Unicode characters and stores the search flags.</summary>
    public TFindDialogRec(byte[] f, ushort opts)
        : this(BytesToChars(f), opts) { }

    private static char[] BytesToChars(byte[]? bytes)
    {
        if (bytes == null) return Array.Empty<char>();
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) chars[i] = (char)bytes[i];
        return chars;
    }
}

/// <summary>Mutable replacement-dialog data carrying search text, replacement text, and flags.</summary>
public sealed class TReplaceDialogRec : IInfo
{
    /// <summary>Null-terminated UTF-16 pattern to find.</summary>
    public char[] Find;
    /// <summary>Null-terminated UTF-16 replacement text.</summary>
    public char[] Replace;
    /// <summary>ef-prefixed search and replacement option bits.</summary>
    public ushort Options;
    /// <summary>Copies search and replacement patterns and stores the option flags.</summary>
    public TReplaceDialogRec(char[] f, char[] r, ushort opts)
    {
        Find = (char[])f.Clone();
        Replace = (char[])r.Clone();
        Options = opts;
    }
    /// <summary>Maps pattern bytes directly to Unicode characters and stores the replacement flags.</summary>
    public TReplaceDialogRec(byte[] f, byte[] r, ushort opts)
        : this(BytesToChars(f), BytesToChars(r), opts) { }

    private static char[] BytesToChars(byte[]? bytes)
    {
        if (bytes == null) return Array.Empty<char>();
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) chars[i] = (char)bytes[i];
        return chars;
    }
}
