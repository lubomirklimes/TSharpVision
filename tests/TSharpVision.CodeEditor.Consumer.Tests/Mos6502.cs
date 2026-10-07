using TSharpVision.CodeEditor.Syntax;

namespace TSharpVision.CodeEditor.Consumer.Tests;

/// <summary>
/// The example an application would write: 6502 assembly, a language TSharpVision does not know, with a small
/// classifier of its own. Nothing here is part of the TSharpVision language catalog.
/// </summary>
public static class Mos6502
{
    public static readonly SyntaxLanguage Language = new("mos6502", "6502 Assembly");

    /// <summary>The language as an application registers it. <c>.asm</c> is also claimed by a built-in language.</summary>
    public static SyntaxLanguageDefinition Definition(int priority = 0) => new(Language)
    {
        Aliases = new[] { "6502", "6502asm", "asm65" },
        FileExtensions = new[] { ".a65", ".asm", "s65" },
        FileNames = new[] { "kernel.6502" },
        FileNamePatterns = new[] { "*.6502.src" },
        FirstLinePattern = @"(?i)^\s*;\s*cpu\s*[:=]?\s*6502\b",
        Priority = priority,
    };

    /// <summary>
    /// Classifies one line of 6502 assembly: <c>;</c> comments, mnemonics, <c>$hex</c>, <c>%binary</c> and decimal
    /// numbers, strings, <c>.directives</c> and <c>labels:</c>. A line depends on nothing before it.
    /// </summary>
    public sealed class Classifier : ISyntaxClassifier
    {
        private static readonly HashSet<string> Mnemonics = new(StringComparer.OrdinalIgnoreCase)
        {
            "ADC", "AND", "ASL", "BCC", "BCS", "BEQ", "BIT", "BMI", "BNE", "BPL", "BRK", "BVC", "BVS", "CLC", "CLD",
            "CLI", "CLV", "CMP", "CPX", "CPY", "DEC", "DEX", "DEY", "EOR", "INC", "INX", "INY", "JMP", "JSR", "LDA",
            "LDX", "LDY", "LSR", "NOP", "ORA", "PHA", "PHP", "PLA", "PLP", "ROL", "ROR", "RTI", "RTS", "SBC", "SEC",
            "SED", "SEI", "STA", "STX", "STY", "TAX", "TAY", "TSX", "TXA", "TXS", "TYA",
        };

        public SyntaxLanguage Language => Mos6502.Language;

        public SyntaxLineState InitialState => LineState.Instance;

        public SyntaxLineResult ClassifyLine(string lineText, SyntaxLineState startState)
        {
            var spans = new List<SyntaxSpan>();
            int i = 0;
            while (i < lineText.Length)
            {
                char c = lineText[i];
                int start = i;

                if (c == ';')
                {
                    spans.Add(new SyntaxSpan(i, lineText.Length - i, SyntaxClass.Comment));
                    break;
                }

                if (c == '"')
                {
                    int close = lineText.IndexOf('"', i + 1);
                    i = close < 0 ? lineText.Length : close + 1;
                    spans.Add(new SyntaxSpan(start, i - start, SyntaxClass.String));
                }
                else if (c == '$' || c == '%' || char.IsAsciiDigit(c))
                {
                    i++;
                    while (i < lineText.Length && char.IsAsciiHexDigit(lineText[i])) i++;
                    spans.Add(new SyntaxSpan(start, i - start, SyntaxClass.Number));
                }
                else if (c == '.' || c == '_' || char.IsAsciiLetter(c))
                {
                    i++;
                    while (i < lineText.Length && (lineText[i] == '_' || char.IsAsciiLetterOrDigit(lineText[i]))) i++;
                    string word = lineText[start..i];
                    if (c == '.')
                    {
                        spans.Add(new SyntaxSpan(start, i - start, SyntaxClass.Preprocessor));
                    }
                    else if (i < lineText.Length && lineText[i] == ':')
                    {
                        i++;
                        spans.Add(new SyntaxSpan(start, i - start, SyntaxClass.Function));
                    }
                    else if (Mnemonics.Contains(word))
                    {
                        spans.Add(new SyntaxSpan(start, i - start, SyntaxClass.Keyword));
                    }
                }
                else
                {
                    i++;
                }
            }

            return new SyntaxLineResult(spans, LineState.Instance);
        }

        private sealed class LineState : SyntaxLineState
        {
            public static readonly LineState Instance = new();

            public override bool Equals(object? obj) => obj is LineState;

            public override int GetHashCode() => 6502;
        }
    }
}
