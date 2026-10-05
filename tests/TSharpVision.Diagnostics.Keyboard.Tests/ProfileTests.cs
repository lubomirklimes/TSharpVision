using TSharpVision.Constants;
using TSharpVision.Diagnostics.Keyboard.Controller;
using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Drivers;

namespace TSharpVision.Diagnostics.Keyboard.Tests;

public sealed class ProfileTests
{
    private static readonly LayoutProfile Us = UsProfile.Instance, Cz = CzechQwertyProfile.Instance;

    public static IEnumerable<object[]> ProfilesAndPolicies() =>
        from profile in LayoutProfile.All
        from latin1 in new[] { false, true }
        select new object[] { profile.Id, latin1 };

    [Fact]
    public void EveryPhysicalKeyHasAUniqueScanCode()
    {
        var seen = new HashSet<(byte, bool)>();
        foreach (PhysicalKey key in PhysicalKeys.All)
        {
            (byte scan, bool extended) = PhysicalKeys.ScanCode(key);
            Assert.NotEqual(0, scan);
            Assert.True(seen.Add((scan, extended)), $"{key} shares scan 0x{scan:X2}");
        }
    }

    [Theory]
    [InlineData(PhysicalKey.Backquote, 0x29)]
    [InlineData(PhysicalKey.Digit2, 0x03)]
    [InlineData(PhysicalKey.Digit0, 0x0B)]
    [InlineData(PhysicalKey.Equal, 0x0D)]
    [InlineData(PhysicalKey.KeyE, 0x12)]
    [InlineData(PhysicalKey.KeyY, 0x15)]
    [InlineData(PhysicalKey.KeyZ, 0x2C)]
    [InlineData(PhysicalKey.Semicolon, 0x27)]
    [InlineData(PhysicalKey.Backslash, 0x2B)]
    [InlineData(PhysicalKey.IntlBackslash, 0x56)]
    [InlineData(PhysicalKey.F10, 0x44)]
    [InlineData(PhysicalKey.F11, 0x57)]
    [InlineData(PhysicalKey.Numpad1, 0x4F)]
    [InlineData(PhysicalKey.NumpadDecimal, 0x53)]
    public void ScanCodesAreThePcSet1MakeCodes(PhysicalKey key, byte scan) =>
        Assert.Equal(scan, PhysicalKeys.ScanCode(key).Scan);

    [Fact]
    public void ExtendedKeysAreTheNavigationClusterAndTheRightHandDuplicates()
    {
        Assert.True(PhysicalKeys.ScanCode(PhysicalKey.NumpadEnter).Extended);
        Assert.True(PhysicalKeys.ScanCode(PhysicalKey.ArrowUp).Extended);
        Assert.True(PhysicalKeys.ScanCode(PhysicalKey.AltRight).Extended);
        Assert.False(PhysicalKeys.ScanCode(PhysicalKey.Enter).Extended);
        Assert.False(PhysicalKeys.ScanCode(PhysicalKey.Numpad8).Extended);
    }

    [Fact]
    public void ProfilesAreFoundByTheirCliName()
    {
        Assert.Same(Us, LayoutProfile.Find("us"));
        Assert.Same(Cz, LayoutProfile.Find("CZ-QWERTY"));
        Assert.Null(LayoutProfile.Find("cz"));
        Assert.Equal("00000409", Us.LayoutId);
        Assert.Equal("00010405", Cz.LayoutId);
    }

    [Fact]
    public void UsProfileIsAsciiOnEveryLevelWithNoAltGrAndNoDeadKeys()
    {
        foreach ((PhysicalKey key, KeyLevels levels) in Us.Keys)
        {
            Assert.All(levels.Plain! + levels.Shift, c => Assert.InRange(c, ' ', '~'));
            Assert.Null(levels.AltGr);
            Assert.False(Us.IsDead(key, KeyLevel.Plain) || Us.IsDead(key, KeyLevel.Shift));
        }
        Assert.Equal("2", Us.Text(PhysicalKey.Digit2, KeyLevel.Plain));
        Assert.Equal("@", Us.Text(PhysicalKey.Digit2, KeyLevel.Shift));
        Assert.Equal("`", Us.Text(PhysicalKey.Backquote, KeyLevel.Plain));
        Assert.Equal("\\", Us.Text(PhysicalKey.IntlBackslash, KeyLevel.Plain));
    }

