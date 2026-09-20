using TSharpVision;
using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Tests.Core;

public sealed class HistoricalExtensionFlagTests
{
    [Fact]
    public void NumericValuesAndSurroundingFlagFamiliesMatchBorland()
    {
        Assert.Equal(0x400, Views.ofValidate);
        Assert.Equal(0x20, Views.gfFixed);
        Assert.Equal(0x08, ButtonConstants.bfGrabFocus);

        Assert.Equal(0x200, Views.ofCenterY);
        Assert.Equal(0x300, Views.ofCentered);
        Assert.Equal(0x10, Views.gfGrowRel);
        Assert.Equal(0x0F, Views.gfGrowAll);
        Assert.Equal(0x04, ButtonConstants.bfBroadcast);
    }

    [Fact]
    public void OfValidateRejectsAndThenAllowsRealMouseFocusMovement()
    {
        var owner = new TGroup(new TRect(0, 0, 30, 8));
        owner.SetState(Views.sfFocused, true);
        var validator = new ReleaseValidator();
        validator.options |= (ushort)(Views.ofSelectable | Views.ofValidate);
        var target = new SelectableView(new TRect(12, 1, 20, 3));
        owner.Insert(validator);
        owner.Insert(target);
        owner.SetCurrent(validator, TView.selectMode.normalSelect);
        TEvent click = MouseDown(13, 1);

        target.HandleEvent(ref click);

        Assert.Same(validator, owner.current);
        Assert.Equal(1, validator.ReleaseValidations);
        Assert.False(target.GetState(Views.sfSelected));

        validator.AllowRelease = true;
        click = MouseDown(13, 1);
        target.HandleEvent(ref click);
        Assert.Same(target, owner.current);
        Assert.Equal(2, validator.ReleaseValidations);
        Assert.True(target.GetState(Views.sfFocused));
    }

    [Fact]
    public void GfFixedPreventsOwnerSizeClampingDuringOwnerResize()
    {
        var owner = new TGroup(new TRect(0, 0, 20, 8));
        var ordinary = new TView(new TRect(0, 0, 15, 2));
        var fixedLimit = new TView(new TRect(0, 3, 15, 5));
        fixedLimit.growMode |= Views.gfFixed;
        owner.Insert(ordinary);
        owner.Insert(fixedLimit);

        owner.ChangeBounds(new TRect(0, 0, 10, 8));

        Assert.Equal(10, ordinary.size.x);
        Assert.Equal(15, fixedLimit.size.x);
        TPoint minimum = default, ordinaryMaximum = default, fixedMaximum = default;
        ordinary.SizeLimits(ref minimum, ref ordinaryMaximum);
        fixedLimit.SizeLimits(ref minimum, ref fixedMaximum);
        Assert.Equal(owner.size, ordinaryMaximum);
        Assert.Equal(new TPoint(int.MaxValue, int.MaxValue), fixedMaximum);
    }

    [Fact]
    public void NormalAndDefaultButtonsDoNotGrabFocusOnMouseDown()
    {
        var owner = FocusedOwner(out SelectableView incumbent);
        var normal = new ProbeButton(new TRect(10, 0, 20, 3), ButtonConstants.bfNormal);
        var @default = new ProbeButton(new TRect(10, 3, 20, 6), ButtonConstants.bfDefault);
        owner.Insert(normal);
        owner.Insert(@default);

        TEvent normalClick = MouseDown(12, 1);
        normal.HandleEvent(ref normalClick);
        TEvent defaultClick = MouseDown(12, 4);
        @default.HandleEvent(ref defaultClick);

        Assert.Same(incumbent, owner.current);
        Assert.Equal(1, normal.PressCalls);
        Assert.Equal(1, @default.PressCalls);
    }

    [Fact]
    public void BfGrabFocusSelectsButtonBeforeMousePress()
    {
        var owner = FocusedOwner(out _);
        var button = new ProbeButton(new TRect(10, 0, 20, 3), ButtonConstants.bfGrabFocus);
        owner.Insert(button);
        TEvent click = MouseDown(12, 1);

        button.HandleEvent(ref click);

        Assert.Same(button, owner.current);
        Assert.True(button.GetState(Views.sfFocused));
        Assert.Equal(1, button.PressCalls);
    }

    [Fact]
    public void KeyboardActivationWorksForFocusedGrabFocusButton()
    {
        var owner = FocusedOwner(out _);
        var button = new ProbeButton(new TRect(10, 0, 20, 3), ButtonConstants.bfGrabFocus);
        owner.Insert(button);
        owner.SetCurrent(button, TView.selectMode.normalSelect);
        TEvent key = default;
        key.What = Events.evKeyDown;
        key.keyDown.charScan.charCode = (byte)' ';

        button.HandleEvent(ref key);

        Assert.Equal(1, button.PressCalls);
        Assert.Equal(Events.evNothing, key.What);
    }

    [Fact]
    public void DisabledGrabFocusButtonNeitherFocusesNorPresses()
    {
        var owner = FocusedOwner(out SelectableView incumbent);
        var button = new ProbeButton(new TRect(10, 0, 20, 3), ButtonConstants.bfGrabFocus);
        button.SetState(Views.sfDisabled, true);
        owner.Insert(button);
        TEvent click = MouseDown(12, 1);

        button.HandleEvent(ref click);

        Assert.Same(incumbent, owner.current);
        Assert.Equal(0, button.PressCalls);
    }

    private static TGroup FocusedOwner(out SelectableView incumbent)
    {
        var owner = new TGroup(new TRect(0, 0, 30, 8));
        incumbent = new SelectableView(new TRect(0, 0, 8, 2));
        owner.Insert(incumbent);
        owner.SetState(Views.sfFocused, true);
        owner.SetCurrent(incumbent, TView.selectMode.normalSelect);
        return owner;
    }

    private static TEvent MouseDown(int x, int y)
    {
        TEvent result = default;
        result.What = Events.evMouseDown;
        result.mouse.where = new TPoint(x, y);
        result.mouse.buttons = (byte)Events.mbLeftButton;
        return result;
    }

    private sealed class SelectableView : TView
    {
        internal SelectableView(TRect bounds) : base(bounds)
            => options |= (ushort)(Views.ofSelectable | Views.ofFirstClick);
    }

    private sealed class ReleaseValidator : TView
    {
        internal ReleaseValidator() : base(new TRect(1, 1, 9, 3)) { }

        internal bool AllowRelease { get; set; }
        internal int ReleaseValidations { get; private set; }

        public override bool Valid(ushort command)
        {
            if (command != Views.cmReleasedFocus)
                return true;
            ReleaseValidations++;
            return AllowRelease;
        }
    }

    private sealed class ProbeButton : TButton
    {
        internal ProbeButton(TRect bounds, ushort flags)
            : base(bounds, "~O~K", Views.cmOK, flags) { }

        internal int PressCalls { get; private set; }

        public override bool MouseEvent(ref TEvent ev, ushort mask) => false;

        public override void Press() => PressCalls++;
    }
}
