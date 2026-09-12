namespace TSharpVision.Drivers;

/// <summary>Registers a driver type for discovery on a platform with a selection name and priority.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ScreenDriverAttribute : Attribute
{
    /// <summary>Platform on which this registration participates in driver selection.</summary>
    public Platform System { get; set; }
    /// <summary>Name accepted when explicitly selecting this driver.</summary>
    public string Driver { get; set; } = string.Empty;
    /// <summary>Selection rank; higher-priority compatible drivers are preferred during automatic selection.</summary>
    public int Priority { get; set; }
}
