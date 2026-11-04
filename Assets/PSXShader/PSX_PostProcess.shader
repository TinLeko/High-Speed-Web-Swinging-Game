// Use with URP's Full Screen Pass Renderer Feature (Injection Point: After Rendering Post Processing,
// Requirements: Fetch Color Buffer). Everything is driven by the globals PSXSettings.cs sets.
Shader "PSX/PostProcess"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Blend Off
        Cull Off

        Pass
        {
            Name "PSXPost"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "PSXCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 uv = i.texcoord;
                float2 pixel;

                if (_PSX_PostFlags.x > 0.5)
                {
                    // Snap the whole image to the virtual resolution (point sampled).
                    float2 res = PSX_Resolution();
                    pixel = floor(uv * res);
                    uv = (pixel + 0.5) / res;
                }
                else
                {
                    // Rely on URP Render Scale for pixelation; dither on actual render pixels.
                    pixel = floor(i.positionCS.xy);
                }

                float3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
                col = PSX_QuantizeDither(col, pixel);
                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
