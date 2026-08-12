Shader "Slingshot/Race Gate Surface"
{
    Properties
    {
        [HDR] _BaseColor ("Primary Color", Color) = (0.08, 0.8, 1.5, 1)
        [HDR] _SecondaryColor ("Checker Color", Color) = (0.015, 0.02, 0.025, 1)
        _Brightness ("Emission Brightness", Range(0, 8)) = 1
        _CheckerMix ("Checker Pattern", Range(0, 1)) = 0
        _CheckerScale ("Checker Scale", Vector) = (8, 4, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+20" "RenderType" = "Opaque" }
        Pass
        {
            Name "RaceGate"
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _SecondaryColor;
                half _Brightness;
                half _CheckerMix;
                float4 _CheckerScale;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 cell = floor(input.uv * max(_CheckerScale.xy, float2(1.0, 1.0)));
                half checker = fmod(cell.x + cell.y, 2.0);
                half3 color = lerp(_BaseColor.rgb, _SecondaryColor.rgb, checker * _CheckerMix);
                return half4(color * _Brightness, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
