namespace TSharpVision.HexView;

/// <summary>
/// A random-access, read-only sequence of bytes that a <see cref="THexView"/> presents.
/// </summary>
/// <remarks>
/// <para>
/// The contract is deliberately the smallest one that paging needs: a length, when known, and a
/// positioned read. It says nothing about files, so a local file, a remote stream, an archive entry or
/// a synthetic buffer can all implement it. Offsets are 64-bit and the view never asks for more than
/// one page at a time, so a source is never required to materialise its whole content.
/// </para>
/// <para>
/// <b>Length.</b> <see cref="Length"/> distinguishes three situations explicitly:
/// </para>
/// <list type="bullet">
/// <item><c>0</c> — a known empty source; the view reads nothing.</item>
/// <item>a positive value — a known length; the view reads no further than it.</item>
/// <item><see langword="null"/> — the total is not known (yet). The view reads page by page as the
/// person navigates, shows only the bytes found so far, and treats a read that returns fewer bytes than
/// requested as the end of the data. It never scans the source to learn its length.</item>
/// </list>
/// <para>
/// A source may change <see cref="Length"/> from <see langword="null"/> to a value when it learns the
/// total. With a known length, a short read before that length is still taken as the real end, so an
/// estimate that turns out too long is shown truthfully.
/// </para>
/// <para>
/// <b>Threading.</b> <see cref="ReadAsync"/> is called from a thread-pool thread, never from the
/// view's drawing code, and may be called again before an earlier call has completed. An
/// implementation that cannot read concurrently must serialise internally. <see cref="Length"/> is read
/// on the event-loop thread and must not block.
/// </para>
/// <para>
/// <b>Ownership.</b> The view does not own its source and never disposes it. Whoever created the
/// source disposes it after detaching it from the view (or after the view has shut down); reads that
/// are still outstanding at that point are cancelled through their token, and their results are
/// ignored.
/// </para>
/// </remarks>
public interface IHexDataSource
{
    /// <summary>
    /// Gets the number of bytes in the source, or <see langword="null"/> while the total is not known.
    /// </summary>
    long? Length { get; }

    /// <summary>
    /// Reads bytes starting at <paramref name="offset"/> into <paramref name="destination"/>.
    /// </summary>
    /// <param name="offset">Zero-based byte offset, never negative.</param>
    /// <param name="destination">Buffer to fill; never larger than one page of the view.</param>
    /// <param name="cancellationToken">Cancelled when the view no longer needs the bytes.</param>
    /// <returns>
    /// The number of bytes read. Fewer than requested is allowed; zero means there is no data at
    /// <paramref name="offset"/>.
    /// </returns>
    ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken);
}
