// SharpVision Resource Tools
// Inspector commands: list / show / dump / validate.
// Produces deterministic text output for svres list / show / dump commands.
using System.IO;
using System.Text;
using SharpVision.Drivers;

namespace SharpVision.ResourceTools;

/// <summary>
/// Produces deterministic text output for the <c>svres</c> CLI commands.
/// All methods return a <see cref="CommandResult"/> (output string + exit code).
/// None of these methods mutate the <c>.tvr</c> file.
/// </summary>
public static class InspectorCommands
{
    // ── list ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Implements <c>svres list &lt;file.tvr&gt;</c>.
    /// Lists all resources in the file: key, position, size, type name.
    /// </summary>
    public static CommandResult List(string tvrPath)
    {
        TvrInspector insp;
        try { insp = TvrInspector.Open(tvrPath); }
        catch (FileNotFoundException ex) { return CommandResult.Fail(ex.Message); }
        catch (Exception ex)             { return CommandResult.Fail($"Cannot open file: {ex.Message}"); }

        var sb = new StringBuilder();
        int count  = insp.Entries.Count;
        string noun = count == 1 ? "item" : "items";
        sb.AppendLine($"Resources in {Path.GetFileName(tvrPath)} ({count} {noun}):");
        sb.AppendLine();

        if (count == 0)
        {
            sb.AppendLine("(empty)");
            return CommandResult.Ok(sb.ToString().TrimEnd());
        }

        // Column widths: key col = max(key length, 16), others fixed.
        int keyW = Math.Max(insp.Entries.Max(e => e.Key.Length), 16);
        sb.AppendLine($"{"Key".PadRight(keyW)}  {"Position",10}  {"Size",8}  Type");
        sb.AppendLine($"{new string('-', keyW)}  {new string('-', 10)}  {new string('-', 8)}  ----");

        foreach (var e in insp.Entries)
        {
            string tname = e.TypeName ?? "(unknown)";
            sb.AppendLine($"{e.Key.PadRight(keyW)}  {e.Position,10}  {e.Size,8}  {tname}");
        }

        return CommandResult.Ok(sb.ToString().TrimEnd());
    }

    // ── show ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Implements <c>svres show &lt;file.tvr&gt; &lt;key&gt;</c>.
    /// Shows metadata and, for known types, an interpreted object summary.
    /// </summary>
    public static CommandResult Show(string tvrPath, string key)
    {
        TvrInspector insp;
        try { insp = TvrInspector.Open(tvrPath); }
        catch (FileNotFoundException ex) { return CommandResult.Fail(ex.Message); }
        catch (Exception ex)             { return CommandResult.Fail($"Cannot open file: {ex.Message}"); }

        var entry = insp.Entries.FirstOrDefault(e => e.Key == key);
        if (entry == null)
            return CommandResult.Fail($"Resource not found: {key}");

        var sb = new StringBuilder();
        sb.AppendLine($"Key:      {entry.Key}");
        sb.AppendLine($"Position: {entry.Position}");
        sb.AppendLine($"Size:     {entry.Size}");
        sb.AppendLine($"Type:     {entry.TypeName ?? "(unknown)"}");

        // Try object interpretation for known types.
        if (entry.TypeName == "TDialog")
            AppendDialogSummary(sb, tvrPath, key);
        else if (entry.TypeName == "TMenuBar" || entry.TypeName == "TMenuView")
            AppendMenuBarSummary(sb, tvrPath, key);
        else if (entry.TypeName == "TStatusLine")
            AppendStatusLineSummary(sb, tvrPath, key);

        return CommandResult.Ok(sb.ToString().TrimEnd());
    }

