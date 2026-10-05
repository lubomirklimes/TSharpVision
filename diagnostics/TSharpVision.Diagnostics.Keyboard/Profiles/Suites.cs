using System.Text;
using TSharpVision.Constants;
using TSharpVision.Diagnostics.Keyboard.Controller;
using TSharpVision.Drivers;

namespace TSharpVision.Diagnostics.Keyboard.Profiles;

/// <summary>One logical user action and what it must produce. The unit of comparison; never one event.</summary>
public sealed record Step(string Id, string Suite, string Prompt, string Expected, Expectation Expect)
{
    /// <summary>The key position under test; null for transport-level (PTY byte) steps.</summary>
    public PhysicalKey? Key { get; init; }
    public KeyLevel Level { get; init; }
    /// <summary>Bytes the PTY source writes, one write per chunk. Empty for physical-key steps.</summary>
    public IReadOnlyList<byte[]> Chunks { get; init; } = [];
    /// <summary>The physical key transitions of the action, in order. What a prompted human or the OS source performs.</summary>
    public IReadOnlyList<KeyStroke> Strokes { get; init; } = [];
    /// <summary>Additional key-downs of the last key before its release: a held, repeating key.</summary>
    public int Repeats { get; init; }
    /// <summary>Lock state the step assumes. Only native injection can supply it without touching the machine.</summary>
    public LockState Locks { get; init; }
    /// <summary>The key-downs must reach the input queue together, before any of them is processed.</summary>
    public bool Batch { get; init; }
    /// <summary>An explicit SDL event order that no physical action is claimed to produce (synthetic stress).</summary>
    public IReadOnlyList<Protocol.NativeEvent>? SdlScript { get; init; }
    /// <summary>The step states something only an injected native event can state (a lock state, an event order).</summary>
    public bool NativeOnly { get; init; }
}

/// <summary>One physical key transition, with the text the layout commits on that key-down, if any.</summary>
public readonly record struct KeyStroke(PhysicalKey Key, bool Down, string? Text = null);

[Flags]
public enum LockState { None = 0, Caps = 1, Num = 2 }

/// <summary>Suite categories and the steps they contain.</summary>
public static class Suites
{
    public const string Full = "full";
    public static readonly IReadOnlyList<string> Names =
        ["main", "punctuation", "function", "navigation", "keypad", "modifiers", "unicode", "release", Full];

    private const uint Shift = Keys.kbShift, Ctrl = Keys.kbCtrlShift, Alt = Keys.kbAltShift;
    private const uint AnyModifier = Shift | Ctrl | Alt;

    private static readonly PhysicalKey[] NumberRow =
    [
        PhysicalKey.Backquote, PhysicalKey.Digit1, PhysicalKey.Digit2, PhysicalKey.Digit3, PhysicalKey.Digit4,
        PhysicalKey.Digit5, PhysicalKey.Digit6, PhysicalKey.Digit7, PhysicalKey.Digit8, PhysicalKey.Digit9,
        PhysicalKey.Digit0, PhysicalKey.Minus, PhysicalKey.Equal,
    ];

    private static readonly PhysicalKey[] Punctuation =
    [
        PhysicalKey.BracketLeft, PhysicalKey.BracketRight, PhysicalKey.Backslash, PhysicalKey.Semicolon,
        PhysicalKey.Quote, PhysicalKey.Comma, PhysicalKey.Period, PhysicalKey.Slash, PhysicalKey.IntlBackslash,
    ];

    /// <summary>Layout-independent keys and the identity every identifying transport must report.</summary>
    public static readonly IReadOnlyDictionary<PhysicalKey, ushort> NamedKeys = new Dictionary<PhysicalKey, ushort>
    {
        [PhysicalKey.Escape] = Keys.kbEsc, [PhysicalKey.Tab] = Keys.kbTab,
        [PhysicalKey.Backspace] = Keys.kbBack, [PhysicalKey.Enter] = Keys.kbEnter,
        [PhysicalKey.F1] = Keys.kbF1, [PhysicalKey.F2] = Keys.kbF2, [PhysicalKey.F3] = Keys.kbF3,
        [PhysicalKey.F4] = Keys.kbF4, [PhysicalKey.F5] = Keys.kbF5, [PhysicalKey.F6] = Keys.kbF6,
        [PhysicalKey.F7] = Keys.kbF7, [PhysicalKey.F8] = Keys.kbF8, [PhysicalKey.F9] = Keys.kbF9,
        [PhysicalKey.F10] = Keys.kbF10, [PhysicalKey.F11] = Keys.kbF11, [PhysicalKey.F12] = Keys.kbF12,
        [PhysicalKey.Insert] = Keys.kbIns, [PhysicalKey.Delete] = Keys.kbDel,
        [PhysicalKey.Home] = Keys.kbHome, [PhysicalKey.End] = Keys.kbEnd,
        [PhysicalKey.PageUp] = Keys.kbPgUp, [PhysicalKey.PageDown] = Keys.kbPgDn,
        [PhysicalKey.ArrowUp] = Keys.kbUp, [PhysicalKey.ArrowDown] = Keys.kbDown,
        [PhysicalKey.ArrowLeft] = Keys.kbLeft, [PhysicalKey.ArrowRight] = Keys.kbRight,
    };

