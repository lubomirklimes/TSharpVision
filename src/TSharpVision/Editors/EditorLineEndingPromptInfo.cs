using System;
using System.Collections.Generic;

namespace TSharpVision;

/// <summary>
/// Payload of the <c>Views.edMixedLineEndings</c> editor prompt: describes a file whose line endings were mixed when it
/// was loaded, offers the line endings it can be saved with, and receives the user's choice.
/// </summary>
/// <remarks>
/// A prompt handler sets <see cref="Choice"/> to one of <see cref="Choices"/> and returns <c>Views.cmOK</c> (or
/// <c>Views.cmYes</c>) to save with that style. Any other result cancels the save and nothing is written.
/// </remarks>
public sealed class EditorLineEndingPromptInfo
{
    private static readonly LineEndingKind[] AvailableChoices =
        { LineEndingKind.Lf, LineEndingKind.CrLf, LineEndingKind.Cr };

    private LineEndingKind _choice;

    /// <summary>Creates a prompt payload for a file and its loaded line-ending counts; the suggested style is the initial choice.</summary>
    public EditorLineEndingPromptInfo(string fileName, int crLfCount, int lfCount, int crCount, LineEndingKind suggested)
    {
        if (crLfCount < 0) throw new ArgumentOutOfRangeException(nameof(crLfCount));
        if (lfCount < 0) throw new ArgumentOutOfRangeException(nameof(lfCount));
        if (crCount < 0) throw new ArgumentOutOfRangeException(nameof(crCount));
        if (Array.IndexOf(AvailableChoices, suggested) < 0)
            throw new ArgumentOutOfRangeException(nameof(suggested), "The suggested line ending must be Lf, CrLf or Cr.");
        FileName = fileName ?? string.Empty;
        CrLfCount = crLfCount;
        LfCount = lfCount;
        CrCount = crCount;
        Suggested = suggested;
        _choice = suggested;
    }

    /// <summary>Path of the document being saved.</summary>
    public string FileName { get; }
    /// <summary>Number of CR LF line endings in the file when it was loaded.</summary>
    public int CrLfCount { get; }
    /// <summary>Number of lone LF line endings in the file when it was loaded.</summary>
    public int LfCount { get; }
    /// <summary>Number of lone CR line endings in the file when it was loaded.</summary>
    public int CrCount { get; }
    /// <summary>The style that occurred most often when the file was loaded (ties prefer CR LF, then LF).</summary>
    public LineEndingKind Suggested { get; }
    /// <summary>The line endings the document can be saved with, in display order: LF, CR LF, CR.</summary>
    public IReadOnlyList<LineEndingKind> Choices => AvailableChoices;

    /// <summary>The style chosen by the prompt handler; initially <see cref="Suggested"/>. Only values in <see cref="Choices"/> are accepted.</summary>
    public LineEndingKind Choice
    {
        get => _choice;
        set
        {
            if (Array.IndexOf(AvailableChoices, value) < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "The line ending must be Lf, CrLf or Cr.");
            _choice = value;
        }
    }
}
