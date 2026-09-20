using System;
using System.Threading;
using TSharpVision;
using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

// ─────────────────────────────────────────────────────────────────────────────
// TTetrisWindow — thin TWindow wrapper that hosts TTetrisView
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class TTetrisWindow : TWindow
{
    public override byte MapColor(int index) => DemoAppearance.GrayWindow(index);
    private readonly TTetrisView _game;

    public TTetrisWindow(TRect r) : base(r, "TETRIS", Views.wnNoNumber)
    {
        flags  = (byte)(Views.wfMove | Views.wfClose);
        palette = (short)Views.wpBlueWindow;

        // Inner view fills the window interior (frame eats 1 char on each edge)
        _game = new TTetrisView(new TRect(1, 1, size.x - 1, size.y - 1));
        Insert(_game);
    }

    public override void Close()
    {
        _game.StopTimer();
        base.Close();
    }

    public override void ShutDown()
    {
        _game.StopTimer();
        base.ShutDown();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// TTetrisView — all game logic, drawing, and input handling
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class TTetrisView : TView
{
    // ── Custom broadcast command for the game timer ────────────────────────
    private const ushort CmdTick = 311;

    // ── Board dimensions ───────────────────────────────────────────────────
    private const int W = 10;
    private const int H = 20;

    // ── VGA color attributes ─────────────────────────────────────────────────
    // Empty cells and panels share the gray window background.
    private const byte AttrEmpty    = 0x70; // black on gray
    private const char PieceChar    = '█';    // full block; foreground controls the piece color
    // Seven contrasting foreground colors on gray.
    private static readonly byte[] PieceAttrs =
    {
        0x73,  // I – cyan
        0x76,  // O – brown
        0x75,  // T – magenta
        0x72,  // S – green
        0x74,  // Z – red
        0x71,  // J – blue
        0x78,  // L – dark gray
    };
    private const byte AttrSep    = 0x70; // black on gray
    private const byte AttrPanel  = 0x70; // black on gray
    private const byte AttrLabel  = 0x70; // black on gray
    private const byte AttrValue  = 0x70; // black on gray
    private const byte AttrGameOv = 0x4F;  // white on red        (game-over overlay)
    private const byte AttrPause  = 0x70; // black on gray

    // ── Piece definitions [pieceType][rotation][cell*2] = {dx,dy} ─────────
    // Each piece has 4 rotations; each rotation lists 4 cells as (dx,dy) pairs
    // stored flat: dx0,dy0, dx1,dy1, dx2,dy2, dx3,dy3.
    private static readonly int[][][] Pieces = new int[][][]
    {
        // 0: I
        new int[][] {
            new int[] { 0,1, 1,1, 2,1, 3,1 },
            new int[] { 2,0, 2,1, 2,2, 2,3 },
            new int[] { 0,2, 1,2, 2,2, 3,2 },
            new int[] { 1,0, 1,1, 1,2, 1,3 },
        },
        // 1: O
        new int[][] {
            new int[] { 1,0, 2,0, 1,1, 2,1 },
            new int[] { 1,0, 2,0, 1,1, 2,1 },
            new int[] { 1,0, 2,0, 1,1, 2,1 },
            new int[] { 1,0, 2,0, 1,1, 2,1 },
        },
        // 2: T
        new int[][] {
            new int[] { 1,0, 0,1, 1,1, 2,1 },
            new int[] { 1,0, 1,1, 2,1, 1,2 },
            new int[] { 0,1, 1,1, 2,1, 1,2 },
            new int[] { 1,0, 0,1, 1,1, 1,2 },
        },
        // 3: S
        new int[][] {
            new int[] { 1,0, 2,0, 0,1, 1,1 },
            new int[] { 1,0, 1,1, 2,1, 2,2 },
            new int[] { 1,0, 2,0, 0,1, 1,1 },
            new int[] { 1,0, 1,1, 2,1, 2,2 },
        },
        // 4: Z
        new int[][] {
            new int[] { 0,0, 1,0, 1,1, 2,1 },
            new int[] { 2,0, 1,1, 2,1, 1,2 },
            new int[] { 0,0, 1,0, 1,1, 2,1 },
            new int[] { 2,0, 1,1, 2,1, 1,2 },
        },
        // 5: J
        new int[][] {
            new int[] { 0,0, 0,1, 1,1, 2,1 },
            new int[] { 1,0, 2,0, 1,1, 1,2 },
            new int[] { 0,1, 1,1, 2,1, 2,2 },
            new int[] { 1,0, 1,1, 0,2, 1,2 },
        },
        // 6: L
        new int[][] {
            new int[] { 2,0, 0,1, 1,1, 2,1 },
            new int[] { 1,0, 1,1, 1,2, 2,2 },
            new int[] { 0,1, 1,1, 2,1, 0,2 },
            new int[] { 0,0, 1,0, 1,1, 1,2 },
        },
    };

    // ── Game state ─────────────────────────────────────────────────────────
    private byte[,] _board = new byte[H, W];  // 0=empty, 1..7=piece type +1
    private int _curPiece, _curRot, _curX, _curY;
    private int _nextPiece;
    private int _score, _level, _lines;
    private bool _gameOver, _paused;
    private readonly Random _rng = new Random();

    // ── Timer (background thread posts CmdTick broadcasts) ─────────────────
    private Thread? _timerThread;
    private volatile bool _timerRunning;

    // Level-based tick interval in ms (level 1 = 800 ms, max level = 100 ms)
    private int TickInterval => Math.Max(100, 800 - (_level - 1) * 80);

    // ─────────────────────────────────────────────────────────────────────────
    public TTetrisView(TRect bounds) : base(bounds)
    {
        options   |= Views.ofSelectable;
        eventMask |= Events.evBroadcast;

        NewGame();
        StartTimer();
    }

    // ── New game ────────────────────────────────────────────────────────────
    private void NewGame()
    {
        _board    = new byte[H, W];
        _score    = 0;
        _level    = 1;
        _lines    = 0;
        _gameOver = false;
        _paused   = false;
        _nextPiece = _rng.Next(7);
        SpawnPiece();
    }

    private void SpawnPiece()
    {
        _curPiece = _nextPiece;
        _nextPiece = _rng.Next(7);
        _curRot   = 0;
        _curX     = 3;
        _curY     = -1;

        if (!IsValid(_curPiece, _curRot, _curX, _curY))
            _gameOver = true;
    }

    // ── Timer management ────────────────────────────────────────────────────
    private void StartTimer()
    {
        _timerRunning = true;
        _timerThread  = new Thread(TimerLoop)
        {
            IsBackground = true,
            Name         = "TetrisTick",
        };
        _timerThread.Start();
    }

    public void StopTimer() => _timerRunning = false;

    private void TimerLoop()
    {
        while (_timerRunning)
        {
            Thread.Sleep(TickInterval);
            if (!_timerRunning) break;

            TEvent ev = default;
            ev.What               = Events.evBroadcast;
            ev.message.command    = CmdTick;
            ev.message.infoPtr    = this;          // TView : IInfo → valid
            TEventQueue.Enqueue(ev);
        }
    }

    // ── Piece helpers ────────────────────────────────────────────────────────
    private void GetCells(int piece, int rot, int x, int y,
                          out (int col, int row)[] cells)
    {
        int[] def = Pieces[piece][rot];
        cells = new (int, int)[4];
        for (int i = 0; i < 4; i++)
            cells[i] = (x + def[i * 2], y + def[i * 2 + 1]);
    }

    private bool IsValid(int piece, int rot, int x, int y)
    {
        GetCells(piece, rot, x, y, out var cells);
        foreach (var (c, r) in cells)
        {
            if (c < 0 || c >= W)  return false;
            if (r >= H)           return false;
            if (r >= 0 && _board[r, c] != 0) return false;
        }
        return true;
    }

    private int GhostRow()
    {
        int gy = _curY;
        while (IsValid(_curPiece, _curRot, _curX, gy + 1))
            gy++;
        return gy;
    }

    private void LockPiece()
    {
        GetCells(_curPiece, _curRot, _curX, _curY, out var cells);
        foreach (var (c, r) in cells)
            if (r >= 0 && r < H && c >= 0 && c < W)
                _board[r, c] = (byte)(_curPiece + 1);

        ClearLines();
        SpawnPiece();
    }

    private void ClearLines()
    {
        int cleared = 0;
        for (int r = H - 1; r >= 0; r--)
        {
            bool full = true;
            for (int c = 0; c < W; c++)
                if (_board[r, c] == 0) { full = false; break; }

            if (!full) continue;

            for (int rr = r; rr > 0; rr--)
                for (int c = 0; c < W; c++)
                    _board[rr, c] = _board[rr - 1, c];
            for (int c = 0; c < W; c++)
                _board[0, c] = 0;
            r++;       // re-examine same row index after shift
            cleared++;
        }

        if (cleared <= 0) return;
        int[] pts = { 0, 100, 300, 500, 800 };
        _lines += cleared;
        _score += pts[Math.Min(cleared, 4)] * _level;
        _level  = _lines / 10 + 1;
    }

    private void TryRotate()
    {
        int newRot = (_curRot + 3) % 4;  // CCW; change +3→+1 for CW
        // Standard wall-kick candidates: 0, -1, +1, -2, +2
        int[] kicks = { 0, -1, +1, -2, +2 };
        foreach (int kx in kicks)
        {
            if (IsValid(_curPiece, newRot, _curX + kx, _curY))
            {
                _curX  += kx;
                _curRot = newRot;
                return;
            }
        }
    }

    private void Tick()
    {
        if (IsValid(_curPiece, _curRot, _curX, _curY + 1))
            _curY++;
        else
            LockPiece();
    }

    // ── Event handling ───────────────────────────────────────────────────────
    public override void HandleEvent(ref TEvent ev)
    {
        // ── Game timer tick ────────────────────────────────────────────────
        if (ev.What == Events.evBroadcast
            && ev.message.command == CmdTick
            && ReferenceEquals(ev.message.infoPtr, this))
        {
            if (!_gameOver && !_paused)
                Tick();
            DrawView();
            ClearEvent(ref ev);
            return;
        }

        // ── Keyboard ───────────────────────────────────────────────────────
        if (ev.What == Events.evKeyDown)
        {
            bool handled = true;
            switch (ev.keyDown.keyCode)
            {
                case Keys.kbLeft:
                    if (!_gameOver && !_paused
                        && IsValid(_curPiece, _curRot, _curX - 1, _curY))
                        _curX--;
                    break;

                case Keys.kbRight:
                    if (!_gameOver && !_paused
                        && IsValid(_curPiece, _curRot, _curX + 1, _curY))
                        _curX++;
                    break;

                case Keys.kbUp:
                    if (!_gameOver && !_paused)
                        TryRotate();
                    break;

                case Keys.kbDown:
                    if (!_gameOver && !_paused)
                    {
                        if (IsValid(_curPiece, _curRot, _curX, _curY + 1))
                            _curY++;
                        else
                            LockPiece();
                    }
                    break;

                default:
                    switch (ev.keyDown.charScan.charCode)
                    {
                        case (byte)' ':
                            // Hard drop — keyCode for space differs by driver (SDL: 0x20, Win32: kbSpace),
                            // so match by charCode instead.
                            if (!_gameOver && !_paused)
                            {
                                _curY = GhostRow();
                                LockPiece();
                            }
                            break;
                        case (byte)'p': case (byte)'P':
                            if (!_gameOver) _paused = !_paused;
                            break;
                        case (byte)'n': case (byte)'N':
                            if (_gameOver) NewGame();
                            break;
                        default:
                            handled = false;
                            break;
                    }
                    break;
            }

            if (handled)
            {
                DrawView();
                ClearEvent(ref ev);
                return;
            }
        }

        base.HandleEvent(ref ev);
    }

    // ── Drawing ──────────────────────────────────────────────────────────────
    public override void Draw()
    {
        // Pre-fill entire inner area with panel background so nothing lingers.
        for (int r = 0; r < size.y; r++)
            WriteChar(0, r, ' ', AttrPanel, size.x);

        // Board colour map
        byte[,] display = new byte[H, W];

        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
                display[r, c] = _board[r, c] == 0
                    ? AttrEmpty
                    : PieceAttrs[_board[r, c] - 1];

        if (!_gameOver)
        {
            // Current piece overlay
            GetCells(_curPiece, _curRot, _curX, _curY, out var cur);
            foreach (var (cc, cr) in cur)
                if (cr >= 0 && cr < H)
                    display[cr, cc] = PieceAttrs[_curPiece];
        }

        // Render board cells (each cell = 2 chars); pieces use PieceChar, empty cells use space
        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
            {
                bool filled = display[r, c] != AttrEmpty;
                WriteChar(c * 2, r, filled ? PieceChar : ' ', display[r, c], 2);
            }

        // Vertical separator between board and panel
        for (int r = 0; r < size.y; r++)
            WriteChar(W * 2, r, '│', AttrSep, 1);   // │

        // Side panel
        DrawPanel();

        // Overlays
        if (_gameOver)
        {
            WriteStr(1, H / 2 - 2, " *** GAME OVER *** ", AttrGameOv);
            WriteStr(3, H / 2,     "  N = new game    ", AttrGameOv);
        }
        else if (_paused)
        {
            WriteStr(3, H / 2, "    -- PAUSED --   ", AttrPause);
            WriteStr(3, H / 2 + 1, "   P = continue   ", AttrPause);
        }
    }

    private void DrawPanel()
    {
        int px = W * 2 + 1;   // panel X start (right of separator)
        int pw = size.x - px; // panel width

        // Score
        WriteStr(px, 0, "SCORE:", AttrLabel);
        WriteStr(px, 1, _score.ToString().PadLeft(pw), AttrValue);

        // Level
        WriteStr(px, 3, "LEVEL:", AttrLabel);
        WriteStr(px, 4, _level.ToString().PadLeft(pw), AttrValue);

        // Lines
        WriteStr(px, 6, "LINES:", AttrLabel);
        WriteStr(px, 7, _lines.ToString().PadLeft(pw), AttrValue);

        // Next piece preview
        WriteStr(px, 9, "NEXT:", AttrLabel);
        DrawNextPiece(px, 10);

        // Controls hint (only if panel is wide enough)
        if (pw >= 8 && size.y >= 20)
        {
            WriteStr(px, 15, "< >  move",   AttrPanel);
            WriteStr(px, 16, " ^   rot",    AttrPanel);
            WriteStr(px, 17, " v   fall",   AttrPanel);
            WriteStr(px, 18, "Spc  drop",   AttrPanel);
            WriteStr(px, 19, " P   pause",  AttrPanel);
        }
    }

    private void DrawNextPiece(int px, int py)
    {
        // Draw the next piece in its default rotation inside the panel
        int[]  def  = Pieces[_nextPiece][0];
        byte   attr = PieceAttrs[_nextPiece];

        for (int i = 0; i < 4; i++)
        {
            int dx = def[i * 2];
            int dy = def[i * 2 + 1];
            int r  = py + dy;
            int cx = px + dx * 2;
            if (r < size.y && cx + 1 < size.x)
                WriteChar(cx, r, PieceChar, attr, 2);
        }
    }
}



