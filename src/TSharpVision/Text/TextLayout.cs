using System.Buffers;
using System.Globalization;
using System.Text;

namespace TSharpVision.Text;

/// <summary>How many screen columns a grapheme cluster occupies.</summary>
public enum TextWidthProfile
{
    /// <summary>
    /// Resolves to <see cref="TextLayout.DefaultProfile"/>: <see cref="Unicode"/>, or <see cref="Legacy"/> when the
    /// <see cref="TextLayout.LegacyCellWidthSwitch"/> AppContext switch is set.
    /// </summary>
    Default = 0,
    /// <summary>
    /// One width per extended grapheme cluster: 2 for East Asian Wide/Fullwidth, emoji presentation, emoji
    /// variation sequences (VS16), emoji ZWJ sequences and regional indicators; 0 for a cluster of only
    /// combining or format characters; otherwise 1.
    /// </summary>
    Unicode = 1,
    /// <summary>Sum of per-code-point widths, as terminals that do not join ZWJ or regional indicator sequences use.</summary>
    CodePoint = 2,
    /// <summary>One column per UTF-16 code unit: the behaviour of TSharpVision 1.2 and earlier.</summary>
    Legacy = 3,
}

/// <summary>What a <see cref="TextCluster"/> holds.</summary>
public enum TextClusterKind : byte
{
    /// <summary>Printable text.</summary>
    Text,
    /// <summary>A horizontal tab; its width reaches the next tab stop.</summary>
    Tab,
    /// <summary>LF or CR LF; zero width.</summary>
    LineBreak,
    /// <summary>Any other C0 or C1 control character, including a lone CR; one column.</summary>
    Control,
    /// <summary>An unpaired surrogate; one column, drawn as U+FFFD.</summary>
    Malformed,
    /// <summary>A cluster of only combining marks or format characters (a mark at the start of a line, ZWSP).</summary>
    ZeroWidth,
}

/// <summary>Which offset <see cref="TextLayout.OffsetAtColumn(ReadOnlySpan{char}, int, in TextLayoutOptions, ColumnBias, out int, int)"/> returns for a column inside a wide cluster.</summary>
public enum ColumnBias
{
    /// <summary>The start of the cluster that covers the column.</summary>
    Floor,
    /// <summary>The end of the cluster that covers the column.</summary>
    Ceiling,
    /// <summary>The cluster edge closer to the column; the start on a tie.</summary>
    Nearest,
}

/// <summary>One extended grapheme cluster with its UTF-16 range and visual columns.</summary>
public readonly struct TextCluster : IEquatable<TextCluster>
{
    /// <summary>Creates a cluster description.</summary>
    public TextCluster(int start, int length, int column, int width, TextClusterKind kind)
    {
        Start = start;
        Length = length;
        Column = column;
        Width = width;
        Kind = kind;
    }

    /// <summary>UTF-16 offset of the first code unit.</summary>
    public int Start { get; }
    /// <summary>Number of UTF-16 code units.</summary>
    public int Length { get; }
    /// <summary>UTF-16 offset just past the cluster.</summary>
    public int End => Start + Length;
    /// <summary>First visual column.</summary>
    public int Column { get; }
    /// <summary>Number of columns (0 for line breaks and zero-width clusters).</summary>
    public int Width { get; }
    /// <summary>Column just past the cluster.</summary>
    public int EndColumn => Column + Width;
    /// <summary>What the cluster holds.</summary>
    public TextClusterKind Kind { get; }

    /// <inheritdoc/>
    public bool Equals(TextCluster other) =>
        Start == other.Start && Length == other.Length && Column == other.Column && Width == other.Width && Kind == other.Kind;
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TextCluster other && Equals(other);
    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Start, Length, Column, Width, Kind);
    /// <summary>Compares two clusters for equality.</summary>
    public static bool operator ==(TextCluster left, TextCluster right) => left.Equals(right);
    /// <summary>Compares two clusters for inequality.</summary>
    public static bool operator !=(TextCluster left, TextCluster right) => !left.Equals(right);
    /// <inheritdoc/>
    public override string ToString() => $"[{Start},{Length}) col {Column} w {Width} {Kind}";
}

