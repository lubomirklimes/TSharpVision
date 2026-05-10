// TSharpVisionIntl localization provider seam tests.
// TSharpVisionIntl.Current is global state → NonParallel collection.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TSharpVision;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Localization;

// ---------------------------------------------------------------------------
// Test-only provider helpers (duplicated here — do NOT modify the demo source).
// ---------------------------------------------------------------------------

// Returns "prefix + key" for every key — exercises swap behaviour.
file sealed class IntlPrefixTestProvider(string prefix) : ITSharpVisionStringProvider
{
    public string Get(string key, string fallback) => prefix + key;
}

// Returns a fixed value for every key — exercises property forwarding.
file sealed class IntlFixedTestProvider(string value) : ITSharpVisionStringProvider
{
    public string Get(string key, string fallback) => value;
}

// Returns values from a supplied dictionary, falling back to the literal.
file sealed class IntlDictTestProvider(Dictionary<string, string> dict)
    : ITSharpVisionStringProvider
{
    public string Get(string key, string fallback)
        => dict.TryGetValue(key, out var v) ? v : fallback;
}

// Legacy provider: intentionally does not implement ITSharpVisionStringLookupProvider.
file sealed class IntlFallbackOnlyTestProvider : ITSharpVisionStringProvider
{
    public string Get(string key, string fallback) => fallback;
}

// ---------------------------------------------------------------------------

[Collection("NonParallel")]
public sealed class TSharpVisionIntlTests
{
    // ── 19a.1 — Default provider returns fallback ─────────────────────────

    [Fact]
    public void DefaultProvider_ReturnsFallback_BtnOK()
    {
        using var scope = new IntlProviderScope();
        Assert.Equal("~O~K", TSharpVisionIntl.Get("Btn_OK", "~O~K"));
    }

    [Fact]
    public void DefaultProvider_ReturnsFallback_HelpTitle()
    {
        using var scope = new IntlProviderScope();
        Assert.Equal("Help", TSharpVisionIntl.Get("Help_WindowTitle", "Help"));
    }

    [Fact]
    public void DefaultProvider_ReturnsFallback_Untitled()
    {
        using var scope = new IntlProviderScope();
        Assert.Equal("Untitled", TSharpVisionIntl.Get("Edit_Untitled", "Untitled"));
    }

    [Fact]
    public void DefaultProvider_MissingKey_ReturnsFallback()
    {
        using var scope = new IntlProviderScope();
        Assert.Equal("fallback", TSharpVisionIntl.Get("Missing_Key_XYZ", "fallback"));
    }

    // ── 19a.2 — Custom prefix provider swap ───────────────────────────────

    [Fact]
    public void PrefixProvider_Get_ReturnsPrefixPlusKey()
    {
        using var scope = new IntlProviderScope(new IntlPrefixTestProvider("XX:"));
        Assert.Equal("XX:Btn_OK", TSharpVisionIntl.Get("Btn_OK", "~O~K"));
    }

    [Fact]
    public void PrefixProvider_Underscore_ReturnsPrefixPlusKey()
    {
        using var scope = new IntlProviderScope(new IntlPrefixTestProvider("XX:"));
        Assert.Equal("XX:Btn_OK", TSharpVisionIntl._("~O~K", "Btn_OK"));
    }

    [Fact]
    public void PrefixProvider_RestoredAfterScope()
    {
        {
            using var scope = new IntlProviderScope(new IntlPrefixTestProvider("XX:"));
            Assert.Equal("XX:Btn_OK", TSharpVisionIntl.Get("Btn_OK", "~O~K"));
        }
        // After restore, fallback behaviour must be back.
        Assert.Equal("~O~K", TSharpVisionIntl.Get("Btn_OK", "~O~K"));
    }

    // ── 19a.3 — Fixed provider & TEditWindow property forwarding ─────────

    [Fact]
    public void FixedProvider_TEditWindow_Untitled()
    {
        using var scope = new IntlProviderScope(new IntlFixedTestProvider("NIC"));
        Assert.Equal("NIC", TEditWindow.untitled);
    }

    [Fact]
    public void FixedProvider_TEditWindow_ClipboardTitle()
    {
        using var scope = new IntlProviderScope(new IntlFixedTestProvider("NIC"));
        Assert.Equal("NIC", TEditWindow.clipboardTitle);
    }

