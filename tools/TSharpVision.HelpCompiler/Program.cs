// svhc — TSharpVision Help Compiler.
//
// Ports tvision/examples/tvhc/tvhc.cc to C# against the TSharpVision
// runtime (THelpFile / THelpTopic / TParagraph / TCrossRef / Fpstream).
//
// Differences from upstream TVHC:
//   * Two-pass design — pass 1 scans every `.topic` line and assigns IDs;
//     pass 2 emits topics with cross-refs already resolved to numeric ids.
//     This avoids upstream's stream back-patch (`TFixUp` / `seekp`).
//   * Symbol header is emitted as a C# `static class HelpCtx`
//     (`public const ushort hc... = N;`) instead of a C `const int` block.
//
// Usage:
//   svhc [--format v1|v2] <input>[.txt] [<output>[.hlp] [<symfile>[.cs]]]
//
// Syntax (compatible with tvision/examples/tvhc/demohelp.txt):
//   .topic Sym[=N][, Sym[=N]...]      topic header
//   <text>                            paragraph body
//   {visible[:alias]}                 cross-reference; '{{' is a literal '{'
//
// Wrapping rule (tvhc.cc:isEndParagraph):
//   * a paragraph that begins flush-left is wrapped at runtime;
//   * a paragraph that begins with a leading space is preformatted.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TSharpVision;
using TSharpVision.Text;

namespace TSharpVision.HelpCompiler;

internal static class Program
{
    private const int HelpCounterStart = 2; // tvhc.cc:449 — "1 is hcDragging"
    private const char NonBreakingSpace = '\xFF';
    private const char CommandChar = '.';

    private static int Main(string[] args)
    {
        // Register help-related streamable types in the current Pstream
        // registry.  THelpFile.RegisterStreamableTypes() uses explicit
        // Pstream.RegisterType() calls so it is safe even if called after
        // a previous Pstream.DeInitTypes() (static field initializers only
        // run once, but RegisterType can be called any number of times).
        THelpFile.RegisterStreamableTypes();

        CompilerOptions options;
        try
        {
            options = CompilerOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        args = options.Positional.ToArray();

        if (!options.Quiet)
            Console.WriteLine(
                "TSharpVision Help Compiler  (port of Borland TVHC, 1991)\n");

        if (args.Length < 1)
        {
            if (!options.Quiet)
                Console.WriteLine(
                    "  Syntax  svhc [--format v1|v2] [--encoding name] [--warn-as-error] [--quiet] [--no-color] <input>[.txt] [<output>[.hlp] [<symfile>[.cs]]]\n\n" +
                    "     input    Help source (.topic / paragraph / {cross-ref} syntax)\n" +
                    "     output   Compiled help file (.hlp, 'FBHF' v1 or 'FBH2' v2 magic)\n" +
                    "     symfile  Generated C# class with hcXxx constants\n" +
                    "     format   v1 = byte/legacy TVHC subset, v2 = UTF-16 plus extensions (default)\n" +
                    "     encoding v1 source/output encoding: latin1, cp852, kamenicky, ... (default latin1)\n");
            return 1;
        }

        string textName = ReplaceExt(args[0], ".txt", force: false);
        string helpName = args.Length >= 2
            ? ReplaceExt(args[1], ".hlp", force: false)
            : ReplaceExt(textName, ".hlp", force: true);
        string symbName = args.Length >= 3
            ? ReplaceExt(args[2], ".cs", force: false)
            : ReplaceExt(helpName, ".cs", force: true);

        if (!File.Exists(textName))
        {
            Console.Error.WriteLine($"Error: file {textName} not found.");
            return 1;
        }

        var diagnostics = new DiagnosticSink(options);
        try
        {
            string[] lines = ReadSourceLines(textName, options);

            var symbolTable = ScanSymbols(lines, textName);
            CompileTopics(lines, helpName, symbolTable, textName, diagnostics, options);
            if (diagnostics.HasPromotedWarnings)
                return 1;
            WriteSymbolFile(symbName, symbolTable);

            if (!options.Quiet)
                Console.WriteLine(
                    $"Compiled {symbolTable.OrderedDefinitions.Count} topic" +
                    $" definition(s); wrote {helpName} and {symbName}.");
            return 0;
        }
        catch (CompileException ex)
        {
            diagnostics.Report(ex.Diagnostic);
            return 1;
        }
    }

    private sealed class CompilerOptions
    {
        public readonly List<string> Positional = new();
        public bool WarnAsError;
        public bool Quiet;
        public int HelpFormatVersion = THelpFile.FormatV2Utf16;
        public bool LegacyEncodingSpecified;
        public ILegacyTextEncoding LegacyEncoding = LegacyTextEncodings.Latin1;

        public static CompilerOptions Parse(string[] args)
        {
            var options = new CompilerOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--warn-as-error":
                        options.WarnAsError = true;
                        break;
                    case "--quiet":
                        options.Quiet = true;
                        break;
                    case "--no-color":
                        break;
                    case "--v1":
                        options.HelpFormatVersion = THelpFile.FormatV1Latin1;
                        break;
                    case "--v2":
                        options.HelpFormatVersion = THelpFile.FormatV2Utf16;
                        break;
                    case "--format":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--format requires v1 or v2");
                        options.HelpFormatVersion = ParseFormat(args[++i]);
                        break;
                    case "--encoding":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--encoding requires an encoding name");
                        options.SetLegacyEncoding(args[++i]);
                        break;
                    default:
                        if (arg.StartsWith("--format=", StringComparison.Ordinal))
                            options.HelpFormatVersion = ParseFormat(arg.Substring("--format=".Length));
                        else if (arg.StartsWith("--encoding=", StringComparison.Ordinal))
                            options.SetLegacyEncoding(arg.Substring("--encoding=".Length));
                        else
                            options.Positional.Add(arg);
                        break;
                }
            }
            if (options.LegacyEncodingSpecified
                && options.HelpFormatVersion != THelpFile.FormatV1Latin1)
            {
                throw new ArgumentException("--encoding applies only when compiling --format v1");
            }
            return options;
        }

