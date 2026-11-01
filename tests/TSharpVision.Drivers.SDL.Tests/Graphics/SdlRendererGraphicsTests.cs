using TSharpVision.Drivers.SDL.Graphics;
using TSharpVision.Drivers.SDL.Renderer;

namespace TSharpVision.Drivers.SDL.Tests.Graphics;

/// <summary>
/// TV12o classic SDL_Renderer graphics without a device: the call plan (one fill batch per primitive and clip
/// rectangle, image placement and clipping, order) and the image texture cache (upload once, release unreferenced,
/// size limit, budget, creation failure, device reset, dispose) over a fake texture backend.
/// </summary>
public sealed class SdlRendererGraphicsTests
{
    private const uint Red = 0xFF0000FF, Green = 0x00FF00FF;

    private static PixelImage Image(int w = 2, int h = 2) => PixelImage.Create(w, h, px => px.Fill(0xFF));

    private static PixelGraphicsFrame Frame(PixelRect[] clip, PixelImage[] images, params GraphicsPrimitive[] primitives)
    {
        var layers = new PixelGraphicsLayers();
        layers.Set(new object(), clip, primitives, images);
        return layers.Frame;
    }

    private static DeviceRect ToDevice(SDL3.SDL.FRect r) => new((int)r.X, (int)r.Y, (int)(r.X + r.W), (int)(r.Y + r.H));

    // ── Plan ────────────────────────────────────────────────────────────

    [Fact]
    public void Lines_and_rectangles_become_one_clipped_fill_batch_per_primitive_and_clip_rectangle()
    {
        var plan = new SdlGraphicsPlan();
        PixelScale scale = PixelScale.Identity(8, 16);
        PixelGraphicsFrame frame = Frame([new(0, 0, 10, 40), new(20, 0, 10, 40)], [],
            GraphicsPrimitive.Line(0, 0, 29, 19, Red),
            GraphicsPrimitive.FillRect(5, 5, 20, 2, Green),
            GraphicsPrimitive.FillRect(0, 0, 30, 30, 0x12345600)); // fully transparent: no call
        plan.Build(frame, scale, 100, 100);

        Assert.Equal(4, plan.Commands.Count);
        Assert.All(plan.Commands, c => Assert.Equal(SdlGraphicsCommandKind.Fill, c.Kind));
        Assert.Equal([Red, Red, Green, Green], plan.Commands.Select(c => c.Rgba));

        // Every batch equals the geometry intersected with its clip rectangle.
        var geometry = new List<DeviceRect>();
        for (int i = 0; i < 4; i++)
        {
            SdlGraphicsCommand c = plan.Commands[i];
            geometry.Clear();
            PixelGraphicsGeometry.AppendRects(frame.Layers[0].Primitives[i / 2], scale, geometry);
            DeviceRect clip = i % 2 == 0 ? new(0, 0, 10, 40) : new(20, 0, 30, 40);
            var expected = geometry.Select(r => r.Intersect(clip)).Where(r => !r.IsEmpty).ToList();
            var actual = plan.Rects.Slice(c.FirstRect, c.RectCount).ToArray().Select(ToDevice).ToList();
            Assert.Equal(expected, actual);
            Assert.Equal(clip, c.Clip);
        }
        Assert.Equal(plan.Commands.Sum(c => c.RectCount), plan.Rects.Length);
    }

    [Fact]
    public void Plan_clips_to_the_output_and_skips_invisible_primitives()
    {
        var plan = new SdlGraphicsPlan();
        plan.Build(Frame([new(0, 0, 50, 50)], [], GraphicsPrimitive.FillRect(-10, -10, 100, 100, Red),
            GraphicsPrimitive.FillRect(60, 60, 5, 5, Red)), PixelScale.Identity(8, 16), 20, 10);
        SdlGraphicsCommand only = Assert.Single(plan.Commands);
        Assert.Equal(new DeviceRect(0, 0, 20, 10), ToDevice(plan.Rects[only.FirstRect]));

        plan.Build(PixelGraphicsFrame.Empty, PixelScale.Identity(8, 16), 20, 10);
        Assert.Empty(plan.Commands);
        Assert.Equal(0, plan.Rects.Length);
    }

