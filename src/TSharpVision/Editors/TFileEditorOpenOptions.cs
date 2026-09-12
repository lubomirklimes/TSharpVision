namespace TSharpVision;

/// <summary>Options controlling how file-editor input is decoded.</summary>
public sealed class TFileEditorOpenOptions
{
    /// <summary>Requested file-open encoding policy; defaults to automatic detection.</summary>
    public EditorTextEncoding Encoding { get; set; } = EditorTextEncoding.Auto;
}
