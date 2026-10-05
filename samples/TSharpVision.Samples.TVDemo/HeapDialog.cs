using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

// Sampling and rendering stay on the UI thread through TVDemoApp.Idle().
internal sealed class HeapContentView : TView
{
    public const int ViewW = 52;
    public const int ViewH = 11;
    internal const int HistoryCapacity = 48;
    internal const int GraphHeight = 5;
    internal const int SampleIntervalMs = 500;

    public Func<long> GetMemory { get; set; } = () => GC.GetTotalMemory(false);
    internal Func<int, int> GetCollectionCount { get; set; } = GC.CollectionCount;
    internal Func<long> GetMilliseconds { get; set; } = () => Environment.TickCount64;
    private readonly Queue<long> _history = new(HistoryCapacity);
    private readonly int[] _collections = new int[3];
    private long _lastSample;
    internal long Current { get; private set; }
    internal long Peak { get; private set; }
    internal IReadOnlyCollection<long> History => _history;
    internal long Scale => Math.Max(Peak, _history.Count == 0 ? 0 : _history.Max());

    public HeapContentView(TRect bounds) : base(bounds) { }

    public static string FormatBytes(long bytes) => bytes >= 1024L * 1024L
        ? $"{bytes / (1024.0 * 1024.0):F2} MB" : $"{bytes / 1024} KB";

    public bool Tick()
    {
        long now = GetMilliseconds();
        if (_history.Count != 0 && now - _lastSample < SampleIntervalMs) return false;
        _lastSample = now;
        Current = GetMemory();
        Peak = Math.Max(Peak, Current);
        if (_history.Count == HistoryCapacity) _history.Dequeue();
        _history.Enqueue(Current);
        for (int gen = 0; gen < _collections.Length; gen++)
            _collections[gen] = GetCollectionCount(gen);
        DrawView();
        return true;
    }

    internal void ResetPeak()
    {
        Peak = Current;
        DrawView();
    }

    internal string TextLine(int row)
    {
        switch (row)
        {
            case 0: return $"Current: {FormatBytes(Current)}   Peak: {FormatBytes(Peak)}";
            case 1: return $"GC counts: Gen0 {_collections[0]}  Gen1 {_collections[1]}  Gen2 {_collections[2]}";
            case 2: return $" ";
            case 3: return $"History (24 s)   Scale: 0 - {FormatBytes(Scale)}";
            case 9: return "0 +" + new string('-', HistoryCapacity);
            case 10: return "  Older -> newer / 500 ms per sample";
        }
        if (row < 4 || row >= 4 + GraphHeight) return string.Empty;
        var cells = new char[HistoryCapacity];
        Array.Fill(cells, ' ');
        int x = HistoryCapacity - _history.Count;
        long scale = Scale;
        foreach (long sample in _history)
        {
            int height = scale == 0 ? 0 : (int)Math.Ceiling((double)sample / scale * GraphHeight);
            cells[x++] = height >= GraphHeight - (row - 4) ? '#' : ' ';
        }
        return "  |" + new string(cells);
    }

    public override void Draw()
    {
        var color = (char)GetColor(1);
        var buffer = new TDrawBuffer();
        for (int y = 0; y < size.y; y++)
        {
            buffer.moveChar(0, ' ', color, size.x);
            string line = TextLine(y);
            buffer.moveStr(0, line[..Math.Min(line.Length, size.x)], color);
            WriteLine(0, (short)y, size.x, 1, buffer);
        }
    }
}

public sealed class HeapDialog : TDialog
{
    public const int DlgW = HeapContentView.ViewW + 4;
    public const int DlgH = 16;
    internal const ushort ResetPeakCommand = 0x7E10;
    internal HeapContentView View { get; }

    public HeapDialog(int left = 12, int top = 3)
        : base(new TRect(left, top, left + DlgW, top + DlgH), "Memory")
    {
        View = new HeapContentView(
            new TRect(2, 1, 2 + HeapContentView.ViewW, 1 + HeapContentView.ViewH));
        Insert(View);
        Insert(new TButton(new TRect(2, 13, 18, 15), "~R~eset Peak", ResetPeakCommand,
            ButtonConstants.bfNormal));
        View.Tick();
        // Preserve the frame close icon and standard cmClose handling.
    }

    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);
        if (ev.What == Events.evCommand && ev.message.command == ResetPeakCommand)
        {
            View.ResetPeak();
            ClearEvent(ref ev);
        }
    }

    public void Tick() => View.Tick();
    public static string FormatBytes(long bytes) => HeapContentView.FormatBytes(bytes);
}
