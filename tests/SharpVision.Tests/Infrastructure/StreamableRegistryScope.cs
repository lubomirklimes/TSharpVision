using SharpVision;

namespace SharpVision.Tests.Infrastructure;

/// <summary>
/// Calls Pstream.DeInitTypes() + StreamableRegistration.RegisterAll() on
/// construction and again on Dispose so each test that touches the registry
/// starts and ends with a fresh registration.
/// </summary>
public sealed class StreamableRegistryScope : IDisposable
{
    public StreamableRegistryScope()
    {
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }

    public void Dispose()
    {
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
    }
}
