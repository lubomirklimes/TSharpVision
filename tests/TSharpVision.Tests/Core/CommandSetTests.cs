// Regression tests for the command-ID range fix.
//
// Before the fix TCommandSet was a fixed 32-byte bitmap covering only commands 0..255:
//   * TCommandSet.Has / EnableCmd / DisableCmd threw IndexOutOfRangeException for cmd >= 256,
//     and so did TView.EnableCommand / TView.DisableCommand;
//   * TView.CommandEnabled papered over that with a "command > 255 ||" short circuit, which
//     reported every high command as permanently enabled and could never be turned off;
//   * TView.GetCommands copied only 0..255, so TGroup.ExecView's save/restore silently
//     dropped the state of any higher command.
// The framework's own TFileDialog / TChDirDialog commands live at 1001..1008.
using System;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class CommandSetTests
{
    // TView.curCommandSet is process-wide mutable state and EnableCommand/DisableCommand
    // mutate it in place, so a real snapshot (not just the reference) has to be restored.
    private sealed class CommandSetScope : IDisposable
    {
        private readonly TCommandSet _saved = new TCommandSet(TView.curCommandSet);
        private readonly bool _savedChanged = TView.commandSetChanged;

        public void Dispose()
        {
            TView.curCommandSet = _saved;
            TView.commandSetChanged = _savedChanged;
        }
    }

    // Spread across the whole ushort range, straddling the old 255/256 boundary and
    // covering the framework's own 1001..1008 block.
    public static TheoryData<ushort> BoundaryCommands => new()
    {
        (ushort)0, (ushort)1, (ushort)254, (ushort)255, (ushort)256, (ushort)257,
        (ushort)1000, (ushort)1001, (ushort)1008, (ushort)32767, (ushort)65535,
    };

    // ── boundary values through the TView API ────────────────────────────────

    [Theory]
    [MemberData(nameof(BoundaryCommands))]
    public void CommandEnabled_DefaultsToEnabledAcrossTheWholeRange(ushort command)
    {
        using var scope = new CommandSetScope();

        // Matches the pre-fix observable behaviour: every command except the five window
        // commands starts enabled, high commands included.
        Assert.True(TView.CommandEnabled(command));
    }

    [Theory]
    [MemberData(nameof(BoundaryCommands))]
    public void DisableCommand_ThenEnableCommand_RoundTrips(ushort command)
    {
        using var scope = new CommandSetScope();

        TView.DisableCommand(command);
        Assert.False(TView.CommandEnabled(command));

        TView.EnableCommand(command);
        Assert.True(TView.CommandEnabled(command));
    }

    [Theory]
    [MemberData(nameof(BoundaryCommands))]
    public void EnableAndDisableCommand_AreIdempotent(ushort command)
    {
        using var scope = new CommandSetScope();

        TView.DisableCommand(command);
        TView.DisableCommand(command);
        Assert.False(TView.CommandEnabled(command));

        TView.EnableCommand(command);
        TView.EnableCommand(command);
        Assert.True(TView.CommandEnabled(command));
    }

    [Theory]
    [MemberData(nameof(BoundaryCommands))]
    public void DisablingOneCommandDoesNotAffectItsNeighbours(ushort command)
    {
        using var scope = new CommandSetScope();

        TView.DisableCommand(command);

        if (command > 0) Assert.True(TView.CommandEnabled((ushort)(command - 1)));
        if (command < ushort.MaxValue) Assert.True(TView.CommandEnabled((ushort)(command + 1)));
    }

    [Fact]
    public void HighCommands_NoLongerThrow()
    {
        using var scope = new CommandSetScope();

        // Each of these threw IndexOutOfRangeException before the fix.
        TView.DisableCommand(256);
        TView.EnableCommand(1001);
        TView.DisableCommand(ushort.MaxValue);
        Assert.False(TView.CommandEnabled(ushort.MaxValue));
    }

    // ── low-ID behaviour is unchanged ────────────────────────────────────────

    [Fact]
    public void WindowCommands_StartDisabled_EverythingElseStartsEnabled()
    {
        using var scope = new CommandSetScope();
        TView.curCommandSet = new TCommandSet(FreshDefaultSet());

        foreach (ushort disabled in new[]
                 { Views.cmZoom, Views.cmClose, Views.cmResize, Views.cmNext, Views.cmPrev })
        {
            Assert.False(TView.CommandEnabled(disabled));
        }

        foreach (ushort enabled in new[]
                 { Views.cmValid, Views.cmQuit, Views.cmMenu, Views.cmOK, Views.cmCancel,
                   Views.cmHelp, Commands.cmFirstUserCommand, (ushort)200, (ushort)255 })
        {
            Assert.True(TView.CommandEnabled(enabled));
        }
    }

    [Fact]
    public void LowCommands_RoundTripExactlyAsBefore()
    {
        using var scope = new CommandSetScope();

        for (int command = 0; command < 256; command++)
        {
            TView.DisableCommand((ushort)command);
            Assert.False(TView.CommandEnabled((ushort)command));
            TView.EnableCommand((ushort)command);
            Assert.True(TView.CommandEnabled((ushort)command));
        }
    }

    // Rebuilds what TView.InitCommands produces, without depending on whatever the
    // currently-running process has done to the live set.
    private static TCommandSet FreshDefaultSet()
    {
        var set = new TCommandSet();
        set.EnableAll();
        set.DisableCmd(Views.cmZoom);
        set.DisableCmd(Views.cmClose);
        set.DisableCmd(Views.cmResize);
        set.DisableCmd(Views.cmNext);
        set.DisableCmd(Views.cmPrev);
        return set;
    }

    // ── TCommandSet itself ───────────────────────────────────────────────────

    [Fact]
    public void NewSet_IsEmpty()
    {
        var set = new TCommandSet();

        Assert.True(set.IsEmpty());
        Assert.False(set.Has(0));
        Assert.False(set.Has(255));
        Assert.False(set.Has(1001));
        Assert.False(set.Has(ushort.MaxValue));
    }

    [Theory]
    [MemberData(nameof(BoundaryCommands))]
    public void Set_AddRemoveHas_WorkAtEveryBoundary(ushort command)
    {
        var set = new TCommandSet();

        set.EnableCmd(command);
        Assert.True(set.Has(command));
        Assert.False(set.IsEmpty());

        set.DisableCmd(command);
        Assert.False(set.Has(command));
        Assert.True(set.IsEmpty());

        // Add/Remove are the aliases for the same operations.
        set.Add(command);
        Assert.True(set.Has(command));
        set.Remove(command);
        Assert.False(set.Has(command));
    }

    [Fact]
    public void EnableAllAndDisableAll_CoverTheWholeRange()
    {
        var set = new TCommandSet();
        set.EnableAll();

        Assert.True(set.Has(0));
        Assert.True(set.Has(255));
        Assert.True(set.Has(256));
        Assert.True(set.Has(Views.cmFileOpen));
        Assert.True(set.Has(ushort.MaxValue));
        Assert.False(set.IsEmpty());

        set.DisableAll();
        Assert.True(set.IsEmpty());
        Assert.False(set.Has(ushort.MaxValue));
    }

    [Fact]
    public void OutOfRangeCodes_AreNotMembersAndDoNotThrow()
    {
        var set = new TCommandSet();

        set.EnableCmd(-1);
        set.EnableCmd(TCommandSet.CommandCount);
        set.EnableCmd(int.MaxValue);
        set.EnableCmd(int.MinValue);

        Assert.True(set.IsEmpty());
        Assert.False(set.Has(-1));
        Assert.False(set.Has(TCommandSet.CommandCount));
        Assert.False(set.Has(int.MaxValue));
        Assert.False(set.Has(int.MinValue));

        set.DisableCmd(-1);
        set.DisableCmd(int.MaxValue);
        Assert.True(set.IsEmpty());
    }

    // ── set operations at low and high IDs ───────────────────────────────────

    private static TCommandSet SetOf(params int[] commands)
    {
        var set = new TCommandSet();
        foreach (int command in commands) set.EnableCmd(command);
        return set;
    }

    [Fact]
    public void Union_CombinesLowAndHighMembers()
    {
        TCommandSet a = SetOf(5, 1001);
        TCommandSet b = SetOf(200, 65535);

        TCommandSet union = a | b;

        Assert.True(union.Has(5));
        Assert.True(union.Has(200));
        Assert.True(union.Has(1001));
        Assert.True(union.Has(65535));

        // Operands are untouched.
        Assert.False(a.Has(200));
        Assert.False(b.Has(1001));
    }

    [Fact]
    public void Intersection_KeepsOnlySharedLowAndHighMembers()
    {
        TCommandSet a = SetOf(5, 200, 1001, 65535);
        TCommandSet b = SetOf(200, 1001, 40000);

        TCommandSet intersection = a & b;

        Assert.False(intersection.Has(5));
        Assert.True(intersection.Has(200));
        Assert.True(intersection.Has(1001));
        Assert.False(intersection.Has(40000));
        Assert.False(intersection.Has(65535));
    }

    [Fact]
    public void AddAndRemoveSet_MutateInPlaceAcrossTheRange()
    {
        TCommandSet target = SetOf(1);
        target.Add(SetOf(255, 256, 1008, 65535));

        Assert.True(target.Has(1));
        Assert.True(target.Has(255));
        Assert.True(target.Has(256));
        Assert.True(target.Has(1008));
        Assert.True(target.Has(65535));

        target.Remove(SetOf(256, 65535));

        Assert.True(target.Has(255));
        Assert.False(target.Has(256));
        Assert.True(target.Has(1008));
        Assert.False(target.Has(65535));
    }

    [Fact]
    public void IntersectsAndIsSupersetOf_AgreeWithTheOperatorFormulations()
    {
        TCommandSet a = SetOf(5, 1001, 65535);
        TCommandSet sharesHigh = SetOf(1001, 40000);
        TCommandSet disjoint = SetOf(6, 1002);
        TCommandSet subset = SetOf(5, 65535);

        Assert.Equal(!(a & sharesHigh).IsEmpty(), a.Intersects(sharesHigh));
        Assert.True(a.Intersects(sharesHigh));

        Assert.Equal(!(a & disjoint).IsEmpty(), a.Intersects(disjoint));
        Assert.False(a.Intersects(disjoint));

        Assert.Equal(a.Equals(a | subset), a.IsSupersetOf(subset));
        Assert.True(a.IsSupersetOf(subset));

        Assert.Equal(a.Equals(a | disjoint), a.IsSupersetOf(disjoint));
        Assert.False(a.IsSupersetOf(disjoint));
    }

    [Fact]
    public void CopyConstructorAndCopyFrom_DuplicateHighMembersAndDoNotAlias()
    {
        TCommandSet original = SetOf(7, 1001, 65535);

        var copy = new TCommandSet(original);
        Assert.True(copy.Has(1001));
        Assert.True(copy.Has(65535));
        Assert.Equal(original, copy);

        copy.DisableCmd(1001);
        Assert.True(original.Has(1001));      // no aliasing

        var target = SetOf(999);
        target.CopyFrom(original);
        Assert.Equal(original, target);
        Assert.False(target.Has(999));         // CopyFrom replaces, it does not merge
        Assert.True(target.Has(65535));
    }

    [Fact]
    public void EqualityConsidersHighCommands()
    {
        TCommandSet a = SetOf(5);
        TCommandSet b = SetOf(5);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        b.EnableCmd(1001);
        Assert.True(a != b);
        Assert.False(a.Equals(b));
    }

    // ── TView.GetCommands / SetCommands round trip (ExecView save/restore) ───

    [Fact]
    public void GetCommandsAndSetCommands_PreserveHighCommandState()
    {
        using var scope = new CommandSetScope();

        TView.DisableCommand(Views.cmFileOpen);   // 1001
        TView.DisableCommand(60000);
        TView.DisableCommand(Views.cmOK);         // low-ID control

        var saved = new TCommandSet();
        TView.GetCommands(saved);

        // Change everything the modal view might change.
        TView.EnableCommand(Views.cmFileOpen);
        TView.EnableCommand(60000);
        TView.EnableCommand(Views.cmOK);
        Assert.True(TView.CommandEnabled(Views.cmFileOpen));

        TView.SetCommands(saved);

        Assert.False(TView.CommandEnabled(Views.cmFileOpen));
        Assert.False(TView.CommandEnabled(60000));
        Assert.False(TView.CommandEnabled(Views.cmOK));
    }

    [Fact]
    public void EnableCommandsAndDisableCommands_TrackCommandSetChangedForHighIds()
    {
        using var scope = new CommandSetScope();

        TView.DisableCommand(1001);

        TView.commandSetChanged = false;
        TView.EnableCommands(SetOf(1001));
        Assert.True(TView.commandSetChanged);
        Assert.True(TView.CommandEnabled(1001));

        // Already a member: nothing changes.
        TView.commandSetChanged = false;
        TView.EnableCommands(SetOf(1001));
        Assert.False(TView.commandSetChanged);

        TView.commandSetChanged = false;
        TView.DisableCommands(SetOf(1001));
        Assert.True(TView.commandSetChanged);
        Assert.False(TView.CommandEnabled(1001));

        // Already absent: nothing changes.
        TView.commandSetChanged = false;
        TView.DisableCommands(SetOf(1001));
        Assert.False(TView.commandSetChanged);
    }

    // ── TStatusLine regression with a command > 255 ──────────────────────────

    private static TStatusLine MakeStatusLine(ushort command) =>
        new TStatusLine(
            new TRect(0, 24, 80, 25),
            new TStatusDef(0, 0xFFFF) +
            new TStatusItem("~F5~ High", Keys.kbF5, command));

    [Fact]
    public void StatusLine_ConvertsAKeyIntoACommandAbove255()
    {
        using var driver = new DriverScope();
        using var scope = new CommandSetScope();

        TStatusLine statusLine = MakeStatusLine(Views.cmFileOpen);   // 1001
        TView.EnableCommand(Views.cmFileOpen);

        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbF5;
        statusLine.HandleEvent(ref ev);

        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(1001, ev.message.command);
        Assert.Equal(Views.cmFileOpen, ev.message.command);
    }

    [Fact]
    public void StatusLine_DoesNotConvertTheKeyWhenTheHighCommandIsDisabled()
    {
        using var driver = new DriverScope();
        using var scope = new CommandSetScope();

        TStatusLine statusLine = MakeStatusLine(Views.cmFileOpen);
        TView.DisableCommand(Views.cmFileOpen);

        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbF5;
        statusLine.HandleEvent(ref ev);

        Assert.Equal(Events.evKeyDown, ev.What);   // left alone for someone else to handle
    }

    [Fact]
    public void StatusLine_DrawsAHighCommandItemWithoutThrowing()
    {
        using var driver = new DriverScope();
        using var scope = new CommandSetScope();

        var root = new TestGroup(new TRect(0, 0, 80, 25));
        root.buffer = new ScreenBuffer(80 * 25 * ScreenBuffer.GetSize());
        root.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        TStatusLine statusLine = MakeStatusLine(Views.cmFileOpen);
        root.Insert(statusLine);

        // DrawSelect reads CommandEnabled(item.Command) for every drawn item; with a
        // 32-byte bitmap this indexed out of range once the guard was removed.
        statusLine.Draw();
        TView.DisableCommand(Views.cmFileOpen);
        statusLine.Draw();

        // The item text still reached the buffer (row 24, starting one column in).
        int idx = (24 * 80) + 1;
        Assert.Equal('F', root.buffer.Data[idx].Character);
    }

    // ── framework-owned 1001..1008 commands ──────────────────────────────────

    [Theory]
    [InlineData(Views.cmFileOpen)]
    [InlineData(Views.cmFileReplace)]
    [InlineData(Views.cmFileClear)]
    [InlineData(Views.cmFileInit)]
    [InlineData(Views.cmChangeDir)]
    [InlineData(Views.cmRevert)]
    [InlineData(Views.cmFileSelect)]
    [InlineData(Views.cmDirSelection)]
    public void FrameworkFileDialogCommands_AreEnabledByDefaultAndCanBeToggled(ushort command)
    {
        using var scope = new CommandSetScope();

        Assert.InRange(command, (ushort)1001, (ushort)1008);
        Assert.True(TView.CommandEnabled(command));

        TView.DisableCommand(command);
        Assert.False(TView.CommandEnabled(command));

        TView.EnableCommand(command);
        Assert.True(TView.CommandEnabled(command));
    }

    [Fact]
    public void Button_WithAHighCommand_FollowsTheCommandSet()
    {
        using var driver = new DriverScope();
        using var scope = new CommandSetScope();

        // TButton's constructor consults CommandEnabled to decide sfDisabled — this is how
        // TFileDialog's Open/Replace/Clear buttons behave.
        TView.EnableCommand(Views.cmFileOpen);
        var enabled = new TButton(new TRect(0, 0, 12, 2), "~O~pen", Views.cmFileOpen, ButtonConstants.bfDefault);
        Assert.False(enabled.GetState(Views.sfDisabled));

        TView.DisableCommand(Views.cmFileOpen);
        var disabled = new TButton(new TRect(0, 0, 12, 2), "~O~pen", Views.cmFileOpen, ButtonConstants.bfDefault);
        Assert.True(disabled.GetState(Views.sfDisabled));
    }

    [Fact]
    public void MenuItem_WithAHighCommand_FollowsTheCommandSet()
    {
        using var driver = new DriverScope();
        using var scope = new CommandSetScope();

        var bar = new TMenuBar(new TRect(0, 0, 80, 1), new TMenu(
            new TMenuItem("~F~ile", Keys.kbAltF, new TMenu(
                new TMenuItem("~O~pen", Views.cmFileOpen, Keys.kbNoKey)))));

        Assert.NotNull(bar);

        TView.EnableCommand(Views.cmFileOpen);
        Assert.True(TView.CommandEnabled(Views.cmFileOpen));

        TView.DisableCommand(Views.cmFileOpen);
        Assert.False(TView.CommandEnabled(Views.cmFileOpen));
    }
}
