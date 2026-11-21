using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace TSharpVision.Tests.Samples;

/// <summary>
/// Builds TVDemo's embedded character-name database (<c>samples/TSharpVision.Samples.TVDemo/Unicode/CharacterNames.txt.gz</c>)
/// from hash-pinned Unicode 16.0.0 and CLDR 46 files kept in the test assets. Offline and deterministic.
/// </summary>
/// <remarks>
/// <see cref="CharacterNameDataTests"/> compares the decompressed embedded text with a fresh generation; set
/// <c>TSV_REGENERATE_CHARACTER_NAMES=1</c> to rewrite the file. Line format (UTF-8, LF, fields separated by ';'):
/// <list type="bullet">
/// <item><c>#</c> header comments (versions, sources, hashes).</item>
/// <item><c>B;start;end;block name</c> from Blocks.txt.</item>
/// <item><c>R;start;end;NAME PREFIX-</c> ranges whose names are the prefix plus the hex code (UAX #44 NR2).</item>
/// <item><c>H;AC00;D7A3</c> the Hangul syllables (names by UAX #44 NR1).</item>
/// <item><c>C;code;name;aliases;cldr short name;cldr keywords</c>, one per named or aliased scalar; lists use '|'.
/// The name is the Unicode Name property (empty for controls, whose labels come from NameAliases).</item>
/// </list>
/// </remarks>
internal static class CharacterNameDataGenerator
{
    public const string FormatVersion = "1";

    public static readonly (string File, string Directory, string Url, string Sha256)[] Inputs =
    [
        ("UnicodeData.txt", "Text/UnicodeData/16.0.0", "https://www.unicode.org/Public/16.0.0/ucd/UnicodeData.txt",
            "ff58e5823bd095166564a006e47d111130813dcf8bf234ef79fa51a870edb48f"),
        ("NameAliases.txt", "Samples/UnicodeNames", "https://www.unicode.org/Public/16.0.0/ucd/NameAliases.txt",
            "9953f0fcebf5ea8091c5c581e4df0e43f20d2533c84ccca7987a9bb819a896a8"),
        ("Blocks.txt", "Samples/UnicodeNames", "https://www.unicode.org/Public/16.0.0/ucd/Blocks.txt",
            "f3907b395d410f1b97342292ca6bc83dd12eb4b205f2a0c48efdef99e517d7b0"),
        ("cldr-46-annotations-en.xml", "Samples/UnicodeNames",
            "https://raw.githubusercontent.com/unicode-org/cldr/release-46/common/annotations/en.xml",
            "7d491f1782480d50ee953d3943e8a65eacaa4394b41af9e9f89c9be418256ade"),
    ];

    public static string TestsRoot(string repositoryRoot) => Path.Combine(repositoryRoot, "tests", "TSharpVision.Tests");

    public static string TargetFile(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "samples", "TSharpVision.Samples.TVDemo", "Unicode", "CharacterNames.txt.gz");

    public static string ReadVerified(string repositoryRoot, string file)
    {
        var input = Array.Find(Inputs, i => i.File == file);
        string path = Path.Combine(TestsRoot(repositoryRoot), input.Directory, file);
        string text = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        if (hash != input.Sha256)
            throw new InvalidDataException($"{file}: SHA-256 {hash} does not match the pinned {input.Sha256}.");
        return text;
    }

