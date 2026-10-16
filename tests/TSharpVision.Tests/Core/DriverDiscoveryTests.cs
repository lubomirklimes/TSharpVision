// ScreenDriverFactory discovery tests.
//
// Discovery used to load every .dll beside the application and enumerate all of its types, to
// find the few that implement IDriver. An application directory is mostly libraries that have
// nothing to do with the screen - archive readers, grammars, parsers - and loading each of them
// was most of what the framework did before an application could be constructed. A driver is
// only ever selected through a [ScreenDriver] attribute on its own class, so an assembly that
// never names that attribute is now told apart from its metadata and never loaded by discovery.
//
// What must not change: every assembly that can contribute a driver is still a candidate (the
// driver packages themselves are checked in TSharpVision.Drivers.Console.Tests), and a file
// nobody can read is still left to the loader.
using System.Reflection;
using TSharpVision.Drivers;
using Xunit;

namespace TSharpVision.Tests.Core;

public sealed class DriverDiscoveryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "tsv-discovery-" + Guid.NewGuid().ToString("N"));

    public DriverDiscoveryTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool MayDefineDriver(string file)
    {
        MethodInfo method = typeof(ScreenDriverFactory)
            .GetMethod("MayDefineDriver", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(null, [file])!;
    }

    [Fact]
    public void TheAssemblyThatDefinesTheDriverContractIsACandidate()
    {
        // NullDriver lives there.
        Assert.True(MayDefineDriver(typeof(IDriver).Assembly.Location));
    }

    [Fact]
    public void AnAssemblyThatUsesTheFrameworkWithoutDeclaringADriverIsNotACandidate()
    {
        // The test-support library references TSharpVision and drives a driver, as an application or a view
        // library may; none of that makes a driver anybody could select.
        System.Reflection.Assembly library = typeof(Infrastructure.DriverScope).Assembly;

        Assert.Contains(
            library.GetReferencedAssemblies(),
            reference => reference.Name == typeof(IDriver).Assembly.GetName().Name);
        Assert.False(MayDefineDriver(library.Location));
    }

    [Fact]
    public void TheAttributeThatMakesADriverSelectableIsNotInherited()
    {
        // What the metadata check rests on: a class cannot become selectable through a base class in
        // another assembly, so its own assembly always names the attribute.
        var usage = typeof(ScreenDriverAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;
        Assert.False(usage.Inherited);
    }

    [Fact]
    public void AManagedLibraryThatDoesNotReferenceTheContractIsNotACandidate()
    {
        // xunit knows nothing of TSharpVision: it cannot define a driver.
        string library = typeof(FactAttribute).Assembly.Location;

        Assert.DoesNotContain(
            typeof(FactAttribute).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == typeof(IDriver).Assembly.GetName().Name);
        Assert.False(MayDefineDriver(library));
    }

    [Fact]
    public void AFileThatIsNotAnAssemblyIsNotACandidate()
    {
        // What a native library looked like to the old code: a load that failed and was skipped.
        string file = Path.Combine(_directory, "native.dll");
        File.WriteAllBytes(file, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00]);
        Assert.False(MayDefineDriver(file));

        string text = Path.Combine(_directory, "text.dll");
        File.WriteAllText(text, "not a portable executable");
        Assert.False(MayDefineDriver(text));

        string empty = Path.Combine(_directory, "empty.dll");
        File.WriteAllBytes(empty, []);
        Assert.False(MayDefineDriver(empty));
    }

    [Fact]
    public void AFileThatCannotBeReadIsLeftToTheLoader()
    {
        // Held open exclusively: the metadata cannot be read, so nothing is concluded from it.
        string file = Path.Combine(_directory, "locked.dll");
        File.Copy(typeof(FactAttribute).Assembly.Location, file);

        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.True(MayDefineDriver(file));

        Assert.False(MayDefineDriver(file));
    }

    [Fact]
    public void EveryDriverTypeInTheLoadedAssembliesIsStillDiscovered()
    {
        // The headless driver is defined in the core assembly; discovery must still find it.
        var field = typeof(ScreenDriverFactory).GetField("DriverTypes", BindingFlags.Static | BindingFlags.NonPublic)!;
        var types = (Type[])field.GetValue(null)!;

        Assert.Contains(typeof(NullDriver), types);
    }
}
