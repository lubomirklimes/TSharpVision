using System.Reflection;
using System.Runtime.CompilerServices;
using TSharpVision.Constants;
using TSharpVision.Diagnostics.Keyboard.Protocol;

namespace TSharpVision.Diagnostics.Keyboard.Tests;

/// <summary>
/// The diagnostic target records what the driver reported. A program normally gives keypad keys their semantic key
/// code before anything sees them (<see cref="KeypadKeys.Normalize"/>); the target opts out, or it could only ever
/// report cursor keys and digits where the suites expect the keypad identities.
/// </summary>
public sealed class RawKeypadTests
{
    private static readonly Type Target = typeof(Hello).Assembly
        .GetType("TSharpVision.Diagnostics.Keyboard.Target.TargetApp", throwOnError: true)!;

    private static readonly MethodInfo Normalize = Target.GetMethod(
        "NormalizeKeyEvent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;

    [Fact]
    public void TheTargetDeclaresItsOwnNormalization() => Assert.NotNull(Normalize);

    [Theory]
    [InlineData(Keys.kbKeypad4, 0u, "")]                    // would be Left
    [InlineData(Keys.kbKeypad1, Keys.kbNumState, "1")]      // would be the character 1
    [InlineData(Keys.kbKeypadDecimal, 0u, "")]              // would be Del
    [InlineData(Keys.kbKeypadEnter, 0u, "")]                // would be Enter
    [InlineData(Keys.kbGrayPlus, Keys.kbNumState, "")]      // would lose the character of its scan pair
    public void TheTargetRecordsKeypadEventsExactlyAsReported(ushort identity, uint state, string text)
    {
        var reported = new TEvent { What = Events.evKeyDown };
        reported.keyDown.keyCode = identity;
        reported.keyDown.charScan = new CharScanType(identity);
        reported.keyDown.controlKeyState = state;
        reported.keyDown.text = text;

        // The method uses no state of the application, which cannot be started without a controller.
        object target = RuntimeHelpers.GetUninitializedObject(Target);
        object[] arguments = [reported];
        Normalize.Invoke(target, arguments);
        var seen = (TEvent)arguments[0];

        Assert.Equal(identity, seen.keyDown.keyCode);
        Assert.Equal(reported.keyDown.charScan.ToUShort(), seen.keyDown.charScan.ToUShort());
        Assert.Equal(state, seen.keyDown.controlKeyState);
        Assert.Equal(text, seen.keyDown.text);
        Assert.Equal(0, seen.keyDown.keypadKey);

        RecordedEvent recorded = RecordedEvent.From(seen, 0, 0);
        Assert.Equal(identity, recorded.KeyCode);
    }
}
