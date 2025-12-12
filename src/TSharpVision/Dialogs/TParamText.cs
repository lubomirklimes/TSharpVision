namespace TSharpVision;

public class TParamText : TStaticText
{
    public static readonly string Name = "TParamText";

    protected short ParamCount;
    protected object[] ParamList;

    // Upstream is TParamText(bounds) with separate setText.
    // We accept the format and an optional arg count
    // matching the existing skeleton signature.
    public TParamText(TRect bounds, string aText, int aParamCount)
        : base(bounds, aText)
    {
        ParamCount = (short)aParamCount;
        ParamList = System.Array.Empty<object>();
    }

    public void SetText(string fmt, params object[] args)
    {
        Text = fmt ?? string.Empty;
        ParamList = args ?? System.Array.Empty<object>();
        ParamCount = (short)ParamList.Length;
        DrawView();
    }

    public override ushort DataSize() => 0;

    public override void GetText(out string result)
    {
        if (string.IsNullOrEmpty(Text)) { result = string.Empty; return; }
        if (ParamList == null || ParamList.Length == 0) { result = Text; return; }
        try { result = string.Format(Text, ParamList); }
        catch (System.FormatException) { result = Text; }
    }

    public override void SetData(object rec)
    {
        if (rec is object[] arr) { ParamList = arr; ParamCount = (short)arr.Length; DrawView(); }
    }

    public static readonly TStreamableClass StreamableClassTParamText =
        new TStreamableClass("TParamText", () => new TParamText(StreamableInit.streamableInit), 0);

    protected TParamText(StreamableInit init) : base(init) { }

    public new static TStreamable Build() => new TParamText(StreamableInit.streamableInit);
}
