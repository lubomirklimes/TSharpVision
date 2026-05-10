// TSharpVision Resource Compiler
// AST → TSharpVision object graph builder.
using TSharpVision.Constants;

namespace TSharpVision.ResourceCompiler;

/// <summary>
/// Converts a parsed <see cref="TrcFile"/> AST into TSharpVision runtime
/// objects (<see cref="TDialog"/>, <see cref="TMenuBar"/>,
/// <see cref="TStatusLine"/>) that can be serialised via
/// <see cref="TResourceFile"/>.
/// Errors are appended to <paramref name="diagnostics"/>; the builder
/// continues on non-fatal errors and may return partial results.
/// </summary>
public sealed class Builder
{
    private readonly List<Diagnostic>       _diag;
    private readonly Dictionary<string,int> _consts;

    public Builder(List<Diagnostic> diagnostics)
    {
        _diag   = diagnostics;
        _consts = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    // Public entry point

    /// <summary>
    /// Builds a list of (key, TStreamable) pairs from the parsed file.
    /// Returns an empty list when fatal errors prevent building.
    /// </summary>
    public List<(string key, TStreamable obj)> Build(TrcFile file)
    {
        _consts.Clear();
        foreach (var c in file.Consts)
            _consts[c.Name] = c.Value;

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var result   = new List<(string, TStreamable)>();

        foreach (var res in file.Resources)
        {
            if (!seenKeys.Add(res.Key))
            {
                _diag.Add(new Diagnostic(DiagnosticCodes.DuplicateResourceKey,
                    $"Duplicate resource key '{res.Key}'", res.Line, res.Column));
                continue;
            }

            TStreamable obj = res.Kind switch
            {
                ResourceKind.Dialog    => BuildDialog(res),
                ResourceKind.Menu      => BuildMenuBar(res),
                ResourceKind.StatusBar => BuildStatusLine(res),
                ResourceKind.Strings   => BuildStrings(res),
                _                      => null,
            };

            if (obj != null) result.Add((res.Key, obj));
        }

        return result;
    }

    // Dialog

    private TDialog BuildDialog(ResourceDecl res)
    {
        var body = res.Dialog;

        if (body.Bounds == null)
        {
            _diag.Add(new Diagnostic(DiagnosticCodes.MissingRequiredBounds,
                $"Dialog '{res.Key}' is missing required 'bounds' field",
                res.Line, res.Column));
            return null;
        }

        var rect  = MakeRect(body.Bounds);
        var title = body.Title ?? string.Empty;
        var dlg   = new TDialog(rect, title);

        if (body.Palette != null)
        {
            // TPalette is not TStreamable; palette cannot be embedded in .tvr.
            // Validate the name for documentation purposes and emit TRC0209
            // for unrecognized names.
            if (!CommandIds.IsKnownPaletteName(body.Palette))
            {
                _diag.Add(new Diagnostic(DiagnosticCodes.UnknownPaletteName,
                    $"Unknown dialog palette name '{body.Palette}'. " +
                    $"Known names: {string.Join(", ", CommandIds.PaletteNames)}.",
                    res.Line, res.Column));
            }
            // No runtime effect: TDialog.GetPalette() returns a fixed class-static palette.
        }

        foreach (var ctrl in body.Controls)
        {
            var view = BuildControl(ctrl);
            if (view != null) dlg.Insert(view);
        }

        return dlg;
    }

    // Controls

    private TView BuildControl(ControlDecl ctrl)
    {
        if (ctrl.Bounds == null)
        {
            _diag.Add(new Diagnostic(DiagnosticCodes.MissingRequiredBounds,
                $"Control '{ctrl.Kind}' at line {ctrl.Line} is missing required 'bounds' attribute",
                ctrl.Line, ctrl.Column));
            return null;
        }

        var rect = MakeRect(ctrl.Bounds);

        switch (ctrl.Kind)
        {
            case ControlKind.Button:   return BuildButton(ctrl, rect);
            case ControlKind.Static:   return new TStaticText(rect, ctrl.Title);
            case ControlKind.Label:    return new TLabel(rect, ctrl.Title, null);
            case ControlKind.Input:    return BuildInput(ctrl, rect);
            case ControlKind.Checkbox: return BuildCheckbox(ctrl, rect);
            case ControlKind.Radio:    return BuildRadio(ctrl, rect);
            default:
                _diag.Add(new Diagnostic(DiagnosticCodes.UnknownControlKind,
                    $"Unknown control kind '{ctrl.Kind}'", ctrl.Line, ctrl.Column));
                return null;
        }
    }

    private TButton BuildButton(ControlDecl ctrl, TRect rect)
    {
        ushort cmd   = ResolveCommand(ctrl.Command, ctrl.Line, ctrl.Column);
        byte   flags = ctrl.IsDefault
            ? ButtonConstants.bfDefault
            : ButtonConstants.bfNormal;
        return new TButton(rect, ctrl.Title, cmd, flags);
    }

    private TInputLine BuildInput(ControlDecl ctrl, TRect rect)
    {
        int maxLen = rect.b.x - rect.a.x;
        if (maxLen < 2) maxLen = 80;
        var input = new TInputLine(rect, maxLen);
        if (ctrl.Validator != null)
            input.Validator = BuildValidator(ctrl.Validator);
        return input;
    }

    private TCheckBoxes BuildCheckbox(ControlDecl ctrl, TRect rect)
        => new TCheckBoxes(rect, BuildTSItemList(ctrl));

    private TRadioButtons BuildRadio(ControlDecl ctrl, TRect rect)
        => new TRadioButtons(rect, BuildTSItemList(ctrl));

    // Validators

    private TValidator BuildValidator(ValidatorNode node)
    {
        switch (node)
        {
            case FilterValidatorNode fv:  return new TFilterValidator(fv.ValidChars);
            case RangeValidatorNode rv:   return new TRangeValidator(rv.Min, rv.Max);
            case PictureValidatorNode pv: return new TPXPictureValidator(pv.Pic);
            default:
                _diag.Add(new Diagnostic(DiagnosticCodes.UnsupportedValidator,
                    $"Unrecognised validator type '{node.GetType().Name}'",
                    node.Line, node.Column));
                return null;
        }
    }

    // Menu

    private static readonly TRect DefaultMenuRect = new TRect(0, 0, 80, 1);

    private TMenuBar BuildMenuBar(ResourceDecl res)
    {
        var body = res.Menu;
        var rect = body.Bounds != null ? MakeRect(body.Bounds) : DefaultMenuRect;
        TMenuItem itemList = BuildMenuItemList(body.Items, res.Line, res.Column);
        return new TMenuBar(rect, new TMenu(itemList));
    }

    private TMenuItem BuildMenuItemList(List<MenuItemDecl> decls, int errLine, int errCol)
    {
        TMenuItem head = null, tail = null;
        foreach (var decl in decls)
        {
            TMenuItem item = BuildMenuItemDecl(decl);
            if (item == null) continue;
            if (head == null) { head = item; tail = item; }
            else              { tail.Next = item; tail = item; }
        }
        return head;
    }

    private TMenuItem BuildMenuItemDecl(MenuItemDecl decl)
    {
        if (decl.Kind == MenuItemKind.Separator)
            return TMenuItem.NewLine();

        if (decl.Kind == MenuItemKind.Submenu)
        {
            if (decl.Children.Count == 0)
                _diag.Add(new Diagnostic(DiagnosticCodes.EmptySubmenu,
                    $"Submenu '{decl.Title}' has no items", decl.Line, decl.Column));
            TMenuItem childList = BuildMenuItemList(decl.Children, decl.Line, decl.Column);
            return new TMenuItem(decl.Title, Keys.kbNoKey, new TMenu(childList), Views.hcNoContext, null);
        }

        // Item
        ushort cmd     = ResolveCommand(decl.Command, decl.Line, decl.Column);
        ushort keyCode = ResolveKeyCode(decl.Key, decl.Line, decl.Column);
        return new TMenuItem(decl.Title, cmd, keyCode, Views.hcNoContext, null, null);
    }

    // StatusBar

    private static readonly TRect DefaultStatusRect = new TRect(0, 24, 80, 25);

    private TStatusLine BuildStatusLine(ResourceDecl res)
    {
        var body = res.StatusBar;
        var rect = body.Bounds != null ? MakeRect(body.Bounds) : DefaultStatusRect;

        if (body.Ranges.Count == 0)
        {
            _diag.Add(new Diagnostic(DiagnosticCodes.EmptyStatusRange,
                $"StatusBar '{res.Key}' has no ranges", res.Line, res.Column));
            return new TStatusLine(rect, new TStatusDef(0, 0xFFFF, null, null));
        }

        TStatusDef firstDef = null, lastDef = null;
        foreach (var rng in body.Ranges)
        {
            if (rng.Min > rng.Max)
                _diag.Add(new Diagnostic(DiagnosticCodes.InvalidStatusRange,
                    $"Status range min ({rng.Min}) must be <= max ({rng.Max})",
                    rng.Line, rng.Column));

            TStatusItem firstItem = null, lastItem = null;
            foreach (var sd in rng.Items)
            {
                ushort cmd     = ResolveCommand(sd.Command, sd.Line, sd.Column);
                ushort keyCode = ResolveKeyCode(sd.Key, sd.Line, sd.Column);
                var si = new TStatusItem(sd.Text, keyCode, cmd);
                if (firstItem == null) { firstItem = si; lastItem = si; }
                else                   { lastItem.Next = si; lastItem = si; }
            }

            var def = new TStatusDef((ushort)rng.Min, (ushort)rng.Max, firstItem, null);
            if (firstDef == null) { firstDef = def; lastDef = def; }
            else                  { lastDef.Next = def; lastDef = def; }
        }

        return new TStatusLine(rect, firstDef);
    }

    // Strings

    private TStringResource BuildStrings(ResourceDecl res)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in res.Strings.Entries)
        {
            if (values.ContainsKey(entry.Key))
            {
                _diag.Add(new Diagnostic(DiagnosticCodes.DuplicateField,
                    $"Duplicate string key '{entry.Key}'", entry.Line, entry.Column));
                continue;
            }

            values[entry.Key] = entry.Value;
        }

