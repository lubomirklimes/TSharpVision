using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class DesktopStateTests
{
    [Fact]
    public void RoundTripPreservesOrderBoundsTextAndPalette()
    {
        using var driver = new DriverScope();
        using var registry = new StreamableRegistryScope();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tvr");
        var first = new TWindow(new TRect(2, 3, 42, 15), "First", 7);
        first.Insert(new TStaticText(new TRect(2, 2, 30, 4), "Saved text"));
        var second = new TWindow(new TRect(5, 4, 45, 16), "Second", 8);
        var palette = new byte[] { 2, 0x17, 0x70 };
        try
        {
            DesktopState.Save(path, new[] { first, second }, palette);
            var restored = DesktopState.Load(path, palette.Length);
            try
            {
                Assert.Equal(new[] { "First", "Second" }, restored.Windows.Select(w => w.title));
                Assert.Equal(palette, restored.Palette);
                Assert.Equal(2, restored.Windows[0].GetBounds().a.x);
                Assert.Equal(3, restored.Windows[0].GetBounds().a.y);
                Assert.Equal((ushort)7, restored.Windows[0].number);
                var texts = new List<string>();
                restored.Windows[0].ForEachView(v => { if (v is TStaticText t) { t.GetText(out string text); texts.Add(text); } });
                Assert.Contains("Saved text", texts);
            }
            finally { foreach (var w in restored.Windows) w.ShutDown(); }
        }
        finally { first.ShutDown(); second.ShutDown(); File.Delete(path); }
    }

    [Fact]
    public void UnsupportedWindowCannotOverwriteExistingSnapshot()
    {
        using var driver = new DriverScope();
        using var registry = new StreamableRegistryScope();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tvr");
        var dialog = new TDialog(new TRect(0, 0, 30, 10), "Unsupported");
        try
        {
            File.WriteAllText(path, "original");
            Assert.Throws<ArgumentException>(() => DesktopState.Save(path, new[] { dialog }, new byte[] { 0 }));
            Assert.Equal("original", File.ReadAllText(path));
            Assert.ThrowsAny<Exception>(() => DesktopState.Load(path, 1));
        }
        finally { dialog.ShutDown(); File.Delete(path); }
    }
}