    /// <summary>Keypad keys and the identity a driver advertising a distinct keypad must report.</summary>
    public static readonly IReadOnlyDictionary<PhysicalKey, ushort> KeypadKeys = new Dictionary<PhysicalKey, ushort>
    {
        [PhysicalKey.Numpad0] = Keys.kbKeypad0, [PhysicalKey.Numpad1] = Keys.kbKeypad1,
        [PhysicalKey.Numpad2] = Keys.kbKeypad2, [PhysicalKey.Numpad3] = Keys.kbKeypad3,
        [PhysicalKey.Numpad4] = Keys.kbKeypad4, [PhysicalKey.Numpad5] = Keys.kbKeypad5,
        [PhysicalKey.Numpad6] = Keys.kbKeypad6, [PhysicalKey.Numpad7] = Keys.kbKeypad7,
        [PhysicalKey.Numpad8] = Keys.kbKeypad8, [PhysicalKey.Numpad9] = Keys.kbKeypad9,
        [PhysicalKey.NumpadDecimal] = Keys.kbKeypadDecimal, [PhysicalKey.NumpadDivide] = Keys.kbKeypadDivide,
        [PhysicalKey.NumpadMultiply] = Keys.kbKeypadMultiply, [PhysicalKey.NumpadSubtract] = Keys.kbGrayMinus,
        [PhysicalKey.NumpadAdd] = Keys.kbGrayPlus, [PhysicalKey.NumpadEnter] = Keys.kbKeypadEnter,
    };

    /// <summary>The physical-key steps of a suite for one layout.</summary>
    /// <param name="latin1Identity">
    /// The Console driver's pinned policy: text in U+0080..U+00FF may carry its own value as the key code.
    /// </param>
    /// <param name="lockKeys">Include steps that press a lock key and so toggle machine state (opt-in).</param>
    public static IReadOnlyList<Step> Build(LayoutProfile profile, string suite, bool latin1Identity, bool lockKeys = false)
    {
        IEnumerable<Step> steps = suite switch
        {
            "main" => MainKeys(profile, latin1Identity),
            "punctuation" => Punctuation.Select(k => TextKey(profile, k, KeyLevel.Plain, "punctuation", latin1Identity)),
            "function" => Named("function", k => k is >= PhysicalKey.F1 and <= PhysicalKey.F12),
            "navigation" => Named("navigation", k => k is < PhysicalKey.F1 or > PhysicalKey.F12),
            "keypad" => KeypadSuite(lockKeys),
            "modifiers" => Modifiers(profile, latin1Identity),
            "unicode" => Unicode(profile, latin1Identity),
            "release" => Release(profile, latin1Identity),
            Full => Names.Where(n => n != Full).SelectMany(n => Build(profile, n, latin1Identity, lockKeys)),
            _ => throw new ArgumentException($"Unknown suite '{suite}'.", nameof(suite)),
        };
        // The same action can belong to several categories; in a full run it is performed once.
        return steps.DistinctBy(s => s.Id).ToArray();
    }

    // A tap of one key, optionally inside held modifiers that go up in reverse order.
    private static KeyStroke[] Tap(PhysicalKey key, string? text = null, params PhysicalKey[] held) =>
    [
        .. held.Select(h => new KeyStroke(h, true)),
        new KeyStroke(key, true, text), new KeyStroke(key, false),
        .. held.Reverse().Select(h => new KeyStroke(h, false)),
    ];

    private static IEnumerable<Step> KeypadSuite(bool lockKeys)
    {
        foreach (PhysicalKey key in KeypadKeys.Keys) yield return Keypad(key);
        // Num Lock as a state, which only an injected event can claim without changing the machine.
        yield return Keypad(PhysicalKey.Numpad1) with
        {
            Id = "NumLock+Numpad1", NativeOnly = true, Expected = "kbKeypad1, Num Lock on",
            Expect = new Expectation(
            [
                ExpectedItem.Press(IdentityRule.Exact(Keys.kbKeypad1)) with { RequiredState = Keys.kbNumState },
                ExpectedItem.Press(IdentityRule.Any) with { Optional = true },
                ExpectedItem.Release(),
            ])
            { Requires = KeyboardCapabilities.DistinctNumericKeypad },
        };
        if (!lockKeys) yield break;
        yield return new Step("NumLock", "keypad", "Tap NumLock twice", "kbNumLock, twice", new Expectation(
            [
                ExpectedItem.Press(IdentityRule.Exact(Keys.kbNumLock)), ExpectedItem.Release(),
                ExpectedItem.Press(IdentityRule.Exact(Keys.kbNumLock)),
                ExpectedItem.Release() with { Identity = IdentityRule.Exact(Keys.kbNumLock) },
            ])
            { TextTotal = string.Empty, Requires = KeyboardCapabilities.DistinctNumericKeypad })
        {
            Key = PhysicalKey.NumLock, Strokes = [.. Tap(PhysicalKey.NumLock), .. Tap(PhysicalKey.NumLock)],
        };
    }

