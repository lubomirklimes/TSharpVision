// Paradox-style picture mask matching.
//
// Picture grammar (from tpxvalid.cc):
//   #   = digit
//   ?   = letter (alpha)
//   &   = letter → force uppercase
//   !   = any character → force uppercase
//   @   = any character
//   *   = repeat group: *<count>{...} or *{...}
//   {   = required group begin
//   [   = optional group begin
//   }   = group end (required)
//   ]   = group end (optional)
//   ;   = escape next character (literal)
//   ,   = alternative separator
//   else = literal character match
//
// TVCodePage substitution: TVCodePage::isNumber → char.IsDigit,
//                          TVCodePage::isAlpha  → char.IsLetter,
//                          TVCodePage::toUpper  → char.ToUpper.
namespace TSharpVision;

/// Result codes returned by the internal picture engine.
public enum TPicResult
{
    prComplete,
    prIncomplete,
    prEmpty,
    prError,
    prSyntax,
    prAmbiguous,
    prIncompNoFill,
}

/// Validates input against a Paradox-style picture mask.
public class TPXPictureValidator : TValidator
{
    protected string Pic;

    // Instance state used during a single picture() call — reset at entry.
    private int _index;   // current position in Pic
    private int _jndex;   // current position in input

    public new static readonly string Name = "TPXPictureValidator";
    public override string streamableName => "TPXPictureValidator";

    public static readonly TStreamableClass StreamableClassTPXPictureValidator =
        new TStreamableClass("TPXPictureValidator",
            () => new TPXPictureValidator(StreamableInit.streamableInit), 0);

    /// Constructs a picture validator.
    /// <param name="aPic">The picture mask string.</param>
    /// <param name="autoFill">If true, set VoFill option so the engine
    /// auto-fills literal characters.</param>
    public TPXPictureValidator(string aPic, bool autoFill = false) : base()
    {
        Pic = aPic ?? string.Empty;
        if (autoFill) Options |= VoFill;
        SyntaxCheck();
    }

    protected TPXPictureValidator(StreamableInit _) : base(_) { }

    public override bool IsValidInput(string s, bool suppressFill)
    {
        bool doFill = !suppressFill && (Options & VoFill) != 0;
        return Picture(ref s, doFill) != TPicResult.prError;
    }

    public override bool IsValid(string s)
    {
        string copy = s ?? string.Empty;
        return Picture(ref copy, false) == TPicResult.prComplete;
    }

    private bool IsSpecial(char c) =>
        c == '#' || c == '?' || c == '&' || c == '!' ||
        c == '@' || c == '*' || c == '{' || c == '[' ||
        c == '}' || c == ']' || c == ';' || c == ',';

    // Consume one literal character from input against expected 'ch'.
    // Returns true if matched (and _jndex advanced).
    private bool Consume(string s, char ch, bool doFill)
    {
        if (_jndex < s.Length && char.ToUpperInvariant(s[_jndex]) == char.ToUpperInvariant(ch))
        {
            _jndex++;
            return true;
        }
        if (_jndex >= s.Length && doFill)
        {
            // auto-fill: pretend consumed (caller will append literal)
            _jndex++;
            return true;
        }
        return false;
    }

    // Advance _index past the current group (to matching '}' or ']').
    private void ToGroupEnd(int ch)
    {
        while (_index < Pic.Length)
        {
            char c = Pic[_index++];
            if (c == '{') ToGroupEnd('}');
            else if (c == '[') ToGroupEnd(']');
            else if (c == ';') _index++;   // skip escaped char
            else if (c == ch) return;
        }
    }

    // Skip past a comma or to end of current group.
    private void SkipToComma(int terminator)
    {
        int depth = 0;
        while (_index < Pic.Length)
        {
            char c = Pic[_index];
            if (c == '{' || c == '[') { depth++; _index++; }
            else if ((c == '}' || c == ']') && depth > 0) { depth--; _index++; }
            else if ((c == '}' || c == ']') && depth == 0) return;
            else if (c == ',' && depth == 0) { _index++; return; }
            else if (c == ';') _index += 2;
            else _index++;
        }
    }

    // Calculate count operand for '*'.
    private int CalcTerm(string s)
    {
        if (_index >= Pic.Length) return 0;
        if (!char.IsDigit(Pic[_index])) return -1;  // infinite
        int count = 0;
        while (_index < Pic.Length && char.IsDigit(Pic[_index]))
            count = count * 10 + (Pic[_index++] - '0');
        return count;
    }

