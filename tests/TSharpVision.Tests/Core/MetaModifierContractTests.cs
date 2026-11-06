// MACOS-1B: the cross-platform Meta modifier (Command, Windows, Super) is one public keyboard-state bit,
// added beside the historical Shift/Ctrl/Alt and lock masks without renumbering any of them.
using System.Globalization;
using System.Numerics;
using System.Reflection;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.Internal;
using Xunit;

namespace TSharpVision.Tests.Core;

public sealed class MetaModifierContractTests
{
    // Every uint keyboard-state mask in Keys, with the value it must keep.
    private static readonly Dictionary<string, uint> StateMasks = new()
    {
        ["kbLeftShift"] = 0x0001, ["kbRightShift"] = 0x0002, ["kbShift"] = 0x0003,
        ["kbLeftCtrl"] = 0x0004, ["kbRightCtrl"] = 0x0004, ["kbCtrlShift"] = 0x0004,
        ["kbLeftAlt"] = 0x0008, ["kbRightAlt"] = 0x0008, ["kbAltShift"] = 0x0008,
        ["kbScrollState"] = 0x0010, ["kbNumState"] = 0x0020, ["kbCapsState"] = 0x0040, ["kbInsState"] = 0x0080,
        ["kbMetaShift"] = 0x0100,
    };

    [Fact]
    public void MetaIsAPublicStateMaskOfItsOwnBit()
    {
        FieldInfo field = typeof(Keys).GetField(nameof(Keys.kbMetaShift), BindingFlags.Public | BindingFlags.Static)!;
        Assert.NotNull(field);
        Assert.True(field.IsLiteral);
        Assert.Equal(typeof(uint), field.FieldType);
        Assert.Equal(0x0100u, Keys.kbMetaShift);
        Assert.Equal(1, BitOperations.PopCount(Keys.kbMetaShift));

        uint others = Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift
            | Keys.kbScrollState | Keys.kbNumState | Keys.kbCapsState | Keys.kbInsState;
        Assert.Equal(0u, others & Keys.kbMetaShift);
        // The first free bit: every lower bit is already a modifier or a lock.
        Assert.Equal(Keys.kbMetaShift - 1, others);
    }

    [Fact]
    public void ExistingStateMasksKeepTheirValuesAndNoOtherMaskWasAdded()
    {
        FieldInfo[] masks = typeof(Keys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(uint)).ToArray();
        Assert.Equal(StateMasks.Keys.Order(), masks.Select(f => f.Name).Order());
        foreach (FieldInfo field in masks)
            Assert.Equal(StateMasks[field.Name], Convert.ToUInt32(field.GetRawConstantValue(), CultureInfo.InvariantCulture));
        // No Cmd/Meta key-code family: Meta lives in the state only.
        Assert.DoesNotContain(typeof(Keys).GetFields(), f => f.Name.StartsWith("kbCmd", StringComparison.Ordinal)
            || (f.Name.StartsWith("kbMeta", StringComparison.Ordinal) && f.Name != nameof(Keys.kbMetaShift)));
    }

    [Fact]
    public void CombinationsKeepEveryBitThroughTheEventAccessors()
    {
        uint[] combinations =
        [
            Keys.kbMetaShift,
            Keys.kbShift | Keys.kbMetaShift,
            Keys.kbCtrlShift | Keys.kbMetaShift,
            Keys.kbAltShift | Keys.kbMetaShift,
            Keys.kbShift | Keys.kbCtrlShift | Keys.kbMetaShift,
            Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift | Keys.kbMetaShift | Keys.kbCapsState | Keys.kbNumState,
        ];
        foreach (uint state in combinations)
        {
            TEvent ev = default;
            ev.What = Events.evKeyDown;
            ev.keyDown.keyCode = Keys.kbF5;
            ev.keyDown.controlKeyState = state;
            TEvent copy = ev;
            Assert.Equal(state, copy.Modifiers);
            copy.Modifiers = state;
            Assert.Equal(state, copy.keyDown.controlKeyState);
            Assert.Contains($"control=0x{state:X8}", InputTrace.FormatEvent(copy));
        }
    }

