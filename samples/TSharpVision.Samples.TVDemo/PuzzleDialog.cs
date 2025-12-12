using TSharpVision;
using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

// ---------------------------------------------------------------------------
// PuzzleModel — pure 15-puzzle logic, no UI dependency.
// Board is a 4×4 grid.  Tiles 1..15 plus blank (0).
// Solved state: 1 2 3 4 / 5 6 7 8 / 9 10 11 12 / 13 14 15 0.
// Testable without any TView or TEvent.
// ---------------------------------------------------------------------------
public sealed class PuzzleModel
{
    private readonly int[] _board = new int[16];

    // Number of moves made since last reset/shuffle.
    public int Moves { get; private set; }

    // Constructs a solved board.
    public PuzzleModel() => Reset();

    // Resets board to the solved state.
    public void Reset()
    {
        for (int i = 0; i < 15; i++) _board[i] = i + 1;
        _board[15] = 0;   // blank
        Moves = 0;
    }

    // Returns a copy of the board array (index 0..15, row-major 4×4).
    public int[] GetBoard() => (int[])_board.Clone();

    // Returns the tile value at row r, column c (0-based).
    public int At(int r, int c) => _board[r * 4 + c];

    // Returns true if the board is in the solved state.
    public bool IsSolved()
    {
        for (int i = 0; i < 15; i++)
            if (_board[i] != i + 1) return false;
        return _board[15] == 0;
    }

    // Finds the position of the blank tile (index in [0..15]).
    private int BlankIndex()
    {
        for (int i = 0; i < 16; i++)
            if (_board[i] == 0) return i;
        return 15;  // should not happen
    }

    // Attempts to slide the tile at the given row/col into the blank.
    // Returns true if the move was valid, false if the tile is not adjacent to blank.
    public bool TryMove(int r, int c)
    {
        int idx = r * 4 + c;
        int blank = BlankIndex();
        int br = blank / 4;
        int bc = blank % 4;
        // Valid only if exactly one of (row or col) differs by 1.
        bool adjacent = (r == br && Math.Abs(c - bc) == 1)
                     || (c == bc && Math.Abs(r - br) == 1);
        if (!adjacent) return false;
        _board[blank] = _board[idx];
        _board[idx]   = 0;
        Moves++;
        return true;
    }

    // Key-based movement: move the tile adjacent to the blank in the given direction.
    // kbUp moves the tile below blank upward; kbDown moves tile above blank down; etc.
    // Matches upstream puzzle.cc convention.
    public bool MoveKey(ushort keyCode)
    {
        int blank = BlankIndex();
        int br = blank / 4;
        int bc = blank % 4;
        return keyCode switch
        {
            Keys.kbUp    when br < 3 => TryMove(br + 1, bc),  // tile below blank slides up
            Keys.kbDown  when br > 0 => TryMove(br - 1, bc),  // tile above blank slides down
            Keys.kbLeft  when bc < 3 => TryMove(br, bc + 1),  // tile right of blank slides left
            Keys.kbRight when bc > 0 => TryMove(br, bc - 1),  // tile left of blank slides right
            _ => false
        };
    }

    // Shuffles the board using a sequence of valid random moves.
    // Uses seeded random for deterministic smoke tests.
    public void Shuffle(int seed = 0, int moves = 200)
    {
        var rng = seed == 0
            ? new Random()
            : new Random(seed);

        Reset();
        Moves = 0;
        for (int i = 0; i < moves; )
        {
            ushort key = (rng.Next() % 4) switch
            {
                0 => Keys.kbUp,
                1 => Keys.kbDown,
                2 => Keys.kbLeft,
                _ => Keys.kbRight
            };
            if (MoveKey(key)) i++;  // only count successful moves
        }
        Moves = 0;  // reset move counter after shuffle
    }
}