/// <summary>Options for <see cref="TextLayout"/>.</summary>
public readonly struct TextLayoutOptions
{
    /// <summary>Tab stop distance; values below 1 behave as 1. <see cref="Default"/> uses 8.</summary>
    public int TabSize { get; init; }
    /// <summary>Width profile; <see cref="TextWidthProfile.Default"/> follows <see cref="TextLayout.DefaultProfile"/>.</summary>
    public TextWidthProfile Profile { get; init; }
    /// <summary>Gives East Asian Ambiguous characters two columns instead of one.</summary>
    public bool AmbiguousIsWide { get; init; }
    /// <summary>Gives zero-width clusters one column, so a caret can stop on them (editors).</summary>
    public bool ZeroWidthAsOne { get; init; }

    /// <summary>Tab size 8, default profile, ambiguous characters narrow.</summary>
    public static TextLayoutOptions Default => new() { TabSize = 8 };

    internal TextWidthProfile EffectiveProfile => Profile == TextWidthProfile.Default ? TextLayout.DefaultProfile : Profile;
}

/// <summary>
/// Grapheme clusters and visual columns of UTF-16 text. Segmentation is the runtime's
/// <see cref="StringInfo.GetNextTextElementLength(ReadOnlySpan{char})"/> (UAX #29 with Unicode 16.0 data on .NET 10,
/// without rule GB9c); widths come from a Unicode 16.0.0 table. All offsets are UTF-16 code units.
/// </summary>
/// <remarks>
/// Offsets passed in are expected to be cluster boundaries of the span; a column or offset that falls inside a
/// cluster is resolved to that cluster. Methods taking <c>startColumn</c> lay text out as if it began at that
/// column, which only matters for tab stops. No method allocates.
/// </remarks>
public static class TextLayout
{
    /// <summary>
    /// AppContext switch that restores one column per UTF-16 code unit (<see cref="TextWidthProfile.Legacy"/>)
    /// for everything that uses the default profile.
    /// </summary>
    public const string LegacyCellWidthSwitch = "TSharpVision.Text.LegacyCellWidth";

    private static readonly bool s_legacySwitch =
        AppContext.TryGetSwitch(LegacyCellWidthSwitch, out bool enabled) && enabled;

    [ThreadStatic] private static TextWidthProfile t_profileOverride;

    /// <summary>The profile that <see cref="TextWidthProfile.Default"/> resolves to.</summary>
    public static TextWidthProfile DefaultProfile =>
        t_profileOverride != TextWidthProfile.Default ? t_profileOverride
        : s_legacySwitch ? TextWidthProfile.Legacy : TextWidthProfile.Unicode;

    /// <summary>Test seam: overrides <see cref="DefaultProfile"/> on the current thread; Default clears it.</summary>
    internal static TextWidthProfile ProfileOverride
    {
        get => t_profileOverride;
        set => t_profileOverride = value;
    }

    /// <summary>Unicode version of the width table.</summary>
    public static string UnicodeVersion => UnicodeWidthData.UnicodeVersion;

    /// <summary>Columns occupied by <paramref name="text"/> with <see cref="TextLayoutOptions.Default"/>.</summary>
    public static int Measure(ReadOnlySpan<char> text) => Measure(text, TextLayoutOptions.Default);

    /// <summary>Columns occupied by <paramref name="text"/> laid out from <paramref name="startColumn"/>.</summary>
    public static int Measure(ReadOnlySpan<char> text, in TextLayoutOptions options, int startColumn = 0)
    {
        TextWidthProfile profile = options.EffectiveProfile;
        int column = startColumn;
        int i = 0;
        while (i < text.Length)
        {
            if (TrySimple(text, i, options, profile, out int width))
            {
                column += width;
                i++;
                continue;
            }
            int length = NextClusterLength(text, i);
            column += ClusterWidth(text.Slice(i, length), column, options, profile, out _);
            i += length;
        }
        return column - startColumn;
    }

