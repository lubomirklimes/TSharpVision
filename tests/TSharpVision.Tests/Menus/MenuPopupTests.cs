using System.Reflection;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Menus;

[Collection("NonParallel")]
public sealed class MenuPopupTests : IDisposable
{
    private const ushort OpenCommand = 600;
    private const ushort ExitCommand = 601;
    private const ushort ChildCommand = 602;
    private readonly DriverScope _driver = new();
    private readonly TCommandSet _commands = new();

    public MenuPopupTests()
    {
        TView.GetCommands(_commands);
        TView.EnableCommand(OpenCommand);
        TView.EnableCommand(ExitCommand);
        TView.EnableCommand(ChildCommand);
    }

    public void Dispose()
    {
        TView.SetCommands(_commands);
        _driver.Dispose();
    }

    private static TMenu Menu() => new(new TMenuItem("~O~pen", OpenCommand, Keys.kbF3,
        Views.hcNoContext, null, new TMenuItem("E~x~it", ExitCommand, Keys.kbAltX)));

    [Fact]
    public void ApiMetadataAndConstruction_MatchBorland()
    {
        var popup = new TMenuPopup(new TRect(5, 4, 80, 25), Menu());
        Assert.Equal(typeof(TMenuBox), typeof(TMenuPopup).BaseType);
        Assert.True(typeof(TMenuPopup).GetMethod(nameof(TMenuPopup.HandleEvent))!.IsVirtual);
        Assert.Null(popup.ParentMenu);
        Assert.NotNull(popup.Menu);
        Assert.NotEqual(0, popup.state & Views.sfShadow);
        Assert.NotEqual(0, popup.options & Views.ofPreProcess);
    }

    [Fact]
    public void Construction_ComputesHistoricalMenuBounds()
    {
        var popup = new TMenuPopup(new TRect(5, 4, 80, 25), Menu());
        Assert.Equal(new TPoint(5, 4), popup.origin);
        Assert.Equal(new TPoint(10, 4), popup.size);
    }

    [Fact]
    public void Construction_ClipsByMovingPopupInsideBoundary()
    {
        var popup = new TMenuPopup(new TRect(78, 24, 80, 25), Menu());
        Assert.Equal(new TPoint(70, 21), popup.origin);
        Assert.Equal(new TPoint(10, 4), popup.size);
    }

