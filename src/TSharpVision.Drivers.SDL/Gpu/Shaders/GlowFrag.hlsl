// Glow post-process fragment shader: a bounded phosphor halo around bright pixels.
//   bright(c) = c * smoothstep(ThresholdLow, ThresholdHigh, luma601(c))
//   halo      = max(sum_i w_i * bright(src(p + round(tap_i * Radius))) - bright(src(p)), 0)
//   out       = saturate(src + Strength * halo)
// A pixel only gains light from brighter neighbours within Radius device pixels, so uniform areas
// and everything farther than the tap reach from a bright pixel stay exactly unchanged, and no
// pixel gets darker. Taps sample texel centres (no filtering); the clamp sampler repeats edges.
// The CPU reference (GpuGlow.ApplyReference) implements the same math with the same tap table.
//
// Fragment resources per SDL_GPU conventions: sampler slot 0 = register(t0/s0, space2),
// uniform buffer slot 0 = register(b0, space3) (SDL_PushGPUFragmentUniformData slot 0).
Texture2D<float4> Source    : register(t0, space2);
SamplerState      SourceSmp : register(s0, space2);

cbuffer Params : register(b0, space3)
{
    float4 V0;  // viewport width, height, 1/width, 1/height (device pixels)
    float4 V1;  // density, strength, radius (device px), threshold low
    float4 V2;  // threshold high, 0, 0, 0
    float4 V3;  // unused
};

struct PsIn { float4 Position : SV_Position; float2 UV : TEXCOORD0; };

// Unit-disc offsets (x, y) and normalised weights; identical to GpuGlow.Taps.
static const float3 Taps[33] =
{
    float3( 0.000000,  0.000000, 0.080454),
    float3( 0.350000,  0.000000, 0.062972),
    float3( 0.247487,  0.247487, 0.062972),
    float3( 0.000000,  0.350000, 0.062972),
    float3(-0.247487,  0.247487, 0.062972),
    float3(-0.350000,  0.000000, 0.062972),
    float3(-0.247487, -0.247487, 0.062972),
    float3( 0.000000, -0.350000, 0.062972),
    float3( 0.247487, -0.247487, 0.062972),
    float3( 0.646716,  0.267878, 0.030195),
    float3( 0.267878,  0.646716, 0.030195),
    float3(-0.267878,  0.646716, 0.030195),
    float3(-0.646716,  0.267878, 0.030195),
    float3(-0.646716, -0.267878, 0.030195),
    float3(-0.267878, -0.646716, 0.030195),
    float3( 0.267878, -0.646716, 0.030195),
    float3( 0.646716, -0.267878, 0.030195),
    float3( 1.000000,  0.000000, 0.010888),
    float3( 0.923880,  0.382683, 0.010888),
    float3( 0.707107,  0.707107, 0.010888),
    float3( 0.382683,  0.923880, 0.010888),
    float3( 0.000000,  1.000000, 0.010888),
    float3(-0.382683,  0.923880, 0.010888),
    float3(-0.707107,  0.707107, 0.010888),
    float3(-0.923880,  0.382683, 0.010888),
    float3(-1.000000,  0.000000, 0.010888),
    float3(-0.923880, -0.382683, 0.010888),
    float3(-0.707107, -0.707107, 0.010888),
    float3(-0.382683, -0.923880, 0.010888),
    float3( 0.000000, -1.000000, 0.010888),
    float3( 0.382683, -0.923880, 0.010888),
    float3( 0.707107, -0.707107, 0.010888),
    float3( 0.923880, -0.382683, 0.010888),
};

float3 Bright(float3 c)
{
    float y = dot(c, float3(0.299, 0.587, 0.114));
    return c * smoothstep(V1.w, V2.x, y);
}

float4 main(PsIn i) : SV_Target
{
    // Pixel index from the interpolated UV (= (pix + 0.5) / size at the pixel centre). The shader
    // must read TEXCOORD0: on D3D12 the pixel-shader input signature has to start with the vertex
    // shader's TEXCOORD0 (spirv-cross emits it before SV_Position), or pipeline creation fails.
    float2 pix = floor(i.UV * V0.xy);
    float4 src = Source.Sample(SourceSmp, (pix + 0.5) * V0.zw);
    float3 blur = float3(0.0, 0.0, 0.0);
    [unroll]
    for (int t = 0; t < 33; t++)
    {
        float2 o = floor(Taps[t].xy * V1.z + 0.5);
        blur += Taps[t].z * Bright(Source.Sample(SourceSmp, (pix + o + 0.5) * V0.zw).rgb);
    }
    float3 halo = max(blur - Bright(src.rgb), float3(0.0, 0.0, 0.0));
    return float4(saturate(src.rgb + V1.y * halo), src.a);
}
