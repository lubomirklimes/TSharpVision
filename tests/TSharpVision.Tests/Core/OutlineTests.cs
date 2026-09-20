using System.Reflection;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class OutlineTests : IDisposable
{
    private readonly DriverScope _driver = new();

    public void Dispose() => _driver.Dispose();

    private static TNode Tree(bool expanded = true)
    {
        var grandchild = new TNode("grandchild");
        var first = new TNode("first", grandchild, null, expanded);
        var second = new TNode("second");
        first.next = second;
        return new TNode("root", first, null, expanded);
    }

    [Fact]
    public void Constants_MatchBorlandOutlineHeader()
    {
        Assert.Equal((ushort)301, Views.cmOutlineItemSelected);
        Assert.Equal((ushort)1, Views.ovExpanded);
        Assert.Equal((ushort)2, Views.ovChildren);
        Assert.Equal((ushort)4, Views.ovLast);
    }

    [Fact]
    public void ApiMetadata_PreservesHistoricalInheritanceAndVirtualHooks()
    {
        Assert.True(typeof(TOutlineViewer).IsAbstract);
        Assert.Equal(typeof(TScroller), typeof(TOutlineViewer).BaseType);
        Assert.Equal(typeof(TOutlineViewer), typeof(TOutline).BaseType);
        Assert.True(typeof(TOutlineViewer).GetMethod(nameof(TOutlineViewer.Adjust))!.IsAbstract);
        Assert.True(typeof(TOutlineViewer).GetMethod(nameof(TOutlineViewer.Selected))!.IsVirtual);
        Assert.True(typeof(TOutline).GetMethod(nameof(TOutline.GetChild))!.IsVirtual);
    }

    [Fact]
    public void Node_Constructors_PreserveLinkedGraphAndDefaults()
    {
        var child = new TNode("child");
        var next = new TNode("next");
        var node = new TNode("node", child, next, false);
        Assert.Same(child, node.childList);
        Assert.Same(next, node.next);
        Assert.False(node.expanded);
        Assert.True(new TNode("leaf").expanded);
    }

    [Fact]
    public void EmptyOutline_HasZeroLimitsAndStableFocus()
    {
        var outline = new TOutline(new TRect(0, 0, 20, 5), null, null, null);
        Assert.Equal(0, outline.limit.x);
        Assert.Equal(0, outline.limit.y);
        Assert.Equal(0, outline.foc);
        Assert.Null(outline.GetNode(0));
    }

    [Fact]
    public void VisibleTraversal_IsPreorderAndIgnoresRootSibling()
    {
        var root = Tree();
        root.next = new TNode("not-another-root");
        var outline = new TOutline(new TRect(0, 0, 30, 8), null, null, root);
        var texts = new List<string>();
        outline.ForEach((_, node, _, _, _, _) => { texts.Add(node.text); return false; });
        Assert.Equal(new[] { "root", "first", "grandchild", "second" }, texts);
    }

    [Fact]
    public void CollapsedNode_HidesDescendantsButKeepsFollowingSibling()
    {
        var root = Tree();
        root.childList!.expanded = false;
        var outline = new TOutline(new TRect(0, 0, 30, 8), null, null, root);
        Assert.Equal(3, outline.limit.y);
        Assert.Equal("second", outline.GetNode(2)!.text);
    }

    [Fact]
    public void Traversal_ReportsExactLevelsPositionsLinesAndFlags()
    {
        var outline = new TOutline(new TRect(0, 0, 30, 8), null, null, Tree());
        var seen = new List<(string, int, int, long, ushort)>();
        outline.ForEach((_, node, level, position, lines, flags) =>
        {
            seen.Add((node.text, level, position, lines, flags));
            return false;
        });
        Assert.Equal(("root", 0, 0, 0L, (ushort)7), seen[0]);
        Assert.Equal(("first", 1, 1, 0L, (ushort)3), seen[1]);
        Assert.Equal(("grandchild", 2, 2, 2L, (ushort)5), seen[2]);
        Assert.Equal(("second", 1, 3, 0L, (ushort)5), seen[3]);
    }

    [Fact]
    public void FirstThat_StopsAtFirstMatch_WhileForEachDoesNot()
    {
        var outline = new TOutline(new TRect(0, 0, 30, 8), null, null, Tree());
        int firstCalls = 0;
        TNode? found = outline.FirstThat((_, _, _, position, _, _) => { firstCalls++; return position == 1; });
        int allCalls = 0;
        TNode? ignored = outline.ForEach((_, _, _, _, _, _) => { allCalls++; return true; });
        Assert.Equal("first", found!.text);
        Assert.Equal(2, firstCalls);
        Assert.Equal(4, allCalls);
        Assert.Null(ignored);
    }

    [Fact]
    public void Update_ComputesVisibleCountAndMaximumGraphPlusTextWidth()
    {
        var outline = new TOutline(new TRect(0, 0, 30, 8), null, null, Tree());
        Assert.Equal(4, outline.limit.y);
        Assert.Equal("      └──grandchild".Length, outline.limit.x);
    }

    [Fact]
    public void CreateGraph_ReproducesHistoricalCharacterSelection()
    {
        var outline = new TOutline(new TRect(0, 0, 10, 3), null, null, new TNode("x"));
        const string chars = " abcdefg";
        Assert.Equal("cdg", outline.CreateGraph(0, 0, Views.ovLast | Views.ovExpanded, 3, 3, chars));
        Assert.Equal("a     beg", outline.CreateGraph(2, 1, Views.ovChildren | Views.ovExpanded, 3, 3, chars));
        Assert.Equal(string.Empty, outline.CreateGraph(0, 0, 0, 3, 1, chars));
    }

    [Fact]
    public void DefaultGraph_UsesUnicodeCp437LineDrawingAdaptation()
    {
        var outline = new TOutline(new TRect(0, 0, 10, 3), null, null, new TNode("x"));
        Assert.Equal("└──", outline.GetGraph(0, 0, Views.ovLast | Views.ovExpanded));
        Assert.Equal("└─+", outline.GetGraph(0, 0, Views.ovLast));
    }

    [Fact]
    public void ExpandAll_ExpandsEveryNonLeafDescendant()
    {
        var root = Tree(false);
        var outline = new TOutline(new TRect(0, 0, 30, 8), null, null, root);
        outline.ExpandAll(root);
        outline.Update();
        Assert.True(root.expanded);
        Assert.True(root.childList!.expanded);
        Assert.Equal(4, outline.limit.y);
    }

    [Fact]
    public void CollapseByKeyboard_UpdatesVisibleRowsAndKeepsParentFocus()
    {
        var outline = new TOutline(new TRect(0, 0, 30, 8), null, null, Tree());
        outline.foc = 1;
        TEvent ev = Key(0, '-');
        outline.HandleEvent(ref ev);
        Assert.False(outline.root!.childList!.expanded);
        Assert.Equal(3, outline.limit.y);
        Assert.Equal(1, outline.foc);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void CollapseHidingFocusedDescendant_ClampsNumericFocusToLastVisibleRow()
    {
        var outline = new TOutline(new TRect(0, 0, 30, 3), null, null, Tree());
        outline.foc = 3;
        outline.Adjust(outline.root!, false);
        outline.Update();
        Assert.Equal(1, outline.limit.y);
        Assert.Equal(0, outline.foc);
    }

    [Fact]
    public void StructuralUpdates_RefreshScrollbarRangeAndFocusScrollRequest()
    {
        var bar = new TScrollBar(new TRect(30, 0, 31, 3));
        var outline = new TOutline(new TRect(0, 0, 30, 2), null, bar, Tree());
        Assert.Equal(2, bar.maxVal);
        TEvent end = Key(Keys.kbCtrlPgDn);
        outline.HandleEvent(ref end);
        Assert.Equal(2, bar.value);

        TEvent changed = default;
        changed.What = Events.evBroadcast;
        changed.message.command = Views.cmScrollBarChanged;
        changed.message.infoPtr = bar;
        outline.HandleEvent(ref changed);
        Assert.Equal(2, outline.delta.y);

        outline.Adjust(outline.root!, false);
        outline.Update();
        Assert.Equal(0, bar.maxVal);
        Assert.Equal(0, outline.foc);
    }

    [Fact]
    public void ArrowPageAndWordStarKeys_ApplyHistoricalFocusPolicy()
    {
        var outline = new TOutline(new TRect(0, 0, 30, 3), null, null, Tree());
        TEvent down = Key(Keys.kbDown); outline.HandleEvent(ref down);
        Assert.Equal(1, outline.foc);
        TEvent ctrlX = Key(Keys.kbCtrlX); outline.HandleEvent(ref ctrlX);
        Assert.Equal(2, outline.foc);
        TEvent page = Key(Keys.kbPgDn); outline.HandleEvent(ref page);
        Assert.Equal(3, outline.foc);
        TEvent home = Key(Keys.kbCtrlPgUp); outline.HandleEvent(ref home);
        Assert.Equal(0, outline.foc);
    }

    [Fact]
    public void ActivationKeys_CallVirtualSelectedWithoutBroadcastingByDefault()
    {
        var outline = new SelectionProbe(Tree());
        TEvent enter = Key(Keys.kbEnter); outline.HandleEvent(ref enter);
        Assert.Equal(0, outline.SelectedPosition);
        TEvent ctrlEnter = Key(Keys.kbCtrlEnter); outline.HandleEvent(ref ctrlEnter);
        Assert.Equal(0, outline.SelectedPosition);
    }

    [Fact]
    public void SingleGraphClick_TogglesExpansion_AndDoubleClickActivates()
    {
        var outline = new SelectionProbe(Tree());
        outline.options |= Views.ofFirstClick;
        TEvent click = Mouse(0, 0); outline.HandleEvent(ref click);
        Assert.False(outline.root!.expanded);
        TEvent twice = Mouse(0, 0, true); outline.HandleEvent(ref twice);
        Assert.Equal(0, outline.SelectedPosition);
    }

    [Fact]
    public void SingleItemClick_FocusesTheClickedVisibleRow()
    {
        var outline = new SelectionProbe(Tree());
        outline.options |= Views.ofFirstClick;
        TEvent click = Mouse(10, 2);
        outline.HandleEvent(ref click);
        Assert.Equal(2, outline.foc);
    }

    [Fact]
    public void Draw_RendersGraphTextAndClearsUnusedRows()
    {
        var outline = new DrawingProbe(new TNode("root", new TNode("child"), null, false));
        outline.Draw();
        Assert.StartsWith("└─+root", outline.Rows[0]);
        Assert.Equal(new string(' ', 20), outline.Rows[1]);
    }

    [Fact]
    public void Draw_UsesNormalAndCollapsedTextPaletteAttributes()
    {
        var outline = new DrawingProbe(new TNode("root", new TNode("child"), null, false));
        outline.Draw();
        Assert.Equal((TColorAttr)6, outline.Attributes[0][0]);
        Assert.Equal((TColorAttr)8, outline.Attributes[0][3]);
    }

    [Fact]
    public void Palette_HasHistoricalFourEntryMapping()
    {
        var outline = new TOutline(new TRect(0, 0, 10, 3), null, null, new TNode("x"));
        Assert.Equal(4, outline.GetPalette().Size);
    }

    [Fact]
    public void Streaming_RoundTripsTreeFocusAndExpansion()
    {
        var source = new TOutline(new TRect(1, 2, 31, 10), null, null, Tree());
        source.foc = 2;
        source.root!.childList!.expanded = false;
        using var stream = new MemoryStream();
        source.Write(new Opstream(stream));
        stream.Position = 0;
        var restored = (TOutline)TOutline.Build();
        restored.Read(new Ipstream(stream));
        Assert.Equal(2, restored.foc);
        Assert.Equal("root", restored.root!.text);
        Assert.False(restored.root.childList!.expanded);
        Assert.Equal("second", restored.root.childList.next!.text);
    }

    [Fact]
    public void Streaming_RoundTripsEmptyOutline()
    {
        var source = new TOutline(new TRect(0, 0, 20, 4), null, null, null);
        using var stream = new MemoryStream();
        source.Write(new Opstream(stream));
        stream.Position = 0;
        var restored = (TOutline)TOutline.Build();
        restored.Read(new Ipstream(stream));
        Assert.Null(restored.root);
        Assert.Equal(0, restored.limit.y);
    }

    [Fact]
    public void Outline_HasNoInventedDataTransferRole()
    {
        var outline = new TOutline(new TRect(0, 0, 20, 4), null, null, new TNode("root"));
        Assert.Equal((ushort)0, outline.DataSize());
    }

    [Fact]
    public void ShutDown_ReleasesOwnedTreeLinksAndRoot()
    {
        TNode root = Tree();
        TNode child = root.childList!;
        var outline = new TOutline(new TRect(0, 0, 20, 4), null, null, root);
        outline.ShutDown();
        Assert.Null(outline.root);
        Assert.Null(root.childList);
        Assert.Null(child.next);
    }

    [Fact]
    public void StreamableRegistration_IncludesConcreteOutline()
    {
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
        Assert.Same(TOutline.StreamableClassTOutline, Pstream.types.Lookup(TOutline.Name));
    }

    [Fact]
    public void PublicNullability_MarksOnlyOptionalTreeAndScrollbarReferencesNullable()
    {
        var context = new NullabilityInfoContext();
        Assert.Equal(NullabilityState.Nullable, context.Create(typeof(TNode).GetField(nameof(TNode.next))!).ReadState);
        Assert.Equal(NullabilityState.Nullable, context.Create(typeof(TNode).GetField(nameof(TNode.childList))!).ReadState);
        Assert.Equal(NullabilityState.NotNull, context.Create(typeof(TNode).GetField(nameof(TNode.text))!).ReadState);
        Assert.Equal(NullabilityState.Nullable, context.Create(typeof(TOutline).GetField(nameof(TOutline.root))!).ReadState);
        ParameterInfo[] parameters = typeof(TOutline).GetConstructors().Single().GetParameters();
        Assert.Equal(NullabilityState.Nullable, context.Create(parameters[1]).ReadState);
        Assert.Equal(NullabilityState.Nullable, context.Create(parameters[2]).ReadState);
        Assert.Equal(NullabilityState.Nullable, context.Create(parameters[3]).ReadState);
    }

    private static TEvent Key(ushort keyCode, char character = '\0')
    {
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.charScan.charCode = (byte)character;
        return ev;
    }

    private static TEvent Mouse(int x, int y, bool twice = false)
    {
        TEvent ev = default;
        ev.What = Events.evMouseDown;
        ev.mouse.where = new TPoint(x, y);
        ev.mouse.doubleClick = twice;
        return ev;
    }

    private class SelectionProbe : TOutline
    {
        public SelectionProbe(TNode root) : base(new TRect(0, 0, 20, 5), null, null, root) { }
        public int SelectedPosition { get; private set; } = -1;
        public override void Selected(int i) => SelectedPosition = i;
        public override bool MouseEvent(ref TEvent ev, ushort mask) => false;
    }

    private sealed class DrawingProbe : SelectionProbe
    {
        public DrawingProbe(TNode root) : base(root) { }
        public Dictionary<int, string> Rows { get; } = new();
        public Dictionary<int, TColorAttr[]> Attributes { get; } = new();
        public override bool IsSelected(int i) => false;
        public override void WriteLine(int x, int y, int w, int h, TDrawBuffer buffer)
        {
            string row = new string(buffer.Data[..w].ToArray().Select(cell => cell.Character).ToArray());
            TColorAttr[] attributes = buffer.Data[..w].ToArray().Select(cell => cell.Attr).ToArray();
            for (int i = 0; i < h; i++)
            {
                Rows[y + i] = row;
                Attributes[y + i] = attributes;
            }
        }
    }
}
