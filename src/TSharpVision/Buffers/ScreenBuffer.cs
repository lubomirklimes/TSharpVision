namespace TSharpVision;

public class ScreenBuffer
{
    private readonly uint _width;
    private readonly uint _height;
    //private readonly TScreenChar[,] _buffer;
    private readonly TScreenChar[] _buffer;

    public uint Width => _width - 1;
    public uint Height => _height - 1;


    public Memory<TScreenChar> BufferMemory => _buffer;

    public Span<TScreenChar> Data => _buffer;

    public ScreenBuffer(int size)
    {
        _buffer = new TScreenChar[size];
    }

    public ScreenBuffer(uint width, uint height)
    {
        _width = width;
        _height = height;
        //_buffer = new TScreenChar[width, height];
        _buffer = new TScreenChar[width * height];

        Clear();
    }

    public static int GetSize()
    {
        //return (int)(_width * _height);
        return 2;
    }

    public TDrawBuffer GetSpan()
    {
        //return new TDrawBuffer(_buffer);
        return new TDrawBuffer();
    }


    private long Index(uint x, uint y) => y * _width + x;

    public void SetChar(uint x, uint y, TScreenChar c)
    {
        _buffer[Index(x, y)] = c;
    }

    //public void SetChar(uint x, uint y, TScreenChar screenChar)
    //{
    //    if (x >= 0 && x < _width && y >= 0 && y < _height)
    //        _buffer[x, y] = screenChar;
    //}

    public TScreenChar GetChar(uint x, uint y) => _buffer[Index(x, y)];
    //{
    //    return _buffer[x, y];
    //}

    public void Clear()
    {
        //for (int x = 0; x < _width; x++)
        //    for (int y = 0; y < _height; y++)
        //        _buffer[x, y] = new TScreenChar(' ', ConsoleColor.White, ConsoleColor.Black);

        for (int i = 0; i < _buffer.Length; i++)
            _buffer[i] = new TScreenChar(' ', ConsoleColor.White, ConsoleColor.Black);
    }

    //public void Bind(ref byte[] screenBuffer)
    //{
    //    throw new NotImplementedException();
    //}
}
