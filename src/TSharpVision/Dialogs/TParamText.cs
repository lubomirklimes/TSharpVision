namespace TSharpVision;

/// <summary>Static text formatted from a composite format string and argument array.</summary>
public class TParamText : TStaticText
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TParamText";

    /// <summary>Number of format arguments currently recorded.</summary>
    protected short ParamCount;
    /// <summary>Arguments substituted into the stored composite format string.</summary>
    protected object[] ParamList;

    // Upstream is TParamText(bounds) with separate setText.
    // We accept the format and an optional arg count
    // matching the existing skeleton signature.
    /// <summary>Creates formatted text at owner-relative cell bounds, storing the requested argument count with an initially empty argument list.</summary>
    public TParamText(TRect bounds, string aText, int aParamCount)
        : base(bounds, aText)
    {
        ParamCount = (short)aParamCount;
        ParamList = System.Array.Empty<object>();
    }

    /// <summary>Replaces the composite format and arguments and redraws the text.</summary>
    public void SetText(string fmt, params object[] args)
    {
        Text = fmt ?? string.Empty;
        ParamList = args ?? System.Array.Empty<object>();
        ParamCount = (short)ParamList.Length;
        DrawView();
    }

    /// <inheritdoc />
    public override ushort DataSize() => 0;

    /// <inheritdoc />
    public override void GetText(out string result)
    {
        if (string.IsNullOrEmpty(Text)) { result = string.Empty; return; }
        if (ParamList == null || ParamList.Length == 0) { result = Text; return; }
        try { result = string.Format(Text, ParamList); }
        catch (System.FormatException) { result = Text; }
    }

    /// <inheritdoc />
    public override void SetData(object rec)
    {
        if (rec is object[] arr) { ParamList = arr; ParamCount = (short)arr.Length; DrawView(); }
    }

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTParamText =
        new TStreamableClass("TParamText", () => new TParamText(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TParamText(StreamableInit init) : base(init) { ParamList = System.Array.Empty<object>(); }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TParamText(StreamableInit.streamableInit);
}
