namespace TSharpVision.CodeEditor.Syntax;

/// <summary>How the text of a <see cref="TEditor"/> changed since it was last observed.</summary>
internal readonly record struct EditorTextChange(EditorTextChangeKind Kind, int FirstLine, int RemovedLines, int InsertedLines)
{
    public static EditorTextChange None => new(EditorTextChangeKind.None, 0, 0, 0);
}

internal enum EditorTextChangeKind
{
    /// <summary>The known lines are exactly as they were.</summary>
    None,

    /// <summary>Lines were replaced; the lines after them are unchanged apart from their index.</summary>
    Edit,

    /// <summary>Everything from <see cref="EditorTextChange.FirstLine"/> on must be considered changed.</summary>
    Invalidate,
}

/// <summary>
/// Knows the lines of a <see cref="TEditor"/> that have been classified, and works out which of them an
/// edit touched — without any notification from the editor.
/// </summary>
/// <remarks>
/// <para>
/// <c>TEditor</c> raises no change events and its insertion method is not virtual, so edits cannot be
/// intercepted: typing, paste, undo, replace and programmatic <c>InsertText</c> all look the same from
/// outside. Instead the tracker keeps a copy of the prefix of the document it has handed out as lines
/// (never more) and, before each draw, compares the editor's gap buffer against it:
/// </para>
/// <list type="number">
/// <item>the common prefix locates the first changed line;</item>
/// <item>the common suffix of the rest, aligned by the change in length, locates the first line after
/// the edit whose text and preceding terminator are untouched;</item>
/// <item>the lines in between were replaced by however many lines the new text has there.</item>
/// </list>
/// <para>
/// Both comparisons run over contiguous spans with vectorised primitives, so an unchanged document
/// costs one memory comparison of the classified prefix per draw. The copy is bounded by the
/// highlighter's size limit. Line breaks are LF, which is what TSharpVision editors store.
/// </para>
/// </remarks>
internal sealed class EditorTextTracker : ISyntaxLineSource
{
    private const int Chunk = 4096;

    private readonly TEditor _editor;
    private readonly List<int> _lineStarts = new() { 0 };
    private readonly char[] _scratch = new char[Chunk];
    private char[] _snapshot = Array.Empty<char>();
    private int _snapshotLength;
    private int _knownLines;
    private bool _coversEnd;
    private int _documentLength;

    public EditorTextTracker(TEditor editor)
    {
        _editor = editor;
    }

    public int LineCount => Math.Max(1, _editor.limit.y);

    public int KnownLines => _knownLines;

    public int SnapshotLength => _snapshotLength;

    public void Reset()
    {
        _snapshot = Array.Empty<char>();
        _snapshotLength = 0;
        _knownLines = 0;
        _coversEnd = false;
        _lineStarts.Clear();
        _lineStarts.Add(0);
        _documentLength = (int)_editor.bufLen;
    }

    /// <summary>Compares the editor with what was observed, updates the copy, and reports the change.</summary>
    public EditorTextChange Synchronize()
    {
        int length = (int)_editor.bufLen;
        if (_knownLines == 0)
        {
            _documentLength = length;
            return EditorTextChange.None;
        }

        int compare = Math.Min(_snapshotLength, length);
        int common = CommonPrefix(compare);
        bool changed = common < _snapshotLength || (_coversEnd && length != _snapshotLength);
        if (!changed)
        {
            _documentLength = length;
            return EditorTextChange.None;
        }

        int firstLine = LineContaining(common);
        int delta = length - _documentLength;
        int oldTailEnd = _snapshotLength;
        int newTailEnd = _snapshotLength + delta;
        int suffix = 0;
        if (newTailEnd > common && oldTailEnd > common && newTailEnd <= length)
            suffix = CommonSuffix(common, oldTailEnd, newTailEnd);

        int oldEditEnd = oldTailEnd - suffix;

        // The first known line whose preceding terminator lies wholly after the edit.
        int unchanged = firstLine + 1;
        while (unchanged < _knownLines && _lineStarts[unchanged] - 1 < oldEditEnd) unchanged++;

        if (unchanged >= _knownLines)
        {
            Truncate(firstLine);
            _documentLength = length;
            return new EditorTextChange(EditorTextChangeKind.Invalidate, firstLine, 0, 0);
        }

        int firstStart = _lineStarts[firstLine];
        int newUnchangedStart = _lineStarts[unchanged] + delta;
        int inserted = CountLineBreaks(firstStart, newUnchangedStart);
        int removed = unchanged - firstLine;

        var starts = new List<int>(_knownLines + 1 + inserted - removed);
        for (int i = 0; i <= firstLine; i++) starts.Add(_lineStarts[i]);
        int position = firstStart;
        for (int i = 1; i < inserted; i++)
        {
            position = IndexOfLineBreak(position, newUnchangedStart) + 1;
            starts.Add(position);
        }

        for (int i = unchanged; i < _lineStarts.Count; i++) starts.Add(_lineStarts[i] + delta);

        int newLength = _snapshotLength + delta;
        EnsureCapacity(newLength);
        CopyFromEditor(common, _snapshot.AsSpan(common, newLength - common));

        _lineStarts.Clear();
        _lineStarts.AddRange(starts);
        _knownLines += inserted - removed;
        _snapshotLength = newLength;
        _documentLength = length;

        return new EditorTextChange(EditorTextChangeKind.Edit, firstLine, removed, inserted);
    }

