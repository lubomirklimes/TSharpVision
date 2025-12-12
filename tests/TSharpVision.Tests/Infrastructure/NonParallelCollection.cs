using Xunit;

namespace TSharpVision.Tests.Infrastructure;

// Used for test classes that touch global state (registry, driver, clipboard,
// intl provider). The DisableParallelization flag prevents them from running
// concurrently even when multiple xUnit test runners are active.
[CollectionDefinition("NonParallel", DisableParallelization = true)]
public sealed class NonParallelCollection { }