    [Fact]
    public void CtrlMnemonic_QueuesEnabledCommandAndClearsInput()
    {
        var owner = new QueueGroup();
        var popup = new TMenuPopup(new TRect(0, 0, 40, 10), Menu());
        owner.Insert(popup);
        TEvent ev = Key(Keys.kbCtrlO);
        popup.HandleEvent(ref ev);
        TEvent command = Assert.Single(owner.Events);
        Assert.Equal(Events.evCommand, command.What);
        Assert.Equal(OpenCommand, command.message.command);
        Assert.Null(command.message.infoPtr);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void Accelerator_QueuesEnabledCommand()
    {
        var owner = new QueueGroup();
        var popup = new TMenuPopup(new TRect(0, 0, 40, 10), Menu());
        owner.Insert(popup);
        TEvent ev = Key(Keys.kbF3);
        popup.HandleEvent(ref ev);
        Assert.Equal(OpenCommand, Assert.Single(owner.Events).message.command);
    }

    [Fact]
    public void UnmatchedAltKey_IsConsumedWithoutOpeningModernBehavior()
    {
        var popup = new TMenuPopup(new TRect(0, 0, 40, 10), Menu());
        TEvent ev = Key(Keys.kbAltZ);
        popup.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void DisabledItem_DoesNotDispatchMnemonic()
    {
        TMenu menu = Menu();
        menu.Items!.Disabled = true;
        var owner = new QueueGroup();
        var popup = new TMenuPopup(new TRect(0, 0, 40, 10), menu);
        owner.Insert(popup);
        TEvent ev = Key(Keys.kbCtrlO);
        popup.HandleEvent(ref ev);
        Assert.Empty(owner.Events);
    }

    [Fact]
    public void ModalKeyboardNavigationAndEnter_ReturnSelectedCommand()
    {
        var popup = new EventPopup(Menu(), Key(Keys.kbDown), Key(Keys.kbEnter));
        Assert.Equal(ExitCommand, popup.Execute());
    }

    [Fact]
    public void ModalEscape_DismissesWithoutCommand()
    {
        var popup = new EventPopup(Menu(), Key(Keys.kbEsc));
        Assert.Equal((ushort)0, popup.Execute());
    }

    [Fact]
    public void ModalOutsideClick_DismissesWithoutCommand()
    {
        var popup = new EventPopup(Menu(), Mouse(79, 24, Events.evMouseDown));
        Assert.Equal((ushort)0, popup.Execute());
    }

    [Fact]
    public void ModalMouseReleaseOnItem_ActivatesCommand()
    {
        var popup = new EventPopup(Menu(), Mouse(3, 1, Events.evMouseUp));
        Assert.Equal(OpenCommand, popup.Execute());
    }

    [Fact]
    public void SubmenuEnter_CreatesHistoricallyLinkedMenuBoxAndReturnsItsCommand()
    {
        var child = new TMenu(new TMenuItem("~C~hild", ChildCommand, Keys.kbNoKey));
        var menu = new TMenu(new TMenuItem("~M~ore", Keys.kbAltM, child));
        var popup = new EventPopup(menu, Key(Keys.kbEnter));
        var owner = new QueueGroup { ExecResult = ChildCommand };
        owner.Insert(popup);
        Assert.Equal(ChildCommand, popup.Execute());
        TMenuBox sub = Assert.IsType<TMenuBox>(owner.ExecutedView);
        Assert.Same(popup, sub.ParentMenu);
        Assert.Same(child, sub.Menu);
    }

    [Fact]
    public void DrawingAndPalette_AreInheritedFromMenuBoxWithSixEntries()
    {
        var popup = new DrawingPopup(Menu());
        popup.Draw();
        Assert.Equal(6, popup.GetPalette().Size);
        Assert.Contains('┌', popup.Rows[0]);
        Assert.Contains("Open", popup.Rows[1]);
        Assert.Equal((TColorAttr)2, popup.Attributes[0][0]);
    }

    [Fact]
    public void SeparatorAndSubmenuArrow_UseMenuBoxRenderer()
    {
        var child = new TMenu(new TMenuItem("Child", ChildCommand, Keys.kbNoKey));
        var first = new TMenuItem("More", Keys.kbAltM, child,
            Views.hcNoContext, new TMenuItem(null, 0, 0));
        var popup = new DrawingPopup(new TMenu(first));
        popup.Draw();
        Assert.Contains(TSharpVisionGlyphs.MenuSubmenuArrow, popup.Rows[1]);
        Assert.Contains('├', popup.Rows[2]);
    }

    [Fact]
    public void Streaming_RoundTripsConcretePopupAndMenu()
    {
        var source = new TMenuPopup(new TRect(2, 3, 50, 20), Menu());
        using var stream = new MemoryStream();
        source.Write(new Opstream(stream));
        stream.Position = 0;
        var restored = (TMenuPopup)TMenuPopup.Build();
        restored.Read(new Ipstream(stream));
        Assert.Equal(source.origin, restored.origin);
        Assert.Equal("~O~pen", restored.Menu!.Items!.Name);
        Assert.Equal(ExitCommand, restored.Menu.Items.Next!.Command);
        Assert.Null(restored.ParentMenu);
        Assert.Equal(TMenuPopup.Name, restored.StreamableName());
    }

    [Fact]
    public void StreamRegistration_IsConcretePopupType()
    {
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
        Assert.Same(TMenuPopup.StreamableClassTMenuPopup, Pstream.types.Lookup(TMenuPopup.Name));
    }

    [Fact]
    public void PublicNullability_RequiresMenuAndRetainsNullableParent()
    {
        var context = new NullabilityInfoContext();
        ConstructorInfo constructor = typeof(TMenuPopup).GetConstructors().Single();
        Assert.Equal(NullabilityState.NotNull, context.Create(constructor.GetParameters()[1]).ReadState);
        PropertyInfo parent = typeof(TMenuView).GetProperty(nameof(TMenuView.ParentMenu))!;
        Assert.Equal(NullabilityState.Nullable, context.Create(parent).ReadState);
    }

    private static TEvent Key(ushort code)
    {
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = code;
        ev.keyDown.charScan.charCode = (byte)code;
        return ev;
    }

    private static TEvent Mouse(int x, int y, ushort what)
    {
        TEvent ev = default;
        ev.What = what;
        ev.mouse.where = new TPoint(x, y);
        return ev;
    }

    private sealed class QueueGroup : TGroup
    {
        public QueueGroup() : base(new TRect(0, 0, 80, 25)) { }
        public List<TEvent> Events { get; } = new();
        public ushort ExecResult { get; init; }
        public TView? ExecutedView { get; private set; }
        public override void PutEvent(ref TEvent ev) => Events.Add(ev);
        public override ushort ExecView(TView p) { ExecutedView = p; return ExecResult; }
    }

    private class EventPopup : TMenuPopup
    {
        private readonly Queue<TEvent> _events;
        public EventPopup(TMenu menu, params TEvent[] events)
            : base(new TRect(0, 0, 40, 10), menu) => _events = new(events);
        public override void GetEvent(ref TEvent ev) => ev = _events.Dequeue();
    }

    private sealed class DrawingPopup : EventPopup
    {
        public DrawingPopup(TMenu menu) : base(menu) { }
        public Dictionary<int, string> Rows { get; } = new();
        public Dictionary<int, TColorAttr[]> Attributes { get; } = new();
        public override void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> cells)
        {
            Rows[y] = new string(cells[..w].ToArray().Select(cell => cell.Character).ToArray());
            Attributes[y] = cells[..w].ToArray().Select(cell => cell.Attr).ToArray();
        }
    }
}
