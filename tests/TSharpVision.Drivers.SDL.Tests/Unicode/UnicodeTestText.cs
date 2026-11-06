using System.IO.Compression;
using System.Text;

namespace TSharpVision.Drivers.SDL.Tests.Unicode;

/// <summary>
/// TV13m test text built from scalar values (sources stay ASCII), the TV13h samples, screen helpers and a tiny PNG
/// writer for the opt-in visual captures.
/// </summary>
internal static class UnicodeTestText
{
    /// <summary>Text made of the given scalars.</summary>
    public static string S(params int[] scalars)
    {
        var sb = new StringBuilder();
        foreach (int s in scalars) sb.Append(char.ConvertFromUtf32(s));
        return sb.ToString();
    }

    public static readonly string Grinning = S(0x1F600);
    public static readonly string ThumbsUpMedium = S(0x1F44D, 0x1F3FD);
    public static readonly string FlagCz = S(0x1F1E8, 0x1F1FF);
    public static readonly string Family = S(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467);
    public static readonly string Technologist = S(0x1F469, 0x1F3FD, 0x200D, 0x1F4BB);
    public static readonly string EAcute = S('e', 0x0301);
    public static readonly string Kan = S(0x6F22);
    public static readonly string Ji = S(0x5B57);
    public static readonly string HeartText = S(0x2764, 0xFE0E);
    public static readonly string HeartEmoji = S(0x2764, 0xFE0F);
    public static readonly string Keycap1 = S('1', 0xFE0F, 0x20E3);

    /// <summary>TV13h sample 1: A, grinning, B, thumbs up + tone, C, flag CZ, D, family, E.</summary>
    public static readonly string Sample1 = "A" + Grinning + "B" + ThumbsUpMedium + "C" + FlagCz + "D" + Family + "E";

    /// <summary>TV13h sample 2: e + combining acute.</summary>
    public static readonly string Sample2 = EAcute;

    /// <summary>TV13h sample 3: two CJK ideographs.</summary>
    public static readonly string Sample3 = Kan + Ji;

    /// <summary>Writes <paramref name="text"/> into row <paramref name="row"/> from <paramref name="column"/> with the core writer.</summary>
    public static void Write(ScreenBuffer screen, uint row, int column, string text, byte attr)
    {
        var buffer = new TDrawBuffer(screen.Data.Slice((int)(row * screen.Width), (int)screen.Width));
        buffer.moveStr(column, text, attr);
    }

    /// <summary>Places a lead of <paramref name="cells"/> cells and its trails directly (bypassing the writers).</summary>
    public static void PutCluster(ScreenBuffer screen, uint col, uint row, string cluster, int cells, byte attr, int trails = -1)
    {
        screen.SetChar(col, row, TScreenChar.CreateCluster(cluster, cells, (TColorAttr)attr));
        if (trails < 0) trails = cells - 1;
        for (int i = 1; i <= trails; i++) screen.SetChar(col + (uint)i, row, TScreenChar.CreateTrail((TColorAttr)attr));
    }

    public static ScreenBuffer Blank(uint cols, uint rows, byte attr = 0x07)
    {
        var screen = new ScreenBuffer((ushort)cols, (ushort)rows);
        for (uint r = 0; r < rows; r++)
            for (uint c = 0; c < cols; c++)
                screen.SetChar(c, r, new TScreenChar(' ', (TColorAttr)attr));
        return screen;
    }

    /// <summary>Writes an 8-bit RGBA PNG (no filtering).</summary>
    public static void SavePng(string path, int width, int height, byte[] rgba)
    {
        using var file = File.Create(path);
        file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)width);
        WriteBE(ihdr, 4, (uint)height);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(file, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgba, y * width * 4, width * 4);
            }
        }
        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len);
        byte[] t = Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        uint crc = Crc(t, 0xFFFFFFFFu);
        crc = Crc(data, crc) ^ 0xFFFFFFFFu;
        var c = new byte[4];
        WriteBE(c, 0, crc);
        s.Write(c);
    }

    private static uint Crc(byte[] data, uint crc)
    {
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }

    private static void WriteBE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }
}
