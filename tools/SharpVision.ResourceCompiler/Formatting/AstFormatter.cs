// SharpVision Resource Compiler — AstFormatter
// Canonical .trc formatter: parses the AST and re-emits deterministic text.
//
// Design notes:
// - Comments are STRIPPED in v1. This is intentional and documented.
// - Output uses LF line endings.
// - Output is deterministic and idempotent: format(format(x)) == format(x).
// - Top-level resource order is preserved (not sorted).
// - const declarations are preserved in declaration order.
// - Indentation is 2 spaces per level.
using System.Text;

namespace SharpVision.ResourceCompiler;

/// <summary>
/// Canonical AST-based formatter for .trc source files.
/// Converts a <see cref="TrcFile"/> AST back to deterministic .trc text.
/// <para>
/// <b>Comments are stripped</b> in this version. The formatter is a
/// canonicalizer, not a lossless round-trip printer.
/// </para>
/// </summary>
public sealed class AstFormatter
{
    /// <summary>
    /// Re-emits <paramref name="ast"/> as canonical .trc text.
    /// </summary>
    public string Format(TrcFile ast)
    {
        var sb = new StringBuilder();
        bool wroteAnything = false;

        // 1. const declarations (original order, no sorting).
        foreach (var c in ast.Consts)
        {
            sb.Append("const ").Append(c.Name).Append(" = ").Append(c.Value).Append(";\n");
            wroteAnything = true;
        }

        // 2. Resource declarations (original order, one blank line between items).
        foreach (var r in ast.Resources)
        {
            if (wroteAnything) sb.Append('\n');
            WriteResource(sb, r);
            wroteAnything = true;
        }

        // 3. Final trailing newline (idempotent: only add if not already there).
        if (sb.Length > 0 && sb[sb.Length - 1] != '\n')
            sb.Append('\n');

        return sb.ToString();
    }

    // ── Resource dispatcher ────────────────────────────────────────────────

    private static void WriteResource(StringBuilder sb, ResourceDecl r)
    {
        string kindStr = r.Kind switch
        {
            ResourceKind.Dialog    => "dialog",
            ResourceKind.Menu      => "menu",
            ResourceKind.StatusBar => "statusbar",
            _                      => "dialog",
        };

        sb.Append("resource ").Append(kindStr).Append(' ')
          .Append(QuoteString(r.Key)).Append(" {\n");

        switch (r.Kind)
        {
            case ResourceKind.Dialog:    WriteDialog(sb, r.Dialog);       break;
            case ResourceKind.Menu:      WriteMenu(sb, r.Menu);           break;
            case ResourceKind.StatusBar: WriteStatusBar(sb, r.StatusBar); break;
        }

        sb.Append("}\n");
    }

    // ── Dialog ────────────────────────────────────────────────────────────

    private static void WriteDialog(StringBuilder sb, DialogBody d)
    {
        if (d == null) return;

        // Canonical field order: bounds, title, palette.
        if (d.Bounds != null)
            sb.Append("  bounds ").Append(FormatBounds(d.Bounds)).Append(";\n");

        if (d.Title != null)
            sb.Append("  title ").Append(QuoteString(d.Title)).Append(";\n");

        if (d.Palette != null)
            sb.Append("  palette ").Append(d.Palette).Append(";\n");

        // Blank line between header fields and controls when both exist.
        if (d.Controls.Count > 0 &&
            (d.Bounds != null || d.Title != null || d.Palette != null))
            sb.Append('\n');

        foreach (var ctrl in d.Controls)
            WriteControl(sb, ctrl);
    }

