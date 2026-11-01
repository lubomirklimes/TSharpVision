using System.Globalization;
using TSharpVision.Constants;
namespace TSharpVision.Samples.TVDemo;

// The framework palette family and the three palette tables of a saved desktop. A null BlackWhite or Monochrome
// table means "not recorded" (a format 1 snapshot): loading leaves the live table as it is.
public sealed record DesktopPalettes(TProgram.AP Mode, byte[] Color, byte[]? BlackWhite, byte[]? Monochrome)
{
    public byte[]? Table(TProgram.AP mode) => mode switch
    {
        TProgram.AP.Color => Color,
        TProgram.AP.BlackWhite => BlackWhite,
        TProgram.AP.Monochrome => Monochrome,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}

public sealed record DesktopSnapshot(List<TWindow> Windows, DesktopPalettes Palettes, bool Legacy);

// Only plain demonstration windows have a defined persistence contract.
// Games, accessories and file viewers remain open when a snapshot is restored.
//
// File: one TResourceFile holding
//   "metadata"      TStringCollection [ "TVDemo desktop 1", hex(Color table), window count ]   (format 1, unchanged)
//   "window.NNNNN"  one TWindow per saved window, back to front
//   "palettes"      TStringCollection [ "TVDemo palettes 1", "mode=<family>", "Color=<t>",
//                                       "BlackWhite=<t>", "Monochrome=<t>" ]                   (added by format 2)
// where <t> is "default" (the table equals TProgram's built-in table) or the hex of the whole table including its
// leading size byte. A reader that does not know "palettes" (TVDemo before TV12d) still finds a complete format 1
// snapshot whose palette is the Color table. A snapshot without "palettes" is format 1: its palette is the Color
// table, the family is Color, and the BlackWhite and Monochrome tables are not recorded.
public static class DesktopState
{
    internal const string MetadataKey = "metadata", PalettesKey = "palettes";
    internal const string DesktopHeader = "TVDemo desktop 1", PalettesHeader = "TVDemo palettes 1";
    internal const string DefaultToken = "default";
    private const string DesktopHeaderPrefix = "TVDemo desktop ", PalettesHeaderPrefix = "TVDemo palettes ";
    private const int MaxWindows = 1000;

    private static readonly TProgram.AP[] Modes = { TProgram.AP.Color, TProgram.AP.BlackWhite, TProgram.AP.Monochrome };

    public static bool Supports(TWindow window)
    {
        bool supported = window.GetType() == typeof(TWindow);
        window.ForEachView(view => supported &= view.GetType() == typeof(TFrame)
            || view.GetType() == typeof(TStaticText));
        return supported;
    }

