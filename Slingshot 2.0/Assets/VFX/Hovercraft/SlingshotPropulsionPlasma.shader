Shader "Slingshot/VFX/Propulsion Plasma"
{
    Properties
    {
        [MainTexture] _BaseMap("Plasma Detail", 2D) = "white" {}
        [HDR] _Tint("Core Tint", Color) = (0.45, 1.25, 1.8, 1)
        [HDR] _EdgeColor("Edge Tint", Color) = (0.55, 0.2, 1.25, 1)
        _Intensity("Intensity", Range(0, 12)) = 2
        _CoreSharpness("Core Sharpness", Range(1, 32)) = 10
        _HaloSharpness("Halo Sharpness", Range(0.25, 8)) = 2
        _FlowSpeed("Flow Speed", Range(-12, 12)) = 3
        _Turbulence("Turbulence", Range(0, 0.25)) = 0.04
        _TextureInfluence("Texture Influence", Range(0, 1)) = 0.72
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent+40"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }

        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "PropulsionPlasma"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                half4 _EdgeColor;
                half _Intensity;
                half _CoreSharpness;
                half _HaloSharpness;
                half _FlowSpeed;
                half _Turbulence;
                half _TextureInfluence;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv * 2.0 - 1.0;
                float endFade = saturate((1.0 - abs(centered.x)) * 4.5);
                endFade *= endFade;

                float phase = _Time.y * _FlowSpeed;
                float waveA = sin(centered.x * 15.0 - phase * 2.1);
                float waveB = sin(centered.x * 31.0 + phase * 1.35 + 1.7);
                float turbulence = (waveA * 0.65 + waveB * 0.35) * _Turbulence;
                float displacedY = centered.y + turbulence * (0.35 + endFade * 0.65);

                float axis = saturate(1.0 - abs(displacedY));
                float core = pow(axis, _CoreSharpness);
                float halo = pow(axis, _HaloSharpness);

                half3 detailSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half detail = max(detailSample.r, max(detailSample.g, detailSample.b));
                detail = lerp(1.0h, saturate(detail * 1.8h), _TextureInfluence);

                half energy = saturate((core + halo * 0.42) * endFade * detail);
                half3 plasma = _EdgeColor.rgb * halo * 0.42h + _Tint.rgb * core * 1.45h;
                plasma *= input.color.rgb * _Intensity * energy * input.color.a;
                return half4(plasma, energy);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
