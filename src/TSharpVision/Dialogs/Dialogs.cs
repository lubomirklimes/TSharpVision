using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>Shared, in-memory input histories identified by unsigned history IDs.</summary>
public static class THistoryList
{
    private static readonly Dictionary<ushort, List<string>> _store = new();
    /// <summary>Maximum number of entries retained per history ID.</summary>
    public const int HistorySize = 64;

    /// <summary>Moves a nonempty string to the front of the history, removing duplicates and discarding the oldest entry when full.</summary>
    public static void Add(ushort id, string str)
    {
        if (string.IsNullOrEmpty(str)) return;
        if (!_store.TryGetValue(id, out var list))
        {
            list = new List<string>();
            _store[id] = list;
        }
        list.RemoveAll(s => s == str);
        list.Insert(0, str);
        if (list.Count > HistorySize) list.RemoveAt(list.Count - 1);
    }

    /// <summary>Returns the number of entries for a history ID, or zero when it has no history.</summary>
    public static int Count(ushort id) =>
        _store.TryGetValue(id, out var list) ? list.Count : 0;

    /// <summary>Returns an entry by zero-based recency index, newest first; missing IDs or indices return an empty string.</summary>
    public static string Str(ushort id, int index)
    {
        if (!_store.TryGetValue(id, out var list)) return string.Empty;
        if (index < 0 || index >= list.Count) return string.Empty;
        return list[index];
    }

    /// <summary>Removes all entries for all history IDs.</summary>
    public static void Clear()
    {
        _store.Clear();
    }
}

/// <summary>Builds and runs standard message and text-input dialogs within a host group.</summary>
public static class MsgBox
{
    /// <summary>Selects the warning title for a message dialog.</summary>
    public const ushort mfWarning     = 0x0000;
    /// <summary>Selects the error title for a message dialog.</summary>
    public const ushort mfError       = 0x0001;
    /// <summary>Selects the information title for a message dialog.</summary>
    public const ushort mfInformation = 0x0002;
    /// <summary>Selects the confirmation title for a message dialog.</summary>
    public const ushort mfConfirmation= 0x0003;
    /// <summary>Includes a Yes button returning cmYes.</summary>
    public const ushort mfYesButton   = 0x0100;
    /// <summary>Includes a No button returning cmNo.</summary>
    public const ushort mfNoButton    = 0x0200;
    /// <summary>Includes an OK button returning cmOK.</summary>
    public const ushort mfOKButton    = 0x0400;
    /// <summary>Includes a Cancel button returning cmCancel.</summary>
    public const ushort mfCancelButton= 0x0800;
    /// <summary>Includes Yes, No, and Cancel responses.</summary>
    public const ushort mfYesNoCancel = mfYesButton | mfNoButton | mfCancelButton;
    /// <summary>Includes OK and Cancel responses.</summary>
    public const ushort mfOKCancel    = mfOKButton | mfCancelButton;

    private static readonly string[] ButtonNames =
        { "~Y~es", "~N~o", "~O~K", "Cancel" };
    private static readonly ushort[] Commands =
        { Views.cmYes, Views.cmNo, Views.cmOK, Views.cmCancel };
    private static readonly string[] Titles =
        { "Warning", "Error", "Information", "Confirm" };
    // localization key tables parallel to the arrays above.
    private static readonly string[] ButtonKeys =
        { "Btn_Yes", "Btn_No", "Btn_OK", "Btn_Cancel" };
    private static readonly string[] TitleKeys =
        { "MsgTitle_Warning", "MsgTitle_Error", "MsgTitle_Information", "MsgTitle_Confirm" };

    /// <summary>Creates a message dialog at owner-relative cell bounds with the requested title category and response buttons; does not execute it.</summary>
    public static TDialog BuildMessageBox(TRect r, string msg, ushort options)
    {
        var dialog = new TDialog(r, TSharpVisionIntl.Get(TitleKeys[options & 0x3], Titles[options & 0x3]));
        int height = r.b.y - r.a.y;
        dialog.Insert(new TStaticText(
            new TRect(3, 2, dialog.size.x - 2, height - 3), msg));

        var buttons = new List<TButton>();
        int totalW = -2;
        for (int i = 0; i < 4; i++)
        {
            if ((options & (0x0100 << i)) != 0)
            {
                var btn = new TButton(new TRect(0, 0, 10, 2),
                    TSharpVisionIntl.Get(ButtonKeys[i], ButtonNames[i]),
                    Commands[i], ButtonConstants.bfNormal);
                buttons.Add(btn);
                totalW += btn.size.x + 2;
            }
        }
        int x = (dialog.size.x - totalW) / 2;
        foreach (var btn in buttons)
        {
            dialog.Insert(btn);
            btn.MoveTo(x, dialog.size.y - 3);
            x += btn.size.x + 2;
        }
        dialog.SelectNext(false);
        return dialog;
    }

