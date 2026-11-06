using TSharpVision.Drivers.SDL.Rendering.Fonts;
using static TSharpVision.Drivers.SDL.Tests.Unicode.UnicodeTestText;

namespace TSharpVision.Drivers.SDL.Tests.Unicode;

/// <summary>
/// TV13m: one primary font per cluster, chosen by TSharpVision (U0-A: SDL_ttf's own fallback splits ZWJ sequences,
/// drops a regional indicator and ignores VS15). Pure: coverage comes from a fake table, no SDL.
/// </summary>
public sealed class SdlClusterResolutionTests
{
    // Chain like the Windows default: base, emoji, cjk, symbol.
    private static readonly SdlFallbackFontRole[] Roles =
        { SdlFallbackFontRole.Base, SdlFallbackFontRole.Emoji, SdlFallbackFontRole.Cjk, SdlFallbackFontRole.Symbol };

    // Coverage modelled on the U0-A table: base has ASCII and U+0301; the emoji font has emoji, regional indicators,
    // modifiers and U+2764 but not ZWJ or VS; the CJK font has the ideographs and ASCII; the symbol font has U+2764.
    private static bool Has(int font, int scalar) => font switch
    {
        0 => scalar < 0x80 || scalar == 0x0301,
        1 => scalar is >= 0x1F000 and <= 0x1FAFF || scalar == 0x2764 || scalar is >= '0' and <= '9',
        2 => scalar < 0x80 || scalar is 0x6F22 or 0x5B57,
        3 => scalar == 0x2764 || scalar == 0x2605,
        _ => false,
    };

    private static (SdlFallbackFontRole? Role, SdlClusterCoverage Coverage) Resolve(string cluster, int cells)
    {
        int index = SdlClusterFontResolver.Resolve(cluster, cells, Roles, Has, out SdlClusterCoverage coverage);
        return (index < 0 ? null : Roles[index], coverage);
    }

    public static TheoryData<string, int, int> Presentations => new()
    {
        { Grinning, 2, (int)SdlClusterPresentation.Emoji },
        { ThumbsUpMedium, 2, (int)SdlClusterPresentation.Emoji },
        { FlagCz, 2, (int)SdlClusterPresentation.Emoji },
        { Family, 2, (int)SdlClusterPresentation.Emoji },
        { HeartEmoji, 2, (int)SdlClusterPresentation.Emoji },
        { HeartText, 1, (int)SdlClusterPresentation.Text },
        { S(0x2764), 1, (int)SdlClusterPresentation.Default },
        { Keycap1, 2, (int)SdlClusterPresentation.Emoji },
        { EAcute, 1, (int)SdlClusterPresentation.Default },
        { Kan, 2, (int)SdlClusterPresentation.Default },
        { S(0x1F600, 0xFE0E), 1, (int)SdlClusterPresentation.Text },
    };

    [Theory]
    [MemberData(nameof(Presentations))]
    public void Presentation_follows_selectors_and_emoji_components(string cluster, int cells, int expected) =>
        Assert.Equal((SdlClusterPresentation)expected, SdlClusterClassifier.Classify(cluster, cells));

    [Theory]
    [InlineData(0x200D, true)]
    [InlineData(0xFE0F, true)]
    [InlineData(0xFE0E, true)]
    [InlineData(0xE0067, true)]   // tag letter (subdivision flags)
    [InlineData(0x00AD, true)]
    [InlineData(0x0301, false)]   // a combining mark must be covered
    [InlineData(0x1F3FD, false)]  // a skin tone must be covered
    [InlineData('A', false)]
    public void Coverage_ignores_only_default_ignorables(int scalar, bool ignorable) =>
        Assert.Equal(ignorable, SdlClusterClassifier.IsIgnorableForCoverage(scalar));

    [Fact]
    public void Combining_sequence_stays_in_the_base_font() =>
        Assert.Equal((SdlFallbackFontRole.Base, SdlClusterCoverage.Full), Resolve(EAcute, 1));

    [Fact]
    public void Cjk_resolves_to_the_cjk_font() =>
        Assert.Equal((SdlFallbackFontRole.Cjk, SdlClusterCoverage.Full), Resolve(Kan, 2));

    [Fact]
    public void Zwj_family_resolves_to_the_emoji_font_although_it_lacks_the_joiner() =>
        Assert.Equal((SdlFallbackFontRole.Emoji, SdlClusterCoverage.Full), Resolve(Family, 2));

