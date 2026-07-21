// Glyph vertex shader — passes position, UV, and color to the fragment stage.
// TEXCOORD0 → location 0 (float2 position)
// TEXCOORD1 → location 1 (float2 UV)
// TEXCOORD2 → location 2 (float4 color, Ubyte4Norm [0,1])
struct VsIn  { float2 Position : TEXCOORD0; float2 UV : TEXCOORD1; float4 Color : TEXCOORD2; };
struct VsOut { float4 Position : SV_Position; float2 UV : TEXCOORD0; float4 Color : TEXCOORD1; };
VsOut main(VsIn i) {
    VsOut o;
    o.Position = float4(i.Position, 0.0, 1.0);
    o.UV       = i.UV;
    o.Color    = i.Color;
    return o;
}
