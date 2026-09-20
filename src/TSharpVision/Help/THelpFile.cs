using System;
using TSharpVision.Text;

namespace TSharpVision;

// THelpFile — keyed help-topic store backed by an Fpstream.
//
// On-disk layout (matches Borland TVHC v1):
//   offset 0..3   long magic = 0x46484246 ('FBHF')
//   offset 4..7   long size  (filelength - 8 — bookkeeping only)
//   offset 8..11  long indexPos (offset of the serialized THelpIndex)
//   offset 12..   help topics, written via WritePointer
//   offset basePos+indexPos: serialized THelpIndex (via WritePointer)
//
// Note: 'FBHF' and 'FBH2' share their low half ('FB' = 0x4246) with the
// resource-file magic 0x52504246 ('FBPR'). The TResourceFile header scanner
// therefore treats a help file as a foreign FB-block and skips it cleanly
// when one is appended to a resource container and vice versa.
/// <summary>Indexed help-topic store backed by a caller-managed file stream; call Flush to persist pending index changes.</summary>
public class THelpFile
{
    /// <summary>File signature identifying version 1 single-byte help data.</summary>
    public const uint magicHeader = 0x46484246u; // 'FBHF'
    /// <summary>File signature identifying version 2 UTF-16 help data.</summary>
    public const uint magicHeaderV2 = 0x32484246u; // 'FBH2'
    /// <summary>Version selector for legacy single-byte help text using the configured legacy encoding.</summary>
    public const int FormatV1Latin1 = THelpTopic.FormatV1Latin1;
    /// <summary>Version selector for UTF-16 help text.</summary>
    public const int FormatV2Utf16 = THelpTopic.FormatV2Utf16;

    // Converted to a get-only property so the active
    // TSharpVisionIntl provider is consulted on each read.
    /// <summary>Localized fallback text for a help context absent from the file.</summary>
    public static string InvalidContext
        => TSharpVisionIntl.Get("Help_NoContext", "\n No help available in this context.");

    /// <summary>Backing stream used for topic and index I/O; its lifetime is managed by the caller.</summary>
    public Fpstream stream;
    /// <summary>Whether the index and file header have changes pending a Flush call.</summary>
    public bool modified;
    /// <summary>Mapping from help context identifiers to serialized topic positions.</summary>
    public THelpIndex index;
    /// <summary>Byte position of the index, also used as the insertion point for the next topic.</summary>
    public long indexPos;
    /// <summary>Active help format version, detected from an existing header or chosen for a new file.</summary>
    public int formatVersion;
    /// <summary>Encoding used for version 1 text; version 2 text uses UTF-16.</summary>
    public ILegacyTextEncoding LegacyEncoding { get; }

    /// <summary>Reads an existing help index or initializes a version 2 store when no recognized header exists; the caller retains the stream lifetime.</summary>
    public THelpFile(Fpstream s)
        : this(s, FormatV2Utf16)
    {
    }

    /// <summary>Reads an existing help store or initializes the requested version, using Latin-1 for legacy text; the caller manages the stream.</summary>
    public THelpFile(Fpstream s, int newFileFormatVersion)
        : this(s, newFileFormatVersion, LegacyTextEncodings.Latin1)
    {
    }

    /// <summary>Opens a help store with the supplied legacy decoding options, falling back to Latin-1; new stores use version 2.</summary>
    public THelpFile(Fpstream s, HelpV1LoadOptions options)
        : this(s, FormatV2Utf16, options?.LegacyEncoding ?? LegacyTextEncodings.Latin1)
    {
    }

    /// <summary>Opens a help store or initializes the requested version with the supplied legacy encoding options.</summary>
    public THelpFile(Fpstream s, int newFileFormatVersion, HelpV1CompileOptions options)
        : this(s, newFileFormatVersion, options?.LegacyEncoding ?? LegacyTextEncodings.Latin1)
    {
    }