    /// <summary>Runs a message dialog at host-relative cell bounds and returns its response command, or zero for a null host.</summary>
    public static ushort MessageBoxRect(TGroup host, TRect r, string msg, ushort options)
    {
        var dialog = BuildMessageBox(r, msg, options);
        if (host == null) return 0;
        return host.ExecView(dialog);
    }

    /// <summary>Returns centered 40-by-9 cell dialog bounds using the host size, or an 80-by-25 reference area for a null host.</summary>
    public static TRect DefaultRect(TGroup host)
    {
        var r = new TRect(0, 0, 40, 9);
        var ds = host?.size ?? new TPoint { x = 80, y = 25 };
        r.Move((ds.x - r.b.x) / 2, (ds.y - r.b.y) / 2);
        return r;
    }

    /// <summary>Runs a centered message dialog and returns its response command, or zero for a null host.</summary>
    public static ushort MessageBox(TGroup host, string msg, ushort options)
        => MessageBoxRect(host, DefaultRect(host), msg, options);

    /// <summary>Creates a dialog with labeled input, initial text, and a character limit at owner-relative cell bounds; does not execute it.</summary>
    public static TDialog BuildInputBox(TRect bounds, string title,
                                        string aLabel, string s, int limit)
    {
        var dialog = new TDialog(bounds, title);
        int x = 4 + aLabel.Length;
        var r = new TRect(x, 2,
            Math.Min(x + limit + 2, dialog.size.x - 3), 3);
        var control = new TInputLine(r, limit);
        dialog.Insert(control);

        r = new TRect(2, 2, 3 + aLabel.Length, 3);
        dialog.Insert(new TLabel(r, aLabel, control));

        r = new TRect(dialog.size.x / 2 - 11, dialog.size.y - 3,
                      dialog.size.x / 2 - 1,  dialog.size.y - 1);
        dialog.Insert(new TButton(r, TSharpVisionIntl.Get("Btn_OK", "~O~K"), Views.cmOK,
            ButtonConstants.bfDefault));
        var r2 = new TRect(r.a.x + 12, r.a.y, r.b.x + 12, r.b.y);
        dialog.Insert(new TButton(r2, TSharpVisionIntl.Get("Btn_Cancel", "Cancel"), Views.cmCancel,
            ButtonConstants.bfNormal));

        dialog.SelectNext(false);
        if (s != null) control.SetData(s);
        return dialog;
    }

    /// <summary>Runs a text-input dialog at host-relative cell bounds and returns its command and text; cancellation preserves the original text.</summary>
    public static (ushort code, string value) InputBoxRect(TGroup host,
        TRect bounds, string title, string aLabel, string s, int limit)
    {
        var dialog = BuildInputBox(bounds, title, aLabel, s, limit);
        if (host == null) return (0, s);
        ushort code = host.ExecView(dialog);
        string result = s;
        if (code != Views.cmCancel)
            result = FindInputLineValue(dialog) ?? s;
        return (code, result);
    }

    /// <summary>Runs a centered text-input dialog and returns its command and text; cancellation preserves the original text.</summary>
    public static (ushort code, string value) InputBox(TGroup host,
        string title, string aLabel, string s, int limit)
    {
        int len = Math.Max(aLabel.Length + 8 + limit, title.Length + 11);
        len = Math.Min(len, 60);
        len = Math.Max(len, 24);
        var r = new TRect(0, 0, len, 7);
        var ds = host?.size ?? new TPoint { x = 80, y = 25 };
        r.Move((ds.x - r.b.x) / 2, (ds.y - r.b.y) / 2);
        return InputBoxRect(host, r, title, aLabel, s, limit);
    }

    private static string FindInputLineValue(TGroup group)
    {
        TView v = group.last;
        if (v == null) return null;
        TView p = v.Next;
        do
        {
            if (p is TInputLine il) return il.Data;
            p = p.Next;
        } while (p != null && p != v.Next);
        return null;
    }
}

/// <summary>Empty compatibility placeholder; use TStringCollection to store and stream strings.</summary>
public class TStringCollectionStub { }
