using TSharpVision.Constants;
using TSharpVision.Drivers;

namespace TSharpVision.Samples.TVDemo;

// Options/Physical palette and Options/Display effect: output-stage display settings of drivers that
// implement IPhysicalPaletteDriver / IDisplayEffectDriver (the SDL GPU driver). They recolour or post-process
// what the driver presents and are independent of Options/Framework palette, which selects the logical
// Color/BlackWhite/Monochrome family; the two compose. The ids come from the driver. A submenu is created only
// when the active driver exposes the capability when the menu bar is built (TApplication initializes the
// driver before TProgram calls InitMenuBar), so other drivers, or a decorator that does not forward the
// capability, show no extra items. The choice is not persisted: [graphics] palette= / effect= set the startup value.
internal static class DisplayPresetCmd
{
    // Contiguous ranges: item i of the driver's list uses First + i; ids past MaxItems are not offered.
    public const ushort PhysicalPaletteFirst = 360, DisplayEffectFirst = 370, MaxItems = 10;

    public static bool IsPhysicalPalette(ushort command) =>
        command >= PhysicalPaletteFirst && command < PhysicalPaletteFirst + MaxItems;

    public static bool IsDisplayEffect(ushort command) =>
        command >= DisplayEffectFirst && command < DisplayEffectFirst + MaxItems;
}

public partial class TVDemoApp
{
    // Assigned by InitMenuBar from the base constructor (null when the driver lacks the capability).
    private DisplayPresetChoice? _physicalPaletteChoice;
    private DisplayPresetChoice? _displayEffectChoice;

    // Appends the display submenus after the given Options item when the active driver supports them.
    private TMenuItem WithDisplayPresetSubmenus(TMenuItem last)
    {
        _physicalPaletteChoice = TDisplay.driver is IPhysicalPaletteDriver palettes
            ? new DisplayPresetChoice(palettes.PhysicalPalettes, DisplayPresetCmd.PhysicalPaletteFirst,
                () => (TDisplay.driver as IPhysicalPaletteDriver)?.PhysicalPalette,
                id => (TDisplay.driver as IPhysicalPaletteDriver)?.SetPhysicalPalette(id))
            : null;
        _displayEffectChoice = TDisplay.driver is IDisplayEffectDriver effects
            ? new DisplayPresetChoice(effects.DisplayEffects, DisplayPresetCmd.DisplayEffectFirst,
                () => (TDisplay.driver as IDisplayEffectDriver)?.DisplayEffect,
                id => (TDisplay.driver as IDisplayEffectDriver)?.SetDisplayEffect(id))
            : null;
        if (_physicalPaletteChoice == null && _displayEffectChoice == null) return last;

        TMenuItem tail = last;
        tail = tail.Next = TMenuItem.NewLine(); // separates the output-stage settings from the logical palette
        if (_physicalPaletteChoice != null)
            tail = tail.Next = DemoSubmenu("~P~hysical palette", _physicalPaletteChoice.Items);
        if (_displayEffectChoice != null)
            tail.Next = DemoSubmenu("Display ~e~ffect", _displayEffectChoice.Items);
        UpdateDisplayPresetIndicators();
        return last;
    }

    // Reflects a choice made by any route (menu, application code) the next time the menu opens.
    private void UpdateDisplayPresetIndicators()
    {
        _physicalPaletteChoice?.UpdateIndicators();
        _displayEffectChoice?.UpdateIndicators();
    }

    private bool HandleDisplayPresetCommand(ushort command)
    {
        DisplayPresetChoice? choice =
            DisplayPresetCmd.IsPhysicalPalette(command) ? _physicalPaletteChoice :
            DisplayPresetCmd.IsDisplayEffect(command) ? _displayEffectChoice : null;
        if (choice == null) return false;
        choice.Select(command);
        return true;
    }

    // One radio-style list of driver-defined ids. The driver applies a selection on its next frame.
    private sealed class DisplayPresetChoice
    {
        private readonly string[] _ids;
        private readonly string[] _captions;
        private readonly ushort _firstCommand;
        private readonly Func<string?> _active;
        private readonly Action<string> _select;
        private string? _marked = "";

        public TMenuItem[] Items { get; }

        public DisplayPresetChoice(IReadOnlyList<string> ids, ushort firstCommand, Func<string?> active, Action<string> select)
        {
            _ids = ids.Take(DisplayPresetCmd.MaxItems).ToArray();
            _captions = WithHotKeys(_ids);
            _firstCommand = firstCommand;
            _active = active;
            _select = select;
            Items = _ids.Select((_, i) => new TMenuItem(_captions[i], (ushort)(firstCommand + i), Keys.kbNoKey)).ToArray();
        }

        public void Select(ushort command)
        {
            int index = command - _firstCommand;
            if (index < 0 || index >= _ids.Length) return;
            try { _select(_ids[index]); }
            catch (ArgumentException) { } // the driver keeps its current choice; the mark shows it
            UpdateIndicators();
        }

        public void UpdateIndicators()
        {
            string? active = _active();
            if (string.Equals(active, _marked, StringComparison.Ordinal)) return;
            _marked = active;
            for (int i = 0; i < _ids.Length; i++)
            {
                bool on = string.Equals(_ids[i], active, StringComparison.OrdinalIgnoreCase);
                Items[i].Name = $"({(on ? TSharpVisionGlyphs.RadioChecked : ' ')}) {_captions[i]}";
            }
        }

        // Marks the first letter or digit of each id that no earlier id uses, e.g. "G~r~ayscale" after "~G~reen".
        private static string[] WithHotKeys(string[] ids)
        {
            var used = new HashSet<char>();
            return ids.Select(id =>
            {
                string text = id.Replace("~", "");
                for (int i = 0; i < text.Length; i++)
                {
                    char c = char.ToUpperInvariant(text[i]);
                    if (char.IsLetterOrDigit(c) && used.Add(c))
                        return text[..i] + "~" + text[i] + "~" + text[(i + 1)..];
                }
                return text;
            }).ToArray();
        }
    }
}
