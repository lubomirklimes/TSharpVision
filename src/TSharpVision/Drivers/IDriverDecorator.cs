namespace TSharpVision.Drivers;

/// <summary>Optional startup provider that wraps one selected, uninitialized backend.</summary>
/// <remarks>
/// <para>Activation requires an explicit assembly-qualified type name in <c>TSHARPVISION_DRIVER_DECORATOR</c>;
/// deploy providers beside the application. The provider must not select, initialize or shut down a driver.</para>
/// <para><b>Ownership.</b> <see cref="ScreenDriverFactory.CreateScreenDriver"/> owns the backend until
/// <see cref="Decorate"/> returns a non-null driver; from then on that driver is the single owner. If the
/// provider type cannot be resolved or constructed, or <see cref="Decorate"/> throws or returns null, the
/// factory still owns the never-initialized backend: it disposes it when it implements
/// <see cref="IDisposable"/> (never <see cref="IDriver.Shutdown"/>) and rethrows. A provider must therefore
/// not dispose the backend when it fails. Returning the backend itself leaves it undecorated.</para>
/// <para>After a successful return, the wrapper's <see cref="IDriver.Initialize"/> must undo its own partial
/// startup before throwing, and its <see cref="IDriver.Shutdown"/> releases the backend exactly once; a
/// wrapper should not also dispose a backend it already shut down. The provider instance itself is a
/// stateless factory: it is used once and never disposed.</para>
/// </remarks>
public interface IDriverDecorator
{
    /// <summary>Transfers lifecycle ownership of the supplied backend to the returned wrapper.</summary>
    IDriver Decorate(IDriver backend, string? requestedBackend);
}