    /// <summary>The cluster that starts at <paramref name="start"/>, which must be a cluster boundary.</summary>
    public static TextCluster GetCluster(ReadOnlySpan<char> text, int start, int column, in TextLayoutOptions options)
    {
        if ((uint)start >= (uint)text.Length) throw new ArgumentOutOfRangeException(nameof(start));
        TextWidthProfile profile = options.EffectiveProfile;
        int length = NextClusterLength(text, start);
        int width = ClusterWidth(text.Slice(start, length), column, options, profile, out TextClusterKind kind);
        return new TextCluster(start, length, column, width, kind);
    }

    /// <summary>Column of the cluster that contains <paramref name="offset"/>; the end column for the text length.</summary>
    public static int ColumnAtOffset(ReadOnlySpan<char> text, int offset, in TextLayoutOptions options, int startColumn = 0)
    {
        int column = startColumn;
        foreach (TextCluster c in EnumerateClusters(text, options, startColumn))
        {
            if (offset < c.End) return c.Column;
            column = c.EndColumn;
        }
        return column;
    }

    /// <summary>
    /// UTF-16 offset for visual <paramref name="column"/>. A column inside a wide cluster resolves by
    /// <paramref name="bias"/>; a column past the end returns the text length.
    /// </summary>
    /// <param name="text">Text to lay out.</param>
    /// <param name="column">Target column, in the same coordinates as <paramref name="startColumn"/>.</param>
    /// <param name="options">Layout options.</param>
    /// <param name="bias">Resolution inside a wide cluster.</param>
    /// <param name="clusterColumn">Column of the returned offset.</param>
    /// <param name="startColumn">Column at which the text begins.</param>
    public static int OffsetAtColumn(ReadOnlySpan<char> text, int column, in TextLayoutOptions options,
                                     ColumnBias bias, out int clusterColumn, int startColumn = 0)
    {
        int end = startColumn;
        foreach (TextCluster c in EnumerateClusters(text, options, startColumn))
        {
            if (column <= c.Column)
            {
                clusterColumn = c.Column;
                return c.Start;
            }
            if (column < c.EndColumn)
            {
                bool toEnd = bias == ColumnBias.Ceiling
                    || (bias == ColumnBias.Nearest && (column - c.Column) * 2 > c.Width);
                clusterColumn = toEnd ? c.EndColumn : c.Column;
                return toEnd ? c.End : c.Start;
            }
            end = c.EndColumn;
        }
        clusterColumn = end;
        return text.Length;
    }

    /// <summary>
    /// Number of UTF-16 code units of <paramref name="text"/> that fit into <paramref name="maxColumns"/> without
    /// splitting a cluster; <paramref name="usedColumns"/> receives their width.
    /// </summary>
    public static int Fit(ReadOnlySpan<char> text, int maxColumns, in TextLayoutOptions options,
                          out int usedColumns, int startColumn = 0)
    {
        int units = 0;
        usedColumns = 0;
        foreach (TextCluster c in EnumerateClusters(text, options, startColumn))
        {
            int used = c.EndColumn - startColumn;
            if (used > maxColumns) break;
            units = c.End;
            usedColumns = used;
        }
        return units;
    }

    /// <summary>Enumerates the clusters of <paramref name="text"/> without allocating.</summary>
    public static TextClusterEnumerator EnumerateClusters(ReadOnlySpan<char> text, in TextLayoutOptions options, int startColumn) =>
        new(text, options, startColumn);

    /// <summary>
    /// Enumerates the clusters of the concatenation <paramref name="first"/> + <paramref name="second"/> (the two
    /// halves of a gap buffer) without allocating. A cluster may straddle the halves; offsets are global.
    /// </summary>
    public static GapTextClusterEnumerator EnumerateClusters(ReadOnlySpan<char> first, ReadOnlySpan<char> second,
                                                             in TextLayoutOptions options, int startColumn) =>
        new(first, second, options, startColumn);

    /// <summary>Length of the cluster at <paramref name="index"/>, with an allocation-free Latin fast path.</summary>
    internal static int NextClusterLength(ReadOnlySpan<char> text, int index)
    {
        if (IsSimple(text[index]) && (index + 1 == text.Length || text[index + 1] < '\u0300'))
            return 1;
        return StringInfo.GetNextTextElementLength(text.Slice(index));
    }

