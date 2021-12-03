using SharpVision.Constants;

namespace SharpVision.Menus;

// ========================================================================
// 5. TStatusLine
// ========================================================================
public class TStatusLine : TView
{
    public const string cpStatusLine = "\x02\x03\x04\x05\x06\x07";

    static TPalette palette = new TPalette(cpStatusLine, (ushort)(cpStatusLine.Length - 1 ));

    public static readonly string Name = "TStatusLine";

    protected TStatusItem Items { get; set; }
    protected TStatusDef Defs { get; set; }

    // Konstruktor TStatusLine(const TRect& bounds, TStatusDef& aDefs)
    public TStatusLine(TRect bounds, TStatusDef aDefs)
        : base(bounds)
    {
        //Bounds = bounds;
        Defs = aDefs;

        options |= Views.ofPreProcess;
        eventMask |= Events.evBroadcast;
        growMode = Views.gfGrowLoY | Views.gfGrowHiX | Views.gfGrowHiY;
        FindItems();        
    }

    ~TStatusLine()
    {
        // Úklid, pokud je třeba
    }

    public override void Draw()
    {
        DrawSelect(null);
    }

    public override TPalette GetPalette()
    {
        // TODO: check
        //static TPalette palette(cpStatusLine, sizeof(cpStatusLine) - 1 );
        //palette = new TPalette(cpStatusLine, (ushort)(cpStatusLine.Length - 1 ));
        return palette;
    }

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);

        switch (@event.What)
        {
            case Events.evMouseDown:
                throw new NotImplementedException("TStatusLine.GetPalette() není implementován.");
                //break;

            case Events.evKeyDown:
                for (TStatusItem t = Items; t != null; t = t.Next)
                {
                    if (@event.keyDown.keyCode == t.KeyCode &&
                        CommandEnabled(t.Command))
                    {
                        @event.What = Events.evCommand;
                        @event.message.command = t.Command;
                        @event.message.infoPtr = null;
                        return;
                    }
                }
                break;
            
            case Events.evBroadcast:
                if (@event.message.command == Views.cmCommandSetChanged)
                    DrawView();
                break;
        }
    }

    // Vrací nápovědu podle daného help kontextu
    public virtual string Hint(ushort aHelpCtx)
    {
        return "";
    }

    public void Update()
    {
        //TView p = TopView();
        //ushort h = (p != null) ? p.GetHelpCtx() : Views.hcNoContext;
        //if (helpCtx != h)
        //{
        //    helpCtx = h;
        //    FindItems();
        //    DrawView();
        //}
        //throw new NotImplementedException("TStatusLine.Update() není implementován.");
    }

    // Soukromé pomocné metody – stuby
    private void DrawSelect(TStatusItem selected) 
    {
        TDrawBuffer b = new TDrawBuffer();
        /*TAttrPair*/
        ushort color;

        ushort cNormal = GetColor(0x0301);
        ushort cSelect = GetColor(0x0604);
        ushort cNormDisabled = GetColor(0x0202);
        ushort cSelDisabled = GetColor(0x0505);
        b.moveChar(0, ' ', cNormal, size.x);
        TStatusItem T = Items;
        int i = 0;

        while (T != null)
        {
            if (T.Text != null)
            {
                var l = T.Text.Length;
                if (i + l < size.x)
                {
                    if (CommandEnabled(T.Command))
                        if (T == selected)
                            color = cSelect;
                        else
                            color = cNormal;
                    else
                        if (T == selected)
                        color = cSelDisabled;
                    else
                        color = cNormDisabled;

                    b.moveChar(i, ' ', color, 1);
                    b.moveCStr((ushort)(i + 1), T.Text, color);
                    b.moveChar(i + l + 1, ' ', color, 1);
                }
                i += l + 2;
            }
            T = T.Next;
        }

        if (i < size.x - 2)
        {
            //TStringView hintText = hint(helpCtx);
            string hintText = Hint(helpCtx);
            //if (hintText.size())
            //{
            //    b.moveStr(i, hintSeparator, cNormal);
            //    i += 2;
            //    b.moveStr(i, hintText, cNormal, size.x - i);
            //}
            if (hintText.Length > 0)
            {
                throw new NotImplementedException("TStatusLine.DrawSelect() není implementován.");
                //b.moveStr(i, hintSeparator, cNormal);
                i += 2;
                //b.moveStr(i, hintText, cNormal, size.x - i);
            }            
        }
        WriteLine(0, 0, size.x, 1, b);        
    }

    private void FindItems() 
    {
        TStatusDef p = Defs;
        while (p != null && (helpCtx < p.Min || helpCtx > p.Max))
        {
            p = p.Next;
        }
        Items = p == null ? null : p.Items;
    }
    private TStatusItem ItemMouseIsIn(TPoint p) { throw new NotImplementedException("TStatusLine.ItemMouseIsIn() není implementován."); }
    private void DisposeItems(TStatusItem item) { throw new NotImplementedException("TStatusLine.DisposeItems() není implementován."); }
    private static readonly string hintSeparator = " - "; // Stub

    protected TStatusLine(object streamableInit) : base(streamableInit) { throw new NotImplementedException("TStatusLine(streamableInit) není implementován."); }
    public override void Write(Opstream os) { throw new NotImplementedException("TStatusLine.Write() není implementován."); }
    public override object Read(Ipstream isStream) { throw new NotImplementedException("TStatusLine.Read() není implementován."); }
    public static TStreamable Build() { throw new NotImplementedException("TStatusLine.Build() není implementován."); }

    public override string ToString() { return Name; }
}