    // Built-in table of a family, including the leading size byte, from TProgram's public constants: the pristine
    // reference does not depend on what any running program has done to the shared tables.
    public static byte[] DefaultTable(TProgram.AP mode) => mode switch
    {
        TProgram.AP.Color => new TPalette(TProgram.cpColor, TProgram.cpColor.Length).Data,
        TProgram.AP.BlackWhite => new TPalette(TProgram.cpBlackWhite, TProgram.cpBlackWhite.Length).Data,
        TProgram.AP.Monochrome => new TPalette(TProgram.cpMonochrome, TProgram.cpMonochrome.Length).Data,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    internal static string ModeName(TProgram.AP mode) => mode switch
    {
        TProgram.AP.Color => "Color",
        TProgram.AP.BlackWhite => "BlackWhite",
        TProgram.AP.Monochrome => "Monochrome",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    // Exact names only: numbers, other casing and whitespace are rejected (Enum.Parse would accept "1" or " color").
    private static TProgram.AP ParseMode(string name) => name switch
    {
        "Color" => TProgram.AP.Color,
        "BlackWhite" => TProgram.AP.BlackWhite,
        "Monochrome" => TProgram.AP.Monochrome,
        _ => throw new InvalidDataException($"Unknown framework palette '{name}'."),
    };

    // Format 1 writer, kept byte for byte: a Color-only snapshot with no palette-family record.
    public static void Save(string path, IReadOnlyList<TWindow> windows, byte[] palette) =>
        Write(path, windows, palette, null);

    // Format 2 writer: the format 1 records (with the Color table as their palette) plus the "palettes" record.
    public static void Save(string path, IReadOnlyList<TWindow> windows, DesktopPalettes palettes)
    {
        if (!Enum.IsDefined(palettes.Mode)) throw new ArgumentOutOfRangeException(nameof(palettes), "Undefined palette family.");
        var record = new TStringCollection();
        record.Insert(PalettesHeader);
        record.Insert("mode=" + ModeName(palettes.Mode));
        foreach (var mode in Modes)
        {
            byte[] table = palettes.Table(mode) ?? throw new ArgumentException($"The {ModeName(mode)} table is required.", nameof(palettes));
            if (table.Length != palettes.Color.Length) throw new ArgumentException("Palette tables differ in size.", nameof(palettes));
            record.Insert(ModeName(mode) + "=" + (table.AsSpan().SequenceEqual(DefaultTable(mode)) ? DefaultToken : Convert.ToHexString(table)));
        }
        Write(path, windows, palettes.Color, record);
    }

    private static void Write(string path, IReadOnlyList<TWindow> windows, byte[] colorPalette, TStringCollection? palettes)
    {
        if (windows.Any(w => !Supports(w)))
            throw new ArgumentException("Only plain demonstration windows can be saved.");
        StreamableRegistration.RegisterAll();
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite))
            {
                var stream = new Fpstream(file);
                var resources = new TResourceFile(stream);
                var metadata = new TStringCollection();
                metadata.Insert(DesktopHeader);
                metadata.Insert(Convert.ToHexString(colorPalette));
                metadata.Insert(windows.Count.ToString(CultureInfo.InvariantCulture));
                resources.Put(metadata, MetadataKey);
                for (int i = 0; i < windows.Count; i++)
                    resources.Put(windows[i], $"window.{i:D5}");
                if (palettes != null) resources.Put(palettes, PalettesKey);
                resources.Flush();
                if (stream.Out.Fail() != 0) throw new IOException("Desktop serialization failed.");
            }
            // The previous snapshot is replaced only by a completely written one.
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // Format 1 view of a snapshot of either format: the windows and the Color table.
    public static (List<TWindow> Windows, byte[] Palette) Load(string path, int paletteLength)
    {
        var snapshot = Read(path, paletteLength);
        return (snapshot.Windows, snapshot.Palettes.Color);
    }

    // Reads and validates the whole snapshot before returning anything; nothing is applied here. On any failure the
    // windows read so far are shut down and the exception propagates.
    public static DesktopSnapshot Load(string path) => Read(path, DefaultTable(TProgram.AP.Color).Length);

    private static DesktopSnapshot Read(string path, int paletteLength)
    {
        StreamableRegistration.RegisterAll();
        var windows = new List<TWindow>();
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
            // Fpstream requires a writable stream for its output half.
            using var copy = new FileStream(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tvr"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                4096, FileOptions.DeleteOnClose);
            file.CopyTo(copy);
            copy.Position = 0;
            var stream = new Fpstream(copy);
            var resources = new TResourceFile(stream);
            if (resources.Get(MetadataKey) is not TStringCollection meta || meta.Count < 1 || !meta[0].StartsWith(DesktopHeaderPrefix, StringComparison.Ordinal))
                throw new InvalidDataException("Not a TVDemo desktop snapshot.");
            if (meta[0] != DesktopHeader)
                throw new InvalidDataException($"Unsupported desktop snapshot version '{meta[0][DesktopHeaderPrefix.Length..]}' (saved by a newer TVDemo?).");
            if (meta.Count != 3) throw new InvalidDataException("Not a TVDemo desktop snapshot.");
            byte[] color = ParseTable(meta[1], paletteLength, "Color");
            if (!int.TryParse(meta[2], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count > MaxWindows)
                throw new InvalidDataException("Invalid window count.");

            object? record = resources.Get(PalettesKey);
            DesktopPalettes palettes = record switch
            {
                null => new DesktopPalettes(TProgram.AP.Color, color, null, null),
                TStringCollection entries => ParsePalettes(entries, paletteLength),
                _ => throw InvalidRecord(record),
            };
            if (stream.In.Fail() != 0) throw new InvalidDataException("Incomplete desktop snapshot.");

            for (int i = 0; i < count; i++)
            {
                if (resources.Get($"window.{i:D5}") is not TWindow window)
                    throw new InvalidDataException("Missing saved window.");
                windows.Add(window);
                if (!Supports(window)) throw new InvalidDataException("Unsupported saved window type.");
            }
            if (stream.In.Fail() != 0) throw new InvalidDataException("Incomplete desktop snapshot.");
            return new DesktopSnapshot(windows, palettes, record == null);
        }
        catch
        {
            foreach (var window in windows) window.ShutDown();
            throw;
        }
    }

    private static InvalidDataException InvalidRecord(object record)
    {
        if (record is TView view) view.ShutDown();
        return new InvalidDataException("Invalid palette record.");
    }

    private static DesktopPalettes ParsePalettes(TStringCollection entries, int paletteLength)
    {
        if (entries.Count < 1 || !entries[0].StartsWith(PalettesHeaderPrefix, StringComparison.Ordinal))
            throw new InvalidDataException("Invalid palette record.");
        if (entries[0] != PalettesHeader)
            throw new InvalidDataException($"Unsupported palette record version '{entries[0][PalettesHeaderPrefix.Length..]}' (saved by a newer TVDemo?).");
        if (entries.Count != 2 + Modes.Length) throw new InvalidDataException("Invalid palette record.");
        var mode = ParseMode(Value(entries[1], "mode"));
        var tables = new byte[Modes.Length][];
        for (int i = 0; i < Modes.Length; i++)
        {
            string name = ModeName(Modes[i]);
            string value = Value(entries[2 + i], name);
            if (value != DefaultToken) tables[i] = ParseTable(value, paletteLength, name);
            else if ((tables[i] = DefaultTable(Modes[i])).Length != paletteLength)
                throw new InvalidDataException("Palette size differs from this application.");
        }
        return new DesktopPalettes(mode, tables[0], tables[1], tables[2]);
    }

    private static string Value(string entry, string key) =>
        entry.StartsWith(key + "=", StringComparison.Ordinal) ? entry[(key.Length + 1)..] : throw new InvalidDataException("Invalid palette record.");

    private static byte[] ParseTable(string hex, int paletteLength, string name)
    {
        byte[] table;
        try { table = Convert.FromHexString(hex); }
        catch (FormatException) { throw new InvalidDataException($"The {name} palette is not valid."); }
        if (table.Length != paletteLength) throw new InvalidDataException("Palette size differs from this application.");
        // The first byte is the table's own entry count (TPalette.Size); a mismatch means the table is corrupt.
        if (table.Length == 0 || table[0] != paletteLength - 1) throw new InvalidDataException($"The {name} palette is not valid.");
        return table;
    }
}