    private static IEnumerable<Step> MainKeys(LayoutProfile profile, bool latin1) =>
        NumberRow.Concat(PhysicalKeys.Letters).Append(PhysicalKey.Space)
            .Select(k => TextKey(profile, k, KeyLevel.Plain, "main", latin1));

    private static IEnumerable<Step> Named(string suite, Func<PhysicalKey, bool> filter) =>
        NamedKeys.Where(k => filter(k.Key)).Select(k => NamedKey(k.Key, k.Value, suite));

    private static IEnumerable<Step> Unicode(LayoutProfile profile, bool latin1)
    {
        var steps = new List<Step>();
        foreach (PhysicalKey key in NumberRow.Concat(Punctuation).Concat(PhysicalKeys.Letters))
        {
            if (profile.IsDead(key, KeyLevel.Plain) || IsNonAscii(profile.Text(key, KeyLevel.Plain)))
                steps.Add(TextKey(profile, key, KeyLevel.Plain, "unicode", latin1));
            if (IsNonAscii(profile.Text(key, KeyLevel.AltGr)))
                steps.Add(TextKey(profile, key, KeyLevel.AltGr, "unicode", latin1));
        }
        steps.AddRange(profile.Compositions.Select(c => Composition(c, latin1)));
        // A layout without non-ASCII keys still has a baseline: every digit must keep its ASCII identity.
        return steps.Count != 0 ? steps
            : NumberRow.Skip(1).Take(10).Select(k => TextKey(profile, k, KeyLevel.Plain, "unicode", latin1));
    }

    private static IEnumerable<Step> Modifiers(LayoutProfile profile, bool latin1)
    {
        yield return BareModifier("Shift", Shift);
        yield return BareModifier("Ctrl", Ctrl);
        yield return TextKey(profile, PhysicalKey.Digit2, KeyLevel.Shift, "modifiers", latin1);
        yield return TextKey(profile, PhysicalKey.KeyA, KeyLevel.Shift, "modifiers", latin1);
        yield return Chord("Ctrl", Ctrl, PhysicalKey.KeyA, Keys.kbCtrlA);
        yield return Chord("Alt", Alt, PhysicalKey.KeyX, Keys.kbAltX);
        yield return Chord("Shift", Shift, PhysicalKey.Tab, Keys.kbShiftTab);
        foreach (PhysicalKey key in new[] { PhysicalKey.Digit2, PhysicalKey.KeyE })
            if (profile.Text(key, KeyLevel.AltGr) != null)
                yield return TextKey(profile, key, KeyLevel.AltGr, "modifiers", latin1);
        // Right Alt on a key the layout gives no AltGr character: an ordinary Alt shortcut.
        Step rightAlt = new Step("AltRight+KeyX", "modifiers", $"Hold the right Alt, tap {Cap(PhysicalKey.KeyX)}, then release Alt",
            KeyNames.Describe(Keys.kbAltX), new Expectation(
            [
                ExpectedItem.Press(IdentityRule.Exact(Keys.kbAltX)) with { RequiredState = Alt },
                ExpectedItem.Release(),
            ])
            { TextTotal = string.Empty, AllowRepeat = true, AllowModifierEvents = true })
        {
            Key = PhysicalKey.KeyX, Strokes = Tap(PhysicalKey.KeyX, null, PhysicalKey.AltRight),
        };
        yield return rightAlt;
        // Held until it repeats: a driver that had to wait for a text commit publishes from the first repeat.
        yield return rightAlt with
        {
            Id = "AltRight+KeyX held", Repeats = 2,
            Prompt = $"Hold the right Alt, hold {Cap(PhysicalKey.KeyX)} until it repeats, release both",
        };
        // Caps Lock as a state: only an injected event can claim it without changing the machine.
        yield return new Step("CapsLock+KeyA", "modifiers", "With Caps Lock on, tap KeyA", "\"A\", Caps Lock on",
            new Expectation(
            [
                ExpectedItem.Press(IdentityRule.Exact('A')) with { RequiredState = Keys.kbCapsState, ForbiddenState = AnyModifier },
                ExpectedItem.Release(),
            ])
            { TextTotal = "A" })
        {
            Key = PhysicalKey.KeyA, Locks = LockState.Caps, NativeOnly = true, Strokes = Tap(PhysicalKey.KeyA, "A"),
        };
    }

