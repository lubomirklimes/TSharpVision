// Cluster pixel shader (TV13m) - samples the RGBA8 cluster atlas. Monochrome clusters are stored white with
// coverage in alpha and drawn with the foreground as vertex colour (a tint); colour clusters (emoji) keep their own
// colours because their vertex colour is white. Straight alpha; the pipeline blends source-over.
// Fragment sampler slot 0 (register t0/s0, space2) per SDL_GPU HLSL conventions.
Texture2D<float4> Atlas    : register(t0, space2);
SamplerState      AtlasSmp : register(s0, space2);
struct PsIn { float2 UV : TEXCOORD0; float4 Color : TEXCOORD1; };
float4 main(PsIn i) : SV_Target {
    float4 t = Atlas.Sample(AtlasSmp, i.UV);
    return float4(t.rgb * i.Color.rgb, t.a * i.Color.a);
}
