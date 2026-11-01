namespace TSharpVision.Drivers.SDL.Graphics;

/// <summary>
/// One owner's immutable layer: its visible clip rectangles and primitives in logical pixels, and the (immutable)
/// images its image primitives refer to by index.
/// </summary>
internal sealed class PixelGraphicsLayer
{
    internal PixelGraphicsLayer(PixelRect[] clip, GraphicsPrimitive[] primitives, PixelImage[]? images = null)
    {
        Clip = clip;
        Primitives = primitives;
        Images = images ?? [];
    }

    internal PixelRect[] Clip { get; }
    internal GraphicsPrimitive[] Primitives { get; }
    internal PixelImage[] Images { get; }

    /// <summary>The image a primitive refers to, or null when it is not an image or its index is out of range.</summary>
    internal PixelImage? ImageOf(in GraphicsPrimitive primitive) =>
        primitive.Kind == GraphicsPrimitiveKind.Image && (uint)primitive.ImageIndex < (uint)Images.Length
            ? Images[primitive.ImageIndex] : null;
}

/// <summary>
/// An immutable snapshot of every layer, in submission order, as a renderer draws it. Safe to hand to another
/// thread: nothing in it is ever mutated.
/// </summary>
internal sealed class PixelGraphicsFrame
{
    internal static readonly PixelGraphicsFrame Empty = new([]);

    internal PixelGraphicsFrame(PixelGraphicsLayer[] layers)
    {
        Layers = layers;
        foreach (PixelGraphicsLayer layer in layers)
            if (layer.Clip.Length > 0) PrimitiveCount += layer.Primitives.Length;
    }

    internal PixelGraphicsLayer[] Layers { get; }

    /// <summary>Primitives of layers with a non-empty clip region; zero means nothing to draw.</summary>
    internal int PrimitiveCount { get; }

    internal bool IsEmpty => PrimitiveCount == 0;
}

/// <summary>
/// The <see cref="IPixelGraphicsDriver"/> layer store shared by the SDL drivers. Owners are compared by reference.
/// UI thread only; <see cref="Frame"/> is an immutable snapshot rebuilt only when a layer changes, so an unchanged
/// resubmission allocates nothing and does not request a repaint.
/// </summary>
internal sealed class PixelGraphicsLayers
{
    private readonly List<object> _order = new();
    private readonly Dictionary<object, PixelGraphicsLayer> _layers = new(ReferenceEqualityComparer.Instance);

    internal PixelGraphicsFrame Frame { get; private set; } = PixelGraphicsFrame.Empty;

    internal int Count => _order.Count;

    /// <summary>Stores the layer; returns true when the presented graphics changed.</summary>
    internal bool Set(object owner, ReadOnlySpan<PixelRect> clip, ReadOnlySpan<GraphicsPrimitive> primitives,
        ReadOnlySpan<PixelImage> images = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        foreach (PixelImage image in images) ArgumentNullException.ThrowIfNull(image, nameof(images));
        if (_layers.TryGetValue(owner, out PixelGraphicsLayer? existing)
            && clip.SequenceEqual(existing.Clip) && primitives.SequenceEqual(existing.Primitives)
            && SameImages(images, existing.Images))
            return false;

        // Empty rectangles carry nothing; dropping them keeps renderers free of the check.
        int nonEmpty = 0;
        foreach (PixelRect r in clip) if (!r.IsEmpty) nonEmpty++;
        var kept = new PixelRect[nonEmpty];
        nonEmpty = 0;
        foreach (PixelRect r in clip) if (!r.IsEmpty) kept[nonEmpty++] = r;

        if (existing == null) _order.Add(owner);
        _layers[owner] = new PixelGraphicsLayer(kept, primitives.ToArray(), images.IsEmpty ? null : images.ToArray());
        Rebuild();
        return true;
    }

    // Images are immutable: the same instances in the same order mean the same pixels.
    private static bool SameImages(ReadOnlySpan<PixelImage> images, PixelImage[] existing)
    {
        if (images.Length != existing.Length) return false;
        for (int i = 0; i < images.Length; i++)
            if (!ReferenceEquals(images[i], existing[i])) return false;
        return true;
    }

    /// <summary>Removes the layer; returns true when one existed.</summary>
    internal bool Remove(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!_layers.Remove(owner)) return false;
        _order.Remove(owner);
        Rebuild();
        return true;
    }

    /// <summary>Drops every layer (driver shutdown); returns true when any existed.</summary>
    internal bool Clear()
    {
        if (_order.Count == 0) return false;
        _order.Clear();
        _layers.Clear();
        Frame = PixelGraphicsFrame.Empty;
        return true;
    }

    private void Rebuild()
    {
        if (_order.Count == 0) { Frame = PixelGraphicsFrame.Empty; return; }
        var layers = new PixelGraphicsLayer[_order.Count];
        for (int i = 0; i < layers.Length; i++) layers[i] = _layers[_order[i]];
        Frame = new PixelGraphicsFrame(layers);
    }
}
