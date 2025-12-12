using System.IO;

namespace TSharpVision;

/// Base class for handling streamable objects. Holds the underlying
/// .NET <see cref="Stream"/> (the upstream <c>CLY_streambuf*</c>),
/// the iostate bits, and the static type registry shared by
/// <see cref="Opstream"/> and <see cref="Ipstream"/>.
public abstract class Pstream
{
    public enum StreamableError
    {
        peNotRegistered,
        peInvalidType,
    }

    public const byte ptNull = 0;
    public const byte ptIndexed = 1;
    public const byte ptObject = 2;

    // We only need the three bits the upstream pstream actually inspects.
    public const int IOSEOFBit = 0x01;
    public const int IOSFailBit = 0x02;
    public const int IOSBadBit = 0x04;

    protected Stream bp;
    public int state;

    // Upstream lazily allocates `types` in initTypes() and frees it via
    // atexit. In C# a single eagerly-constructed registry is sufficient.
    public static TStreamableTypes types = new TStreamableTypes();

    protected Pstream() { }
    protected Pstream(Stream sb) { Init(sb); }

    protected void Init(Stream sb) { bp = sb; }

    public int Rdstate() => state;
    public int Eof() => state & IOSEOFBit;
    public int Fail() => state & (IOSFailBit | IOSBadBit);
    public int Bad() => state & IOSBadBit;
    public bool Good() => state == 0;
    public void Clear(int s = 0) { state = s; }
    public void SetState(int s) { state |= s; }

    // Upstream forwards to a pluggable handler; we set the fail bit and
    // record the most recent error so smoke tests can inspect it.
    public StreamableError lastError;
    public bool hasError;
    public virtual void Error(StreamableError e)
    {
        lastError = e;
        hasError = true;
        state |= IOSFailBit;
    }
    
    public virtual void Error(StreamableError e, TStreamable t) => Error(e);

    public static void RegisterType(TStreamableClass tc) => types.RegisterType(tc);

    public static void InitTypes()
    {
        types ??= new TStreamableTypes();
    }
    
    public static void DeInitTypes()
    {
        types = new TStreamableTypes();
    }
}