    [Fact]
    public void CzechNumberRowIsTheVerifiedOne()
    {
        PhysicalKey[] row =
        [
            PhysicalKey.Backquote, PhysicalKey.Digit1, PhysicalKey.Digit2, PhysicalKey.Digit3, PhysicalKey.Digit4,
            PhysicalKey.Digit5, PhysicalKey.Digit6, PhysicalKey.Digit7, PhysicalKey.Digit8, PhysicalKey.Digit9,
            PhysicalKey.Digit0, PhysicalKey.Minus,
        ];
        Assert.Equal(";+ěščřžýáíé=", string.Concat(row.Select(k => Cz.Text(k, KeyLevel.Plain))));
        Assert.Equal("1234567890", string.Concat(row.Skip(1).Take(10).Select(k => Cz.Text(k, KeyLevel.Shift))));
        Assert.Equal("!@#$%^&*()", string.Concat(row.Skip(1).Take(10).Select(k => Cz.Text(k, KeyLevel.AltGr))));
    }

    [Fact]
    public void CzechDeadKeysProduceNoImmediateText()
    {
        Assert.True(Cz.IsDead(PhysicalKey.Equal, KeyLevel.Plain));
        Assert.True(Cz.IsDead(PhysicalKey.Equal, KeyLevel.Shift));
        Assert.True(Cz.IsDead(PhysicalKey.Backslash, KeyLevel.Plain));
        Assert.True(Cz.IsDead(PhysicalKey.Backquote, KeyLevel.Shift));
        Assert.Null(Cz.Text(PhysicalKey.Equal, KeyLevel.Plain));
        Assert.Equal("=", Cz.Text(PhysicalKey.Equal, KeyLevel.AltGr));

        // Pressed alone a dead key stays pending, so its step resolves it with Space: the spacing
        // accent is the step's one commit, and it belongs neither to the dead key nor to Space.
        Step step = Suites.TextKey(Cz, PhysicalKey.Equal, KeyLevel.Plain, "unicode", latin1Identity: false);
        Assert.Equal("´", step.Expect.TextTotal);
        Assert.False(step.Expect.SilentAllowed);
        Assert.Equal(IdentityKind.None, step.Expect.Items[0].Identity.Kind);
        Assert.Contains((ushort)' ', step.Expect.ForbiddenIdentities);
        Assert.Equal(
            [PhysicalKey.Equal, PhysicalKey.Equal, PhysicalKey.Space, PhysicalKey.Space], step.Strokes.Select(s => s.Key));
        Assert.Null(step.Strokes[0].Text);
        Assert.Equal("´", step.Strokes[2].Text);
    }

    // Observed on the live layout with SendInput on Console, SDL Renderer and SDL GPU.
    [Fact]
    public void CzechDeadAcuteComposesWithE()
    {
        DeadKeyComposition composition = Assert.Single(Cz.Compositions);
        Assert.Equal(new DeadKeyComposition(PhysicalKey.Equal, PhysicalKey.KeyE, "é"), composition);
        Assert.Empty(UsProfile.Instance.Compositions);

        Step step = Suites.Build(Cz, "unicode", latin1Identity: false).Single(s => s.Id == "Equal,KeyE");
        Assert.Equal("é", step.Expect.TextTotal);
        Assert.Equal(IdentityKind.None, step.Expect.Items[0].Identity.Kind);
        // The composed text must not take the identity of the letter that completed it.
        Assert.Contains((ushort)'e', step.Expect.ForbiddenIdentities);
        Assert.Equal("é", step.Strokes.Single(s => s.Text != null).Text);
    }

    [Fact]
    public void CzechQwertyDiffersFromQwertzAndCarriesItsAltGrLevel()
    {
        Assert.Equal("y", Cz.Text(PhysicalKey.KeyY, KeyLevel.Plain));
        Assert.Equal("z", Cz.Text(PhysicalKey.KeyZ, KeyLevel.Plain));
        Assert.Equal("€", Cz.Text(PhysicalKey.KeyE, KeyLevel.AltGr));
        Assert.Equal("ů", Cz.Text(PhysicalKey.Semicolon, KeyLevel.Plain));
        Assert.Equal("ú", Cz.Text(PhysicalKey.BracketLeft, KeyLevel.Plain));
        Assert.Equal("§", Cz.Text(PhysicalKey.Quote, KeyLevel.Plain));
        Assert.Equal("-", Cz.Text(PhysicalKey.Slash, KeyLevel.Plain));
        Assert.Equal("ß", Cz.Text(PhysicalKey.IntlBackslash, KeyLevel.AltGr));
    }

