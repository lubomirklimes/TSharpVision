using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

internal sealed class MessageDemoDialog : TDialog
{
    internal const ushort ShowMessage = 520;
    internal TRadioButtons Category { get; }
    internal TRadioButtons Buttons { get; }
    internal TParamText Result { get; }

    internal MessageDemoDialog(TRect bounds) : base(bounds, "Message boxes")
    {
        Insert(new TStaticText(new TRect(2, 1, 61, 3),
            "Choose a title and button set, then Show.\nThe response code appears here when that box closes."));
        Category = new TRadioButtons(new TRect(3, 4, 29, 8),
            new TSItem("~I~nformation", new TSItem("~W~arning",
            new TSItem("~E~rror", new TSItem("Con~f~irmation", null)))));
        Buttons = new TRadioButtons(new TRect(33, 4, 61, 7),
            new TSItem("~O~K", new TSItem("OK / Ca~n~cel",
            new TSItem("~Y~es / No / Cancel", null))));
        Insert(Category);
        Insert(Buttons);
        Result = new TParamText(new TRect(3, 9, 61, 10), "No message shown yet.", 0);
        Insert(Result);
        Insert(new TButton(new TRect(17, 11, 29, 13), "~S~how", ShowMessage, ButtonConstants.bfDefault));
        Insert(new TButton(new TRect(33, 11, 45, 13), "~C~lose", Views.cmCancel, ButtonConstants.bfNormal));
        Category.Select();
    }

    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);
        if (ev.What != Events.evCommand || ev.message.command != ShowMessage) return;
        ClearEvent(ref ev);
        ushort category = Category.value switch
        {
            1 => MsgBox.mfWarning, 2 => MsgBox.mfError, 3 => MsgBox.mfConfirmation,
            _ => MsgBox.mfInformation
        };
        ushort buttons = Buttons.value switch
        {
            1 => MsgBox.mfOKCancel, 2 => MsgBox.mfYesNoCancel, _ => MsgBox.mfOKButton
        };
        var box = MsgBox.BuildMessageBox(TVDemoApp.ResultBounds(owner, "This is the selected message style.\nChoose a response or press Esc."),
            "This is the selected message style.\nChoose a response or press Esc.",
            (ushort)(category | buttons));
        ushort code;
        try { code = owner!.ExecView(box); }
        finally { box.ShutDown(); }
        Result.SetText("Response: {0} ({1})", TVDemoApp.DemoResultName(code), code);
    }
}

public partial class TVDemoApp
{
    internal static TRect ResultBounds(TGroup? host, string? text = null)
    {
        int width = Math.Min(74, host?.size.x ?? 80);
        // Allow one separator row between the actual wrapped text and actions.
        int rows = text == null ? 8 : WrappedRows(text, Math.Max(1, width - 5));
        int height = Math.Min(Math.Max(7, rows + 6), host?.size.y ?? 23);
        int left = Math.Max(0, ((host?.size.x ?? 80) - width) / 2);
        int top = Math.Max(0, ((host?.size.y ?? 23) - height) / 2);
        return new TRect(left, top, left + width, top + height);
    }

    private static int WrappedRows(string text, int width)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        int rows = 0, position = 0;
        // Match TStaticText's word boundaries, including long unbroken paths.
        while (position < text.Length)
        {
            if (text[position] == '\x03') position++;
            int start = position, word;
            do
            {
                word = position;
                while (position < text.Length && text[position] == ' ') position++;
                while (position < text.Length && text[position] != ' ' && text[position] != '\n') position++;
            } while (position < text.Length && position < start + width && text[position] != '\n');
            if (position > start + width) position = word > start ? word : start + width;
            while (position < text.Length && text[position] == ' ') position++;
            if (position < text.Length && text[position] == '\n') position++;
            rows++;
        }
        return rows;
    }

    internal static void ShowDemoResult(TGroup? host, string text, ushort category = MsgBox.mfInformation)
    {
        if (host == null) return;
        var dialog = MsgBox.BuildMessageBox(ResultBounds(host, text), text, (ushort)(category | MsgBox.mfOKButton));
        try { host.ExecView(dialog); }
        finally { dialog.ShutDown(); }
    }

    internal static string DemoResultName(ushort code) => code switch
    {
        Views.cmOK => "cmOK", Views.cmCancel => "cmCancel", Views.cmYes => "cmYes",
        Views.cmNo => "cmNo", Views.cmFileOpen => "cmFileOpen", _ => $"command {code}"
    };

    private void OpenInputBoxDemo()
    {
        if (DeskTop == null) return;
        const string original = "World";
        var result = MsgBox.InputBox(DeskTop, "Input box (Esc preserves World)", "~N~ame:", original, 40);
        ShowDemoResult(DeskTop,
            $"Input box: {DemoResultName(result.code)}\nOriginal: {original}\n" +
            $"Returned: {result.value}\n" +
            (result.code == Views.cmCancel ? "Cancelled: original value preserved." : "Accepted value."));
    }

    private void OpenMessageBoxDemo()
    {
        if (DeskTop == null) return;
        var dialog = new MessageDemoDialog(DemoBounds(64, 14));
        try { if (ValidView(dialog) != null) DeskTop.ExecView(dialog); }
        finally { dialog.ShutDown(); }
    }
}
