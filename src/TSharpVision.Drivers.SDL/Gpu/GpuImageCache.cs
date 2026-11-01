namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>Creates and releases the RGBA8 textures of a <see cref="GpuImageCache"/> (SDL_GPU, or a fake in tests).</summary>
internal interface IGpuImageTextureBackend
{
    /// <summary>A sampled RGBA8 texture of the given size, or zero when the device cannot create it.</summary>
    IntPtr CreateTexture(int width, int height);

    /// <summary>Releases a texture created here. SDL defers the release until in-flight work no longer uses it.</summary>
    void ReleaseTexture(IntPtr texture);
}

/// <summary>The cached texture of one <see cref="PixelImage"/>.</summary>
internal sealed class GpuImageTexture
{
    internal GpuImageTexture(PixelImage image, IntPtr texture)
    {
        Image = image;
        Texture = texture;
    }

    internal PixelImage Image { get; }
    internal IntPtr Texture { get; private set; }
    internal long Bytes => (long)Image.Width * Image.Height * 4;

    /// <summary>True once the pixels were handed to a copy pass; an unchanged image is never uploaded again.</summary>
    internal bool Uploaded { get; set; }

    internal bool PendingUpload { get; set; }

    // Frame stamp of the last BeginFrame/Acquire that referenced the image.
    internal uint Stamp { get; set; }

    internal void Clear() => Texture = IntPtr.Zero;
}

/// <summary>
/// GPU textures of <see cref="PixelImage"/>s, keyed by <see cref="PixelImage.Id"/> (images are immutable, so an id is
/// its pixels). A texture is created and queued for upload the first time a visible image primitive needs it,
/// uploaded once, and released as soon as no layer of the current frame holds the image any more (or on
/// <see cref="ReleaseAll"/>). Limits: each side at most <see cref="MaxTextureDimension"/>, all textures together at
/// most the byte budget; an image that does not fit draws nothing (the pass logs once). Render thread only;
/// allocation-free in the steady state (one entry per new image).
/// </summary>
internal sealed class GpuImageCache
{
    /// <summary>Total texture memory the cache may hold.</summary>
    internal const long DefaultBudgetBytes = 512L * 1024 * 1024;

    /// <summary>Largest texture side every supported backend creates (D3D12 feature level 11: 16384).</summary>
    internal const int MaxTextureDimension = 16384;

    private readonly IGpuImageTextureBackend _backend;
    private readonly Dictionary<long, GpuImageTexture> _entries = new();
    private readonly List<GpuImageTexture> _pending = new();
    private uint _stamp;

    internal GpuImageCache(IGpuImageTextureBackend backend, long budgetBytes = DefaultBudgetBytes, int maxDimension = MaxTextureDimension)
    {
        _backend = backend;
        BudgetBytes = budgetBytes;
        MaxDimension = maxDimension;
    }

    internal long BudgetBytes { get; }
    internal int MaxDimension { get; }

    /// <summary>Cached textures.</summary>
    internal int Count => _entries.Count;

    /// <summary>Bytes of all cached textures.</summary>
    internal long TotalBytes { get; private set; }

    /// <summary>Textures created / released since construction. Tests and diagnostics.</summary>
    internal int Created { get; private set; }
    internal int Released { get; private set; }

    /// <summary>Images refused because of the dimension limit, the budget, or a failed texture creation.</summary>
    internal int Refused { get; private set; }

    /// <summary>Last refusal reason (for the pass's log-once message).</summary>
    internal string? LastRefusal { get; private set; }

    /// <summary>Textures created but not uploaded yet, in creation order. The pass uploads them and calls
    /// <see cref="MarkUploaded"/>.</summary>
    internal IReadOnlyList<GpuImageTexture> PendingUploads => _pending;

    /// <summary>Bytes the pending uploads need.</summary>
    internal long PendingBytes { get; private set; }

