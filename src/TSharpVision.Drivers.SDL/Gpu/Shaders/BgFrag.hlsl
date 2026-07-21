// Background pixel shader — solid fill.
struct PsIn { float4 Color : TEXCOORD0; };
float4 main(PsIn i) : SV_Target { return i.Color; }