    private static IEnumerable<Step> Release(LayoutProfile profile, bool latin1)
    {
        Step[] steps =
        [
            TextKey(profile, PhysicalKey.KeyA, KeyLevel.Plain, "release", latin1),
            TextKey(profile, PhysicalKey.Digit2, KeyLevel.Plain, "release", latin1),
            NamedKey(PhysicalKey.F5, Keys.kbF5, "release"),
            NamedKey(PhysicalKey.ArrowUp, Keys.kbUp, "release"),
            NamedKey(PhysicalKey.Enter, Keys.kbEnter, "release"),
            Keypad(PhysicalKey.Numpad1) with { Suite = "release" },
        ];
        Step held = TextKey(profile, PhysicalKey.KeyA, KeyLevel.Plain, "release", latin1);
        string a = profile.Text(PhysicalKey.KeyA, KeyLevel.Plain)!, b = profile.Text(PhysicalKey.KeyB, KeyLevel.Plain)!;
        Step[] extra =
        [
            held with { Id = "KeyA held", Prompt = $"Hold {Cap(PhysicalKey.KeyA)} until it repeats, then release", Repeats = 2 },
            // Two keys down before either goes up. Each key's text must follow its own key-down, so
            // each press, and each release, keeps the identity of its own key.
            new Step("KeyA+KeyB rollover", "release",
                $"Press {Cap(PhysicalKey.KeyA)}, press {Cap(PhysicalKey.KeyB)}, release A, release B",
                KeyNames.Quote(a + b), new Expectation(
                [
                    ExpectedItem.Press(IdentityRule.Exact(a[0])) with { ForbiddenState = AnyModifier },
                    ExpectedItem.Press(IdentityRule.Exact(b[0])),
                    ExpectedItem.Release(),
                    ExpectedItem.Release() with { Identity = IdentityRule.Exact(b[0]) },
                ])
                { TextTotal = a + b })
            {
                Key = PhysicalKey.KeyA, Batch = true,
                Strokes =
                [
                    new KeyStroke(PhysicalKey.KeyA, true, a), new KeyStroke(PhysicalKey.KeyB, true, b),
                    new KeyStroke(PhysicalKey.KeyA, false), new KeyStroke(PhysicalKey.KeyB, false),
                ],
            },
        ];
        return steps.Concat(extra).Concat(SdlEventOrders()).Select(s => s with
        {
            Expect = s.Expect with { Requires = KeyboardCapabilities.KeyReleaseEvents },
        });
    }

    // Synthetic SDL event orders, pushed straight onto the event queue. They are stress coverage
    // for the held-key tracker, not a claim about what a keyboard produces: on every SDL backend a
    // key's text is generated while its own key-down is handled, so the commit follows that
    // key-down directly (Level C confirms it on Windows). Whatever the order, typed text must be
    // intact and no release may carry an identity that no press reported.
    private static IEnumerable<Step> SdlEventOrders()
    {
        static Protocol.NativeEvent K(bool down, uint key, uint mod = 0) =>
            new(Protocol.NativeEvent.KeyKind, down, key, State: mod);
        static Protocol.NativeEvent T(string text) => new(Protocol.NativeEvent.TextKind, Text: text);
        static Step Script(string id, string expected, Expectation expect, params Protocol.NativeEvent[] script) =>
            new("sdl: " + id, "release", "SDL event order: " + id, expected, expect) { NativeOnly = true, SdlScript = script };
        ExpectedItem anyRelease(params ushort[] codes) =>
            ExpectedItem.Release() with { Identity = IdentityRule.NoneOrOneOf(codes), Optional = true };

        foreach (bool reversed in new[] { false, true })
        {
            yield return Script($"two key-downs before either commit{(reversed ? ", reverse release" : "")}", "\"ab\"",
                new Expectation(
                [
                    ExpectedItem.Press(IdentityRule.Exact('a')), ExpectedItem.Press(IdentityRule.Exact('b')),
                    anyRelease('a', 'b'), anyRelease('a', 'b'),
                ])
                { TextTotal = "ab" },
                [K(true, 'a'), K(true, 'b'), T("a"), T("b"), .. reversed
                    ? new[] { K(false, 'b'), K(false, 'a') } : new[] { K(false, 'a'), K(false, 'b') }]);
        }
        yield return Script("two text-only key-downs before either commit", KeyNames.Quote("ěš"),
            new Expectation(
            [
                ExpectedItem.Press(IdentityRule.None), ExpectedItem.Press(IdentityRule.None),
                anyRelease(), anyRelease(),
            ])
            { TextTotal = "ěš", ForbiddenIdentities = ['2', '3'] },
            K(true, '2'), K(true, '3'), T("ě"), T("š"), K(false, '2'), K(false, '3'));
        yield return Script("focus lost while keys and Shift are held", "everything released",
            new Expectation(
            [
                ExpectedItem.Press(IdentityRule.Exact(Keys.kbF5)), ExpectedItem.Press(IdentityRule.None),
                ExpectedItem.Modifier(Shift, 0),
                ExpectedItem.Release(), ExpectedItem.Release() with { Identity = IdentityRule.None },
                ExpectedItem.Modifier(0, AnyModifier),
            ])
            { TextTotal = "ě", ForbiddenIdentities = ['2'] },
            K(true, NativeKeys.SdlScancodeMask | 62), K(true, '2'), T("ě"),
            K(true, NativeKeys.SdlScancodeMask | 225, Sources.NativeEvents.SdlLeftShift),
            new Protocol.NativeEvent(Protocol.NativeEvent.FocusLostKind));
        yield return Script("supplementary-plane text", KeyNames.Quote("😀"),
            new Expectation([ExpectedItem.Press(IdentityRule.None), ExpectedItem.Release() with { Identity = IdentityRule.None }])
            { TextTotal = "😀", ForbiddenIdentities = ['e', 'E'] },
            K(true, 'e'), T("😀"), K(false, 'e'));
    }

