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
public class THelpFile
{
    public const uint magicHeader = 0x46484246u; // 'FBHF'
    public const uint magicHeaderV2 = 0x32484246u; // 'FBH2'
    public const int FormatV1Latin1 = THelpTopic.FormatV1Latin1;
    public const int FormatV2Utf16 = THelpTopic.FormatV2Utf16;

    // Converted to a get-only property so the active
    // TSharpVisionIntl provider is consulted on each read.
    public static string InvalidContext
        => TSharpVisionIntl.Get("Help_NoContext", "\n No help available in this context.");

    public Fpstream stream;
    public bool modified;
    public THelpIndex index;
    public long indexPos;
    public int formatVersion;
    public ILegacyTextEncoding LegacyEncoding { get; }

    public THelpFile(Fpstream s)
        : this(s, FormatV2Utf16)
    {
    }

    public THelpFile(Fpstream s, int newFileFormatVersion)
        : this(s, newFileFormatVersion, LegacyTextEncodings.Latin1)
    {
    }

    public THelpFile(Fpstream s, HelpV1LoadOptions options)
        : this(s, FormatV2Utf16, options?.LegacyEncoding ?? LegacyTextEncodings.Latin1)
    {
    }

    public THelpFile(Fpstream s, int newFileFormatVersion, HelpV1CompileOptions options)
        : this(s, newFileFormatVersion, options?.LegacyEncoding ?? LegacyTextEncodings.Latin1)
    {
    }

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
            index = (THelpIndex)s.In.ReadPointer() ?? new THelpIndex();
            modified = false;
        }
    }

    public THelpTopic GetTopic(int i)
    {
        long pos = index.Position(i);
        if (pos > 0)
        {
            stream.In.Seekg(pos);
            stream.In.HelpFormatVersion = formatVersion;
            stream.In.HelpLegacyEncoding = LegacyEncoding;
            return (THelpTopic)stream.In.ReadPointer();
        }
        return InvalidTopic();
    }

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

    public void RecordPositionInIndex(int i)
    {
        index.Add(i, indexPos);
        modified = true;
    }

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
    public static void RegisterStreamableTypes()
    {
        Pstream.RegisterType(THelpTopic.StreamableClass);
        Pstream.RegisterType(THelpIndex.StreamableClass);
    }

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
