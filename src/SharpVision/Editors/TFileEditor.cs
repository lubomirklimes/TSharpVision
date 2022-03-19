using System;
using System.IO;
using SharpVision.Constants;

namespace SharpVision;

// TFileEditor: a TEditor that backs its gap buffer with an
// on-disk file. Adds load/save/saveAs, override SetBufSize for realloc on
// growth, override Valid for unsaved-modify prompt, and the cmSave/cmSaveAs
// handlers.
public class TFileEditor : TEditor
{
    public static string backupExt = ".bak";

    public string fileName;

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
            SetBufLen(0);
            return true;
        }

        long fSize;
        try
        {
            using var f = File.OpenRead(fileName);
            fSize = f.Length;
            if (!SetBufSize((uint)fSize))
            {
                editorDialog(Views.edOutOfMemory, null);
                return false;
            }
            // Live data lives at the end of the buffer; gap is at front.
            int read = 0;
            int offset = (int)(bufSize - (uint)fSize);
            while (read < fSize)
            {
                int n = f.Read(buffer, offset + read, (int)fSize - read);
                if (n <= 0) break;
                read += n;
            }
            if (read != fSize)
            {
                editorDialog(Views.edReadError, fileName);
                return false;
            }
        }
        catch
        {
            editorDialog(Views.edReadError, fileName);
            return false;
        }

        SetBufLen((uint)fSize);
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
            if (curPtr > 0)
                f.Write(buffer, 0, (int)curPtr);
            uint right = bufLen - curPtr;
            if (right > 0)
                f.Write(buffer, (int)(curPtr + gapLen), (int)right);
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
