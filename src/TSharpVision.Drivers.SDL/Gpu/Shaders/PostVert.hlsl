// Post-process vertex shader: one full-screen triangle generated from SV_VertexID (no vertex
// buffer, no vertex input). Drawn with 3 vertices into the grid viewport; the triangle covers the
// whole viewport and the clip volume cuts it to the viewport rectangle.
//   id 0 -> clip (-1,  1), uv (0, 0)   top-left
//   id 1 -> clip ( 3,  1), uv (2, 0)
//   id 2 -> clip (-1, -3), uv (0, 2)
// UV (0,0) is the top-left of the offscreen grid texture, (1,1) its bottom-right.
// Every post fragment shader declares the same input: SV_Position + TEXCOORD0 (UV).
struct VsOut { float4 Position : SV_Position; float2 UV : TEXCOORD0; };
VsOut main(uint id : SV_VertexID) {
    VsOut o;
    float2 uv  = float2((id << 1) & 2, id & 2);
    o.Position = float4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, 0.0, 1.0);
    o.UV       = uv;
    return o;
}
