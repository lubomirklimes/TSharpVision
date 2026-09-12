// File-system DOS-style flags and the TSearchRec record used by
// TFileList, TFileCollection and TFileInfoPane.
namespace TSharpVision;

/// <summary>DOS-compatible attribute bits carried by file-dialog search records.</summary>
public static class FileAttr
{
    /// <summary>Marks a file as read-only.</summary>
    public const byte faReadOnly = 0x01;
    /// <summary>Marks a file as hidden.</summary>
    public const byte faHidden   = 0x02;
    /// <summary>Marks a system file.</summary>
    public const byte faSystem   = 0x04;
    /// <summary>Marks a volume-label entry.</summary>
    public const byte faVolumeId = 0x08;
    /// <summary>Marks an entry as a directory.</summary>
    public const byte faDirec    = 0x10;
    /// <summary>Marks a file for archival or backup.</summary>
    public const byte faArch     = 0x20;
}

/// <summary>File-dialog entry containing a name, attributes, modification timestamp, and byte size.</summary>
public class TSearchRec : IInfo
{
    /// <summary>Combined DOS-compatible FileAttr bits describing this entry.</summary>
    public byte attr;
    /// <summary>Last-write timestamp as seconds since the Unix epoch; synthetic entries may use zero.</summary>
    public long time;     // upstream time_t — stored as Unix-style ticks.
    /// <summary>File length in bytes; directory and synthetic entries may use zero.</summary>
    public long size;     // upstream size_t.
    /// <summary>Entry name displayed and compared by file lists, including the synthetic parent name '..'.</summary>
    public string name = string.Empty;
}

/// <summary>Bit values controlling file-list sorting and filename-category filtering.</summary>
public static class FileCollectionOptions
{
    /// <summary>Sorts by name without directory grouping.</summary>
    public const uint fcolAlphabetical    = 0;
    /// <summary>Groups directories before ordinary files.</summary>
    public const uint fcolDirsFirst       = 1;
    /// <summary>Groups directories after ordinary files.</summary>
    public const uint fcolDirsLast        = 2;
    /// <summary>Mask extracting the directory-grouping sort mode.</summary>
    public const uint fcolTypeMask        = 0x1F;

    /// <summary>Uses ordinal case-insensitive filename comparison.</summary>
    public const uint fcolCaseInsensitive = 0x20;
    /// <summary>Uses ordinal case-sensitive filename comparison when the case-insensitive bit is absent.</summary>
    public const uint fcolCaseSensitive   = 0;

    /// <summary>Places the '..' entry last when directory grouping is active.</summary>
    public const uint fcolParentLast      = 0x40;
    /// <summary>Places the '..' entry first when directory grouping is active and the parent-last bit is absent.</summary>
    public const uint fcolParentFirst     = 0;

    /// <summary>Sorts dot-prefixed names after other names, excluding the parent entry.</summary>
    public const uint fcolDotsLast        = 0x80;
    /// <summary>Leaves dot-prefixed names in normal name order when the dots-last bit is absent.</summary>
    public const uint fcolDotsFirst       = 0;

    /// <summary>Filename-filter category for names ending in a tilde.</summary>
    public const uint fcolHideEndTilde    = 0x100;
    /// <summary>Filename-filter category for names at least five characters long ending in '.bkp', compared case-insensitively.</summary>
    public const uint fcolHideEndBkp      = 0x200;
    /// <summary>Filename-filter category for dot-prefixed names.</summary>
    public const uint fcolHideStartDot    = 0x400;
    /// <summary>Mask selecting filename-hiding category bits.</summary>
    public const uint fcolHideMask        = 0xF00;
}

/// <summary>Provides the current directory and wildcard filter to associated file-dialog controls.</summary>
public interface IFileDialogContext
{
    /// <summary>Directory path used to resolve entries shown by the dialog.</summary>
    string Directory { get; }
    /// <summary>Wildcard expression restricting filenames shown by the dialog.</summary>
    string WildCard  { get; }
}
