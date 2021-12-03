namespace SharpVision.Drivers;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ScreenDriverAttribute : Attribute
{
    public Platform System { get; set; }
    public string Driver { get; set; } = string.Empty;
    public int Priority { get; set; }
}
