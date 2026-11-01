// Pixel graphics vertex shader (TerminalGpuPipeline.Graphics.cs): one instance per Line, FillRect or Image
// primitive, no vertex buffer for the corners. Each instance is a quad over its device-pixel bounding box,
// generated from SV_VertexID (two triangles, 6 vertices):
//   id 0..5 -> corner (0,0) (1,0) (0,1) (1,0) (1,1) (0,1) of [Quad.x, Quad.z) x [Quad.y, Quad.w)
// Box edges are integer device pixels, so the rasteriser covers exactly the box's pixels; the fragment shaders
// decide coverage inside it. Per-instance data passes through unchanged (nointerpolation).
//
// Instance input (GpuGraphicsInstance, 32 bytes, instance-rate vertex buffer slot 0):
//   TEXCOORD0 Quad   short4 -> int4   device box x0, y0, x1, y1 (half-open, inside the grid)
//   TEXCOORD1 P      int4             line: ordered logical end points; image: device dest x0, y0, width, height
//   TEXCOORD2 Color  ubyte4 norm      straight-alpha colour (image: white with the opacity in alpha)
//   TEXCOORD3 Flags  uint             kind / filter bits, see the fragment shaders
// Vertex uniform buffer slot 0 = register(b0, space1) (SDL_PushGPUVertexUniformData slot 0).
cbuffer Grid : register(b0, space1)
{
    float4 G;   // 2 / grid width, 2 / grid height (device pixels), 0, 0
};

struct VsIn
{
    int4   Quad  : TEXCOORD0;
    int4   P     : TEXCOORD1;
    float4 Color : TEXCOORD2;
    uint   Flags : TEXCOORD3;
};

struct VsOut
{
    nointerpolation int4   P     : TEXCOORD0;
    nointerpolation float4 Color : TEXCOORD1;
    nointerpolation uint   Flags : TEXCOORD2;
    float4 Position : SV_Position;
};

VsOut main(VsIn i, uint id : SV_VertexID)
{
    // Corner bits: x = 1 for ids 1, 3, 4; y = 1 for ids 2, 4, 5.
    uint cx = (0x1Au >> id) & 1u;
    uint cy = (0x34u >> id) & 1u;
    float2 px = float2(cx != 0u ? i.Quad.z : i.Quad.x, cy != 0u ? i.Quad.w : i.Quad.y);
    VsOut o;
    o.P        = i.P;
    o.Color    = i.Color;
    o.Flags    = i.Flags;
    o.Position = float4(px.x * G.x - 1.0, 1.0 - px.y * G.y, 0.0, 1.0);
    return o;
}
