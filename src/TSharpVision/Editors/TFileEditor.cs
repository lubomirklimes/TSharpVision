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
    public LineEndingKind SaveLineEnding { get; set; } = DefaultLineEndingForPlatform();
    /// <summary>Whether the loaded file contained multiple line-ending conventions.</summary>
    public bool HadMixedLineEndings { get; private set; }
    /// <summary>Encoding recognized or selected when the document was loaded.</summary>
    public EditorEncodingKind OriginalEncoding { get; private set; } = EditorEncodingKind.Utf8;
    /// <summary>Legacy codec associated with the loaded file, or null for UTF-8.</summary>
    public ILegacyTextEncoding? OriginalLegacyEncoding { get; private set; }
    /// <summary>Whether the loaded file began with a UTF-8 byte-order mark.</summary>
    public bool HadUtf8Bom { get; private set; }
    private TFileEditorOpenOptions openOptions;

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
            SaveLineEnding = DefaultLineEndingForPlatform();
            HadMixedLineEndings = false;
            OriginalEncoding = EditorEncodingKind.Utf8;
            OriginalLegacyEncoding = null;
            HadUtf8Bom = false;
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
        char[] normalized = NormalizeLineEndings(text, out var detected, out var saveAs, out bool mixed);
        OriginalLineEnding = detected;
        SaveLineEnding = saveAs;
        HadMixedLineEndings = mixed;
        OriginalEncoding = encoding;
        OriginalLegacyEncoding = legacyEncoding;
        HadUtf8Bom = bom;

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

    /// <summary>Prompts for a destination, expands the accepted filename, and saves; returns false on cancellation or failure.</summary>
    public bool SaveAs()
    {
        bool res = false;
        if (editorDialog(Views.edSaveAs, this) != Views.cmCancel)
        {
            // CLY_fexpand mapped to Path.GetFullPath; matches the upstream
            // contract that fileName is canonicalised after a successful
            // edSaveAs dialog.
            try { fileName = Path.GetFullPath(fileName); } catch { }
            // Upstream: message(owner, evBroadcast, cmUpdateTitle, 0).
            if (owner != null)
                Message(owner, Events.evBroadcast, Views.cmUpdateTitle, null);
            res = SaveFile();
            if (IsClipboard())
                fileName = string.Empty;
        }
        return res;
    }

    /// <summary>Writes content using the selected line endings and retained encoding, optionally backing up the old file; clears modified state on success.</summary>
    public bool SaveFile()
    {
        if ((editorFlags & Views.efBackupFiles) != 0
            && File.Exists(fileName))
        {
            string backup = MakeBackupName(fileName);
            try
            {
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(fileName, backup);
            }
            catch
            {
                // Failure to roll the backup is non-fatal upstream.
            }
        }

        FileStream f;
        try { f = File.Create(fileName); }
        catch
        {
            editorDialog(Views.edCreateError, fileName);
            return false;
        }

        try
        {
            if (HadUtf8Bom || OriginalEncoding == EditorEncodingKind.Utf8Bom)
            {
                byte[] bom = { 0xEF, 0xBB, 0xBF };
                f.Write(bom, 0, bom.Length);
            }
            WriteRangeWithLineEndings(f, 0, (int)curPtr);
            uint right = bufLen - curPtr;
            if (right > 0)
                WriteRangeWithLineEndings(f, (int)(curPtr + gapLen), (int)right);
        }
        catch (EncoderFallbackException)
        {
            f.Dispose();
            editorDialog(Views.edEncodingWriteError, fileName);
            return false;
        }
        catch
        {
            f.Dispose();
            editorDialog(Views.edWriteError, fileName);
            return false;
        }

        try { f.Dispose(); }
        catch
        {
            editorDialog(Views.edWriteError, fileName);
            return false;
        }

        modified = false;
        Update(Views.ufUpdate);
        return true;
    }

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
        out bool mixed)
    {
        int crlf = 0, lf = 0, cr = 0;
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

    private void WriteRangeWithLineEndings(Stream stream, int offset, int length)
    {
        string lineEnding = SaveLineEnding switch
        {
            LineEndingKind.CrLf => "\r\n",
            LineEndingKind.Cr => "\r",
            LineEndingKind.Lf => "\n",
            _ => DefaultLineEndingForPlatform() == LineEndingKind.CrLf ? "\r\n" : "\n",
        };
        var sb = new StringBuilder(length + 16);
        int end = offset + length;
        for (int i = offset; i < end; i++)
        {
            if (Buf[i] == '\n')
                sb.Append(lineEnding);
            else
                sb.Append(Buf[i]);
        }
        byte[] bytes = EncodeTextForSave(sb.ToString());
        stream.Write(bytes, 0, bytes.Length);
    }

    private byte[] EncodeTextForSave(string text)
    {
        if (OriginalLegacyEncoding != null)
            return OriginalLegacyEncoding.Encode(text);

        return OriginalEncoding == EditorEncodingKind.Latin1
            ? Encoding.Latin1.GetBytes(text)
            : Encoding.UTF8.GetBytes(text);
    }

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

            // Upstream copies up to min(newSize, bufSize) bytes from
            // the front, then memmoves the right-side tail (post-gap)
            // to its new position at `newSize - n`.
            uint n = bufLen - curPtr + delCount;
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
                case Views.cmNo:     modified = false; return true;
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
