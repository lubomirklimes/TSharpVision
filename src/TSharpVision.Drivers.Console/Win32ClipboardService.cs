using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TSharpVision.Drivers.Console;

// Win32 raw clipboard bridge using user32.dll + kernel32.dll.
// Format: CF_UNICODETEXT (UTF-16, NUL-terminated, CRLF newlines).
//
// Threading: raw Win32 clipboard does NOT require [STAThread]. STA is only
// needed for OLE / IDataObject. All operations here are bounded P/Invoke.
//
// Lifetime: stateless. No initialisation / shutdown is required; the
// AppLifecycleGuard does not need to know about the clipboard.
//
// All public methods catch every exception and translate to a safe
// false / null return so editor command dispatch never sees a Win32 fault.

/// <summary>
/// <see cref="IClipboardService"/> implementation backed by the Windows
/// clipboard via user32.dll. Lives in <c>TSharpVision.Console</c> so that
/// TSharpVision core does not depend on any P/Invoke surface.
/// </summary>
public sealed class Win32ClipboardService : IClipboardService
{
    // ---- Win32 constants ---------------------------------------------
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE  = 0x0002;
    private const int  OpenRetries    = 5;
    private const int  OpenRetryDelayMs = 10;

    // Hard cap on payload sizes to avoid pathological allocations from a
    // hostile clipboard owner. Mirrors ClipboardEncoding.MaxPasteChars
    // by default but enforced independently of editor-side checks.
    private const long MaxBytes = 32L * 1024 * 1024; // 32 MiB of UTF-16 == 16 Mchar

    // ---- user32 / kernel32 P/Invoke surface --------------------------
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    // ---- IClipboardService -------------------------------------------

    public bool IsAvailable => OperatingSystem.IsWindows();

    public string? GetText()
    {
        if (!IsAvailable) return null;
        return TryGetTextCore();
    }

    public bool TryGetText(out string text)
    {
        text = string.Empty;
        if (!IsAvailable) return false;
        var s = TryGetTextCore();
        if (s == null) return false;
        text = s;
        return true;
    }

    public bool SetText(string text)
    {
        if (!IsAvailable) return false;
        return TrySetTextCore(text ?? string.Empty);
    }

    // ---- Implementation ----------------------------------------------

    private static string? TryGetTextCore()
    {
        try
        {
            if (!TryOpenClipboard()) return null;
            try
            {
                if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
                IntPtr h = GetClipboardData(CF_UNICODETEXT);
                if (h == IntPtr.Zero) return null;

                UIntPtr size = GlobalSize(h);
                if (size != UIntPtr.Zero && size.ToUInt64() > (ulong)MaxBytes)
                    return null;

                IntPtr p = GlobalLock(h);
                if (p == IntPtr.Zero) return null;
                try
                {
                    // PtrToStringUni stops at the first NUL.
                    string raw = Marshal.PtrToStringUni(p) ?? string.Empty;
                    if (raw.Length > ClipboardEncoding.MaxPasteChars) return null;
                    return ClipboardEncoding.NormalizeFromCrLf(raw);
                }
                finally
                {
                    GlobalUnlock(h);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            // Swallow all P/Invoke / marshalling exceptions; editor must
            // never see a clipboard fault during command dispatch.
            return null;
        }
    }

    private static bool TrySetTextCore(string text)
    {
        try
        {
            string normalised = ClipboardEncoding.NormalizeToCrLf(text);
            if ((long)normalised.Length > ClipboardEncoding.MaxPasteChars) return false;

            // UTF-16 bytes plus trailing NUL.
            int charCount = normalised.Length + 1;
            long byteCount = (long)charCount * 2;
            if (byteCount > MaxBytes) return false;

            IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(ulong)byteCount);
            if (hMem == IntPtr.Zero) return false;

            try
            {
                IntPtr p = GlobalLock(hMem);
                if (p == IntPtr.Zero) return false;
                try
                {
                    // Copy chars + trailing NUL.
                    byte[] tmp = Encoding.Unicode.GetBytes(normalised);
                    Marshal.Copy(tmp, 0, p, tmp.Length);
                    Marshal.WriteInt16(p, tmp.Length, 0);
                }
                finally
                {
                    GlobalUnlock(hMem);
                }

                if (!TryOpenClipboard()) return false;
                bool transferred = false;
                try
                {
                    EmptyClipboard();
                    if (SetClipboardData(CF_UNICODETEXT, hMem) != IntPtr.Zero)
                    {
                        // Ownership transferred to clipboard. Do NOT free.
                        transferred = true;
                        return true;
                    }
                    return false;
                }
                finally
                {
                    CloseClipboard();
                    if (!transferred)
                    {
                        // SetClipboardData failed — we still own hMem.
                        GlobalFree(hMem);
                        hMem = IntPtr.Zero;
                    }
                    else
                    {
                        // Mark transferred so the outer finally block does
                        // not attempt to free.
                        hMem = IntPtr.Zero;
                    }
                }
            }
            finally
            {
                if (hMem != IntPtr.Zero)
                    GlobalFree(hMem);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryOpenClipboard()
    {
        for (int i = 0; i < OpenRetries; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            try { System.Threading.Thread.Sleep(OpenRetryDelayMs); }
            catch { /* ignore */ }
        }
        return false;
    }
}
