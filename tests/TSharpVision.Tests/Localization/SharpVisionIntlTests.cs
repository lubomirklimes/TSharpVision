// TSharpVisionIntl localization provider seam tests.
// TSharpVisionIntl.Current is global state → NonParallel collection.
using System.Collections.Generic;
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
}
