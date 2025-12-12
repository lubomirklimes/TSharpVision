using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class NullDriverEventTests : IDisposable
{
    private readonly DriverScope _driver;

    public NullDriverEventTests()
    {
        _driver = new DriverScope(80, 25);
    }

    public void Dispose() => _driver.Dispose();

    [Fact]
    public void Factory_HonorsNullDriverEnvVar()
    {
        var saved = Environment.GetEnvironmentVariable("TSharpVision_DRIVER");
        try
        {
            Environment.SetEnvironmentVariable("TSharpVision_DRIVER", "NullDriver");
            var d = ScreenDriverFactory.CreateScreenDriver();
            Assert.IsType<NullDriver>(d);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TSharpVision_DRIVER", saved);
        }
    }

    [Fact]
    public void NullDriver_ReportsDefaultDimensions()
    {
        Assert.Equal(80, _driver.Driver.GetCols());
        Assert.Equal(25, _driver.Driver.GetRows());
    }

    [Fact]
    public void GetEvent_DeliversScriptedKey()
    {
        var nd = _driver.Driver;
        var key = new TEvent { What = Events.evKeyDown };
        key.keyDown.keyCode = Keys.kbEsc;
        nd.EnqueueKey(key);

        TEvent received = default;
        TScreen.GetEvent(ref received);
        Assert.Equal(Events.evKeyDown, received.What);
        Assert.Equal(Keys.kbEsc, received.keyDown.keyCode);
    }

    [Fact]
    public void Queue_DeliversScriptedCommand()
    {
        var nd = _driver.Driver;
        TEventQueue.Resume();
        nd.EnqueueCommand(Views.cmQuit);

        TEvent received = default;
        TScreen.GetEvent(ref received);
        Assert.Equal(Events.evCommand, received.What);
        Assert.Equal(Views.cmQuit, received.message.command);
    }

    [Fact]
    public void EmptyQueue_YieldsEvNothing()
    {
        // Drain any residual events first.
        for (int i = 0; i < 32; i++)
        {
            TEvent dummy = default;
            TScreen.GetEvent(ref dummy);
            if (dummy.What == Events.evNothing) break;
        }

        TEvent received = default;
        TScreen.GetEvent(ref received);
        Assert.Equal(Events.evNothing, received.What);
    }

    [Fact]
    public void EventLoop_ProcessesScriptedSequence()
    {
        var nd = _driver.Driver;
        TEventQueue.Resume();

        var kbF1 = new TEvent { What = Events.evKeyDown };
        kbF1.keyDown.keyCode = Keys.kbF1;
        nd.EnqueueKey(kbF1);

        var kbDown = new TEvent { What = Events.evKeyDown };
        kbDown.keyDown.keyCode = Keys.kbDown;
        nd.EnqueueKey(kbDown);

        var quit = new TEvent { What = Events.evCommand };
        quit.message.command = Views.cmQuit;
        nd.EnqueueKey(quit);

        int seen = 0;
        bool gotQuit = false;
        while (!gotQuit && seen < 100)
        {
            TEvent ev = default;
            TScreen.GetEvent(ref ev);
            if (ev.What == Events.evNothing) break;
            seen++;
            if (ev.What == Events.evCommand && ev.message.command == Views.cmQuit)
                gotQuit = true;
        }

        Assert.Equal(3, seen);
        Assert.True(gotQuit);
    }
}
