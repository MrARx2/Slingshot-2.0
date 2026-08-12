Shader "Slingshot/Track Guide Emissive"
{
    Properties
    {
        [HDR] _BaseColor ("Guide Color", Color) = (0.42, 2.1, 2.45, 1)
        _Brightness ("Brightness", Range(0, 8)) = 1
        _Opacity ("Opacity", Range(0, 1)) = 0.92
        _PulseStrength ("Pulse Strength", Range(0, 1)) = 0
        _PulseSpeed ("Pulse Speed", Range(0, 12)) = 2
        _PulseScale ("Pulse Scale", Range(0.01, 2)) = 0.15
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "RenderType" = "Transparent" }
        Pass
        {
            Name "Guide"
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Brightness;
                half _Opacity;
                half _PulseStrength;
                half _PulseSpeed;
                half _PulseScale;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half pulse = 0.5h + 0.5h * sin(
                    (input.positionWS.x + input.positionWS.y + input.positionWS.z) * _PulseScale
                    - _Time.y * _PulseSpeed);
                half pulseGain = lerp(1.0h, 0.55h + pulse * 0.9h, _PulseStrength);
                return half4(_BaseColor.rgb * _Brightness * pulseGain,
                    _BaseColor.a * _Opacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