    [Fact]
    public void Images_keep_their_full_destination_under_each_clip_rectangle_in_primitive_order()
    {
        var plan = new SdlGraphicsPlan();
        PixelImage image = Image(3, 2);
        var scale = new PixelScale(8, 16, 16, 32);
        plan.Build(Frame([new(0, 0, 4, 16), new(6, 0, 4, 16)], [image],
            GraphicsPrimitive.FillRect(0, 0, 10, 2, Red),
            GraphicsPrimitive.Image(0, 1, 1, 8, 4, GraphicsImageFilter.Linear, 200),
            GraphicsPrimitive.Image(1, 1, 1, 8, 4), // out of range: nothing
            GraphicsPrimitive.Line(0, 3, 9, 3, Green)), scale, 200, 200);

        Assert.Equal(
            [SdlGraphicsCommandKind.Fill, SdlGraphicsCommandKind.Fill, SdlGraphicsCommandKind.Image,
             SdlGraphicsCommandKind.Image, SdlGraphicsCommandKind.Fill, SdlGraphicsCommandKind.Fill],
            plan.Commands.Select(c => c.Kind));
        SdlGraphicsCommand left = plan.Commands[2], right = plan.Commands[3];
        foreach (SdlGraphicsCommand c in new[] { left, right })
        {
            Assert.Same(image, c.Image);
            Assert.True(c.WholeSource);
            Assert.Equal(new DeviceRect(2, 2, 18, 10), ToDevice(c.Destination));
            Assert.Equal(GraphicsImageFilter.Linear, c.Filter);
            Assert.Equal(200, (byte)c.Rgba);
        }
        Assert.Equal(new DeviceRect(0, 0, 8, 32), left.Clip);
        Assert.Equal(new DeviceRect(2, 2, 8, 10), left.Visible);
        Assert.Equal(new DeviceRect(12, 0, 20, 32), right.Clip);
        Assert.Equal(new DeviceRect(12, 2, 18, 10), right.Visible);
    }

    [Fact]
    public void Destinations_beyond_float_precision_use_the_visible_part_and_the_matching_source_rectangle()
    {
        var plan = new SdlGraphicsPlan();
        PixelImage image = Image(4, 2);
        plan.Build(Frame([new(0, 0, 100, 100)], [image],
            GraphicsPrimitive.Image(0, -1_000_000_000, 0, 2_000_000_000, 20)), new PixelScale(1, 1, 1, 1), 50, 50);
        SdlGraphicsCommand c = Assert.Single(plan.Commands);
        Assert.False(c.WholeSource);
        Assert.Equal(new DeviceRect(0, 0, 50, 20), c.Visible);
        Assert.Equal(c.Visible, ToDevice(c.Destination));
        // texel u = (px + 1e9) * 4 / 2e9: the visible part starts at u = 2 and is 50 * 2e-9 texels wide.
        Assert.Equal(2f, c.Source.X, 4);
        Assert.Equal(1e-7f, c.Source.W, 9);
        Assert.Equal(0f, c.Source.Y);
        Assert.Equal(2f, c.Source.H);
    }

    // ── Texture cache ───────────────────────────────────────────────────

    private sealed class FakeBackend : ISdlImageTextureBackend
    {
        private long _next = 1;
        public readonly List<long> Created = new();
        public readonly HashSet<IntPtr> Live = new();
        public readonly List<IntPtr> Destroyed = new();
        public bool Fail;

        public string LastError => "simulated failure";

        public IntPtr Create(PixelImage image)
        {
            if (Fail) return IntPtr.Zero;
            Created.Add(image.Id);
            var handle = (IntPtr)_next++;
            Live.Add(handle);
            return handle;
        }

        public void Destroy(IntPtr texture)
        {
            Assert.True(Live.Remove(texture), "destroyed a texture twice or one never created");
            Destroyed.Add(texture);
        }
    }

