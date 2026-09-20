using System.Reflection;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class PreFreezeApiCleanupTests : IDisposable
{
    private readonly DriverScope _driver = new();

    public void Dispose() => _driver.Dispose();

    [Fact]
    public void ModalRootIsSharedNestedAndRestored()
    {
        var host = new TGroup(new TRect(0, 0, 20, 10));
        ModalProbe? outer = null;
        ModalProbe? inner = null;
        inner = new ModalProbe(() =>
        {
            Assert.Same(inner, host.TopView());
            return Views.cmOK;
        });
        outer = new ModalProbe(() =>
        {
            Assert.Same(outer, new TView(new TRect(0, 0, 1, 1)).TopView());
            Assert.Equal(Views.cmOK, host.ExecView(inner));
            Assert.Same(outer, host.TopView());
            return Views.cmCancel;
        });

        Assert.Equal(Views.cmCancel, host.ExecView(outer));
        Assert.Null(host.TopView());
        Assert.Null(outer.owner);
        Assert.Null(inner.owner);
    }

    [Fact]
    public void ModalRootAndGroupStateAreRestoredWhenExecuteThrows()
    {
        var host = new TGroup(new TRect(0, 0, 20, 10));
        var selected = new TView(new TRect(0, 0, 2, 2));
        selected.options |= Views.ofSelectable;
        host.Insert(selected);
        TCommandSet commands = new();
        TView.GetCommands(commands);
        var modal = new ModalProbe(() => throw new InvalidOperationException("probe"));
        ushort savedOptions = modal.options;

        Assert.Throws<InvalidOperationException>(() => host.ExecView(modal));

        Assert.Null(host.TopView());
        Assert.Same(selected, host.current);
        Assert.Null(modal.owner);
        Assert.Equal(savedOptions, modal.options);
        Assert.Equal(0, modal.state & (Views.sfModal | Views.sfActive));
        TCommandSet restored = new();
        TView.GetCommands(restored);
        Assert.Equal(commands, restored);
    }

    [Fact]
    public void EventsRouteToTheModalChildWhileItsSharedRootIsActive()
    {
        var host = new TGroup(new TRect(0, 0, 20, 10));
        RoutingModalProbe? modal = null;
        modal = new RoutingModalProbe(() =>
        {
            TEvent ev = new() { What = Events.evCommand };
            ev.message.command = Views.cmHelp;
            host.HandleEvent(ref ev);
            Assert.Same(modal, host.TopView());
            return Views.cmOK;
        });

        Assert.Equal(Views.cmOK, host.ExecView(modal));
        Assert.True(modal.Handled);
        Assert.Null(host.TopView());
    }

    [Fact]
    public void ProgramImplementationFieldsAreNotPublic()
    {
        const BindingFlags allStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        Assert.True(typeof(TProgram).GetField("Pending", allStatic)!.IsFamily);
        Assert.True(typeof(TProgram).GetField("LastIdleClock", allStatic)!.IsAssembly);
        Assert.True(typeof(TProgram).GetField("InIdleTime", allStatic)!.IsAssembly);
        Assert.True(typeof(TProgram).GetField("InIdle", allStatic)!.IsAssembly);
        Assert.True(typeof(TProgram).GetField("DoNotReleaseCPU", allStatic)!.IsFamily);
        Assert.True(typeof(TProgram).GetField("DoNotHandleAltNumber", allStatic)!.IsFamily);
        Assert.Null(typeof(TView).GetField("TheTopView", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void PublicLifecycleNullabilityMatchesRuntimeStates()
    {
        NullabilityInfoContext context = new();
        Assert.Equal(NullabilityState.Nullable, context.Create(typeof(TView).GetField(nameof(TView.owner))!).ReadState);
        Assert.Equal(NullabilityState.Nullable, context.Create(typeof(TView).GetField(nameof(TView.Next))!).ReadState);
        Assert.Equal(NullabilityState.Nullable, context.Create(typeof(TGroup).GetField(nameof(TGroup.current))!).ReadState);
        Assert.Equal(NullabilityState.Nullable, context.Create(typeof(TGroup).GetField(nameof(TGroup.last))!).ReadState);
        Assert.Equal(NullabilityState.Nullable, context.Create(typeof(TGroup).GetField(nameof(TGroup.buffer))!).ReadState);
        Assert.Equal(NullabilityState.Nullable, context.Create(typeof(TWindow).GetField(nameof(TWindow.frame))!).ReadState);
        Assert.Equal(NullabilityState.Nullable, context.Create(typeof(TProgram).GetProperty(nameof(TProgram.DeskTop))!).ReadState);
        Assert.Equal(NullabilityState.Nullable, context.Create(typeof(TProgram).GetMethod(nameof(TProgram.GetHelpFile))!.ReturnParameter).ReadState);
        Assert.Equal(NullabilityState.Nullable, context.Create(typeof(TProgram).GetMethod(nameof(TProgram.ValidView))!.ReturnParameter).ReadState);
    }

    [Fact]
    public void KeyTextIsNonNullForDefaultAndNullAssignment()
    {
        KeyDownEvent key = default;
        Assert.Same(string.Empty, key.text);
        key.text = null!;
        Assert.Same(string.Empty, key.text);
        Assert.Equal(NullabilityState.NotNull,
            new NullabilityInfoContext().Create(typeof(KeyDownEvent).GetProperty(nameof(KeyDownEvent.text))!).ReadState);
    }

    [Fact]
    public void ColorCommandsPreserveBorlandSlotAndUseDistinctExtensionSlots()
    {
        Assert.Equal((ushort)76, Views.cmSaveColorIndex);
        Assert.Equal((ushort)77, Views.cmUpdateColorsChanged);
        Assert.Equal((ushort)78, Views.cmTryColors);
        Assert.Equal(3, new[] { Views.cmSaveColorIndex, Views.cmUpdateColorsChanged, Views.cmTryColors }.Distinct().Count());
    }

    [Fact]
    public void SourceCompatibleOverloadsNeedNoApiDesignSuppressions()
    {
        MethodInfo[] insertText = typeof(TEditor).GetMethods()
            .Where(method => method.Name == nameof(TEditor.InsertText)
                && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(string))
            .ToArray();
        Assert.Equal(2, insertText.Length);
        Assert.All(insertText.SelectMany(method => method.GetParameters()),
            parameter => Assert.False(parameter.IsOptional));

        ConstructorInfo[] menuItems = typeof(TMenuItem).GetConstructors();
        Assert.Equal(7, menuItems.Length);
        Assert.All(menuItems.SelectMany(constructor => constructor.GetParameters()),
            parameter => Assert.False(parameter.IsOptional));
    }

    [Fact]
    public void ColorGroupRestoresItsRememberedItemIndex()
    {
        var first = new TColorGroup("one",
            new TColorItem("a", 1, new TColorItem("b", 2)));
        var second = new TColorGroup("two", new TColorItem("c", 3));
        first.Next = second;
        var host = new TGroup(new TRect(0, 0, 40, 10));
        var groupScrollBar = new TScrollBar(new TRect(9, 0, 10, 5));
        var groups = new TColorGroupList(new TRect(0, 0, 9, 5), groupScrollBar, first);
        var items = new TColorItemList(new TRect(10, 0, 20, 5), null, first.Items);
        host.Insert(groupScrollBar);
        host.Insert(groups);
        host.Insert(items);

        groups.FocusItem(0);
        items.FocusItem(1);
        Assert.Equal(1, first.Index);
        groups.FocusItem(1);
        groups.FocusItem(0);

        Assert.Equal(1, items.focused);
    }

    [Fact]
    public void TryColorsUsesOnlyItsExtensionCommandAndBroadcastsRefresh()
    {
        var host = new TGroup(new TRect(0, 0, 80, 25));
        var probe = new BroadcastProbe(new TRect(0, 0, 1, 1));
        var colors = new TColorGroup("one", new TColorItem("a", 1));
        var dialog = new TColorDialog(new TPalette("\x07", 1), colors);
        host.Insert(probe);
        host.Insert(dialog);
        TEvent ev = new() { What = Events.evCommand };
        ev.message.command = Views.cmTryColors;

        dialog.HandleEvent(ref ev);

        Assert.Equal(Views.cmUpdateColorsChanged, probe.LastBroadcast);
        Assert.Equal(Events.evCommand, ev.What);
    }

    private class ModalProbe : TView
    {
        private readonly Func<ushort> _execute;
        public ModalProbe(Func<ushort> execute) : base(new TRect(0, 0, 5, 3)) => _execute = execute;
        public override ushort Execute() => _execute();
    }

    private sealed class RoutingModalProbe : ModalProbe
    {
        public bool Handled { get; private set; }
        public RoutingModalProbe(Func<ushort> execute) : base(execute) { }
        public override void HandleEvent(ref TEvent @event)
        {
            Handled = true;
            ClearEvent(ref @event);
        }
    }
}
