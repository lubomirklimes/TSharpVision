// Disable test parallelization assembly-wide to avoid flakes from
// global state (TDisplay.driver, ClipboardService.Current, Pstream.types,
// TSharpVisionIntl.Current).
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