        private static int ParseFormat(string value)
        {
            if (value.Equals("v1", StringComparison.OrdinalIgnoreCase))
                return THelpFile.FormatV1Latin1;
            if (value.Equals("v2", StringComparison.OrdinalIgnoreCase))
                return THelpFile.FormatV2Utf16;
            throw new ArgumentException("--format requires v1 or v2");
        }

        private void SetLegacyEncoding(string name)
        {
            if (!LegacyTextEncodings.TryGet(name, out var encoding))
                throw new ArgumentException($"unknown legacy encoding '{name}'");

            LegacyEncoding = encoding;
            LegacyEncodingSpecified = true;
        }
    }

    private static string[] ReadSourceLines(string textName, CompilerOptions options)
    {
        if (options.HelpFormatVersion == THelpFile.FormatV1Latin1)
        {
            byte[] bytes = File.ReadAllBytes(textName);
            int offset = bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF
                    ? 3
                    : 0;
            string text = options.LegacyEncoding.Decode(bytes.AsSpan(offset));
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return text.Split('\n');
        }

        return File.ReadAllLines(textName, Encoding.UTF8);
    }

    private enum DiagnosticSeverity
    {
        Warning,
        Error,
    }

    private readonly record struct Diagnostic(
        string Code,
        DiagnosticSeverity Severity,
        string File,
        int Line,
        int Column,
        string Message)
    {
        public Diagnostic WithSeverity(DiagnosticSeverity severity) =>
            this with { Severity = severity };

        public override string ToString()
        {
            string severity = Severity == DiagnosticSeverity.Error
                ? "error"
                : "warning";
            return $"{File}({Line},{Column}): {severity} {Code}: {Message}";
        }
    }

    private sealed class DiagnosticSink
    {
        private readonly CompilerOptions _options;
        public bool HasPromotedWarnings { get; private set; }

        public DiagnosticSink(CompilerOptions options) => _options = options;

        public void Warning(string source, int lineNo, int column, string code, string message)
        {
            var severity = _options.WarnAsError
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;
            if (_options.WarnAsError)
                HasPromotedWarnings = true;
            Report(new Diagnostic(
                code,
                severity,
                source,
                lineNo + 1,
                Math.Max(1, column),
                message));
        }

        public void Report(Diagnostic diagnostic) =>
            Console.Error.WriteLine(diagnostic.ToString());
    }

    // -------------------------------------------------------------------
    // Pass 1 — scan `.topic` lines and assign IDs (tvhc.cc:topicDefinition).
    // -------------------------------------------------------------------

    private sealed class SymbolTable
    {
        public readonly Dictionary<string, ushort> Map =
            new(StringComparer.Ordinal);
        public readonly List<KeyValuePair<string, ushort>> OrderedDefinitions =
            new();
    }

    private static SymbolTable ScanSymbols(string[] lines, string sourceName)
    {
        var table = new SymbolTable();
        int counter = HelpCounterStart;

        for (int lineNo = 0; lineNo < lines.Length; lineNo++)
        {
            string raw = lines[lineNo];
            int i = 0;
            string firstWord = GetWord(raw, ref i);
            if (firstWord != ".") continue;

            string keyword = GetWord(raw, ref i).ToUpperInvariant();
            if (keyword != "TOPIC")
                continue;

            // Comma-separated symbol list (tvhc.cc:topicDefinitionList).
            while (true)
            {
                string sym = GetWord(raw, ref i);
                if (sym.Length == 0)
                    throw Err(sourceName, lineNo, 1, "SVHC0001", "expected topic definition");

                int probe = i;
                string maybeEq = GetWord(raw, ref probe);
                if (maybeEq == "=")
                {
                    i = probe;
                    string num = GetWord(raw, ref i);
                    if (!IsNumeric(num))
                        throw Err(sourceName, lineNo, 1, "SVHC0001", "expected numeric");
                    counter = int.Parse(num);
                }
                else
                {
                    counter++;
                }

                if (table.Map.ContainsKey(sym))
                    throw Err(sourceName, lineNo, 1, "SVHC0002", $"redefinition of {sym}");
                ushort value = checked((ushort)counter);
                table.Map.Add(sym, value);
                table.OrderedDefinitions.Add(
                    new KeyValuePair<string, ushort>(sym, value));

                int probe2 = i;
                string sep = GetWord(raw, ref probe2);
                if (sep != ",") break;
                i = probe2;
            }
        }
        return table;
    }

    // -------------------------------------------------------------------
    // Pass 2 — emit topics + cross-refs into the .hlp file.
    // -------------------------------------------------------------------

    private static void CompileTopics(
        string[] lines, string helpName, SymbolTable symbols, string sourceName,
        DiagnosticSink diagnostics, CompilerOptions options)
    {
        // Truncate (tvhc.cc opens helpStrm with CLY_IOSOut|CLY_IOSBin).
        if (File.Exists(helpName)) File.Delete(helpName);

        var fp = new Fpstream(helpName);
        var helpFile = new THelpFile(
            fp,
            options.HelpFormatVersion,
            new HelpV1CompileOptions { LegacyEncoding = options.LegacyEncoding });
        try
        {
            int idx = 0;
            while (idx < lines.Length)
            {
                // Skip blank lines.
                while (idx < lines.Length && lines[idx].Length == 0) idx++;
                if (idx >= lines.Length) break;

                string header = lines[idx];
                if (header.Length == 0 || header[0] != CommandChar)
                    throw Err(sourceName, idx, 1, "SVHC0001", "expected '.topic'");

                if (!IsTopicDirective(header))
                {
                    ReportUnknownDirective(sourceName, idx, diagnostics, options, header);
                    idx++;
                    continue;
                }

                List<KeyValuePair<string, ushort>> definitions =
                    ParseTopicHeaderDefinitions(header, idx, sourceName, symbols);
                idx++;

                var topic = new THelpTopic();
                int byteOffset = 0;
                var refs = new List<(string Target, int Offset, byte Length, int LineNo)>();

                while (idx < lines.Length)
                {
                    // Read paragraphs until next '.topic' or EOF.
                    if (idx < lines.Length && lines[idx].Length > 0 &&
                        lines[idx][0] == CommandChar)
                    {
                        if (IsTopicDirective(lines[idx])) break;
                        ReportUnknownDirective(sourceName, idx, diagnostics, options, lines[idx]);
                        idx++;
                        continue;
                    }

                    var para = ReadParagraph(
                        lines, ref idx, ref byteOffset, refs, sourceName, options);
                    if (para != null) topic.AddParagraph(para);
                    else if (idx < lines.Length && lines[idx].Length > 0 &&
                             lines[idx][0] == CommandChar)
                    {
                        if (IsTopicDirective(lines[idx])) break;
                        ReportUnknownDirective(sourceName, idx, diagnostics, options, lines[idx]);
                        idx++;
                    }
                }

                if (options.HelpFormatVersion == THelpFile.FormatV2Utf16
                    && TopicHasDefinition(definitions, "Index")
                    && topic.paragraphs == null)
                    PopulateGeneratedIndexTopic(
                        topic, symbols, refs, ref byteOffset, sourceName, idx);

                // Resolve cross-refs by symbol → id (forward refs allowed
                // because pass 1 has populated the table).
                topic.SetNumCrossRefs(refs.Count);
                for (int r = 0; r < refs.Count; r++)
                {
                    var (target, offset, length, lineNo) = refs[r];
                    if (!symbols.Map.TryGetValue(target, out ushort id))
                    {
                        diagnostics.Warning(
                            sourceName,
                            lineNo,
                            1,
                            "SVHC0004",
                            $"unresolved cross-reference \"{target}\"");
                        id = 0; // hcNoContext → invalid-context placeholder
                    }
                    topic.SetCrossRef(r, new TCrossRef
                    {
                        @ref = id,
                        offset = offset,
                        length = length,
                    });
                }

                // Each id in `ids` points at the same on-disk topic offset
                // (tvhc.cc:recordTopicDefinitions).
                foreach (var definition in definitions)
                    helpFile.RecordPositionInIndex(definition.Value);
                helpFile.PutTopic(topic);
            }
        }
        finally
        {
            helpFile.Flush();
            fp.Close();
        }
    }

    private static List<KeyValuePair<string, ushort>> ParseTopicHeaderDefinitions(
        string header, int lineNo, string sourceName, SymbolTable symbols)
    {
        var definitions = new List<KeyValuePair<string, ushort>>();
        int i = 0;
        if (GetWord(header, ref i) != ".")
            throw Err(sourceName, lineNo, 1, "SVHC0001", "expected '.'");
        if (GetWord(header, ref i).ToUpperInvariant() != "TOPIC")
            throw Err(sourceName, lineNo, 1, "SVHC0001", "TOPIC expected");

        while (true)
        {
            string sym = GetWord(header, ref i);
            if (sym.Length == 0)
                throw Err(sourceName, lineNo, 1, "SVHC0001", "expected topic definition");

            int probe = i;
            if (GetWord(header, ref probe) == "=")
            {
                i = probe;
                _ = GetWord(header, ref i); // consume numeric
            }
            definitions.Add(new KeyValuePair<string, ushort>(sym, symbols.Map[sym]));

            int probe2 = i;
            if (GetWord(header, ref probe2) != ",") break;
            i = probe2;
        }
        return definitions;
    }

    private static bool TopicHasDefinition(
        List<KeyValuePair<string, ushort>> definitions, string name)
    {
        foreach (var definition in definitions)
            if (definition.Key == name)
                return true;
        return false;
    }

    private static void PopulateGeneratedIndexTopic(
        THelpTopic topic,
        SymbolTable symbols,
        List<(string Target, int Offset, byte Length, int LineNo)> refs,
        ref int byteOffset,
        string sourceName,
        int lineNo)
    {
        var names = new List<string>();
        foreach (var definition in symbols.OrderedDefinitions)
            if (definition.Key != "Index")
                names.Add(definition.Key);
        names.Sort(StringComparer.Ordinal);

        foreach (string name in names)
        {
            var para = ReadGeneratedIndexParagraph(
                name, ref byteOffset, refs, sourceName, lineNo);
            topic.AddParagraph(para);
        }
    }

    private static TParagraph ReadGeneratedIndexParagraph(
        string symbol,
        ref int byteOffset,
        List<(string Target, int Offset, byte Length, int LineNo)> refs,
        string sourceName,
        int lineNo)
    {
        string line = "{" + symbol + "}";
        string text = ScanForCrossRefs(
            line, byteOffset, refs, lineNo, sourceName) + "\n";
        byteOffset += text.Length;
        return new TParagraph
        {
            chars = text.ToCharArray(),
            size = (ushort)text.Length,
            wrap = false,
            next = null,
        };
    }

    private static void ReportUnknownDirective(
        string sourceName,
        int lineNo,
        DiagnosticSink diagnostics,
        CompilerOptions options,
        string line)
    {
        string message = $"unknown directive '{DirectiveName(line)}' (ignored)";
        if (options.HelpFormatVersion == THelpFile.FormatV1Latin1)
            throw Err(sourceName, lineNo, 1, "SVHC0010", message);

        diagnostics.Warning(sourceName, lineNo, 1, "SVHC0010", message);
    }

    // -------------------------------------------------------------------
    // Paragraph reader (tvhc.cc:readParagraph).
    // -------------------------------------------------------------------

    private enum WrapState { Undefined, Wrapping, NotWrapping }

    private static TParagraph ReadParagraph(
        string[] lines, ref int idx, ref int byteOffset,
        List<(string Target, int Offset, byte Length, int LineNo)> refs,
        string sourceName,
        CompilerOptions options)
    {
        // Skip leading blank lines (they belong to the previous para's tail
        // in TVHC; here we treat them as paragraph separators by returning
        // null when only blanks remain before next '.topic').
        var buffer = new StringBuilder();
        var state = WrapState.Undefined;
        int paragraphStartByte = byteOffset;

        // Skip blank lines between paragraphs.
        while (idx < lines.Length && lines[idx].Length == 0) idx++;

        if (idx >= lines.Length) return null;
        if (lines[idx].Length > 0 && lines[idx][0] == CommandChar) return null;

        while (idx < lines.Length)
        {
            string line = lines[idx];
            if (line.Length == 0) break;
            if (line[0] == CommandChar) break;

            bool startsWithSpace = line[0] == ' ';
            if (state == WrapState.Undefined)
                state = startsWithSpace ? WrapState.NotWrapping : WrapState.Wrapping;
            else if (startsWithSpace && state == WrapState.Wrapping) break;
            else if (!startsWithSpace && state == WrapState.NotWrapping) break;

            string stripped = ScanForCrossRefs(
                line, paragraphStartByte + buffer.Length,
                refs, idx, sourceName);
            buffer.Append(stripped);

            // tvhc.cc:addToBuffer appends '\n' for non-wrap and ' ' for wrap.
            buffer.Append(state == WrapState.Wrapping ? ' ' : '\n');
            idx++;
        }

        if (buffer.Length == 0) return null;
        if (buffer.Length > ushort.MaxValue)
            throw Err(sourceName, idx, 1, "SVHC0001", "paragraph too long");

        string text = buffer.ToString();
        ValidateV1Text(text, sourceName, idx, options);
        byteOffset += text.Length;
        return new TParagraph
        {
            chars = text.ToCharArray(),
            size = (ushort)text.Length,
            wrap = state == WrapState.Wrapping,
            next = null,
        };
    }

    private static void ValidateV1Text(
        string text,
        string sourceName,
        int lineNo,
        CompilerOptions options)
    {
        if (options.HelpFormatVersion != THelpFile.FormatV1Latin1)
            return;

        for (int i = 0; i < text.Length; i++)
        {
            if (!options.LegacyEncoding.TryEncodeChar(text[i], out _))
            {
                throw Err(
                    sourceName,
                    lineNo,
                    i + 1,
                    "SVHC0011",
                    $"character U+{(int)text[i]:X4} cannot be encoded as {options.LegacyEncoding.Name}");
            }
        }
    }

    // -------------------------------------------------------------------
    // Cross-ref scanner (tvhc.cc:scanForCrossRefs).
    // -------------------------------------------------------------------

    private static string ScanForCrossRefs(
        string line, int paragraphStartOffset,
        List<(string, int, byte, int)> refs, int lineNo, string sourceName)
    {
        var output = new StringBuilder(line.Length);
        int i = 0;
        while (i < line.Length)
        {
            char ch = line[i];
            if (ch != '{')
            {
                output.Append(ch);
                i++;
                continue;
            }

            // '{{' → literal '{'.
            if (i + 1 < line.Length && line[i + 1] == '{')
            {
                output.Append('{');
                i += 2;
                continue;
            }

            // Find matching '}'.
            int end = -1;
            int colon = -1;
            for (int j = i + 1; j < line.Length; j++)
            {
                if (line[j] == ':' && colon < 0) colon = j;
                if (line[j] == '}') { end = j; break; }
            }
            if (end < 0)
                throw Err(sourceName, lineNo, i + 1, "SVHC0003", "unterminated topic reference");

            int textStart = i + 1;
            int textEnd, target;
            string targetSym;
            if (colon > 0 && colon < end)
            {
                textEnd = colon;
                target = colon + 1;
                targetSym = line.Substring(target, end - target);
            }
            else
            {
                textEnd = end;
                targetSym = line.Substring(textStart, end - textStart);
            }

            int visibleLen = textEnd - textStart;
            int hotspotOffset = paragraphStartOffset + output.Length;
            for (int k = textStart; k < textEnd; k++)
            {
                char c = line[k];
                output.Append(c == ' ' ? NonBreakingSpace : c);
            }
            refs.Add((targetSym, hotspotOffset, (byte)visibleLen, lineNo));
            i = end + 1;
        }
        return output.ToString();
    }

    // -------------------------------------------------------------------
    // Symbol-header writer — emits a C# class instead of a C header
    // (tvhc.cc:writeSymbFile).
    // -------------------------------------------------------------------

    private static void WriteSymbolFile(string symbName, SymbolTable symbols)
    {
        using var w = new StreamWriter(symbName, append: false, Encoding.UTF8);
        w.WriteLine("// <auto-generated> — produced by svhc.");
        w.WriteLine("// TSharpVision Help Compiler symbol output.");
        w.WriteLine();
        w.WriteLine("namespace TSharpVision;");
        w.WriteLine();
        w.WriteLine("public static class HelpCtx");
        w.WriteLine("{");
        foreach (var kv in symbols.OrderedDefinitions)
        {
            w.WriteLine($"    public const ushort hc{kv.Key} = {kv.Value};");
        }
        w.WriteLine("}");
    }

    // -------------------------------------------------------------------
    // Lexer helpers (tvhc.cc:getWord / skipWhite / checkForValidChar).
    // -------------------------------------------------------------------

    private static string GetWord(string line, ref int i)
    {
        // Skip whitespace (' ' and ASCII 8 — tvhc.cc:skipWhite).
        while (i < line.Length && (line[i] == ' ' || line[i] == 8)) i++;
        if (i >= line.Length) return string.Empty;

        int start = i;
        char c = line[i];
        if (IsValidWordChar(c))
        {
            while (i < line.Length && IsValidWordChar(line[i])) i++;
        }
        else
        {
            i++; // single-char punctuation token (".", "=", ",", etc.)
        }
        return line.Substring(start, i - start);
    }

    private static bool IsValidWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    private static bool IsNumeric(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s) if (!char.IsDigit(c)) return false;
        return true;
    }

    // -------------------------------------------------------------------
    // Filename / error helpers.
    // -------------------------------------------------------------------

    private static string ReplaceExt(string fileName, string newExt, bool force)
    {
        string existing = Path.GetExtension(fileName);
        if (force || string.IsNullOrEmpty(existing))
            return Path.ChangeExtension(fileName, newExt);
        return fileName;
    }

    private sealed class CompileException : Exception
    {
        public Diagnostic Diagnostic { get; }

        public CompileException(Diagnostic diagnostic)
            : base(diagnostic.Message)
        {
            Diagnostic = diagnostic;
        }
    }

    private static CompileException Err(
        string source, int lineNo, int column, string code, string text)
        => new(new Diagnostic(
            code,
            DiagnosticSeverity.Error,
            source,
            lineNo + 1,
            Math.Max(1, column),
            text));

    private static bool IsTopicDirective(string line)
    {
        int i = 0;
        if (GetWord(line, ref i) != ".") return false;
        return GetWord(line, ref i).Equals("TOPIC", StringComparison.OrdinalIgnoreCase);
    }

    private static string DirectiveName(string line)
    {
        int i = 0;
        string dot = GetWord(line, ref i);
        string name = dot == "." ? GetWord(line, ref i) : dot;
        return "." + (name.Length == 0 ? "?" : name);
    }
}