    // Handle '*' repeat construct.
    private TPicResult Iteration(string s, bool doFill)
    {
        int saveIndex = _index;
        int saveJndex = _jndex;
        int count = CalcTerm(s);
        if (_index >= Pic.Length || (Pic[_index] != '{' && Pic[_index] != '['))
            return TPicResult.prSyntax;

        char terminator = Pic[_index] == '{' ? '}' : ']';
        _index++;   // skip '{'/'['

        int groupStart = _index;
        bool optional  = terminator == ']';

        TPicResult last = TPicResult.prEmpty;
        int iterations  = 0;

        while (true)
        {
            int beforeGroup = _index;
            _index = groupStart;

            TPicResult r = Scan(s, doFill, terminator);
            if (r == TPicResult.prError || r == TPicResult.prEmpty)
            {
                if (count >= 0 && iterations < count)
                    return optional ? TPicResult.prIncomplete : TPicResult.prError;
                break;
            }
            if (r == TPicResult.prSyntax) return TPicResult.prSyntax;
            last = r;
            iterations++;
            if (count >= 0 && iterations >= count) break;
            if (_jndex >= s.Length && r != TPicResult.prComplete) break;
        }

        // advance _index past the closing terminator
        _index = saveIndex;
        int dummy = CalcTerm(s);
        _index++;   // skip '{'
        ToGroupEnd(terminator);

        if (count >= 0 && iterations < count)
            return optional ? TPicResult.prIncomplete : TPicResult.prError;
        return last == TPicResult.prEmpty ? TPicResult.prEmpty : last;
    }

    // Scan through one group in Pic up to 'terminator' ('}', ']', or 0).
    private TPicResult Scan(string s, bool doFill, int terminator)
    {
        TPicResult result = TPicResult.prEmpty;

        while (_index < Pic.Length)
        {
            char picChar = Pic[_index];

            // Stop at group end
            if (picChar == '}' || picChar == ']')
            {
                if ((int)picChar == terminator) { _index++; break; }
                return TPicResult.prSyntax;
            }
            if (picChar == ',')
            {
                // End of this alternative — consumed
                break;
            }

            _index++;   // consume picture character

            switch (picChar)
            {
                case '#':
                    if (_jndex < s.Length && char.IsDigit(s[_jndex]))
                    {
                        _jndex++;
                        result = (_jndex < s.Length) ? TPicResult.prIncomplete
                                                      : TPicResult.prComplete;
                    }
                    else if (_jndex >= s.Length)
                    {
                        result = TPicResult.prIncomplete;
                        goto Done;
                    }
                    else return TPicResult.prError;
                    break;

                case '?':
                    if (_jndex < s.Length && char.IsLetter(s[_jndex]))
                    {
                        _jndex++;
                        result = (_jndex < s.Length) ? TPicResult.prIncomplete
                                                      : TPicResult.prComplete;
                    }
                    else if (_jndex >= s.Length)
                    {
                        result = TPicResult.prIncomplete;
                        goto Done;
                    }
                    else return TPicResult.prError;
                    break;

                case '&':
                    if (_jndex < s.Length && char.IsLetter(s[_jndex]))
                    {
                        _jndex++;
                        result = (_jndex < s.Length) ? TPicResult.prIncomplete
                                                      : TPicResult.prComplete;
                    }
                    else if (_jndex >= s.Length)
                    {
                        result = TPicResult.prIncomplete;
                        goto Done;
                    }
                    else return TPicResult.prError;
                    break;

                case '!':
                case '@':
                    if (_jndex < s.Length)
                    {
                        _jndex++;
                        result = (_jndex < s.Length) ? TPicResult.prIncomplete
                                                      : TPicResult.prComplete;
                    }
                    else if (_jndex >= s.Length)
                    {
                        result = TPicResult.prIncomplete;
                        goto Done;
                    }
                    break;

                case '*':
                {
                    TPicResult r = Iteration(s, doFill);
                    if (r == TPicResult.prSyntax) return TPicResult.prSyntax;
                    if (r == TPicResult.prError)  return TPicResult.prError;
                    if (r != TPicResult.prEmpty)  result = r;
                    break;
                }

                case '{':
                {
                    TPicResult r = Scan(s, doFill, '}');
                    if (r == TPicResult.prSyntax) return TPicResult.prSyntax;
                    if (r == TPicResult.prError)  return TPicResult.prError;
                    if (r != TPicResult.prEmpty)  result = r;
                    break;
                }

                case '[':
                {
                    // Optional group: prError becomes prComplete
                    TPicResult r = Scan(s, doFill, ']');
                    if (r == TPicResult.prSyntax) return TPicResult.prSyntax;
                    if (r == TPicResult.prError)
                    {
                        // Rewind input to before optional group
                        result = TPicResult.prIncomplete;
                    }
                    else if (r != TPicResult.prEmpty)
                    {
                        result = r;
                    }
                    break;
                }

                case ';':
                    // Escape: next pic char is literal
                    if (_index >= Pic.Length) return TPicResult.prSyntax;
                    {
                        char literal = Pic[_index++];
                        if (_jndex < s.Length &&
                            char.ToUpperInvariant(s[_jndex]) == char.ToUpperInvariant(literal))
                        {
                            _jndex++;
                            result = (_jndex < s.Length) ? TPicResult.prIncomplete
                                                          : TPicResult.prComplete;
                        }
                        else if (_jndex >= s.Length)
                        {
                            if (doFill) _jndex++;
                            result = TPicResult.prIncomplete;
                            goto Done;
                        }
                        else return TPicResult.prError;
                    }
                    break;

                default:
                    // Literal character match (case-insensitive)
                    if (_jndex < s.Length &&
                        char.ToUpperInvariant(s[_jndex]) == char.ToUpperInvariant(picChar))
                    {
                        _jndex++;
                        result = (_jndex < s.Length) ? TPicResult.prIncomplete
                                                      : TPicResult.prComplete;
                    }
                    else if (_jndex >= s.Length)
                    {
                        if (doFill) _jndex++;
                        result = TPicResult.prIncomplete;
                        goto Done;
                    }
                    else return TPicResult.prError;
                    break;
            }

            // If we've consumed all input characters and there are more
            // picture characters, the result is incomplete.
            if (_jndex >= s.Length && _index < Pic.Length)
            {
                if (result == TPicResult.prComplete)
                    result = TPicResult.prIncomplete;
            }
        }

    Done:
        return result;
    }