    private static void AppendDialogSummary(StringBuilder sb, string tvrPath, string key)
    {
        // Object deserialization requires the streamable registry and a display driver.
        // We initialize a NullDriver and call RegisterAll() internally so the CLI tool
        // does not need to configure these externally.
        var prevDriver = TDisplay.driver;
        NullDriver drv = null;
        if (TDisplay.driver == null)
        {
            drv = new NullDriver(80, 25);
            drv.Initialize();
            TDisplay.driver = drv;
            TScreen.ScreenBuffer = drv.AllocateScreenBuffer();
        }

        try
        {
            StreamableRegistration.RegisterAll();

            var fp = new Fpstream(tvrPath);
            TDialog dlg = null;
            try
            {
                var rf  = new TResourceFile(fp);
                dlg = rf.Get(key) as TDialog;
            }
            finally
            {
                fp.Close();
            }

            if (dlg == null)
            {
                sb.AppendLine("Interpretation: (could not deserialise)");
                return;
            }

            // Bounds: TDialog stores origin + size; reconstruct rect.
            int x1 = dlg.origin.x;
            int y1 = dlg.origin.y;
            int x2 = x1 + dlg.size.x;
            int y2 = y1 + dlg.size.y;

            sb.AppendLine($"Title:    {(string.IsNullOrEmpty(dlg.title) ? "(none)" : dlg.title)}");
            sb.AppendLine($"Bounds:   ({x1}, {y1})-({x2}, {y2})");

            var childNames = new List<string>();
            dlg.ForEachView(v => childNames.Add(v.GetType().Name));
            sb.AppendLine($"Children: {childNames.Count}");
            foreach (var name in childNames)
                sb.AppendLine($"  {name}");

            dlg.ShutDown();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Interpretation: (error: {ex.Message})");
        }
        finally
        {
            if (drv != null)
                TDisplay.driver = prevDriver;
        }
    }

    private static void AppendMenuBarSummary(StringBuilder sb, string tvrPath, string key)
    {
        var prevDriver = TDisplay.driver;
        NullDriver drv = null;
        if (TDisplay.driver == null)
        {
            drv = new NullDriver(80, 25);
            drv.Initialize();
            TDisplay.driver = drv;
            TScreen.ScreenBuffer = drv.AllocateScreenBuffer();
        }

        try
        {
            StreamableRegistration.RegisterAll();
            var fp = new Fpstream(tvrPath);
            TMenuBar mb = null;
            try
            {
                var rf = new TResourceFile(fp);
                mb = rf.Get(key) as TMenuBar;
            }
            finally { fp.Close(); }

            if (mb == null) { sb.AppendLine("Interpretation: (could not deserialise)"); return; }

            // Count top-level items.
            int count = 0;
            var names = new List<string>();
            for (TMenuItem m = mb.Menu?.Items; m != null; m = m.Next)
            {
                count++;
                if (m.Name != null) names.Add(m.Name);
            }
            sb.AppendLine($"TopLevelItems: {count}");
            foreach (var n in names)
                sb.AppendLine($"  {n}");

            mb.ShutDown();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Interpretation: (error: {ex.Message})");
        }
        finally
        {
            if (drv != null) TDisplay.driver = prevDriver;
        }
    }

    private static void AppendStatusLineSummary(StringBuilder sb, string tvrPath, string key)
    {
        var prevDriver = TDisplay.driver;
        NullDriver drv = null;
        if (TDisplay.driver == null)
        {
            drv = new NullDriver(80, 25);
            drv.Initialize();
            TDisplay.driver = drv;
            TScreen.ScreenBuffer = drv.AllocateScreenBuffer();
        }

        try
        {
            StreamableRegistration.RegisterAll();
            var fp = new Fpstream(tvrPath);
            TStatusLine sl = null;
            try
            {
                var rf = new TResourceFile(fp);
                sl = rf.Get(key) as TStatusLine;
            }
            finally { fp.Close(); }

            if (sl == null) { sb.AppendLine("Interpretation: (could not deserialise)"); return; }

            int defCount = 0, itemCount = 0;
            for (TStatusDef d = sl.Defs; d != null; d = d.Next)
            {
                defCount++;
                for (TStatusItem i = d.Items; i != null; i = i.Next)
                    itemCount++;
            }
            sb.AppendLine($"Ranges: {defCount}");
            sb.AppendLine($"Items:  {itemCount}");
            for (TStatusDef d = sl.Defs; d != null; d = d.Next)
            {
                sb.AppendLine($"  range {d.Min}..{d.Max}");
                for (TStatusItem i = d.Items; i != null; i = i.Next)
                    sb.AppendLine($"    {i.Text}");
            }

            sl.ShutDown();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Interpretation: (error: {ex.Message})");
        }
        finally
        {
            if (drv != null) TDisplay.driver = prevDriver;
        }
    }

