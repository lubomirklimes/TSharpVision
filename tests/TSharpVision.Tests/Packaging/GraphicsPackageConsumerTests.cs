// Opt-in end-to-end check that TSharpVision.Graphics works as an optional NuGet package.
//
// Packs src/TSharpVision, src/TSharpVision.Graphics and src/TSharpVision.Drivers.SDL with one unique version into a
// private temp feed (the packs themselves restore like the normal repo build), copies the pinned SDL3-CS packages from
// the local NuGet cache into the same feed, and then builds two console apps OUTSIDE the repo that use
// PackageReference only, restoring from that feed alone (cleared sources, private packages folder, neutral
// Directory.Build.* and Directory.Packages.props):
//   1) gfx: TSharpVision.Graphics only. Runs a GWindow + GImage under NullDriver (text fallback, no layer) and under
//      an in-app recording IPixelGraphicsDriver decorator (one layer with one Image primitive and the image itself).
//      Its resolved packages must contain TSharpVision and TSharpVision.Graphics and must NOT contain
//      TSharpVision.Drivers.SDL.
//   2) sdl: TSharpVision.Graphics + TSharpVision.Drivers.SDL. Builds, and checks by reflection that both SDL drivers
//      implement IPixelGraphicsDriver. No window is opened.
//
// Slow (several dotnet invocations), so it only runs when TSV_RUN_PACKAGE_CONSUMER_TESTS=1:
//   TSV_RUN_PACKAGE_CONSUMER_TESTS=1 dotnet test tests/TSharpVision.Tests --filter FullyQualifiedName~GraphicsPackageConsumerTests
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Tests.Packaging;

