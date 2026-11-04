#ifndef PSX_COMMON_INCLUDED
#define PSX_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// ---------------------------------------------------------------------------
// Global settings, written every frame by PSXSettings.cs
//   _PSX_Params    : x,y = virtual resolution, z = color levels per channel, w = dither strength
//   _PSX_PostFlags : x   = pixelate inside the post pass (1) or rely on URP Render Scale (0)
// If PSXSettings is missing (all zeros) the defaults below are used.
// ---------------------------------------------------------------------------
float4 _PSX_Params;
float4 _PSX_PostFlags;

float2 PSX_Resolution()     { return _PSX_Params.x > 0.5 ? _PSX_Params.xy : float2(320.0, 240.0); }
float  PSX_Levels()         { return _PSX_Params.x > 0.5 ? _PSX_Params.z  : 32.0; }
float  PSX_DitherStrength() { return _PSX_Params.x > 0.5 ? _PSX_Params.w  : 1.0; }

// ---------------------------------------------------------------------------
// Vertex snapping: round clip-space XY to a virtual pixel grid.
// NDC spans 2 units across the screen, so the grid has (res / 2) cells per unit.
// ---------------------------------------------------------------------------
float4 PSX_SnapClip(float4 posCS)
{
    // Leave vertices at/behind the camera alone, snapping them looks broken.
    if (posCS.w > 1e-4)
    {
        float2 grid = PSX_Resolution() * 0.5;
        float2 ndc  = posCS.xy / posCS.w;
        ndc = round(ndc * grid) / grid;
        posCS.xy = ndc * posCS.w;
    }
    return posCS;
}

// ---------------------------------------------------------------------------
// The PS1 GPU's 4x4 ordered dither table (values are in 8-bit color units).
// ---------------------------------------------------------------------------
static const float kPSXDither[16] =
{
    -4.0,  0.0, -3.0,  1.0,
     2.0, -2.0,  3.0, -1.0,
    -3.0,  1.0, -4.0,  0.0,
     3.0, -1.0,  2.0, -2.0
};

// Self-contained sRGB <-> linear helpers (so we don't depend on a name from Unity's library).
float3 PSX_LinearToGamma(float3 c)
{
    c = saturate(c);
    float3 lo = c * 12.92;
    float3 hi = 1.055 * pow(c, 1.0 / 2.4) - 0.055;
    return lerp(hi, lo, step(c, 0.0031308));
}

float3 PSX_GammaToLinear(float3 c)
{
    float3 lo = c / 12.92;
    float3 hi = pow((c + 0.055) / 1.055, 2.4);
    return lerp(hi, lo, step(c, 0.04045));
}

// Quantize to N levels per channel (32 = PS1's 15-bit color) with ordered dithering.
// Works in gamma space so the banding looks like the original hardware.
float3 PSX_QuantizeDither(float3 col, float2 pixel)
{
    float levels   = PSX_Levels();
    float strength = PSX_DitherStrength();

    uint2 p = uint2(pixel) & 3u;
    // 8-bit table value -> fraction of one quantization step (-0.5 .. +0.375)
    float d = kPSXDither[p.y * 4u + p.x] * (1.0 / 8.0) * strength;

    #ifndef UNITY_COLORSPACE_GAMMA
        float3 c = PSX_LinearToGamma(col);
    #else
        float3 c = saturate(col);
    #endif

    float steps = max(levels - 1.0, 1.0);
    c = saturate(floor(c * steps + 0.5 + d) / steps);

    #ifndef UNITY_COLORSPACE_GAMMA
        c = PSX_GammaToLinear(c);
    #endif
    return c;
}

#endif // PSX_COMMON_INCLUDED