    /// <summary>A dead key followed by the letter it combines with: one commit of the composed character.</summary>
    public static Step Composition(DeadKeyComposition composition, bool latin1Identity)
    {
        string text = composition.Text;
        string letter = UsProfile.Instance.Text(composition.Base, KeyLevel.Plain) ?? string.Empty;
        return new Step($"{composition.Dead},{composition.Base}", "unicode",
            $"Tap {Cap(composition.Dead)}, then tap {Cap(composition.Base)}", KeyNames.Quote(text), new Expectation(
            [
                ExpectedItem.Press(TextIdentity(text, latin1Identity)) with { ForbiddenState = AnyModifier },
                ExpectedItem.Release(),
            ])
            {
                TextTotal = text,
                // The composed text must not take the identity of the letter key that completed it.
                ForbiddenIdentities = [.. letter.Select(c => (ushort)c), .. letter.ToUpperInvariant().Select(c => (ushort)c)],
            })
        {
            Key = composition.Base,
            Strokes =
            [
                new KeyStroke(composition.Dead, true), new KeyStroke(composition.Dead, false),
                new KeyStroke(composition.Base, true, text), new KeyStroke(composition.Base, false),
            ],
        };
    }

    /// <summary>A key whose result is decided by the layout: text, and an identity only where the text is ASCII.</summary>
    public static Step TextKey(LayoutProfile profile, PhysicalKey key, KeyLevel level, string suite, bool latin1Identity)
    {
        string id = level == KeyLevel.Plain ? key.ToString() : $"{level}+{key}";
        string prompt = level switch
        {
            KeyLevel.Shift => $"Hold Shift, tap {Cap(key)}, then release Shift",
            KeyLevel.AltGr => $"Hold AltGr (right Alt), tap {Cap(key)}, then release AltGr",
            _ => $"Tap {Cap(key)}",
        };

        if (profile.IsDead(key, level))
        {
            // A dead key produces nothing by itself and stays pending in the layout; left alone it
            // would merge into the next step's key. Space resolves it into its spacing accent, which
            // is the one text commit of the step. That text belongs to no key: not to the dead key,
            // and not to Space.
            string accent = (level == KeyLevel.Plain ? profile.Keys[key].Plain : profile.Keys[key].Shift)!;
            return new Step(id, suite, $"{prompt}, then tap Space", $"dead, then {KeyNames.Quote(accent)}",
                new Expectation(
                [
                    ExpectedItem.Press(TextIdentity(accent, latin1Identity)) with { ForbiddenState = AnyModifier },
                    ExpectedItem.Release(),
                ])
                {
                    TextTotal = accent, ForbiddenIdentities = [' '], AllowModifierEvents = level != KeyLevel.Plain,
                })
            {
                Key = key, Level = level,
                Strokes = [.. Tap(key, null, Held(level)), new KeyStroke(PhysicalKey.Space, true, accent), new KeyStroke(PhysicalKey.Space, false)],
            };
        }

        string text = profile.Text(key, level)
            ?? throw new ArgumentException($"{profile.Id} has no {level} text for {key}.");
        var items = new List<ExpectedItem>();
        ExpectedItem press = ExpectedItem.Press(TextIdentity(text, latin1Identity));
        switch (level)
        {
            case KeyLevel.Shift:
                items.Add(ExpectedItem.Modifier(Shift, 0));
                items.Add(press with { RequiredState = Shift, ForbiddenState = Ctrl | Alt });
                items.Add(ExpectedItem.Release());
                items.Add(ExpectedItem.Modifier(0, Shift));
                break;
            case KeyLevel.AltGr:
                // AltGr is Right Alt, on Windows with a synthetic Left Ctrl: no fixed transition pattern.
                items.Add(press);
                items.Add(ExpectedItem.Release());
                break;
            default:
                items.Add(press with { ForbiddenState = AnyModifier });
                items.Add(ExpectedItem.Release());
                break;
        }

        return new Step(id, suite, prompt, KeyNames.Quote(text), new Expectation(items)
        {
            TextTotal = text,
            ForbiddenIdentities = FalseIdentities(key, level, text),
            AllowRepeat = true,
            AllowModifierEvents = level == KeyLevel.AltGr,
        }) { Key = key, Level = level, Strokes = Tap(key, text, Held(level)) };
    }