    private static void WriteControl(StringBuilder sb, ControlDecl ctrl)
    {
        string kindStr = ctrl.Kind switch
        {
            ControlKind.Button   => "button",
            ControlKind.Static   => "static",
            ControlKind.Label    => "label",
            ControlKind.Input    => "input",
            ControlKind.Checkbox => "checkbox",
            ControlKind.Radio    => "radio",
            _                    => "static",
        };

        sb.Append("  ").Append(kindStr).Append(' ').Append(QuoteString(ctrl.Title));

        if (ctrl.Bounds != null)
            sb.Append(" bounds=").Append(FormatBounds(ctrl.Bounds));

        if (!string.IsNullOrEmpty(ctrl.Command))
            sb.Append(" command=").Append(ctrl.Command);

        if (ctrl.IsDefault)
            sb.Append(" default");

        if (ctrl.Validator != null)
            sb.Append(' ').Append(FormatValidator(ctrl.Validator));

        if (ctrl.Items != null && ctrl.Items.Count > 0)
        {
            sb.Append(" items=(");
            for (int i = 0; i < ctrl.Items.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(QuoteString(ctrl.Items[i]));
            }
            sb.Append(')');
        }

        sb.Append(";\n");
    }

    private static string FormatValidator(ValidatorNode v)
    {
        return v switch
        {
            FilterValidatorNode f  => $"validator=filter({QuoteString(f.ValidChars)})",
            RangeValidatorNode  r  => $"validator=range({r.Min}, {r.Max})",
            PictureValidatorNode p => $"validator=picture({QuoteString(p.Pic)})",
            _                      => string.Empty,
        };
    }

    // ── Menu ──────────────────────────────────────────────────────────────

    private static void WriteMenu(StringBuilder sb, MenuBody m)
    {
        if (m == null) return;

        if (m.Bounds != null)
            sb.Append("  bounds ").Append(FormatBounds(m.Bounds)).Append(";\n");

        if (m.Items.Count > 0 && m.Bounds != null)
            sb.Append('\n');

        foreach (var item in m.Items)
            WriteMenuItem(sb, item, 1);
    }

    private static void WriteMenuItem(StringBuilder sb, MenuItemDecl item, int depth)
    {
        string indent = new string(' ', depth * 2);

        switch (item.Kind)
        {
            case MenuItemKind.Separator:
                sb.Append(indent).Append("separator;\n");
                break;

            case MenuItemKind.Item:
                sb.Append(indent).Append("item ").Append(QuoteString(item.Title));
                if (!string.IsNullOrEmpty(item.Command))
                    sb.Append(" command=").Append(item.Command);
                if (!string.IsNullOrEmpty(item.Key))
                    sb.Append(" key=").Append(QuoteString(item.Key));
                sb.Append(";\n");
                break;

            case MenuItemKind.Submenu:
                sb.Append(indent).Append("submenu ").Append(QuoteString(item.Title)).Append(" {\n");
                foreach (var child in item.Children)
                    WriteMenuItem(sb, child, depth + 1);
                sb.Append(indent).Append("}\n");
                break;
        }
    }

    // ── StatusBar ─────────────────────────────────────────────────────────

    private static void WriteStatusBar(StringBuilder sb, StatusBarBody s)
    {
        if (s == null) return;

        if (s.Bounds != null)
            sb.Append("  bounds ").Append(FormatBounds(s.Bounds)).Append(";\n");

        if (s.Ranges.Count > 0 && s.Bounds != null)
            sb.Append('\n');

        foreach (var range in s.Ranges)
        {
            sb.Append("  range ").Append(range.Min).Append(' ').Append(range.Max).Append(" {\n");
            foreach (var item in range.Items)
            {
                sb.Append("    item ").Append(QuoteString(item.Text));
                if (!string.IsNullOrEmpty(item.Command))
                    sb.Append(" command=").Append(item.Command);
                if (!string.IsNullOrEmpty(item.Key))
                    sb.Append(" key=").Append(QuoteString(item.Key));
                sb.Append(";\n");
            }
            sb.Append("  }\n");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string FormatBounds(BoundsNode b)
        => $"({b.X1}, {b.Y1}, {b.X2}, {b.Y2})";

    /// <summary>
    /// Wraps <paramref name="s"/> in double quotes and escapes special characters.
    /// </summary>
    internal static string QuoteString(string s)
    {
        if (s == null) return "\"\"";
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:   sb.Append(c);      break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