    /// <summary>Makes sure the copy includes line <paramref name="lineIndex"/>, if the document has it.</summary>
    public void EnsureLine(int lineIndex)
    {
        while (_knownLines <= lineIndex && !_coversEnd) AppendLine();
    }

    /// <summary>Returns the index of the line that starts at or contains logical offset <paramref name="pointer"/>.</summary>
    public int LineOfPointer(uint pointer)
    {
        while (!_coversEnd && _lineStarts[_knownLines] <= pointer) AppendLine();
        return LineContaining((int)Math.Min(pointer, (uint)int.MaxValue));
    }

    public string GetLine(int index)
    {
        EnsureLine(index);
        if (index < 0 || index >= _knownLines) return string.Empty;

        int start = _lineStarts[index];
        int end = index == _knownLines - 1 && _coversEnd ? _snapshotLength : _lineStarts[index + 1] - 1;
        if (end > start && _snapshot[end - 1] == '\r') end--;
        return new string(_snapshot, start, Math.Max(0, end - start));
    }

    private void AppendLine()
    {
        int length = (int)_editor.bufLen;
        int start = _lineStarts[_knownLines];
        int lineBreak = IndexOfLineBreak(start, length);
        int end = lineBreak >= 0 ? lineBreak + 1 : length;

        EnsureCapacity(end);
        CopyFromEditor(start, _snapshot.AsSpan(start, end - start));
        _snapshotLength = end;
        _knownLines++;

        if (lineBreak >= 0) _lineStarts.Add(end);
        else _coversEnd = true;

        _documentLength = length;
    }

    private void Truncate(int lines)
    {
        _knownLines = lines;
        _snapshotLength = _lineStarts[lines];
        if (_lineStarts.Count > lines + 1) _lineStarts.RemoveRange(lines + 1, _lineStarts.Count - lines - 1);
        _coversEnd = false;
    }

    private int LineContaining(int offset)
    {
        int low = 0, high = _knownLines - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (_lineStarts[mid] <= offset) low = mid;
            else high = mid - 1;
        }

        return Math.Max(0, low);
    }

    private void EnsureCapacity(int length)
    {
        if (_snapshot.Length >= length) return;
        Array.Resize(ref _snapshot, Math.Max(length, Math.Min(int.MaxValue / 2, _snapshot.Length * 2)));
    }

    // ── gap-buffer access ────────────────────────────────────────────────────

    /// <summary>The logical range [start, end) as up to two contiguous spans of the gap buffer.</summary>
    private (ReadOnlyMemory<char> First, ReadOnlyMemory<char> Second) Segments(int start, int end)
    {
        char[] buffer = _editor.buffer ?? Array.Empty<char>();
        int gapStart = (int)_editor.curPtr;
        int gap = (int)_editor.gapLen;

        if (end <= gapStart) return (buffer.AsMemory(start, end - start), ReadOnlyMemory<char>.Empty);
        if (start >= gapStart) return (buffer.AsMemory(start + gap, end - start), ReadOnlyMemory<char>.Empty);
        return (buffer.AsMemory(start, gapStart - start), buffer.AsMemory(gapStart + gap, end - gapStart));
    }

    private void CopyFromEditor(int start, Span<char> destination)
    {
        (ReadOnlyMemory<char> first, ReadOnlyMemory<char> second) = Segments(start, start + destination.Length);
        first.Span.CopyTo(destination);
        second.Span.CopyTo(destination[first.Length..]);
    }

    private int CommonPrefix(int length)
    {
        (ReadOnlyMemory<char> first, ReadOnlyMemory<char> second) = Segments(0, length);
        int common = first.Span.CommonPrefixLength(_snapshot.AsSpan(0, first.Length));
        if (common < first.Length || second.IsEmpty) return common;
        return common + second.Span.CommonPrefixLength(_snapshot.AsSpan(first.Length, second.Length));
    }

    /// <summary>Common suffix of the snapshot's [start, oldEnd) and the editor's [start, newEnd).</summary>
    private int CommonSuffix(int start, int oldEnd, int newEnd)
    {
        int limit = Math.Min(oldEnd - start, newEnd - start);
        int matched = 0;
        while (matched < limit)
        {
            int count = Math.Min(Chunk, limit - matched);
            Span<char> current = _scratch.AsSpan(0, count);
            CopyFromEditor(newEnd - matched - count, current);
            ReadOnlySpan<char> previous = _snapshot.AsSpan(oldEnd - matched - count, count);

            if (current.SequenceEqual(previous))
            {
                matched += count;
                continue;
            }

            for (int i = count - 1; i >= 0 && current[i] == previous[i]; i--) matched++;
            break;
        }

        return matched;
    }

    private int CountLineBreaks(int start, int end)
    {
        if (end <= start) return 0;
        (ReadOnlyMemory<char> first, ReadOnlyMemory<char> second) = Segments(start, end);
        return first.Span.Count('\n') + second.Span.Count('\n');
    }

    private int IndexOfLineBreak(int start, int end)
    {
        if (end <= start) return -1;
        (ReadOnlyMemory<char> first, ReadOnlyMemory<char> second) = Segments(start, end);
        int at = first.Span.IndexOf('\n');
        if (at >= 0) return start + at;
        at = second.Span.IndexOf('\n');
        return at >= 0 ? start + first.Length + at : -1;
    }
}
