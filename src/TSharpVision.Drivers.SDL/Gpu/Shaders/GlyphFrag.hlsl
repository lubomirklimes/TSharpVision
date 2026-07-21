// Glyph pixel shader — samples R8 atlas for glyph alpha, outputs fg color x alpha.
// Fragment sampler slot 0 (register t0/s0, space2) per SDL_GPU HLSL conventions.
Texture2D<float> Atlas    : register(t0, space2);
SamplerState     AtlasSmp : register(s0, space2);
struct PsIn { float2 UV : TEXCOORD0; float4 Color : TEXCOORD1; };
float4 main(PsIn i) : SV_Target {
    float a = Atlas.Sample(AtlasSmp, i.UV).r;
    return float4(i.Color.rgb, a);
}
