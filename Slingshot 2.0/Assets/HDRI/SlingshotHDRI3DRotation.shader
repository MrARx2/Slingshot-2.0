Shader "Slingshot/Skybox HDRI 3D Rotation"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("HDRI", 2D) = "grey" {}
        [HideInInspector] _MainTex_HDR ("HDR Decode", Vector) = (1,1,0,0)
        _Tint ("Tint", Color) = (1,1,1,1)
        _Exposure ("Exposure", Range(0,8)) = 1
        _RotationXYZ ("Pitch Yaw Roll", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_HDR;
            float4 _Tint;
            float _Exposure;
            float4 _RotationXYZ;

            struct Attributes
            {
                float4 vertex : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            float3 RotateX(float3 value, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return float3(value.x, c * value.y - s * value.z, s * value.y + c * value.z);
            }

            float3 RotateY(float3 value, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return float3(c * value.x + s * value.z, value.y, -s * value.x + c * value.z);
            }

            float3 RotateZ(float3 value, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return float3(c * value.x - s * value.y, s * value.x + c * value.y, value.z);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.vertex);
                output.direction = input.vertex.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                const float PI = 3.14159265359;
                float3 direction = normalize(input.direction);
                float3 radiansXYZ = radians(-_RotationXYZ.xyz);
                direction = RotateY(direction, radiansXYZ.y);
                direction = RotateX(direction, radiansXYZ.x);
                direction = RotateZ(direction, radiansXYZ.z);

                float2 uv;
                uv.x = atan2(direction.x, direction.z) / (2.0 * PI) + 0.5;
                uv.y = asin(clamp(direction.y, -1.0, 1.0)) / PI + 0.5;

                half4 encoded = tex2D(_MainTex, uv);
                half3 color = DecodeHDR(encoded, _MainTex_HDR);
                return half4(color * _Tint.rgb * _Exposure, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
