// TSharpVision Resource Compiler
// Recursive-descent parser for the .trc grammar.
namespace TSharpVision.ResourceCompiler;

/// <summary>
/// Converts a flat token list produced by <see cref="Lexer"/> into a
/// <see cref="TrcFile"/> AST.  Parse errors are appended to the diagnostics
/// list; the parser recovers where possible so subsequent errors are visible.
/// </summary>
public sealed class Parser
{
    private readonly List<Token>      _tokens;
    private readonly List<Diagnostic> _diag;
    private int _pos;

    public Parser(List<Token> tokens, List<Diagnostic> diagnostics)
    {
        _tokens = tokens;
        _diag   = diagnostics;
        _pos    = 0;
    }

    // ── Entry point ───────────────────────────────────────────────────────────

    public TrcFile ParseFile()
    {
        var file = new TrcFile();
        while (!AtEof())
        {
            if (PeekIs("const"))        { var c = ParseConst();    if (c != null) file.Consts.Add(c); }
            else if (PeekIs("resource")){ var r = ParseResource(); if (r != null) file.Resources.Add(r); }
            else
            {
                var t = Peek();
                _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                    $"Expected 'const' or 'resource', got '{t.Value}'",
                    t.Line, t.Column));
                Advance(); // skip unknown token
            }
        }
        return file;
    }

    // ── Const directive ───────────────────────────────────────────────────────

    // const NAME = INTEGER ;
    private ConstDirective ParseConst()
    {
        var kw = Consume(); // 'const'
        var name = Expect(TokenKind.Identifier, "identifier after 'const'");
        if (name == null) { SkipToSemicolon(); return null; }
        if (!ExpectPunct(TokenKind.Equals,     "'=' in const")) { SkipToSemicolon(); return null; }
        var val = Expect(TokenKind.Integer,    "integer value");
        if (val == null) { SkipToSemicolon(); return null; }
        if (!ExpectPunct(TokenKind.Semicolon,  "';' after const")) return null;

        if (!int.TryParse(val.Value, out int intVal))
        {
            _diag.Add(new Diagnostic(DiagnosticCodes.InvalidBoundsValue,
                $"Invalid integer constant '{val.Value}'", val.Line, val.Column));
            return null;
        }
        return new ConstDirective { Name = name.Value, Value = intVal, Line = kw.Line, Column = kw.Column };
    }

    // ── Resource declaration ──────────────────────────────────────────────────

    // resource dialog|menu|statusbar "key" { body }
    private ResourceDecl ParseResource()
    {
        var kw = Consume(); // 'resource'
        var kindTok = Expect(TokenKind.Identifier, "resource kind (e.g. 'dialog', 'menu', 'statusbar')");
        if (kindTok == null) { SkipToCloseBrace(); return null; }

        ResourceKind kind;
        switch (kindTok.Value)
        {
            case "dialog":    kind = ResourceKind.Dialog;    break;
            case "menu":      kind = ResourceKind.Menu;      break;
            case "statusbar": kind = ResourceKind.StatusBar; break;
            default:
                _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                    $"Unknown resource kind '{kindTok.Value}'. Supported: dialog, menu, statusbar.",
                    kindTok.Line, kindTok.Column));
                SkipToCloseBrace();
                return null;
        }

        var keyTok = Expect(TokenKind.StringLiteral, "resource key string");
        if (keyTok == null) { SkipToCloseBrace(); return null; }

        if (!ExpectPunct(TokenKind.OpenBrace, "'{'")) { SkipToCloseBrace(); return null; }

        var decl = new ResourceDecl
        {
            Kind   = kind,
            Key    = keyTok.Value,
            Line   = kw.Line,
            Column = kw.Column,
        };

        switch (kind)
        {
            case ResourceKind.Dialog:    decl.Dialog    = ParseDialogBody();    break;
            case ResourceKind.Menu:      decl.Menu      = ParseMenuBody();      break;
            case ResourceKind.StatusBar: decl.StatusBar = ParseStatusBarBody(); break;
        }

        if (!ExpectPunct(TokenKind.CloseBrace, "'}'")) { /* best-effort */ }

        return decl;
    }

    // ── Dialog body ───────────────────────────────────────────────────────────

    private DialogBody ParseDialogBody()
    {
        var body = new DialogBody();
        bool hasBounds  = false;
        bool hasTitle   = false;
        bool hasPalette = false;

        while (!AtEof() && !PeekKind(TokenKind.CloseBrace))
        {
            var t = Peek();
            if (t.Kind == TokenKind.Identifier)
            {
                switch (t.Value)
                {
                    case "bounds":
                        if (hasBounds)
                            _diag.Add(new Diagnostic(DiagnosticCodes.DuplicateField,
                                "Duplicate 'bounds' field in dialog", t.Line, t.Column));
                        Advance();
                        body.Bounds  = ParseBoundsArgs(t);
                        hasBounds    = true;
                        ExpectPunct(TokenKind.Semicolon, "';' after bounds");
                        continue;

                    case "title":
                        if (hasTitle)
                            _diag.Add(new Diagnostic(DiagnosticCodes.DuplicateField,
                                "Duplicate 'title' field in dialog", t.Line, t.Column));
                        Advance();
                        var titleTok = Expect(TokenKind.StringLiteral, "title string");
                        body.Title   = titleTok?.Value ?? string.Empty;
                        hasTitle     = true;
                        ExpectPunct(TokenKind.Semicolon, "';' after title");
                        continue;

                    case "palette":
                        if (hasPalette)
                            _diag.Add(new Diagnostic(DiagnosticCodes.DuplicateField,
                                "Duplicate 'palette' field in dialog", t.Line, t.Column));
                        Advance();
                        var palTok  = Expect(TokenKind.Identifier, "palette name");
                        body.Palette = palTok?.Value ?? "wpGrayDialog";
                        hasPalette   = true;
                        ExpectPunct(TokenKind.Semicolon, "';' after palette");
                        continue;

                    // Known control keywords
                    case "button":
                    case "static":
                    case "label":
                    case "input":
                    case "checkbox":
                    case "radio":
                        var ctrl = ParseControl();
                        if (ctrl != null) body.Controls.Add(ctrl);
                        continue;

                    default:
                        _diag.Add(new Diagnostic(DiagnosticCodes.UnknownControlKind,
                            $"Unknown keyword '{t.Value}' in dialog body", t.Line, t.Column));
                        SkipToSemicolon();
                        continue;
                }
            }
            // Unexpected token inside dialog body
            _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                $"Unexpected token '{t.Value}' in dialog body", t.Line, t.Column));
            Advance();
        }
        return body;
    }

    // ── Control declaration ───────────────────────────────────────────────────

    // control-kind "title" { attr* } ;
    private ControlDecl ParseControl()
    {
        var kindTok = Consume();
        ControlKind kind = kindTok.Value switch
        {
            "button"   => ControlKind.Button,
            "static"   => ControlKind.Static,
            "label"    => ControlKind.Label,
            "input"    => ControlKind.Input,
            "checkbox" => ControlKind.Checkbox,
            "radio"    => ControlKind.Radio,
            _          => ControlKind.Static, // unreachable
        };

        var titleTok = Expect(TokenKind.StringLiteral, "control title string");
        if (titleTok == null) { SkipToSemicolon(); return null; }

        var ctrl = new ControlDecl
        {
            Kind   = kind,
            Title  = titleTok.Value,
            Line   = kindTok.Line,
            Column = kindTok.Column,
        };

        // Parse zero or more control attributes (no braces — space-separated before ;)
        while (!AtEof() && !PeekKind(TokenKind.Semicolon) && !PeekKind(TokenKind.CloseBrace))
        {
            var t = Peek();
            if (t.Kind != TokenKind.Identifier)
            {
                _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                    $"Expected control attribute, got '{t.Value}'", t.Line, t.Column));
                Advance();
                continue;
            }

            switch (t.Value)
            {
                case "bounds":
                    Advance();
                    if (!ExpectPunct(TokenKind.Equals, "'=' after bounds")) break;
                    ctrl.Bounds = ParseBoundsArgs(t);
                    break;

                case "command":
                    Advance();
                    if (!ExpectPunct(TokenKind.Equals, "'=' after command")) break;
                    var cmdTok = Expect(TokenKind.Identifier, "command identifier");
                    ctrl.Command = cmdTok?.Value;
                    break;

                case "default":
                    Advance();
                    ctrl.IsDefault = true;
                    break;

                case "validator":
                    Advance();
                    if (!ExpectPunct(TokenKind.Equals, "'=' after validator")) break;
                    ctrl.Validator = ParseValidator();
                    break;

                case "items":
                    Advance();
                    if (!ExpectPunct(TokenKind.Equals, "'=' after items")) break;
                    ctrl.Items = ParseItemList();
                    break;

                default:
                    _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                        $"Unknown control attribute '{t.Value}'", t.Line, t.Column));
                    Advance();
                    break;
            }
        }

        ExpectPunct(TokenKind.Semicolon, "';' at end of control");
        return ctrl;
    }

    // ── Bounds: ( X1 , Y1 , X2 , Y2 ) ───────────────────────────────────────

    private BoundsNode ParseBoundsArgs(Token contextTok)
    {
        if (!ExpectPunct(TokenKind.OpenParen, "'(' in bounds")) return null;
        var x1 = ParseInt("x1"); if (x1 == null) { SkipToCloseParen(); return null; }
        if (!ExpectPunct(TokenKind.Comma, "','")) { SkipToCloseParen(); return null; }
        var y1 = ParseInt("y1"); if (y1 == null) { SkipToCloseParen(); return null; }
        if (!ExpectPunct(TokenKind.Comma, "','")) { SkipToCloseParen(); return null; }
        var x2 = ParseInt("x2"); if (x2 == null) { SkipToCloseParen(); return null; }
        if (!ExpectPunct(TokenKind.Comma, "','")) { SkipToCloseParen(); return null; }
        var y2 = ParseInt("y2"); if (y2 == null) { SkipToCloseParen(); return null; }
        ExpectPunct(TokenKind.CloseParen, "')'");
        return new BoundsNode
        {
            X1 = x1.Value, Y1 = y1.Value, X2 = x2.Value, Y2 = y2.Value,
            Line = contextTok.Line, Column = contextTok.Column,
        };
    }

    private int? ParseInt(string what)
    {
        var t = Peek();
        if (t.Kind != TokenKind.Integer)
        {
            _diag.Add(new Diagnostic(DiagnosticCodes.MalformedBounds,
                $"Expected integer for {what}, got '{t.Value}'", t.Line, t.Column));
            return null;
        }
        Advance();
        if (!int.TryParse(t.Value, out int v))
        {
            _diag.Add(new Diagnostic(DiagnosticCodes.InvalidBoundsValue,
                $"Invalid integer '{t.Value}'", t.Line, t.Column));
            return null;
        }
        return v;
    }

    // ── Validator ─────────────────────────────────────────────────────────────

    // filter("chars") | range(min, max) | picture("mask")
    private ValidatorNode ParseValidator()
    {
        var nameTok = Expect(TokenKind.Identifier, "validator kind");
        if (nameTok == null) return null;
        int vl = nameTok.Line, vc = nameTok.Column;

        switch (nameTok.Value)
        {
            case "filter":
            {
                if (!ExpectPunct(TokenKind.OpenParen,  "'(' after filter")) return null;
                var chars = Expect(TokenKind.StringLiteral, "filter valid-chars string");
                if (!ExpectPunct(TokenKind.CloseParen, "')'")) return null;
                return new FilterValidatorNode { ValidChars = chars?.Value ?? string.Empty, Line = vl, Column = vc };
            }
            case "range":
            {
                if (!ExpectPunct(TokenKind.OpenParen, "'(' after range")) return null;
                var minT = ParseInt("min"); if (minT == null) { SkipToCloseParen(); return null; }
                if (!ExpectPunct(TokenKind.Comma, "','")) return null;
                var maxT = ParseInt("max"); if (maxT == null) { SkipToCloseParen(); return null; }
                if (!ExpectPunct(TokenKind.CloseParen, "')'")) return null;
                return new RangeValidatorNode { Min = minT.Value, Max = maxT.Value, Line = vl, Column = vc };
            }
            case "picture":
            {
                if (!ExpectPunct(TokenKind.OpenParen, "'(' after picture")) return null;
                var pic = Expect(TokenKind.StringLiteral, "picture mask string");
                if (!ExpectPunct(TokenKind.CloseParen, "')'")) return null;
                return new PictureValidatorNode { Pic = pic?.Value ?? string.Empty, Line = vl, Column = vc };
            }
            default:
                _diag.Add(new Diagnostic(DiagnosticCodes.UnsupportedValidator,
                    $"Unknown validator '{nameTok.Value}'. Supported: filter, range, picture.",
                    vl, vc));
                return null;
        }
    }

    // ── Item list: ( "A", "B", "C" ) ─────────────────────────────────────────

    private List<string> ParseItemList()
    {
        var items = new List<string>();
        var openTok = Peek();
        if (!ExpectPunct(TokenKind.OpenParen, "'(' in items list")) return items;

        if (PeekKind(TokenKind.CloseParen))
        {
            _diag.Add(new Diagnostic(DiagnosticCodes.EmptyItemList,
                "items list must not be empty", openTok.Line, openTok.Column));
            Advance();
            return items;
        }

        while (!AtEof() && !PeekKind(TokenKind.CloseParen))
        {
            var s = Expect(TokenKind.StringLiteral, "item string in items list");
            if (s != null) items.Add(s.Value);
            if (PeekKind(TokenKind.Comma)) Advance();
            else break;
        }
        ExpectPunct(TokenKind.CloseParen, "')'");
        return items;
    }

    // ── Token-stream helpers ──────────────────────────────────────────────────

    private Token Peek() => _tokens[_pos];

    private bool PeekKind(TokenKind k) => _tokens[_pos].Kind == k;
    private bool PeekIs(string value)
    {
        var t = _tokens[_pos];
        return t.Kind == TokenKind.Identifier && t.Value == value;
    }

    private bool AtEof() => _tokens[_pos].Kind == TokenKind.Eof;

    private Token Consume()
    {
        var t = _tokens[_pos];
        if (!AtEof()) _pos++;
        return t;
    }

    private Token Advance() => Consume();

    private Token Expect(TokenKind kind, string what)
    {
        var t = Peek();
        if (t.Kind == kind) { Consume(); return t; }
        _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
            $"Expected {what}, got '{t.Value}'", t.Line, t.Column));
        return null;
    }

    private bool ExpectPunct(TokenKind kind, string what)
    {
        if (PeekKind(kind)) { Consume(); return true; }
        var t = Peek();
        string code = kind switch
        {
            TokenKind.Semicolon  => DiagnosticCodes.MissingSemicolon,
            TokenKind.OpenBrace  => DiagnosticCodes.MissingOpenBrace,
            TokenKind.CloseBrace => DiagnosticCodes.MissingCloseBrace,
            TokenKind.OpenParen  => DiagnosticCodes.MissingOpenParen,
            TokenKind.CloseParen => DiagnosticCodes.MissingCloseParen,
            TokenKind.Equals     => DiagnosticCodes.MissingEquals,
            _                    => DiagnosticCodes.UnexpectedToken,
        };
        _diag.Add(new Diagnostic(code, $"Expected {what}, got '{t.Value}'", t.Line, t.Column));
        return false;
    }

    private void SkipToSemicolon()
    {
        while (!AtEof() && !PeekKind(TokenKind.Semicolon) && !PeekKind(TokenKind.CloseBrace))
            Advance();
        if (PeekKind(TokenKind.Semicolon)) Advance();
    }

    private void SkipToCloseBrace()
    {
        int depth = 0;
        while (!AtEof())
        {
            if (PeekKind(TokenKind.OpenBrace))  { depth++; Advance(); continue; }
            if (PeekKind(TokenKind.CloseBrace))
            {
                if (depth == 0) { Advance(); break; }
                depth--; Advance();
            }
            else Advance();
        }
    }

    private void SkipToCloseParen()
    {
        while (!AtEof() && !PeekKind(TokenKind.CloseParen) && !PeekKind(TokenKind.Semicolon))
            Advance();
        if (PeekKind(TokenKind.CloseParen)) Advance();
    }

    // ── Menu body ─────────────────────────────────────────────────────────────

    // menu-body = ( "bounds" "(" x1 "," y1 "," x2 "," y2 ")" ";" )?
    //             ( submenu-decl | item-decl | separator-decl )*
    private MenuBody ParseMenuBody()
    {
        var body = new MenuBody();
        while (!AtEof() && !PeekKind(TokenKind.CloseBrace))
        {
            var t = Peek();
            if (t.Kind == TokenKind.Identifier)
            {
                switch (t.Value)
                {
                    case "bounds":
                        if (body.Bounds != null)
                            _diag.Add(new Diagnostic(DiagnosticCodes.DuplicateField,
                                "Duplicate 'bounds' in menu", t.Line, t.Column));
                        Advance();
                        body.Bounds = ParseBoundsArgs(t);
                        ExpectPunct(TokenKind.Semicolon, "';' after bounds");
                        continue;

                    case "submenu":
                        var sub = ParseSubmenu();
                        if (sub != null) body.Items.Add(sub);
                        continue;

                    case "item":
                        var itm = ParseMenuItem();
                        if (itm != null) body.Items.Add(itm);
                        continue;

                    case "separator":
                        Advance();
                        ExpectPunct(TokenKind.Semicolon, "';' after separator");
                        body.Items.Add(new MenuItemDecl
                        {
                            Kind = MenuItemKind.Separator,
                            Line = t.Line, Column = t.Column,
                        });
                        continue;

                    default:
                        _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                            $"Unknown keyword '{t.Value}' in menu body. Expected: submenu, item, separator, bounds.",
                            t.Line, t.Column));
                        SkipToSemicolon();
                        continue;
                }
            }
            _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                $"Unexpected token '{t.Value}' in menu body", t.Line, t.Column));
            Advance();
        }
        return body;
    }

    // submenu "Title" { ( item | separator | submenu )* }
    private MenuItemDecl ParseSubmenu()
    {
        var kw = Consume(); // 'submenu'
        var titleTok = Expect(TokenKind.StringLiteral, "submenu title string");
        if (titleTok == null) { SkipToCloseBrace(); return null; }

        if (!ExpectPunct(TokenKind.OpenBrace, "'{'")) { SkipToCloseBrace(); return null; }

        var decl = new MenuItemDecl
        {
            Kind = MenuItemKind.Submenu,
            Title = titleTok.Value,
            Line = kw.Line, Column = kw.Column,
        };

        while (!AtEof() && !PeekKind(TokenKind.CloseBrace))
        {
            var t = Peek();
            if (t.Kind == TokenKind.Identifier)
            {
                switch (t.Value)
                {
                    case "item":
                        var itm = ParseMenuItem();
                        if (itm != null) decl.Children.Add(itm);
                        continue;
                    case "separator":
                        Advance();
                        ExpectPunct(TokenKind.Semicolon, "';' after separator");
                        decl.Children.Add(new MenuItemDecl
                        {
                            Kind = MenuItemKind.Separator,
                            Line = t.Line, Column = t.Column,
                        });
                        continue;
                    case "submenu":
                        var nested = ParseSubmenu();
                        if (nested != null) decl.Children.Add(nested);
                        continue;
                    default:
                        _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                            $"Unknown keyword '{t.Value}' in submenu. Expected: item, separator, submenu.",
                            t.Line, t.Column));
                        SkipToSemicolon();
                        continue;
                }
            }
            _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                $"Unexpected token '{t.Value}' in submenu", t.Line, t.Column));
            Advance();
        }

        ExpectPunct(TokenKind.CloseBrace, "'}' at end of submenu");
        return decl;
    }

    // item "Title" command=ID key="F3" ;
    private MenuItemDecl ParseMenuItem()
    {
        var kw = Consume(); // 'item'
        var titleTok = Expect(TokenKind.StringLiteral, "menu item title string");
        if (titleTok == null) { SkipToSemicolon(); return null; }

        var decl = new MenuItemDecl
        {
            Kind = MenuItemKind.Item,
            Title = titleTok.Value,
            Line = kw.Line, Column = kw.Column,
        };

        while (!AtEof() && !PeekKind(TokenKind.Semicolon) && !PeekKind(TokenKind.CloseBrace))
        {
            var t = Peek();
            if (t.Kind != TokenKind.Identifier)
            {
                _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                    $"Expected item attribute (command, key), got '{t.Value}'", t.Line, t.Column));
                Advance();
                continue;
            }
            switch (t.Value)
            {
                case "command":
                    Advance();
                    if (!ExpectPunct(TokenKind.Equals, "'=' after command")) break;
                    var cmdTok = Expect(TokenKind.Identifier, "command identifier");
                    decl.Command = cmdTok?.Value;
                    break;
                case "key":
                    Advance();
                    if (!ExpectPunct(TokenKind.Equals, "'=' after key")) break;
                    var keyTok = Expect(TokenKind.StringLiteral, "key string (e.g. \"F3\")");
                    decl.Key = keyTok?.Value;
                    break;
                default:
                    _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                        $"Unknown item attribute '{t.Value}'. Expected: command, key.",
                        t.Line, t.Column));
                    Advance();
                    break;
            }
        }

        ExpectPunct(TokenKind.Semicolon, "';' at end of item");
        return decl;
    }

    // ── StatusBar body ────────────────────────────────────────────────────────

    // statusbar-body = ( "bounds" "(" … ")" ";" )?
    //                  ( "range" INT INT "{" status-item* "}" )*
    private StatusBarBody ParseStatusBarBody()
    {
        var body = new StatusBarBody();
        while (!AtEof() && !PeekKind(TokenKind.CloseBrace))
        {
            var t = Peek();
            if (t.Kind == TokenKind.Identifier)
            {
                switch (t.Value)
                {
                    case "bounds":
                        if (body.Bounds != null)
                            _diag.Add(new Diagnostic(DiagnosticCodes.DuplicateField,
                                "Duplicate 'bounds' in statusbar", t.Line, t.Column));
                        Advance();
                        body.Bounds = ParseBoundsArgs(t);
                        ExpectPunct(TokenKind.Semicolon, "';' after bounds");
                        continue;

                    case "range":
                        var rng = ParseStatusRange();
                        if (rng != null) body.Ranges.Add(rng);
                        continue;

                    default:
                        _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                            $"Unknown keyword '{t.Value}' in statusbar body. Expected: range, bounds.",
                            t.Line, t.Column));
                        SkipToSemicolon();
                        continue;
                }
            }
            _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                $"Unexpected token '{t.Value}' in statusbar body", t.Line, t.Column));
            Advance();
        }
        return body;
    }

    // range MIN MAX { status-item* }
    private StatusRangeDecl ParseStatusRange()
    {
        var kw = Consume(); // 'range'
        var minTok = ParseInt("range min");
        if (minTok == null) { SkipToCloseBrace(); return null; }
        var maxTok = ParseInt("range max");
        if (maxTok == null) { SkipToCloseBrace(); return null; }

        if (!ExpectPunct(TokenKind.OpenBrace, "'{'")) { SkipToCloseBrace(); return null; }

        var rng = new StatusRangeDecl
        {
            Min = minTok.Value, Max = maxTok.Value,
            Line = kw.Line, Column = kw.Column,
        };

        while (!AtEof() && !PeekKind(TokenKind.CloseBrace))
        {
            var t = Peek();
            if (t.Kind == TokenKind.Identifier && t.Value == "item")
            {
                var itm = ParseStatusItem();
                if (itm != null) rng.Items.Add(itm);
                continue;
            }
            _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                $"Expected 'item' in status range, got '{t.Value}'", t.Line, t.Column));
            Advance();
        }

        ExpectPunct(TokenKind.CloseBrace, "'}' at end of range");
        return rng;
    }

    // item "text" command=ID key="F1" ;
    private StatusItemDecl ParseStatusItem()
    {
        var kw = Consume(); // 'item'
        var textTok = Expect(TokenKind.StringLiteral, "status item text string");
        if (textTok == null) { SkipToSemicolon(); return null; }

        var decl = new StatusItemDecl
        {
            Text = textTok.Value,
            Line = kw.Line, Column = kw.Column,
        };

        while (!AtEof() && !PeekKind(TokenKind.Semicolon) && !PeekKind(TokenKind.CloseBrace))
        {
            var t = Peek();
            if (t.Kind != TokenKind.Identifier)
            {
                _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                    $"Expected status item attribute (command, key), got '{t.Value}'", t.Line, t.Column));
                Advance();
                continue;
            }
            switch (t.Value)
            {
                case "command":
                    Advance();
                    if (!ExpectPunct(TokenKind.Equals, "'=' after command")) break;
                    var cmdTok = Expect(TokenKind.Identifier, "command identifier");
                    decl.Command = cmdTok?.Value;
                    break;
                case "key":
                    Advance();
                    if (!ExpectPunct(TokenKind.Equals, "'=' after key")) break;
                    var keyTok = Expect(TokenKind.StringLiteral, "key string (e.g. \"F1\")");
                    decl.Key = keyTok?.Value;
                    break;
                default:
                    _diag.Add(new Diagnostic(DiagnosticCodes.UnexpectedToken,
                        $"Unknown status item attribute '{t.Value}'. Expected: command, key.",
                        t.Line, t.Column));
                    Advance();
                    break;
            }
        }

        ExpectPunct(TokenKind.Semicolon, "';' at end of status item");
        return decl;
    }
}

