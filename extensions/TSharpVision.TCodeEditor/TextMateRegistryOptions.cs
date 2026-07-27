using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace TSharpVision.TCodeEditor;

internal sealed class TextMateRegistryOptions : IRegistryOptions
{
    private readonly Dictionary<string, IRawGrammar> grammars = new(StringComparer.Ordinal);
    private readonly IRawTheme theme = new MinimalRawTheme();

    public bool TryLoadGrammar(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        using var reader = new StreamReader(path);
        IRawGrammar grammar = GrammarReader.ReadGrammarSync(reader);
        string? scopeName = grammar.GetScopeName();
        if (string.IsNullOrWhiteSpace(scopeName))
            return false;

        grammars[scopeName] = grammar;
        return true;
    }

    public IRawTheme GetDefaultTheme() => theme;

    public IRawGrammar? GetGrammar(string scopeName)
        => grammars.TryGetValue(scopeName, out IRawGrammar? grammar) ? grammar : null;

    public ICollection<string> GetInjections(string scopeName) => Array.Empty<string>();

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
