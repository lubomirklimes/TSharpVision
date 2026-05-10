// TSharpVision.Samples.TVIntl
//
// Internationalization sample:
// - Loc(...) fallback strings
// - .tvr string resources
// - runtime English/Czech/Pseudo switching
// - MissingKey diagnostics
// - editor open with explicit file encoding
using System.Text;
using TSharpVision;
using TSharpVision.Config;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Text;
using static TSharpVision.TInternationalizationExtensions;

namespace TSharpVision.Samples.TVIntl;

internal static class Program
{
    private static int Main(string[] args)
    {
        StreamableRegistration.RegisterAll();

        var config = TSharpVisionConfigurationLoader.Load();
        ScreenDriverFactory.ConfiguredDriverName = config.DriverName;
        ScreenDriverFactory.ConfiguredSdlFontName = config.SdlFontName;

        var app = new TVIntlApp();
        return AppLifecycleGuard.Run(app);
    }
}

internal static class TVIntlCmd
{
    public const ushort OpenText = 700;
    public const ushort OpenTextEncoding = 701;
    public const ushort LanguageEnglish = 702;
    public const ushort LanguageCzech = 703;
    public const ushort LanguagePseudo = 704;
    public const ushort MessageBox = 705;
    public const ushort InputBox = 706;
    public const ushort FileDialog = 707;
    public const ushort EncodingSample = 708;
    public const ushort MissingKey = 709;
    public const ushort About = 710;
}

internal static class TVIntlHelpCtx
{
    public const ushort NoContext = 0;
    public const ushort Index = 1;
    public const ushort MainMenu = 2;
    public const ushort OpenFileWindow = 3;
    public const ushort Language = 4;
    public const ushort Resources = 5;
    public const ushort Encoding = 6;
    public const ushort MissingKey = 7;
}

internal sealed class TVIntlApp : TApplication
{
    private readonly List<string> _missingKeys = new();
    private readonly EventHandler<MissingLocalizationKeyEventArgs> _missingKeyHandler;
    private string _language = "en";
    private THelpFile? _helpFile;
    private Fpstream? _helpStream;
    private string? _helpLanguage;
    private int _windowNumber;

    public TVIntlApp()
    {
        _missingKeyHandler = (_, args) =>
            _missingKeys.Add($"{args.Key} -> {args.Fallback}");
        TSharpVisionIntl.MissingKey += _missingKeyHandler;
        THelpFile.RegisterStreamableTypes();
        SetLanguage("en", showMessage: false);
    }