    internal bool Contains(PixelImage image) => _entries.ContainsKey(image.Id);

    /// <summary>
    /// Starts a frame: keeps the textures of every image the frame's layers hold and releases all others.
    /// </summary>
    internal void BeginFrame(Graphics.PixelGraphicsFrame frame)
    {
        _stamp++;
        if (_entries.Count == 0) return;
        foreach (Graphics.PixelGraphicsLayer layer in frame.Layers)
            foreach (PixelImage image in layer.Images)
                if (_entries.TryGetValue(image.Id, out GpuImageTexture? entry)) entry.Stamp = _stamp;
        // Removing during enumeration is allowed for Dictionary (no version change on Remove).
        foreach (KeyValuePair<long, GpuImageTexture> pair in _entries)
            if (pair.Value.Stamp != _stamp) Release(pair.Value);
    }

    /// <summary>The texture of <paramref name="image"/>, created (and queued for upload) on first use; null when the
    /// image exceeds a limit or the texture cannot be created.</summary>
    internal GpuImageTexture? Acquire(PixelImage image)
    {
        if (_entries.TryGetValue(image.Id, out GpuImageTexture? entry))
        {
            entry.Stamp = _stamp;
            if (entry.Texture == IntPtr.Zero) return null; // creation failed before; not retried while held
            if (!entry.Uploaded && !entry.PendingUpload) Queue(entry);
            return entry;
        }

        long bytes = (long)image.Width * image.Height * 4;
        if (image.Width > MaxDimension || image.Height > MaxDimension)
            return Refuse($"image {image.Width} x {image.Height} exceeds the {MaxDimension} texture limit");
        if (TotalBytes + bytes > BudgetBytes)
            return Refuse($"image {image.Width} x {image.Height} exceeds the {BudgetBytes >> 20} MiB texture budget");

        IntPtr texture = _backend.CreateTexture(image.Width, image.Height);
        entry = new GpuImageTexture(image, texture) { Stamp = _stamp };
        _entries.Add(image.Id, entry);
        if (texture == IntPtr.Zero)
        {
            Refuse($"texture {image.Width} x {image.Height} could not be created");
            return null;
        }
        Created++;
        TotalBytes += bytes;
        Queue(entry);
        return entry;
    }

    private void Queue(GpuImageTexture entry)
    {
        entry.PendingUpload = true;
        _pending.Add(entry);
        PendingBytes += entry.Bytes;
    }

    private GpuImageTexture? Refuse(string reason)
    {
        Refused++;
        LastRefusal = reason;
        return null;
    }

    /// <summary>Marks every pending texture uploaded (after the copy pass recorded them).</summary>
    internal void MarkUploaded()
    {
        foreach (GpuImageTexture entry in _pending)
        {
            entry.PendingUpload = false;
            entry.Uploaded = true;
        }
        _pending.Clear();
        PendingBytes = 0;
    }

    /// <summary>Forgets the pending uploads without uploading (a failed frame); they are queued again on next use.</summary>
    internal void CancelPending()
    {
        foreach (GpuImageTexture entry in _pending) entry.PendingUpload = false;
        _pending.Clear();
        PendingBytes = 0;
    }

    private void Release(GpuImageTexture entry)
    {
        _entries.Remove(entry.Image.Id);
        if (entry.PendingUpload)
        {
            _pending.Remove(entry);
            PendingBytes -= entry.Bytes;
            entry.PendingUpload = false;
        }
        if (entry.Texture == IntPtr.Zero) return;
        _backend.ReleaseTexture(entry.Texture);
        entry.Clear();
        TotalBytes -= entry.Bytes;
        Released++;
    }

    /// <summary>Releases every texture (no graphics any more, failure, or dispose).</summary>
    internal void ReleaseAll()
    {
        foreach (KeyValuePair<long, GpuImageTexture> pair in _entries) Release(pair.Value);
        _pending.Clear();
        PendingBytes = 0;
    }
}
