using System.Text;

namespace TSharpVision.Text;

/// <summary>
/// Built-in and explicitly registered legacy single-byte encodings.
/// Registration affects only this lookup table. It does not change rendering,
/// drivers, glyphs, or any global TSharpVision UI code page.
/// </summary>
public static class LegacyTextEncodings
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, ILegacyTextEncoding> Registry =
        new(StringComparer.OrdinalIgnoreCase);
    private static bool _codePagesRegistered;

    public static readonly ILegacyTextEncoding Latin1 =
        new DotNetLegacyTextEncoding("latin1", 28591);
    public static readonly ILegacyTextEncoding Cp437 =
        new DotNetLegacyTextEncoding("cp437", 437);
    public static readonly ILegacyTextEncoding Cp852 =
        new DotNetLegacyTextEncoding("cp852", 852);
    public static readonly ILegacyTextEncoding Windows1250 =
        new DotNetLegacyTextEncoding("windows-1250", 1250);
    public static readonly ILegacyTextEncoding Iso8859_2 =
        new DotNetLegacyTextEncoding("iso-8859-2", 28592);
    public static readonly ILegacyTextEncoding Kamenicky =
        new SingleByteTextEncoding("kamenicky", KamenickyEncodingTable.ByteToChar);

    static LegacyTextEncodings()
    {
        RegisterBuiltIn(Latin1, "latin1", "latin-1", "iso-8859-1", "28591");
        RegisterBuiltIn(Cp437, "cp437", "437", "ibm437");
        RegisterBuiltIn(Cp852, "cp852", "852", "dos-latin2", "dos-latin-2");
        RegisterBuiltIn(Windows1250, "windows-1250", "win1250", "cp1250", "1250");
        RegisterBuiltIn(Iso8859_2, "iso-8859-2", "latin2", "latin-2", "28592");
        RegisterBuiltIn(Kamenicky, "kamenicky", "keybcs2", "keybcs", "cp895", "895");
    }

    internal static void EnsureCodePagesRegistered()
    {
        lock (Sync)
        {
            if (_codePagesRegistered) return;

            // This registers .NET's code-page provider for Encoding.GetEncoding.
            // It is not a TSharpVision global code page and has no renderer side
            // effects.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _codePagesRegistered = true;
        }
    }

    public static bool TryGet(string name, out ILegacyTextEncoding encoding)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            encoding = null;
            return false;
        }

        lock (Sync)
            return Registry.TryGetValue(name.Trim(), out encoding);
    }

    public static void Register(string name, ILegacyTextEncoding encoding)
    {
        ValidateRegistration(name, encoding);
        lock (Sync)
            Registry.Add(name.Trim(), encoding);
    }

    public static bool TryRegister(string name, ILegacyTextEncoding encoding)
    {
        ValidateRegistration(name, encoding);
        lock (Sync)
        {
            if (Registry.ContainsKey(name.Trim()))
                return false;

            Registry.Add(name.Trim(), encoding);
            return true;
        }
    }

    public static void RegisterSingleByte(string name, IReadOnlyList<char> byteToChar)
        => Register(name, new SingleByteTextEncoding(name, byteToChar));

    private static void RegisterBuiltIn(ILegacyTextEncoding encoding, params string[] names)
    {
        foreach (string name in names)
            Registry.Add(name, encoding);
    }

    private static void ValidateRegistration(string name, ILegacyTextEncoding encoding)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Encoding name must not be empty.", nameof(name));
        if (encoding == null)
            throw new ArgumentNullException(nameof(encoding));
    }
}
