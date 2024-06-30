// svhc — SharpVision Help Compiler.
//
// Ports tvision/examples/tvhc/tvhc.cc to C# against the SharpVision
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
//   svhc <input>[.txt] [<output>[.hlp] [<symfile>[.cs]]]
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
using SharpVision;

namespace SharpVision.HelpCompiler;

internal static class Program
{
    private const int HelpCounterStart = 2; // tvhc.cc:449 — "1 is hcDragging"
    private const byte NonBreakingSpace = 0xFF;
    private const char CommandChar = '.';

    private static int Main(string[] args)
    {
        // Register help-related streamable types in the current Pstream
        // registry.  THelpFile.RegisterStreamableTypes() uses explicit
        // Pstream.RegisterType() calls so it is safe even if called after
        // a previous Pstream.DeInitTypes() (static field initializers only
        // run once, but RegisterType can be called any number of times).
        THelpFile.RegisterStreamableTypes();

        Console.WriteLine(
            "SharpVision Help Compiler  (port of Borland TVHC, 1991)\n");

        if (args.Length < 1)
        {
            Console.WriteLine(
                "  Syntax  svhc <input>[.txt] [<output>[.hlp] [<symfile>[.cs]]]\n\n" +
                "     input    Help source (.topic / paragraph / {cross-ref} syntax)\n" +
                "     output   Compiled help file (.hlp, 'FBHF' magic)\n" +
                "     symfile  Generated C# class with hcXxx constants\n");
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

        try
        {
            // Read the entire source. Latin-1 keeps CP437 line-art bytes
            // round-trip clean (matches tvhc.cc which copies bytes verbatim).
            string[] lines = File.ReadAllLines(textName, Encoding.Latin1);

            var symbolTable = ScanSymbols(lines, textName);
            CompileTopics(lines, helpName, symbolTable, textName);
            WriteSymbolFile(symbName, symbolTable);

            Console.WriteLine(
                $"Compiled {symbolTable.OrderedDefinitions.Count} topic" +
                $" definition(s); wrote {helpName} and {symbName}.");
            return 0;
        }
        catch (CompileException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
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
                throw Err(sourceName, lineNo, "TOPIC expected");

            // Comma-separated symbol list (tvhc.cc:topicDefinitionList).
            while (true)
            {
                string sym = GetWord(raw, ref i);
                if (sym.Length == 0)
                    throw Err(sourceName, lineNo, "expected topic definition");

                int probe = i;
                string maybeEq = GetWord(raw, ref probe);
                if (maybeEq == "=")
                {
                    i = probe;
                    string num = GetWord(raw, ref i);
                    if (!IsNumeric(num))
                        throw Err(sourceName, lineNo, "expected numeric");
                    counter = int.Parse(num);
                }
                else
                {
                    counter++;
                }

                if (table.Map.ContainsKey(sym))
                    throw Err(sourceName, lineNo, $"redefinition of {sym}");
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
        string[] lines, string helpName, SymbolTable symbols, string sourceName)
    {
        // Truncate (tvhc.cc opens helpStrm with CLY_IOSOut|CLY_IOSBin).
        if (File.Exists(helpName)) File.Delete(helpName);

        var fp = new Fpstream(helpName);
        var helpFile = new THelpFile(fp);
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
                    throw Err(sourceName, idx, "expected '.topic'");

                List<ushort> ids = ParseTopicHeaderIds(header, idx, sourceName, symbols);
                idx++;

                var topic = new THelpTopic();
                int byteOffset = 0;
                var refs = new List<(string Target, int Offset, byte Length, int LineNo)>();

                while (idx < lines.Length)
                {
                    // Read paragraphs until next '.topic' or EOF.
                    if (idx < lines.Length && lines[idx].Length > 0 &&
                        lines[idx][0] == CommandChar) break;

                    var para = ReadParagraph(
                        lines, ref idx, ref byteOffset, refs, sourceName);
                    if (para != null) topic.AddParagraph(para);
                    else if (idx < lines.Length && lines[idx].Length > 0 &&
                             lines[idx][0] == CommandChar) break;
                }

                // Resolve cross-refs by symbol → id (forward refs allowed
                // because pass 1 has populated the table).
                topic.SetNumCrossRefs(refs.Count);
                for (int r = 0; r < refs.Count; r++)
                {
                    var (target, offset, length, lineNo) = refs[r];
                    if (!symbols.Map.TryGetValue(target, out ushort id))
                    {
                        Console.Error.WriteLine(
                            $"Warning: {sourceName}({lineNo + 1}): " +
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
                foreach (ushort id in ids)
                    helpFile.RecordPositionInIndex(id);
                helpFile.PutTopic(topic);
            }
        }
        finally
        {
            helpFile.Flush();
            fp.Close();
        }
    }

    private static List<ushort> ParseTopicHeaderIds(
        string header, int lineNo, string sourceName, SymbolTable symbols)
    {
        var ids = new List<ushort>();
        int i = 0;
        if (GetWord(header, ref i) != ".")
            throw Err(sourceName, lineNo, "expected '.'");
        if (GetWord(header, ref i).ToUpperInvariant() != "TOPIC")
            throw Err(sourceName, lineNo, "TOPIC expected");

        while (true)
        {
            string sym = GetWord(header, ref i);
            if (sym.Length == 0)
                throw Err(sourceName, lineNo, "expected topic definition");

            int probe = i;
            if (GetWord(header, ref probe) == "=")
            {
                i = probe;
                _ = GetWord(header, ref i); // consume numeric
            }
            ids.Add(symbols.Map[sym]);

            int probe2 = i;
            if (GetWord(header, ref probe2) != ",") break;
            i = probe2;
        }
        return ids;
    }

    // -------------------------------------------------------------------
    // Paragraph reader (tvhc.cc:readParagraph).
    // -------------------------------------------------------------------

    private enum WrapState { Undefined, Wrapping, NotWrapping }

    private static TParagraph ReadParagraph(
        string[] lines, ref int idx, ref int byteOffset,
        List<(string Target, int Offset, byte Length, int LineNo)> refs,
        string sourceName)
    {
        // Skip leading blank lines (they belong to the previous para's tail
        // in TVHC; here we treat them as paragraph separators by returning
        // null when only blanks remain before next '.topic').
        var buffer = new MemoryStream();
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

            // Encode to Latin-1, scan + strip cross-refs, and append.
            byte[] encoded = Encoding.Latin1.GetBytes(line);
            byte[] stripped = ScanForCrossRefs(
                encoded, paragraphStartByte + (int)buffer.Length,
                refs, idx, sourceName);
            buffer.Write(stripped, 0, stripped.Length);

            // tvhc.cc:addToBuffer appends '\n' for non-wrap and ' ' for wrap.
            buffer.WriteByte((byte)(state == WrapState.Wrapping ? ' ' : '\n'));
            idx++;
        }

        if (buffer.Length == 0) return null;
        if (buffer.Length > ushort.MaxValue)
            throw Err(sourceName, idx, "paragraph too long");

        byte[] text = buffer.ToArray();
        byteOffset += text.Length;
        return new TParagraph
        {
            text = text,
            size = (ushort)text.Length,
            wrap = state == WrapState.Wrapping,
            next = null,
        };
    }

    // -------------------------------------------------------------------
    // Cross-ref scanner (tvhc.cc:scanForCrossRefs).
    // -------------------------------------------------------------------

    private static byte[] ScanForCrossRefs(
        byte[] line, int paragraphStartOffset,
        List<(string, int, byte, int)> refs, int lineNo, string sourceName)
    {
        var output = new List<byte>(line.Length);
        int i = 0;
        while (i < line.Length)
        {
            byte b = line[i];
            if (b != (byte)'{')
            {
                output.Add(b);
                i++;
                continue;
            }

            // '{{' → literal '{'.
            if (i + 1 < line.Length && line[i + 1] == (byte)'{')
            {
                output.Add((byte)'{');
                i += 2;
                continue;
            }

            // Find matching '}'.
            int end = -1;
            int colon = -1;
            for (int j = i + 1; j < line.Length; j++)
            {
                if (line[j] == (byte)':' && colon < 0) colon = j;
                if (line[j] == (byte)'}') { end = j; break; }
            }
            if (end < 0)
                throw Err(sourceName, lineNo, "unterminated topic reference");

            int textStart = i + 1;
            int textEnd, target;
            string targetSym;
            if (colon > 0 && colon < end)
            {
                textEnd = colon;
                target = colon + 1;
                targetSym = Encoding.Latin1.GetString(
                    line, target, end - target);
            }
            else
            {
                textEnd = end;
                targetSym = Encoding.Latin1.GetString(
                    line, textStart, end - textStart);
            }

            int visibleLen = textEnd - textStart;
            int hotspotOffset = paragraphStartOffset + output.Count;
            for (int k = textStart; k < textEnd; k++)
            {
                byte c = line[k];
                output.Add(c == (byte)' ' ? NonBreakingSpace : c);
            }
            refs.Add((targetSym, hotspotOffset, (byte)visibleLen, lineNo));
            i = end + 1;
        }
        return output.ToArray();
    }

    // -------------------------------------------------------------------
    // Symbol-header writer — emits a C# class instead of a C header
    // (tvhc.cc:writeSymbFile).
    // -------------------------------------------------------------------

    private static void WriteSymbolFile(string symbName, SymbolTable symbols)
    {
        using var w = new StreamWriter(symbName, append: false, Encoding.UTF8);
        w.WriteLine("// <auto-generated> — produced by svhc.");
        w.WriteLine("// SharpVision Help Compiler symbol output.");
        w.WriteLine();
        w.WriteLine("namespace SharpVision;");
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
        public CompileException(string message) : base(message) { }
    }

    private static CompileException Err(string source, int lineNo, string text)
        => new($"Error: {source}({lineNo + 1}): {text}");
}
