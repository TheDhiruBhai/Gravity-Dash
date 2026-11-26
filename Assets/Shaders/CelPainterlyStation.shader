Shader "Custom/CelPainterlyStation"
{
    Properties
    {
        [Header(Style Settings)]
        [KeywordEnum(CelShaded, Painterly)] _RenderStyle("Rendering Style", Float) = 0
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (0.8, 0.8, 0.85, 1.0)
        
        [Header(Cel Shaded Parameters)]
        _ShadowColor("Shadow Tint Color", Color) = (0.15, 0.18, 0.28, 1.0)
        _HighlightColor("Highlight Tint Color", Color) = (1.0, 0.98, 0.9, 1.0)
        _CelBands("Diffuse Steps", Range(2, 6)) = 3.0
        _RimPower("Rim Light Exponent", Range(1, 10)) = 3.5
        _RimIntensity("Rim Light Intensity", Range(0, 2)) = 0.6
        _OutlineWidth("Outline Width", Range(0, 0.05)) = 0.015
        _OutlineColor("Outline Color", Color) = (0.05, 0.05, 0.08, 1.0)

        [Header(Painterly Parameters)]
        _BrushScale("Brushstroke Scale", Range(5, 80)) = 32.0
        _BrushStrength("Brushstroke Breakup Strength", Range(0, 0.4)) = 0.18
        _PainterlyWarmth("Shadow-to-Light Warmth", Range(0, 1)) = 0.4
        
        [Header(Emission)]
        _EmissionMap("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
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
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _RenderStyle;
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float4 _ShadowColor;
            float4 _HighlightColor;
            float _CelBands;
            float _RimPower;
            float _RimIntensity;
            float _OutlineWidth;
            float4 _OutlineColor;
            float _BrushScale;
            float _BrushStrength;
            float _PainterlyWarmth;
            float4 _EmissionMap_ST;
            float4 _EmissionColor;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_EmissionMap);
        SAMPLER(sampler_EmissionMap);

        // Procedural brushstroke noise for painterly breakup
        float BrushstrokeHash(float2 uv)
        {
            float2 p = uv * _BrushScale;
            float n = sin(dot(p, float2(127.1, 311.7))) * 43758.5453;
            float wave1 = sin(p.x * 0.8 + p.y * 1.3 + n * 0.1);
            float wave2 = cos(p.x * 1.4 - p.y * 0.7);
            return (wave1 + wave2) * 0.5 * _BrushStrength;
        }
        ENDHLSL

        // ==========================================
        // PASS 1: FORWARD LIT (Cel / Painterly)
        // ==========================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _RENDERSTYLE_CELSHADED _RENDERSTYLE_PAINTERLY

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float2 uv           : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);
                
                // Sample textures
                float4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                float4 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv) * _EmissionColor;

                // Lighting
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                
                float3 lightDir = normalize(mainLight.direction);
                float NdotL = dot(normalWS, lightDir);
                float halfLambert = saturate(NdotL * 0.5 + 0.5);
                float shadowAttenuation = mainLight.shadowAttenuation;

                float3 finalColor = float3(0, 0, 0);

                // Check mode (Keyword or Uniform float _RenderStyle)
                bool isPainterly = (_RenderStyle > 0.5);
                #if defined(_RENDERSTYLE_PAINTERLY)
                    isPainterly = true;
                #elif defined(_RENDERSTYLE_CELSHADED)
                    isPainterly = false;
                #endif

                if (!isPainterly)
                {
                    // === CEL-SHADED MODE ===
                    // Stepped diffuse lighting bands
                    float lightFactor = halfLambert * shadowAttenuation;
                    float steppedLight = floor(lightFactor * _CelBands) / (_CelBands - 1.0);
                    steppedLight = saturate(steppedLight);

                    // Shadow tint blending
                    float3 diffuseColor = lerp(_ShadowColor.rgb, _HighlightColor.rgb * albedo.rgb, steppedLight);
                    
                    // Stepped Rim Light
                    float NdotV = saturate(dot(normalWS, viewDirWS));
                    float rim = pow(1.0 - NdotV, _RimPower) * _RimIntensity;
                    float steppedRim = step(0.4, rim) * 0.8;
                    float3 rimColor = steppedRim * mainLight.color.rgb;

                    finalColor = (diffuseColor * mainLight.color.rgb) + rimColor;
                }
                else
                {
                    // === PAINTERLY MODE ===
                    // UV & Normal brushstroke breakup
                    float brushNoise = BrushstrokeHash(input.uv);
                    float painterlyL = saturate(halfLambert + brushNoise);
                    painterlyL *= shadowAttenuation;

                    // Stylized color ramp mapping
                    float3 coolShadow = _ShadowColor.rgb * albedo.rgb * 0.7;
                    float3 warmMid = lerp(coolShadow, albedo.rgb, 0.6);
                    float3 brightLight = albedo.rgb * _HighlightColor.rgb * 1.15;

                    float3 ramp;
                    if (painterlyL < 0.45)
                    {
                        ramp = lerp(coolShadow, warmMid, painterlyL / 0.45);
                    }
                    else
                    {
                        ramp = lerp(warmMid, brightLight, (painterlyL - 0.45) / 0.55);
                    }

                    // Soft stylized rim
                    float NdotV = saturate(dot(normalWS, viewDirWS));
                    float painterlyRim = smoothstep(0.5, 0.85, 1.0 - NdotV) * _RimIntensity * 0.6;

                    finalColor = ramp * mainLight.color.rgb + (painterlyRim * albedo.rgb);
                }

                // Add Emission
                finalColor += emission.rgb;

                return float4(finalColor, albedo.a);
            }
            ENDHLSL
        }

        // ==========================================
        // PASS 2: INVERTED HULL OUTLINE (Cel-Shaded)
        // ==========================================
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertOutline
            #pragma fragment FragOutline

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
            };

            Varyings VertOutline(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                // If width is zero, clamp to prevent artifacts
                float width = max(_OutlineWidth, 0.0);
                float3 normalExtruded = input.positionOS.xyz + normalize(input.normalOS) * width;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(normalExtruded);
                output.positionCS = vertexInput.positionCS;
                return output;
            }

            float4 FragOutline(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ==========================================
        // PASS 3: SHADOW CASTER
        // ==========================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

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

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ==========================================
        // PASS 4: DEPTH ONLY
        // ==========================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            struct Attributes
            {
                float4 positionOS   : POSITION;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