    /// <summary>ASCII text is its own identity; other text has none, never a fabricated one.</summary>
    public static IdentityRule TextIdentity(string text, bool latin1Identity)
    {
        if (text.Length == 1 && text[0] <= 0x7E) return IdentityRule.Exact(text[0]);
        return latin1Identity && text.Length == 1 && text[0] <= 0xFF
            ? IdentityRule.NoneOrOneOf(text[0])
            : IdentityRule.None;
    }

    // Identities a broken translation is known to invent: the code point itself (U+011B is kbEsc),
    // its low byte as a character or control code (U+010D gives Enter), and the shortcut the key
    // cap would be with Ctrl or Alt.
    private static ushort[] FalseIdentities(PhysicalKey key, KeyLevel level, string text)
    {
        var codes = new HashSet<ushort>();
        foreach (char c in text)
        {
            if (c <= 0xFF) continue;
            codes.Add(c);
            codes.Add((ushort)(c & 0xFF));
            if ((c & 0xFF) == 0x0D) codes.Add(Keys.kbEnter);
            if ((c & 0xFF) == 0x1B) codes.Add(Keys.kbEsc);
            char low = (char)(c & 0xFF);
            if (char.IsAsciiLetter(low)) codes.Add((ushort)(low ^ 0x20));
        }
        if (level == KeyLevel.AltGr)
        {
            string? cap = UsProfile.Instance.Text(key, KeyLevel.Plain)?.ToUpperInvariant();
            foreach (string name in new[] { $"kbAlt{cap}", $"kbCtrl{cap}" })
                if (typeof(Keys).GetField(name)?.GetRawConstantValue() is ushort code) codes.Add(code);
        }
        codes.ExceptWith(text.Where(c => c <= 0x7E).Select(c => (ushort)c));
        codes.Remove(0);
        return codes.ToArray();
    }

    private static Step NamedKey(PhysicalKey key, ushort code, string suite) =>
        new(key.ToString(), suite, $"Tap {key}", KeyNames.Describe(code), new Expectation(
        [
            ExpectedItem.Press(IdentityRule.Exact(code)) with { ForbiddenState = AnyModifier },
            ExpectedItem.Release(),
        ])
        { TextTotal = string.Empty, AllowRepeat = true }) { Key = key, Strokes = Tap(key) };

    private static PhysicalKey[] Held(KeyLevel level) => level switch
    {
        KeyLevel.Shift => [PhysicalKey.ShiftLeft],
        KeyLevel.AltGr => [PhysicalKey.AltRight],
        _ => [],
    };

    // What a keypad key types with Num Lock on. The decimal key follows the locale and is not pinned.
    private static string? KeypadText(PhysicalKey key) => key switch
    {
        >= PhysicalKey.Numpad0 and <= PhysicalKey.Numpad9 => ((char)('0' + (key - PhysicalKey.Numpad0))).ToString(),
        PhysicalKey.NumpadDivide => "/",
        PhysicalKey.NumpadMultiply => "*",
        PhysicalKey.NumpadSubtract => "-",
        PhysicalKey.NumpadAdd => "+",
        _ => null,
    };

    private static Step Keypad(PhysicalKey key)
    {
        ushort code = KeypadKeys[key];
        const KeyboardCapabilities distinct = KeyboardCapabilities.DistinctNumericKeypad;
        return new Step(key.ToString(), "keypad", $"Tap {key} on the numeric keypad", KeyNames.Describe(code),
            new Expectation(
            [
                ExpectedItem.Press(IdentityRule.Exact(code)) with
                {
                    When = distinct, LimitedNote = "the keypad aliases main keys on this transport",
                },
                ExpectedItem.Press(IdentityRule.Any) with { Unless = distinct },
                // SDL commits the keypad key's text as a second press that is not the key itself.
                ExpectedItem.Press(IdentityRule.Any) with { When = distinct, Optional = true },
                ExpectedItem.Release(),
            ])
            { AllowRepeat = true }) { Key = key, Locks = LockState.Num, Strokes = Tap(key, KeypadText(key)) };
    }

    private static Step BareModifier(string name, uint mask) =>
        new(name, "modifiers", $"Tap {name} alone", $"{name} down, {name} up", new Expectation(
        [
            ExpectedItem.Modifier(mask, AnyModifier & ~mask),
            ExpectedItem.Modifier(0, AnyModifier),
        ])
        {
            TextTotal = string.Empty, Requires = KeyboardCapabilities.StandaloneModifierTransitions,
        })
        { Strokes = Tap(ModifierKey(name)) };

