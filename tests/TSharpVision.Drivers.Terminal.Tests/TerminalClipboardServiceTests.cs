using TSharpVision.Drivers.Terminal;
using Xunit;

namespace TSharpVision.Tests.Drivers;

public sealed class TerminalClipboardServiceTests
{
    private sealed class FakeRunner : ITerminalClipboardCommandRunner
    {
        public readonly HashSet<string> Commands = new();
        public string ClipboardText;
        public string LastReadFileName;
        public string[] LastReadArgs;
        public string LastWriteFileName;
        public string[] LastWriteArgs;
        public string LastWrittenText;
        public bool WriteResult = true;

        public bool CommandExists(string fileName) => Commands.Contains(fileName);

        public string ReadText(string fileName, string[] args)
        {
            LastReadFileName = fileName;
            LastReadArgs = args;
            return ClipboardText;
        }

        public bool WriteText(string fileName, string[] args, string text)
        {
            LastWriteFileName = fileName;
            LastWriteArgs = args;
            LastWrittenText = text;
            ClipboardText = text;
            return WriteResult;
        }
    }

    [Fact]
    public void IsAvailable_LinuxWithWlClipboard_ReturnsTrue()
    {
        var fake = new FakeRunner();
        fake.Commands.Add("wl-paste");
        fake.Commands.Add("wl-copy");
        var svc = new TerminalClipboardService(fake, TerminalClipboardPlatform.Linux);
        Assert.True(svc.IsAvailable);
    }

    [Fact]
    public void IsAvailable_UnsupportedPlatform_ReturnsFalse()
    {
        var fake = new FakeRunner();
        fake.Commands.Add("wl-paste");
        fake.Commands.Add("wl-copy");
        var svc = new TerminalClipboardService(fake, TerminalClipboardPlatform.Unsupported);
        Assert.False(svc.IsAvailable);
    }

    [Fact]
    public void GetText_LinuxPrefersWlClipboard()
    {
        var fake = new FakeRunner { ClipboardText = "hello" };
        fake.Commands.Add("wl-paste");
        fake.Commands.Add("wl-copy");
        fake.Commands.Add("xclip");
        var svc = new TerminalClipboardService(fake, TerminalClipboardPlatform.Linux);
        Assert.Equal("hello", svc.GetText());
        Assert.Equal("wl-paste", fake.LastReadFileName);
        Assert.Empty(fake.LastReadArgs);
    }

    [Fact]
    public void GetText_LinuxFallsBackToXclip()
    {
        var fake = new FakeRunner { ClipboardText = "xclip text" };
        fake.Commands.Add("xclip");
        var svc = new TerminalClipboardService(fake, TerminalClipboardPlatform.Linux);
        Assert.Equal("xclip text", svc.GetText());
        Assert.Equal("xclip", fake.LastReadFileName);
        Assert.Equal(new[] { "-selection", "clipboard", "-out" }, fake.LastReadArgs);
    }

    [Fact]
    public void GetText_NormalisesCarriageReturns()
    {
        var fake = new FakeRunner { ClipboardText = "a\r\nb\rc" };
        fake.Commands.Add("pbpaste");
        fake.Commands.Add("pbcopy");
        var svc = new TerminalClipboardService(fake, TerminalClipboardPlatform.MacOS);
        Assert.Equal("a\nb\nc", svc.GetText());
    }

    [Fact]
    public void TryGetText_EmptyClipboard_ReturnsFalse()
    {
        var fake = new FakeRunner { ClipboardText = string.Empty };
        fake.Commands.Add("wl-paste");
        fake.Commands.Add("wl-copy");
        var svc = new TerminalClipboardService(fake, TerminalClipboardPlatform.Linux);
        bool ok = svc.TryGetText(out string text);
        Assert.False(ok);
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void SetText_LinuxUsesWlCopy()
    {
        var fake = new FakeRunner();
        fake.Commands.Add("wl-paste");
        fake.Commands.Add("wl-copy");
        var svc = new TerminalClipboardService(fake, TerminalClipboardPlatform.Linux);
        Assert.True(svc.SetText("copy me"));
        Assert.Equal("wl-copy", fake.LastWriteFileName);
        Assert.Empty(fake.LastWriteArgs);
        Assert.Equal("copy me", fake.LastWrittenText);
    }

    [Fact]
    public void SetText_Null_TreatedAsEmpty()
    {
        var fake = new FakeRunner();
        fake.Commands.Add("pbpaste");
        fake.Commands.Add("pbcopy");
        var svc = new TerminalClipboardService(fake, TerminalClipboardPlatform.MacOS);
        Assert.True(svc.SetText(null!));
        Assert.Equal(string.Empty, fake.LastWrittenText);
    }

    [Fact]
    public void SetText_NoBackend_ReturnsFalse()
    {
        var fake = new FakeRunner();
        var svc = new TerminalClipboardService(fake, TerminalClipboardPlatform.Linux);
        Assert.False(svc.SetText("x"));
    }
}
