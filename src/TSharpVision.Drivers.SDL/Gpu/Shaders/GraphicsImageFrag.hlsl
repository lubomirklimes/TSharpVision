// Pixel graphics fragment shader for Image instances (GraphicsVert.hlsl draws the visible part of the
// destination). P = device destination (x0, y0, W, H); the bound RGBA8 texture is the image (w x h texels,
// straight alpha). For the device pixel k = (px - x0, py - y0) inside the destination:
//   Flags & 1 == 0 (Nearest): t = ((2k + 1) * size) / (2 * dest) in integers, clamped; one Load. This is the
//                             CPU reference's integer form, so nearest is exact at every scale (no float ties).
//   Flags & 1 == 1 (Linear):  hardware bilinear at u = (k + 0.5) / dest (texel space (k + 0.5) * size / dest - 0.5)
//                             with the clamp-to-edge linear sampler; within +-2 per channel of the CPU float form.
// Output: texel rgb (times the instance colour, white) with alpha = texel alpha * opacity; the pipeline blends
// source-over. Intermediates fit int32: dest <= 32768 and size <= 16384 (enforced when instancing).
//
// Fragment resources per SDL_GPU conventions: sampler slot 0 = register(t0/s0, space2),
// uniform buffer slot 0 = register(b0, space3).
Texture2D<float4> Image    : register(t0, space2);
SamplerState      ImageSmp : register(s0, space2);

cbuffer Params : register(b0, space3)
{
    int4 Scale;   // logical cell width, logical cell height, device cell width, device cell height (unused)
    int4 Origin;  // viewport x, y in render-target pixels, 0, 0
};

struct PsIn
{
    nointerpolation int4   P     : TEXCOORD0;
    nointerpolation float4 Color : TEXCOORD1;
    nointerpolation uint   Flags : TEXCOORD2;
    float4 Position : SV_Position;
};

float4 main(PsIn i) : SV_Target
{
    int2 k = int2(floor(i.Position.xy)) - Origin.xy - i.P.xy;
    uint w, h;
    Image.GetDimensions(w, h);
    int2 size = int2(int(w), int(h));
    float4 c;
    if ((i.Flags & 1u) == 0u)
    {
        int2 t = clamp(((2 * k + 1) * size) / (2 * i.P.zw), int2(0, 0), size - 1);
        c = Image.Load(int3(t, 0));
    }
    else
    {
        c = Image.SampleLevel(ImageSmp, (float2(k) + 0.5) / float2(i.P.zw), 0.0);
    }
    return float4(c.rgb * i.Color.rgb, c.a * i.Color.a);
}
