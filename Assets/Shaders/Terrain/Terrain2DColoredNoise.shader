Shader "Custom/Terrain2DColoredNoise"
{
    Properties
    {
        _TerrainDataTex ("Terrain Data Texture", 2D) = "black" {}

        [Header(Biome Thresholds)]
        _BeachTop ("Beach Top", Range(0, 1)) = 0.05
        _BiomeTop ("Biome Top", Range(0, 1)) = 0.10
        _RockStart ("Rock Start", Range(0, 1)) = 0.20
        _RockEnd ("Rock End", Range(0, 1)) = 0.70
        _SnowLineCold ("Snow Line Cold", Range(0, 1)) = 0.50
        _SnowLineHot ("Snow Line Hot", Range(0, 1)) = 1.00
        _SnowBandWidth ("Snow Band Width", Range(0, 1)) = 0.15
        _TemperatureEnabled ("Temperature Enabled", Range(0, 1)) = 1
        _MoistureEnabled ("Moisture Enabled", Range(0, 1)) = 1

        [Header(Shading Style)]
        _LightBands ("Light Bands (1=smooth, 3-5=toon)", Range(1, 8)) = 3
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.45
        _SaturationBoost ("Color Saturation Boost", Range(0.5, 3)) = 1.4

        [Header(Lighting Mix)]
        _MainLightMix ("Main Light Mix", Range(0, 1)) = 1.0
        _ShadowDarkness ("Shadow Darkness", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_TerrainDataTex);
            SAMPLER(sampler_TerrainDataTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _TerrainDataTex_ST;
                float _BeachTop;
                float _BiomeTop;
                float _RockStart;
                float _RockEnd;
                float _SnowLineCold;
                float _SnowLineHot;
                float _SnowBandWidth;
                float _TemperatureEnabled;
                float _MoistureEnabled;
                float _LightBands;
                float _AmbientStrength;
                float _SaturationBoost;
                float _MainLightMix;
                float _ShadowDarkness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _TerrainDataTex);
                OUT.fogFactor = ComputeFogFactor(p.positionCS.z);

                float3 smoothN = normalize(OUT.normalWS);
                float3 lightDir = _MainLightPosition.xyz;
                float cosTheta = saturate(dot(smoothN, lightDir));
                float biasSlope = sqrt(1.0 - cosTheta * cosTheta) / max(0.001, cosTheta);
                float dynamicBias = min(0.05 * biasSlope, 0.15);
                float3 biasedPositionWS = p.positionWS + (smoothN * (0.03 + dynamicBias));
                OUT.shadowCoord = TransformWorldToShadowCoord(biasedPositionWS);

                return OUT;
            }

            float3 C32(float r, float g, float b)
            {
                return float3(r, g, b) / 255.0;
            }

            float SafeSmoothStep(float edge0, float edge1, float x)
            {
                float t = saturate((x - edge0) / max(0.0001, edge1 - edge0));
                return t * t * (3.0 - 2.0 * t);
            }

            float3 ColorLerp(float3 a, float3 b, float t)
            {
                return lerp(a, b, saturate(t));
            }

            float3 MoistureBand(float moisture, float3 dry, float3 mid, float3 wet)
            {
                if (moisture < 0.5)
                {
                    return ColorLerp(dry, mid, SafeSmoothStep(0.0, 1.0, moisture * 2.0));
                }

                return ColorLerp(mid, wet, SafeSmoothStep(0.0, 1.0, (moisture - 0.5) * 2.0));
            }

            float3 TempBand(float temperature, float3 cold, float3 normal, float3 hot)
            {
                if (temperature < 0.5)
                {
                    return ColorLerp(cold, normal, SafeSmoothStep(0.0, 1.0, temperature * 2.0));
                }

                return ColorLerp(normal, hot, SafeSmoothStep(0.0, 1.0, (temperature - 0.5) * 2.0));
            }

            float3 GetZoneColor(float temperature, float moisture)
            {
                float3 coldDry = C32(215, 225, 240);
                float3 coldMid = C32(165, 215, 175);
                float3 coldWet = C32(45, 135, 80);
                float3 normDry = C32(245, 215, 110);
                float3 normMid = C32(170, 230, 90);
                float3 normWet = C32(55, 165, 80);
                float3 hotDry = C32(255, 175, 90);
                float3 hotMid = C32(220, 200, 70);
                float3 hotWet = C32(35, 150, 50);

                float3 cold = MoistureBand(moisture, coldDry, coldMid, coldWet);
                float3 normal = MoistureBand(moisture, normDry, normMid, normWet);
                float3 hot = MoistureBand(moisture, hotDry, hotMid, hotWet);
                return TempBand(temperature, cold, normal, hot);
            }

            float3 BiomeColor(float height01, float moisture, float temperature)
            {
                float3 sea = C32(60, 165, 220);
                float3 beach = C32(245, 225, 165);
                float3 rock = C32(165, 150, 130);
                float3 snow = C32(248, 252, 255);

                float h = saturate(height01);

                if (h < _BeachTop)
                {
                    return ColorLerp(sea, beach, SafeSmoothStep(0.0, _BeachTop, h));
                }

                float colorTemperature = _TemperatureEnabled > 0.5 && temperature >= 0.0 ? saturate(temperature) : 0.5;
                float colorMoisture = _MoistureEnabled > 0.5 && moisture >= 0.0 ? saturate(moisture) : 0.5;
                float3 biome = GetZoneColor(colorTemperature, colorMoisture);

                if (h < _BiomeTop)
                {
                    return ColorLerp(beach, biome, SafeSmoothStep(_BeachTop, _BiomeTop, h));
                }

                float rockBlend = SafeSmoothStep(_RockStart, _RockEnd, h) * 0.5;
                biome = ColorLerp(biome, rock, rockBlend);

                float snowLine = lerp(_SnowLineCold, _SnowLineHot, colorTemperature);
                float snowBlend = SafeSmoothStep(snowLine, snowLine + _SnowBandWidth, h);
                return ColorLerp(biome, snow, snowBlend);
            }

            float3 SaturateColor(float3 c, float strength)
            {
                float gray = dot(c, float3(0.299, 0.587, 0.114));
                return lerp(float3(gray, gray, gray), c, strength);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 data = SAMPLE_TEXTURE2D(_TerrainDataTex, sampler_TerrainDataTex, IN.uv);
                float3 albedo = BiomeColor(data.r, data.g, data.b);
                albedo = SaturateColor(albedo, _SaturationBoost);

                float3 smoothN = normalize(IN.normalWS);
                Light mainLight = GetMainLight(IN.shadowCoord);
                float NdotL = saturate(dot(smoothN, mainLight.direction));

                float bands = max(1.0, _LightBands);
                float bandedNdotL = floor(NdotL * bands) / bands;
                float useBanding = step(1.5, bands);
                NdotL = lerp(NdotL, bandedNdotL, useBanding);

                float combinedLight = NdotL * mainLight.shadowAttenuation;
                combinedLight = lerp(1.0 - _ShadowDarkness, 1.0, combinedLight);

                float3 ambient = SampleSH(smoothN) * _AmbientStrength;
                float3 lighting = mainLight.color * combinedLight * _MainLightMix + ambient;
                float3 finalColor = albedo * lighting;

                finalColor = MixFog(finalColor, IN.fogFactor);
                return half4(finalColor, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_shadowcaster

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float3 _LightDirection;

            Varyings ShadowPassVertex(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                return OUT;
            }

            half4 ShadowPassFragment(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
