using System;
using System.IO;
using System.Text;
using TSharpVision.Constants;
using TSharpVision.Text;

namespace TSharpVision;

/// <summary>File-backed editor that tracks encoding and line endings, saves changes, and prompts before discarding modified content.</summary>
public class TFileEditor : TEditor
{
    /// <summary>Filename extension used for backups when the backup-files option is enabled.</summary>
    public static string backupExt = ".bak";

    /// <summary>Associated file path; empty for an unnamed document.</summary>
    public string fileName;
    /// <summary>Line-ending convention detected when content was loaded.</summary>
    public LineEndingKind OriginalLineEnding { get; private set; } = LineEndingKind.Unknown;
    /// <summary>Line-ending convention requested when serializing the editor's LF-normalized text.</summary>
    /// <remarks>
    /// Loading sets the dominant style without counting as a choice. Assigning this property is an explicit choice: a
    /// document loaded with mixed line endings is then saved with the assigned style without the
    /// <see cref="Views.edMixedLineEndings"/> prompt.
    /// </remarks>
    public LineEndingKind SaveLineEnding
    {
        get => saveLineEnding;
        set
        {
            saveLineEnding = value;
            mixedLineEndingChoicePending = false;
        }
    }
    /// <summary>Whether the loaded file contained multiple line-ending conventions.</summary>
    public bool HadMixedLineEndings { get; private set; }
    /// <summary>
    /// Whether the next save asks, through <see cref="TEditor.editorDialog"/> with <see cref="Views.edMixedLineEndings"/>,
    /// which line endings to write. True after loading a file with mixed line endings until a style is chosen by that
    /// prompt or by assigning <see cref="SaveLineEnding"/>; reloading the file starts over.
    /// </summary>
    public bool IsMixedLineEndingChoicePending => mixedLineEndingChoicePending;

    /// <summary>Forgets the remembered line-ending choice so that the next save of a document loaded with mixed line endings asks again; no effect otherwise.</summary>
    public void ResetMixedLineEndingChoice() => mixedLineEndingChoicePending = HadMixedLineEndings;
    /// <summary>Encoding recognized or selected when the document was loaded.</summary>
    public EditorEncodingKind OriginalEncoding { get; private set; } = EditorEncodingKind.Utf8;
    /// <summary>Legacy codec associated with the loaded file, or null for UTF-8.</summary>
    public ILegacyTextEncoding? OriginalLegacyEncoding { get; private set; }
    /// <summary>Whether the loaded file began with a UTF-8 byte-order mark.</summary>
    public bool HadUtf8Bom { get; private set; }
    /// <summary>
    /// Encoding used by the next save, including its byte-order mark. Loading sets it to the loaded encoding; a new or
    /// unnamed document uses UTF-8 without a byte-order mark. Change it with <c>SetSaveEncoding</c>.
    /// </summary>
    public EditorEncodingKind SaveEncoding => saveEncoding;
    /// <summary>Single-byte codec used by the next save when <see cref="SaveEncoding"/> is Latin1 or Legacy; otherwise null.</summary>
    public ILegacyTextEncoding? SaveLegacyEncoding => saveLegacyEncoding;
    private TFileEditorOpenOptions openOptions;
    private LineEndingKind saveLineEnding = DefaultLineEndingForPlatform();
    private bool mixedLineEndingChoicePending;
    private int loadedCrLfCount, loadedLfCount, loadedCrCount;
    private EditorEncodingKind saveEncoding = EditorEncodingKind.Utf8;
    private ILegacyTextEncoding? saveLegacyEncoding;