    // ── dump ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Implements <c>svres dump &lt;file.tvr&gt; &lt;key&gt; [--hex]</c>.
    /// Prints raw payload metadata; with <c>--hex</c>, prints a hex dump.
    /// </summary>
    public static CommandResult Dump(string tvrPath, string key, bool hex = false)
    {
        byte[] raw;
        try { raw = TvrInspector.ReadRawPayload(tvrPath, key); }
        catch (FileNotFoundException ex) { return CommandResult.Fail(ex.Message); }
        catch (Exception ex)             { return CommandResult.Fail($"Cannot open file: {ex.Message}"); }

        if (raw == null)
            return CommandResult.Fail($"Resource not found: {key}");

        var sb = new StringBuilder();
        string tname = TvrInspector.PeekTypeName(raw) ?? "(unknown)";
        sb.AppendLine($"Key:  {key}");
        sb.AppendLine($"Size: {raw.Length}");
        sb.AppendLine($"Type: {tname}");

        if (hex)
        {
            sb.AppendLine();
            sb.Append(FormatHexDump(raw));
        }

        return CommandResult.Ok(sb.ToString().TrimEnd());
    }

    // ── validate ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Implements <c>svres validate &lt;file.tvr&gt;</c>.
    /// Checks that the file can be opened, every resource has a readable type
    /// name, and every <c>TDialog</c> resource can be fully deserialized.
    /// Returns exit code 0 on success, 1 on any error.
    /// </summary>
    public static CommandResult Validate(string tvrPath)
    {
        TvrInspector insp;
        try { insp = TvrInspector.Open(tvrPath); }
        catch (FileNotFoundException ex) { return CommandResult.Fail(ex.Message); }
        catch (Exception ex)             { return CommandResult.Fail($"TRV0001: Cannot open file: {ex.Message}"); }

        var errors = new List<string>();
        int dialogs = 0, menus = 0, statusLines = 0;

        foreach (var entry in insp.Entries)
        {
            if (string.IsNullOrEmpty(entry.Key))
            {
                errors.Add("TRV0002: A resource has an empty key");
                continue;
            }
            if (entry.TypeName == null)
            {
                errors.Add($"TRV0003: Resource '{entry.Key}' has an unreadable type name (invalid payload prefix)");
                continue;
            }
            switch (entry.TypeName)
            {
                case "TDialog":
                    dialogs++;
                    string dlgErr = TryDeserializeDialogForValidation(tvrPath, entry.Key);
                    if (dlgErr != null)
                        errors.Add($"TRV0004: Resource '{entry.Key}' could not be deserialized as TDialog: {dlgErr}");
                    break;
                case "TMenuBar":
                case "TMenuView":
                    menus++;
                    string mbErr = TryDeserializeMenuBarForValidation(tvrPath, entry.Key);
                    if (mbErr != null)
                        errors.Add($"TRV0004: Resource '{entry.Key}' could not be deserialized as TMenuBar: {mbErr}");
                    break;
                case "TStatusLine":
                    statusLines++;
                    string slErr = TryDeserializeStatusLineForValidation(tvrPath, entry.Key);
                    if (slErr != null)
                        errors.Add($"TRV0004: Resource '{entry.Key}' could not be deserialized as TStatusLine: {slErr}");
                    break;
                // Other known types: just report type name in output (no deep validation).
            }
        }

        int total = insp.Entries.Count;
        var sb = new StringBuilder();

        if (errors.Count == 0)
        {
            sb.AppendLine($"Validated {Path.GetFileName(tvrPath)}");
            sb.AppendLine($"Resources:   {total}");
            sb.AppendLine($"Dialogs:     {dialogs}");
            sb.AppendLine($"Menus:       {menus}");
            sb.AppendLine($"StatusLines: {statusLines}");
            sb.AppendLine($"Errors:      0");
            return CommandResult.Ok(sb.ToString().TrimEnd());
        }
        else
        {
            sb.AppendLine($"Validation failed: {Path.GetFileName(tvrPath)}");
            sb.AppendLine($"Errors: {errors.Count}");
            foreach (var e in errors) sb.AppendLine(e);
            return CommandResult.Fail(sb.ToString().TrimEnd());
        }
    }