    [Fact]
    public void FixedProvider_Restored_TEditWindow_Untitled()
    {
        {
            using var scope = new IntlProviderScope(new IntlFixedTestProvider("NIC"));
        }
        Assert.Equal("Untitled", TEditWindow.untitled);
    }

    // ── 19a.4 — Dict provider & THelpWindow property forwarding ──────────

    [Fact]
    public void DictProvider_THelpWindow_HelpTitle()
    {
        var dict = new Dictionary<string, string>
        {
            ["Help_WindowTitle"] = "Pomoc",
            ["Help_NoContext"]   = "..."
        };
        using var scope = new IntlProviderScope(new IntlDictTestProvider(dict));
        Assert.Equal("Pomoc", THelpWindow.helpWinTitle);
    }

    [Fact]
    public void DictProvider_Restored_THelpWindow_HelpTitle()
    {
        {
            var dict = new Dictionary<string, string> { ["Help_WindowTitle"] = "Pomoc" };
            using var scope = new IntlProviderScope(new IntlDictTestProvider(dict));
        }
        Assert.Equal("Help", THelpWindow.helpWinTitle);
    }

    // ── 19a.5 — Literal-key shorthand ────────────────────────────────────

    [Fact]
    public void Underscore_SingleArg_ReturnsLiteral()
    {
        using var scope = new IntlProviderScope();
        Assert.Equal("~O~K", TSharpVisionIntl._("~O~K"));
    }

    [Fact]
    public void Underscore_SingleArg_NotInDict_ReturnsLiteral()
    {
        using var scope = new IntlProviderScope();
        Assert.Equal("NotInDict", TSharpVisionIntl._("NotInDict"));
    }

    [Fact]
    public void MissingKey_DefaultProvider_Hit_DoesNotRaise()
    {
        using var scope = new IntlProviderScope();

        var events = CaptureMissingKeys(() =>
        {
            Assert.Equal("~O~K", TSharpVisionIntl.Get("Btn_OK", "fallback"));
        });

        Assert.Empty(events);
    }

    [Fact]
    public void MissingKey_DefaultProvider_Miss_ReturnsFallbackAndRaisesOnce()
    {
        using var scope = new IntlProviderScope();
        var provider = TSharpVisionIntl.Current;

        var events = CaptureMissingKeys(() =>
        {
            Assert.Equal("fallback", TSharpVisionIntl.Get("Missing_Key_XYZ", "fallback"));
        });

        var ev = Assert.Single(events);
        Assert.Equal("Missing_Key_XYZ", ev.Key);
        Assert.Equal("fallback", ev.Fallback);
        Assert.Same(provider, ev.Provider);
    }

    [Fact]
    public void MissingKey_ResourceProvider_Hit_DoesNotRaise()
    {
        using var tmp = new TempDirectory();
        var provider = WriteStringProvider(tmp.Path, new Dictionary<string, string>
        {
            ["Btn_OK"] = "~B~udiz",
        });
        using var scope = new IntlProviderScope(provider);

        var events = CaptureMissingKeys(() =>
        {
            Assert.Equal("~B~udiz", TSharpVisionIntl.Get("Btn_OK", "~O~K"));
        });

        Assert.Empty(events);
    }

    [Fact]
    public void MissingKey_ResourceProvider_Miss_ReturnsFallbackAndRaisesOnce()
    {
        using var tmp = new TempDirectory();
        var provider = WriteStringProvider(tmp.Path, new Dictionary<string, string>
        {
            ["Btn_OK"] = "~B~udiz",
        });
        using var scope = new IntlProviderScope(provider);

        var events = CaptureMissingKeys(() =>
        {
            Assert.Equal("fallback", TSharpVisionIntl.Get("Missing", "fallback"));
        });

        var ev = Assert.Single(events);
        Assert.Equal("Missing", ev.Key);
        Assert.Equal("fallback", ev.Fallback);
        Assert.Same(provider, ev.Provider);
    }

    [Fact]
    public void MissingKey_ProviderChain_HitInSecondProvider_DoesNotRaise()
    {
        var first = new TResourceStringProvider(new TStringResource());
        var second = new TResourceStringProvider(new Dictionary<string, string>
        {
            ["Menu_File"] = "~S~oubor",
        }.ToStringResource());
        var chain = new TSharpVisionStringProviderChain(first, second);
        using var scope = new IntlProviderScope(chain);

        var events = CaptureMissingKeys(() =>
        {
            Assert.Equal("~S~oubor", TSharpVisionIntl.Get("Menu_File", "~F~ile"));
        });

        Assert.Empty(events);
    }

