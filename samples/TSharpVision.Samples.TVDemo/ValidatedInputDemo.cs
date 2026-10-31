using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

// Feedback belongs to the sample; the production validators remain unchanged.
internal sealed class DemoValidatedLine : TInputLine
{
    private readonly string _description;
    private readonly TParamText _status;

    internal DemoValidatedLine(TRect bounds, TValidator validator, string description, TParamText status)
        : base(bounds, 32)
    {
        Validator = validator;
        _description = description;
        _status = status;
        options |= Views.ofValidate;
    }

    public override bool InsertChar(char value)
    {
        // TInputLine consults Validator at validation time, not per character.
        // This sample explicitly illustrates its public partial-input contract.
        string previous = Data;
        int cursor = CurPos, first = FirstPos, start = SelStart, end = SelEnd;
        bool inserted = base.InsertChar(value);
        if (inserted && Validator!.IsValidInput(Data, suppressFill: true)) return true;
        Data = previous;
        CurPos = cursor;
        FirstPos = first;
        SelStart = start;
        SelEnd = end;
        DrawView();
        _status.SetText("Rejected character. {0}", _description);
        return false;
    }

    public override bool Valid(ushort command)
    {
        bool valid = base.Valid(command);
        if (!valid) _status.SetText("Invalid value. {0}\nCorrect it, or Esc / Cancel to discard.", _description);
        return valid;
    }
}

internal sealed class ValidatedInputDemoDialog : TDialog
{
    internal DemoValidatedLine Letters { get; }
    internal DemoValidatedLine Range { get; }
    internal DemoValidatedLine Picture { get; }
    internal DemoValidatedLine Lookup { get; }
    internal TParamText Status { get; }

    internal ValidatedInputDemoDialog(TRect bounds) : base(bounds, "Validated input")
    {
        Insert(new TStaticText(new TRect(2, 1, 67, 3),
            "Invalid values block OK and leaving a field (Tab / mnemonic).\nEsc or Cancel discards all edits; start with the valid examples."));
        Status = new TParamText(new TRect(2, 14, 67, 16), "Try an invalid value, then Tab or OK.", 0);
        Letters = new DemoValidatedLine(new TRect(18, 4, 38, 5),
            new TFilterValidator("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ "),
            "Letters: A-Z / a-z / spaces only.", Status);
        Letters.SetData("Ada");
        Insert(Letters);
        Insert(new TLabel(new TRect(2, 4, 17, 5), "~L~etters:", Letters));
        Insert(new TStaticText(new TRect(40, 4, 67, 5), "Character filter"));

        var group = new TGroupBox(new TRect(2, 6, 67, 9), "Nested range validation");
        Range = new DemoValidatedLine(new TRect(16, 1, 36, 2),
            new TRangeValidator(1, 100), "Range: enter an integer from 1 to 100.", Status);
        Range.SetData("42");
        group.Insert(Range);
        group.Insert(new TLabel(new TRect(1, 1, 15, 2), "~R~ange:", Range));
        group.Insert(new TStaticText(new TRect(38, 1, 62, 2), "1 .. 100"));
        Insert(group);

        Picture = new DemoValidatedLine(new TRect(18, 10, 38, 11),
            new TPXPictureValidator("###-##"), "Picture: three digits, dash, two digits (123-45).", Status);
        Picture.SetData("123-45");
        Insert(Picture);
        Insert(new TLabel(new TRect(2, 10, 17, 11), "~P~icture:", Picture));
        Insert(new TStaticText(new TRect(40, 10, 67, 11), "###-## (123-45)"));

        var allowed = new TStringCollection();
        foreach (string value in new[] { "Red", "Green", "Blue" }) allowed.Insert(value);
        Lookup = new DemoValidatedLine(new TRect(18, 12, 38, 13),
            new TStringLookupValidator(allowed), "Lookup: Red, Green or Blue (case-sensitive).", Status);
        Lookup.SetData("Green");
        Insert(Lookup);
        Insert(new TLabel(new TRect(2, 12, 17, 13), "Loo~k~up:", Lookup));
        Insert(new TStaticText(new TRect(40, 12, 67, 13), "Red / Green / Blue"));
        Insert(Status);
        Insert(new TButton(new TRect(21, 17, 33, 19), "~O~K", Views.cmOK, ButtonConstants.bfDefault));
        Insert(new TButton(new TRect(37, 17, 49, 19), "~C~ancel", Views.cmCancel, ButtonConstants.bfNormal));
        Letters.Select();
    }
}

public partial class TVDemoApp
{
    private void OpenValidatedInputDemo()
    {
        if (DeskTop == null) return;
        var dialog = new ValidatedInputDemoDialog(DemoBounds(70, 21));
        string summary;
        try
        {
            if (ValidView(dialog) == null) return;
            ushort result = DeskTop.ExecView(dialog);
            summary = result == Views.cmOK
                ? $"Validated input accepted.\nLetters: {dialog.Letters.Data}\nRange: {dialog.Range.Data}\n" +
                    $"Picture: {dialog.Picture.Data}\nLookup: {dialog.Lookup.Data}"
                : "Validated input cancelled. Original values retained.";
        }
        finally { dialog.ShutDown(); }
        ShowDemoResult(DeskTop, summary);
    }
}
