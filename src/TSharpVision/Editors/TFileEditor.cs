using System;
using System.IO;
using TSharpVision.Constants;

namespace TSharpVision;

// TFileEditor: a TEditor that backs its gap buffer with an
// on-disk file. Adds load/save/saveAs, override SetBufSize for realloc on
// growth, override Valid for unsaved-modify prompt, and the cmSave/cmSaveAs
// handlers.
public class TFileEditor : TEditor
{
    public static string backupExt = ".bak";

    public string fileName;
    public LineEndingKind OriginalLineEnding { get; private set; } = LineEndingKind.Unknown;
    public LineEndingKind SaveLineEnding { get; set; } = DefaultLineEndingForPlatform();
    public bool HadMixedLineEndings { get; private set; }

    public TFileEditor(TRect bounds, TScrollBar aHScrollBar,
                       TScrollBar aVScrollBar, TIndicator aIndicator,
                       string aFileName)
        : base(bounds, aHScrollBar, aVScrollBar, aIndicator, 4096)
    {
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

    public bool LoadFile()
    {
        if (!File.Exists(fileName))
        {
            // Upstream returns True on missing file (creates a new file
            // editor with an empty buffer).
            OriginalLineEnding = LineEndingKind.Unknown;
            SaveLineEnding = DefaultLineEndingForPlatform();
            HadMixedLineEndings = false;
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

        byte[] normalized = NormalizeLineEndings(fileBytes, out var detected, out var saveAs, out bool mixed);
        OriginalLineEnding = detected;
        SaveLineEnding = saveAs;
        HadMixedLineEndings = mixed;

        if (!SetBufSize((uint)normalized.Length))
        {
            editorDialog(Views.edOutOfMemory, null);
            return false;
        }

        if (normalized.Length > 0)
            Array.Copy(normalized, 0, buffer, (int)(bufSize - (uint)normalized.Length), normalized.Length);

        SetBufLen((uint)normalized.Length);
        return true;
    }

    public bool Save()
    {
        if (string.IsNullOrEmpty(fileName))
            return SaveAs();
        return SaveFile();
    }

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
            WriteRangeWithLineEndings(f, 0, (int)curPtr);
            uint right = bufLen - curPtr;
            if (right > 0)
                WriteRangeWithLineEndings(f, (int)(curPtr + gapLen), (int)right);
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

    private static byte[] NormalizeLineEndings(
        byte[] source,
        out LineEndingKind detected,
        out LineEndingKind saveAs,
        out bool mixed)
    {
        int crlf = 0, lf = 0, cr = 0;
        var normalized = new byte[source.Length];
        int n = 0;

        for (int i = 0; i < source.Length; i++)
        {
            byte b = source[i];
            if (b == 0x0D)
            {
                normalized[n++] = 0x0A;
                if (i + 1 < source.Length && source[i + 1] == 0x0A)
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (b == 0x0A)
            {
                normalized[n++] = 0x0A;
                lf++;
            }
            else
            {
                normalized[n++] = b;
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
        var result = new byte[n];
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
        byte[] lineEnding = BytesForLineEnding(SaveLineEnding);
        int end = offset + length;
        for (int i = offset; i < end; i++)
        {
            if (buffer[i] == 0x0A)
                stream.Write(lineEnding, 0, lineEnding.Length);
            else
                stream.WriteByte(buffer[i]);
        }
    }

    private static string MakeBackupName(string path)
    {
        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileName(path);
        int dot = name.LastIndexOf('.');
        string baseName = dot >= 0 ? name.Substring(0, dot) : name;
        return Path.Combine(dir, baseName + backupExt);
    }

    public override bool SetBufSize(uint newSize)
    {
        newSize = (newSize + 0x0FFFu) & 0xFFFFF000u;
        if (newSize != bufSize)
        {
            byte[] temp = buffer;
            byte[] fresh;
            try { fresh = new byte[newSize]; }
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

    public override void ShutDown()
    {
        SetCmdState(Views.cmSave, false);
        SetCmdState(Views.cmSaveAs, false);
        base.ShutDown();
    }

    public override void UpdateCommands()
    {
        base.UpdateCommands();
        SetCmdState(Views.cmSave, true);
        SetCmdState(Views.cmSaveAs, true);
    }

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
    protected TFileEditor(StreamableInit init) : base(init) { fileName = string.Empty; }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteString(fileName);
        os.WriteInt(selStart);
        os.WriteInt(selEnd);
        os.WriteInt(curPtr);
    }

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

    public new static TStreamable Build() => new TFileEditor(StreamableInit.streamableInit);
    public static readonly TStreamableClass StreamableClassTFileEditor =
        new TStreamableClass("TFileEditor", () => new TFileEditor(StreamableInit.streamableInit), 0);
}
