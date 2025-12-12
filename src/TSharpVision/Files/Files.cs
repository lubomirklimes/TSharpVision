// File-system DOS-style flags and the TSearchRec record used by
// TFileList, TFileCollection and TFileInfoPane.
namespace TSharpVision;

// DOS file attribute bits.
public static class FileAttr
{
    public const byte faReadOnly = 0x01;
    public const byte faHidden   = 0x02;
    public const byte faSystem   = 0x04;
    public const byte faVolumeId = 0x08;
    public const byte faDirec    = 0x10;
    public const byte faArch     = 0x20;
}

public class TSearchRec : IInfo
{
    public byte attr;
    public long time;     // upstream time_t — stored as Unix-style ticks.
    public long size;     // upstream size_t.
    public string name = string.Empty;
}

public static class FileCollectionOptions
{
    public const uint fcolAlphabetical    = 0;
    public const uint fcolDirsFirst       = 1;
    public const uint fcolDirsLast        = 2;
    public const uint fcolTypeMask        = 0x1F;

    public const uint fcolCaseInsensitive = 0x20;
    public const uint fcolCaseSensitive   = 0;

    public const uint fcolParentLast      = 0x40;
    public const uint fcolParentFirst     = 0;

    public const uint fcolDotsLast        = 0x80;
    public const uint fcolDotsFirst       = 0;

    public const uint fcolHideEndTilde    = 0x100;
    public const uint fcolHideEndBkp      = 0x200;
    public const uint fcolHideStartDot    = 0x400;
    public const uint fcolHideMask        = 0xF00;
}

public interface IFileDialogContext
{
    string Directory { get; }
    string WildCard  { get; }
}