    /// <summary>The uncompressed database text.</summary>
    public static string GenerateText(string repositoryRoot)
    {
        var names = new SortedDictionary<int, string>();
        var ranges = new List<(int First, int Last, string Prefix)>();
        int first = -1;
        foreach (string line in ReadVerified(repositoryRoot, "UnicodeData.txt").Split('\n'))
        {
            if (line.Length == 0) continue;
            string[] f = line.Split(';');
            int cp = int.Parse(f[0], NumberStyles.HexNumber);
            string name = f[1];
            if (name.EndsWith(", First>", StringComparison.Ordinal)) { first = cp; continue; }
            if (name.EndsWith(", Last>", StringComparison.Ordinal))
            {
                if (name.StartsWith("<CJK Ideograph", StringComparison.Ordinal)) ranges.Add((first, cp, "CJK UNIFIED IDEOGRAPH-"));
                else if (name.StartsWith("<Tangut Ideograph", StringComparison.Ordinal)) ranges.Add((first, cp, "TANGUT IDEOGRAPH-"));
                // Hangul syllables are emitted as H; surrogates and private use have no names.
                continue;
            }
            names[cp] = name == "<control>" ? string.Empty : name;
        }

        var aliases = new SortedDictionary<int, List<string>>();
        foreach (string raw in ReadVerified(repositoryRoot, "NameAliases.txt").Split('\n'))
        {
            string line = StripComment(raw);
            if (line.Length == 0) continue;
            string[] f = line.Split(';');
            int cp = int.Parse(f[0], NumberStyles.HexNumber);
            if (!aliases.TryGetValue(cp, out var list)) aliases[cp] = list = [];
            list.Add(f[1].Trim());
        }

        var shortNames = new Dictionary<int, string>();
        var keywords = new Dictionary<int, string>();
        XDocument cldr = XDocument.Parse(ReadVerified(repositoryRoot, "cldr-46-annotations-en.xml"));
        foreach (XElement a in cldr.Descendants("annotation"))
        {
            string cps = ((string?)a.Attribute("cp") ?? string.Empty).Replace("️", string.Empty);
            // Only single scalars: sequences (flags, ZWJ, keycaps) are not characters of the browser.
            if (cps.EnumerateRunes().Count() != 1) continue;
            int cp = cps.EnumerateRunes().First().Value;
            string value = a.Value.Trim();
            if ((string?)a.Attribute("type") == "tts") shortNames[cp] = value;
            else keywords[cp] = string.Join('|', value.Split('|').Select(k => k.Trim()).Where(k => k.Length > 0));
        }

        var sb = new StringBuilder();
        sb.Append("# TSharpVision TVDemo character names, format ").Append(FormatVersion).Append('\n');
        sb.Append("# Unicode 16.0.0 names and aliases; CLDR 46 English annotations (short names and keywords, not Unicode names).\n");
        foreach (var (file, _, url, sha) in Inputs)
            sb.Append("# ").Append(url).Append(" sha256=").Append(sha).Append('\n');
        sb.Append("# Unicode, Inc. data under the Unicode License v3; see resources/licences/THIRD-PARTY-NOTICES.md.\n");

        foreach (string raw in ReadVerified(repositoryRoot, "Blocks.txt").Split('\n'))
        {
            string line = StripComment(raw);
            if (line.Length == 0) continue;
            string[] f = line.Split(';');
            string[] r = f[0].Trim().Split("..");
            sb.Append("B;").Append(r[0]).Append(';').Append(r[1]).Append(';').Append(f[1].Trim()).Append('\n');
        }
        foreach (var (lo, hi, prefix) in ranges)
            sb.Append("R;").Append(Hex(lo)).Append(';').Append(Hex(hi)).Append(';').Append(prefix).Append('\n');
        sb.Append("H;AC00;D7A3\n");

        var codes = new SortedSet<int>(names.Keys);
        codes.UnionWith(aliases.Keys);
        foreach (int cp in codes)
        {
            names.TryGetValue(cp, out string? name);
            aliases.TryGetValue(cp, out var aliasList);
            shortNames.TryGetValue(cp, out string? shortName);
            keywords.TryGetValue(cp, out string? keywordList);
            sb.Append("C;").Append(Hex(cp)).Append(';').Append(name ?? string.Empty).Append(';')
              .Append(aliasList is null ? string.Empty : string.Join('|', aliasList)).Append(';')
              .Append(Clean(shortName)).Append(';').Append(Clean(keywordList)).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>GZip of <paramref name="text"/> (UTF-8). The equality test compares decompressed text only.</summary>
    public static byte[] Compress(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }

    public static string Decompress(byte[] data)
    {
        using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    private static string Hex(int cp) => cp.ToString("X4", CultureInfo.InvariantCulture);

    // ';' separates fields: CLDR text never contains it, but guard anyway.
    private static string Clean(string? value) => (value ?? string.Empty).Replace(';', ',');

    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#');
        return (hash < 0 ? line : line[..hash]).Trim();
    }
}
