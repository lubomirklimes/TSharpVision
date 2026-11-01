// Pixel graphics fragment shader for Line and FillRect instances (GraphicsVert.hlsl draws the box).
//   Flags & 3 == 0: FillRect, every pixel of the box.
//   Flags & 3 == 1: x-major line, P = (x1, y1, x2, y2) logical with x1 <= x2.
//   Flags & 3 == 2: y-major line, P = (x1, y1, x2, y2) logical with y1 <= y2.
// A line keeps a device pixel exactly when its logical pixel is on the line, with the integer formula every SDL
// backend shares (PixelGraphicsRasterizer is the CPU reference):
//   device d -> logical v = ((d + 1) * L + D - 1) / D - 1      (inverse of floor(v * D / L), d >= 0)
//   x-major: y(x) = y1 + floorDiv(2 (x - x1) dy + dx, 2 dx)    (dx == 0: the single pixel (x1, y1))
//   y-major: x(y) = x1 + floorDiv(2 (y - y1) dx + dy, 2 dy)
// so a logical pixel covers its whole device block (HiDPI) and CPU and GPU agree pixel for pixel. Intermediates
// fit int32 for extents up to GraphicsPrimitive.MaxLineExtent (lines beyond it are never instanced).
//
// Fragment uniform buffer slot 0 = register(b0, space3) (SDL_PushGPUFragmentUniformData slot 0).
cbuffer Params : register(b0, space3)
{
    int4 Scale;   // logical cell width, logical cell height, device cell width, device cell height
    int4 Origin;  // viewport x, y in render-target pixels, 0, 0
};

struct PsIn
{
    nointerpolation int4   P     : TEXCOORD0;
    nointerpolation float4 Color : TEXCOORD1;
    nointerpolation uint   Flags : TEXCOORD2;
    float4 Position : SV_Position;
};

// Division rounding toward negative infinity; d > 0.
int FloorDiv(int n, int d)
{
    return n >= 0 ? n / d : -((d - 1 - n) / d);
}

float4 main(PsIn i) : SV_Target
{
    uint kind = i.Flags & 3u;
    if (kind != 0u)
    {
        int2 d = int2(floor(i.Position.xy)) - Origin.xy;
        int lx = ((d.x + 1) * Scale.x + Scale.z - 1) / Scale.z - 1;
        int ly = ((d.y + 1) * Scale.y + Scale.w - 1) / Scale.w - 1;
        int ex = i.P.z - i.P.x, ey = i.P.w - i.P.y;
        if (kind == 1u)
        {
            if (lx < i.P.x || lx > i.P.z) discard;
            int y = ex == 0 ? i.P.y : i.P.y + FloorDiv(2 * (lx - i.P.x) * ey + ex, 2 * ex);
            if (ly != y) discard;
        }
        else
        {
            if (ly < i.P.y || ly > i.P.w) discard;
            int x = ey == 0 ? i.P.x : i.P.x + FloorDiv(2 * (ly - i.P.y) * ex + ey, 2 * ey);
            if (lx != x) discard;
        }
    }
    return i.Color;
}
