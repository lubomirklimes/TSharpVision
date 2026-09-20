using System;
using System.IO;
using System.Text;
using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>A linked node in the concrete <see cref="TOutline"/> tree.</summary>
public class TNode
{
    /// <summary>Creates an expanded leaf node with no following sibling.</summary>
    public TNode(string aText) : this(aText, null, null, true) { }

    /// <summary>Creates a node with linked children, a following sibling, and an initial expansion state.</summary>
    public TNode(string aText, TNode? aChildren, TNode? aNext, bool initialState = true)
    {
        ArgumentNullException.ThrowIfNull(aText);
        next = aNext;
        text = aText;
        childList = aChildren;
        expanded = initialState;
    }

    /// <summary>The next sibling, or <see langword="null"/> for the last sibling.</summary>
    public TNode? next;
    /// <summary>The text displayed for this node.</summary>
    public string text;
    /// <summary>The first child, whose <see cref="next"/> chain contains the remaining children.</summary>
    public TNode? childList;
    /// <summary>Whether the node's children are visible.</summary>
    public bool expanded;
}

/// <summary>
/// Scrollable abstract outline renderer whose data and expansion operations are supplied by derived classes.
/// </summary>
public abstract class TOutlineViewer : TScroller
{
    /// <summary>Historical stream type identifier retained for the abstract viewer surface.</summary>
    public new static readonly string Name = "TOutlineViewer";

    private static readonly TPalette _palette = new TPalette("\x06\x07\x03\x08", 4);
    private const string GraphCharacters = " │├└──+─";

    /// <summary>Creates an empty outline viewport with optional horizontal and vertical scrollbars.</summary>
    public TOutlineViewer(TRect bounds, TScrollBar? aHScrollBar, TScrollBar? aVScrollBar)
        : base(bounds, aHScrollBar, aVScrollBar)
    {
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        foc = 0;
    }

    /// <summary>Zero-based position of the focused visible node.</summary>
    public int foc;

    /// <summary>Changes a node's expansion state.</summary>
    public abstract void Adjust(TNode node, bool expand);
    /// <summary>Returns the child at a zero-based sibling index, or null when it is outside the child list.</summary>
    public abstract TNode? GetChild(TNode node, int i);
    /// <summary>Returns the number of children linked from a node.</summary>
    public abstract int GetNumChildren(TNode node);
    /// <summary>Returns the root node, or null for an empty outline.</summary>
    public abstract TNode? GetRoot();
    /// <summary>Returns the text displayed for a node.</summary>
    public abstract string GetText(TNode node);
    /// <summary>Returns whether a node has one or more children.</summary>
    public abstract bool HasChildren(TNode node);
    /// <summary>Returns whether a node's children are expanded.</summary>
    public abstract bool IsExpanded(TNode node);

    /// <inheritdoc />
    public override void Draw()
    {
        int lastDrawn = delta.y - 1;
        Span<TScreenChar> cells = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var buffer = new TDrawBuffer(cells);
        int lastPosition = Math.Min(limit.y, delta.y + size.y);
        for (int position = Math.Max(0, delta.y); position < lastPosition; position++)
        {
            GetTraversalData(position, out TNode? node, out int level, out long lines, out ushort flags);
            if (node == null) break;
            ushort color = position == foc && (state & Views.sfFocused) != 0
                ? GetColor(0x0202)
                : IsSelected(position) ? GetColor(0x0303) : GetColor(0x0401);
            buffer.moveChar(0, ' ', color, size.x);
            string graph = GetGraph(level, lines, flags);
            string text = GetText(node);
            string row = (flags & Views.ovExpanded) == 0
                ? string.Concat(graph, "~", text, "~")
                : string.Concat(graph, text);
            buffer.moveCStr(0, delta.x < row.Length ? row[delta.x..] : string.Empty, color);
            WriteLine(0, position - delta.y, size.x, 1, buffer);
            lastDrawn = position;
        }

        buffer.moveChar(0, ' ', GetColor(0x0401), size.x);
        int firstBlank = Math.Max(0, lastDrawn - delta.y + 1);
        if (firstBlank < size.y)
            WriteLine(0, firstBlank, size.x, size.y - firstBlank, buffer);
    }

    /// <summary>Records a newly focused visible position; derived viewers may override focus bookkeeping.</summary>
    public virtual void Focused(int i) => foc = i;

    /// <summary>Creates the default three-cell-per-level Turbo Vision outline graph prefix.</summary>
    public virtual string GetGraph(int level, long lines, ushort flags) =>
        CreateGraph(level, lines, flags, 3, 3, GraphCharacters);

