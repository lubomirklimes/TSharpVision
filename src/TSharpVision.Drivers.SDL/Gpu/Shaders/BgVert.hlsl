// Background vertex shader.
// Inputs are already in clip space (pre-transformed by CPU).
// TEXCOORD0 → location 0 (float2 position)
// TEXCOORD1 → location 1 (float4 color, Ubyte4Norm [0,1])
struct VsIn  { float2 Position : TEXCOORD0; float4 Color : TEXCOORD1; };
struct VsOut { float4 Position : SV_Position; float4 Color : TEXCOORD0; };
VsOut main(VsIn i) {
    VsOut o;
    o.Position = float4(i.Position, 0.0, 1.0);
    o.Color    = i.Color;
    return o;
}
