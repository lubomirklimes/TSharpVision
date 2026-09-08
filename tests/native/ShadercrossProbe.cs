using System.Diagnostics;
using System.Runtime.InteropServices;
using TSharpVision.Drivers.SDL.Gpu;

try
{
    // Standalone Windows native deployment probe. Invoked by ShadercrossDeploymentSmoke.ps1.
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This probe asserts Windows companion loading.");
    Console.WriteLine($"SDL version: {SDL3.SDL.GetVersion()}");
    if (!SDL3.TTF.Init()) throw new Exception(SDL3.SDL.GetError());
    try
    {
        if (!SDL3.ShaderCross.Init()) throw new Exception(SDL3.SDL.GetError());
        try
        {
            using var stream = typeof(SDLGpuDriver).Assembly.GetManifestResourceStream("TSharpVision.Drivers.SDL.Gpu.Shaders.BgVert.spv")!;
            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var info = new SDL3.ShaderCross.SPIRVInfo
                {
                    ByteCode = pin.AddrOfPinnedObject(), ByteCodeSize = (nuint)bytes.Length,
                    ManagedEntrypoint = "main", ShaderStage = SDL3.ShaderCross.ShaderStage.Vertex
                };
                var dxil = SDL3.ShaderCross.CompileDXILFromSPIRV(in info, out var size);
                if (dxil == IntPtr.Zero) throw new Exception($"DXIL compilation failed: {SDL3.SDL.GetError()}");
                try { if (size == 0) throw new Exception("Empty DXIL output."); }
                finally { SDL3.SDL.Free(dxil); }
            }
            finally { pin.Free(); }

            // DXIL compilation above does not necessarily load the optional validator.
            // Verify that standard probing also resolves that deployed companion.
            var validator = NativeLibrary.Load("dxil", typeof(SDL3.ShaderCross).Assembly, null);
            string[] required = ["SDL3.dll", "SDL3_ttf.dll", "SDL3_shadercross.dll", "spirv-cross-c-shared.dll", "dxcompiler.dll", "dxil.dll"];
            var loaded = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().ToArray();
            foreach (string name in required)
            {
                var module = loaded.Single(m => string.Equals(m.ModuleName, name, StringComparison.OrdinalIgnoreCase));
                string relative = Path.GetRelativePath(AppContext.BaseDirectory, module.FileName);
                if (Path.IsPathRooted(relative) || relative.StartsWith(".."))
                    throw new Exception($"Native module escaped deployment: {module.FileName}");
                Console.WriteLine($"MODULE {relative}");
            }
            NativeLibrary.Free(validator);
        }
        finally { SDL3.ShaderCross.Quit(); }
    }
    finally { SDL3.TTF.Quit(); }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
