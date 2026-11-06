using System.Numerics;
using System.Runtime.InteropServices;

namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// Per-frame input of a post-process uniform builder: the grid viewport (the offscreen texture
/// size) in device pixels and the active pixel density.
/// </summary>
internal readonly record struct GpuPostFrame(int ViewportWidth, int ViewportHeight, float Density);

/// <summary>
/// Fragment uniforms of a post-process effect: 64 bytes, four <c>float4</c>, pushed with
/// <c>SDL_PushGPUFragmentUniformData(slot 0)</c> and read in HLSL as
/// <c>cbuffer Params : register(b0, space3) { float4 V0; float4 V1; float4 V2; float4 V3; }</c>.
/// Convention: <see cref="V0"/> = viewport width, height, 1/width, 1/height (device pixels);
/// <see cref="V1"/>.X = density; everything else is effect-specific (see <see cref="Base"/>).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct GpuPostUniforms
{
    /// <summary>Size in bytes; also what is pushed to the GPU.</summary>
    public const int SizeInBytes = 64;

    public Vector4 V0;
    public Vector4 V1;
    public Vector4 V2;
    public Vector4 V3;

    /// <summary>The shared part: <see cref="V0"/> from the viewport and <see cref="V1"/>.X = density; the rest zero.</summary>
    public static GpuPostUniforms Base(in GpuPostFrame frame)
    {
        float w = Math.Max(1, frame.ViewportWidth), h = Math.Max(1, frame.ViewportHeight);
        return new GpuPostUniforms
        {
            V0 = new Vector4(w, h, 1f / w, 1f / h),
            V1 = new Vector4(frame.Density, 0f, 0f, 0f),
        };
    }
}

/// <summary>Builds an effect's uniforms for one frame. Must be pure and allocation-free.</summary>
internal delegate GpuPostUniforms GpuPostUniformBuilder(in GpuPostFrame frame);

/// <summary>
/// Data-only description of a post-process effect: its embedded fragment shader (an
/// <c>&lt;Name&gt;Frag.spv</c> resource, compiled from <c>Gpu/Shaders/&lt;Name&gt;Frag.hlsl</c>), how many
/// fragment samplers it declares (0 or 1: slot 0 is the offscreen grid texture with a linear,
/// clamp-to-edge sampler, HLSL <c>register(t0, space2)</c>/<c>register(s0, space2)</c>), and its
/// uniform builder. Every post shader is compiled with one fragment uniform buffer
/// (<c>register(b0, space3)</c>) and runs after <c>PostVert</c>, so its input is
/// <c>struct PsIn { float4 Position : SV_Position; float2 UV : TEXCOORD0; }</c>.
/// </summary>
internal sealed record GpuPostEffectSpec(
    string FragmentShaderResource,
    uint NumSamplers,
    GpuPostUniformBuilder BuildUniforms);

/// <summary>
/// The post-process effects the shader pipeline can draw. An effect for which
/// <see cref="GpuDisplayEffects.IsPostProcess"/> is true but which has no entry here renders as
/// <see cref="GpuDisplayEffect.Default"/> without error.
/// </summary>
internal static class GpuPostEffectCatalog
{
    /// <summary>Resource name prefix of the embedded shaders.</summary>
    internal const string ShaderResourcePrefix = "TSharpVision.Drivers.SDL.Gpu.Shaders.";

    private static readonly GpuPostEffectSpec Glow =
        new(ShaderResourcePrefix + "GlowFrag.spv", 1, GpuGlow.BuildUniforms);

    private static readonly GpuPostEffectSpec Curvature =
        new(ShaderResourcePrefix + "CurvatureFrag.spv", 1, GpuCurvature.BuildUniforms);

    private static readonly GpuPostEffectSpec ShadowMask =
        new(ShaderResourcePrefix + "ShadowMaskFrag.spv", 1, GpuShadowMask.BuildUniforms);

    /// <summary>The spec of <paramref name="effect"/>, when it is an implemented post-process effect.</summary>
    internal static bool TryGet(GpuDisplayEffect effect, out GpuPostEffectSpec spec)
    {
        GpuPostEffectSpec? found = effect switch
        {
            GpuDisplayEffect.Glow => Glow,
            GpuDisplayEffect.Curvature  when GpuCurvature.Available  => Curvature,
            GpuDisplayEffect.ShadowMask when GpuShadowMask.Available => ShadowMask,
            _ => null,
        };
        spec = found!;
        return found != null;
    }
}
