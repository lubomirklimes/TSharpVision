using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TSharpVision.Tests.Text;

/// <summary>
/// Generates <c>src/TSharpVision/Text/UnicodeWidthData.g.cs</c> from the hash-pinned Unicode 16.0.0 files
/// under <c>tests/TSharpVision.Tests/Text/UnicodeData/16.0.0</c>. Offline: no network access is involved.
/// </summary>
/// <remarks>
/// <see cref="UnicodeWidthDataGenerationTests"/> regenerates the source in memory and requires it to equal
/// the checked-in file. To rewrite the file after changing the inputs or this generator, run that test with
/// the environment variable <c>TSV_REGENERATE_UNICODE_WIDTH=1</c>.
/// </remarks>
internal static class UnicodeWidthTableGenerator
{
    public const string UnicodeVersion = "16.0.0";
    public const string GeneratorVersion = "1";

    /// <summary>Input files with their SHA-256 over LF-normalised content (equal to the published files).</summary>
    public static readonly (string File, string Url, string Sha256)[] Inputs =
    [
        ("EastAsianWidth.txt", "https://www.unicode.org/Public/16.0.0/ucd/EastAsianWidth.txt",
            "43adc76c0686a42cb370764eb8cfe2b2a45b10b855e5572a2db4a0eecce15d5b"),
        ("emoji-data.txt", "https://www.unicode.org/Public/16.0.0/ucd/emoji/emoji-data.txt",
            "f1365a5173eee18e1f98b240cdc492e84a25f1ce7e0c9d1094eb29c41a22696a"),
        ("emoji-variation-sequences.txt", "https://www.unicode.org/Public/16.0.0/ucd/emoji/emoji-variation-sequences.txt",
            "71d93ec015011371a027ba2bc0a63155d381c6e0b94a586c1a88a49400cd6864"),
        ("UnicodeData.txt", "https://www.unicode.org/Public/16.0.0/ucd/UnicodeData.txt",
            "ff58e5823bd095166564a006e47d111130813dcf8bf234ef79fa51a870edb48f"),
        ("GraphemeBreakTest.txt", "https://www.unicode.org/Public/16.0.0/ucd/auxiliary/GraphemeBreakTest.txt",
            "ee2b9354d270ac061b29f09662cafea06341d77e704b8cc6bd72aaeeda363cb5"),
    ];

    // Property bits; must match TSharpVision.Text.UnicodeWidthData.
    public const byte EawWide = 0x01, EawAmbiguous = 0x02, ZeroWidth = 0x04, EmojiPresentation = 0x08,
        ExtendedPictographic = 0x10, EmojiVariationBase = 0x20, RegionalIndicator = 0x40, HangulMedialFinal = 0x80;

    public static string DataDirectory(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "tests", "TSharpVision.Tests", "Text", "UnicodeData", UnicodeVersion);

    public static string TargetFile(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "src", "TSharpVision", "Text", "UnicodeWidthData.g.cs");

    /// <summary>Reads a pinned input as LF-normalised text and verifies its hash.</summary>
    public static string ReadVerified(string dataDirectory, string file)
    {
        var input = Array.Find(Inputs, i => i.File == file);
        if (input.File is null) throw new ArgumentException($"Not a pinned input: {file}", nameof(file));
        string text = File.ReadAllText(Path.Combine(dataDirectory, file), Encoding.UTF8).Replace("\r\n", "\n");
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        if (hash != input.Sha256)
            throw new InvalidDataException($"{file}: SHA-256 {hash} does not match the pinned {input.Sha256}.");
        return text;
    }

    /// <summary>Property byte for every code point 0..10FFFF.</summary>
    public static byte[] BuildProperties(string dataDirectory)
    {
        var props = new byte[0x110000];

        foreach (var (lo, hi, value) in Ranges(ReadVerified(dataDirectory, "EastAsianWidth.txt")))
        {
            byte bit = value switch { "W" or "F" => EawWide, "A" => EawAmbiguous, _ => 0 };
            for (int cp = lo; cp <= hi; cp++) props[cp] = (byte)((props[cp] & ~0x03) | bit);
        }

        foreach (var (lo, hi, value) in Ranges(ReadVerified(dataDirectory, "emoji-data.txt")))
        {
            byte bit = value switch
            {
                "Emoji_Presentation" => EmojiPresentation,
                "Extended_Pictographic" => ExtendedPictographic,
                _ => 0,
            };
            for (int cp = lo; cp <= hi; cp++) props[cp] |= bit;
        }

        foreach (string raw in ReadVerified(dataDirectory, "emoji-variation-sequences.txt").Split('\n'))
        {
            string line = StripComment(raw);
            if (line.Length == 0) continue;
            string sequence = line.Split(';')[0].Trim();
            int cp = int.Parse(sequence.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], NumberStyles.HexNumber);
            props[cp] |= EmojiVariationBase;
        }

