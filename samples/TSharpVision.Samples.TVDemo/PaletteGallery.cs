using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

/// <summary>Commands of the control palette gallery. <see cref="Unavailable"/> stays disabled.</summary>
internal static class PaletteGalleryCmd
{
    public const ushort Apply = 346, Modal = 347, Unavailable = 348, Close = 349;
}

/// <summary>
/// The same production controls, laid out identically in whichever parent they are inserted into, so the parents can
/// be compared cell by cell: a static text, a name field, a combo box, check boxes, radio buttons, a list with its
/// scrollbar, a progress bar, a status line and a bottom row of default, normal, disabled and close buttons.
/// </summary>
internal sealed class PaletteGallerySet
{
    public const int Width = 47, Height = 16;

    public PaletteGallerySet(TGroup host, bool label)
    {
        host.Insert(new TStaticText(new TRect(2, 1, 45, 2), "Static text on the parent surface"));

        Input = new TInputLine(new TRect(9, 2, 25, 3), 30);
        Input.SetData("Text");
        host.Insert(Input);
        // TLabel's roles (7-9) are a window's selected-text and reserved roles, so a plain TWindow gets a static text.
        host.Insert(label ? new TLabel(new TRect(2, 2, 8, 3), "~N~ame:", Input)
                          : new TStaticText(new TRect(2, 2, 8, 3), "Name:"));
        Combo = new TComboBox(new TRect(27, 2, 45, 3), new[] { "UTF-8", "UTF-16", "Windows-1250", "ISO-8859-2" });
        host.Insert(Combo);

        Checks = new TCheckBoxes(new TRect(2, 4, 22, 7),
            new TSItem("~B~old", new TSItem("~I~talic", new TSItem("~U~nderline", null))));
        Checks.value = 0b001u;
        host.Insert(Checks);
        Radio = new TRadioButtons(new TRect(24, 4, 45, 7),
            new TSItem("~L~eft", new TSItem("Cen~t~er", new TSItem("~R~ight", null))));
        Radio.value = 1u;
        host.Insert(Radio);

        var bar = new TScrollBar(new TRect(18, 8, 19, 12)) { growMode = Views.gfGrowHiY };
        host.Insert(bar);
        List = new TListBox(new TRect(2, 8, 18, 12), 1, bar) { growMode = Views.gfGrowHiY };
        var items = new TStringCollection();
        foreach (string item in new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta" }) items.Insert(item);
        List.NewList(items);
        List.FocusItem(1);
        host.Insert(List);

        Progress = new TProgressBar(new TRect(21, 8, 45, 9)) { ShowPercentage = true, Value = 40, growMode = Views.gfGrowHiX };
        host.Insert(Progress);
        Status = new TParamText(new TRect(21, 10, 45, 11), "Tab moves, Enter/Space press", 0) { growMode = Views.gfGrowHiX };
        host.Insert(Status);

        const byte bottom = Views.gfGrowLoY | Views.gfGrowHiY;
        Default = new TButton(new TRect(2, 13, 10, 15), "~O~K", Views.cmOK, ButtonConstants.bfDefault) { growMode = bottom };
        Normal = new TButton(new TRect(11, 13, 20, 15), "~A~pply", PaletteGalleryCmd.Apply, ButtonConstants.bfNormal) { growMode = bottom };
        Disabled = new TButton(new TRect(21, 13, 33, 15), "~D~isabled", PaletteGalleryCmd.Unavailable, ButtonConstants.bfNormal) { growMode = bottom };
        CloseButton = new TButton(new TRect(34, 13, 44, 15), "~C~lose", PaletteGalleryCmd.Close, ButtonConstants.bfNormal) { growMode = bottom };
        host.Insert(Default);
        host.Insert(Normal);
        host.Insert(Disabled);
        host.Insert(CloseButton);
    }

    public TInputLine Input { get; }
    public TComboBox Combo { get; }
    public TCheckBoxes Checks { get; }
    public TRadioButtons Radio { get; }
    public TListBox List { get; }
    public TProgressBar Progress { get; }
    public TParamText Status { get; }
    public TButton Default { get; }
    public TButton Normal { get; }
    public TButton Disabled { get; }
    public TButton CloseButton { get; }

    /// <summary>Last gallery action, as shown in the status line.</summary>
    public string LastAction { get; private set; } = string.Empty;

    /// <summary>Handles the gallery commands of <paramref name="window"/>; returns whether the event was consumed.</summary>
    public bool Handle(TWindow window, ref TEvent @event)
    {
        if (@event.What != Events.evCommand) return false;
        switch (@event.message.command)
        {
            case Views.cmOK: Report("OK pressed"); break;
            case PaletteGalleryCmd.Apply:
                Progress.Value = Progress.Value >= 100 ? 0 : Progress.Value + 20;
                Report("Apply pressed");
                break;
            case PaletteGalleryCmd.Close: window.Close(); break;
            default: return false;
        }
        window.ClearEvent(ref @event);
        return true;
    }

    public void Report(string action)
    {
        LastAction = action;
        Status.SetText(action);
    }
}

/// <summary>A: the standard gray TDialog, modeless. Esc closes it like its Close button.</summary>
internal sealed class GrayGalleryDialog : TDialog
{
    public GrayGalleryDialog(TRect bounds) : base(bounds, "Gray TDialog")
    {
        number = 1;
        Set = new PaletteGallerySet(this, label: true);
        SelectNext(false);
    }