    [Fact]
    public void MissingKey_ProviderChain_FullMiss_ReturnsFallbackAndRaisesOnce()
    {
        var chain = new TSharpVisionStringProviderChain(
            new TResourceStringProvider(new TStringResource()),
            new TResourceStringProvider(new TStringResource()));
        using var scope = new IntlProviderScope(chain);

        var events = CaptureMissingKeys(() =>
        {
            Assert.Equal("fallback", TSharpVisionIntl.Get("Missing", "fallback"));
        });

        var ev = Assert.Single(events);
        Assert.Equal("Missing", ev.Key);
        Assert.Equal("fallback", ev.Fallback);
        Assert.Same(chain, ev.Provider);
    }

    [Fact]
    public void MissingKey_TranslationEqualToFallback_IsHit()
    {
        var provider = new TResourceStringProvider(new Dictionary<string, string>
        {
            ["Same"] = "fallback",
        }.ToStringResource());
        using var scope = new IntlProviderScope(provider);

        var events = CaptureMissingKeys(() =>
        {
            Assert.Equal("fallback", TSharpVisionIntl.Get("Same", "fallback"));
        });

        Assert.Empty(events);
    }

    [Fact]
    public void MissingKey_LegacyProviderWithoutTryGet_PreservesBehaviorAndDoesNotRaise()
    {
        using var scope = new IntlProviderScope(new IntlFallbackOnlyTestProvider());

        var events = CaptureMissingKeys(() =>
        {
            Assert.Equal("fallback", TSharpVisionIntl.Get("Missing", "fallback"));
        });

        Assert.Empty(events);
    }

    [Fact]
    public void ResourceProvider_LoadsUtf16StringsFromTvr()
    {
        using var tmp = new TempDirectory();
        string path = Path.Combine(tmp.Path, "app_cs.tvr");

        StreamableRegistration.RegisterAll();
        var fpw = new Fpstream(path);
        var rfw = new TResourceFile(fpw);
        rfw.Put(new TStringResource(new Dictionary<string, string>
        {
            ["Btn_OK"] = "~B~udiž",
        }), "sharpvision.intl");
        rfw.Flush();
        fpw.Close();

        var provider = TResourceStringProvider.Load(path);
        Assert.Equal("~B~udiž", provider.Get("Btn_OK", "~O~K"));
        Assert.Equal("fallback", provider.Get("Missing", "fallback"));
    }

    [Fact]
    public void ResourceProvider_TryGet_DistinguishesHitAndMiss()
    {
        var provider = new TResourceStringProvider(new TStringResource(
            new Dictionary<string, string>
            {
                ["Menu_File"] = "~S~oubor",
            }));

        Assert.True(provider.TryGet("Menu_File", out string value));
        Assert.Equal("~S~oubor", value);

        Assert.False(provider.TryGet("Missing", out value));
        Assert.Null(value);
        Assert.Equal("fallback", provider.Get("Missing", "fallback"));
    }

    [Fact]
    public void ResourceProvider_ValueEqualToFallback_IsPreciseHit()
    {
        var provider = new TResourceStringProvider(new TStringResource(
            new Dictionary<string, string>
            {
                ["Same_As_Fallback"] = "Same",
            }));

        Assert.True(provider.TryGet("Same_As_Fallback", out string value));
        Assert.Equal("Same", value);
        Assert.Equal("Same", provider.Get("Same_As_Fallback", "Same"));

        using var scope = new IntlProviderScope(provider);
        var events = CaptureMissingKeys(() =>
        {
            Assert.Equal("Same", TSharpVisionIntl.Get("Same_As_Fallback", "Same"));
        });
        Assert.Empty(events);
    }

    [Fact]
    public void ProviderChain_ResourceMiss_DefaultHit_DoesNotRaiseMissingKey()
    {
        var resourceProvider = new TResourceStringProvider(new TStringResource());
        var chain = new TSharpVisionStringProviderChain(
            resourceProvider,
            new DefaultEnglishStringProvider());
        using var scope = new IntlProviderScope(chain);

        var events = CaptureMissingKeys(() =>
        {
            Assert.Equal("~F~ile", TSharpVisionIntl.Get("Menu_File", "fallback"));
        });

        Assert.Empty(events);
    }