        int rangeStart = -1;
        foreach (string line in ReadVerified(dataDirectory, "UnicodeData.txt").Split('\n'))
        {
            if (line.Length == 0) continue;
            string[] f = line.Split(';');
            int cp = int.Parse(f[0], NumberStyles.HexNumber);
            bool zero = f[2] is "Mn" or "Me" or "Cf" && cp != 0x00AD;
            if (f[1].EndsWith(", First>", StringComparison.Ordinal)) { rangeStart = cp; continue; }
            int lo = f[1].EndsWith(", Last>", StringComparison.Ordinal) ? rangeStart : cp;
            if (zero) for (int c = lo; c <= cp; c++) props[c] |= ZeroWidth;
        }

        // Regional_Indicator (UAX #29) and the Hangul medial vowels / final consonants (Grapheme_Cluster_Break
        // V and T, the zero-advance jamo of terminal wcwidth): fixed ranges, stable since Unicode 6.0 and 5.2.
        for (int cp = 0x1F1E6; cp <= 0x1F1FF; cp++) props[cp] |= RegionalIndicator;
        for (int cp = 0x1160; cp <= 0x11FF; cp++) props[cp] |= HangulMedialFinal;
        for (int cp = 0xD7B0; cp <= 0xD7FF; cp++) props[cp] |= HangulMedialFinal;
        return props;
    }

    /// <summary>The complete C# source of UnicodeWidthData.g.cs, with LF line endings.</summary>
    public static string GenerateSource(string dataDirectory)
    {
        byte[] props = BuildProperties(dataDirectory);
        var blocks = new List<byte[]>();
        var index = new Dictionary<string, int>();
        var stage1 = new byte[0x110000 >> 8];
        for (int b = 0; b < stage1.Length; b++)
        {
            byte[] block = props.AsSpan(b << 8, 256).ToArray();
            string key = Convert.ToHexString(block);
            if (!index.TryGetValue(key, out int id))
            {
                id = blocks.Count;
                if (id > byte.MaxValue) throw new InvalidOperationException("More than 256 distinct blocks.");
                index.Add(key, id);
                blocks.Add(block);
            }
            stage1[b] = (byte)id;
        }

        var sb = new StringBuilder();
        sb.Append("// <auto-generated>\n");
        sb.Append("// Generated by tests/TSharpVision.Tests/Text/UnicodeWidthTableGenerator.cs (generator version ")
          .Append(GeneratorVersion).Append(").\n");
        sb.Append("// Do not edit. Regenerate with TSV_REGENERATE_UNICODE_WIDTH=1 and the UnicodeWidthDataGenerationTests.\n");
        sb.Append("//\n// Unicode ").Append(UnicodeVersion).Append(" sources (SHA-256 of the LF-normalised file):\n");
        foreach (var (file, url, sha) in Inputs)
            sb.Append("//   ").Append(url).Append('\n').Append("//     ").Append(sha).Append('\n');
        sb.Append("//\n");
        sb.Append("// Unicode data is Copyright (c) 1991-2026 Unicode, Inc. and distributed under the\n");
        sb.Append("// Unicode License v3 (https://www.unicode.org/license.txt); see resources/licences/THIRD-PARTY-NOTICES.md.\n");
        sb.Append("// </auto-generated>\n\n");
        sb.Append("namespace TSharpVision.Text;\n\n");
        sb.Append("internal static partial class UnicodeWidthData\n{\n");
        sb.Append("    /// <summary>Unicode version of the width and emoji properties in this table.</summary>\n");
        sb.Append("    public const string UnicodeVersion = \"").Append(UnicodeVersion).Append("\";\n\n");
        sb.Append("    // Block index (one byte per 256 code points) into Stage2.\n");
        AppendBytes(sb, "Stage1", stage1);
        sb.Append('\n');
        sb.Append("    // ").Append(blocks.Count).Append(" distinct 256-entry blocks of property bytes.\n");
        AppendBytes(sb, "Stage2", blocks.SelectMany(b => b).ToArray());
        sb.Append("}\n");
        return sb.ToString();
    }

    private static void AppendBytes(StringBuilder sb, string name, byte[] data)
    {
        sb.Append("    private static ReadOnlySpan<byte> ").Append(name).Append(" =>\n    [\n");
        for (int i = 0; i < data.Length; i += 32)
        {
            sb.Append("        ");
            int n = Math.Min(32, data.Length - i);
            for (int j = 0; j < n; j++)
            {
                sb.Append("0x").Append(data[i + j].ToString("X2", CultureInfo.InvariantCulture)).Append(',');
                if (j < n - 1) sb.Append(' ');
            }
            sb.Append('\n');
        }
        sb.Append("    ];\n");
    }

    private static IEnumerable<(int Lo, int Hi, string Value)> Ranges(string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            string line = StripComment(raw);
            if (line.Length == 0) continue;
            string[] parts = line.Split(';');
            string range = parts[0].Trim();
            string value = parts[1].Trim();
            int dots = range.IndexOf("..", StringComparison.Ordinal);
            int lo = int.Parse(dots < 0 ? range : range[..dots], NumberStyles.HexNumber);
            int hi = dots < 0 ? lo : int.Parse(range[(dots + 2)..], NumberStyles.HexNumber);
            yield return (lo, hi, value);
        }
    }

    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#');
        return (hash < 0 ? line : line[..hash]).Trim();
    }
}