    /// <summary>Returns the visible node at a zero-based display position, or null when no such row exists.</summary>
    public virtual TNode? GetNode(int i) =>
        FirstThat((_, _, _, position, _, _) => position == i);

    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    /// <summary>Returns whether a visible position is selected; by default only the focused position is selected.</summary>
    public virtual bool IsSelected(int i) => foc == i;

    /// <summary>Handles activation of a visible position; the historical base implementation does nothing.</summary>
    public virtual void Selected(int i) { }

    /// <summary>Recomputes content limits after tree data or expansion state changes.</summary>
    public void Update()
    {
        int count = 0;
        int maxWidth = 0;
        FirstThat((viewer, node, level, _, lines, flags) =>
        {
            count++;
            maxWidth = Math.Max(maxWidth, GetText(node).Length + GetGraph(level, lines, flags).Length);
            return false;
        });
        SetLimit(maxWidth, count);
        AdjustFocus(foc);
    }

    /// <summary>Expands a node and every descendant that has children.</summary>
    public void ExpandAll(TNode node)
    {
        if (!HasChildren(node))
            return;
        Adjust(node, true);
        int count = GetNumChildren(node);
        for (int i = 0; i < count; i++)
        {
            TNode? child = GetChild(node, i);
            if (child != null)
                ExpandAll(child);
        }
    }

    /// <summary>Traverses visible nodes in preorder and returns the first node for which the predicate returns true.</summary>
    public TNode? FirstThat(Func<TOutlineViewer, TNode, int, int, long, ushort, bool> test)
    {
        ArgumentNullException.ThrowIfNull(test);
        return Iterate(test, true);
    }

    /// <summary>Invokes an action predicate for every visible node in preorder and always completes the traversal.</summary>
    public TNode? ForEach(Func<TOutlineViewer, TNode, int, int, long, ushort, bool> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Iterate(action, false);
    }

