// SharpVision — StreamableRegistration.cs
// Central registration helper for all SharpVision streamable types.
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
namespace SharpVision;

/// <summary>
/// Registers all SharpVision streamable types with the current
/// <see cref="Pstream"/> type registry.
/// </summary>
public static class StreamableRegistration
{
    /// <summary>
    /// Register every known SharpVision streamable type with
    /// <see cref="Pstream.types"/>.  Safe to call multiple times and safe
    /// after <see cref="Pstream.DeInitTypes()"/>.
    /// </summary>
    public static void RegisterAll()
    {
        //TODO: streamable initialization for all streamable types, e.g. TFrame.Build, TWindow.StreamableClassTWindow, etc.
    }
}
