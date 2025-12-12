// TSharpVision Resource Compiler
// AST node types for the .trc grammar.
// AST node types for .trc source files.
namespace TSharpVision.ResourceCompiler;

// ── Top-level file ────────────────────────────────────────────────────────────

/// <summary>Root of the .trc AST. Contains const directives and resources.</summary>
public sealed class TrcFile
{
    public List<ConstDirective>  Consts    { get; } = new();
    public List<ResourceDecl>    Resources { get; } = new();
}

// ── Const directive ───────────────────────────────────────────────────────────

/// <summary>
/// <c>const NAME = VALUE;</c>
/// </summary>
public sealed class ConstDirective
{
    public string Name  { get; set; }
    public int    Value { get; set; }
    public int    Line  { get; set; }
    public int    Column { get; set; }
}

// ── Resource declaration ──────────────────────────────────────────────────────

/// <summary>
/// <c>resource dialog|menu|statusbar "key" { … }</c>
/// </summary>
public sealed class ResourceDecl
{
    public ResourceKind    Kind       { get; set; }
    public string          Key        { get; set; }
    public int             Line       { get; set; }
    public int             Column     { get; set; }
    public DialogBody      Dialog     { get; set; }   // non-null when Kind==Dialog
    public MenuBody        Menu       { get; set; }   // non-null when Kind==Menu
    public StatusBarBody   StatusBar  { get; set; }   // non-null when Kind==StatusBar
}

public enum ResourceKind { Dialog, Menu, StatusBar }

// ── Dialog body ───────────────────────────────────────────────────────────────

public sealed class DialogBody
{
    public BoundsNode     Bounds  { get; set; }  // null → error in builder
    public string         Title   { get; set; }  // null → empty
    public string         Palette { get; set; }  // null → wpGrayDialog implied
    public List<ControlDecl> Controls { get; } = new();
}

// ── Bounds ────────────────────────────────────────────────────────────────────

public sealed class BoundsNode
{
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }
    public int Line   { get; set; }
    public int Column { get; set; }
}

// ── Control declarations ──────────────────────────────────────────────────────

public sealed class ControlDecl
{
    public ControlKind     Kind      { get; set; }
    public string          Title     { get; set; }
    public BoundsNode      Bounds    { get; set; }
    public string          Command   { get; set; }  // identifier (built-in or user const)
    public bool            IsDefault { get; set; }
    public ValidatorNode   Validator { get; set; }
    public List<string>    Items     { get; set; }  // for checkbox/radio
    public int             Line      { get; set; }
    public int             Column    { get; set; }
}

public enum ControlKind
{
    Button,
    Static,
    Label,
    Input,
    Checkbox,
    Radio,
}

// ── Validator ─────────────────────────────────────────────────────────────────

public abstract class ValidatorNode
{
    public int Line   { get; set; }
    public int Column { get; set; }
}

public sealed class FilterValidatorNode : ValidatorNode
{
    public string ValidChars { get; set; }
}

public sealed class RangeValidatorNode : ValidatorNode
{
    public long Min { get; set; }
    public long Max { get; set; }
}

public sealed class PictureValidatorNode : ValidatorNode
{
    public string Pic { get; set; }
}

// ── Menu resource ─────────────────────────────────────────────────────────────

/// <summary>
/// Body of a <c>resource menu "key" { … }</c> declaration.
/// </summary>
public sealed class MenuBody
{
    /// <summary>Optional explicit bounds; defaults to (0,0,80,1) in the builder.</summary>
    public BoundsNode         Bounds   { get; set; }
    public List<MenuItemDecl> Items    { get; } = new();
}

/// <summary>
/// One entry inside a menu or submenu: either an item, a separator, or a nested submenu.
/// </summary>
public sealed class MenuItemDecl
{
    public MenuItemKind      Kind      { get; set; }
    public string            Title     { get; set; }  // null for separator
    public string            Command   { get; set; }  // identifier; null for submenu/separator
    public string            Key       { get; set; }  // key text (e.g. "F3"); null = no key
    public List<MenuItemDecl> Children { get; } = new(); // non-empty only when Kind==Submenu
    public int               Line      { get; set; }
    public int               Column    { get; set; }
}

public enum MenuItemKind { Item, Separator, Submenu }

// ── StatusBar resource ────────────────────────────────────────────────────────

/// <summary>
/// Body of a <c>resource statusbar "key" { … }</c> declaration.
/// </summary>
public sealed class StatusBarBody
{
    /// <summary>Optional explicit bounds; defaults to (0,24,80,25) in the builder.</summary>
    public BoundsNode           Bounds { get; set; }
    public List<StatusRangeDecl> Ranges { get; } = new();
}

/// <summary>
/// A single <c>range MIN MAX { … }</c> block inside a statusbar resource.
/// </summary>
public sealed class StatusRangeDecl
{
    public int                   Min   { get; set; }
    public int                   Max   { get; set; }
    public List<StatusItemDecl>  Items { get; } = new();
    public int                   Line   { get; set; }
    public int                   Column { get; set; }
}

/// <summary>
/// One status item: <c>item "text" command=ID key="F1";</c>
/// </summary>
public sealed class StatusItemDecl
{
    public string  Text    { get; set; }
    public string  Command { get; set; }  // identifier
    public string  Key     { get; set; }  // key text; null = kbNoKey
    public int     Line    { get; set; }
    public int     Column  { get; set; }
}