    [Fact]
    public void CapabilityValuesAreStableAndMetaTransitionsAreTheirOwnFlag()
    {
        Assert.Equal(0, (int)KeyboardCapabilities.None);
        Assert.Equal(1, (int)KeyboardCapabilities.KeyReleaseEvents);
        Assert.Equal(2, (int)KeyboardCapabilities.StandaloneModifierTransitions);
        Assert.Equal(4, (int)KeyboardCapabilities.DistinctNumericKeypad);
        Assert.Equal(8, (int)KeyboardCapabilities.StandaloneMetaTransitions);
    }

    [Fact]
    public void PhysicalMetaKeysAggregateIntoOneLogicalFlag()
    {
        var tracker = new ModifierStateTracker();
        Assert.True(tracker.Set(PhysicalModifier.LeftMeta, true, out uint state));
        Assert.Equal(Keys.kbMetaShift, state);
        Assert.False(tracker.Set(PhysicalModifier.RightMeta, true, out state));
        Assert.False(tracker.Set(PhysicalModifier.LeftMeta, false, out state));
        Assert.Equal(Keys.kbMetaShift, state);
        Assert.True(tracker.Set(PhysicalModifier.LeftShift, true, out state));
        Assert.Equal(Keys.kbShift | Keys.kbMetaShift, state);
        Assert.True(tracker.Set(PhysicalModifier.RightMeta, false, out state));
        Assert.Equal(Keys.kbShift, state);
        Assert.True(tracker.Set(PhysicalModifier.LeftMeta, true, out _));
        Assert.True(tracker.Reset(out state));
        Assert.Equal(0u, state);
        Assert.Equal(0u, tracker.LogicalState);
    }

    [Fact]
    public void PublicApiBaselineShipsTheMetaEntries()
    {
        string root = RepositoryRoot();
        string shipped = File.ReadAllText(Path.Combine(root, "src", "TSharpVision", "PublicAPI.Shipped.txt"));
        string[] unshipped = File.ReadAllLines(Path.Combine(root, "src", "TSharpVision", "PublicAPI.Unshipped.txt"));
        // Meta is released API: nothing about it may be pending, while unrelated later additions may be.
        Assert.Equal("#nullable enable", unshipped.FirstOrDefault());
        Assert.DoesNotContain(unshipped, line => line.Contains("Meta", StringComparison.Ordinal)
            || line.Contains("KeyboardCapabilities", StringComparison.Ordinal));
        Assert.Contains(
            "TSharpVision.Drivers.KeyboardCapabilities.StandaloneMetaTransitions = 8 -> TSharpVision.Drivers.KeyboardCapabilities",
            shipped);
        foreach ((string name, uint value) in StateMasks)
            Assert.Contains($"const TSharpVision.Constants.Keys.{name} = {value} -> uint", shipped);
        Assert.Contains("TSharpVision.Drivers.KeyboardCapabilities.StandaloneModifierTransitions = 2 ->", shipped);
    }

    [Theory]
    [InlineData("1", null, true)]
    [InlineData(null, "1", true)]
    [InlineData("0", "1", false)]
    [InlineData(null, null, false)]
    [InlineData("true", null, false)]
    public void InputTraceReadsTheDocumentedUppercaseName(string? upper, string? legacy, bool enabled)
    {
        Assert.Equal("TSHARPVISION_TRACE_INPUT", InputTrace.EnvironmentVariable);
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["TSHARPVISION_TRACE_INPUT"] = upper,
            ["TSharpVision_TRACE_INPUT"] = legacy,
        };
        Assert.Equal(enabled, InputTrace.IsEnabled(name => variables.GetValueOrDefault(name)));
    }

    [Fact]
    public void InputTraceWritesTheDocumentedLowercaseFileName()
        => Assert.Equal("tsharpvision-input-trace.log", InputTrace.LogFileName);

    private static string RepositoryRoot() =>
        TSharpVision.Tests.Infrastructure.RepositoryLayout.Root(typeof(MetaModifierContractTests).Assembly);
}