    public override TMenuBar InitMenuBar(TRect r)
    {
        r.b.y = r.a.y + 1;
        var menu = new TMenuBar(r,
            new TSubMenu(Loc("TVIntl_Menu_File", "~F~ile"), Keys.kbAltF) +
                new TMenuItem(Loc("TVIntl_Menu_OpenText", "~O~pen file..."), TVIntlCmd.OpenText, Keys.kbF3, TVIntlHelpCtx.MainMenu, "F3") +
                new TMenuItem(Loc("TVIntl_Menu_OpenTextEncoding", "Open file with ~e~ncoding..."), TVIntlCmd.OpenTextEncoding, Keys.kbNoKey, TVIntlHelpCtx.MainMenu) +
                TMenuItem.NewLine() +
                new TMenuItem(Loc("TVIntl_Menu_Exit", "E~x~it"), Views.cmQuit, Keys.kbAltX, Views.hcNoContext, "Alt+X") +
            new TSubMenu(Loc("TVIntl_Menu_Language", "~L~anguage"), Keys.kbAltL) +
                new TMenuItem(Loc("TVIntl_Menu_LangEnglish", "~E~nglish"), TVIntlCmd.LanguageEnglish, Keys.kbNoKey, TVIntlHelpCtx.Language) +
                new TMenuItem(Loc("TVIntl_Menu_LangCzech", "~C~zech"), TVIntlCmd.LanguageCzech, Keys.kbNoKey, TVIntlHelpCtx.Language) +
                new TMenuItem(Loc("TVIntl_Menu_LangPseudo", "~P~seudo / Debug"), TVIntlCmd.LanguagePseudo, Keys.kbNoKey, TVIntlHelpCtx.Language) +
            new TSubMenu(Loc("TVIntl_Menu_Demos", "~D~emos"), Keys.kbAltD) +
                new TMenuItem(Loc("TVIntl_Menu_MessageBox", "~M~essage box"), TVIntlCmd.MessageBox, Keys.kbF2, Views.hcNoContext, "F2") +
                new TMenuItem(Loc("TVIntl_Menu_InputBox", "~I~nput box"), TVIntlCmd.InputBox, Keys.kbNoKey) +
                new TMenuItem(Loc("TVIntl_Menu_FileDialog", "~F~ile dialog"), TVIntlCmd.FileDialog, Keys.kbNoKey) +
                new TMenuItem(Loc("TVIntl_Menu_EncodingSample", "~E~ncoding sample"), TVIntlCmd.EncodingSample, Keys.kbNoKey, TVIntlHelpCtx.Encoding) +
                new TMenuItem(Loc("TVIntl_Menu_MissingKey", "~M~issing key"), TVIntlCmd.MissingKey, Keys.kbNoKey, TVIntlHelpCtx.MissingKey) +
            new TSubMenu(Loc("TVIntl_Menu_Help", "~H~elp"), Keys.kbAltH) +
                new TMenuItem(Loc("TVIntl_Menu_HelpIndex", "~I~ndex"), Views.cmHelpIndex, Keys.kbNoKey, TVIntlHelpCtx.Index) +
                TMenuItem.NewLine() +
                new TMenuItem(Loc("TVIntl_Menu_About", "~A~bout"), TVIntlCmd.About, Keys.kbNoKey));
        menu.helpCtx = TVIntlHelpCtx.MainMenu;
        return menu;
    }

