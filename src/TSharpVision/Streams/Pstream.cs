using System.IO;
using TSharpVision.Text;

namespace TSharpVision;

/// Base class for handling streamable objects. Holds the underlying
/// .NET <see cref="Stream"/> (the upstream <c>CLY_streambuf*</c>),
/// the iostate bits, and the static type registry shared by
/// <see cref="Opstream"/> and <see cref="Ipstream"/>.
public abstract class Pstream
{
    /// <summary>Errors reported while resolving serialized object types and references.</summary>
    public enum StreamableError
    {
        /// <summary>The requested streamable type has no registered factory.</summary>
        peNotRegistered,
        /// <summary>Serialized data contains an invalid type or object-reference encoding.</summary>
        peInvalidType,
    }

    /// <summary>Pointer tag representing a null object reference.</summary>
    public const byte ptNull = 0;
    /// <summary>Pointer tag referring to an object already registered in the current stream graph.</summary>
    public const byte ptIndexed = 1;
    /// <summary>Pointer tag introducing a newly serialized object.</summary>
    public const byte ptObject = 2;

    // We only need the three bits the upstream pstream actually inspects.
    /// <summary>State bit indicating that input reached the end of the underlying stream.</summary>
    public const int IOSEOFBit = 0x01;
    /// <summary>State bit indicating a failed stream operation or serialization error.</summary>
    public const int IOSFailBit = 0x02;
    /// <summary>State bit indicating an underlying stream failure.</summary>
    public const int IOSBadBit = 0x04;

    /// <summary>Underlying byte stream used by concrete readers or writers; may be null before initialization.</summary>
    protected Stream? bp;
    internal Stream Buffer =>
        bp ?? throw new InvalidOperationException("The serialization stream is not initialized.");
    /// <summary>Combined stream status bits; zero means no status flags are set.</summary>
    public int state;
    /// <summary>Help-topic serialization version; defaults to version 2 UTF-16.</summary>
    public int HelpFormatVersion = THelpTopic.FormatV2Utf16;
    /// <summary>Single-byte encoding for legacy help payloads; defaults to Latin-1.</summary>
    public ILegacyTextEncoding HelpLegacyEncoding = LegacyTextEncodings.Latin1;

    // Upstream lazily allocates `types` in initTypes() and frees it via
    // atexit. In C# a single eagerly-constructed registry is sufficient.
    /// <summary>Process-wide registry mapping serialized type names to restoration factories.</summary>
    public static TStreamableTypes types = new TStreamableTypes();

    /// <summary>Creates an unbound serialization stream with clear status and default help encoding settings.</summary>
    protected Pstream() { }
    /// <summary>References the supplied byte stream with clear status; lifetime handling belongs to concrete wrappers or the caller.</summary>
    protected Pstream(Stream sb) { Init(sb); }

    /// <summary>Replaces the backing byte-stream reference without resetting status or closing the previous stream.</summary>
    protected void Init(Stream sb) { bp = sb; }

    /// <summary>Returns all current stream status bits.</summary>
    public int Rdstate() => state;
    /// <summary>Returns the end-of-input bit, or zero when that bit is clear.</summary>
    public int Eof() => state & IOSEOFBit;
    /// <summary>Returns the failure and bad-stream bits, or zero when both are clear.</summary>
    public int Fail() => state & (IOSFailBit | IOSBadBit);
    /// <summary>Returns the bad-stream bit, or zero when that bit is clear.</summary>
    public int Bad() => state & IOSBadBit;
    /// <summary>Returns true when no stream status bits are set.</summary>
    public bool Good() => state == 0;
    /// <summary>Replaces the stream status bits, defaulting to zero; recorded serialization-error fields are unchanged.</summary>
    public void Clear(int s = 0) { state = s; }
    /// <summary>Adds the supplied status bits without clearing existing bits.</summary>
    public void SetState(int s) { state |= s; }

    // Upstream forwards to a pluggable handler; we set the fail bit and
    // record the most recent error so smoke tests can inspect it.
    /// <summary>Most recently reported serialization error; meaningful only when hasError is true.</summary>
    public StreamableError lastError;
    /// <summary>Whether a serialization error has been recorded by Error.</summary>
    public bool hasError;
    /// <summary>Records a serialization error and sets the failure bit; the base implementation does not throw.</summary>
    public virtual void Error(StreamableError e)
    {
        lastError = e;
        hasError = true;
        state |= IOSFailBit;
    }
    
    /// <summary>Reports a serialization error associated with an object; the base implementation ignores the object and records the error.</summary>
    public virtual void Error(StreamableError e, TStreamable t) => Error(e);

    /// <summary>Registers a restoration descriptor in the current registry, replacing any descriptor with the same name.</summary>
    public static void RegisterType(TStreamableClass tc) => types.RegisterType(tc);

    /// <summary>Creates the global type registry if its reference is null, preserving existing registrations otherwise.</summary>
    public static void InitTypes()
    {
        types ??= new TStreamableTypes();
    }
    
    /// <summary>Replaces the global registry with an empty one; types must be explicitly registered again before restoration.</summary>
    public static void DeInitTypes()
    {
        types = new TStreamableTypes();
    }
}
