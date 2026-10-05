namespace TSharpVision.Diagnostics.Keyboard.Profiles;

/// <summary>What one key position produces on each level of a layout.</summary>
/// <remarks>A null level has no character. A dead level produces no text when the key is pressed alone.</remarks>
public sealed record KeyLevels(string? Plain, string? Shift = null, string? AltGr = null,
    bool DeadPlain = false, bool DeadShift = false);

/// <summary>A plain dead key followed by a plain base key, and the single text commit they produce.</summary>
public sealed record DeadKeyComposition(PhysicalKey Dead, PhysicalKey Base, string Text);

/// <summary>The modifier held around the key of a step.</summary>
public enum KeyLevel { Plain, Shift, AltGr }

/// <summary>Expected text per key position for one (operating system, layout) pair.</summary>
public sealed class LayoutProfile
{
    public LayoutProfile(string id, string name, string layoutId,
        IReadOnlyDictionary<PhysicalKey, KeyLevels> keys, IReadOnlyList<(PhysicalKey Key, string Text)> fingerprint,
        IReadOnlyList<DeadKeyComposition>? compositions = null)
    {
        Compositions = compositions ?? [];
        Id = id;
        Name = name;
        LayoutId = layoutId;
        Keys = keys;
        Fingerprint = fingerprint;
    }

    /// <summary>The CLI name, such as <c>cz-qwerty</c>.</summary>
    public string Id { get; }
    public string Name { get; }
    /// <summary>The Windows KLID the table was verified against.</summary>
    public string LayoutId { get; }
    public IReadOnlyDictionary<PhysicalKey, KeyLevels> Keys { get; }
    /// <summary>Keys whose text tells this layout from its neighbours.</summary>
    public IReadOnlyList<(PhysicalKey Key, string Text)> Fingerprint { get; }

    /// <summary>Dead-key compositions observed on the live layout; none are assumed.</summary>
    public IReadOnlyList<DeadKeyComposition> Compositions { get; }

    /// <summary>The text of a level, or null when the level has no character or is a dead key.</summary>
    public string? Text(PhysicalKey key, KeyLevel level)
    {
        if (!Keys.TryGetValue(key, out KeyLevels? levels)) return null;
        return level switch
        {
            KeyLevel.Plain => levels.DeadPlain ? null : levels.Plain,
            KeyLevel.Shift => levels.DeadShift ? null : levels.Shift,
            _ => levels.AltGr,
        };
    }

    public bool IsDead(PhysicalKey key, KeyLevel level) =>
        Keys.TryGetValue(key, out KeyLevels? levels)
        && (level == KeyLevel.Plain ? levels.DeadPlain : level == KeyLevel.Shift && levels.DeadShift);

    public static IReadOnlyList<LayoutProfile> All { get; } = [UsProfile.Instance, CzechQwertyProfile.Instance];

    public static LayoutProfile? Find(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds the 26 letter keys, which both built-in layouts place as QWERTY.</summary>
    internal static void AddQwertyLetters(Dictionary<PhysicalKey, KeyLevels> keys)
    {
        foreach (PhysicalKey key in PhysicalKeys.Letters)
        {
            char letter = PhysicalKeys.Letter(key);
            keys[key] = new KeyLevels(letter.ToString(), char.ToUpperInvariant(letter).ToString());
        }
    }
}
