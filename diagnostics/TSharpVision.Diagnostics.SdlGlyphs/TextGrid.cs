namespace TSharpVision.Diagnostics.SdlGlyphs;

/// <summary>One character cell of a diagnostic scene: character plus VGA colour indices.</summary>
internal readonly record struct SceneCell(char Character, byte Foreground, byte Background)
{
    public static readonly SceneCell Empty = new(' ', 7, 0);
}

/// <summary>
/// A rectangular grid of <see cref="SceneCell"/>s — the diagnostic equivalent of a
/// <c>ScreenBuffer</c> region. Scenes are described here in character terms and only then
/// rasterised by <see cref="SceneRenderer"/>, so a scene's layout can be reviewed as text.
/// </summary>
internal sealed class TextGrid
{
    private readonly SceneCell[] _cells;

    public int Columns { get; }
    public int Rows    { get; }

    public TextGrid(int columns, int rows, byte foreground = 7, byte background = 0)
    {
        Columns = columns;
        Rows    = rows;
        _cells  = new SceneCell[columns * rows];
        Array.Fill(_cells, new SceneCell(' ', foreground, background));
    }

    public SceneCell this[int col, int row]
    {
        get => (uint)col < (uint)Columns && (uint)row < (uint)Rows
            ? _cells[row * Columns + col]
            : SceneCell.Empty;
        set
        {
            if ((uint)col < (uint)Columns && (uint)row < (uint)Rows)
                _cells[row * Columns + col] = value;
        }
    }

    public void Put(int col, int row, char ch, byte fg, byte bg) =>
        this[col, row] = new SceneCell(ch, fg, bg);

    public void PutString(int col, int row, string text, byte fg, byte bg)
    {
        for (int i = 0; i < text.Length; i++)
            Put(col + i, row, text[i], fg, bg);
    }

    public void Repeat(int col, int row, char ch, int count, byte fg, byte bg)
    {
        for (int i = 0; i < count; i++)
            Put(col + i, row, ch, fg, bg);
    }

    public void FillRect(int col, int row, int width, int height, char ch, byte fg, byte bg)
    {
        for (int r = 0; r < height; r++)
            for (int c = 0; c < width; c++)
                Put(col + c, row + r, ch, fg, bg);
    }

    /// <summary>Stamps a block of scene text (one char per cell); spaces are written as-is.</summary>
    public void PutBlock(int col, int row, IReadOnlyList<string> lines, byte fg, byte bg)
    {
        for (int r = 0; r < lines.Count; r++)
            PutString(col, row + r, lines[r], fg, bg);
    }
}