    public PaletteGallerySet Set { get; }

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        // Set is still null while the constructor inserts views that broadcast (the list's scrollbar).
        if (Set is null || Set.Handle(this, ref @event)) return;
        if (@event.What == Events.evCommand && @event.message.command == Views.cmCancel && (state & Views.sfModal) == 0)
        {
            ClearEvent(ref @event);
            Close();
        }
    }
}

/// <summary>B: the standard blue TWindow (resizable, movable, zoomable) holding the same controls.</summary>
internal sealed class BlueGalleryWindow : TWindow
{
    public BlueGalleryWindow(TRect bounds) : base(bounds, "Blue TWindow", 2)
    {
        Set = new PaletteGallerySet(this, label: false);
        ModalButton = new TButton(new TRect(21, 11, 33, 13), "~M~odal...", PaletteGalleryCmd.Modal, ButtonConstants.bfNormal)
        {
            growMode = Views.gfGrowLoY | Views.gfGrowHiY,
        };
        Insert(ModalButton);
        SelectNext(false);
    }

    public PaletteGallerySet Set { get; }
    public TButton ModalButton { get; }

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        // Set is still null while the constructor inserts views that broadcast (the list's scrollbar).
        if (Set is null || Set.Handle(this, ref @event)) return;
        if (@event.What == Events.evCommand && @event.message.command == PaletteGalleryCmd.Modal && owner != null)
        {
            ClearEvent(ref @event);
            var dialog = new BlueDocumentDialog();
            ushort result = owner.ExecView(dialog);
            Set.Report(result == Views.cmOK ? $"Modal: OK \"{dialog.Find.Data}\"" : "Modal: cancelled");
            dialog.ShutDown();
        }
    }
}

/// <summary>
/// C: a blue document-style dialog run through ExecView. It is a TDialog, so Enter presses the default button, Esc
/// and the close icon cancel, and only its palette differs: entries 1-32 keep the dialog role numbers but point at the
/// blue window roles, with label normal/selected/shortcut on the frame passive/active and normal-text roles.
/// </summary>
internal sealed class BlueDocumentDialog : TDialog
{
    public const int Width = 44, Height = 13;

    private static readonly TPalette DocumentPalette = new(
        "\x08\x09\x0A\x0B\x0C\x0D\x08\x09\x0D" +
        "\x29\x2A\x2B\x2C\x2D\x2E\x2F\x30\x31\x1D\x33\x1A\x35\x0A\x37\x38\x39\x3A\x3B\x3C\x3D\x3E\x3F", 32);

