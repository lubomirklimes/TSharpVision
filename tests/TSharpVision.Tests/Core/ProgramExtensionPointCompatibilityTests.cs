using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class ProgramExtensionPointCompatibilityTests : IDisposable
{
    private readonly DriverScope _driver = new(80, 25);

    public void Dispose() => _driver.Dispose();

    [Fact]
    public void AltNumberSelectionRoutesThroughCanMoveFocusOverride()
    {
        var program = new ProbeProgram { ForcedCanMoveFocus = false };
        try
        {
            var deskTop = Assert.IsType<TDeskTop>(program.DeskTop);
            var first = new TWindow(new TRect(1, 1, 25, 10), "One", 1);
            var second = new TWindow(new TRect(3, 2, 27, 11), "Two", 2);
            deskTop.Insert(first);
            deskTop.Insert(second);
            second.Select();
            program.ResetCounts();
            TEvent selectOne = AltNumber(Keys.kbAlt1);

            program.HandleEvent(ref selectOne);

            Assert.Equal(1, program.CanMoveFocusCalls);
            Assert.Same(second, program.DeskTop.current);

            program.ForcedCanMoveFocus = true;
            selectOne = AltNumber(Keys.kbAlt1);
            program.HandleEvent(ref selectOne);
            Assert.Equal(2, program.CanMoveFocusCalls);
            Assert.Same(first, program.DeskTop.current);
        }
        finally
        {
            program.ShutDown();
        }
    }

    [Fact]
    public void ExecuteDialogWithoutDataRunsAndShutsDownExactlyOnce()
    {
        var program = new ProbeProgram();
        var dialog = new ScriptedDialog(Views.cmOK);
        try
        {
            ushort result = program.ExecuteDialog(dialog);

            Assert.Equal(Views.cmOK, result);
            Assert.Equal(1, program.ExecuteDialogCalls);
            Assert.Equal(1, dialog.ExecuteCalls);
            Assert.Equal(1, dialog.ShutDownCalls);
            Assert.Null(dialog.owner);
            Assert.Null(dialog.last);
        }
        finally
        {
            program.ShutDown();
        }
    }

    [Fact]
    public void AcceptedDialogCopiesFlattenedRecordBackAndEndsOwnership()
    {
        var program = new ProbeProgram();
        var dialog = new ScriptedDialog(Views.cmOK);
        dialog.Input.SetData("initial");
        dialog.Checks.SetData((ushort)1);
        TDataRecord record = Read(dialog);
        dialog.Input.SetData("stale");
        dialog.Checks.SetData((ushort)0);
        dialog.OnExecute = () =>
        {
            Assert.Equal("initial", dialog.Input.Data);
            Assert.Equal((uint)1, dialog.Checks.value);
            dialog.Input.SetData("done");
            dialog.Checks.SetData((ushort)2);
        };
        try
        {
            ushort result = program.ExecuteDialog(dialog, record);

            Assert.Equal(Views.cmOK, result);
            Assert.Equal(record.Size, dialog.OriginalDataSize);
            Assert.Contains(record.Segments, segment => Equals(segment.Value, "done"));
            Assert.Contains(record.Segments, segment => Equals(segment.Value, (ushort)2));
            Assert.DoesNotContain(record.Segments, segment => segment.Value is TDataRecord);
            Assert.Equal(1, dialog.ShutDownCalls);
            Assert.Null(dialog.owner);
        }
        finally
        {
            program.ShutDown();
        }
    }

    [Fact]
    public void CancelDoesNotCopyDialogChangesBack()
    {
        var program = new ProbeProgram();
        var dialog = new ScriptedDialog(Views.cmCancel);
        dialog.Input.SetData("initial");
        dialog.Checks.SetData((ushort)1);
        TDataRecord record = Read(dialog);
        dialog.OnExecute = () =>
        {
            dialog.Input.SetData("cancelled change");
            dialog.Checks.SetData((ushort)2);
        };
        try
        {
            ushort result = program.ExecuteDialog(dialog, record);

            Assert.Equal(Views.cmCancel, result);
            Assert.Contains(record.Segments, segment => Equals(segment.Value, "initial"));
            Assert.Contains(record.Segments, segment => Equals(segment.Value, (ushort)1));
            Assert.DoesNotContain(record.Segments, segment => Equals(segment.Value, "cancelled change"));
            Assert.Equal(1, dialog.ShutDownCalls);
        }
        finally
        {
            program.ShutDown();
        }
    }

    [Fact]
    public void InvalidDialogIsShutDownWithoutExecution()
    {
        var program = new ProbeProgram();
        var dialog = new ScriptedDialog(Views.cmOK) { AcceptValidation = false };
        try
        {
            ushort result = program.ExecuteDialog(dialog);

            Assert.Equal(Views.cmCancel, result);
            Assert.Equal(0, dialog.ExecuteCalls);
            Assert.Equal(1, dialog.ShutDownCalls);
            Assert.Null(dialog.owner);
        }
        finally
        {
            program.ShutDown();
        }
    }

    [Fact]
    public void InsertWindowSuccessUsesBothVirtualHooksAndSelectsWindow()
    {
        var program = new ProbeProgram();
        var window = new TrackingWindow();
        try
        {
            TWindow? result = program.InsertWindow(window);

            Assert.Same(window, result);
            Assert.Equal(1, program.InsertWindowCalls);
            Assert.Equal(1, program.CanMoveFocusCalls);
            Assert.Same(program.DeskTop, window.owner);
            var deskTop = Assert.IsType<TDeskTop>(program.DeskTop);
            Assert.Same(window, deskTop.current);
            Assert.Equal(0, window.ShutDownCalls);
        }
        finally
        {
            program.ShutDown();
        }
    }

    [Fact]
    public void InsertWindowValidationRejectionShutsDownWithoutInsertion()
    {
        var program = new ProbeProgram();
        var window = new TrackingWindow { AcceptValidation = false };
        try
        {
            TWindow? result = program.InsertWindow(window);

            Assert.Null(result);
            Assert.Null(window.owner);
            Assert.Equal(1, window.ShutDownCalls);
            Assert.Equal(0, program.CanMoveFocusCalls);
        }
        finally
        {
            program.ShutDown();
        }
    }

    [Fact]
    public void InsertWindowFocusRejectionShutsDownCandidateAndPreservesCurrentWindow()
    {
        var program = new ProbeProgram();
        var current = new TWindow(new TRect(1, 1, 30, 12), "Current", 1);
        var validator = new FocusValidator { AllowRelease = false };
        validator.options |= Views.ofValidate;
        current.options |= Views.ofValidate;
        current.Insert(validator);
        var deskTop = Assert.IsType<TDeskTop>(program.DeskTop);
        deskTop.Insert(current);
        current.Select();
        var candidate = new TrackingWindow();
        program.ResetCounts();
        try
        {
            TWindow? result = program.InsertWindow(candidate);

            Assert.Null(result);
            Assert.Equal(1, validator.ReleaseValidations);
            Assert.Equal(1, program.CanMoveFocusCalls);
            Assert.Equal(1, candidate.ShutDownCalls);
            Assert.Null(candidate.owner);
            Assert.Same(current, program.DeskTop.current);
        }
        finally
        {
            program.ShutDown();
        }
    }

    private static TEvent AltNumber(ushort keyCode)
    {
        TEvent result = default;
        result.What = Events.evKeyDown;
        result.keyDown.keyCode = keyCode;
        return result;
    }

    private static TDataRecord Read(TGroup group)
    {
        object value = new object();
        group.GetData(ref value);
        return Assert.IsType<TDataRecord>(value);
    }

    private sealed class ProbeProgram : TProgram
    {
        internal bool? ForcedCanMoveFocus { get; set; }
        internal int CanMoveFocusCalls { get; private set; }
        internal int ExecuteDialogCalls { get; private set; }
        internal int InsertWindowCalls { get; private set; }

        public override bool CanMoveFocus()
        {
            CanMoveFocusCalls++;
            return ForcedCanMoveFocus ?? base.CanMoveFocus();
        }

        public override ushort ExecuteDialog(TDialog? dialog, object? data = null)
        {
            ExecuteDialogCalls++;
            return base.ExecuteDialog(dialog, data);
        }

        public override TWindow? InsertWindow(TWindow? window)
        {
            InsertWindowCalls++;
            return base.InsertWindow(window);
        }

        internal void ResetCounts()
        {
            CanMoveFocusCalls = 0;
            ExecuteDialogCalls = 0;
            InsertWindowCalls = 0;
        }
    }

    private sealed class ScriptedDialog : TDialog
    {
        private readonly ushort _result;

        internal ScriptedDialog(ushort result) : base(new TRect(5, 3, 45, 15), "Scripted")
        {
            _result = result;
            var nested = new TGroup(new TRect(1, 1, 30, 7));
            Input = new TInputLine(new TRect(1, 1, 20, 2), 8);
            Checks = new TCheckBoxes(
                new TRect(1, 3, 20, 5),
                new TSItem("One", new TSItem("Two", null)));
            nested.Insert(Input);
            nested.Insert(Checks);
            Insert(nested);
            OriginalDataSize = DataSize();
        }

        internal TInputLine Input { get; }
        internal TCheckBoxes Checks { get; }
        internal ushort OriginalDataSize { get; }
        internal Action? OnExecute { get; set; }
        internal bool AcceptValidation { get; init; } = true;
        internal int ExecuteCalls { get; private set; }
        internal int ShutDownCalls { get; private set; }

        public override ushort Execute()
        {
            ExecuteCalls++;
            Assert.Same(owner, owner?.owner is TProgram program ? program.DeskTop : owner);
            OnExecute?.Invoke();
            return _result;
        }

        public override bool Valid(ushort command)
            => command == Views.cmValid ? AcceptValidation : base.Valid(command);

        public override void ShutDown()
        {
            ShutDownCalls++;
            base.ShutDown();
        }
    }

    private sealed class TrackingWindow : TWindow
    {
        internal TrackingWindow() : base(new TRect(2, 2, 28, 12), "Candidate", 2) { }

        internal bool AcceptValidation { get; init; } = true;
        internal int ShutDownCalls { get; private set; }

        public override bool Valid(ushort command)
            => command == Views.cmValid ? AcceptValidation : base.Valid(command);

        public override void ShutDown()
        {
            ShutDownCalls++;
            base.ShutDown();
        }
    }

    private sealed class FocusValidator : TView
    {
        internal FocusValidator() : base(new TRect(1, 1, 10, 2))
            => options |= Views.ofSelectable;

        internal bool AllowRelease { get; init; }
        internal int ReleaseValidations { get; private set; }

        public override bool Valid(ushort command)
        {
            if (command != Views.cmReleasedFocus)
                return true;
            ReleaseValidations++;
            return AllowRelease;
        }
    }
}
