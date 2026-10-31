using System.Collections.ObjectModel;
using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>
/// Single-line control that chooses a string from a dropdown list, optionally with free text entry.
/// </summary>
/// <remarks>
/// <para>
/// A non-editable combo box shows the selected item and lets the user pick another one: Up/Down, PgUp/PgDn,
/// Home/End and typed letters change the selection directly, and Alt+Down, F4 or a click opens the dropdown.
/// An editable combo box is a <see cref="TInputLine"/> with a dropdown of suggested values: the text may differ from
/// every item, and Down, Alt+Down, F4 or a click on the arrow opens the dropdown.
/// </para>
/// <para>
/// The dropdown runs modally on the desktop, like the <see cref="THistory"/> window: Up/Down/PgUp/PgDn/Home/End move
/// the highlight, Enter (or a click on an item, Space, F4, Alt+Up/Down) commits it, Esc or a click outside closes it
/// without any change, and Tab/Shift+Tab commit and then move the focus. Keys and clicks consumed by the dropdown never
/// reach the dialog, so Enter commits an item without pressing the default button and the first Esc only closes the
/// dropdown. While the dropdown is open the committed selection and the edit text are left untouched.
/// </para>
/// <para>
/// <see cref="SelectionChanged"/> and <see cref="TextChanged"/> are raised after the state has changed, at most once
/// each per action, text first. They are raised for programmatic changes too, but never while only the dropdown
/// highlight moves. The control owns its child views and its dropdown; it does not take part in dialog data records
/// (<see cref="DataSize"/> is zero) and cannot be written to a stream.
/// </para>
/// </remarks>
public class TComboBox : TGroup
{
    /// <summary>Type identifier of this view class.</summary>
    public new static readonly string Name = "TComboBox";

    /// <summary>Default value of <see cref="MaxDropDownRows"/>.</summary>
    public const int DefaultMaxDropDownRows = 8;

    /// <summary>Default maximum text length, in UTF-16 code units, of an editable combo box.</summary>
    public const int DefaultMaxLength = 255;

    private string[] _items;
    private ReadOnlyCollection<string> _itemsView;
    private int _itemsVersion;
    private int _selectedIndex = -1;
    private int _maxDropDownRows = DefaultMaxDropDownRows;
    private readonly TComboBoxInputLine? _input;
    private readonly TComboBoxField? _field;
    private readonly TComboBoxButton _button;
    private TComboBoxDropDown? _dropDown;
    private bool _shutDown;

    /// <summary>
    /// Creates a combo box at owner-relative cell bounds with a copy of <paramref name="items"/> and no selection.
    /// </summary>
    /// <param name="bounds">Owner-relative bounds; the control uses its first row.</param>
    /// <param name="items">Items to offer, copied; null elements are rejected.</param>
    /// <param name="editable">Whether the user may type text that is not one of the items.</param>
    /// <param name="maxLength">Maximum text length of an editable combo box in UTF-16 code units; longer text is truncated.</param>
    public TComboBox(TRect bounds, IEnumerable<string> items, bool editable = false, int maxLength = DefaultMaxLength)
        : base(bounds)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        _items = CopyItems(items);
        _itemsView = Array.AsReadOnly(_items);
        IsEditable = editable;
        MaxLength = maxLength;

        options = (ushort)((options | Views.ofSelectable | Views.ofFirstClick) & ~Views.ofBuffered);