    /// <summary>Builds a graph prefix from traversal level, continuation-line bits, flags, widths, and eight graph characters.</summary>
    public string CreateGraph(int level, long lines, ushort flags, int levWidth, int endWidth, string chars)
    {
        ArgumentNullException.ThrowIfNull(chars);
        if (level < 0) throw new ArgumentOutOfRangeException(nameof(level));
        if (levWidth < 1) throw new ArgumentOutOfRangeException(nameof(levWidth));
        if (endWidth < 1) throw new ArgumentOutOfRangeException(nameof(endWidth));
        if (chars.Length < 8) throw new ArgumentException("Eight graph characters are required.", nameof(chars));

        var graph = new StringBuilder(checked(level * levWidth + endWidth));
        for (int i = 0; i < level; i++, lines >>= 1)
        {
            graph.Append((lines & 1) != 0 ? chars[1] : chars[0]);
            graph.Append(chars[0], levWidth - 1);
        }

        int remaining = endWidth - 1;
        if (remaining > 0)
        {
            graph.Append((flags & Views.ovLast) != 0 ? chars[3] : chars[2]);
            remaining--;
            if (remaining > 0)
            {
                remaining--;
                if (remaining > 0)
                    graph.Append(chars[4], remaining);
                graph.Append((flags & Views.ovChildren) != 0 ? chars[5] : chars[4]);
            }
            graph.Append((flags & Views.ovExpanded) != 0 ? chars[7] : chars[6]);
        }
        return graph.ToString();
    }

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evMouseDown)
        {
            int newFocus = foc;
            int count = 0;
            byte dragged = 0;
            TPoint mouse;
            do
            {
                if (dragged < 2) dragged++;
                mouse = MakeLocal(@event.mouse.where);
                if (MouseInView(@event.mouse.where))
                    newFocus = delta.y + mouse.y;
                else if (@event.What == Events.evMouseAuto && ++count == 3)
                {
                    count = 0;
                    if (mouse.y < 0) newFocus--;
                    if (mouse.y >= size.y) newFocus++;
                }
                if (foc != newFocus)
                {
                    AdjustFocus(newFocus);
                    DrawView();
                }
            } while (MouseEvent(ref @event, (ushort)(Events.evMouseMove | Events.evMouseAuto)));

            mouse = MakeLocal(@event.mouse.where);
            if ((@event.mouse.eventFlags & Events.meDoubleClick) != 0)
                Selected(foc);
            else if (dragged < 2)
            {
                GetTraversalData(foc, out TNode? node, out int level, out long lines, out ushort flags);
                if (node != null && mouse.x < GetGraph(level, lines, flags).Length)
                {
                    Adjust(node, !IsExpanded(node));
                    Update();
                    DrawView();
                }
            }
        }
        else if (@event.What == Events.evKeyDown)
        {
            int newFocus = foc;
            switch (CtrlToArrow(@event.keyDown.keyCode))
            {
                case Keys.kbUp:
                case Keys.kbLeft: newFocus--; break;
                case Keys.kbDown:
                case Keys.kbRight: newFocus++; break;
                case Keys.kbPgDn: newFocus += size.y - 1; break;
                case Keys.kbPgUp: newFocus -= size.y - 1; break;
                case Keys.kbHome: newFocus = delta.y; break;
                case Keys.kbEnd: newFocus = delta.y + size.y - 1; break;
                case Keys.kbCtrlPgUp: newFocus = 0; break;
                case Keys.kbCtrlPgDn: newFocus = limit.y - 1; break;
                case Keys.kbCtrlEnter:
                case Keys.kbEnter: Selected(newFocus); break;
                default:
                    TNode? node = GetNode(newFocus);
                    switch (@event.keyDown.charScan.charCode)
                    {
                        case (byte)'-': if (node != null) Adjust(node, false); break;
                        case (byte)'+': if (node != null) Adjust(node, true); break;
                        case (byte)'*': if (node != null) ExpandAll(node); break;
                        default: return;
                    }
                    Update();
                    break;
            }
            ClearEvent(ref @event);
            AdjustFocus(newFocus);
            DrawView();
        }
    }

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if ((aState & Views.sfFocused) != 0)
            DrawView();
    }

    /// <summary>Releases links owned by a node graph; managed nodes require no explicit memory release.</summary>
    protected static void DisposeNode(TNode? node)
    {
        if (node == null) return;
        DisposeNode(node.childList);
        DisposeNode(node.next);
        node.childList = null;
        node.next = null;
    }

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TOutlineViewer(StreamableInit init) : base(init) { }

    /// <summary>The historical factory cannot construct this abstract managed type.</summary>
    /// <exception cref="NotSupportedException">Always thrown because <see cref="TOutlineViewer"/> is abstract.</exception>
    public new static TStreamable Build() =>
        throw new NotSupportedException("TOutlineViewer is abstract; stream a concrete TOutline instead.");

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteInt((uint)foc);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        foc = (int)isStream.ReadInt();
        return this;
    }

    private void AdjustFocus(int newFocus)
    {
        if (limit.y == 0)
            newFocus = 0;
        else if (newFocus < 0)
            newFocus = 0;
        else if (newFocus >= limit.y)
            newFocus = limit.y - 1;
        if (foc != newFocus)
            Focused(newFocus);
        if (limit.y == 0)
            return;
        if (newFocus < delta.y)
            ScrollTo(delta.x, newFocus);
        else if (newFocus - size.y >= delta.y)
            ScrollTo(delta.x, newFocus - size.y + 1);
    }

    private static ushort CtrlToArrow(ushort keyCode)
    {
        return (byte)keyCode switch
        {
            (byte)Keys.kbCtrlS => Keys.kbLeft,
            (byte)Keys.kbCtrlD => Keys.kbRight,
            (byte)Keys.kbCtrlE => Keys.kbUp,
            (byte)Keys.kbCtrlX => Keys.kbDown,
            (byte)Keys.kbCtrlA => Keys.kbHome,
            (byte)Keys.kbCtrlF => Keys.kbEnd,
            (byte)Keys.kbCtrlG => Keys.kbDel,
            (byte)Keys.kbCtrlV => Keys.kbIns,
            (byte)Keys.kbCtrlR => Keys.kbPgUp,
            (byte)Keys.kbCtrlC => Keys.kbPgDn,
            (byte)Keys.kbCtrlH => Keys.kbBack,
            _ => keyCode
        };
    }

    private TNode? Iterate(Func<TOutlineViewer, TNode, int, int, long, ushort, bool> action, bool checkResult)
    {
        int position = -1;
        return TraverseTree(action, checkResult, GetRoot(), 0, 0, true, ref position);
    }

    private TNode? TraverseTree(Func<TOutlineViewer, TNode, int, int, long, ushort, bool> action,
        bool checkResult, TNode? current, int level, long lines, bool lastChild, ref int position)
    {
        if (current == null) return null;
        bool children = HasChildren(current);
        bool expanded = IsExpanded(current);
        ushort flags = 0;
        if (lastChild) flags |= Views.ovLast;
        if (children && expanded) flags |= Views.ovChildren;
        if (!children || expanded) flags |= Views.ovExpanded;
        position++;
        if (action(this, current, level, position, lines, flags) && checkResult)
            return current;
        if (children && expanded)
        {
            int childCount = GetNumChildren(current);
            if (!lastChild) lines |= 1L << level;
            for (int i = 0; i < childCount; i++)
            {
                TNode? found = TraverseTree(action, checkResult, GetChild(current, i), level + 1,
                    lines, i == childCount - 1, ref position);
                if (found != null) return found;
            }
        }
        return null;
    }

    private void GetTraversalData(int position, out TNode? node, out int level, out long lines, out ushort flags)
    {
        node = null;
        level = 0;
        lines = 0;
        flags = 0;
        TNode? foundNode = null;
        int foundLevel = 0;
        long foundLines = 0;
        ushort foundFlags = 0;
        foundNode = FirstThat((_, current, currentLevel, currentPosition, currentLines, currentFlags) =>
        {
            if (currentPosition != position) return false;
            foundLevel = currentLevel;
            foundLines = currentLines;
            foundFlags = currentFlags;
            return true;
        });
        node = foundNode;
        level = foundLevel;
        lines = foundLines;
        flags = foundFlags;
    }
}