        return new TStringResource(values);
    }

    // Shared helpers

    private ushort ResolveCommand(string identifier, int line, int col)
    {
        if (string.IsNullOrEmpty(identifier)) return 0;
        if (CommandIds.TryResolve(identifier, _consts, out ushort code)) return code;
        _diag.Add(new Diagnostic(DiagnosticCodes.UndefinedCommand,
            $"Undefined command identifier '{identifier}'. " +
            "Declare it with 'const NAME = VALUE;' or use a built-in name.",
            line, col));
        return 0;
    }

    private ushort ResolveKeyCode(string keyText, int line, int col)
    {
        if (string.IsNullOrEmpty(keyText)) return Keys.kbNoKey;
        if (CommandIds.TryResolveKey(keyText, out ushort kc)) return kc;
        _diag.Add(new Diagnostic(DiagnosticCodes.UnknownKeyName,
            $"Unknown key name '{keyText}'. Key will be set to kbNoKey.", line, col));
        return Keys.kbNoKey;
    }

    private TSItem BuildTSItemList(ControlDecl ctrl)
    {
        if (ctrl.Items == null || ctrl.Items.Count == 0)
        {
            _diag.Add(new Diagnostic(DiagnosticCodes.EmptyItemList,
                $"'{ctrl.Kind}' control requires at least one item in 'items=(...)'",
                ctrl.Line, ctrl.Column));
            return new TSItem("(empty)", null);
        }
        TSItem head = null;
        for (int i = ctrl.Items.Count - 1; i >= 0; i--)
            head = new TSItem(ctrl.Items[i], head);
        return head;
    }

    private static TRect MakeRect(BoundsNode b) =>
        new TRect(b.X1, b.Y1, b.X2, b.Y2);
}