        TRect field = new TRect(0, 0, 1, 1);
        _button = new TComboBoxButton(this, field);
        Insert(_button);
        if (editable)
        {
            _input = new TComboBoxInputLine(this, field, maxLength + 1);
            Insert(_input);
        }
        else
        {
            _field = new TComboBoxField(this, field);
            Insert(_field);
        }
        Layout();
    }

    /// <summary>Whether the user may enter text that differs from every item.</summary>
    public bool IsEditable { get; }

    /// <summary>Maximum text length of an editable combo box in UTF-16 code units.</summary>
    public int MaxLength { get; }

    /// <summary>Read-only view of the current items; replace them with <see cref="SetItems"/>.</summary>
    public IReadOnlyList<string> Items => _itemsView;

    /// <summary>
    /// Zero-based index of the selected item, or -1 for no selection.
    /// </summary>
    /// <remarks>
    /// Setting an index selects that item; in editable mode it also replaces the text with the item. Setting -1 clears
    /// the selection; in editable mode it also clears the text unless the selection was already -1. In editable mode the
    /// index follows the text: it is the selected item while the text still equals it, otherwise the first item equal
    /// to the text (ordinal comparison), otherwise -1.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than -1 or not less than the item count.</exception>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= _items.Length)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The index must be -1 or a valid item index.");
            if (value == _selectedIndex) return;
            Apply(value, IsEditable ? (value >= 0 ? Fit(_items[value]) : string.Empty) : null);
        }
    }

    /// <summary>The selected item, or null when nothing is selected.</summary>
    public string? SelectedItem => _selectedIndex >= 0 ? _items[_selectedIndex] : null;

    /// <summary>
    /// The displayed text: the edit text of an editable combo box, otherwise the selected item or an empty string.
    /// </summary>
    /// <remarks>
    /// In editable mode any text may be set; it is truncated to <see cref="MaxLength"/> and the selection follows it.
    /// In non-editable mode the value must equal an item (ordinal), which is then selected, or be empty to clear the selection.
    /// </remarks>
    /// <exception cref="ArgumentException">Non-editable mode and the value is neither empty nor an item.</exception>
    public string Text
    {
        get => _input != null ? _input.Data : SelectedItem ?? string.Empty;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_input == null)
            {
                int index = value.Length == 0 ? -1 : Array.IndexOf(_items, value);
                if (index < 0 && value.Length != 0)
                    throw new ArgumentException("A non-editable combo box can only show one of its items.", nameof(value));
                SelectedIndex = index;
                return;
            }

            string text = Fit(value);
            if (text == _input.Data) return;
            Apply(MatchIndex(text), text);
        }
    }

    /// <summary>Whether the dropdown list is currently open.</summary>
    public bool IsDropDownOpen => _dropDown != null;

    /// <summary>Maximum number of item rows the dropdown shows before it scrolls; at least one.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    public int MaxDropDownRows
    {
        get => _maxDropDownRows;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxDropDownRows = value;
        }
    }

    /// <summary>Raised after <see cref="SelectedIndex"/> changed, whether by the user or by code.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised after the edit text of an editable combo box changed, whether by the user or by code.</summary>
    public event EventHandler? TextChanged;

    /// <summary>
    /// Replaces the items with a copy of <paramref name="items"/>, closing an open dropdown without committing it.
    /// </summary>
    /// <remarks>
    /// A non-editable combo box keeps the previously selected item when the new items contain it (first ordinal match),
    /// otherwise it has no selection. An editable combo box keeps its text and selects the first item equal to it.
    /// </remarks>
    public void SetItems(IEnumerable<string> items)
    {
        string[] copy = CopyItems(items);
        CloseDropDown();

        int previousIndex = _selectedIndex;
        string? previousItem = SelectedItem;
        _items = copy;
        _itemsView = Array.AsReadOnly(copy);
        _itemsVersion++;
        _selectedIndex = -1;
        _selectedIndex = _input != null
            ? MatchIndex(_input.Data)
            : previousItem != null ? Array.IndexOf(copy, previousItem) : -1;

        DrawView();
        if (_selectedIndex != previousIndex) OnSelectionChanged();
    }

    /// <summary>
    /// Opens the dropdown and runs it modally until it is closed; returns whether an item was committed.
    /// </summary>
    /// <remarks>
    /// Returns false at once when the dropdown is already open, the control is disabled, hidden or not in a view tree,
    /// or there is no room for a dropdown.
    /// </remarks>
    public virtual bool OpenDropDown()
    {
        if (_dropDown != null || _shutDown || owner == null) return false;
        if ((state & (Views.sfVisible | Views.sfDisabled)) != Views.sfVisible) return false;

        TGroup? host = FindDropDownHost();
        if (host == null || ComputeDropDownBounds(host) is not TRect bounds) return false;

        var popup = new TComboBoxDropDown(this, bounds, InitialHighlight());
        popup.helpCtx = GetHelpCtx();
        int version = _itemsVersion;
        _dropDown = popup;
        _button.DrawView();
        ushort result;
        try
        {
            result = host.ExecView(popup);
        }
        finally
        {
            _dropDown = null;
            popup.ShutDown();
        }
        if (!_shutDown) _button.DrawView();

        int chosen = popup.List.focused;
        bool committed = result == Views.cmOK && !_shutDown && version == _itemsVersion
                         && chosen >= 0 && chosen < _items.Length;
        if (committed)
            Apply(chosen, _input != null ? Fit(_items[chosen]) : null);
        else if (!_shutDown)
            DrawView();

        if (popup.ForwardedKey is TEvent forwarded && !_shutDown && owner != null)
            PutEvent(ref forwarded);
        return committed;
    }

    /// <summary>Requests that an open dropdown close without committing; it closes before the next event is handled.</summary>
    public void CloseDropDown() => _dropDown?.EndModal(Views.cmCancel);

    /// <summary>Raises <see cref="SelectionChanged"/>.</summary>
    protected virtual void OnSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="TextChanged"/>.</summary>
    protected virtual void OnTextChanged() => TextChanged?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        switch (@event.What)
        {
            case Events.evMouseDown:
                if ((state & Views.sfDisabled) != 0) return;
                if (!Focus())
                {
                    ClearEvent(ref @event);
                    return;
                }
                if (_input == null || MakeLocal(@event.mouse.where).x >= _button.origin.x)
                {
                    ClearEvent(ref @event);
                    OpenDropDown();
                    return;
                }
                break;

            case Events.evKeyDown:
                if ((state & (Views.sfSelected | Views.sfDisabled)) != Views.sfSelected) break;
                if (IsOpenKey(@event.keyDown))
                {
                    ClearEvent(ref @event);
                    OpenDropDown();
                    return;
                }
                if (_input == null && HandleSelectionKey(@event.keyDown))
                {
                    ClearEvent(ref @event);
                    return;
                }
                break;
        }
        base.HandleEvent(ref @event);
    }

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if (aState == Views.sfDisabled && enable)
            CloseDropDown();
        if (aState == Views.sfSelected)
            _input?.SelectAll(enable);
        if ((aState & (Views.sfDisabled | Views.sfFocused | Views.sfSelected)) != 0)
            DrawView();
    }

    /// <inheritdoc />
    public override void ChangeBounds(TRect bounds)
    {
        SetBounds(bounds);
        clip = GetExtent();
        Layout();
        DrawView();
    }

    /// <inheritdoc />
    /// <remarks>Closes an open dropdown and releases the change handlers before shutting down the child views.</remarks>
    public override void ShutDown()
    {
        _shutDown = true;
        CloseDropDown();
        SelectionChanged = null;
        TextChanged = null;
        base.ShutDown();
    }

    /// <inheritdoc />
    /// <remarks>A combo box takes no part in dialog data records; use its properties instead.</remarks>
    public override ushort DataSize() => 0;

    /// <inheritdoc />
    /// <remarks>A combo box takes no part in dialog data records; the record is left unchanged.</remarks>
    public override void GetData(ref object rec) { }

    /// <inheritdoc />
    /// <remarks>A combo box takes no part in dialog data records; the record is ignored.</remarks>
    public override void SetData(object rec) { }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: a combo box cannot be written to a stream.</exception>
    public override void Write(Opstream os)
        => throw new NotSupportedException("TComboBox does not support streaming.");

    /// <inheritdoc />
    public override string StreamableName() => Name;

    // ── internals shared with the child views ───────────────────────────────

    internal bool IsDisabled => (state & Views.sfDisabled) != 0;

    // The arrow is highlighted while the control has the focus and while its dropdown is open (the dialog loses the
    // focus to the dropdown then).
    internal bool IsArrowHighlighted => _dropDown != null || (state & Views.sfFocused) != 0;

    internal TComboBoxDropDown? DropDown => _dropDown;

    internal string ItemText(int index) => (uint)index < (uint)_items.Length ? _items[index] : string.Empty;

    internal int ItemCount => _items.Length;

    // Called by the input line after a key or paste changed its text.
    internal void OnInputEdited()
    {
        int previous = _selectedIndex;
        _selectedIndex = MatchIndex(_input!.Data);
        OnTextChanged();
        if (_selectedIndex != previous) OnSelectionChanged();
    }

    // Checks, once per event read by the dropdown, that it may stay open, and moves it after the control when the
    // control, its window or the desktop changed. Returns false when the dropdown must close.
    internal bool SyncDropDown(TComboBoxDropDown popup)
    {
        if (_shutDown || popup.owner is not TGroup host) return false;
        if (!ReachesHost(host)) return false;
        if (ComputeDropDownBounds(host) is not TRect bounds) return false;
        if (bounds != popup.GetBounds())
            popup.Relayout(bounds);
        return true;
    }

    internal static int FindByPrefix(IReadOnlyList<string> items, string prefix, int after)
    {
        int n = items.Count;
        for (int i = 1; i <= n; i++)
        {
            int index = ((after < 0 ? -1 : after) + i) % n;
            if (items[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return index;
        }
        return -1;
    }

    // Truncates to a number of UTF-16 units without splitting a surrogate pair.
    internal static string Clip(string text, int units)
    {
        if (units <= 0) return string.Empty;
        if (text.Length <= units) return text;
        if (char.IsHighSurrogate(text[units - 1])) units--;
        return text.Substring(0, units);
    }

    /// <summary>
    /// Places a dropdown of <paramref name="itemCount"/> rows (at most <paramref name="maxRows"/>) for a control,
    /// both rectangles in the host's coordinates: below the control when it fits, else above, else on the larger side
    /// shrunk to fit, always inside <paramref name="area"/>. Null when the area cannot hold a framed row.
    /// </summary>
    internal static TRect? PlaceDropDown(TRect control, TRect area, int itemCount, int longestItem, int maxRows)
    {
        int areaWidth = area.b.x - area.a.x;
        int areaHeight = area.b.y - area.a.y;
        if (areaWidth < 3 || areaHeight < 3) return null;

        int height = Math.Clamp(itemCount, 1, Math.Max(1, maxRows)) + 2;
        // Frame, leading space and trailing space around the longest item; never narrower than the control.
        int width = Math.Min(Math.Max(Math.Max(control.b.x - control.a.x, longestItem + 4), 3), areaWidth);

        int below = area.b.y - control.b.y;
        int above = control.a.y - area.a.y;
        int y;
        if (below >= height) y = control.b.y;
        else if (above >= height) y = control.a.y - height;
        else if (Math.Max(below, above) >= 3)
        {
            if (below >= above) { height = below; y = control.b.y; }
            else { height = above; y = control.a.y - height; }
        }
        else
        {
            height = Math.Min(height, areaHeight);
            y = Math.Clamp(control.b.y, area.a.y, area.b.y - height);
        }

        int x = control.a.x;
        if (x + width > area.b.x) x = area.b.x - width;
        if (x < area.a.x) x = area.a.x;
        return new TRect(x, y, x + width, y + height);
    }

    // ── private ──────────────────────────────────────────────────────────────

    private static string[] CopyItems(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        string[] copy = items.ToArray();
        if (Array.Exists(copy, item => item is null))
            throw new ArgumentException("Combo box items cannot be null.", nameof(items));
        return copy;
    }

    private string Fit(string text) => Clip(text, MaxLength);

    // The index the text selects: the selected item while it still matches, else the first matching item, else -1.
    private int MatchIndex(string text)
    {
        if (_selectedIndex >= 0 && _selectedIndex < _items.Length && Fit(_items[_selectedIndex]) == text)
            return _selectedIndex;
        for (int i = 0; i < _items.Length; i++)
            if (Fit(_items[i]) == text) return i;
        return -1;
    }

    // Updates selection and (editable) text, redraws, then notifies; null text leaves the text alone.
    private void Apply(int index, string? text)
    {
        bool selectionChanged = index != _selectedIndex;
        bool textChanged = false;
        _selectedIndex = index;
        if (_input != null && text != null)
        {
            textChanged = text != _input.Data;
            _input.Assign(text);
        }
        DrawView();
        if (textChanged) OnTextChanged();
        if (selectionChanged) OnSelectionChanged();
    }

    private void Layout()
    {
        int width = Math.Max(0, size.x);
        int buttonWidth = width >= 6 ? 3 : Math.Min(1, width);
        int fieldWidth = width - buttonWidth;
        TView field = (TView?)_input ?? _field!;
        field.SetBounds(new TRect(0, 0, fieldWidth, 1));
        _button.SetBounds(new TRect(fieldWidth, 0, width, 1));
    }

    private bool IsOpenKey(in KeyDownEvent key)
    {
        uint modifiers = key.controlKeyState & (Keys.kbAltShift | Keys.kbCtrlShift | Keys.kbShift);
        if (key.keyCode == Keys.kbF4) return modifiers == 0;
        if (key.keyCode != Keys.kbDown || (modifiers & Keys.kbCtrlShift) != 0) return false;
        // Alt+Down in both modes; plain Down too in editable mode, as THistory does.
        return (modifiers & Keys.kbAltShift) != 0 || (_input != null && modifiers == 0);
    }

    // Selection keys of a closed non-editable combo box; every change is committed at once.
    private bool HandleSelectionKey(in KeyDownEvent key)
    {
        if ((key.controlKeyState & (Keys.kbAltShift | Keys.kbCtrlShift)) != 0
            && key.keyCode is not (Keys.kbCtrlPgUp or Keys.kbCtrlPgDn or Keys.kbCtrlHome or Keys.kbCtrlEnd))
            return false;

        int count = _items.Length;
        int current = _selectedIndex;
        int target;
        switch (key.keyCode)
        {
            case Keys.kbUp: target = current <= 0 ? 0 : current - 1; break;
            case Keys.kbDown: target = current < 0 ? 0 : current + 1; break;
            case Keys.kbPgUp: target = current - _maxDropDownRows; break;
            case Keys.kbPgDn: target = current < 0 ? _maxDropDownRows - 1 : current + _maxDropDownRows; break;
            case Keys.kbHome:
            case Keys.kbCtrlHome:
            case Keys.kbCtrlPgUp: target = 0; break;
            case Keys.kbEnd:
            case Keys.kbCtrlEnd:
            case Keys.kbCtrlPgDn: target = count - 1; break;
            default:
                string text = KeyText.PrintableText(key);
                if (text.Length == 0 || text == " ") return false;
                target = count == 0 ? -1 : FindByPrefix(_itemsView, text, current);
                if (target < 0) return true;
                break;
        }
        if (count == 0) return true;
        target = Math.Clamp(target, 0, count - 1);
        if (target != _selectedIndex) SelectedIndex = target;
        return true;
    }

    private int InitialHighlight()
    {
        if (_selectedIndex >= 0) return _selectedIndex;
        if (_input != null && _input.Data.Length > 0)
        {
            int prefix = FindByPrefix(_itemsView, _input.Data, -1);
            if (prefix >= 0) return prefix;
        }
        return 0;
    }

    // The desktop that holds the control, or the root of its view tree when it is not on a desktop.
    private TGroup? FindDropDownHost()
    {
        TGroup? top = null;
        for (TGroup? g = owner; g != null; g = g.owner)
        {
            if (g is TDeskTop) return g;
            top = g;
        }
        return top;
    }

    // Whether the control is still shown and enabled inside the host and no modal view is ending.
    private bool ReachesHost(TGroup host)
    {
        if (!IsShownAndEnabled()) return false;
        for (TGroup? g = owner; g != null; g = g.owner)
        {
            if ((g.state & Views.sfModal) != 0 && g.endState != 0) return false;
            if (ReferenceEquals(g, host)) return true;
        }
        return false;
    }

    private TRect? ComputeDropDownBounds(TGroup host)
    {
        TPoint origin = host.MakeGlobal(default);
        TPoint global = MakeGlobal(default);
        int x = global.x - origin.x, y = global.y - origin.y;
        TRect control = new TRect(x, y, x + size.x, y + Math.Max(1, size.y));

        int longest = 0;
        foreach (string item in _items) longest = Math.Max(longest, item.Length);
        return PlaceDropDown(control, WorkingArea(host), _items.Length, longest, _maxDropDownRows);
    }

    // The host's extent without the menu bar and status line when the host is the program itself.
    private static TRect WorkingArea(TGroup host)
    {
        TRect area = host.GetExtent();
        if (host is TProgram program)
        {
            if (program.MenuBar is { } menu && ReferenceEquals(menu.owner, host))
                area.a.y = Math.Max(area.a.y, menu.origin.y + menu.size.y);
            if (program.StatusLine is { } status && ReferenceEquals(status.owner, host))
                area.b.y = Math.Min(area.b.y, status.origin.y);
        }
        return area;
    }
}