    [Fact]
    public void FingerprintsQuoteTheirOwnProfileAndTellTheLayoutsApart()
    {
        foreach (LayoutProfile profile in LayoutProfile.All)
            Assert.All(profile.Fingerprint, f => Assert.Equal(profile.Text(f.Key, KeyLevel.Plain), f.Text));

        Assert.Equal(new[] { (PhysicalKey.Digit2, "ě"), (PhysicalKey.KeyY, "y"), (PhysicalKey.Semicolon, "ů") }, Cz.Fingerprint);
        Assert.Contains(Cz.Fingerprint, f => Us.Text(f.Key, KeyLevel.Plain) != f.Text);
    }

    [Theory, MemberData(nameof(ProfilesAndPolicies))]
    public void EverySuiteBuildsStepsWithUniqueIdsPromptsAndKnownKeys(string profileId, bool latin1)
    {
        LayoutProfile profile = LayoutProfile.Find(profileId)!;
        foreach (string suite in Suites.Names)
        {
            IReadOnlyList<Step> steps = Suites.Build(profile, suite, latin1);
            Assert.NotEmpty(steps);
            Assert.Equal(steps.Count, steps.Select(s => s.Id).Distinct().Count());
            Assert.All(steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Prompt)));
            Assert.All(steps, s => Assert.Empty(s.Chunks));
        }
        IReadOnlyList<Step> full = Suites.Build(profile, Suites.Full, latin1);
        Assert.All(Suites.Names.Where(n => n != Suites.Full).SelectMany(n => Suites.Build(profile, n, latin1)),
            s => Assert.Contains(full, f => f.Id == s.Id));
        Assert.Throws<ArgumentException>(() => Suites.Build(profile, "everything", latin1));
    }

    // The rule the whole tool rests on: an exact identity is only ever ASCII or a named key, so
    // no profile can demand that Unicode text be encoded into keyCode.
    [Theory, MemberData(nameof(ProfilesAndPolicies))]
    public void NoStepExpectsANonAsciiIdentity(string profileId, bool latin1)
    {
        var named = Suites.NamedKeys.Values.Concat(Suites.KeypadKeys.Values)
            .Concat([Keys.kbCtrlA, Keys.kbAltX, Keys.kbShiftTab]).ToHashSet();
        foreach (Step step in Suites.Build(LayoutProfile.Find(profileId)!, Suites.Full, latin1))
            foreach (ExpectedItem item in step.Expect.Items.Where(i => i.Identity.Kind == IdentityKind.Exact))
            {
                ushort code = item.Identity.Codes![0];
                Assert.True(code is >= 0x20 and <= 0x7E || named.Contains(code), $"{step.Id}: 0x{code:X4}");
            }
    }

    [Fact]
    public void TextIdentityFollowsTheTextAndOnlyTheConsoleMayUseLatin1()
    {
        Assert.Equal(IdentityRule.Exact('2'), Suites.TextIdentity("2", false), IdentityComparer.Instance);
        Assert.Equal(IdentityKind.None, Suites.TextIdentity("ě", true).Kind);
        Assert.Equal(IdentityKind.None, Suites.TextIdentity("á", false).Kind);
        Assert.Equal(IdentityKind.None, Suites.TextIdentity("😀", true).Kind);
        IdentityRule latin1 = Suites.TextIdentity("á", true);
        Assert.Equal(IdentityKind.NoneOrOneOf, latin1.Kind);
        Assert.Equal(new ushort[] { 0x00E1 }, latin1.Codes);
    }

    [Theory]
    [InlineData(PhysicalKey.Digit2, "ě", new ushort[] { Keys.kbEsc })]
    [InlineData(PhysicalKey.Digit3, "š", new ushort[] { 'a', 'A' })]
    [InlineData(PhysicalKey.Digit4, "č", new ushort[] { Keys.kbEnter, Keys.kbCtrlM })]
    [InlineData(PhysicalKey.Digit5, "ř", new ushort[] { 'Y', 'y' })]
    public void CzechRegressionKeysNameTheIdentityThatWasOnceFabricated(PhysicalKey key, string text, ushort[] forbidden)
    {
        Step step = Suites.TextKey(Cz, key, KeyLevel.Plain, "unicode", latin1Identity: true);

        Assert.Equal(text, step.Expect.TextTotal);
        Assert.Equal(IdentityKind.None, step.Expect.Items[0].Identity.Kind);
        Assert.All(forbidden, code => Assert.Contains(code, step.Expect.ForbiddenIdentities));
    }

    [Fact]
    public void CzechModifierCasesAreDescribedByTextIdentityAndState()
    {
        Step shifted = Suites.TextKey(Cz, PhysicalKey.Digit2, KeyLevel.Shift, "modifiers", false);
        ExpectedItem press = Assert.Single(shifted.Expect.Items, i => i.Kind == ItemKind.Press);
        Assert.Equal("2", shifted.Expect.TextTotal);
        Assert.Equal(new ushort[] { '2' }, press.Identity.Codes);
        Assert.Equal(Keys.kbShift, press.RequiredState);

        Step at = Suites.TextKey(Cz, PhysicalKey.Digit2, KeyLevel.AltGr, "modifiers", false);
        Assert.Equal("@", at.Expect.TextTotal);
        Assert.Contains(Keys.kbAlt2, at.Expect.ForbiddenIdentities);
        Assert.True(at.Expect.AllowModifierEvents);

        Step euro = Suites.TextKey(Cz, PhysicalKey.KeyE, KeyLevel.AltGr, "modifiers", false);
        Assert.Equal("€", euro.Expect.TextTotal);
        Assert.Equal(IdentityKind.None, euro.Expect.Items[0].Identity.Kind);
        Assert.Contains(Keys.kbAltE, euro.Expect.ForbiddenIdentities);
        Assert.Contains(Keys.kbCtrlE, euro.Expect.ForbiddenIdentities);

        Step a8 = Suites.TextKey(Cz, PhysicalKey.Digit8, KeyLevel.Plain, "unicode", latin1Identity: true);
        Assert.Equal(new ushort[] { 0x00E1 }, a8.Expect.Items[0].Identity.Codes);
    }

    [Fact]
    public void ModifierSuiteOffersAltGrOnlyWhereTheLayoutHasIt()
    {
        Assert.DoesNotContain(Suites.Build(Us, "modifiers", false), s => s.Level == KeyLevel.AltGr);
        Assert.Equal(["AltGr+Digit2", "AltGr+KeyE"],
            Suites.Build(Cz, "modifiers", false).Where(s => s.Level == KeyLevel.AltGr).Select(s => s.Id));
    }

    [Fact]
    public void ReleaseSuiteIsIrrelevantWithoutReleaseEvents()
    {
        Assert.All(Suites.Build(Cz, "release", false),
            s => Assert.Equal(KeyboardCapabilities.KeyReleaseEvents, s.Expect.Requires));
        Assert.All(Suites.Build(Cz, Suites.Full, false).Where(s => s.Suite is not ("modifiers" or "keypad" or "release")),
            s => Assert.Equal(KeyboardCapabilities.None, s.Expect.Requires));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(31)]
    public void PtyStepsAreBytesOnlyAndNeverClaimAPhysicalKey(int flags)
    {
        IReadOnlyList<Step> steps = Suites.Pty(flags, Suites.Full);

        Assert.NotEmpty(steps);
        Assert.Equal(steps.Count, steps.Select(s => s.Id).Distinct().Count());
        Assert.All(steps, s => { Assert.Null(s.Key); Assert.NotEmpty(s.Chunks); });
        Assert.Equal(steps.Where(s => s.Suite == "unicode").Select(s => s.Id), Suites.Pty(flags, "unicode").Select(s => s.Id));
        // Split UTF-8 exists only where a terminal sends text as UTF-8 at all.
        Assert.Equal((flags & 8) == 0, steps.Any(s => s.Id == "utf8-split-011B" && s.Chunks.Count == 2));
        Assert.Equal((flags & 8) != 0, steps.Any(s => s.Id == "kitty-011B"));
    }

    private sealed class IdentityComparer : IEqualityComparer<IdentityRule>
    {
        public static readonly IdentityComparer Instance = new();
        public bool Equals(IdentityRule x, IdentityRule y) =>
            x.Kind == y.Kind && (x.Codes ?? []).SequenceEqual(y.Codes ?? []);
        public int GetHashCode(IdentityRule rule) => (int)rule.Kind;
    }
}