    // Printable U+0020..U+02FF other than C1 controls and U+00AD: always a cluster of its own when the next
    // unit is below U+0300 (no Extend, SpacingMark, ZWJ, Prepend or surrogate lies below U+0300).
    private static bool IsSimple(char c) =>
        c >= ' ' && c < '\u0300' && (c < '\u007F' || c > '\u009F') && c != '\u00AD';

    /// <summary>
    /// Fast path: the unit at <paramref name="index"/> is a one-unit printable Latin cluster; returns its width
    /// without segmentation.
    /// </summary>
    internal static bool TrySimple(ReadOnlySpan<char> text, int index, in TextLayoutOptions options,
                                   TextWidthProfile profile, out int width)
    {
        char c = text[index];
        bool ideograph = IsUnifiedIdeograph(c);
        if (!(ideograph || IsSimple(c))
            || (index + 1 < text.Length && text[index + 1] >= '\u0300' && !IsUnifiedIdeograph(text[index + 1])))
        {
            width = 0;
            return false;
        }
        if (profile == TextWidthProfile.Legacy) width = 1;
        else if (ideograph) width = 2;
        else width = c >= '\u007F' && options.AmbiguousIsWide
                     && (UnicodeWidthData.Get(c) & UnicodeWidthData.EawAmbiguous) != 0 ? 2 : 1;
        return true;
    }

    // CJK Unified Ideographs and Extension A: Grapheme_Cluster_Break Other, East Asian Width W.
    private static bool IsUnifiedIdeograph(char c) => (c >= '\u4E00' && c <= '\u9FFF') || (c >= '\u3400' && c <= '\u4DBF');

    private static bool IsControl(char c) => c < ' ' || (c >= '\u007F' && c <= '\u009F');

    /// <summary>Width and kind of one cluster laid out at <paramref name="column"/>.</summary>
    internal static int ClusterWidth(ReadOnlySpan<char> cluster, int column, in TextLayoutOptions options,
                                     TextWidthProfile profile, out TextClusterKind kind)
    {
        char first = cluster[0];
        if (cluster.Length == 1 && first < '\u007F' && first >= ' ')
        {
            kind = TextClusterKind.Text;
            return profile == TextWidthProfile.Legacy ? cluster.Length : 1;
        }
        if (first == '\t')
        {
            kind = TextClusterKind.Tab;
            int stop = Math.Max(1, options.TabSize);
            return stop - (int)((uint)column % (uint)stop);
        }
        if (first == '\n' || (first == '\r' && cluster.Length > 1))
        {
            kind = TextClusterKind.LineBreak;
            return 0;
        }
        if (IsControl(first))
        {
            kind = TextClusterKind.Control;
            return profile == TextWidthProfile.Legacy ? cluster.Length : 1;
        }
        if (Rune.DecodeFromUtf16(cluster, out _, out _) != OperationStatus.Done)
        {
            kind = TextClusterKind.Malformed;
            return profile == TextWidthProfile.Legacy ? cluster.Length : 1;
        }

        kind = TextClusterKind.Text;
        if (profile == TextWidthProfile.Legacy) return cluster.Length;
        int width = profile == TextWidthProfile.CodePoint
            ? CodePointWidth(cluster, options)
            : UnicodeWidth(cluster, options);
        if (width == 0)
        {
            kind = TextClusterKind.ZeroWidth;
            return options.ZeroWidthAsOne ? 1 : 0;
        }
        return width;
    }

