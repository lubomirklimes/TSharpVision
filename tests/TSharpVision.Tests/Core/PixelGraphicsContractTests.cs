using TSharpVision.Drivers;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

/// <summary>TV12o core pixel-graphics contracts: <see cref="PixelImage"/>, the image primitive and the pre-present
/// synchronization point.</summary>
[Collection("NonParallel")]
public sealed class PixelGraphicsContractTests
{
    [Fact]
    public void Images_copy_their_pixels_and_honour_the_stride()
    {
        byte[] source = new byte[2 * 12];
        for (int i = 0; i < source.Length; i++) source[i] = (byte)i;
        var image = new PixelImage(2, 2, source, stride: 12);
        Assert.Equal((2, 2, 8), (image.Width, image.Height, image.Stride));
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 12, 13, 14, 15, 16, 17, 18, 19 }, image.Pixels.ToArray());
        source[0] = 99;
        Assert.Equal(0, image.Pixels[0]); // the caller's buffer is not referenced

        var tight = new PixelImage(1, 1, [1, 2, 3, 4]);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, tight.Pixels.ToArray());
        Assert.True(tight.Id > image.Id);
    }

    [Fact]
    public void Image_creation_is_bounds_checked_without_overflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelImage(0, 1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelImage(1, PixelImage.MaxDimension + 1, new byte[4]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelImage(PixelImage.MaxDimension, PixelImage.MaxDimension, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelImage(2, 1, new byte[8], stride: 4));
        Assert.Throws<ArgumentException>(() => new PixelImage(2, 2, new byte[15]));
        // The last row only needs one row of bytes, not a whole stride.
        Assert.Equal(2, new PixelImage(2, 2, new byte[12 + 8], stride: 12).Height);
        Assert.Throws<ArgumentNullException>(() => PixelImage.Create(1, 1, null!));
    }

    [Fact]
    public void Created_images_start_transparent_and_are_filled_once()
    {
        int calls = 0;
        var image = PixelImage.Create(3, 2, rgba =>
        {
            calls++;
            Assert.Equal(24, rgba.Length);
            Assert.All(rgba.ToArray(), b => Assert.Equal(0, b));
            rgba[^1] = 255;
        });
        Assert.Equal(1, calls);
        Assert.Equal(255, image.Pixels[23]);
    }

    [Fact]
    public void Image_primitives_carry_index_filter_and_opacity()
    {
        GraphicsPrimitive p = GraphicsPrimitive.Image(3, 10, 20, 30, 40, GraphicsImageFilter.Linear, 128);
        Assert.Equal(GraphicsPrimitiveKind.Image, p.Kind);
        Assert.Equal((10, 20, 40, 60), (p.X1, p.Y1, p.X2, p.Y2));
        Assert.Equal((3, GraphicsImageFilter.Linear, 128u), (p.ImageIndex, p.Filter, p.Rgba & 0xFF));
        // Existing primitives keep their shape and defaults.
        Assert.Equal(new GraphicsPrimitive(GraphicsPrimitiveKind.Line, 1, 2, 3, 4, 5), GraphicsPrimitive.Line(1, 2, 3, 4, 5));
        Assert.Equal(0, GraphicsPrimitive.FillRect(0, 0, 1, 1, 0).ImageIndex);
    }

    [Fact]
    public void Synchronizers_run_once_each_with_the_presenting_program_and_registration_is_idempotent()
    {
        using var scope = new DriverScope();
        var app = new TApplication();
        var seen = new List<string>();
        Action<TProgram> first = p => seen.Add("a:" + ReferenceEquals(p, app)), second = p => seen.Add("b:" + ReferenceEquals(p, app));
        int before = PixelGraphicsSynchronization.Count;
        try
        {
            PixelGraphicsSynchronization.Register(first);
            PixelGraphicsSynchronization.Register(first);
            PixelGraphicsSynchronization.Register(second);
            Assert.Equal(before + 2, PixelGraphicsSynchronization.Count);
            PixelGraphicsSynchronization.BeforePresent(app);
            Assert.Equal(["a:True", "b:True"], seen);
            // The second present of the same pass (TScreen.GetEvent) gets the same program.
            PixelGraphicsSynchronization.BeforePresent();
            Assert.Equal(["a:True", "b:True", "a:True", "b:True"], seen);
            PixelGraphicsSynchronization.Unregister(first);
            PixelGraphicsSynchronization.Unregister(first);
            seen.Clear();
            PixelGraphicsSynchronization.BeforePresent(app);
            Assert.Equal(["b:True"], seen);
        }
        finally
        {
            PixelGraphicsSynchronization.Unregister(first);
            PixelGraphicsSynchronization.Unregister(second);
            app.ShutDown();
        }
        Assert.Equal(before, PixelGraphicsSynchronization.Count);
        Assert.Throws<ArgumentNullException>(() => PixelGraphicsSynchronization.Register(null!));
        Assert.Throws<ArgumentNullException>(() => PixelGraphicsSynchronization.Unregister(null!));
    }

    [Fact]
    public void Each_present_of_a_program_pass_is_preceded_by_its_synchronizers()
    {
        using var scope = new DriverScope();
        var order = new List<string>();
        var driver = new OrderDriver(scope.Driver, order);
        TDisplay.driver = driver;
        var app = new TApplication();
        Action<TProgram> sync = p => order.Add(ReferenceEquals(p, app) ? "sync" : "sync-other");
        PixelGraphicsSynchronization.Register(sync);
        try
        {
            order.Clear();
            TEvent ev = default;
            app.GetEvent(ref ev);
            // TProgram.GetEvent presents, delivers posts, then TScreen.GetEvent presents again: both are synchronized.
            Assert.Equal(["sync", "pump", "sync", "pump"], order.Take(4));
            Assert.DoesNotContain("sync-other", order);
        }
        finally
        {
            PixelGraphicsSynchronization.Unregister(sync);
            app.ShutDown();
        }
    }

    private sealed class OrderDriver(NullDriver inner, List<string> order) : IDriver
    {
        public void Initialize() => inner.Initialize();
        public void Suspend() => inner.Suspend();
        public void Resume() => inner.Resume();
        public void Shutdown() => inner.Shutdown();
        public ushort GetCols() => inner.GetCols();
        public ushort GetRows() => inner.GetRows();
        public TDisplay.SM GetScreenMode() => inner.GetScreenMode();
        public void SetScreenMode(TDisplay.SM mode) => inner.SetScreenMode(mode);
        public ScreenBuffer AllocateScreenBuffer() => inner.AllocateScreenBuffer();
        public void ClearScreen(ushort cols, ushort rows) => inner.ClearScreen(cols, rows);
        public ushort GetCursorType() => inner.GetCursorType();
        public void SetCursorType(ushort cursorType) => inner.SetCursorType(cursorType);
        public void SetCaretPosition(int x, int y) => inner.SetCaretPosition(x, y);
        public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf) => inner.WriteBuf(x, y, w, h, buf);
        public void MakeBeep() => inner.MakeBeep();
        public void PumpMessages() { order.Add("pump"); inner.PumpMessages(); }
        public bool ReadKeyEvent(out TEvent ev) => inner.ReadKeyEvent(out ev);
        public bool SupportsMouse => inner.SupportsMouse;
        public bool SupportsTrueColor => inner.SupportsTrueColor;
        public bool SupportsGraphics => inner.SupportsGraphics;
        public KeyboardCapabilities KeyboardCapabilities => ((IDriver)inner).KeyboardCapabilities;
    }
}
