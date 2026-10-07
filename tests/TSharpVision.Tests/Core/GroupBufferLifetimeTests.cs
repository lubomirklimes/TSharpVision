// TGroup buffer lifetime regression tests.
//
// A buffered group keeps its ScreenBuffer instance when FreeBuffer() releases it, so that
// GetBuffer() can reuse the allocation. A released buffer is not a current image: it holds
// whatever was drawn before the group was hidden, laid out for the size the group had then.
// Draw(), Lock() and the child write path used to test only `buffer != null`, so a group that
// was hidden, resized and shown again wrote the released buffer: an ArgumentOutOfRangeException
// from TView.WriteBuf when the group had grown, the old image at the wrong stride when it had
// shrunk, and the image from before it was hidden otherwise.
//
// Every test drives the real lifecycle — Hide(), ChangeBounds(), Show() — and asserts on what
// reaches the root buffer. All are headless (NullDriver + DriverScope).
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class GroupBufferLifetimeTests : IDisposable
{
    private const int RootWidth = 40;
    private const int RootHeight = 14;
    private const char Background = '.';

    private readonly DriverScope _ds;

    public GroupBufferLifetimeTests()
    {
        _ds = new DriverScope(80, 25);
        TEventQueue.Resume();
    }

    public void Dispose() => _ds.Dispose();

    /// <summary>Fills its whole extent with one character.</summary>
    private sealed class Fill : TView
    {
        private readonly char _ch;

        public Fill(TRect bounds, char ch) : base(bounds) => _ch = ch;

        public override void Draw()
        {
            var b = new TDrawBuffer();
            b.moveChar(0, _ch, 0x07, size.x);
            for (int y = 0; y < size.y; y++) WriteBuf(0, y, size.x, 1, b);
        }
    }

    /// <summary>
    /// Draws an image that depends on the view's current size: <see cref="Token"/> everywhere,
    /// '|' down the last column and '_' along the last row. An image drawn for another size, or
    /// with another token, is therefore distinguishable cell by cell.
    /// </summary>
    private sealed class Pattern : TView
    {
        public char Token = 'a';
        public int Draws;

        public Pattern(TRect bounds) : base(bounds)
            => growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);

        public static char Expected(int x, int y, int width, int height, char token)
            => y == height - 1 ? '_' : x == width - 1 ? '|' : token;

        public override void Draw()
        {
            Draws++;
            var b = new TDrawBuffer();
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                    b.moveChar(x, Expected(x, y, size.x, size.y, Token), 0x1F, 1);
                WriteBuf(0, y, size.x, 1, b);
            }
        }
    }

    private sealed class Scene
    {
        public required TestGroup Root { get; init; }
        public required TestGroup Group { get; init; }
        public required Pattern Content { get; init; }
    }

    /// <summary>
    /// A root with its own buffer (standing in for TProgram), a background, and one group holding
    /// a <see cref="Pattern"/> that fills it — directly, or inside a second buffered group.
    /// </summary>
    private static Scene Build(TRect bounds, bool buffered = true, bool nested = false)
    {
        var root = new TestGroup(new TRect(0, 0, RootWidth, RootHeight));
        root.buffer = new ScreenBuffer(RootWidth * RootHeight * ScreenBuffer.GetSize());
        root.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        root.Insert(new Fill(new TRect(0, 0, RootWidth, RootHeight), Background));

        var group = new TestGroup(bounds);
        if (!buffered) group.options = (ushort)(group.options & ~Views.ofBuffered);

        int width = bounds.b.x - bounds.a.x;
        int height = bounds.b.y - bounds.a.y;
        var content = new Pattern(new TRect(0, 0, width, height));

        if (nested)
        {
            var inner = new TestGroup(new TRect(0, 0, width, height))
            {
                growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY),
            };
            inner.Insert(content);
            group.Insert(inner);
        }
        else
        {
            group.Insert(content);
        }

        root.Insert(group);
        return new Scene { Root = root, Group = group, Content = content };
    }

    private static char RootChar(Scene scene, int x, int y)
        => scene.Root.buffer!.Data[(y * RootWidth) + x].Character;

    /// <summary>
    /// The root shows the group's image for the size it has now, cell for cell, and the
    /// background everywhere else — so nothing of an older, larger image is left behind.
    /// </summary>
    private static void AssertShowsCurrentImage(Scene scene, char token = 'a')
    {
        TRect r = scene.Group.GetBounds();
        int width = r.b.x - r.a.x;
        int height = r.b.y - r.a.y;

        for (int y = 0; y < RootHeight; y++)
        for (int x = 0; x < RootWidth; x++)
        {
            bool inside = x >= r.a.x && x < r.b.x && y >= r.a.y && y < r.b.y;
            char expected = inside
                ? Pattern.Expected(x - r.a.x, y - r.a.y, width, height, token)
                : Background;
            Assert.True(expected == RootChar(scene, x, y),
                $"cell ({x},{y}): expected '{expected}', found '{RootChar(scene, x, y)}' for a {width}x{height} group");
        }
    }

    private static void AssertBufferFitsBounds(TGroup group)
    {
        Assert.NotNull(group.buffer);
        Assert.Equal(group.size.x * group.size.y * ScreenBuffer.GetSize(), group.buffer!.Size);
    }

    // ── baseline ─────────────────────────────────────────────────────────────

    [Fact]
    public void AVisibleBufferedGroupDrawsThroughABufferOfItsOwnSize()
    {
        Scene scene = Build(new TRect(2, 1, 12, 5));

        AssertBufferFitsBounds(scene.Group);
        AssertShowsCurrentImage(scene);
    }

    // ── 1. hide → grow → show ────────────────────────────────────────────────

    [Fact]
    public void AGroupEnlargedWhileHiddenIsShownWithoutThrowingAtItsNewSize()
    {
        Scene scene = Build(new TRect(2, 1, 12, 5));

        scene.Group.Hide();
        scene.Group.ChangeBounds(new TRect(2, 1, 30, 11));

        Exception? thrown = Record.Exception(() => scene.Group.Show());

        Assert.Null(thrown);
        AssertBufferFitsBounds(scene.Group);
        AssertShowsCurrentImage(scene);
    }

    [Theory]
    [InlineData(30, 5)]    // wider only
    [InlineData(12, 11)]   // taller only
    public void GrowingInOneDirectionWhileHiddenIsEnough(int right, int bottom)
    {
        Scene scene = Build(new TRect(2, 1, 12, 5));

        scene.Group.Hide();
        scene.Group.ChangeBounds(new TRect(2, 1, right, bottom));
        scene.Group.Show();

        AssertBufferFitsBounds(scene.Group);
        AssertShowsCurrentImage(scene);
    }

    // ── 2. hide → shrink → show ──────────────────────────────────────────────

    [Fact]
    public void AGroupShrunkWhileHiddenShowsNothingOfItsOldLargerImage()
    {
        Scene scene = Build(new TRect(2, 1, 30, 11));

        scene.Group.Hide();
        scene.Group.ChangeBounds(new TRect(2, 1, 12, 5));
        scene.Group.Show();

        AssertBufferFitsBounds(scene.Group);
        AssertShowsCurrentImage(scene);
    }

    [Fact]
    public void AGroupReshapedToTheSameAreaWhileHiddenIsNotDrawnAtTheOldStride()
    {
        // Same cell count, so the retained allocation is the right length and nothing can
        // throw; only the layout of the image is wrong if the released buffer is written back.
        Scene scene = Build(new TRect(2, 1, 14, 5));   // 12 x 4

        scene.Group.Hide();
        scene.Group.ChangeBounds(new TRect(2, 1, 8, 9));   // 6 x 8
        scene.Group.Show();

        AssertBufferFitsBounds(scene.Group);
        AssertShowsCurrentImage(scene);
    }

    // ── 3. same size ─────────────────────────────────────────────────────────

    [Fact]
    public void AGroupShownAgainAtTheSameSizeIsRedrawnNotReplayed()
    {
        Scene scene = Build(new TRect(2, 1, 12, 5));

        scene.Group.Hide();

        // What the group shows changes while nobody can see it; a hidden view does not paint.
        int drawsWhileVisible = scene.Content.Draws;
        scene.Content.Token = 'z';
        scene.Content.DrawView();
        Assert.Equal(drawsWhileVisible, scene.Content.Draws);

        scene.Group.Show();

        Assert.True(scene.Content.Draws > drawsWhileVisible);
        AssertShowsCurrentImage(scene, token: 'z');
    }

    [Fact]
    public void AHiddenGroupIsNotOnTheScreenAndItsReleasedBufferReceivesNoWrites()
    {
        Scene scene = Build(new TRect(2, 1, 12, 5));
        ScreenBuffer retained = scene.Group.buffer!;

        scene.Group.Hide();

        for (int y = 0; y < RootHeight; y++)
        for (int x = 0; x < RootWidth; x++)
            Assert.Equal(Background, RootChar(scene, x, y));

        // A child asked to draw while its group is hidden writes nowhere — not to the screen,
        // and not into the image the group kept from before.
        TScreenChar[] before = retained.Data.ToArray();
        scene.Content.Token = 'z';
        scene.Content.Draw();
        Assert.True(before.AsSpan().SequenceEqual(retained.Data));
    }

    // ── 4. repeated cycles ───────────────────────────────────────────────────

    [Fact]
    public void RepeatedHideResizeShowCyclesStayConsistent()
    {
        Scene scene = Build(new TRect(2, 1, 12, 5));

        (int Right, int Bottom)[] sizes =
        {
            (30, 11), (8, 4), (8, 4), (38, 13), (14, 5), (8, 9), (30, 11), (12, 5),
        };

        char token = 'a';
        foreach ((int right, int bottom) in sizes)
        {
            scene.Group.Hide();
            scene.Group.ChangeBounds(new TRect(2, 1, right, bottom));
            scene.Content.Token = ++token;
            scene.Group.Show();

            AssertBufferFitsBounds(scene.Group);
            AssertShowsCurrentImage(scene, token);

            // And an ordinary redraw afterwards goes through the same, live buffer.
            scene.Content.Token = ++token;
            scene.Content.DrawView();
            AssertShowsCurrentImage(scene, token);
        }
    }

    [Fact]
    public void ResizingSeveralTimesWhileHiddenUsesOnlyTheLastSize()
    {
        Scene scene = Build(new TRect(2, 1, 12, 5));

        scene.Group.Hide();
        scene.Group.ChangeBounds(new TRect(2, 1, 38, 13));
        scene.Group.ChangeBounds(new TRect(2, 1, 6, 3));
        scene.Group.ChangeBounds(new TRect(4, 2, 24, 9));
        scene.Group.Show();

        AssertBufferFitsBounds(scene.Group);
        AssertShowsCurrentImage(scene);
    }

    // ── 5. nested buffered groups ────────────────────────────────────────────

    [Theory]
    [InlineData(30, 11)]
    [InlineData(8, 4)]
    [InlineData(12, 5)]
    public void ABufferedGroupInsideAHiddenResizedGroupIsRedrawnToo(int right, int bottom)
    {
        Scene scene = Build(new TRect(2, 1, 12, 5), nested: true);
        var inner = (TGroup)scene.Content.owner!;
        AssertShowsCurrentImage(scene);

        scene.Group.Hide();
        scene.Group.ChangeBounds(new TRect(2, 1, right, bottom));
        scene.Content.Token = 'n';
        scene.Group.Show();

        AssertBufferFitsBounds(scene.Group);
        AssertBufferFitsBounds(inner);
        AssertShowsCurrentImage(scene, token: 'n');
    }

    // ── 6. non-buffered groups are unchanged ─────────────────────────────────

    [Theory]
    [InlineData(30, 11)]
    [InlineData(8, 4)]
    [InlineData(12, 5)]
    public void AGroupWithoutTheBufferedOptionNeverGetsABufferAndStillRedraws(int right, int bottom)
    {
        Scene scene = Build(new TRect(2, 1, 12, 5), buffered: false);
        Assert.Null(scene.Group.buffer);
        AssertShowsCurrentImage(scene);

        scene.Group.Hide();
        scene.Group.ChangeBounds(new TRect(2, 1, right, bottom));
        scene.Content.Token = 'u';
        scene.Group.Show();

        Assert.Null(scene.Group.buffer);
        AssertShowsCurrentImage(scene, token: 'u');
    }

    // ── buffering is still buffering ─────────────────────────────────────────

    [Fact]
    public void AVisibleGroupResizedInPlaceIsStillDrawnCorrectly()
    {
        Scene scene = Build(new TRect(2, 1, 12, 5));

        // Locate is the visible-resize entry point: it also repaints what the old bounds uncover.
        scene.Group.Locate(new TRect(2, 1, 30, 11));
        AssertBufferFitsBounds(scene.Group);
        AssertShowsCurrentImage(scene);

        scene.Group.Locate(new TRect(2, 1, 9, 4));
        AssertBufferFitsBounds(scene.Group);
        AssertShowsCurrentImage(scene);
    }

    [Fact]
    public void TheRetainedAllocationIsStillReusedWhenTheSizeAllowsIt()
    {
        Scene scene = Build(new TRect(2, 1, 12, 5));
        ScreenBuffer original = scene.Group.buffer!;

        // Hidden and shown at the same size: the same allocation, redrawn.
        scene.Group.Hide();
        scene.Group.Show();
        Assert.Same(original, scene.Group.buffer);
        AssertShowsCurrentImage(scene);

        // A different size cannot reuse it.
        scene.Group.Hide();
        scene.Group.ChangeBounds(new TRect(2, 1, 30, 11));
        scene.Group.Show();
        Assert.NotSame(original, scene.Group.buffer);
        AssertShowsCurrentImage(scene);
    }

    [Fact]
    public void ALiveBufferIsWrittenBackWithoutRedrawingTheChildren()
    {
        Scene scene = Build(new TRect(2, 1, 12, 5));
        int draws = scene.Content.Draws;

        // The point of the buffer: redrawing the group replays the image, it does not repaint.
        scene.Group.DrawView();
        scene.Group.DrawView();

        Assert.Equal(draws, scene.Content.Draws);
        AssertShowsCurrentImage(scene);
    }
}