    public BlueDocumentDialog() : base(new TRect(0, 0, Width, Height), "Modal blue window")
    {
        options |= Views.ofCentered;
        Insert(new TStaticText(new TRect(2, 1, 42, 2), "Enter accepts, Esc cancels"));
        Find = new TInputLine(new TRect(9, 3, 30, 4), 30);
        Find.SetData("needle");
        Insert(Find);
        Insert(new TLabel(new TRect(2, 3, 8, 4), "~F~ind:", Find));
        var bar = new TScrollBar(new TRect(20, 5, 21, 9));
        Insert(bar);
        List = new TListBox(new TRect(2, 5, 20, 9), 1, bar);
        var items = new TStringCollection();
        foreach (string item in new[] { "First", "Second", "Third", "Fourth", "Fifth", "Sixth" }) items.Insert(item);
        List.NewList(items);
        Insert(List);
        Default = new TButton(new TRect(2, 10, 10, 12), "~O~K", Views.cmOK, ButtonConstants.bfDefault);
        Disabled = new TButton(new TRect(11, 10, 23, 12), "~D~isabled", PaletteGalleryCmd.Unavailable, ButtonConstants.bfNormal);
        Cancel = new TButton(new TRect(24, 10, 34, 12), "~C~ancel", Views.cmCancel, ButtonConstants.bfNormal);
        Insert(Default);
        Insert(Disabled);
        Insert(Cancel);
        SelectNext(false);
    }

    public TInputLine Find { get; }
    public TListBox List { get; }
    public TButton Default { get; }
    public TButton Disabled { get; }
    public TButton Cancel { get; }

    public override TPalette GetPalette() => DocumentPalette;
}

/// <summary>
/// D: a TWindow with its own palette: a cyan surface built only from existing application roles, which also defines
/// label roles. The controls are unchanged; their colors follow the parent's logical roles.
/// </summary>
internal sealed class CustomPaletteGalleryWindow : TWindow
{
    private static readonly TPalette CustomPalette = new(
        "\x10\x11\x12\x13\x14\x15\x10\x11\x15" +
        "\x29\x2A\x2B\x2C\x2D\x2E\x1D\x19\x28\x32\x33\x34\x35\x12\x37\x38\x1D\x3A\x28\x0E\x3D\x2C\x3F", 32);

    public CustomPaletteGalleryWindow(TRect bounds) : base(bounds, "Custom palette", 3)
    {
        Set = new PaletteGallerySet(this, label: true);
        SelectNext(false);
    }

    public PaletteGallerySet Set { get; }

    public override TPalette GetPalette() => CustomPalette;

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (Set is not null) Set.Handle(this, ref @event);
    }
}

/// <summary>The three modeless parents of the gallery.</summary>
internal sealed record PaletteGallery(GrayGalleryDialog Gray, BlueGalleryWindow Blue, CustomPaletteGalleryWindow Custom);

public partial class TVDemoApp
{
    internal static PaletteGallery BuildPaletteGallery()
    {
        // The disabled buttons stay disabled: nothing handles this command.
        TView.DisableCommand(PaletteGalleryCmd.Unavailable);
        int w = PaletteGallerySet.Width, h = PaletteGallerySet.Height;
        return new PaletteGallery(
            new GrayGalleryDialog(new TRect(1, 0, 1 + w, h)),
            new BlueGalleryWindow(new TRect(16, 3, 16 + w, 3 + h)),
            new CustomPaletteGalleryWindow(new TRect(32, 6, 32 + w, 6 + h)));
    }

    private void OpenPaletteGallery()
    {
        if (DeskTop == null) return;
        PaletteGallery gallery = BuildPaletteGallery();
        // Inserted last, the blue window is on top; Alt+1/2/3 or F6 switch between the parents.
        foreach (TWindow window in new TWindow[] { gallery.Gray, gallery.Custom, gallery.Blue })
            if (ValidView(window) != null)
                DeskTop.Insert(window);
    }
}
