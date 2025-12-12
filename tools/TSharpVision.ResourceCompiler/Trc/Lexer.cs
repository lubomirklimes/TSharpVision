// TSharpVision Resource Compiler
// Lexer for the .trc text resource script language.
using System.Text;
namespace TSharpVision.ResourceCompiler;

// ── Token types ───────────────────────────────────────────────────────────────

public enum TokenKind
{
    // Literals
    Identifier,
    Integer,
    StringLiteral,

    // Keywords (returned as Identifier; parser checks Value)
    // No separate kind — parser does keyword comparison against Value.

    // Punctuation
    Semicolon,       // ;
    OpenBrace,       // {
    CloseBrace,      // }
    OpenParen,       // (
    CloseParen,      // )
    Equals,          // =
    Comma,           // ,

    // Special
    Eof,
}

public sealed class Token
{
    public TokenKind Kind   { get; }
    public string    Value  { get; }
    public int       Line   { get; }
    public int       Column { get; }

    public Token(TokenKind kind, string value, int line, int column)
    {
        Kind   = kind;
        Value  = value;
        Line   = line;
        Column = column;
    }

    public override string ToString() => $"{Kind}({Value}) @{Line}:{Column}";
}

// ── Lexer ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Converts a UTF-8 .trc source string into a flat list of <see cref="Token"/>s.
/// Errors are appended to the supplied <paramref name="diagnostics"/> list;
/// lexing continues past errors so the parser can report as many issues as
/// possible.
/// </summary>
public sealed class Lexer
{
    private readonly string _src;
    private int    _pos;
    private int    _line;
    private int    _col;
    private readonly List<Diagnostic> _diag;

    public Lexer(string source, List<Diagnostic> diagnostics)
    {
        _src  = source ?? string.Empty;
        _pos  = 0;
        _line = 1;
        _col  = 1;
        _diag = diagnostics;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (true)
        {
            SkipWhitespaceAndComments();
            if (_pos >= _src.Length)
            {
                tokens.Add(Tok(TokenKind.Eof, string.Empty));
                break;
            }
            char c = _src[_pos];
            Token t = ReadToken(c);
            if (t != null) tokens.Add(t);
        }
        return tokens;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private char Peek(int offset = 0)
    {
        int i = _pos + offset;
        return i < _src.Length ? _src[i] : '\0';
    }

    private Token Tok(TokenKind kind, string value)
        => new Token(kind, value, _line, _col);

    private Token TokAt(TokenKind kind, string value, int line, int col)
        => new Token(kind, value, line, col);

    private void Advance(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            if (_pos >= _src.Length) break;
            if (_src[_pos] == '\n') { _line++; _col = 1; }
            else { _col++; }
            _pos++;
        }
    }

    // ── Whitespace / comments ─────────────────────────────────────────────────

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _src.Length)
        {
            char c = _src[_pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                Advance();
                continue;
            }
            // Line comment: //
            if (c == '/' && Peek(1) == '/')
            {
                while (_pos < _src.Length && _src[_pos] != '\n') Advance();
                continue;
            }
            // Block comment: /* … */
            if (c == '/' && Peek(1) == '*')
            {
                Advance(2); // consume /*
                while (_pos < _src.Length)
                {
                    if (_src[_pos] == '*' && Peek(1) == '/') { Advance(2); break; }
                    Advance();
                }
                continue;
            }
            break;
        }
    }

    // ── Token dispatch ────────────────────────────────────────────────────────

    private Token ReadToken(char c)
    {
        int startLine = _line, startCol = _col;

        switch (c)
        {
            case ';': Advance(); return TokAt(TokenKind.Semicolon,   ";",  startLine, startCol);
            case '{': Advance(); return TokAt(TokenKind.OpenBrace,   "{",  startLine, startCol);
            case '}': Advance(); return TokAt(TokenKind.CloseBrace,  "}",  startLine, startCol);
            case '(': Advance(); return TokAt(TokenKind.OpenParen,   "(",  startLine, startCol);
            case ')': Advance(); return TokAt(TokenKind.CloseParen,  ")",  startLine, startCol);
            case '=': Advance(); return TokAt(TokenKind.Equals,      "=",  startLine, startCol);
            case ',': Advance(); return TokAt(TokenKind.Comma,       ",",  startLine, startCol);
        }

        if (c == '"') return ReadString(startLine, startCol);
        if (c == '-' || char.IsDigit(c)) return ReadInteger(startLine, startCol);
        if (c == '_' || char.IsLetter(c)) return ReadIdentifier(startLine, startCol);

        // Unknown character — emit diagnostic, skip it.
        _diag.Add(new Diagnostic(
            DiagnosticCodes.InvalidCharacter,
            $"Unexpected character '{c}' (U+{(int)c:X4})",
            startLine, startCol));
        Advance();
        return null;
    }

    // ── String literal ────────────────────────────────────────────────────────

    private Token ReadString(int startLine, int startCol)
    {
        Advance(); // opening "
        var sb = new StringBuilder();
        while (_pos < _src.Length)
        {
            char c = _src[_pos];
            if (c == '"') { Advance(); break; }
            if (c == '\n' || c == '\r')
            {
                _diag.Add(new Diagnostic(
                    DiagnosticCodes.UnterminatedString,
                    "Unterminated string literal (newline inside string)",
                    startLine, startCol));
                break;
            }
            if (c == '\\')
            {
                Advance();
                if (_pos >= _src.Length)
                {
                    _diag.Add(new Diagnostic(
                        DiagnosticCodes.UnterminatedString,
                        "Unterminated escape sequence at end of file",
                        startLine, startCol));
                    break;
                }
                char esc = _src[_pos];
                Advance();
                switch (esc)
                {
                    case '"':  sb.Append('"');  break;
                    case '\\': sb.Append('\\'); break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    default:   sb.Append('\\'); sb.Append(esc); break;
                }
                continue;
            }
            sb.Append(c);
            Advance();
        }
        return TokAt(TokenKind.StringLiteral, sb.ToString(), startLine, startCol);
    }

    // ── Integer literal ───────────────────────────────────────────────────────

    private Token ReadInteger(int startLine, int startCol)
    {
        var sb = new StringBuilder();
        if (_src[_pos] == '-') { sb.Append('-'); Advance(); }
        while (_pos < _src.Length && char.IsDigit(_src[_pos]))
        {
            sb.Append(_src[_pos]);
            Advance();
        }
        return TokAt(TokenKind.Integer, sb.ToString(), startLine, startCol);
    }

    // ── Identifier / keyword ──────────────────────────────────────────────────

    private Token ReadIdentifier(int startLine, int startCol)
    {
        var sb = new StringBuilder();
        while (_pos < _src.Length && (_src[_pos] == '_' || char.IsLetterOrDigit(_src[_pos])))
        {
            sb.Append(_src[_pos]);
            Advance();
        }
        return TokAt(TokenKind.Identifier, sb.ToString(), startLine, startCol);
    }
}
