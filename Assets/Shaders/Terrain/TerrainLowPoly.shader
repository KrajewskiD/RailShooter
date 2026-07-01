Shader "Custom/TerrainLowPoly"
{
    Properties
    {
        [Header(Shading Style)]
        _LightBands       ("Light Bands (1=smooth, 3-5=toon)", Range(1, 8)) = 3
        _AmbientStrength  ("Ambient Strength", Range(0, 1)) = 0.45
        _SaturationBoost  ("Color Saturation Boost", Range(0.5, 3)) = 1.4

        [Header(Lighting Mix)]
        _MainLightMix     ("Main Light Mix (0=ambient only, 1=full sun)", Range(0, 1)) = 1.0
        _ShadowDarkness   ("Shadow Darkness (cieńszy = ciemniejszy cień)", Range(0, 1)) = 0.4

        [Header(Flat Shading)]
        [Toggle] _UseFlatShading ("Use Flat Shading (per-triangle)", Float) = 1
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

            CBUFFER_START(UnityPerMaterial)
                float _LightBands;
                float _AmbientStrength;
                float _SaturationBoost;
                float _MainLightMix;
                float _ShadowDarkness;
                float _UseFlatShading;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : COLOR;
                float  fogFactor  : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color      = IN.color;
                OUT.fogFactor  = ComputeFogFactor(p.positionCS.z);




                float3 smoothN = normalize(OUT.normalWS);
                float3 lightDir = _MainLightPosition.xyz;
                
                float cosTheta = saturate(dot(smoothN, lightDir));

                float biasSlope = sqrt(1.0 - cosTheta * cosTheta) / max(0.001, cosTheta);
                float dynamicBias = min(0.05 * biasSlope, 0.15);


                float3 biasedPositionWS = p.positionWS + (smoothN * (0.03 + dynamicBias));
                OUT.shadowCoord = TransformWorldToShadowCoord(biasedPositionWS);

                return OUT;
            }

            float3 SaturateColor(float3 c, float strength)
            {
                float gray = dot(c, float3(0.299, 0.587, 0.114));
                return lerp(float3(gray, gray, gray), c, strength);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 smoothN = normalize(IN.normalWS);
                

                float3 N;
                if (_UseFlatShading > 0.5)
                {
                    float3 dpdx = ddx(IN.positionWS);
                    float3 dpdy = ddy(IN.positionWS);
                    N = normalize(cross(dpdy, dpdx));
                }
                else
                {
                    N = smoothN;
                }


                Light mainLight = GetMainLight(IN.shadowCoord); 
                float shadowAttenuation = mainLight.shadowAttenuation;


                float NdotL = saturate(dot(N, mainLight.direction));


                float bands = max(1.0, _LightBands);
                float bandedNdotL = floor(NdotL * bands) / bands;
                float useBanding = step(1.5, bands);
                NdotL = lerp(NdotL, bandedNdotL, useBanding);


                float combinedLight = NdotL * shadowAttenuation;
                combinedLight = lerp(1.0 - _ShadowDarkness, 1.0, combinedLight);

                float3 ambient = SampleSH(smoothN) * _AmbientStrength;
                float3 lighting = mainLight.color * combinedLight * _MainLightMix + ambient;

                float3 albedo = SaturateColor(IN.color.rgb, _SaturationBoost);
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
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
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