// TSharpVision — StreamableRegistration.cs
// Central registration helper for all TSharpVision streamable types.
//
// Rationale:
//   Each TStreamableClass auto-registers in Pstream.types via its constructor,
//   which runs once when the static field is first touched.  After a call to
//   Pstream.DeInitTypes() the registry is cleared and the constructors will
//   NOT run again.  This helper re-registers every known type by calling
//   Pstream.RegisterType() explicitly with the already-constructed singletons,
//   which is safe to call any number of times.
//
// Usage:
//   Call StreamableRegistration.RegisterAll() once at application startup, and
//   again after any Pstream.DeInitTypes() call that precedes serialisation /
//   deserialisation.  The call is idempotent and will not throw.
namespace TSharpVision;

/// <summary>
/// Registers all TSharpVision streamable types with the current
/// <see cref="Pstream"/> type registry.
/// </summary>
public static class StreamableRegistration
{
    /// <summary>
    /// Register every known TSharpVision streamable type with
    /// <see cref="Pstream.types"/>.  Safe to call multiple times and safe
    /// after <see cref="Pstream.DeInitTypes()"/>.
    /// </summary>
    public static void RegisterAll()
    {
        // ── Streams / resources ──────────────────────────────────────────
        Pstream.RegisterType(TResourceCollection.StreamableClass);
        Pstream.RegisterType(TStringResource.StreamableClass);

        // ── Views ────────────────────────────────────────────────────────
        Pstream.RegisterType(TView.StreamableClassTView);
        Pstream.RegisterType(TGroup.StreamableClassTGroup);
        Pstream.RegisterType(TFrame.StreamableClassTFrame);
        Pstream.RegisterType(TWindow.StreamableClassTWindow);
        Pstream.RegisterType(TScrollBar.StreamableClassTScrollBar);
        Pstream.RegisterType(TScroller.StreamableClassTScroller);
        Pstream.RegisterType(TListViewer.StreamableClassTListViewer);

        // ── App ──────────────────────────────────────────────────────────
        Pstream.RegisterType(TDeskTop.StreamableClassTDeskTop);
        Pstream.RegisterType(TBackground.StreamableClassTBackground);

        // ── Dialogs — text / list ────────────────────────────────────────
        Pstream.RegisterType(TTerminal.StreamableClassTTerminal);
        Pstream.RegisterType(TStringCollection.StreamableClass);
        Pstream.RegisterType(TListBox.StreamableClassTListBox);
        Pstream.RegisterType(TDialog.StreamableClassTDialog);
        Pstream.RegisterType(TButton.StreamableClassTButton);
        Pstream.RegisterType(TStaticText.StreamableClassTStaticText);
        Pstream.RegisterType(TParamText.StreamableClassTParamText);
        Pstream.RegisterType(TLabel.StreamableClassTLabel);
        Pstream.RegisterType(TInputLine.StreamableClassTInputLine);
        Pstream.RegisterType(TCluster.StreamableClassTCluster);
        Pstream.RegisterType(TCheckBoxes.StreamableClassTCheckBoxes);
        Pstream.RegisterType(TRadioButtons.StreamableClassTRadioButtons);
        Pstream.RegisterType(THistory.StreamableClassTHistory);

        // ── Dialogs — color ──────────────────────────────────────────────
        Pstream.RegisterType(TColorDisplay.StreamableClassTColorDisplay);
        Pstream.RegisterType(TColorGroupList.StreamableClassTColorGroupList);
        Pstream.RegisterType(TColorItemList.StreamableClassTColorItemList);
        Pstream.RegisterType(TColorSelector.StreamableClassTColorSelector);
        Pstream.RegisterType(TMonoSelector.StreamableClassTMonoSelector);
        Pstream.RegisterType(TColorDialog.StreamableClassTColorDialog);

        // ── Dialogs — file ───────────────────────────────────────────────
        Pstream.RegisterType(TFileDialog.StreamableClassTFileDialog);
        Pstream.RegisterType(TFileInputLine.StreamableClassTFileInputLine);
        Pstream.RegisterType(TFileList.StreamableClassTFileList);
        Pstream.RegisterType(TFileInfoPane.StreamableClassTFileInfoPane);
        Pstream.RegisterType(TDirListBox.StreamableClassTDirListBox);
        Pstream.RegisterType(TChDirDialog.StreamableClassTChDirDialog);

        // ── Menus ────────────────────────────────────────────────────────
        Pstream.RegisterType(TMenuView.StreamableClassTMenuView);
        Pstream.RegisterType(TMenuBar.StreamableClassTMenuBar);
        Pstream.RegisterType(TStatusLine.StreamableClassTStatusLine);

        // ── Editors ──────────────────────────────────────────────────────
        Pstream.RegisterType(TEditor.StreamableClassTEditor);
        Pstream.RegisterType(TFileEditor.StreamableClassTFileEditor);
        Pstream.RegisterType(TEditWindow.StreamableClassTEditWindow);
        Pstream.RegisterType(TMemo.StreamableClassTMemo);
        Pstream.RegisterType(TIndicator.StreamableClassTIndicator);

        // ── Validators (also re-registers TInputLine) ────────────────────
        // Delegates to TValidator.RegisterStreamableTypes() which covers
        // TValidator, TFilterValidator, TRangeValidator, TPXPictureValidator,
        // TLookupValidator, TStringLookupValidator, and TInputLine.
        TValidator.RegisterStreamableTypes();

        // ── Help ─────────────────────────────────────────────────────────
        // Delegates to THelpFile.RegisterStreamableTypes() which covers
        // THelpTopic and THelpIndex.
        THelpFile.RegisterStreamableTypes();
    }
}