// ---------------------------------------------------------------------------
// PuzzleView — TView that renders the 15-puzzle board.
// Handles arrow keys and mouse clicks.
// ---------------------------------------------------------------------------
internal sealed class PuzzleView : TView
{
    // Board display: 4 cols × 4 rows.  Each cell is 4 chars wide × 1 tall.
    // Total visible area: 16 wide × 4 tall + 1 row for move count.
    public const int ViewW = 16;
    public const int ViewH = 5;   // 4 rows + 1 move-count row

    public PuzzleModel Model { get; }

    private static readonly TPalette _palette = new TPalette("\x06\x07", 2);
    public override TPalette GetPalette() => _palette;

    public PuzzleView(TRect bounds, PuzzleModel model) : base(bounds)
    {
        Model = model;
        options |= Views.ofSelectable;
        eventMask |= Events.evMouseDown;
    }

    public override void Draw()
    {
        var color     = (char)GetColor(1);
        var solColor  = (char)GetColor(2);

        for (int row = 0; row < 4; row++)
        {
            var b = new TDrawBuffer();
            b.moveChar(0, ' ', color, size.x);
            for (int col = 0; col < 4; col++)
            {
                int val = Model.At(row, col);
                if (val == 0)
                    b.moveStr(col * 4, "    ", color);       // blank
                else
                {
                    string cell = $"{val,3} ";
                    b.moveStr(col * 4, cell, color);
                }
            }
            WriteLine(0, row, size.x, 1, b);
        }

        // Row 4: move count and solved indicator.
        var bm = new TDrawBuffer();
        bm.moveChar(0, ' ', color, size.x);
        string status = Model.IsSolved()
            ? $"Moves:{Model.Moves,4}  SOLVED!"
            : $"Moves:{Model.Moves,4}";
        bm.moveStr(0, status, Model.IsSolved() ? solColor : color);
        WriteLine(0, 4, size.x, 1, bm);
    }

    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);

        bool changed = false;

        if (ev.What == Events.evKeyboard)
        {
            changed = Model.MoveKey(ev.keyDown.keyCode);
            if (changed) ClearEvent(ref ev);
        }
        else if (ev.What == Events.evMouseDown)
        {
            var pt = MakeLocal(ev.mouse.where);
            int col = pt.x / 4;
            int row = pt.y;
            if (col >= 0 && col < 4 && row >= 0 && row < 4)
            {
                changed = Model.TryMove(row, col);
                ClearEvent(ref ev);
            }
        }

        if (changed) DrawView();
    }
}

// ---------------------------------------------------------------------------
// PuzzleDialog — TDialog hosting the PuzzleView + Shuffle button.
// Fixed size, no resize/grow/zoom.  Opened modeless by TVDemoApp.
// ---------------------------------------------------------------------------
public sealed class PuzzleDialog : TDialog
{
    // Inner area: ViewW + 2 frame = 18; add 2 each side padding = 22 min.
    public const int DlgW = PuzzleView.ViewW + 4;   // 20
    public const int DlgH = PuzzleView.ViewH + 4;   // 9

    // Command for the Shuffle button.
    public const ushort CmShuffle = 350;

    internal PuzzleView View { get; }

    public PuzzleDialog(int left = 30, int top = 3)
        : base(new TRect(left, top, left + DlgW, top + DlgH), "Puzzle")
    {
        // TDialog already sets growMode=0, flags=wfMove|wfClose.
        var model = new PuzzleModel();
        model.Shuffle();
        View = new PuzzleView(new TRect(2, 1, 2 + PuzzleView.ViewW, 1 + PuzzleView.ViewH), model);
        Insert(View);

        // "Shuffle" button on the last row.
        Insert(new TButton(
            new TRect(2, DlgH - 3, DlgW - 2, DlgH - 1),
            "~S~huffle", CmShuffle, ButtonConstants.bfNormal));
    }

    // Expose model for smoke tests.
    public PuzzleModel Model => View.Model;

    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);
        if (ev.What == Events.evCommand && ev.message.command == CmShuffle)
        {
            Model.Shuffle();
            View.DrawView();
            ClearEvent(ref ev);
        }
    }
}
