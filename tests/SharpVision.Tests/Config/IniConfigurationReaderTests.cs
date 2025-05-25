using SharpVision.Config;
using Xunit;

namespace SharpVision.Tests.Config;

public sealed class IniConfigurationReaderTests
{
    // ── Missing / empty input ─────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyString_ReturnsNoValues()
    {
        var ini = IniConfigurationReader.Parse(string.Empty);
        Assert.Null(ini.Get("driver", "name"));
        Assert.Null(ini.Get("sdl", "fontName"));
    }

    [Fact]
    public void Get_MissingSection_ReturnsNull()
    {
        var ini = IniConfigurationReader.Parse("[other]\nkey=val");
        Assert.Null(ini.Get("driver", "name"));
    }

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        var ini = IniConfigurationReader.Parse("[driver]\nother=sdl");
        Assert.Null(ini.Get("driver", "name"));
    }

    // ── Basic reads ───────────────────────────────────────────────────────

    [Fact]
    public void Parse_ReadsDriverName()
    {
        var ini = IniConfigurationReader.Parse("[driver]\nname=sdl");
        Assert.Equal("sdl", ini.Get("driver", "name"));
    }

    [Fact]
    public void Parse_ReadsSdlFontName()
    {
        var ini = IniConfigurationReader.Parse("[sdl]\nfontName=Cascadia Mono");
        Assert.Equal("Cascadia Mono", ini.Get("sdl", "fontName"));
    }

    [Fact]
    public void Parse_ReadsBothSections()
    {
        const string cfg = """
            [driver]
            name=sdl

            [sdl]
            fontName=Cascadia Mono
            """;

        var ini = IniConfigurationReader.Parse(cfg);
        Assert.Equal("sdl",           ini.Get("driver", "name"));
        Assert.Equal("Cascadia Mono", ini.Get("sdl",    "fontName"));
    }

    // ── Case-insensitivity ────────────────────────────────────────────────

    [Fact]
    public void Parse_SectionNameIsCaseInsensitive()
    {
        var ini = IniConfigurationReader.Parse("[DRIVER]\nname=console");
        Assert.Equal("console", ini.Get("driver", "name"));
        Assert.Equal("console", ini.Get("Driver", "name"));
        Assert.Equal("console", ini.Get("DRIVER", "name"));
    }

    [Fact]
    public void Parse_KeyNameIsCaseInsensitive()
    {
        var ini = IniConfigurationReader.Parse("[driver]\nNAME=console");
        Assert.Equal("console", ini.Get("driver", "name"));
        Assert.Equal("console", ini.Get("driver", "Name"));
        Assert.Equal("console", ini.Get("driver", "NAME"));
    }

    // ── Comments and blank lines ──────────────────────────────────────────

    [Fact]
    public void Parse_SemicolonCommentIsIgnored()
    {
        var ini = IniConfigurationReader.Parse("; this is a comment\n[driver]\nname=sdl");
        Assert.Equal("sdl", ini.Get("driver", "name"));
    }

    [Fact]
    public void Parse_HashCommentIsIgnored()
    {
        var ini = IniConfigurationReader.Parse("# comment\n[driver]\nname=sdl");
        Assert.Equal("sdl", ini.Get("driver", "name"));
    }

    [Fact]
    public void Parse_BlankLinesAreIgnored()
    {
        var ini = IniConfigurationReader.Parse("\n\n[driver]\n\nname=sdl\n\n");
        Assert.Equal("sdl", ini.Get("driver", "name"));
    }

    // ── Unknown sections / keys are ignored ───────────────────────────────

    [Fact]
    public void Parse_UnknownSectionIsIgnored()
    {
        // Unknown sections are stored internally but callers simply don't query them.
        // Verify that the presence of an unknown section doesn't corrupt known sections.
        var ini = IniConfigurationReader.Parse("[unknown]\nfoo=bar\n[driver]\nname=sdl");
        Assert.Equal("sdl", ini.Get("driver", "name"));
    }

    [Fact]
    public void Parse_UnknownKeyDoesNotAffectKnownKeys()
    {
        var ini = IniConfigurationReader.Parse("[driver]\nname=sdl\nunknown=val");
        Assert.Equal("sdl", ini.Get("driver", "name"));
    }

    // ── Whitespace trimming ───────────────────────────────────────────────

    [Fact]
    public void Parse_TrimsWhitespaceAroundKeyAndValue()
    {
        var ini = IniConfigurationReader.Parse("[driver]\n  name  =  console  ");
        Assert.Equal("console", ini.Get("driver", "name"));
    }

    [Fact]
    public void Parse_TrimsWhitespaceInSectionName()
    {
        var ini = IniConfigurationReader.Parse("[  driver  ]\nname=sdl");
        Assert.Equal("sdl", ini.Get("driver", "name"));
    }

    // ── Value with equals sign ─────────────────────────────────────────────

    [Fact]
    public void Parse_ValueContainingEqualsSign()
    {
        var ini = IniConfigurationReader.Parse("[sdl]\nfontName=Font=Name");
        Assert.Equal("Font=Name", ini.Get("sdl", "fontName"));
    }
}