    [Fact]
    public void ResourceProvider_TryLoad_MissingFile_ReturnsNull()
    {
        using var tmp = new TempDirectory();
        string path = Path.Combine(tmp.Path, "missing.tvr");

        Assert.Null(TResourceStringProvider.TryLoad(path));
    }

    [Fact]
    public void ResourceProvider_TryLoad_InvalidFile_ReturnsNull()
    {
        using var tmp = new TempDirectory();
        string path = Path.Combine(tmp.Path, "broken.tvr");
        File.WriteAllBytes(path, new byte[] { 0x01, 0x02, 0x03 });

        Assert.Null(TResourceStringProvider.TryLoad(path));
    }

    [Fact]
    public void ResourceProvider_Load_MissingStringTable_ReturnsEmptyProvider()
    {
        using var tmp = new TempDirectory();
        string path = Path.Combine(tmp.Path, "app_cs.tvr");

        StreamableRegistration.RegisterAll();
        var fpw = new Fpstream(path);
        var rfw = new TResourceFile(fpw);
        rfw.Put(new TStringResource(new Dictionary<string, string>
        {
            ["Other"] = "Value",
        }), "other.table");
        rfw.Flush();
        fpw.Close();

        var provider = TResourceStringProvider.Load(path);

        Assert.False(provider.TryGet("Other", out string value));
        Assert.Null(value);
        Assert.Equal("fallback", provider.Get("Other", "fallback"));
    }

    [Fact]
    public void LocalizedResolver_UsesLanguageEnglishAndBaseOrder()
    {
        var candidates = LocalizedResourceResolver.GetCandidatePaths(
            Path.Combine("res", "app.tvr"), ".tvr", "cs").ToArray();

        Assert.Equal(Path.Combine("res", "app_cs.tvr"), candidates[0]);
        Assert.Equal(Path.Combine("res", "app_en.tvr"), candidates[1]);
        Assert.Equal(Path.Combine("res", "app.tvr"), candidates[2]);
    }

    [Fact]
    public void LocalizedResolver_AppliesSameConventionToHelpFiles()
    {
        var candidates = LocalizedResourceResolver.GetCandidatePaths(
            "help", "hlp", "cs").ToArray();

        Assert.Equal("help_cs.hlp", candidates[0]);
        Assert.Equal("help_en.hlp", candidates[1]);
        Assert.Equal("help.hlp", candidates[2]);
    }

    [Fact]
    public void LocalizedResolver_Resolve_SelectsFirstExistingCandidate()
    {
        using var tmp = new TempDirectory();
        string app = Path.Combine(tmp.Path, "app.tvr");
        string appEn = Path.Combine(tmp.Path, "app_en.tvr");
        string appCs = Path.Combine(tmp.Path, "app_cs.tvr");

        File.WriteAllText(app, "base");
        File.WriteAllText(appEn, "english");
        File.WriteAllText(appCs, "czech");

        Assert.Equal(appCs, LocalizedResourceResolver.Resolve(app, ".tvr", "cs"));

        File.Delete(appCs);
        Assert.Equal(appEn, LocalizedResourceResolver.Resolve(app, ".tvr", "cs"));

        File.Delete(appEn);
        Assert.Equal(app, LocalizedResourceResolver.Resolve(app, ".tvr", "cs"));
    }

    private static List<MissingLocalizationKeyEventArgs> CaptureMissingKeys(System.Action action)
    {
        var events = new List<MissingLocalizationKeyEventArgs>();
        void Handler(object sender, MissingLocalizationKeyEventArgs e) => events.Add(e);

        TSharpVisionIntl.MissingKey += Handler;
        try
        {
            action();
        }
        finally
        {
            TSharpVisionIntl.MissingKey -= Handler;
        }

        return events;
    }

    private static TResourceStringProvider WriteStringProvider(
        string directory,
        Dictionary<string, string> strings)
    {
        string path = Path.Combine(directory, "app_cs.tvr");

        StreamableRegistration.RegisterAll();
        var fpw = new Fpstream(path);
        var rfw = new TResourceFile(fpw);
        rfw.Put(new TStringResource(strings), "sharpvision.intl");
        rfw.Flush();
        fpw.Close();

        return TResourceStringProvider.Load(path);
    }
}

file static class StringResourceTestExtensions
{
    public static TStringResource ToStringResource(this Dictionary<string, string> strings)
        => new(strings);
}
