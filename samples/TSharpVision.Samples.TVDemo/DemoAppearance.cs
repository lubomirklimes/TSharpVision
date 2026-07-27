namespace TSharpVision.Samples.TVDemo;

// Fixed classic colors for the reference accessories and game surfaces.
internal static class DemoAppearance
{
    public const byte Gray = 0x70;
    public static byte GrayWindow(int index) => index switch
    {
        3 or 4 => 0x7F, // active frame/title
        5 or 7 => 0x71, // controls/emphasis
        _ => Gray
    };
}