    [Fact]
    public void Skin_tone_and_flag_resolve_to_the_emoji_font_as_one_cluster()
    {
        Assert.Equal((SdlFallbackFontRole.Emoji, SdlClusterCoverage.Full), Resolve(ThumbsUpMedium, 2));
        Assert.Equal((SdlFallbackFontRole.Emoji, SdlClusterCoverage.Full), Resolve(FlagCz, 2));
    }

    [Fact]
    public void Vs15_moves_the_emoji_font_last_and_vs16_first()
    {
        Assert.Equal(SdlFallbackFontRole.Symbol, Resolve(HeartText, 1).Role);
        Assert.Equal(SdlFallbackFontRole.Emoji, Resolve(HeartEmoji, 2).Role);
        // No selector: chain order (base lacks it, the emoji font is next), as in the U0-A U3 simulation.
        Assert.Equal(SdlFallbackFontRole.Emoji, Resolve(S(0x2764), 1).Role);
    }

    [Fact]
    public void Keycap_prefers_the_emoji_font_even_though_the_base_font_has_the_digit() =>
        Assert.Equal(SdlFallbackFontRole.Emoji, Resolve(Keycap1, 2).Role);

    [Fact]
    public void Uncovered_mark_is_partial_in_the_font_of_the_base_character() =>
        Assert.Equal((SdlFallbackFontRole.Base, SdlClusterCoverage.Partial), Resolve(S('e', 0x20DD), 1));

    [Fact]
    public void Cluster_no_font_has_is_missing() =>
        Assert.Equal((null, SdlClusterCoverage.Missing), Resolve(S(0x10FFFD), 1));

    [Fact]
    public void Only_ignorables_resolve_to_the_first_font_without_a_box() =>
        Assert.Equal((SdlFallbackFontRole.Base, SdlClusterCoverage.Full), Resolve(S(0x200D), 1));

    [Fact]
    public void Excessive_combining_and_zwj_runs_resolve_without_limits()
    {
        string marks = "a" + new string((char)0x0301, 5000);
        Assert.Equal((SdlFallbackFontRole.Base, SdlClusterCoverage.Full), Resolve(marks, 1));

        var zwj = new System.Text.StringBuilder(Grinning);
        for (int i = 0; i < 500; i++) zwj.Append((char)0x200D).Append(Grinning);
        Assert.Equal((SdlFallbackFontRole.Emoji, SdlClusterCoverage.Full), Resolve(zwj.ToString(), 2));

        // Lone surrogates decode as U+FFFD, which no font in the table has.
        Assert.Equal(SdlClusterCoverage.Missing, Resolve("\uD800", 1).Coverage);
    }

    [Fact]
    public void Chain_override_parses_roles_in_order_and_drops_missing_files()
    {
        IReadOnlyList<SdlFallbackFont> parsed = SdlFallbackFontChain.Parse(" cjk = c.ttc ; emoji=\"e.ttf\";bogus=x;symbol=;symbol=s.ttf ");
        Assert.Equal(new[]
        {
            new SdlFallbackFont(SdlFallbackFontRole.Cjk, "c.ttc"),
            new SdlFallbackFont(SdlFallbackFontRole.Emoji, "e.ttf"),
            new SdlFallbackFont(SdlFallbackFontRole.Symbol, "s.ttf"),
        }, parsed);

        IReadOnlyList<SdlFallbackFont> chain = SdlFallbackFontChain.Resolve("emoji=e.ttf;cjk=c.ttc;cjk=d.ttc", Array.Empty<SdlFallbackFont>(),
            path => path != "c.ttc");
        Assert.Equal(new[] { "e.ttf", "d.ttc" }, chain.Select(f => f.Path));
        Assert.Empty(SdlFallbackFontChain.Resolve("none", SdlFallbackFontChain.DefaultCandidates(), _ => true));
    }

    [Fact]
    public void Default_chain_keeps_the_first_cjk_candidate_and_never_msgothic()
    {
        IReadOnlyList<SdlFallbackFont> defaults = SdlFallbackFontChain.DefaultCandidates();
        Assert.DoesNotContain(defaults, f => f.Path.Contains("msgothic", StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<SdlFallbackFont> chain = SdlFallbackFontChain.Resolve(null, defaults, _ => true);
        Assert.Single(chain, f => f.Role == SdlFallbackFontRole.Cjk);
        Assert.Equal(SdlFallbackFontRole.Emoji, chain[0].Role);
        if (OperatingSystem.IsWindows())
            Assert.Equal(new[] { "seguiemj.ttf", "YuGothM.ttc", "seguisym.ttf" }, chain.Select(f => Path.GetFileName(f.Path)));
    }
}
