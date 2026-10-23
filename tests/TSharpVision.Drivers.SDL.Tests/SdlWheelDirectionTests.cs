// SDL wheel direction (MACOS-3): SDL3 reports wheel deltas already in the direction the user's
// system preference asks for, and MouseWheelEvent.Direction only records that they are opposite to
// the physical device motion. Both SDL drivers therefore use X/Y as reported, whatever Direction says.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Gpu;
using Xunit;

namespace TSharpVision.Drivers.SDL.Tests;

[Collection("SDL initialization")]
public sealed class SdlWheelDirectionTests
{
    private const SDL3.SDL.MouseWheelDirection Normal = SDL3.SDL.MouseWheelDirection.Normal;
    private const SDL3.SDL.MouseWheelDirection Flipped = SDL3.SDL.MouseWheelDirection.Flipped;

    private static readonly SdlDisplayScale Retina = SdlDisplayScale.Create(1440, 888, 2880, 1776, 2f);

    private static SDL3.SDL.MouseWheelEvent Wheel(
        float x, float y, SDL3.SDL.MouseWheelDirection direction, float mouseX = 0, float mouseY = 0) => new()
    {
        Type = SDL3.SDL.EventType.MouseWheel,
        X = x,
        Y = y,
        Direction = direction,
        MouseX = mouseX,
        MouseY = mouseY,
    };

    private static uint[] Flags(TEvent[] events) => events.Select(ev => ev.mouse.eventFlags).ToArray();

    // ── TSharpVision's logical wheel contract ────────────────────────────────

    [Fact]
    public void Logical_wheel_event_values_are_unchanged()
    {
        Assert.Equal(0x04u, Events.meWheelUp);
        Assert.Equal(0x08u, Events.meWheelDown);
        Assert.Equal(0x10u, Events.meWheelLeft);
        Assert.Equal(0x20u, Events.meWheelRight);
        Assert.Equal(0x0020, Events.evMouseWheel);
    }

    // ── 1–8. Each axis and sign, NORMAL and FLIPPED ───────────────────────────

    [Theory]
    [InlineData(0f, 1f, 0x04u)]   // +Y → WheelUp
    [InlineData(0f, -1f, 0x08u)]  // -Y → WheelDown
    [InlineData(1f, 0f, 0x20u)]   // +X → WheelRight
    [InlineData(-1f, 0f, 0x10u)]  // -X → WheelLeft
    public void Deltas_map_by_sign_whatever_the_direction(float x, float y, uint expected)
    {
        Assert.Equal([expected], Flags(SdlMouseTranslator.MakeWheelEvents(Wheel(x, y, Normal), 0, 0)));
        Assert.Equal([expected], Flags(SdlMouseTranslator.MakeWheelEvents(Wheel(x, y, Flipped), 0, 0)));
    }

    // ── 9/10. Combined axes ───────────────────────────────────────────────────

    [Theory]
    [InlineData(Normal)]
    [InlineData(Flipped)]
    public void Combined_axes_emit_vertical_then_horizontal(SDL3.SDL.MouseWheelDirection direction)
    {
        Assert.Equal([Events.meWheelUp, Events.meWheelLeft],
            Flags(SdlMouseTranslator.MakeWheelEvents(Wheel(-2f, 3f, direction), 0, 0)));
        Assert.Equal([Events.meWheelDown, Events.meWheelRight],
            Flags(SdlMouseTranslator.MakeWheelEvents(Wheel(2f, -3f, direction), 0, 0)));
    }

    // ── 11. Zero stays zero ───────────────────────────────────────────────────

    [Theory]
    [InlineData(Normal)]
    [InlineData(Flipped)]
    public void Zero_deltas_produce_no_events(SDL3.SDL.MouseWheelDirection direction)
        => Assert.Empty(SdlMouseTranslator.MakeWheelEvents(Wheel(0f, 0f, direction), 0, 0));

