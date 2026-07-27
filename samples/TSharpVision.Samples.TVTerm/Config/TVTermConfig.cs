using System.IO;
using System.Text.Json;

namespace TSharpVision.Samples.TVTerm;

public enum DefaultSessionMode
{
    Pty = 0,
    Pipe = 1,
    Fake = 2,
    InMemory = 3,
}

/// <summary>
/// Simple JSON-backed configuration for TVTerm. Stored next to the executable
/// as <c>tvterm.json</c>. Kept separate from the .cfg file used by
/// <see cref="TApplication"/>'s suspend/resume infrastructure.
/// </summary>
public sealed class TVTermConfig
{
    private const string FileName = "tvterm.json";

    public int ScrollbackLines { get; set; } = 1000;
    public string DefaultShell { get; set; } = string.Empty;
    public DefaultSessionMode DefaultSessionMode { get; set; } = DefaultSessionMode.Pty;
    /// <summary>16 ANSI colour → TSharpVision palette index map (raw VGA attribute byte).</summary>
    public byte[] ColorMap { get; set; } = System.Array.Empty<byte>();
    /// <summary>Free-form key bindings (key → action string). Not used by default.</summary>
    public System.Collections.Generic.Dictionary<string, string> KeyBindings { get; set; }
        = new System.Collections.Generic.Dictionary<string, string>();

    public static TVTermConfig Load()
    {
        string path = Path.Combine(System.AppContext.BaseDirectory, FileName);
        if (!File.Exists(path)) return new TVTermConfig();
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TVTermConfig>(json) ?? new TVTermConfig();
        }
        catch
        {
            return new TVTermConfig();
        }
    }

    public void Save()
    {
        string path = Path.Combine(System.AppContext.BaseDirectory, FileName);
        try
        {
            string json = JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }
}