    /// <summary>
    /// Tries to fully deserialize a <c>TDialog</c> from the given file/key.
    /// Returns <c>null</c> on success, or an error message string on failure.
    /// </summary>
    private static string TryDeserializeDialogForValidation(string tvrPath, string key)
    {
        var prevDriver = TDisplay.driver;
        NullDriver drv = null;
        if (TDisplay.driver == null)
        {
            drv = new NullDriver(80, 25);
            drv.Initialize();
            TDisplay.driver = drv;
            TScreen.ScreenBuffer = drv.AllocateScreenBuffer();
        }

        try
        {
            StreamableRegistration.RegisterAll();

            var fp = new Fpstream(tvrPath);
            TDialog dlg = null;
            try
            {
                var rf = new TResourceFile(fp);
                dlg = rf.Get(key) as TDialog;
            }
            finally
            {
                fp.Close();
            }

            if (dlg == null)
                return "deserialized as null (type mismatch or registry error)";

            if (dlg.size.x <= 0 || dlg.size.y <= 0)
                return $"invalid size ({dlg.size.x}, {dlg.size.y})";

            // Enumerate children to verify the child list is intact.
            int childCount = 0;
            dlg.ForEachView(_ => childCount++);
            dlg.ShutDown();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            if (drv != null)
                TDisplay.driver = prevDriver;
        }
    }

    private static string TryDeserializeMenuBarForValidation(string tvrPath, string key)
    {
        var prevDriver = TDisplay.driver;
        NullDriver drv = null;
        if (TDisplay.driver == null)
        {
            drv = new NullDriver(80, 25);
            drv.Initialize();
            TDisplay.driver = drv;
            TScreen.ScreenBuffer = drv.AllocateScreenBuffer();
        }
        try
        {
            StreamableRegistration.RegisterAll();
            var fp = new Fpstream(tvrPath);
            TMenuBar mb = null;
            try { var rf = new TResourceFile(fp); mb = rf.Get(key) as TMenuBar; }
            finally { fp.Close(); }

            if (mb == null) return "deserialized as null (type mismatch or registry error)";
            mb.ShutDown();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
        finally { if (drv != null) TDisplay.driver = prevDriver; }
    }

    private static string TryDeserializeStatusLineForValidation(string tvrPath, string key)
    {
        var prevDriver = TDisplay.driver;
        NullDriver drv = null;
        if (TDisplay.driver == null)
        {
            drv = new NullDriver(80, 25);
            drv.Initialize();
            TDisplay.driver = drv;
            TScreen.ScreenBuffer = drv.AllocateScreenBuffer();
        }
        try
        {
            StreamableRegistration.RegisterAll();
            var fp = new Fpstream(tvrPath);
            TStatusLine sl = null;
            try { var rf = new TResourceFile(fp); sl = rf.Get(key) as TStatusLine; }
            finally { fp.Close(); }

            if (sl == null) return "deserialized as null (type mismatch or registry error)";
            sl.ShutDown();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
        finally { if (drv != null) TDisplay.driver = prevDriver; }
    }

    // ── hex dump formatter ────────────────────────────────────────────────────

    /// <summary>
    /// Formats <paramref name="data"/> as a standard hex dump:
    /// 16 bytes per line, address | hex | ASCII.
    /// Output is deterministic (pure function of input bytes).
    /// </summary>
    public static string FormatHexDump(byte[] data)
    {
        const int bytesPerLine = 16;
        var sb = new StringBuilder();

        for (int off = 0; off < data.Length; off += bytesPerLine)
        {
            int lineLen = Math.Min(bytesPerLine, data.Length - off);

            // Address column.
            sb.Append($"{off:X8}  ");

            // Hex bytes.
            for (int i = 0; i < lineLen; i++)
            {
                sb.Append($"{data[off + i]:X2}");
                if (i < lineLen - 1) sb.Append(' ');
            }

            // Padding to keep ASCII column aligned.
            if (lineLen < bytesPerLine)
                sb.Append(new string(' ', (bytesPerLine - lineLen) * 3));

            // ASCII column.
            sb.Append("  |");
            for (int i = 0; i < lineLen; i++)
            {
                byte b = data[off + i];
                sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }
            sb.AppendLine("|");
        }

        return sb.ToString().TrimEnd();
    }
}