    private static readonly UTF8Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding StrictUtf16Le =
        new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding StrictUtf16Be =
        new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);

    /// <summary>Creates a file editor at owner-relative cell bounds and loads the path using automatic encoding detection; empty path creates an unnamed document.</summary>
    public TFileEditor(TRect bounds, TScrollBar? aHScrollBar,
                       TScrollBar? aVScrollBar, TIndicator? aIndicator,
                       string? aFileName)
        : this(bounds, aHScrollBar, aVScrollBar, aIndicator, aFileName, null)
    {
    }

    /// <summary>Creates a file editor with associated controls and loads the path using the supplied decoding policy; null options use defaults.</summary>
    public TFileEditor(TRect bounds, TScrollBar? aHScrollBar,
                       TScrollBar? aVScrollBar, TIndicator? aIndicator,
                       string? aFileName,
                       TFileEditorOpenOptions? options)
        : base(bounds, aHScrollBar, aVScrollBar, aIndicator, 4096)
    {
        openOptions = options ?? new TFileEditorOpenOptions();
        if (string.IsNullOrEmpty(aFileName))
        {
            fileName = string.Empty;
        }
        else
        {
            fileName = aFileName;
            if (isValid)
                isValid = LoadFile();
        }
    }

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);
        if (ev.What == Events.evCommand)
        {
            switch (ev.message.command)
            {
                case Views.cmSave:   Save();   break;
                case Views.cmSaveAs: SaveAs(); break;
                default: return;
            }
            ClearEvent(ref ev);
        }
    }

    /// <summary>Loads and normalizes file content while recording encoding and line endings; a missing file is accepted as a new document, and read failure returns false.</summary>
    public bool LoadFile()
    {
        if (!File.Exists(fileName))
        {
            // Upstream returns True on missing file (creates a new file
            // editor with an empty buffer).
            OriginalLineEnding = LineEndingKind.Unknown;
            saveLineEnding = DefaultLineEndingForPlatform();
            HadMixedLineEndings = false;
            mixedLineEndingChoicePending = false;
            loadedCrLfCount = loadedLfCount = loadedCrCount = 0;
            OriginalEncoding = EditorEncodingKind.Utf8;
            OriginalLegacyEncoding = null;
            HadUtf8Bom = false;
            saveEncoding = EditorEncodingKind.Utf8;
            saveLegacyEncoding = null;
            SetBufLen(0);
            return true;
        }

        byte[] fileBytes;
        try
        {
            fileBytes = File.ReadAllBytes(fileName);
        }
        catch
        {
            editorDialog(Views.edReadError, fileName);
            return false;
        }

        string text = DecodeFileBytes(
            fileBytes,
            openOptions?.Encoding ?? EditorTextEncoding.Auto,
            out var encoding,
            out bool bom,
            out var legacyEncoding);
        char[] normalized = NormalizeLineEndings(
            text, out var detected, out var saveAs, out bool mixed,
            out loadedCrLfCount, out loadedLfCount, out loadedCrCount);
        OriginalLineEnding = detected;
        saveLineEnding = saveAs;
        HadMixedLineEndings = mixed;
        mixedLineEndingChoicePending = mixed;
        OriginalEncoding = encoding;
        OriginalLegacyEncoding = legacyEncoding;
        HadUtf8Bom = bom;
        saveEncoding = encoding;
        saveLegacyEncoding = encoding switch
        {
            EditorEncodingKind.Latin1 => legacyEncoding ?? LegacyTextEncodings.Latin1,
            EditorEncodingKind.Legacy => legacyEncoding,
            _ => null,
        };

        if (!SetBufSize((uint)normalized.Length))
        {
            editorDialog(Views.edOutOfMemory, null);
            return false;
        }

        if (normalized.Length > 0)
            Array.Copy(normalized, 0, Buf, (int)(bufSize - (uint)normalized.Length), normalized.Length);

        SetBufLen((uint)normalized.Length);
        return true;
    }

    /// <summary>Saves to the current path or invokes Save As for an unnamed document; returns whether saving succeeded.</summary>
    public bool Save()
    {
        if (string.IsNullOrEmpty(fileName))
            return SaveAs();
        return SaveFile();
    }

    /// <summary>
    /// Encoding chosen in the Save As dialog for the Save As in progress; null or <see cref="EditorTextEncoding.Auto"/>
    /// keeps the document's encoding. An <see cref="TEditor.editorDialog"/> handler sets it while answering
    /// <see cref="Views.edSaveAs"/>; <see cref="SaveAs"/> clears it before the dialog and after the save.
    /// </summary>
    public EditorTextEncoding? SaveAsEncoding { get; set; }

    /// <summary>Prompts for a destination, expands the accepted filename, and saves; returns false on cancellation or failure.</summary>
    /// <remarks>The save uses <see cref="SaveAsEncoding"/> when the dialog set it. A failed save keeps the previous
    /// filename and encoding, and leaves the destination untouched.</remarks>
    public bool SaveAs()
    {
        bool res = false;
        string previousName = fileName;
        SaveAsEncoding = null;
        try
        {
            if (editorDialog(Views.edSaveAs, this) != Views.cmCancel)
            {
                EditorTextEncoding requested = SaveAsEncoding ?? EditorTextEncoding.Auto;
                // CLY_fexpand mapped to Path.GetFullPath; matches the upstream
                // contract that fileName is canonicalised after a successful
                // edSaveAs dialog.
                try { fileName = Path.GetFullPath(fileName); } catch { }
                res = SaveFile(requested);
                if (!res)
                    fileName = previousName;
                // Upstream: message(owner, evBroadcast, cmUpdateTitle, 0).
                if (owner != null)
                    Message(owner, Events.evBroadcast, Views.cmUpdateTitle, null);
                if (IsClipboard())
                    fileName = string.Empty;
            }
        }
        finally { SaveAsEncoding = null; }
        return res;
    }

    /// <summary>Selects the encoding for the next save; Legacy needs a codec and is rejected here, Latin1 uses <see cref="LegacyTextEncodings.Latin1"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is Legacy or not a defined value.</exception>
    public void SetSaveEncoding(EditorEncodingKind kind)
    {
        switch (kind)
        {
            case EditorEncodingKind.Utf8:
            case EditorEncodingKind.Utf8Bom:
            case EditorEncodingKind.Utf16LeBom:
            case EditorEncodingKind.Utf16BeBom:
                saveEncoding = kind;
                saveLegacyEncoding = null;
                break;
            case EditorEncodingKind.Latin1:
                saveEncoding = kind;
                saveLegacyEncoding = LegacyTextEncodings.Latin1;
                break;
            default:
                throw new ArgumentException(
                    "Use SetSaveEncoding(ILegacyTextEncoding) to select a legacy single-byte encoding.", nameof(kind));
        }
    }

    /// <summary>Selects a single-byte codec for the next save; <see cref="LegacyTextEncodings.Latin1"/> selects Latin1, any other codec Legacy.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="encoding"/> is null.</exception>
    public void SetSaveEncoding(ILegacyTextEncoding encoding)
    {
        if (encoding == null) throw new ArgumentNullException(nameof(encoding));
        saveEncoding = ReferenceEquals(encoding, LegacyTextEncodings.Latin1)
            ? EditorEncodingKind.Latin1
            : EditorEncodingKind.Legacy;
        saveLegacyEncoding = encoding;
    }

    /// <summary>
    /// Applies a file-dialog encoding choice to the next save: Auto keeps the current save encoding, UTF-8 selects UTF-8
    /// (keeping a UTF-8 byte-order mark the document already uses), and Legacy selects that codec.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="choice"/> is null.</exception>
    public void SetSaveEncoding(EditorTextEncoding choice)
    {
        if (choice == null) throw new ArgumentNullException(nameof(choice));
        switch (choice.Mode)
        {
            case EditorTextEncodingMode.Utf8:
                SetSaveEncoding(saveEncoding == EditorEncodingKind.Utf8Bom
                    ? EditorEncodingKind.Utf8Bom
                    : EditorEncodingKind.Utf8);
                break;
            case EditorTextEncodingMode.Legacy when choice.LegacyEncoding != null:
                SetSaveEncoding(choice.LegacyEncoding);
                break;
        }
    }

    /// <summary>Writes content in the given encoding and, on success, makes it the document's encoding for later saves.</summary>
    /// <remarks>
    /// Applies <paramref name="encoding"/> as <see cref="SetSaveEncoding(EditorTextEncoding)"/> does and then saves with
    /// <see cref="SaveFile()"/>, with the same strict encoding and mixed line-ending rules. <see cref="EditorTextEncoding.Auto"/>
    /// keeps the current save encoding. A failed or cancelled save restores the previous save encoding; a successful
    /// explicit choice also updates <see cref="OriginalEncoding"/>, <see cref="OriginalLegacyEncoding"/> and
    /// <see cref="HadUtf8Bom"/>.
    /// </remarks>
    public bool SaveFile(EditorTextEncoding encoding)
    {
        var previousKind = saveEncoding;
        var previousLegacy = saveLegacyEncoding;
        var requested = encoding ?? EditorTextEncoding.Auto;
        SetSaveEncoding(requested);
        if (!SaveFile())
        {
            saveEncoding = previousKind;
            saveLegacyEncoding = previousLegacy;
            return false;
        }
        if (requested.Mode != EditorTextEncodingMode.Auto)
        {
            OriginalEncoding = saveEncoding;
            OriginalLegacyEncoding = saveLegacyEncoding;
            HadUtf8Bom = saveEncoding == EditorEncodingKind.Utf8Bom;
        }
        return true;
    }

    /// <summary>Writes content using the selected line endings and save encoding, optionally backing up the old file; clears modified state on success.</summary>
    /// <remarks>
    /// Saving is strict: when the text contains characters that <see cref="SaveEncoding"/> cannot represent (including
    /// unpaired surrogates in UTF-8 and UTF-16), <see cref="Views.edEncodingWriteError"/> is reported and false is
    /// returned without touching the destination or its backup. For a document loaded with mixed line endings, the first
    /// save asks for the line endings to write (<see cref="IsMixedLineEndingChoicePending"/>); a cancelled prompt
    /// returns false and writes nothing.
    /// </remarks>
    public bool SaveFile()
    {
        if (mixedLineEndingChoicePending)
        {
            var prompt = new EditorLineEndingPromptInfo(
                fileName, loadedCrLfCount, loadedLfCount, loadedCrCount, NormalizeChoice(saveLineEnding));
            ushort answer = editorDialog(Views.edMixedLineEndings, prompt);
            if (answer != Views.cmOK && answer != Views.cmYes)
                return false;
            saveLineEnding = prompt.Choice;
            mixedLineEndingChoicePending = false;
        }

        string? temporary = null;
        int error = Views.edCreateError;
        try
        {
            // Encoding runs first: an unrepresentable character fails before any file is created.
            byte[] content = EncodeContent();
            string destination = Path.GetFullPath(fileName);
            temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".tsharpvision-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var f = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                error = Views.edWriteError;
                f.Write(content, 0, content.Length);
                f.Flush(flushToDisk: true);
            }

            // The destination and its backup are touched only after the complete new file is closed.
            if ((editorFlags & Views.efBackupFiles) != 0 && File.Exists(destination))
                File.Replace(temporary, destination, MakeBackupName(destination));
            else
                File.Move(temporary, destination, overwrite: true);
        }
        catch (EncoderFallbackException)
        {
            editorDialog(Views.edEncodingWriteError, fileName);
            return false;
        }
        catch
        {
            editorDialog(error, fileName);
            return false;
        }
        finally
        {
            if (temporary != null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        MarkUnmodified();
        Update(Views.ufUpdate);
        return true;
    }

    private static LineEndingKind NormalizeChoice(LineEndingKind kind)
        => kind is LineEndingKind.Lf or LineEndingKind.CrLf or LineEndingKind.Cr
            ? kind
            : DefaultLineEndingForPlatform();

    private static LineEndingKind DefaultLineEndingForPlatform()
        => OperatingSystem.IsWindows() ? LineEndingKind.CrLf : LineEndingKind.Lf;

    private static string DecodeFileBytes(
        byte[] source,
        EditorTextEncoding requestedEncoding,
        out EditorEncodingKind encoding,
        out bool hadBom,
        out ILegacyTextEncoding? legacyEncoding)
    {
        requestedEncoding ??= EditorTextEncoding.Auto;
        legacyEncoding = null;
        hadBom = source.Length >= 3
            && source[0] == 0xEF && source[1] == 0xBB && source[2] == 0xBF;

        int offset = hadBom ? 3 : 0;
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        if (requestedEncoding.Mode == EditorTextEncodingMode.Utf8)
        {
            encoding = hadBom ? EditorEncodingKind.Utf8Bom : EditorEncodingKind.Utf8;
            return strictUtf8.GetString(source, offset, source.Length - offset);
        }

        if (requestedEncoding.Mode == EditorTextEncodingMode.Legacy)
        {
            ILegacyTextEncoding selectedLegacyEncoding = requestedEncoding.LegacyEncoding
                ?? throw new InvalidOperationException("Legacy mode requires a legacy text encoding.");
            encoding = selectedLegacyEncoding == LegacyTextEncodings.Latin1
                ? EditorEncodingKind.Latin1
                : EditorEncodingKind.Legacy;
            legacyEncoding = selectedLegacyEncoding;
            hadBom = false;
            return selectedLegacyEncoding.Decode(source);
        }

        // Auto: a UTF-16 byte-order mark selects strict UTF-16; undecodable content falls back to Latin-1 below.
        if (source.Length >= 2 && ((source[0] == 0xFF && source[1] == 0xFE) || (source[0] == 0xFE && source[1] == 0xFF)))
        {
            bool bigEndian = source[0] == 0xFE;
            try
            {
                string utf16 = (bigEndian ? StrictUtf16Be : StrictUtf16Le).GetString(source, 2, source.Length - 2);
                encoding = bigEndian ? EditorEncodingKind.Utf16BeBom : EditorEncodingKind.Utf16LeBom;
                hadBom = false;
                return utf16;
            }
            catch (DecoderFallbackException)
            {
                encoding = EditorEncodingKind.Latin1;
                hadBom = false;
                return Encoding.Latin1.GetString(source);
            }
        }

        try
        {
            string text = strictUtf8.GetString(source, offset, source.Length - offset);
            encoding = hadBom ? EditorEncodingKind.Utf8Bom : EditorEncodingKind.Utf8;
            return text;
        }
        catch (DecoderFallbackException)
        {
            encoding = EditorEncodingKind.Latin1;
            legacyEncoding = null;
            hadBom = false;
            return Encoding.Latin1.GetString(source);
        }
    }

    private static char[] NormalizeLineEndings(
        string source,
        out LineEndingKind detected,
        out LineEndingKind saveAs,
        out bool mixed,
        out int crlf,
        out int lf,
        out int cr)
    {
        crlf = 0; lf = 0; cr = 0;
        var normalized = new char[source.Length];
        int n = 0;

        for (int i = 0; i < source.Length; i++)
        {
            char ch = source[i];
            if (ch == '\r')
            {
                normalized[n++] = '\n';
                if (i + 1 < source.Length && source[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (ch == '\n')
            {
                normalized[n++] = '\n';
                lf++;
            }
            else
            {
                normalized[n++] = ch;
            }
        }

        int styles = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        mixed = styles > 1;
        if (styles == 0)
        {
            detected = LineEndingKind.Unknown;
            saveAs = DefaultLineEndingForPlatform();
        }
        else
        {
            saveAs = DominantLineEnding(crlf, lf, cr);
            detected = mixed ? LineEndingKind.Mixed : saveAs;
        }

        if (n == source.Length) return normalized;
        var result = new char[n];
        Array.Copy(normalized, result, n);
        return result;
    }

    private static LineEndingKind DominantLineEnding(int crlf, int lf, int cr)
    {
        if (crlf >= lf && crlf >= cr) return LineEndingKind.CrLf;
        if (lf >= cr) return LineEndingKind.Lf;
        return LineEndingKind.Cr;
    }

    private static byte[] BytesForLineEnding(LineEndingKind kind)
    {
        kind = kind switch
        {
            LineEndingKind.CrLf => LineEndingKind.CrLf,
            LineEndingKind.Cr => LineEndingKind.Cr,
            LineEndingKind.Lf => LineEndingKind.Lf,
            _ => DefaultLineEndingForPlatform(),
        };
        return kind switch
        {
            LineEndingKind.CrLf => new byte[] { 0x0D, 0x0A },
            LineEndingKind.Cr => new byte[] { 0x0D },
            _ => new byte[] { 0x0A },
        };
    }

    private byte[] EncodeContent()
    {
        byte[] preamble = saveEncoding switch
        {
            EditorEncodingKind.Utf8Bom => new byte[] { 0xEF, 0xBB, 0xBF },
            EditorEncodingKind.Utf16LeBom => new byte[] { 0xFF, 0xFE },
            EditorEncodingKind.Utf16BeBom => new byte[] { 0xFE, 0xFF },
            _ => Array.Empty<byte>(),
        };
        byte[] body = EncodeTextForSave(TextWithLineEndings());
        if (preamble.Length == 0) return body;
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    private string TextWithLineEndings()
    {
        string lineEnding = saveLineEnding switch
        {
            LineEndingKind.CrLf => "\r\n",
            LineEndingKind.Cr => "\r",
            LineEndingKind.Lf => "\n",
            _ => DefaultLineEndingForPlatform() == LineEndingKind.CrLf ? "\r\n" : "\n",
        };
        var sb = new StringBuilder((int)bufLen);
        for (uint i = 0; i < bufLen; i++)
        {
            if (BufChar(i) == '\n')
                sb.Append(lineEnding);
            else
                sb.Append(BufChar(i));
        }
        return sb.ToString();
    }

    // Every codec rejects what it cannot represent (EncoderFallbackException); nothing is replaced or best-fitted.
    private byte[] EncodeTextForSave(string text) => saveEncoding switch
    {
        EditorEncodingKind.Utf16LeBom => StrictUtf16Le.GetBytes(text),
        EditorEncodingKind.Utf16BeBom => StrictUtf16Be.GetBytes(text),
        EditorEncodingKind.Latin1 or EditorEncodingKind.Legacy =>
            (saveLegacyEncoding ?? LegacyTextEncodings.Latin1).Encode(text),
        _ => StrictUtf8.GetBytes(text),
    };

    private static string MakeBackupName(string path)
    {
        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileName(path);
        int dot = name.LastIndexOf('.');
        string baseName = dot >= 0 ? name.Substring(0, dot) : name;
        return Path.Combine(dir, baseName + backupExt);
    }

    /// <inheritdoc />
    public override bool SetBufSize(uint newSize)
    {
        newSize = (newSize + 0x0FFFu) & 0xFFFFF000u;
        if (newSize != bufSize)
        {
            char[]? temp = buffer;
            char[] fresh;
            try { fresh = new char[newSize]; }
            catch (OutOfMemoryException) { return false; }

            // Copies up to min(newSize, bufSize) characters from the
            // front, then moves the right-side tail (the text after
            // curPtr, which sits after the gap) to `newSize - n`. Undo
            // text lives in the editor's history, not in the gap, so
            // delCount is not part of the tail.
            uint n = bufLen - curPtr;
            if (temp != null)
            {
                uint copyLen = Math.Min(newSize, bufSize);
                if (copyLen > 0)
                    Array.Copy(temp, 0, fresh, 0, (int)copyLen);
                if (n > 0)
                    Array.Copy(temp, (int)(bufSize - n),
                               fresh, (int)(newSize - n), (int)n);
            }
            buffer = fresh;
            bufSize = newSize;
            gapLen = bufSize - bufLen;
        }
        return true;
    }

    /// <inheritdoc />
    public override void ShutDown()
    {
        SetCmdState(Views.cmSave, false);
        SetCmdState(Views.cmSaveAs, false);
        base.ShutDown();
    }

    /// <inheritdoc />
    public override void UpdateCommands()
    {
        base.UpdateCommands();
        SetCmdState(Views.cmSave, true);
        SetCmdState(Views.cmSaveAs, true);
    }

    /// <inheritdoc />
    public override bool Valid(ushort command)
    {
        if (command == Views.cmValid)
            return isValid;

        if (modified)
        {
            int dlg = string.IsNullOrEmpty(fileName)
                ? Views.edSaveUntitled
                : Views.edSaveModify;
            ushort answer = editorDialog(dlg, fileName);
            switch (answer)
            {
                case Views.cmYes:    return Save();
                case Views.cmNo:
                    // A later window can still veto application quit. Keep the
                    // unsaved buffer dirty until the accepted exit disposes it.
                    if (command != Views.cmQuit)
                        modified = false;
                    return true;
                case Views.cmCancel: return false;
            }
        }
        return true;
    }

    // Wire: TEditor base + fileName(string) + selStart(uint32) + selEnd(uint32) + curPtr(uint32).
    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TFileEditor(StreamableInit init) : base(init)
    {
        fileName = string.Empty;
        openOptions = new TFileEditorOpenOptions();
    }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteString(fileName);
        os.WriteInt(selStart);
        os.WriteInt(selEnd);
        os.WriteInt(curPtr);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        fileName = isStream.ReadString() ?? string.Empty;
        if (isValid)
        {
            isValid = LoadFile();
            uint sStart = isStream.ReadInt();
            uint sEnd   = isStream.ReadInt();
            uint curs   = isStream.ReadInt();
            if (isValid && sEnd <= bufLen)
            {
                SetSelect(sStart, sEnd, curs == sStart);
                TrackCursor(true);
            }
        }
        else
        {
            // skip the three uint32 fields even though isValid is false
            isStream.ReadInt(); isStream.ReadInt(); isStream.ReadInt();
        }
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TFileEditor(StreamableInit.streamableInit);
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTFileEditor =
        new TStreamableClass("TFileEditor", () => new TFileEditor(StreamableInit.streamableInit), 0);
}