public sealed class GraphicsPackageConsumerTests(ITestOutputHelper output)
{
    private const string OptInVariable = "TSV_RUN_PACKAGE_CONSUMER_TESTS";

    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));

    // Prerelease of the repository's family version (Directory.Build.props), so the private test packages sort
    // below the real release and never go stale when the version is bumped.
    private static string RepositoryVersion() =>
        System.Xml.Linq.XDocument.Load(Path.Combine(RepoRoot(), "Directory.Build.props"))
            .Descendants("Version").Single().Value.Trim();

    [Fact]
    public void PackedGraphics_WorksForPackageOnlyConsumers_WithAndWithoutSdl()
    {
        if (Environment.GetEnvironmentVariable(OptInVariable) != "1")
            return; // Opt-in; see the file header.

        string root = Path.Combine(Path.GetTempPath(), "tsv-gfx-consumer-" + Guid.NewGuid().ToString("N")[..12]);
        string feed = Path.Combine(root, "feed");
        string packages = Path.Combine(root, "packages");
        string version = RepositoryVersion() + "-gfxcheck." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        Directory.CreateDirectory(feed);
        var clock = Stopwatch.StartNew();
        try
        {
            foreach (string project in new[]
            {
                Path.Combine("src", "TSharpVision", "TSharpVision.csproj"),
                Path.Combine("src", "TSharpVision.Graphics", "TSharpVision.Graphics.csproj"),
                Path.Combine("src", "TSharpVision.Drivers.SDL", "TSharpVision.Drivers.SDL.csproj"),
            })
            {
                Run(RepoRoot(), "pack", Path.Combine(RepoRoot(), project), "-c", "Release", "-o", feed,
                    "--artifacts-path", Path.Combine(root, "artifacts"), "-p:Version=" + version, "-nologo");
            }
            output.WriteLine($"packed in {clock.Elapsed}");

            AssertGraphicsNuspec(Path.Combine(feed, $"TSharpVision.Graphics.{version}.nupkg"), version);
            CopyPinnedSdlPackages(feed);

            File.WriteAllText(Path.Combine(root, "NuGet.config"), $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="local" value="{feed}" />
                  </packageSources>
                </configuration>
                """);
            // Keep the temp projects out of any Directory.Build.* / central package management above them.
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), "<Project />");
            File.WriteAllText(Path.Combine(root, "Directory.Build.targets"), "<Project />");
            File.WriteAllText(Path.Combine(root, "Directory.Packages.props"),
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");

            // ── 1) Graphics only ─────────────────────────────────────────
            string gfx = WriteConsumer(root, "gfx", "TsvGfxConsumer", version, withSdl: false, GraphicsOnlyProgram);
            Run(root, "build", Path.Combine(gfx, "TsvGfxConsumer.csproj"), "-c", "Release", "--packages", packages, "-nologo");
            AssertAssets(gfx, version, expectSdl: false);
            string dll = Path.Combine(gfx, "bin", "Release", "net10.0", "TsvGfxConsumer.dll");
            Assert.True(File.Exists(Path.Combine(gfx, "bin", "Release", "net10.0", "TSharpVision.Graphics.dll")));
            Assert.False(File.Exists(Path.Combine(gfx, "bin", "Release", "net10.0", "TSharpVision.Drivers.SDL.dll")));

            string text = Run(Path.GetTempPath(), dll, "text");
            output.WriteLine(text);
            Assert.Contains("fit-width=0,12,100,75", text);
            Assert.Contains("fit-height=90,20,40,30", text);
            Assert.Contains("pixel-capability=False", text);
            Assert.Contains("has-layer=False", text);
            Assert.Contains("fallback-on-screen=True", text);
            Assert.Contains("text-done", text);

            string pixel = Run(Path.GetTempPath(), dll, "pixel");
            output.WriteLine(pixel);
            Assert.Contains("pixel-capability=True", pixel);
            Assert.Contains("has-layer=True", pixel);
            Assert.Contains("layers=1", pixel);
            Assert.Contains("owner-is-view=True", pixel);
            Assert.Contains("primitives=1", pixel);
            Assert.Contains("kind=Image index=0 filter=Linear opacity=255", pixel);
            Assert.Contains("images=1 same-instance=True size=4x3", pixel);
            Assert.Contains("view-pixels=24,48,464,288", pixel);
            Assert.Contains("image-rect=64,48,448,336", pixel);
            Assert.Contains("matches-fit=True", pixel);
            Assert.Contains("clip=24,48,464,288", pixel);
            Assert.Contains("replaced-image-submitted=True", pixel);
            Assert.Contains("after-close layers=0 has-layer=False", pixel);
            Assert.Contains("pixel-done", pixel);

            // ── 2) Graphics + SDL ────────────────────────────────────────
            string sdl = WriteConsumer(root, "sdl", "TsvGfxSdlConsumer", version, withSdl: true, SdlProgram);
            Run(root, "build", Path.Combine(sdl, "TsvGfxSdlConsumer.csproj"), "-c", "Release", "--packages", packages, "-nologo");
            AssertAssets(sdl, version, expectSdl: true);
            string reflection = Run(Path.GetTempPath(), Path.Combine(sdl, "bin", "Release", "net10.0", "TsvGfxSdlConsumer.dll"));
            output.WriteLine(reflection);
            Assert.Contains("SDLGpuDriver-pixel=True", reflection);
            Assert.Contains("SDLDriver-pixel=True", reflection);
            Assert.Contains("gwindow=TSharpVision.Graphics.GWindow", reflection);
            output.WriteLine($"total {clock.Elapsed}");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // The Graphics package depends on exactly TSharpVision at the same version, and nothing else.
    private static void AssertGraphicsNuspec(string nupkg, string version)
    {
        using ZipArchive zip = ZipFile.OpenRead(nupkg);
        ZipArchiveEntry nuspec = Assert.Single(zip.Entries, e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        XDocument doc;
        using (Stream s = nuspec.Open()) doc = XDocument.Load(s);
        var deps = doc.Descendants().Where(e => e.Name.LocalName == "dependency")
            .Select(e => (Id: (string)e.Attribute("id")!, Version: (string)e.Attribute("version")!)).ToArray();
        Assert.Equal(new[] { ("TSharpVision", version) }, deps);
        Assert.NotNull(zip.GetEntry("lib/net10.0/TSharpVision.Graphics.dll"));
        Assert.NotNull(zip.GetEntry("lib/net10.0/TSharpVision.Graphics.xml"));
        Assert.NotNull(zip.GetEntry("README.md"));
    }

    // The SDL driver package depends on the SDL3-CS packages pinned in Directory.Packages.props. The repo pack above
    // restored them into the NuGet cache; copying their nupkgs keeps the consumer restore offline and feed-only.
    private static void CopyPinnedSdlPackages(string feed)
    {
        string cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        XDocument props = XDocument.Load(Path.Combine(RepoRoot(), "Directory.Packages.props"));
        var pinned = props.Descendants("PackageVersion")
            .Select(e => (Id: (string)e.Attribute("Include")!, Version: (string)e.Attribute("Version")!))
            .Where(p => p.Id.StartsWith("SDL3-CS", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(pinned);
        foreach (var (id, ver) in pinned)
        {
            string lower = id.ToLowerInvariant();
            string source = Path.Combine(cache, lower, ver, $"{lower}.{ver}.nupkg");
            Assert.True(File.Exists(source), $"{id} {ver} is not in the NuGet cache ({source}).");
            File.Copy(source, Path.Combine(feed, $"{lower}.{ver}.nupkg"));
        }
    }

    private static void AssertAssets(string project, string version, bool expectSdl)
    {
        using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "obj", "project.assets.json")));
        string[] libraries = assets.RootElement.GetProperty("libraries").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Contains($"TSharpVision/{version}", libraries);
        Assert.Contains($"TSharpVision.Graphics/{version}", libraries);
        Assert.Equal(expectSdl, libraries.Contains($"TSharpVision.Drivers.SDL/{version}"));
        if (!expectSdl)
        {
            Assert.DoesNotContain(libraries, l => l.StartsWith("TSharpVision.Drivers.", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(libraries, l => l.StartsWith("SDL3-CS", StringComparison.OrdinalIgnoreCase));
        }
        // Every TSharpVision package resolved at the packed version, and each internal dependency asks for it.
        Assert.All(libraries.Where(l => l.StartsWith("TSharpVision", StringComparison.Ordinal)),
            l => Assert.EndsWith("/" + version, l));
        JsonElement target = assets.RootElement.GetProperty("targets").GetProperty("net10.0");
        foreach (string id in expectSdl ? new[] { "TSharpVision.Graphics", "TSharpVision.Drivers.SDL" } : ["TSharpVision.Graphics"])
        {
            JsonElement deps = target.GetProperty($"{id}/{version}").GetProperty("dependencies");
            Assert.Equal(version, deps.GetProperty("TSharpVision").GetString());
            if (id == "TSharpVision.Graphics")
                Assert.Equal(["TSharpVision"], deps.EnumerateObject().Select(p => p.Name).ToArray());
        }
    }

    private static string WriteConsumer(string root, string folder, string name, string version, bool withSdl, string program)
    {
        string dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        string sdlReference = withSdl ? $"""<PackageReference Include="TSharpVision.Drivers.SDL" Version="{version}" />""" : "";
        File.WriteAllText(Path.Combine(dir, name + ".csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <NuGetAudit>false</NuGetAudit>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="TSharpVision.Graphics" Version="{version}" />
                {sdlReference}
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(dir, "Program.cs"), program);
        return dir;
    }

    // A package-only app: an image preview window as docs/graphics/tsharpcommander-image-preview.md describes it.
    private const string GraphicsOnlyProgram = """
        using TSharpVision;
        using TSharpVision.Constants;
        using TSharpVision.Drivers;
        using TSharpVision.Graphics;

        bool pixelMode = args.Length > 0 && args[0] == "pixel";

        // "Decoded" RGBA8888, straight alpha, rows top to bottom: what an app's own decoder hands over.
        const int W = 4, H = 3;
        byte[] rgba = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++) { rgba[i * 4] = (byte)(i * 20); rgba[i * 4 + 1] = 0x80; rgba[i * 4 + 2] = 0x40; rgba[i * 4 + 3] = 0xFF; }
        var image = new PixelImage(W, H, rgba);

        PixelRect f1 = GImage.Fit(W, H, new PixelRect(0, 0, 100, 100));
        PixelRect f2 = GImage.Fit(W, H, new PixelRect(10, 20, 200, 30));
        Console.WriteLine($"fit-width={f1.X},{f1.Y},{f1.W},{f1.H}");
        Console.WriteLine($"fit-height={f2.X},{f2.Y},{f2.W},{f2.H}");

        var nullDriver = new NullDriver(80, 25);
        RecordingPixelDriver? recorder = pixelMode ? new RecordingPixelDriver(nullDriver) : null;
        IDriver driver = recorder ?? (IDriver)nullDriver;
        driver.Initialize();
        TDisplay.driver = driver;
        TScreen.ScreenBuffer = driver.AllocateScreenBuffer();

        var app = new ConsumerApp();
        var win = new GWindow(new TRect(2, 1, 62, 21), "Preview", Views.wnNoNumber);
        win.View.UnavailableText = "NO-PIXELS-FALLBACK";
        var gimage = new GImage(image, GraphicsImageFilter.Linear);
        win.Insert(gimage);
        app.DeskTop!.Insert(win);

        // One pass of the application loop: synchronization runs right before the driver presents.
        TEvent ev = default;
        app.GetEvent(ref ev);

        Console.WriteLine("pixel-capability=" + (TDisplay.driver is IPixelGraphicsDriver));
        Console.WriteLine("has-layer=" + win.View.HasGraphicsLayer);

        if (recorder == null)
        {
            var sb = new System.Text.StringBuilder();
            ScreenBuffer screen = TScreen.ScreenBuffer!;
            for (uint y = 0; y < screen.Height; y++)
                for (uint x = 0; x < screen.Width; x++) sb.Append(screen.GetChar(x, y).Character);
            Console.WriteLine("fallback-on-screen=" + sb.ToString().Contains("NO-PIXELS-FALLBACK"));
            win.Close();
            app.ShutDown();
            Console.WriteLine("text-done");
            return 0;
        }

        Console.WriteLine("layers=" + recorder.Layers.Count);
        var (owner, clip, prims, imgs) = recorder.Layers.Single();
        Console.WriteLine("owner-is-view=" + ReferenceEquals(owner, win.View));
        Console.WriteLine("primitives=" + prims.Length);
        GraphicsPrimitive p = prims[0];
        Console.WriteLine($"kind={p.Kind} index={p.ImageIndex} filter={p.Filter} opacity={p.Rgba & 0xFF}");
        Console.WriteLine($"images={imgs.Length} same-instance={ReferenceEquals(imgs[0], image)} size={imgs[0].Width}x{imgs[0].Height}");
        TPoint g = win.View.MakeGlobal(new TPoint(0, 0));
        var viewPixels = new PixelRect(g.x * recorder.CellPixelWidth, g.y * recorder.CellPixelHeight,
            win.View.size.x * recorder.CellPixelWidth, win.View.size.y * recorder.CellPixelHeight);
        Console.WriteLine($"view-pixels={viewPixels.X},{viewPixels.Y},{viewPixels.W},{viewPixels.H}");
        Console.WriteLine($"image-rect={p.X1},{p.Y1},{p.X2},{p.Y2}");
        PixelRect expected = GImage.Fit(W, H, viewPixels);
        Console.WriteLine("matches-fit=" + (p.X1 == expected.X && p.Y1 == expected.Y && p.X2 - p.X1 == expected.W && p.Y2 - p.Y1 == expected.H));
        Console.WriteLine("clip=" + string.Join(";", clip.Select(r => $"{r.X},{r.Y},{r.W},{r.H}")));

        // New content: a new immutable instance, then redraw.
        var replacement = new PixelImage(2, 2, new byte[16]);
        gimage.Image = replacement;
        win.View.DrawView();
        app.GetEvent(ref ev);
        Console.WriteLine("replaced-image-submitted=" + ReferenceEquals(recorder.Layers.Single().Images[0], replacement));

        win.Close();
        Console.WriteLine($"after-close layers={recorder.Layers.Count} has-layer={win.View.HasGraphicsLayer}");
        app.ShutDown();
        Console.WriteLine("pixel-done");
        return 0;

        sealed class ConsumerApp : TApplication { }

        // A driver decorator that adds the pixel capability and records the layers: what an SDL driver would draw.
        sealed class RecordingPixelDriver(IDriver inner) : IDriver, IPixelGraphicsDriver
        {
            public readonly List<(object Owner, PixelRect[] Clip, GraphicsPrimitive[] Primitives, PixelImage[] Images)> Layers = new();
            public int CellPixelWidth => 8;
            public int CellPixelHeight => 16;
            public void SetGraphicsLayer(object owner, ReadOnlySpan<PixelRect> clip, ReadOnlySpan<GraphicsPrimitive> primitives,
                ReadOnlySpan<PixelImage> images = default)
            {
                ArgumentNullException.ThrowIfNull(owner);
                Layers.RemoveAll(l => ReferenceEquals(l.Owner, owner));
                Layers.Add((owner, clip.ToArray(), primitives.ToArray(), images.ToArray()));
            }
            public void RemoveGraphicsLayer(object owner)
            {
                ArgumentNullException.ThrowIfNull(owner);
                Layers.RemoveAll(l => ReferenceEquals(l.Owner, owner));
            }
            public void Initialize() => inner.Initialize();
            public void Suspend() => inner.Suspend();
            public void Resume() => inner.Resume();
            public void Shutdown() => inner.Shutdown();
            public ushort GetCols() => inner.GetCols();
            public ushort GetRows() => inner.GetRows();
            public TDisplay.SM GetScreenMode() => inner.GetScreenMode();
            public void SetScreenMode(TDisplay.SM mode) => inner.SetScreenMode(mode);
            public ScreenBuffer AllocateScreenBuffer() => inner.AllocateScreenBuffer();
            public void ClearScreen(ushort cols, ushort rows) => inner.ClearScreen(cols, rows);
            public ushort GetCursorType() => inner.GetCursorType();
            public void SetCursorType(ushort cursorType) => inner.SetCursorType(cursorType);
            public void SetCaretPosition(int x, int y) => inner.SetCaretPosition(x, y);
            public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf) => inner.WriteBuf(x, y, w, h, buf);
            public void MakeBeep() => inner.MakeBeep();
            public void PumpMessages() => inner.PumpMessages();
            public bool ReadKeyEvent(out TEvent ev) => inner.ReadKeyEvent(out ev);
            public bool SupportsMouse => inner.SupportsMouse;
            public bool SupportsTrueColor => inner.SupportsTrueColor;
            public bool SupportsGraphics => true;
            public KeyboardCapabilities KeyboardCapabilities => inner.KeyboardCapabilities;
        }
        """;

    // References both packages; reflection only, so no SDL window or native library is touched.
    private const string SdlProgram = """
        using TSharpVision.Drivers;
        using TSharpVision.Drivers.SDL;
        using TSharpVision.Drivers.SDL.Gpu;
        using TSharpVision.Graphics;

        Console.WriteLine("SDLGpuDriver-pixel=" + typeof(IPixelGraphicsDriver).IsAssignableFrom(typeof(SDLGpuDriver)));
        Console.WriteLine("SDLDriver-pixel=" + typeof(IPixelGraphicsDriver).IsAssignableFrom(typeof(SDLDriver)));
        Console.WriteLine("gwindow=" + typeof(GWindow).FullName);
        """;

    private static string Run(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment.Remove("MSBuildExtensionsPath");
        psi.Environment.Remove("MSBUILD_EXE_PATH");
        psi.Environment.Remove("MSBuildSDKsPath");
        // A parent build's artifacts redirection must not leak into the packs or the consumers.
        psi.Environment.Remove("UseArtifactsOutput");
        psi.Environment.Remove("ArtifactsPath");
        // The consumers choose no driver by configuration; keep a developer's override out of them.
        psi.Environment.Remove("TSHARPVISION_DRIVER");

        using var process = Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEndAsync();
        string stdout = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(10)), "dotnet " + string.Join(' ', args) + " timed out.");
        string output = stdout + stderr.Result;
        Assert.True(process.ExitCode == 0, $"dotnet {string.Join(' ', args)} failed ({process.ExitCode}):\n{output}");
        return output;
    }
}
