using System.Collections.Generic;
using System.Text;

namespace TSharpVision;

/// <summary>How an undo record may combine with the record before it.</summary>
internal enum EditorUndoKind
{
    /// <summary>A record of its own: paste, cut, newline, replace, programmatic edits.</summary>
    Other,
    /// <summary>Typed text; extends a typing record that ends where it starts.</summary>
    Typing,
    /// <summary>Backspace; extends a backward-deletion record that starts where it ends.</summary>
    DeleteBackward,
    /// <summary>Delete; extends a forward-deletion record at the same offset.</summary>
    DeleteForward,
}

/// <summary>One replacement of the text: <see cref="Removed"/> at <see cref="Offset"/> became <see cref="Inserted"/>.</summary>
internal sealed class EditorUndoOperation
{
    public EditorUndoOperation(uint offset, string removed, string inserted)
    {
        Offset = offset;
        Removed = new StringBuilder(removed);
        Inserted = new StringBuilder(inserted);
    }

    /// <summary>Logical UTF-16 offset where the replacement starts.</summary>
    public uint Offset;
    /// <summary>Text the replacement removed.</summary>
    public readonly StringBuilder Removed;
    /// <summary>Text the replacement inserted.</summary>
    public readonly StringBuilder Inserted;

    public int Size => Removed.Length + Inserted.Length;
}

/// <summary>Operations that one Undo or Redo reverses or reapplies together.</summary>
internal sealed class EditorUndoGroup
{
    public EditorUndoGroup(EditorUndoKind kind) => Kind = kind;

    public readonly EditorUndoKind Kind;
    public readonly List<EditorUndoOperation> Operations = new();
    public long Size;
}

/// <summary>
/// Multi-level undo history of a <see cref="TEditor"/>: a list of groups with a position separating the groups that
/// can be undone from those that can be redone, a saved position, and group-count and text-size limits.
/// </summary>
/// <remarks>
/// The history only holds text. Offsets are logical UTF-16 offsets that are valid for the document exactly as it was
/// when the operation ran, so the editor must clear the history whenever text changes without being recorded.
/// </remarks>
internal sealed class EditorUndoHistory
{
    /// <summary>Saved position meaning "the saved text can no longer be reached by undo or redo".</summary>
    public const int Unreachable = -1;

    private readonly List<EditorUndoGroup> groups = new();
    private int groupLimit;
    private int textLimit;
    private long textSize;
    private bool lastGroupOpen;
    private int batchDepth;
    private EditorUndoKind batchKind;
    private EditorUndoGroup? batchGroup;

    public EditorUndoHistory(int groupLimit, int textLimit)
    {
        this.groupLimit = groupLimit;
        this.textLimit = textLimit;
    }

    /// <summary>Number of groups that can be undone; groups at and after it can be redone.</summary>
    public int Position { get; private set; }

    /// <summary>Position at which the text equals the saved text, or <see cref="Unreachable"/>.</summary>
    public int SavedPosition { get; set; }

    public int Count => groups.Count;

    public long TextSize => textSize;

    public bool CanUndo => Position > 0;

    public bool CanRedo => Position < groups.Count;

    public bool IsEnabled => groupLimit > 0 && textLimit > 0;

    public bool IsAtSavedPosition => Position == SavedPosition;

    public int GroupLimit
    {
        get => groupLimit;
        set { groupLimit = value; Trim(); }
    }

    public int TextLimit
    {
        get => textLimit;
        set { textLimit = value; Trim(); }
    }

    /// <summary>The most recent group while no caret move, Undo or Redo has closed it.</summary>
    public EditorUndoGroup? OpenGroup => batchGroup ?? (lastGroupOpen && Position == groups.Count && Position > 0 ? groups[Position - 1] : null);

    /// <summary>Forgets every group; the current text becomes the saved text only if <paramref name="saved"/>.</summary>
    public void Clear(bool saved)
    {
        groups.Clear();
        textSize = 0;
        Position = 0;
        SavedPosition = saved ? 0 : Unreachable;
        lastGroupOpen = false;
        batchGroup = null;
    }

    /// <summary>Ends the group that recording could otherwise extend, as a caret move does.</summary>
    public void Close() => lastGroupOpen = false;

    /// <summary>Starts collecting every following operation into one group; calls nest, and the outermost kind wins.</summary>
    public void Begin(EditorUndoKind kind)
    {
        if (batchDepth++ == 0)
        {
            batchKind = kind;
            batchGroup = null;
        }
    }

