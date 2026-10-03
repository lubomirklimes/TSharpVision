namespace TSharpVision.Samples.TVDemo;

internal static class DemoButtonLayout
{
    internal static void CenterRows(TDialog dialog)
    {
        var buttons = new List<TButton>();
        dialog.ForEachView(view =>
        {
            if (view is TButton button) buttons.Add(button);
        });
        foreach (var row in buttons.GroupBy(button => button.GetBounds().a.y))
        {
            int left = row.Min(button => button.GetBounds().a.x);
            int right = row.Max(button => button.GetBounds().b.x);
            // Window-local interior is [1, size.x - 1), excluding both frame cells.
            int offset = 1 + (dialog.size.x - 2 - (right - left)) / 2 - left;
            foreach (var button in row)
            {
                var bounds = button.GetBounds();
                button.MoveTo(bounds.a.x + offset, bounds.a.y);
            }
        }
    }
}
