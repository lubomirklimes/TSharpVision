using TSharpVision.Drivers;

namespace TSharpVision.Drivers.SDL.Tests;

[Collection("SDL initialization")]
public sealed class SdlEnvironmentVariableTests
{
    [Theory]
    [InlineData("1", null, true)]
    [InlineData(null, "1", true)]
    [InlineData("0", "1", false)]
    [InlineData(null, null, false)]
    [InlineData("true", null, false)]
    public void HeadlessOptOutReadsTheUppercaseNameBeforeTheLegacySpelling(string? upper, string? legacy, bool requested)
    {
        Assert.Equal("TSHARPVISION_NO_SDL", SdlHeadlessOptOut.EnvironmentVariable);
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["TSHARPVISION_NO_SDL"] = upper,
            ["TSharpVision_NO_SDL"] = legacy,
        };
        Assert.Equal(requested, SdlHeadlessOptOut.IsRequested(name => variables.GetValueOrDefault(name)));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("OFF", 0)]
    [InlineData(" false ", 0)]
    [InlineData("1", 1)]
    [InlineData("On", 1)]
    [InlineData("TRUE", 1)]
    [InlineData("Adaptive", -1)]
    [InlineData("yes", null)]
    [InlineData("2", null)]
    public void VSyncRequestIsTrimmedAndCaseInsensitive(string value, int? expected)
        => Assert.Equal(expected, SDLRenderer.ParseVSyncRequest(value));

    [Fact]
    public void PaddedDriverNameSelectsThatDriver()
    {
        string? saved = Environment.GetEnvironmentVariable("TSHARPVISION_DRIVER");
        try
        {
            // The SDL drivers outrank NullDriver here, so an unmatched name would not select it.
            Environment.SetEnvironmentVariable("TSHARPVISION_DRIVER", "  NullDriver ");
            Assert.IsType<NullDriver>(ScreenDriverFactory.CreateScreenDriver());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TSHARPVISION_DRIVER", saved);
        }
    }
}
