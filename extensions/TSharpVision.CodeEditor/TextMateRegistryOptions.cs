using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using BuiltInRegistryOptions = TextMateSharp.Grammars.RegistryOptions;

namespace TSharpVision.CodeEditor;

/// <summary>
/// The grammar locator behind a <see cref="Syntax.TextMate.TextMateSyntaxService"/>: grammars loaded
/// from files first, then the grammars bundled with TextMateSharp.Grammars.
/// </summary>
/// <remarks>
/// The theme it hands TextMateSharp is a minimal placeholder. TextMate themes are never used for
/// colour: classification maps scopes to <see cref="Syntax.SyntaxClass"/> roles, and a
/// <see cref="Syntax.SyntaxColorScheme"/> over the view's palette decides the attributes.
/// </remarks>
internal sealed class TextMateRegistryOptions : IRegistryOptions
{
    private readonly Dictionary<string, IRawGrammar> grammars = new(StringComparer.Ordinal);
    private readonly IRawTheme theme = new MinimalRawTheme();
    private readonly BuiltInRegistryOptions? builtIn;

    public TextMateRegistryOptions(BuiltInRegistryOptions? builtIn = null)
    {
        this.builtIn = builtIn;
    }

    /// <summary>Loads a JSON grammar file and returns its scope name, or null when it cannot be used.</summary>
    public string? TryLoadGrammar(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        using var reader = new StreamReader(path);
        IRawGrammar grammar = GrammarReader.ReadGrammarSync(reader);
        string? scopeName = grammar.GetScopeName();
        if (string.IsNullOrWhiteSpace(scopeName))
            return null;

        grammars[scopeName] = grammar;
        return scopeName;
    }

    public IRawTheme GetDefaultTheme() => theme;

    public IRawGrammar? GetGrammar(string scopeName)
        => grammars.TryGetValue(scopeName, out IRawGrammar? grammar) ? grammar : builtIn?.GetGrammar(scopeName);

    public ICollection<string> GetInjections(string scopeName)
        => builtIn?.GetInjections(scopeName) ?? Array.Empty<string>();

    public IRawTheme GetTheme(string scopeName) => theme;

    private sealed class MinimalRawTheme : IRawTheme
    {
        private readonly IRawThemeSetting[] settings =
        [
            new MinimalRawThemeSetting()
        ];

        public string GetInclude() => string.Empty;

        public string GetName() => "TSharpVision";

        public ICollection<KeyValuePair<string, object>> GetGuiColors()
            => Array.Empty<KeyValuePair<string, object>>();

        public ICollection<IRawThemeSetting> GetSettings() => settings;

        public ICollection<IRawThemeSetting> GetTokenColors() => settings;
    }

    private sealed class MinimalRawThemeSetting : IRawThemeSetting
    {
        private readonly IThemeSetting setting = new MinimalThemeSetting();

        public string GetName() => "Default";

        public object GetScope() => string.Empty;

        public IThemeSetting GetSetting() => setting;
    }

    private sealed class MinimalThemeSetting : IThemeSetting
    {
        public string GetBackground() => "#000000";

        public object GetFontStyle() => string.Empty;

        public string GetForeground() => "#c0c0c0";
    }
}
