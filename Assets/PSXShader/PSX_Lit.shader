Shader "PSX/Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Brightness("Brightness", Range(0, 3)) = 1

        [Header(Vertex Color)]
        _VertexColorStrength("Vertex Color Strength", Range(0, 1)) = 1

        [Header(Emission)]
        [NoScaleOffset] _EmissionMap("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)

        [Header(PSX)]
        [Toggle] _VertexSnap("Vertex Snapping", Float) = 1
        _AffineStrength("Affine Texture Warp", Range(0, 1)) = 1
        [Toggle(_FLAT_SHADING)] _FlatShading("Flat Shading (use hard-edge normals)", Float) = 0
        [Toggle(_MIPMAPS)] _Mipmaps("Use Mipmaps (less shimmer)", Float) = 0

        [Header(Surface)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "PSXCommon.hlsl"

        TEXTURE2D(_BaseMap);
        TEXTURE2D(_EmissionMap);
        SamplerState psx_point_repeat_sampler;

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half4  _EmissionColor;
            half   _Cutoff;
            half   _Brightness;
            half   _VertexColorStrength;
            half   _VertexSnap;
            half   _AffineStrength;
        CBUFFER_END

        float4 PSX_Snap(float4 posCS)
        {
            return _VertexSnap > 0.5 ? PSX_SnapClip(posCS) : posCS;
        }

        // Affine trick: interpolating (uv * w) and w with perspective-correct hardware
        // interpolation and dividing in the fragment shader gives screen-space-linear UVs
        float3 PSX_PackAffine(float2 uv, float w)
        {
            return float3(uv * w, w);
        }

        float2 PSX_ResolveUV(float2 uvPerspective, float3 uvAffinePacked)
        {
            float2 uvAffine = uvAffinePacked.xy / uvAffinePacked.z;
            return lerp(uvPerspective, uvAffine, _AffineStrength);
        }

        half4 PSX_SampleBase(float2 uv)
        {
            #if defined(_MIPMAPS)
                return SAMPLE_TEXTURE2D(_BaseMap, psx_point_repeat_sampler, uv);
            #else
                return SAMPLE_TEXTURE2D_LOD(_BaseMap, psx_point_repeat_sampler, uv, 0);
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local _FLAT_SHADING
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _MIPMAPS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #ifdef _FLAT_SHADING
                #define PSX_FLAT nointerpolation
            #else
                #define PSX_FLAT
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 uvAffine   : TEXCOORD1;
                PSX_FLAT half3 lighting : TEXCOORD2;
                half4  color      : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float4 positionCS = TransformWorldToHClip(positionWS);
                float3 normalWS   = TransformObjectToWorldNormal(v.normalOS);

                float2 uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.uv        = uv;
                o.uvAffine  = PSX_PackAffine(uv, positionCS.w); // use the unsnapped w
                o.color     = v.color;
                o.fogFactor = ComputeFogFactor(positionCS.z);

                // Gouraud lighting, evaluated per vertex like the original hardware
                Light mainLight = GetMainLight();
                half3 diffuse = mainLight.color * saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 extra   = VertexLighting(positionWS, normalWS);   // zero unless Additional Lights = Per Vertex
                o.lighting = diffuse + ambient + extra;

                o.positionCS = PSX_Snap(positionCS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 uv = PSX_ResolveUV(i.uv, i.uvAffine);
                half4 tex = PSX_SampleBase(uv);

                half3 vcol   = lerp(half3(1, 1, 1), i.color.rgb, _VertexColorStrength);
                half3 albedo = tex.rgb * _BaseColor.rgb * vcol;

                #if defined(_ALPHATEST_ON)
                    clip(tex.a * _BaseColor.a - _Cutoff);
                #endif

                half3 emission = SAMPLE_TEXTURE2D_LOD(_EmissionMap, psx_point_repeat_sampler, uv, 0).rgb
                                 * _EmissionColor.rgb;

                half3 col = albedo * i.lighting * _Brightness + emission;
                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _MIPMAPS
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 uvAffine   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes v)
            {
                DepthVaryings o = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float4 positionCS = TransformWorldToHClip(positionWS);

                float2 uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.uv       = uv;
                o.uvAffine = PSX_PackAffine(uv, positionCS.w);
                o.positionCS = PSX_Snap(positionCS);
                return o;
            }

            half DepthFrag(DepthVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                #if defined(_ALPHATEST_ON)
                    float2 uv = PSX_ResolveUV(i.uv, i.uvAffine);
                    clip(PSX_SampleBase(uv).a * _BaseColor.a - _Cutoff);
                #endif
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
