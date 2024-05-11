// Disable test parallelization assembly-wide to avoid flakes from
// global state (TDisplay.driver, ClipboardService.Current, Pstream.types,
// SharpVisionIntl.Current).
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
