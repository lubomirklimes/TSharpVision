using TSharpVision;
using TSharpVision.Drivers.Terminal;
using Xunit;

namespace TSharpVision.Tests.Drivers;

public sealed class AnsiTerminalDriverOutputTests
{
    [Fact]
    public void FormatBuffer_RedrawAndResize_EmitsCompleteRowsAndRestoresCaret()
    {
        var cells = new[]
        {
            new TScreenChar('A', new TColorAttr(0x07)),
            new TScreenChar('B', new TColorAttr(0x07)),
            new TScreenChar('C', new TColorAttr(0x1F)),
            new TScreenChar('\0', new TColorAttr(0x1F)),
        };
        string gray = AnsiTerminalDriver.AttrToSgr(new TColorAttr(0x07));
        string blue = AnsiTerminalDriver.AttrToSgr(new TColorAttr(0x1F));

        for (int i = 0; i < 50; i++)
        {
            string output = AnsiTerminalDriver.FormatBuffer(i, i + 1, 2, 2, cells, 4, 5);
            Assert.Equal($"\x1b[{i + 2};{i + 1}H{gray}AB\x1b[{i + 3};{i + 1}H{blue}C \x1b[0m\x1b[6;5H",
                output);
        }
    }

    [Fact]
    public async Task FormatBuffer_ConcurrentRedraws_DoNotMixOutput()
    {
        const int width = 2048;
        const int iterations = 40;
        using var start = new Barrier(2);
        var first = Task.Run(() => Render('A', 0x07, 0));
        var second = Task.Run(() => Render('B', 0x1F, 1));
        var results = await Task.WhenAll(first, second);

        Assert.All(results[0].Outputs, output => Assert.Equal(results[0].Expected, output));
        Assert.All(results[1].Outputs, output => Assert.Equal(results[1].Expected, output));

        (string Expected, string[] Outputs) Render(char character, byte attribute, int row)
        {
            var cells = new TScreenChar[width];
            Array.Fill(cells, new TScreenChar(character, new TColorAttr(attribute)));
            string expected = $"\x1b[{row + 1};1H" +
                AnsiTerminalDriver.AttrToSgr(new TColorAttr(attribute)) +
                new string(character, width) + "\x1b[0m\x1b[1;1H";

            var outputs = new string[iterations];
            for (int i = 0; i < iterations; i++)
            {
                if (!start.SignalAndWait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("The concurrent render did not reach the barrier.");
                outputs[i] = AnsiTerminalDriver.FormatBuffer(0, row, width, 1, cells, 0, 0);
            }
            return (expected, outputs);
        }
    }
}
