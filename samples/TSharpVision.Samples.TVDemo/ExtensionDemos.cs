using System.Text;
using TSharpVision.CodeEditor;
using TSharpVision.Constants;
using TSharpVision.HexView;
using TSharpVision.TableView;

namespace TSharpVision.Samples.TVDemo;

public partial class TVDemoApp
{
    private void OpenCodeEditorDemo()
    {
        if (DeskTop == null) return;
        var win = new DemoCodeWindow(DemoBounds(72, 19), _nextWinNum++);
        win.CodeEditor.InsertText("using System;\n\n" +
            "public static class Sample\n{\n" +
            "    public static int Square(int value)\n" +
            "    {\n        return value * value;\n    }\n}\n\n" +
            "Console.WriteLine(Sample.Square(12));\n");
        for (int n = 1; n <= 24; n++)
            win.CodeEditor.InsertText($"// Row {n:00}: edit this code and scroll to explore.\n");
        win.CodeEditor.SetSelect(0, 0, false);
        win.CodeEditor.modified = false;
        win.CodeEditor.SetSyntaxScope("source.cs");
        InsertDemoWindow(win);
    }

    private void OpenHexViewDemo()
    {
        if (DeskTop == null) return;
        var win = new TWindow(DemoBounds(72, 18), "Hex Viewer", _nextWinNum++);
        int w = win.size.x, h = win.size.y;
        var scroll = new TScrollBar(new TRect(w - 2, 1, w - 1, h - 1));
        win.Insert(scroll);
        var hex = new THexView(new TRect(1, 1, w - 2, h - 1), scroll);
        win.Insert(hex);
        hex.SetDataSource(new DemoHexSource());
        win.SelectNext(false);
        InsertDemoWindow(win);
    }

    private void OpenTableViewDemo()
    {
        if (DeskTop == null) return;
        var win = new TWindow(DemoBounds(74, 19), "Table Viewer", _nextWinNum++);
        int w = win.size.x, h = win.size.y;
        var vertical = new TScrollBar(new TRect(w - 2, 1, w - 1, h - 2));
        var horizontal = new TScrollBar(new TRect(1, h - 2, w - 2, h - 1));
        win.Insert(vertical);
        win.Insert(horizontal);
        var table = new TTableView(new TRect(1, 1, w - 2, h - 2), horizontal, vertical);
        win.Insert(table);
        table.SetDataSource(new DemoTableSource());
        win.SelectNext(false);
        InsertDemoWindow(win);
    }
}

internal sealed class DemoCodeWindow : TCodeWindow
{
    public DemoCodeWindow(TRect bounds, ushort number) : base(bounds, null, number) { }

    public override string GetTitle(short maxSize) => "Code Editor (C#)";
}

internal sealed class DemoHexSource : IHexDataSource
{
    private static readonly byte[] Caption = Encoding.ASCII.GetBytes("TSharpVision sample data  ");
    public long? Length => 4096;

    public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int count = (int)Math.Min(destination.Length, Length!.Value - offset);
        for (int i = 0; i < count; i++)
            destination.Span[i] = (offset + i) % 64 < Caption.Length
                ? Caption[(offset + i) % 64]
                : (byte)((offset + i) & 0xff);
        return ValueTask.FromResult(count);
    }
}

internal sealed class DemoTableSource : ITableDataSource
{
    public IReadOnlyList<TableColumn> Columns { get; } =
    [
        new("Id", 6, TableAlignment.Right), new("Name", 19), new("Category", 14),
        new("Value", 10, TableAlignment.Right), new("Status", 12), new("Owner", 14)
    ];

    public long? RowCount => 250;

    public ValueTask<TableRowBlock> ReadRowsAsync(long start, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int available = (int)Math.Clamp(RowCount!.Value - start, 0, count);
        var rows = new TableRow[available];
        string[] categories = ["Input", "Views", "Menus", "Editors"];
        string[] statuses = ["Ready", "Draft", "Review"];
        for (int i = 0; i < available; i++)
        {
            long id = start + i + 1;
            rows[i] = new TableRow(
            [
                new(id.ToString()), new($"Component {id:000}"),
                new(categories[(int)(id % categories.Length)]),
                new((id * 17.25m).ToString("0.00")),
                new(statuses[(int)(id % statuses.Length)]), new($"Team {(id % 5) + 1}")
            ], label: id.ToString());
        }
        return ValueTask.FromResult(new TableRowBlock(start, rows, start + available >= RowCount));
    }
}
