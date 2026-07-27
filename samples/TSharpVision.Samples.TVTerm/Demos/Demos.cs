using System.Diagnostics;
using TSharpVision.Terminal;

namespace TSharpVision.Samples.TVTerm;

/// <summary>
/// Self-contained scripts emitted into an InMemorySession to exercise
/// specific terminal-rendering paths. Each demo writes one or more banners
/// + payload into the supplied session.
/// </summary>
public static class TerminalDemos
{
    public static void RunSelectionDemo(InMemoryTerminalSession s)
    {
        s.Emit("Drag to select. Ctrl+C copies selection; otherwise it interrupts.\n");
        s.Emit("Ctrl+V pastes into the local prompt. Esc clears selection.\n");
        s.Emit("Row A: The quick brown fox jumps over the lazy dog\n");
        s.Emit("Row B: 0123456789 abcdefghijklmnopqrstuvwxyz\n");
        s.Emit("Row C: TSharpVision TTerminal selection test line\n");
    }

    public static void RunUnicodeDemo(InMemoryTerminalSession s)
    {
        s.Emit("=== Unicode demo ===\n");
        s.Emit("Box drawing: ┌─┬─┐ │ │ │ ├─┼─┤ │ │ │ └─┴─┘\n");
        s.Emit("Block elements: ░ ▒ ▓ █ ▀ ▄ ▌ ▐\n");
        s.Emit("Arrows: ← ↑ → ↓ ↔ ↕ ⇐ ⇑ ⇒ ⇓\n");
        s.Emit("Mathematical: ∀ ∂ ∃ ∅ ∆ ∇ ∈ ∉ ∋ ∏ ∑ √ ∞ ∫ ≈ ≠ ≤ ≥\n");
        s.Emit("Latin Extended: Příliš žluťoučký kůň úpěl ďábelské ódy.\n");
        s.Emit("Greek: Καλημέρα κόσμε\n");
        s.Emit("Cyrillic: Здравствуй, мир\n");
        s.Emit("CJK (wide cells): 你好世界  こんにちは  안녕하세요\n");
        s.Emit("Emoji (variable width): 😀 🚀 🎉 ✅ ❌\n");
        s.Emit("=== end ===\n");
    }

    public static void RunAnsiDemo(InMemoryTerminalSession s)
    {
        s.Emit("=== ANSI demo ===\n");
        s.Emit("Normal text\n");
        s.Emit("\x1B[31mRed\x1B[0m  \x1B[32mGreen\x1B[0m  \x1B[33mYellow\x1B[0m  \x1B[34mBlue\x1B[0m  \x1B[35mMagenta\x1B[0m  \x1B[36mCyan\x1B[0m\n");
        s.Emit("\x1B[91mBrRed\x1B[0m \x1B[92mBrGreen\x1B[0m \x1B[93mBrYellow\x1B[0m \x1B[94mBrBlue\x1B[0m \x1B[95mBrMag\x1B[0m \x1B[96mBrCyan\x1B[0m\n");
        s.Emit("\x1B[1mBold\x1B[0m  \x1B[4mUnderline\x1B[0m  \x1B[7mReverse\x1B[0m\n");
        s.Emit("Background: \x1B[41m red \x1B[42m green \x1B[44m blue \x1B[0m\n");
        s.Emit("Combined: \x1B[1;33;44m bold yellow on blue \x1B[0m\n");
        s.Emit("=== end ===\n");
    }

    public static void RunPerformanceDemo(InMemoryTerminalSession s, int kilobytes = 64)
    {
        s.Emit($"=== Performance demo: writing {kilobytes} KB ===\n");
        const int chunk = 1024;
        var line = new string('x', chunk - 1) + "\n";
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < kilobytes; i++)
            s.Emit(line);
        sw.Stop();
        double mb = kilobytes / 1024.0;
        double secs = sw.Elapsed.TotalSeconds;
        double mbps = secs > 0 ? mb / secs : 0;
        s.Emit($"Emitted {kilobytes} KB in {sw.ElapsedMilliseconds} ms ({mbps:F2} MB/s); excludes completed screen rendering.\n");
        s.Emit("=== end ===\n");
    }
}
