using System.Runtime.CompilerServices;
using System.Text;

namespace TSharpVision.Terminal.Tests;

/// <summary>
/// Collects session output as text for assertions. Each builder keeps its own UTF-8 decoder, so a character split
/// between two reads decodes as the one character it is — the same contract the emulator keeps.
/// </summary>
internal static class OutputTextExtensions
{
    private static readonly ConditionalWeakTable<StringBuilder, Decoder> Decoders = new();

    public static void AppendOutput(this StringBuilder output, TerminalOutputEventArgs e)
    {
        Decoder decoder = Decoders.GetValue(output, _ => Encoding.UTF8.GetDecoder());
        ReadOnlySpan<byte> bytes = e.Data.Span;
        char[] chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        int count = decoder.GetChars(bytes, chars, flush: false);
        output.Append(chars, 0, count);
    }
}
