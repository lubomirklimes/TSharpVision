// Curvature post-process fragment shader: a bounded CRT barrel warp, as a destination -> source
// mapping (each output pixel samples the grid texture at f(dst)):
//   c   = 2 * uv - 1                          centred, normalised (-1..1 across the viewport)
//   e   = c * Aspect                          Aspect = (w, h) / max(w, h): physical, round warp
//   src = c * (1 + K * dot(e, e))             factor >= 1, so the whole grid stays visible
//   suv = src * 0.5 + 0.5                     source UV; sp = suv * (w, h) in source pixels
//   d   = min(sp.x, w - sp.x, sp.y, h - sp.y) signed distance to the image edge (source px)
//   out = bilinear(Source, suv).rgb * saturate(d / Edge + 0.5), alpha 1
// Outside the image (corners, edge bands) is black with an antialiased edge of Edge px.
// GpuCurvature.MapToSource implements exactly this mapping on the CPU (pointer hit-testing) and
// GpuCurvature.ApplyReference the whole shader (tests).
//
// Fragment resources per SDL_GPU conventions: sampler slot 0 = register(t0/s0, space2) (linear,
// clamp to edge), uniform buffer slot 0 = register(b0, space3).
Texture2D<float4> Source    : register(t0, space2);
SamplerState      SourceSmp : register(s0, space2);

cbuffer Params : register(b0, space3)
{
    float4 V0;  // viewport width, height, 1/width, 1/height (device pixels)
    float4 V1;  // density, K, aspect x, aspect y
    float4 V2;  // edge antialias width (source px), 0, 0, 0
    float4 V3;  // unused
};

struct PsIn { float4 Position : SV_Position; float2 UV : TEXCOORD0; };

float4 main(PsIn i) : SV_Target
{
    // The shader must read TEXCOORD0 (D3D12 input-signature rule, see GlowFrag.hlsl).
    float2 c   = i.UV * 2.0 - 1.0;
    float2 e   = c * V1.zw;
    float  g   = 1.0 + V1.y * dot(e, e);
    float2 suv = c * g * 0.5 + 0.5;
    float2 sp  = suv * V0.xy;
    float  d   = min(min(sp.x, V0.x - sp.x), min(sp.y, V0.y - sp.y));
    float  a   = saturate(d / V2.x + 0.5);
    float3 rgb = Source.Sample(SourceSmp, suv).rgb;
    return float4(rgb * a, 1.0);
}