    // Handle comma-separated alternatives (top-level or within a group).
    private TPicResult Process(string s, bool doFill)
    {
        int altStart = _index;
        TPicResult best = TPicResult.prError;

        while (true)
        {
            _index = altStart;
            int saveJ = _jndex;
            int picLen = Pic.Length;

            TPicResult r = Scan(s, doFill, 0 /* no group terminator */);

            if (r == TPicResult.prSyntax) return TPicResult.prSyntax;

            // Prefer better results
            if (r == TPicResult.prComplete)
            {
                return TPicResult.prComplete;
            }
            if ((int)r > (int)best)
            {
                best = r;
            }
            _jndex = saveJ;

            // Skip to next alternative
            _index = altStart;
            SkipToComma(0);
            if (_index >= Pic.Length || Pic[_index - 1] != ',')
                break;   // no more alternatives
            altStart = _index;
        }

        return best;
    }

    /// Runs the picture engine against the supplied input string.
    public TPicResult Picture(ref string s, bool doFill)
    {
        if (Status == VsSyntax) return TPicResult.prSyntax;
        if (Pic.Length == 0)   return TPicResult.prComplete;

        _index = 0;
        _jndex = 0;

        return Process(s, doFill);
    }

    private void SyntaxCheck()
    {
        // A simplified syntax check: verify group delimiters are balanced.
        int depth = 0;
        bool ok   = true;
        int i = 0;
        while (i < Pic.Length && ok)
        {
            char c = Pic[i++];
            if (c == ';') { i++; continue; }   // skip escaped char
            if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']')
            {
                if (depth == 0) ok = false;
                else depth--;
            }
        }
        if (!ok || depth != 0) Status = VsSyntax;
    }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteString(Pic);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        Pic = isStream.ReadString() ?? string.Empty;
        SyntaxCheck();
        return this;
    }

    public new static TStreamable Build() =>
        new TPXPictureValidator(StreamableInit.streamableInit);
}