/// <summary>A concrete linked-node outline with recursive stream persistence.</summary>
public class TOutline : TOutlineViewer
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TOutline";

    /// <summary>Creates a linked-node outline; a null root represents an empty outline.</summary>
    public TOutline(TRect bounds, TScrollBar? aHScrollBar, TScrollBar? aVScrollBar, TNode? aRoot)
        : base(bounds, aHScrollBar, aVScrollBar)
    {
        root = aRoot;
        Update();
    }

    /// <summary>The outline root, or null for an empty outline.</summary>
    public TNode? root;

    /// <inheritdoc />
    public override void Adjust(TNode node, bool expand) => node.expanded = expand;
    /// <inheritdoc />
    public override TNode? GetRoot() => root;
    /// <inheritdoc />
    public override int GetNumChildren(TNode node)
    {
        int count = 0;
        for (TNode? child = node.childList; child != null; child = child.next) count++;
        return count;
    }
    /// <inheritdoc />
    public override TNode? GetChild(TNode node, int i)
    {
        TNode? child = node.childList;
        while (i > 0 && child != null) { i--; child = child.next; }
        return i == 0 ? child : null;
    }
    /// <inheritdoc />
    public override string GetText(TNode node) => node.text;
    /// <inheritdoc />
    public override bool IsExpanded(TNode node) => node.expanded;
    /// <inheritdoc />
    public override bool HasChildren(TNode node) => node.childList != null;

    /// <inheritdoc />
    public override void ShutDown()
    {
        DisposeNode(root);
        root = null;
        base.ShutDown();
    }

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTOutline =
        new TStreamableClass(Name, () => new TOutline(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TOutline(StreamableInit init) : base(init) { }

    /// <summary>Writes one node and its child and sibling chains in historical preorder.</summary>
    protected virtual void WriteNode(TNode node, Opstream os)
    {
        os.WriteByte(node.next != null ? (byte)1 : (byte)0);
        os.WriteByte(node.expanded ? (byte)1 : (byte)0);
        os.WriteInt((uint)GetNumChildren(node));
        os.WriteString(node.text);
        if (node.childList != null) WriteNode(node.childList, os);
        if (node.next != null) WriteNode(node.next, os);
    }

    /// <summary>Reads one node and its recursively stored child and sibling chains.</summary>
    protected virtual TNode ReadNode(Ipstream isStream)
    {
        byte more = isStream.ReadByte();
        byte expand = isStream.ReadByte();
        uint childCount = isStream.ReadInt();
        string text = isStream.ReadString() ?? throw new InvalidDataException("An outline node must have text.");
        TNode? children = childCount != 0 ? ReadNode(isStream) : null;
        TNode? next = more != 0 ? ReadNode(isStream) : null;
        return new TNode(text, children, next, expand != 0);
    }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteByte(root != null ? (byte)1 : (byte)0);
        if (root != null) WriteNode(root, os);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        root = isStream.ReadByte() != 0 ? ReadNode(isStream) : null;
        Update();
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TOutline(StreamableInit.streamableInit);
    /// <inheritdoc />
    public override string StreamableName() => Name;
}