    private static int UnicodeWidth(ReadOnlySpan<char> cluster, in TextLayoutOptions options)
    {
        int visible = -1;
        bool vs15 = false, vs16 = false, zwj = false;
        for (int i = 0; i < cluster.Length;)
        {
            Rune.DecodeFromUtf16(cluster.Slice(i), out Rune rune, out int used);
            int cp = rune.Value;
            if (cp == 0xFE0F) vs16 = true;
            else if (cp == 0xFE0E) vs15 = true;
            else if (cp == 0x200D) zwj = true;
            else if (visible < 0 && (UnicodeWidthData.Get(cp) & UnicodeWidthData.ZeroWidth) == 0) visible = cp;
            i += used;
        }
        if (visible < 0) return 0;

        byte p = UnicodeWidthData.Get(visible);
        if ((p & UnicodeWidthData.EmojiVariationBase) != 0)
        {
            if (vs16) return 2;
            if (vs15) return 1;
        }
        if (zwj && (p & UnicodeWidthData.ExtendedPictographic) != 0) return 2;
        if ((p & (UnicodeWidthData.EawWide | UnicodeWidthData.EmojiPresentation | UnicodeWidthData.RegionalIndicator)) != 0)
            return 2;
        if ((p & UnicodeWidthData.EawAmbiguous) != 0 && options.AmbiguousIsWide) return 2;
        return 1;
    }

    private static int CodePointWidth(ReadOnlySpan<char> cluster, in TextLayoutOptions options)
    {
        int width = 0;
        for (int i = 0; i < cluster.Length;)
        {
            if (Rune.DecodeFromUtf16(cluster.Slice(i), out Rune rune, out int used) != OperationStatus.Done)
            {
                width++;
                i += Math.Max(1, used);
                continue;
            }
            byte p = UnicodeWidthData.Get(rune.Value);
            if ((p & (UnicodeWidthData.ZeroWidth | UnicodeWidthData.HangulMedialFinal)) != 0) { }
            else if ((p & (UnicodeWidthData.EawWide | UnicodeWidthData.EmojiPresentation)) != 0) width += 2;
            else if ((p & UnicodeWidthData.EawAmbiguous) != 0 && options.AmbiguousIsWide) width += 2;
            else width += 1;
            i += used;
        }
        return width;
    }
}

/// <summary>Allocation-free enumerator over the clusters of one span.</summary>
public ref struct TextClusterEnumerator
{
    private readonly ReadOnlySpan<char> _text;
    private readonly TextLayoutOptions _options;
    private readonly TextWidthProfile _profile;
    private int _position;
    private int _column;
    private TextCluster _current;

    internal TextClusterEnumerator(ReadOnlySpan<char> text, in TextLayoutOptions options, int startColumn)
    {
        _text = text;
        _options = options;
        _profile = options.EffectiveProfile;
        _column = startColumn;
    }

    /// <summary>The current cluster.</summary>
    public readonly TextCluster Current => _current;

    /// <summary>Returns this enumerator, for <c>foreach</c>.</summary>
    public readonly TextClusterEnumerator GetEnumerator() => this;

    /// <summary>Advances to the next cluster.</summary>
    public bool MoveNext()
    {
        if (_position >= _text.Length) return false;
        int length = 1, width;
        TextClusterKind kind = TextClusterKind.Text;
        if (!TextLayout.TrySimple(_text, _position, _options, _profile, out width))
        {
            length = TextLayout.NextClusterLength(_text, _position);
            width = TextLayout.ClusterWidth(_text.Slice(_position, length), _column, _options, _profile, out kind);
        }
        _current = new TextCluster(_position, length, _column, width, kind);
        _position += length;
        _column += width;
        return true;
    }
}

