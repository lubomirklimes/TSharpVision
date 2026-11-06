// ShadowMask post-process fragment shader: a fine, neutral (luminance-only) CRT phosphor mask
// anchored to device pixels.
//   pix    = floor(uv * viewport)                  device pixel (viewport origin = device 0,0)
//   m      = pix / pitch  (integer division)       mask cell; pitch = max(1, round(density))
//   row    = m.y % 2, col = m.x % 3
//   dark   = col == (row == 0 ? 2 : 1)             3 x 2 tile, staggered dark gap:
//                                                    row 0: 1 1 D
//                                                    row 1: 1 D 1
//   out    = src.rgb * (dark ? 1 - Strength : 1)   same factor on every channel (no hue change)
// The source is read at its texel centre, so the image itself is never resampled or blurred.
// The CPU reference (GpuShadowMask.ApplyReference) implements the same math.
//
// Fragment resources per SDL_GPU conventions: sampler slot 0 = register(t0/s0, space2),
// uniform buffer slot 0 = register(b0, space3) (SDL_PushGPUFragmentUniformData slot 0).
Texture2D<float4> Source    : register(t0, space2);
SamplerState      SourceSmp : register(s0, space2);

cbuffer Params : register(b0, space3)
{
    float4 V0;  // viewport width, height, 1/width, 1/height (device pixels)
    float4 V1;  // density, strength, pitch (device px per mask cell), 0
    float4 V2;  // unused
    float4 V3;  // unused
};

struct PsIn { float4 Position : SV_Position; float2 UV : TEXCOORD0; };

float4 main(PsIn i) : SV_Target
{
    // Pixel index from the interpolated UV (= (pix + 0.5) / size at the pixel centre). The shader
    // must read TEXCOORD0 (D3D12 input signature, see GlowFrag.hlsl).
    float2 pix = floor(i.UV * V0.xy);
    float4 src = Source.Sample(SourceSmp, (pix + 0.5) * V0.zw);

    uint  pitch = max((uint)V1.z, 1u);
    uint2 m     = (uint2)pix / pitch;
    uint  row   = m.y % 2u;
    uint  col   = m.x % 3u;
    float f     = (col == (row == 0u ? 2u : 1u)) ? (1.0 - V1.y) : 1.0;
    return float4(src.rgb * f, src.a);
}
