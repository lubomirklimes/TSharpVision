// TSharpVision Resource Tools
// TvrInspector — read-only inspector for .tvr binary resource files.
// Core inspection logic: open a .tvr file and enumerate its resources.
using System.IO;

namespace TSharpVision.ResourceTools;

/// <summary>
/// Opens a TSharpVision <c>.tvr</c> resource file, reads its index, and
/// exposes per-resource metadata and raw payload bytes.
/// Does not mutate the file.
/// </summary>
public sealed class TvrInspector
{
    // ptObject tag written by Opstream.WritePointer for every stored object.
    private const byte PtObject = 0x02;
    // '[' prefix byte written by Opstream.WritePrefix.
    private const byte PrefixBracket = 0x5B;
    // 0xFF sentinel = null string; 0xFE = long-length string. String payloads
    // are UTF-16 code units in little-endian order.
    private const byte NullLen = 0xFF;
    private const byte LongLen = 0xFE;

    /// <summary>Full path to the <c>.tvr</c> file.</summary>
    public string FilePath { get; }

    /// <summary>Total file size in bytes.</summary>
    public long FileSize { get; }

    /// <summary>All resource entries in index order (sorted alphabetically by key by TResourceCollection).</summary>
    public IReadOnlyList<ResourceEntry> Entries { get; }

    private TvrInspector(string path, long fileSize, List<ResourceEntry> entries)
    {
        FilePath = path;
        FileSize = fileSize;
        Entries  = entries;
    }

    /// <summary>
    /// Opens and inspects a <c>.tvr</c> file.
    /// </summary>
    /// <exception cref="FileNotFoundException">File does not exist.</exception>
    /// <exception cref="InvalidDataException">File is not a valid TSharpVision <c>.tvr</c>.</exception>
    public static TvrInspector Open(string tvrPath)
    {
        if (!File.Exists(tvrPath))
            throw new FileNotFoundException($"File not found: {tvrPath}", tvrPath);

        long fileSize = new FileInfo(tvrPath).Length;

        // TResourceFile constructor calls ReadPointer() to deserialize the
        // TResourceCollection index.  In a fresh process no streamable types
        // are registered yet, so we register them now before opening.
        StreamableRegistration.RegisterAll();

        var fp = new Fpstream(tvrPath);
        try
        {
            var rf = new TResourceFile(fp);
            if (rf.Count() == 0 && fileSize < 12)
                throw new InvalidDataException($"Not a valid TSharpVision .tvr file: {tvrPath}");

            var entries = new List<ResourceEntry>(rf.Count());
            for (int i = 0; i < rf.Count(); i++)
            {
                var item  = rf.ItemAt(i);
                var raw   = rf.GetRawBytes(item.key);
                var tname = raw != null ? PeekTypeName(raw) : null;
                entries.Add(new ResourceEntry
                {
                    Key      = item.key,
                    Position = item.pos,
                    Size     = item.size,
                    TypeName = tname,
                });
            }

            return new TvrInspector(tvrPath, fileSize, entries);
        }
        finally
        {
            fp.Close();
        }
    }

    /// <summary>
    /// Returns the raw payload bytes for the given key, or <c>null</c> if the key is not found.
    /// Opens its own file handle so it can be called independently of <see cref="Open"/>.
    /// </summary>
    public static byte[] ReadRawPayload(string tvrPath, string key)
    {
        if (!File.Exists(tvrPath))
            throw new FileNotFoundException($"File not found: {tvrPath}", tvrPath);

        StreamableRegistration.RegisterAll();
        var fp = new Fpstream(tvrPath);
        try
        {
            var rf = new TResourceFile(fp);
            return rf.GetRawBytes(key);
        }
        finally
        {
            fp.Close();
        }
    }

    /// <summary>
    /// Extracts the streamable type name from the first bytes of a resource payload.
    /// Returns <c>null</c> if the payload does not start with the expected <c>ptObject + '['</c>
    /// prefix, or if the encoded length is invalid.
    /// </summary>
    public static string PeekTypeName(byte[] payload)
    {
        if (payload == null || payload.Length < 3) return null;
        if (payload[0] != PtObject)      return null;
        if (payload[1] != PrefixBracket) return null;

        byte len0 = payload[2];
        if (len0 == NullLen) return null;  // null string

        int nameStart;
        int nameLen;
        if (len0 == LongLen)
        {
            // 4-byte length follows.
            if (payload.Length < 7) return null;
            nameLen = (int)(
                payload[3]
                | ((uint)payload[4] << 8)
                | ((uint)payload[5] << 16)
                | ((uint)payload[6] << 24));
            nameStart = 7;
        }
        else
        {
            nameLen   = len0;
            nameStart = 3;
        }

        int byteLen = checked(nameLen * 2);
        if (nameLen < 0 || payload.Length < nameStart + byteLen) return null;

        var chars = new char[nameLen];
        for (int i = 0; i < nameLen; i++)
        {
            int offset = nameStart + i * 2;
            chars[i] = (char)(payload[offset] | (payload[offset + 1] << 8));
        }
        return new string(chars);
    }
}