    /// <summary>Ends a <see cref="Begin"/>; the outermost call merges the group into the previous one when the kinds allow it.</summary>
    public void End()
    {
        if (batchDepth == 0 || --batchDepth > 0) return;
        EditorUndoGroup? group = batchGroup;
        batchGroup = null;
        if (group == null) return;

        bool previousOpen = lastGroupOpen;
        lastGroupOpen = true;
        if (previousOpen && TryMerge(group)) return;
        Trim();
    }

    /// <summary>Records one replacement, discarding the groups that could be redone.</summary>
    public void Record(uint offset, string removed, string inserted)
    {
        if (!IsEnabled) return;
        DiscardRedo();

        var operation = new EditorUndoOperation(offset, removed, inserted);
        if (batchDepth > 0)
        {
            if (batchGroup == null)
            {
                batchGroup = new EditorUndoGroup(batchKind);
                groups.Add(batchGroup);
                Position = groups.Count;
            }
            batchGroup.Operations.Add(operation);
            batchGroup.Size += operation.Size;
            textSize += operation.Size;
            return;
        }

        var group = new EditorUndoGroup(EditorUndoKind.Other);
        group.Operations.Add(operation);
        group.Size = operation.Size;
        textSize += group.Size;
        groups.Add(group);
        Position = groups.Count;
        // Open until a caret move, Undo or Redo; a group of kind Other never merges, so this only drives the counters.
        lastGroupOpen = true;
        Trim();
    }

    /// <summary>Returns the group to undo and moves the position before it.</summary>
    public EditorUndoGroup TakeUndo()
    {
        lastGroupOpen = false;
        return groups[--Position];
    }

    /// <summary>Returns the group to redo and moves the position after it.</summary>
    public EditorUndoGroup TakeRedo()
    {
        lastGroupOpen = false;
        return groups[Position++];
    }

    private void DiscardRedo()
    {
        if (Position >= groups.Count) return;
        for (int i = Position; i < groups.Count; i++) textSize -= groups[i].Size;
        groups.RemoveRange(Position, groups.Count - Position);
        if (SavedPosition > Position) SavedPosition = Unreachable;
        lastGroupOpen = false;
    }

    // Folds a just-finished single-operation group into the previous group when it continues it.
    private bool TryMerge(EditorUndoGroup group)
    {
        if (group.Kind == EditorUndoKind.Other || group.Operations.Count != 1 || groups.Count < 2) return false;
        EditorUndoGroup previous = groups[^2];
        if (previous.Kind != group.Kind || previous.Operations.Count != 1) return false;
        // The saved text lies between the two groups: merging would make it unreachable.
        if (SavedPosition == groups.Count - 1) return false;

        EditorUndoOperation last = previous.Operations[0];
        EditorUndoOperation next = group.Operations[0];
        switch (group.Kind)
        {
            case EditorUndoKind.Typing when next.Offset == last.Offset + (uint)last.Inserted.Length:
                last.Removed.Append(next.Removed);
                last.Inserted.Append(next.Inserted);
                break;
            case EditorUndoKind.DeleteBackward when next.Inserted.Length == 0 && last.Inserted.Length == 0
                                                    && next.Offset + (uint)next.Removed.Length == last.Offset:
                last.Offset = next.Offset;
                last.Removed.Insert(0, next.Removed);
                break;
            case EditorUndoKind.DeleteForward when next.Inserted.Length == 0 && last.Inserted.Length == 0
                                                   && next.Offset == last.Offset:
                last.Removed.Append(next.Removed);
                break;
            default:
                return false;
        }

        previous.Size += group.Size;
        groups.RemoveAt(groups.Count - 1);
        Position = groups.Count;
        Trim();
        return true;
    }

    // Drops the oldest groups until both limits hold; a disabled history keeps nothing.
    private void Trim()
    {
        if (!IsEnabled)
        {
            if (groups.Count > 0) Clear(saved: IsAtSavedPosition);
            return;
        }

        while (groups.Count > 0 && (groups.Count > groupLimit || textSize > textLimit))
        {
            if (Position > 0)
            {
                // The oldest undoable group goes first.
                if (groups[0] == batchGroup) return;
                textSize -= groups[0].Size;
                groups.RemoveAt(0);
                Position--;
                SavedPosition = SavedPosition > 0 ? SavedPosition - 1 : Unreachable;
            }
            else
            {
                // Nothing left to undo: the furthest redo goes, so the remaining redo chain stays contiguous.
                textSize -= groups[^1].Size;
                groups.RemoveAt(groups.Count - 1);
                if (SavedPosition > groups.Count) SavedPosition = Unreachable;
            }
        }
    }
}