    /// <summary>Reads a recognized help header or initializes a new index; version 1 is selected only when requested explicitly, and null encoding uses Latin-1. The stream remains caller-managed.</summary>
    public THelpFile(Fpstream s, int newFileFormatVersion, ILegacyTextEncoding legacyEncoding)
    {
        stream = s;
        LegacyEncoding = legacyEncoding ?? LegacyTextEncodings.Latin1;
        long fileSize = s.Filelength();
        s.In.Seekg(0);
        uint magic = 0;
        if (fileSize > 4)
            magic = s.In.Read32();
        if (magic != magicHeader && magic != magicHeaderV2)
        {
            formatVersion = newFileFormatVersion == FormatV1Latin1
                ? FormatV1Latin1
                : FormatV2Utf16;
            indexPos = 12;
            s.In.Seekg(indexPos);
            index = new THelpIndex();
            modified = true;
        }
        else
        {
            formatVersion = magic == magicHeader
                ? FormatV1Latin1
                : FormatV2Utf16;
            s.In.Seekg(8);
            indexPos = (int)s.In.Read32();
            s.In.Seekg(indexPos);
            s.In.HelpFormatVersion = formatVersion;
            s.In.HelpLegacyEncoding = LegacyEncoding;
            index = s.In.ReadPointer() as THelpIndex ?? new THelpIndex();
            modified = false;
        }
    }

    /// <summary>Loads the topic for a nonnegative help context identifier; an unindexed context produces a fallback topic.</summary>
    public THelpTopic GetTopic(int i)
    {
        long pos = index.Position(i);
        if (pos > 0)
        {
            stream.In.Seekg(pos);
            stream.In.HelpFormatVersion = formatVersion;
            stream.In.HelpLegacyEncoding = LegacyEncoding;
            if (stream.In.ReadPointer() is THelpTopic topic)
                return topic;
        }
        return InvalidTopic();
    }

    /// <summary>Creates a topic containing localized text explaining that no help is available.</summary>
    public THelpTopic InvalidTopic()
    {
        var topic = new THelpTopic();
        var para = new TParagraph
        {
            wrap = false,
            next = null,
        };
        para.Text = InvalidContext;
        topic.AddParagraph(para);
        return topic;
    }

    /// <summary>Maps a nonnegative help context identifier to the next topic insertion position and marks the index modified.</summary>
    public void RecordPositionInIndex(int i)
    {
        index.Add(i, indexPos);
        modified = true;
    }

    /// <summary>Writes a topic at the current index position and advances that position; record its context mapping before calling this method.</summary>
    public void PutTopic(THelpTopic topic)
    {
        stream.Out.Seekp(indexPos);
        stream.Out.HelpFormatVersion = formatVersion;
        stream.Out.HelpLegacyEncoding = LegacyEncoding;
        stream.Out.WritePointer(topic);
        indexPos = stream.Out.Tellp();
        modified = true;
    }

    // Registers THelpTopic and THelpIndex with the current Pstream type
    // registry.  Must be called explicitly after any Pstream.DeInitTypes()
    // call because C# static field initializers only run once per process.
    // Using Pstream.RegisterType(X.StreamableClass) (rather than just
    // accessing the field) is safe even after DeInitTypes: it always inserts
    // the existing TStreamableClass object into the fresh registry.
    /// <summary>Registers help topic and index factories; call again after clearing the stream type registry.</summary>
    public static void RegisterStreamableTypes()
    {
        Pstream.RegisterType(THelpTopic.StreamableClass);
        Pstream.RegisterType(THelpIndex.StreamableClass);
    }

    /// <summary>Writes a modified index and header, flushes output, and clears the modified flag without closing the stream.</summary>
    public void Flush()
    {
        if (!modified) return;
        stream.Out.Seekp(indexPos);
        stream.Out.HelpFormatVersion = formatVersion;
        stream.Out.HelpLegacyEncoding = LegacyEncoding;
        stream.Out.WritePointer(index);
        long after = stream.Out.Tellp();
        stream.Out.Seekp(0);
        stream.Out.Write32(formatVersion == THelpTopic.FormatV2Utf16
            ? magicHeaderV2
            : magicHeader);
        stream.Out.Write32((uint)(after - 8));
        stream.Out.Write32((uint)indexPos);
        stream.Out.Flush();
        modified = false;
    }
}