/// <summary>Allocation-free enumerator over the clusters of two adjacent spans (a gap buffer).</summary>
public ref struct GapTextClusterEnumerator
{
    private readonly ReadOnlySpan<char> _first;
    private readonly ReadOnlySpan<char> _second;
    private readonly TextLayoutOptions _options;
    private readonly TextWidthProfile _profile;
    private int _position;
    private int _column;
    private TextCluster _current;

    internal GapTextClusterEnumerator(ReadOnlySpan<char> first, ReadOnlySpan<char> second, in TextLayoutOptions options, int startColumn)
    {
        _first = first;
        _second = second;
        _options = options;
        _profile = options.EffectiveProfile;
        _column = startColumn;
    }

    /// <summary>The current cluster; offsets count from the start of the first span.</summary>
    public readonly TextCluster Current => _current;

    /// <summary>Returns this enumerator, for <c>foreach</c>.</summary>
    public readonly GapTextClusterEnumerator GetEnumerator() => this;

    /// <summary>Advances to the next cluster.</summary>
    public bool MoveNext()
    {
        int firstLength = _first.Length;
        if (_position >= firstLength + _second.Length) return false;

        int length, width;
        TextClusterKind kind;
        if (_position >= firstLength)
        {
            ReadOnlySpan<char> rest = _second.Slice(_position - firstLength);
            length = TextLayout.NextClusterLength(rest, 0);
            width = TextLayout.ClusterWidth(rest.Slice(0, length), _column, _options, _profile, out kind);
        }
        else
        {
            ReadOnlySpan<char> rest = _first.Slice(_position);
            length = TextLayout.NextClusterLength(rest, 0);
            // A boundary is final only when the scalar after it lies wholly in the first span: a high surrogate
            // at its end may pair with the second span and join the cluster (a skin tone, a regional indicator).
            int reliable = char.IsHighSurrogate(rest[^1]) && !_second.IsEmpty ? rest.Length - 1 : rest.Length;
            if (length < reliable || _second.IsEmpty)
                width = TextLayout.ClusterWidth(rest.Slice(0, length), _column, _options, _profile, out kind);
            else
                length = Straddle(rest, out width, out kind);
        }

        _current = new TextCluster(_position, length, _column, width, kind);
        _position += length;
        _column += width;
        return true;
    }

    // The cluster starting in the first span reaches its end: segment a pooled copy of its tail plus a growing
    // prefix of the second span until the cluster ends inside the window or the input is exhausted. The window
    // never ends on a high surrogate while input remains, so the scalar after the cluster is always complete.
    private int Straddle(ReadOnlySpan<char> head, out int width, out TextClusterKind kind)
    {
        int take = Math.Min(_second.Length, 16);
        while (true)
        {
            if (take < _second.Length && char.IsHighSurrogate(_second[take - 1])) take++;
            int windowLength = head.Length + take;
            char[] rented = ArrayPool<char>.Shared.Rent(windowLength);
            try
            {
                Span<char> window = rented.AsSpan(0, windowLength);
                head.CopyTo(window);
                _second.Slice(0, take).CopyTo(window.Slice(head.Length));
                int length = StringInfo.GetNextTextElementLength(window);
                if (length < windowLength || take == _second.Length)
                {
                    width = TextLayout.ClusterWidth(window.Slice(0, length), _column, _options, _profile, out kind);
                    return length;
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rented);
            }
            take = (int)Math.Min(_second.Length, (long)take * 2);
        }
    }
}

/// <summary>
/// Measures and parses Turbo Vision <c>~hotkey~</c> markup: '~' toggles the highlight and is not drawn.
/// </summary>
internal static class HotkeyText
{
    /// <summary>Columns of <paramref name="text"/> without its '~' markers (<c>cstrlen</c>).</summary>
    public static int Measure(ReadOnlySpan<char> text, in TextLayoutOptions options, int startColumn = 0)
    {
        int column = startColumn;
        while (!text.IsEmpty)
        {
            int tilde = text.IndexOf('~');
            ReadOnlySpan<char> segment = tilde < 0 ? text : text.Slice(0, tilde);
            column += TextLayout.Measure(segment, options, column);
            if (tilde < 0) break;
            text = text.Slice(tilde + 1);
        }
        return column - startColumn;
    }

    /// <summary>Columns of <paramref name="text"/> without its '~' markers, with default options.</summary>
    public static int Measure(string? text) =>
        text is null ? 0 : Measure(text, TextLayoutOptions.Default with { ZeroWidthAsOne = true });

    /// <summary>The upper-cased character after the first '~', or '\0'.</summary>
    public static char GetHotkey(string? text)
    {
        if (string.IsNullOrEmpty(text)) return '\0';
        int tilde = text.IndexOf('~');
        return tilde >= 0 && tilde + 1 < text.Length ? char.ToUpperInvariant(text[tilde + 1]) : '\0';
    }
}