    public override TStatusLine InitStatusLine(TRect r)
    {
        r.a.y = r.b.y - 1;
        return new TStatusLine(r,
            new TStatusDef(0, 0xFFFF) +
            new TStatusItem(Loc("TVIntl_Status_Help", "~F1~ Help"), Keys.kbF1, Views.cmHelp) +
            new TStatusItem(Loc("TVIntl_Status_Message", "~F2~ Message"), Keys.kbF2, TVIntlCmd.MessageBox) +
            new TStatusItem(Loc("TVIntl_Status_Open", "~F3~ Open"), Keys.kbF3, TVIntlCmd.OpenText) +
            new TStatusItem(Loc("TVIntl_Status_Exit", "~Alt+X~ Exit"), Keys.kbAltX, Views.cmQuit));
    }

    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);
        if (ev.What != Events.evCommand) return;

        switch (ev.message.command)
        {
            case TVIntlCmd.OpenText:
                OpenText(EditorTextEncoding.Auto);
                ClearEvent(ref ev);
                break;
            case TVIntlCmd.OpenTextEncoding:
                OpenTextWithEncodingSelector();
                ClearEvent(ref ev);
                break;
            case TVIntlCmd.LanguageEnglish:
                SetLanguage("en");
                ClearEvent(ref ev);
                break;
            case TVIntlCmd.LanguageCzech:
                SetLanguage("cs");
                ClearEvent(ref ev);
                break;
            case TVIntlCmd.LanguagePseudo:
                SetPseudoLanguage();
                ClearEvent(ref ev);
                break;
            case TVIntlCmd.MessageBox:
                ShowMessageDemo();
                ClearEvent(ref ev);
                break;
            case TVIntlCmd.InputBox:
                ShowInputDemo();
                ClearEvent(ref ev);
                break;
            case TVIntlCmd.FileDialog:
                ShowFileDialogDemo();
                ClearEvent(ref ev);
                break;
            case TVIntlCmd.EncodingSample:
                ShowEncodingSample();
                ClearEvent(ref ev);
                break;
            case TVIntlCmd.MissingKey:
                ShowMissingKeyDemo();
                ClearEvent(ref ev);
                break;
            case Views.cmHelpIndex:
                OpenHelpIndex();
                ClearEvent(ref ev);
                break;
            case TVIntlCmd.About:
                ShowAbout();
                ClearEvent(ref ev);
                break;
        }
    }

    public override THelpFile GetHelpFile()
    {
        EnsureHelpFile();
        return _helpFile!;
    }

    public override void ShutDown()
    {
        TSharpVisionIntl.MissingKey -= _missingKeyHandler;
        CloseHelpFile();
        base.ShutDown();
    }

    private void SetLanguage(string language, bool showMessage = true)
    {
        _language = language;
        string basePath = Path.Combine(AppContext.BaseDirectory, "app.tvr");
        string resourcePath = LocalizedResourceResolver.Resolve(basePath, ".tvr", language);
        var resourceProvider = TResourceStringProvider.TryLoad(resourcePath);
        TSharpVisionIntl.Current = resourceProvider == null
            ? new DefaultEnglishStringProvider()
            : new TSharpVisionStringProviderChain(resourceProvider, new DefaultEnglishStringProvider());

        CloseHelpFile();
        RefreshChrome();
        if (showMessage)
            ShowInfo(Loc("TVIntl_Dlg_Language_Title", "Language"),
                string.Format(Loc("TVIntl_Dlg_Language_Text", "Language switched to {0}."), language));
    }

    private void SetPseudoLanguage()
    {
        _language = "pseudo";
        TSharpVisionIntl.Current = new PseudoStringProvider();
        CloseHelpFile();
        RefreshChrome();
        ShowInfo(Loc("TVIntl_Dlg_Language_Title", "Language"),
            Loc("TVIntl_Dlg_Language_Pseudo_Text", "Pseudo/debug provider is active."));
    }

    private void RefreshChrome()
    {
        var desktop = DeskTop;
        if (desktop != null && desktop.owner == this)
            Remove(desktop);

        if (MenuBar != null && MenuBar.owner == this)
            Remove(MenuBar);

        if (StatusLine != null && StatusLine.owner == this)
            Remove(StatusLine);

        StatusLine = InitStatusLine(GetExtent());
        MenuBar = InitMenuBar(GetExtent());

        // Keep the exact TProgram construction order: status line, menu bar,
        // desktop. TGroup.Insert maintains z-order from that sequence, and
        // changing it makes mouse/key routing look like menu/status mix-up.
        if (StatusLine != null) Insert(StatusLine);
        if (MenuBar != null) Insert(MenuBar);
        if (desktop != null)
        {
            DeskTop = desktop;
            Insert(DeskTop);
            SetCurrent(DeskTop, selectMode.normalSelect);
        }

        Redraw();
    }

    private void ShowMessageDemo()
        => ShowInfo(
            Loc("TVIntl_Dlg_Message_Title", "Localized message"),
            Loc("TVIntl_Dlg_Message_Text", "This message was resolved through Loc(...)."));

    private void ShowInputDemo()
    {
        var result = MsgBox.InputBox(
            DeskTop,
            Loc("TVIntl_Dlg_Input_Title", "Input"),
            Loc("TVIntl_Dlg_Input_Label", "~N~ame:"),
            Loc("TVIntl_Dlg_Input_Default", "World"),
            40);

        if (result.code != Views.cmCancel)
        {
            string name = string.IsNullOrWhiteSpace(result.value)
                ? Loc("TVIntl_Dlg_Input_Default", "World")
                : result.value;
            ShowInfo(
                Loc("TVIntl_Dlg_Greeting_Title", "Greeting"),
                string.Format(Loc("TVIntl_Dlg_Greeting_Text", "Hello, {0}!"), name));
        }
    }

    private void ShowFileDialogDemo()
    {
        var dlg = new TFileDialog(
            "*.*",
            Loc("TVIntl_Dlg_File_Title", "Select a file"),
            Loc("File_Label_Name", "~N~ame"),
            FileDialogOptions.fdOpenButton,
            0);

        if (ValidView(dlg) == null) return;
        if (DeskTop.ExecView(dlg) == Views.cmOK)
        {
            dlg.GetData(out string path);
            ShowInfo(
                Loc("TVIntl_Dlg_File_Selected_Title", "Selected file"),
                string.Format(Loc("TVIntl_Dlg_File_Selected_Text", "Selected path:\n{0}"), path));
        }
    }

    private void OpenTextWithEncodingSelector()
    {
        var dlg = new TFileDialog(
            "*.*",
            Loc("TVIntl_Dlg_OpenEncoding_Title", "Open text with encoding"),
            Loc("File_Label_Name", "~N~ame"),
            (ushort)(FileDialogOptions.fdOpenButton | FileDialogOptions.fdEncodingSelector),
            0);

        if (ValidView(dlg) == null) return;
        if (DeskTop.ExecView(dlg) == Views.cmOK)
        {
            dlg.GetData(out string path);
            OpenText(dlg.SelectedEncoding, path);
        }
    }

    private void OpenText(EditorTextEncoding encoding, string? path = null)
    {
        path ??= AskForTextPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        var bounds = DeskTop.GetExtent();
        bounds.Grow(-2, -1);
        bounds.a.x += _windowNumber % 4;
        bounds.a.y += _windowNumber % 3;
        bounds.b.x -= _windowNumber % 4;
        bounds.b.y -= _windowNumber % 3;
        _windowNumber++;

        TEditWindow window;
        try
        {
            window = new TEditWindow(
                bounds,
                path,
                _windowNumber,
                new TFileEditorOpenOptions { Encoding = encoding });
        }
        catch (DecoderFallbackException)
        {
            ShowError(Loc("File_Err_EncodingDecode", "The file could not be decoded using the selected encoding."));
            return;
        }

        var valid = ValidView(window);
        if (valid != null)
        {
            valid.helpCtx = TVIntlHelpCtx.OpenFileWindow;
            DeskTop.Insert(valid);
        }
    }

    private string? AskForTextPath()
    {
        var dlg = new TFileDialog(
            "*.*",
            Loc("File_Title_Open", "Open File"),
            Loc("File_Label_Name", "~N~ame"),
            FileDialogOptions.fdOpenButton,
            0);

        if (ValidView(dlg) == null) return null;
        if (DeskTop.ExecView(dlg) != Views.cmOK) return null;
        dlg.GetData(out string path);
        return path;
    }

    private void ShowEncodingSample()
    {
        string czech = Loc("TVIntl_Encoding_Text", "Příliš žluťoučký kůň");
        byte[] cp852 = LegacyTextEncodings.Cp852.Encode(czech);
        byte[] kamenicky = LegacyTextEncodings.Kamenicky.Encode(czech);

        string message =
            Loc("TVIntl_Encoding_Title", "Encoding sample") + "\n\n" +
            Loc("TVIntl_Encoding_Auto", "Auto") + ": UTF-8 -> Latin-1 fallback\n" +
            Loc("TVIntl_Encoding_UTF8", "UTF-8") + ": " + Encoding.UTF8.GetByteCount(czech) + " bytes\n" +
            Loc("TVIntl_Encoding_CP852", "CP852") + ": " + ToHex(cp852) + "\n" +
            Loc("TVIntl_Encoding_Kamenicky", "Kamenicky") + ": " + ToHex(kamenicky);

        ShowInfo(Loc("TVIntl_Encoding_Title", "Encoding sample"), message);
    }

    private void OpenHelpIndex()
    {
        var hf = GetHelpFile();
        if (hf == null)
        {
            ShowError(Loc("TVIntl_Help_NotFound", "Help file was not found."));
            return;
        }

        var win = new THelpWindow(hf, TVIntlHelpCtx.Index);
        if (ValidView(win) != null)
            ExecuteHelp(win);
    }

    private void EnsureHelpFile()
    {
        string helpLanguage = _language == "cs" ? "cs" : "en";
        if (_helpFile != null && _helpLanguage == helpLanguage)
            return;

        CloseHelpFile();

        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Help",
            "tvintl_" + helpLanguage + ".hlp");
        if (!File.Exists(path))
            return;

        _helpStream = new Fpstream(path);
        _helpFile = new THelpFile(_helpStream);
        _helpLanguage = helpLanguage;
    }

    private void CloseHelpFile()
    {
        _helpFile = null;
        _helpLanguage = null;
        _helpStream?.Close();
        _helpStream = null;
    }

    private void ShowMissingKeyDemo()
    {
        int before = _missingKeys.Count;
        var saved = TSharpVisionIntl.Current;
        TSharpVisionIntl.Current = new DefaultEnglishStringProvider();
        string fallback;
        try
        {
            fallback = TSharpVisionIntl.Get("TVIntl_Missing_Demo_Key", "This is fallback text.");
        }
        finally
        {
            TSharpVisionIntl.Current = saved;
        }

        string recent = RecentMissingKeys(before);
        ShowInfo(
            Loc("TVIntl_Dlg_MissingKey_Title", "Missing key"),
            Loc("TVIntl_Dlg_MissingKey_Text", "A missing localization key was requested and fallback text was used.") +
            "\n\n" + fallback + "\n\n" + recent);
    }

    private string RecentMissingKeys(int startIndex)
    {
        if (_missingKeys.Count == 0)
            return Loc("TVIntl_Dlg_MissingKey_None", "No missing keys recorded.");

        int start = Math.Max(0, Math.Min(startIndex, _missingKeys.Count - 5));
        return Loc("TVIntl_Dlg_MissingKey_Recent", "Recent missing keys:") +
            "\n" + string.Join("\n", _missingKeys.Skip(start).TakeLast(5));
    }

    private void ShowAbout()
    {
        var aboutBox = new TDialog(
            new TRect(0, 0, 39, 13),
            Loc("TVIntl_Dlg_About_Title", "About"));

        aboutBox.Insert(new TStaticText(
            new TRect(9, 2, 30, 9),
            "\u0003TSharpVision Intl Demo\n\u0003\n" +
            "\u0003.NET Version\n\u0003\n" +
            "\u0003Localization Demo\n\u0003\n" +
            "\u0003TSharpVision"));

        aboutBox.Insert(new TButton(
            new TRect(14, 10, 25, 12),
            Loc("Btn_OK", " OK"),
            Views.cmOK,
            ButtonConstants.bfDefault));

        aboutBox.options |= Views.ofCentered;

        var valid = ValidView(aboutBox);
        if (valid != null)
            DeskTop.ExecView(valid);
    }

    private void ShowInfo(string title, string text)
    {
        var dlg = new TDialog(CenteredRect(56, 12), title);
        dlg.Insert(new TStaticText(new TRect(2, 2, 54, 8), text));
        dlg.Insert(new TButton(new TRect(23, 9, 33, 11), Loc("Btn_OK", "~O~K"), Views.cmOK, ButtonConstants.bfDefault));
        var valid = ValidView(dlg);
        if (valid != null)
            DeskTop.ExecView(valid);
    }

    private void ShowError(string text)
        => MsgBox.MessageBox(DeskTop, text, MsgBox.mfError | MsgBox.mfOKButton);

    private TRect CenteredRect(int width, int height)
    {
        var area = DeskTop?.size ?? size;
        var rect = new TRect(0, 0, width, height);
        rect.Move(Math.Max(0, (area.x - width) / 2), Math.Max(0, (area.y - height) / 2));
        return rect;
    }

    private static string ToHex(byte[] bytes)
        => string.Join(" ", bytes.Select(b => b.ToString("X2")));
}

internal sealed class PseudoStringProvider : ITSharpVisionStringLookupProvider
{
    public string Get(string key, string fallback)
        => TryGet(key, out string value) ? value : fallback;

    public bool TryGet(string key, out string value)
    {
        value = "[[" + key + ": " + key.Replace('_', ' ') + "]]";
        return true;
    }
}