    private static PhysicalKey ModifierKey(string name) => name switch
    {
        "Shift" => PhysicalKey.ShiftLeft,
        "Ctrl" => PhysicalKey.ControlLeft,
        _ => PhysicalKey.AltLeft,
    };

    private static Step Chord(string name, uint mask, PhysicalKey key, ushort code) =>
        new($"{name}+{key}", "modifiers", $"Hold {name}, tap {Cap(key)}, then release {name}",
            KeyNames.Describe(code), new Expectation(
            [
                ExpectedItem.Modifier(mask, 0),
                ExpectedItem.Press(IdentityRule.Exact(code)) with { RequiredState = mask },
                ExpectedItem.Release(),
                ExpectedItem.Modifier(0, mask),
            ])
            { TextTotal = string.Empty, AllowRepeat = true }) { Key = key, Strokes = Tap(key, null, ModifierKey(name)) };

    private static bool IsNonAscii(string? text) => text != null && text.Any(c => c > 0x7E);

    // The position name plus the US key cap, which is what is printed on most keyboards.
    private static string Cap(PhysicalKey key)
    {
        string? cap = UsProfile.Instance.Text(key, KeyLevel.Plain);
        return cap is null or " " ? key.ToString() : $"{key} [US key cap {cap.ToUpperInvariant()}]";
    }

    // ── Terminal PTY (Level B) ────────────────────────────────────────────────

    /// <summary>
    /// Transport steps for the Terminal driver behind a PTY, as bytes a terminal confirming
    /// <paramref name="kittyFlags"/> would send. They say nothing about physical keys or layouts.
    /// </summary>
    public static IReadOnlyList<Step> Pty(int kittyFlags, string suite)
    {
        bool events = (kittyFlags & 2) != 0, allKeys = (kittyFlags & 8) != 0, text16 = (kittyFlags & 16) != 0;
        var steps = new List<Step>();
        void Add(string id, string category, string expected, Expectation expect, params string[] chunks) =>
            steps.Add(new Step(id, category, "bytes: " + Visible(string.Concat(chunks)), expected, expect)
            {
                Chunks = chunks.Select(Encoding.Latin1.GetBytes).ToArray(),
            });
        void Text(string id, string category, string text, params string[] chunks) =>
            Add(id, category, KeyNames.Quote(text), TextOnly(text), chunks);
        void Key(string id, string category, ushort code, params string[] chunks) =>
            Add(id, category, KeyNames.Describe(code), new Expectation(
                [ExpectedItem.Press(IdentityRule.Exact(code)), ExpectedItem.Release()]) { TextTotal = string.Empty },
                chunks);
        // Without associated text a Kitty key report carries the key, not what it typed: the ASCII
        // identity where there is one, no identity otherwise, and never text.
        const string noText = "the terminal did not report associated text";
        Expectation Typed(string text, bool repeat = false) => text16
            ? TextOnly(text) with { AllowRepeat = repeat }
            : TextOnly(text) with { TextTotal = string.Empty, AllowRepeat = repeat, Limitation = noText };
        static string Utf8(string text) => Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(text));
        // A Kitty text key: key code, optional associated text, and its release when event types are on.
        string[] Kitty(int key, string text, string modifiers = "1")
        {
            string codepoints = string.Join(':', text.EnumerateRunes().Select(r => r.Value));
            string press = text16 && text.Length != 0 ? $"\x1b[{key};{(modifiers == "1" ? "" : modifiers)};{codepoints}u"
                : modifiers == "1" ? $"\x1b[{key}u" : $"\x1b[{key};{modifiers}u";
            return events ? [press, $"\x1b[{key};{modifiers}:3u"] : [press];
        }