    [Fact]
    public void Textures_are_uploaded_once_and_released_when_no_layer_refers_to_the_image()
    {
        var backend = new FakeBackend();
        var log = new List<string>();
        using var cache = new SdlImageTextureCache(backend, 4096, log: log.Add);
        PixelImage a = Image(), b = Image(4, 4);
        PixelGraphicsFrame both = Frame([new(0, 0, 8, 8)], [a, b], GraphicsPrimitive.Image(0, 0, 0, 2, 2));

        cache.Retain(both);
        IntPtr ta = cache.Get(a);
        Assert.NotEqual(IntPtr.Zero, ta);
        Assert.Equal(ta, cache.Get(a));
        IntPtr tb = cache.Get(b);
        Assert.Equal(2, cache.Uploads);
        Assert.Equal(2 * 2 * 4 + 4 * 4 * 4, cache.BytesInUse);

        cache.Retain(both); // unchanged frame: nothing happens
        Assert.Empty(backend.Destroyed);

        PixelGraphicsFrame onlyB = Frame([new(0, 0, 8, 8)], [b], GraphicsPrimitive.Image(0, 0, 0, 2, 2));
        cache.Retain(onlyB);
        Assert.Equal([ta], backend.Destroyed);
        Assert.Equal(1, cache.Count);
        Assert.Equal(tb, cache.Get(b)); // still cached, not uploaded again
        Assert.Equal(2, cache.Uploads);

        cache.Retain(PixelGraphicsFrame.Empty);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.BytesInUse);
        Assert.Empty(backend.Live);
        Assert.Empty(log);
    }

    [Fact]
    public void Images_over_the_size_limit_or_budget_are_skipped_and_logged_once_per_reason()
    {
        var backend = new FakeBackend();
        var log = new List<string>();
        using var cache = new SdlImageTextureCache(backend, maxTextureSize: 8, budgetBytes: 100, log: log.Add);
        PixelImage wide = Image(9, 1), tall = Image(1, 9), fits = Image(4, 4), second = Image(4, 4);

        Assert.Equal(IntPtr.Zero, cache.Get(wide));
        Assert.Equal(IntPtr.Zero, cache.Get(wide));
        Assert.Equal(IntPtr.Zero, cache.Get(tall));
        Assert.Single(log);
        Assert.Contains("maximum texture size", log[0]);

        Assert.NotEqual(IntPtr.Zero, cache.Get(fits)); // 64 bytes
        Assert.Equal(IntPtr.Zero, cache.Get(second));  // 64 more exceed 100
        Assert.Equal(IntPtr.Zero, cache.Get(Image(4, 4)));
        Assert.Equal(2, log.Count);
        Assert.Contains("budget", log[1]);
        Assert.Equal(5, cache.Skips);
        Assert.Equal([fits.Id], backend.Created);

        // Once the frame drops the first image, the budget frees and a skipped image is retried.
        cache.Retain(Frame([new(0, 0, 8, 8)], [second], GraphicsPrimitive.Image(0, 0, 0, 2, 2)));
        Assert.NotEqual(IntPtr.Zero, cache.Get(second));
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void Failed_creation_is_skipped_logged_once_and_retried_after_a_device_reset()
    {
        var backend = new FakeBackend { Fail = true };
        var log = new List<string>();
        using var cache = new SdlImageTextureCache(backend, log: log.Add);
        PixelImage image = Image();
        Assert.Equal(IntPtr.Zero, cache.Get(image));
        Assert.Equal(IntPtr.Zero, cache.Get(image));
        Assert.Single(log);
        Assert.Contains("simulated failure", log[0]);

        backend.Fail = false;
        cache.Reset();
        IntPtr first = cache.Get(image);
        Assert.NotEqual(IntPtr.Zero, first);

        // Device reset: textures are destroyed and recreated on next use.
        cache.Reset();
        Assert.Equal([first], backend.Destroyed);
        Assert.Equal(0, cache.Count);
        IntPtr second = cache.Get(image);
        Assert.NotEqual(first, second);
        Assert.Equal(2, cache.Uploads);
    }

    [Fact]
    public void Dispose_destroys_every_texture()
    {
        var backend = new FakeBackend();
        var cache = new SdlImageTextureCache(backend);
        cache.Get(Image());
        cache.Get(Image(3, 3));
        cache.Dispose();
        Assert.Empty(backend.Live);
        Assert.Equal(2, backend.Destroyed.Count);
    }
}
