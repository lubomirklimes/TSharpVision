using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

/// <summary>
/// Demonstrates <see cref="TProgressBar"/>: a determinate bar over a simulated byte count with its percentage, and an
/// indeterminate activity bar. Step and Reset change them directly; Run starts a worker that only posts ticks, which the
/// dialog applies on the UI thread, so the dialog stays responsive while it runs.
/// </summary>
internal sealed class ProgressDemoDialog : TDialog
{
    public const ushort cmStep = 340, cmRun = 341, cmStop = 342, cmReset = 343, cmTick = 344;

    /// <summary>Simulated total of the determinate bar: 250 MiB.</summary>
    public const long TotalBytes = 250L * 1024 * 1024;

    private CancellationTokenSource? _worker;

    public ProgressDemoDialog(TRect bounds) : base(bounds, "Progress Bars")
    {
        Insert(new TStaticText(new TRect(2, 2, 40, 3), "Reading (determinate, 250 MiB):"));
        Reading = new TProgressBar(new TRect(2, 3, size.x - 2, 4), 0, TotalBytes) { ShowPercentage = true };
        Insert(Reading);

        Insert(new TStaticText(new TRect(2, 5, 40, 6), "Scanning (indeterminate):"));
        Activity = new TProgressBar(new TRect(2, 6, size.x - 2, 7)) { IsIndeterminate = true };
        Insert(Activity);

        Insert(new TButton(new TRect(2, 8, 12, 10), "~S~tep", cmStep, ButtonConstants.bfDefault));
        Insert(new TButton(new TRect(13, 8, 22, 10), "~R~un", cmRun, ButtonConstants.bfNormal));
        Insert(new TButton(new TRect(23, 8, 33, 10), "S~t~op", cmStop, ButtonConstants.bfNormal));
        Insert(new TButton(new TRect(34, 8, 45, 10), "R~e~set", cmReset, ButtonConstants.bfNormal));
        Insert(new TButton(new TRect(46, 8, 56, 10), "~C~lose", Views.cmCancel, ButtonConstants.bfNormal));
        SelectNext(false);
    }

    public TProgressBar Reading { get; }

    public TProgressBar Activity { get; }

    public bool IsRunning => _worker != null;

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What != Events.evCommand) return;
        switch (@event.message.command)
        {
            case cmStep: Step(TotalBytes / 20); break;
            case cmTick: if (_worker != null) Step(TotalBytes / 100); break;
            case cmRun: StartWorker(); break;
            case cmStop: StopWorker(); break;
            case cmReset:
                Reading.Reset();
                Activity.Reset();
                break;
            default: return;
        }
        ClearEvent(ref @event);
    }

    public override void ShutDown()
    {
        StopWorker();
        base.ShutDown();
    }

    // Runs on the UI thread, for a button or for a tick the worker posted.
    private void Step(long bytes)
    {
        Reading.Value += bytes;
        Activity.Advance();
        if (Reading.Value == Reading.Maximum) StopWorker();
    }

    private void StartWorker()
    {
        if (_worker != null) return;
        if (Reading.Value == Reading.Maximum) Reading.Reset();
        var cts = new CancellationTokenSource();
        _worker = cts;
        // The worker never touches a view: it only posts ticks, which the event loop delivers to this dialog.
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(60, cts.Token).ConfigureAwait(false);
                    Post(cmTick);
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    private void StopWorker()
    {
        _worker?.Cancel();
        _worker = null;
    }
}

public partial class TVDemoApp
{
    internal ProgressDemoDialog BuildProgressDemoDialog()
    {
        var dlg = new ProgressDemoDialog(new TRect(0, 0, 58, 11));
        if (DeskTop != null)
            dlg.MoveTo((DeskTop.size.x - 58) / 2, (DeskTop.size.y - 11) / 2);
        DemoButtonLayout.CenterRows(dlg);
        return dlg;
    }

    private void OpenProgressDemoDialog()
    {
        if (DeskTop == null) return;
        var dlg = BuildProgressDemoDialog();
        if (ValidView(dlg) == null) return;
        DeskTop.ExecView(dlg);
        dlg.ShutDown();
    }
}