        if (!allKeys)
        {
            // Legacy encoding: text as UTF-8, functional keys as escape sequences, no releases.
            Text("ascii-a", "main", "a", "a");
            Text("ascii-2", "main", "2", "2");
            foreach (string c in new[] { "ě", "š", "č", "ř", "á", "€", "😀" })
                Text($"utf8-{Rune.GetRuneAt(c, 0).Value:X4}", "unicode", c, Utf8(c));
            Text("utf8-split-011B", "unicode", "ě", "\xC4", "\x9B");
            Text("utf8-split-20AC", "unicode", "€", "\xE2", "\x82\xAC");
            Key("ArrowUp", "navigation", Keys.kbUp, "\x1b[A");
            Key("ArrowDown", "navigation", Keys.kbDown, "\x1b[B");
            Key("ArrowRight", "navigation", Keys.kbRight, "\x1b[C");
            Key("ArrowLeft", "navigation", Keys.kbLeft, "\x1b[D");
            Key("Home", "navigation", Keys.kbHome, "\x1b[H");
            Key("End", "navigation", Keys.kbEnd, "\x1b[F");
            Key("Insert", "navigation", Keys.kbIns, "\x1b[2~");
            Key("Delete", "navigation", Keys.kbDel, "\x1b[3~");
            Key("PageUp", "navigation", Keys.kbPgUp, "\x1b[5~");
            Key("PageDown", "navigation", Keys.kbPgDn, "\x1b[6~");
            Key("Enter", "navigation", Keys.kbEnter, "\r");
            Key("Tab", "navigation", Keys.kbTab, "\t");
            Key("Backspace", "navigation", Keys.kbBack, "\x7f");
            Key("escape-split-ArrowUp", "navigation", Keys.kbUp, "\x1b[", "A");
            Key("F1", "function", Keys.kbF1, "\x1bOP");
            Key("F5", "function", Keys.kbF5, "\x1b[15~");
            Key("F10", "function", Keys.kbF10, "\x1b[21~");
            Key("F12", "function", Keys.kbF12, "\x1b[24~");
            Key("Ctrl+a", "modifiers", Keys.kbCtrlA, "\x01");
            Key("Alt+x", "modifiers", Keys.kbAltX, "\x1bx");
            Key("Shift+Tab", "modifiers", Keys.kbShiftTab, "\x1b[Z");
            // Disambiguation alone still sends ambiguous keys as CSI u.
            if ((kittyFlags & 1) != 0) Key("kitty-Escape", "navigation", Keys.kbEsc, "\x1b[27u");
        }
        else
        {
            Add("kitty-a", "main", text16 ? "\"a\"" : "'a', no text", Typed("a"), Kitty('a', "a"));
            foreach (string c in new[] { "ě", "š", "č", "ř", "á", "€" })
                Add($"kitty-{Rune.GetRuneAt(c, 0).Value:X4}", "unicode", text16 ? KeyNames.Quote(c) : "key, no text",
                    Typed(c), Kitty(Rune.GetRuneAt(c, 0).Value, c));
            if ((kittyFlags & 4) != 0)
            {
                // Czech Shift+Digit2: key "ě", shifted key "2".
                string press = text16 ? "\x1b[283:50;2;50u" : "\x1b[283:50;2u";
                Add("kitty-Shift+011B", "modifiers", text16 ? "\"2\"" : "'2', no text", new Expectation(
                    [
                        ExpectedItem.Press(IdentityRule.Exact('2')) with { RequiredState = Shift },
                        ExpectedItem.Release(),
                    ])
                    { TextTotal = text16 ? "2" : string.Empty, Limitation = text16 ? null : noText },
                    events ? [press, "\x1b[283:50;2:3u"] : [press]);
            }
            Key("kitty-Enter", "navigation", Keys.kbEnter, events ? ["\x1b[13u", "\x1b[13;1:3u"] : ["\x1b[13u"]);
            Key("kitty-Escape", "navigation", Keys.kbEsc, events ? ["\x1b[27u", "\x1b[27;1:3u"] : ["\x1b[27u"]);
            Key("kitty-ArrowUp", "navigation", Keys.kbUp, events ? ["\x1b[1;1:1A", "\x1b[1;1:3A"] : ["\x1b[A"]);
            Key("kitty-F5", "function", Keys.kbF5, events ? ["\x1b[15;1:1~", "\x1b[15;1:3~"] : ["\x1b[15~"]);
            Add("kitty-Numpad1", "keypad", KeyNames.Describe(Keys.kbKeypad1), new Expectation(
                [ExpectedItem.Press(IdentityRule.Exact(Keys.kbKeypad1)), ExpectedItem.Release()])
                { TextTotal = text16 ? "1" : string.Empty },
                Kitty(57400, "1"));
            Key("kitty-Ctrl+a", "modifiers", Keys.kbCtrlA, events ? ["\x1b[97;5u", "\x1b[97;5:3u"] : ["\x1b[97;5u"]);
            if (events)
            {
                Add("kitty-Shift", "modifiers", "Shift down, Shift up", new Expectation(
                    [ExpectedItem.Modifier(Shift, Ctrl | Alt), ExpectedItem.Modifier(0, AnyModifier)])
                    { TextTotal = string.Empty },
                    "\x1b[57441;2u", "\x1b[57441;1:3u");
                Add("kitty-repeat-a", "release", "'a', held", Typed("a", repeat: true),
                    text16 ? "\x1b[97;;97u" : "\x1b[97u", text16 ? "\x1b[97;1:2;97u" : "\x1b[97;1:2u",
                    "\x1b[97;1:3u");
            }
        }

        return suite == Full ? steps : steps.Where(s => s.Suite == suite).ToArray();
    }

    private static Expectation TextOnly(string text) => new(
        [ExpectedItem.Press(TextIdentity(text, latin1Identity: false)), ExpectedItem.Release()])
    {
        TextTotal = text,
        ForbiddenIdentities = FalseIdentities(PhysicalKey.Space, KeyLevel.Plain, text),
    };

    private static string Visible(string bytes) => string.Concat(bytes.Select(c =>
        c == '\x1b' ? "ESC" : c is >= ' ' and <= '~' ? c.ToString() : $"<{(int)c:X2}>"));
}