    // ── 12/13. Magnitude: one event per axis and sign, as before ──────────────

    [Theory]
    [InlineData(0.1f)]
    [InlineData(1f)]
    [InlineData(7.5f)]
    public void Precise_and_large_deltas_each_produce_one_event_per_axis(float magnitude)
    {
        // The framework wheel event carries a direction only; SDL's fractional trackpad deltas
        // (0.1 × scrollingDelta on macOS) and coarse wheel ticks both yield one event per axis.
        Assert.Equal([Events.meWheelUp], Flags(SdlMouseTranslator.MakeWheelEvents(Wheel(0, magnitude, Flipped), 0, 0)));
        Assert.Equal([Events.meWheelDown], Flags(SdlMouseTranslator.MakeWheelEvents(Wheel(0, -magnitude, Normal), 0, 0)));
    }

    // ── macOS natural scrolling, as SDL's Cocoa backend reports it ────────────

    [Fact]
    public void Natural_scrolling_swipe_up_scrolls_toward_the_end_like_native_apps()
    {
        // Natural scrolling ON, two fingers moving up: AppKit's scrollingDeltaY is negative (content
        // follows the fingers, the view advances), so SDL sends Y < 0 with Direction = Flipped. Native
        // apps scroll toward the end of the document, which is TSharpVision's WheelDown.
        Assert.Equal([Events.meWheelDown], Flags(SdlMouseTranslator.MakeWheelEvents(Wheel(0, -0.4f, Flipped), 0, 0)));
    }

    [Fact]
    public void Natural_scrolling_off_mouse_wheel_toward_user_scrolls_toward_the_end()
    {
        // Natural scrolling OFF, wheel rolled toward the user: SDL sends Y < 0 with Direction = Normal.
        Assert.Equal([Events.meWheelDown], Flags(SdlMouseTranslator.MakeWheelEvents(Wheel(0, -1f, Normal), 0, 0)));
    }

    // ── 14/15/16. Both drivers use the same translation ───────────────────────

    public static TheoryData<float, float, SDL3.SDL.MouseWheelDirection> DriverCases => new()
    {
        { 0f, 1f, Normal }, { 0f, -1f, Normal }, { 0f, 1f, Flipped }, { 0f, -1f, Flipped },
        { 1f, 0f, Normal }, { -1f, 0f, Normal }, { 1f, 0f, Flipped }, { -1f, 0f, Flipped },
        { 0.25f, -0.5f, Flipped }, { -3f, 2f, Normal },
    };

    [Theory]
    [MemberData(nameof(DriverCases))]
    public void Both_drivers_produce_the_translator_result_at_the_pointer_cell(
        float x, float y, SDL3.SDL.MouseWheelDirection direction)
    {
        var sdl = new SDLDriver();
        sdl.AttachForTest(SdlCellMetrics.ForDensity(12, 24, 2f), Retina, 120, 37);
        var gpu = new SDLGpuDriver();
        gpu.AttachForTest(SdlCellMetrics.ForDensity(12, 24, 2f), Retina, 120, 37);

        // Window point (606, 245) is drawable pixel (1212, 490): cell (50, 10) at 24×48 px cells.
        SDL3.SDL.MouseWheelEvent wheel = Wheel(x, y, direction, 606f, 245f);
        TEvent[] expected = SdlMouseTranslator.MakeWheelEvents(x, y, 50, 10);
        TEvent[] fromSdl = sdl.TranslateWheel(wheel);
        TEvent[] fromGpu = gpu.TranslateWheel(wheel);

        Assert.NotEmpty(expected);
        foreach (TEvent[] actual in new[] { fromSdl, fromGpu })
        {
            Assert.Equal(Flags(expected), Flags(actual));
            Assert.All(actual, ev =>
            {
                Assert.Equal(Events.evMouseWheel, ev.What);
                Assert.Equal(new TPoint(50, 10), ev.mouse.where);
            });
        }
    }
}
