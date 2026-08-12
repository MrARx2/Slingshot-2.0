Shader "Slingshot/Craft Corona Discharge"
{
    Properties
    {
        // CraftFeedbackSystem owns the art-direction controls. Keeping the shader
        // plumbing hidden avoids a second, misleading set of runtime-overridden knobs.
        [HideInInspector] _MainColor ("Main Corona", Color) = (0.28, 1.55, 2.2, 1)
        [HideInInspector] _OverchargeColor ("Overcharge Corona", Color) = (0.95, 0.18, 1.55, 1)
        [HideInInspector] _SparkColor ("Electrical Sparks", Color) = (0.25, 1.8, 2.2, 1)
        [HideInInspector] _OuterColor ("Outer Fringe", Color) = (1.2, 0.18, 1.55, 1)
        [HideInInspector] _ChromaSeparation ("Chroma Separation", Range(0, 1)) = 0.38
        [HideInInspector] _Brightness ("Brightness", Range(0, 3)) = 1
        [HideInInspector] _Saturation ("Saturation", Range(0, 2)) = 1.08
        [HideInInspector] _EdgeTightness ("Edge Tightness", Range(0.5, 20)) = 7.5
        [HideInInspector] _BandWidth ("Spectral Band Width", Range(0.01, 0.5)) = 0.16
        [HideInInspector] _OutlineStrength ("Outline Strength", Range(0, 4)) = 1.4
        [HideInInspector] _OutlineCoverage ("Outline Coverage", Range(0, 1)) = 1
        [HideInInspector] _CoverageFade ("Coverage Fade", Range(0, 1)) = 0.45
        [HideInInspector] _SurfaceFill ("Whole Mesh Ionization", Range(0, 1)) = 0.045
        [HideInInspector] _LeadingEdgeBias ("Leading Edge Pressure", Range(0, 4)) = 1.35
        [HideInInspector] _RearSuppression ("Rear Suppression", Range(0, 1)) = 0.55
        [HideInInspector] _ShellExpansion ("Shell Expansion", Range(0, 0.2)) = 0.004
        [HideInInspector] _SurfaceBreakup ("Surface Breakup", Range(0, 1)) = 0.34
        [HideInInspector] _NoiseScale ("Noise Scale", Range(0.1, 20)) = 5.5
        [HideInInspector] _NoiseContrast ("Noise Contrast", Range(0.25, 8)) = 2.2
        [HideInInspector] _CrawlSpeed ("Crawl Speed", Range(0, 20)) = 5.5
        [HideInInspector] _FlickerAmount ("Flicker Amount", Range(0, 1)) = 0.12
        [HideInInspector] _FlickerSpeed ("Flicker Speed", Range(0, 30)) = 13
        [HideInInspector] _FlowStrength ("Flow Strength", Range(0, 2)) = 0.45
        [HideInInspector] _FlowDensity ("Flow Density", Range(0.1, 12)) = 3.4

        [HideInInspector] _Intensity ("Intensity", Range(0, 4)) = 0
        [HideInInspector] _Speed01 ("Speed", Range(0, 1)) = 0
        [HideInInspector] _BoostBlend ("Overcharge", Range(0, 1)) = 0
        [HideInInspector] _Ignition ("Ignition", Range(0, 1)) = 0
        [HideInInspector] _ElectricalMultiplier ("Electrical Multiplier", Range(0, 4)) = 1
        [HideInInspector] _TravelDirectionWS ("Travel Direction", Vector) = (0, 0, 1, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+40"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "CoronaDischarge"
            // Alpha-composited HDR prevents overlapping craft parts from adding into
            // a frame-filling white bloom while preserving a luminous surface rim.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _MainColor;
                half4 _OverchargeColor;
                half4 _SparkColor;
                half4 _OuterColor;
                half _ChromaSeparation;
                half _Brightness;
                half _Saturation;
                half _EdgeTightness;
                half _BandWidth;
                half _OutlineStrength;
                half _OutlineCoverage;
                half _CoverageFade;
                half _SurfaceFill;
                half _LeadingEdgeBias;
                half _RearSuppression;
                half _ShellExpansion;
                half _SurfaceBreakup;
                half _NoiseScale;
                half _NoiseContrast;
                half _CrawlSpeed;
                half _FlickerAmount;
                half _FlickerSpeed;
                half _FlowStrength;
                half _FlowDensity;
                half _Intensity;
                half _Speed01;
                half _BoostBlend;
                half _Ignition;
                half _ElectricalMultiplier;
                float4 _TravelDirectionWS;
            CBUFFER_END

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float3 p)
            {
                float3 cell = floor(p);
                float3 local = frac(p);
                local = local * local * (3.0 - 2.0 * local);

                float n000 = Hash31(cell + float3(0, 0, 0));
                float n100 = Hash31(cell + float3(1, 0, 0));
                float n010 = Hash31(cell + float3(0, 1, 0));
                float n110 = Hash31(cell + float3(1, 1, 0));
                float n001 = Hash31(cell + float3(0, 0, 1));
                float n101 = Hash31(cell + float3(1, 0, 1));
                float n011 = Hash31(cell + float3(0, 1, 1));
                float n111 = Hash31(cell + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, local.x);
                float nx10 = lerp(n010, n110, local.x);
                float nx01 = lerp(n001, n101, local.x);
                float nx11 = lerp(n011, n111, local.x);
                return lerp(lerp(nx00, nx10, local.y), lerp(nx01, nx11, local.y), local.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 expandedPositionOS = input.positionOS.xyz
                    + normalize(input.normalOS) * _ShellExpansion;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(expandedPositionOS);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = normalInputs.normalWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = SafeNormalize(input.normalWS);
                half3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = saturate(1.0h - dot(normalWS, viewDirection));

                half tightness = max(0.5h, _EdgeTightness);
                half width = max(0.01h, _BandWidth);
                half innerEnvelope = pow(rim, tightness);
                half mainEnvelope = pow(rim, max(0.35h,
                    tightness / (1.0h + width * (3.0h + _ChromaSeparation * 6.0h))));
                half outerEnvelope = pow(rim, max(0.25h,
                    tightness / (1.0h + width * (8.0h + _ChromaSeparation * 10.0h))));
                half innerBand = innerEnvelope;
                half mainBand = saturate(mainEnvelope - innerEnvelope);
                half outerBand = saturate(outerEnvelope - mainEnvelope);

                // The primary hue has one explicit source in each gameplay state.
                // Ignition never introduces a hidden white core.
                half overchargeColorBlend = saturate(_BoostBlend + _Ignition * 0.35h);
                half3 primaryColor = lerp(_MainColor.rgb, _OverchargeColor.rgb,
                    overchargeColorBlend);
                half3 mergedSpectrum = primaryColor * innerEnvelope;
                half3 separatedSpectrum = primaryColor * innerBand
                    + primaryColor * mainBand * 1.12h
                    + _OuterColor.rgb * outerBand * 0.9h;
                half3 spectrum = lerp(mergedSpectrum, separatedSpectrum, _ChromaSeparation);

                float travelLengthSq = max(dot(_TravelDirectionWS.xyz, _TravelDirectionWS.xyz), 0.0001);
                half3 travelDirection = (half3)(_TravelDirectionWS.xyz * rsqrt(travelLengthSq));
                half leading = pow(saturate(dot(normalWS, travelDirection)), 2.0h)
                    * _LeadingEdgeBias;
                half rear = saturate(dot(normalWS, -travelDirection)) * _RearSuppression;
                half directionalMask = max(0.16h, (1.0h - rear) * (1.0h + leading));

                float3 noisePosition = input.positionOS * _NoiseScale;
                noisePosition += float3(0.0, _Time.y * _CrawlSpeed * 0.37, -_Time.y * _CrawlSpeed);
                half noiseA = (half)ValueNoise(noisePosition);
                half noiseB = (half)ValueNoise(noisePosition * 2.07 + 11.7);
                half electricalNoise = pow(saturate(noiseA * 0.72h + noiseB * 0.48h),
                    max(0.25h, _NoiseContrast));
                half breakup = saturate(_SurfaceBreakup * _ElectricalMultiplier);
                half breakupMask = lerp(1.0h, 0.22h + electricalNoise * 1.08h, breakup);

                half flowPhase = input.positionOS.z * _FlowDensity
                    + input.positionOS.x * (_FlowDensity * 0.41h)
                    - _Time.y * _CrawlSpeed;
                half flowA = pow(saturate(0.5h + 0.5h * sin(flowPhase)), 7.0h);
                half flowB = pow(saturate(0.5h + 0.5h * sin(
                    flowPhase * 0.53h + input.positionOS.y * 2.3h + _Time.y * 1.9h)), 11.0h);
                half flow = saturate(flowA * 0.72h + flowB * 0.58h);
                half flowMask = lerp(1.0h, 0.42h + flow, saturate(_FlowStrength));

                // Tiny, high-contrast electrical pinpricks. They crawl with the same
                // field as the corona but remain sparse and attached to the mesh.
                half sparkSeed = saturate((electricalNoise - 0.68h) * 3.125h);
                half sparkMask = pow(sparkSeed, 10.0h)
                    * pow(saturate(flow * 1.08h), 4.0h)
                    * saturate(breakup * 1.35h);

                // Stable mesh-space dissolve controls how much of the silhouette is
                // affected. A feathered threshold leaves natural attached arcs instead
                // of cutting the outline into hard binary fragments.
                half coverageField = saturate(noiseA * 0.62h + noiseB * 0.38h
                    + flow * 0.12h);
                half coverageFeather = lerp(0.015h, 0.30h, saturate(_CoverageFade));
                // Offset the threshold beyond the 0..1 field at both endpoints so
                // Coverage=0 is truly absent and Coverage=1 is truly complete.
                half coverageThreshold = lerp(1.0h + coverageFeather,
                    -coverageFeather, saturate(_OutlineCoverage));
                half outlineCoverageMask = smoothstep(
                    coverageThreshold - coverageFeather,
                    coverageThreshold + coverageFeather,
                    coverageField);

                half flickerWave = sin(_Time.y * _FlickerSpeed + electricalNoise * 9.7h);
                half flicker = 1.0h + flickerWave * _FlickerAmount
                    * lerp(0.5h, _ElectricalMultiplier, _BoostBlend);

                half wholeBody = _SurfaceFill * (0.28h + 0.72h * saturate(leading + 0.18h));
                half3 bodyColor = lerp(primaryColor, _OuterColor.rgb,
                    saturate(electricalNoise * 0.12h * _ChromaSeparation));
                half3 coronaColor = spectrum * _OutlineStrength * outlineCoverageMask
                    + bodyColor * wholeBody;
                coronaColor *= breakupMask * flowMask * directionalMask * flicker;
                coronaColor += _SparkColor.rgb * sparkMask
                    * (0.42h + _ElectricalMultiplier * 0.34h);

                half luminance = dot(coronaColor, half3(0.2126h, 0.7152h, 0.0722h));
                coronaColor = lerp(luminance.xxx, coronaColor, _Saturation);
                half energy = min(1.35h, _Intensity * _Brightness);
                half opacity = saturate(energy * (0.32h + rim * 0.18h));
                coronaColor = min(coronaColor, half3(3.2h, 3.2h, 3.2h));
                return half4(coronaColor, opacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
