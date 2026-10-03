using System.Text;
using Xunit;

namespace TSharpVision.Terminal.Tests.Emulation;

/// <summary>Feeding and inspecting a <see cref="TerminalEmulator"/> in tests.</summary>
internal static class EmulatorTestKit
{
    public static TerminalEmulator New(int columns = 10, int rows = 4, int scrollback = 100)
        => new(columns, rows, scrollback);

    public static TerminalEmulator Fed(string stream, int columns = 10, int rows = 4, int scrollback = 100)
    {
        TerminalEmulator emulator = New(columns, rows, scrollback);
        emulator.Feed(Encoding.UTF8.GetBytes(stream));
        return emulator;
    }

    public static void Feed(this TerminalEmulator emulator, string stream, bool asBytes)
    {
        if (asBytes) emulator.Feed(Encoding.UTF8.GetBytes(stream));
        else emulator.Feed(stream);
    }

    /// <summary>The screen as text rows, trailing blanks removed.</summary>
    public static string[] Screen(this TerminalEmulator emulator)
        => Enumerable.Range(0, emulator.Rows).Select(emulator.GetRowText).ToArray();

    public static void AssertScreen(this TerminalEmulator emulator, params string[] rows)
        => Assert.Equal(rows, emulator.Screen());

    public static void AssertCursor(this TerminalEmulator emulator, int row, int column)
        => Assert.Equal((row, column), (emulator.CursorRow, emulator.CursorColumn));

    /// <summary>
    /// Everything observable, cell by cell: content, width, style, cursor, modes, title and scrollback — so two
    /// emulators that agree here are indistinguishable.
    /// </summary>
    public static string Snapshot(this TerminalEmulator e)
    {
        var s = new StringBuilder();
        s.Append($"{e.Columns}x{e.Rows} cursor={e.CursorRow},{e.CursorColumn} visible={e.CursorVisible} alt={e.IsAlternateScreenActive} ");
        s.Append($"ckm={e.ApplicationCursorKeys} paste={e.BracketedPaste} wrap={e.AutoWrap} origin={e.OriginMode} insert={e.InsertMode} ");
        s.Append($"title=[{e.Title}] style={e.CurrentStyle} scrollback={e.ScrollbackCount}\n");
        for (int row = 0; row < e.BufferRowCount; row++)
        {
            ReadOnlySpan<TerminalCell> cells = e.GetBufferRow(row, out bool wrapped);
            s.Append(wrapped ? 'W' : '-').Append('|');
            foreach (TerminalCell cell in cells)
                s.Append(cell.Text).Append('/').Append(cell.Width).Append('/').Append(cell.Style).Append('|');
            s.Append('\n');
        }

        return s.ToString();
    }

    /// <summary>
    /// Feeds <paramref name="stream"/> whole, byte by byte, and in random pieces (several seeds), and asserts every way
    /// ends in the same state. Returns the whole-fed emulator.
    /// </summary>
    public static TerminalEmulator AssertChunkingIndependent(string stream, int columns = 20, int rows = 6)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(stream);
        TerminalEmulator whole = New(columns, rows);
        whole.Feed(bytes);
        string expected = whole.Snapshot();

        TerminalEmulator single = New(columns, rows);
        foreach (byte b in bytes) single.Feed(new[] { b });
        Assert.Equal(expected, single.Snapshot());

        for (int seed = 1; seed <= 8; seed++)
        {
            var random = new Random(seed);
            TerminalEmulator pieces = New(columns, rows);
            int at = 0;
            while (at < bytes.Length)
            {
                int length = Math.Min(bytes.Length - at, random.Next(1, 8));
                pieces.Feed(bytes.AsSpan(at, length));
                at += length;
            }

            Assert.Equal(expected, pieces.Snapshot());
        }

        return whole;
    }
}
