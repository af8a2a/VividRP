// Physically-Based Solid Optical / Volumetric Glass Shader (OpenPBR Transmission + Volume + Dispersion)
//   - Pass 1 (VividTransparentBackface): Rasterizes exit backface oct-normal + linearViewZ for exact thickness
//   - Pass 2 (VividRefraction):          Double-sided Snell refraction, TIR fallback, same-frame OpaqueColorPyramid
//                                        rough transmission blur, OpenPBR Beer-Lambert volume absorption,
//                                        OpenPBR Cauchy/Fraunhofer RGB Abbe dispersion, foreground rejection,
//                                        and Clustered LightGrid + Reflection Probes + Sky IBL + CSM surface specular.
//   - Pass 3 (ShadowCaster):             Standard shadow caster.
//   - Pass 4 (ReferencedPathtracingDXR): Direct OpenPBR ground-truth Path Tracing parity via ReferencedPathtracing.hlsl.
Shader "VividRP/Material/Glass/OpticalGlass"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Surface Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Smoothness ("Smoothness", Range(0.0, 1.0)) = 0.98
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0.0, 2.0)) = 1.0

        [Header(OpenPBR Transmission and Volume)]
        _SpecularIOR ("Index of Refraction (IOR)", Range(1.0, 2.5)) = 1.50
        _TransmissionWeight ("Transmission Weight", Range(0.0, 1.0)) = 1.0
        _TransmissionColor ("Attenuation Color (Beer-Lambert)", Color) = (0.95, 0.99, 0.98, 1.0)
        _TransmissionDepth ("Attenuation Distance (m)", Float) = 0.25
        _TransmissionScatter ("Volume Scatter Color", Color) = (0.0, 0.0, 0.0, 1.0)
        _TransmissionScatterAnisotropy ("Volume Anisotropy", Range(-0.99, 0.99)) = 0.0
        _ThinWalledTransmission ("Thin-Walled Mode", Range(0.0, 1.0)) = 0.0

        [Header(Dispersion and Thickness)]
        _Dispersion ("OpenPBR Dispersion (20 / Abbe Vd)", Range(0.0, 1.5)) = 0.0
        _ThicknessMap ("Baked Thickness Map (Optional)", 2D) = "white" {}
        _ThicknessScale ("Thickness Scale (m)", Range(0.005, 5.0)) = 1.0
        _UseThicknessMap ("Use Baked Thickness Map", Range(0.0, 1.0)) = 0.0
        _RefractionStrength ("Refraction Offset Scale", Range(0.0, 2.0)) = 1.0
        _BackfaceSpecularIntensity ("Internal Reflection Cue", Range(0.0, 1.0)) = 0.25

        [Header(Clear Coat)]
        _ClearCoatMask ("Clear Coat Weight", Range(0.0, 1.0)) = 0.0
        _ClearCoatSmoothness ("Clear Coat Smoothness", Range(0.0, 1.0)) = 0.95
        [HDR] _EmissionColor ("Emission Color", Color) = (0.0, 0.0, 0.0, 1.0)

        // Hidden StandardLitInput compatibility defaults for ReferencedPathtracingDXR parity
        [HideInInspector] _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _SmoothnessTextureChannel ("Smoothness Channel", Float) = 0.0
        [HideInInspector] _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _MetallicRemapMin ("Metallic Remap Min", Float) = 0.0
        [HideInInspector] _MetallicRemapMax ("Metallic Remap Max", Float) = 1.0
        [HideInInspector] _SmoothnessRemapMin ("Smoothness Remap Min", Float) = 0.0
        [HideInInspector] _SmoothnessRemapMax ("Smoothness Remap Max", Float) = 1.0
        [HideInInspector] _AORemapMin ("AO Remap Min", Float) = 0.0
        [HideInInspector] _AORemapMax ("AO Remap Max", Float) = 1.0
        [HideInInspector] _OcclusionStrength ("Occlusion Strength", Range(0.0, 1.0)) = 1.0
        [HideInInspector] _AlphaClip ("Alpha Clip", Float) = 0.0
        [HideInInspector] _WorkflowMode ("Workflow Mode", Float) = 0.0
        [HideInInspector] _ReceiveSSR ("Receive SSR", Float) = 1.0
        [HideInInspector] _ReceiveDecals ("Receive Decals", Float) = 0.0
        [HideInInspector] _SubsurfaceColor ("Subsurface Color", Color) = (0.8, 0.5, 0.4, 1.0)
        [HideInInspector] _SubsurfaceRadiusScale ("Subsurface Radius Scale", Vector) = (1.0, 0.35, 0.19, 0.0)
        [HideInInspector] _SubsurfaceWeight ("Subsurface Weight", Float) = 0.0
        [HideInInspector] _SubsurfaceRadius ("Subsurface Radius", Float) = 0.0
        [HideInInspector] _SubsurfaceScatterAnisotropy ("Subsurface Anisotropy", Float) = 0.0
        [HideInInspector] _SubsurfaceTransmissionWeight ("Subsurface Transmission", Float) = 0.0
        [HideInInspector] _OpacityMap ("Opacity Map", 2D) = "white" {}
        [HideInInspector] _TransmissionMap ("Transmission Map", 2D) = "white" {}
        [HideInInspector] _RMOMap ("RMO Map", 2D) = "white" {}
        [HideInInspector] _MetallicGlossMap ("Metallic Map", 2D) = "white" {}
        [HideInInspector] _RoughnessMap ("Roughness Map", 2D) = "white" {}
        [HideInInspector] _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        [HideInInspector] _EmissionMap ("Emission Map", 2D) = "black" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "VividRenderPipeline"
        }

        // =========================================================================
        // Pass 1: Backface Depth & Octahedral Normal (for exact thickness + exit Snell)
        // =========================================================================
        Pass
        {
            Name "VividTransparentBackface"
            Tags { "LightMode" = "VividTransparentBackface" }

            Cull Front
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma target 5.0
            #pragma use_dxc
            #pragma multi_compile_instancing
            #pragma vertex VertBackface
            #pragma fragment FragBackface

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float2 VividGlassEncodeOctNormal(float3 n)
            {
                n = SafeNormalize(n);
                float invL1 = rcp(max(abs(n.x) + abs(n.y) + abs(n.z), 1e-6f));
                float2 oct = n.xy * invL1;
                if (n.z < 0.0f)
                {
                    float2 signNotZero = float2(oct.x >= 0.0f ? 1.0f : -1.0f, oct.y >= 0.0f ? 1.0f : -1.0f);
                    oct = (1.0f - abs(oct.yx)) * signNotZero;
                }
                return oct * 0.5f + 0.5f;
            }

            Varyings VertBackface(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 posWS = TransformObjectToWorld(input.positionOS);
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            float4 FragBackface(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float linearViewZ = max(-TransformWorldToView(input.positionWS).z, 1e-4f);
                float2 octN = VividGlassEncodeOctNormal(input.normalWS);
                return float4(octN, linearViewZ, 1.0f);
            }
            ENDHLSL
        }

        // =========================================================================
        // Pass 2: Forward Physical Refraction + Beer-Lambert + Abbe Dispersion + LightGrid
        // =========================================================================
        Pass
        {
            Name "VividRefraction"
            Tags { "LightMode" = "VividRefraction" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma target 5.0
            #pragma use_dxc
            #pragma multi_compile_instancing
            #pragma vertex VertGlass
            #pragma fragment FragGlass

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Input.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/AutoExposure.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/PunctualLightCommon.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividAreaLightCommon.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/ClusteredLighting.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividSkyLighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _EmissionColor;
                float4 _TransmissionColor;
                float4 _TransmissionScatter;
                float _Smoothness;
                float _BumpScale;
                float _SpecularIOR;
                float _TransmissionWeight;
                float _TransmissionDepth;
                float _TransmissionScatterAnisotropy;
                float _ThinWalledTransmission;
                float _Dispersion;
                float _ThicknessScale;
                float _UseThicknessMap;
                float _RefractionStrength;
                float _BackfaceSpecularIntensity;
                float _ClearCoatMask;
                float _ClearCoatSmoothness;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_ThicknessMap);
            SAMPLER(sampler_ThicknessMap);

            // Global refraction resources bound by ForwardRefractionPass
            Texture2D<float4> _OpaqueColorPyramid;
            Texture2D<float> _OpaqueDepthTexture;
            Texture2D<float4> _RefractionBackfaceData;
            Texture2D<float> _DirectionalShadowTexture;

            float4 _RefractionScreenSize;      // (width, height, 1/width, 1/height)
            float _OpaqueColorPyramidMaxMip;
            uint _RefractionHasBackfaceData;
            uint _RefractionEnable;
            uint _RefractionEnableDispersion;
            uint _RefractionEnableBackface;
            uint _RefractionDebugMode;
            uint _PunctualLightCount;
            uint _AreaLightCount;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv0 : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 VividGlassDecodeOctNormal(float2 oct01)
            {
                float2 f = oct01 * 2.0f - 1.0f;
                float3 n = float3(f.x, f.y, 1.0f - abs(f.x) - abs(f.y));
                if (n.z < 0.0f)
                {
                    float2 signNotZero = float2(n.x >= 0.0f ? 1.0f : -1.0f, n.y >= 0.0f ? 1.0f : -1.0f);
                    n.xy = (1.0f - abs(n.yx)) * signNotZero;
                }
                return SafeNormalize(n);
            }

            // Exact dielectric Fresnel reflectance (unpolarized) for IOR ratio eta = n_interior / n_exterior
            float VividGlassFresnelDielectric(float cosThetaI, float ior)
            {
                float c = saturate(cosThetaI);
                float g2 = ior * ior - 1.0f + c * c;
                if (g2 <= 0.0f)
                    return 1.0f;
                float g = sqrt(g2);
                float a = (g - c) / max(g + c, 1e-6f);
                float b = (c * (g + c) - 1.0f) / max(c * (g - c) + 1.0f, 1e-6f);
                return saturate(0.5f * a * a * (1.0f + b * b));
            }

            // Exact OpenPBR Cauchy / Fraunhofer dispersion formula (matching openpbr_dispersion_utils.h)
            float VividGlassDispersionIor(float ior, float dispersionParam, float wavelengthNm)
            {
                if (dispersionParam <= 1e-4f || abs(ior - 1.0f) <= 1e-4f)
                    return ior;

                const float invShortSq = 1.0f / (486.1f * 486.1f);  // Fraunhofer F
                const float invMedSq   = 1.0f / (587.6f * 587.6f);  // Fraunhofer d
                const float invLongSq  = 1.0f / (656.3f * 656.3f);  // Fraunhofer C
                float Vd = 20.0f / max(dispersionParam, 1e-4f);
                float B = (ior - 1.0f) / (Vd * (invShortSq - invLongSq));
                float A = ior - B * invMedSq;
                return max(A + B / (wavelengthNm * wavelengthNm), 1.01f);
            }

            // Exact OpenPBR homogeneous volume Beer-Lambert transmittance (matching openpbr_volume_utils.h)
            float3 VividGlassBeerLambert(float3 transColor, float transDepth, float pathLength)
            {
                if (transDepth <= 1e-5f)
                    return saturate(transColor);
                float3 clampedColor = max(saturate(transColor), 1e-3f.xxx);
                float clampedDepth = max(transDepth, 1e-3f);
                float3 sigma = -log(clampedColor) / clampedDepth;
                return exp(-sigma * max(pathLength, 0.0f));
            }

            float VividGlassD_GGX(float NdotH, float alpha)
            {
                float a2 = alpha * alpha;
                float d = (NdotH * a2 - NdotH) * NdotH + 1.0f;
                return a2 / max(PI * d * d, 1e-7f);
            }

            float VividGlassV_SmithGGX(float NdotV, float NdotL, float alpha)
            {
                float a2 = alpha * alpha;
                float lambdaV = NdotL * sqrt((NdotV - NdotV * a2) * NdotV + a2);
                float lambdaL = NdotV * sqrt((NdotL - NdotL * a2) * NdotL + a2);
                return 0.5f / max(lambdaV + lambdaL, 1e-6f);
            }

            float3 EvaluateGlassSpecBRDF(float3 N, float3 V, float3 L, float alpha, float ior)
            {
                float NdotL = dot(N, L);
                if (NdotL <= 0.0f)
                    return 0.0f.xxx;
                float NdotV = max(dot(N, V), 1e-4f);
                float3 H = SafeNormalize(V + L);
                float NdotH = saturate(dot(N, H));
                float VdotH = saturate(dot(V, H));
                float D = VividGlassD_GGX(NdotH, alpha);
                float Vis = VividGlassV_SmithGGX(NdotV, NdotL, alpha);
                float F = VividGlassFresnelDielectric(VdotH, ior);
                return (D * Vis * F * NdotL).xxx;
            }

            float3 EvaluateGlassEnvironment(float3 R, float perceptualRoughness)
            {
                if (HasSkyTexture())
                    return max(SampleSkyTexture(R, perceptualRoughness), 0.0f.xxx);

                // Calibrated indoor studio softbox environment fallback when SkyType == None
                float3 dir = SafeNormalize(R);
                float horizon = smoothstep(-0.35f, 0.45f, dir.y);
                float3 baseRoom = lerp(float3(0.06f, 0.07f, 0.08f), float3(0.18f, 0.20f, 0.23f), horizon);
                float softboxTop = smoothstep(0.62f - perceptualRoughness * 0.35f, 0.92f, dir.y);
                float softboxSide = smoothstep(0.68f - perceptualRoughness * 0.35f, 0.94f, dot(dir, SafeNormalize(float3(0.65f, 0.55f, -0.52f))));
                float softboxRim = smoothstep(0.72f - perceptualRoughness * 0.35f, 0.95f, dot(dir, SafeNormalize(float3(-0.55f, 0.55f, 0.62f))));
                float softboxAtten = lerp(1.0f, 0.32f, perceptualRoughness);
                return baseRoom + (softboxTop * 0.85f + softboxSide * 1.15f + softboxRim * 0.70f) * softboxAtten;
            }

            float3 SampleOpaquePyramidFiltered(float2 uv, float mipLevel)
            {
                if (mipLevel <= 0.35f)
                    return _OpaqueColorPyramid.SampleLevel(sampler_LinearClamp, uv, mipLevel).rgb;

                // 4-tap rotated tent filter removes bilinear boxiness on high-roughness frosted glass
                float2 texel = _RefractionScreenSize.zw * exp2(min(mipLevel, 5.0f)) * 0.65f;
                float3 c0 = _OpaqueColorPyramid.SampleLevel(sampler_LinearClamp, clamp(uv + float2( texel.x,  texel.y * 0.5f), 0.002f.xx, 0.998f.xx), mipLevel).rgb;
                float3 c1 = _OpaqueColorPyramid.SampleLevel(sampler_LinearClamp, clamp(uv + float2(-texel.x, -texel.y * 0.5f), 0.002f.xx, 0.998f.xx), mipLevel).rgb;
                float3 c2 = _OpaqueColorPyramid.SampleLevel(sampler_LinearClamp, clamp(uv + float2(-texel.x * 0.5f,  texel.y), 0.002f.xx, 0.998f.xx), mipLevel).rgb;
                float3 c3 = _OpaqueColorPyramid.SampleLevel(sampler_LinearClamp, clamp(uv + float2( texel.x * 0.5f, -texel.y), 0.002f.xx, 0.998f.xx), mipLevel).rgb;
                return (c0 + c1 + c2 + c3) * 0.25f;
            }

            float3 EvaluateGlassDirectSpecular(
                float3 posWS,
                float3 N,
                float3 V,
                float2 screenUV,
                uint2 pixelCoord,
                float linearViewZ,
                float alpha,
                float ior,
                float coatWeight,
                float coatAlpha)
            {
                float3 spec = 0.0f.xxx;

                // 1. Directional Lights + CSM Shadow
                float mainShadow = _DirectionalShadowTexture.SampleLevel(sampler_PointClamp, screenUV, 0).r;
                [loop]
                for (uint di = 0u; di < _DirectionalLightCount; ++di)
                {
                    DirectionalLightData dl = _DirectionalLights[di];
                    float3 L = SafeNormalize(dl.directionWS);
                    float shadow = ((int)di == _MainDirectionalLightIndex) ? saturate(mainShadow) : 1.0f;
                    float3 radiance = max(dl.color, 0.0f.xxx) * shadow;
                    spec += EvaluateGlassSpecBRDF(N, V, L, alpha, ior) * radiance;
                    if (coatWeight > 1e-3f)
                        spec += coatWeight * EvaluateGlassSpecBRDF(N, V, L, coatAlpha, 1.5f) * radiance;
                }

                // 2. Clustered Punctual Lights
                if (_ClusteredPunctualLightGridEnabled != 0u && _PunctualLightCount > 0u)
                {
                    VividClusteredLightCell cell = VividClusteredLighting::LoadPunctualLightCell(pixelCoord, linearViewZ);
                    [loop]
                    for (uint i = 0u; i < cell.count; ++i)
                    {
                        uint lightIdx = VividClusteredLighting::LoadLightIndex(cell, i);
                        if (lightIdx >= _PunctualLightCount)
                            continue;
                        PunctualLightData pl = _PunctualLights[lightIdx];
                        float3 L;
                        float4 distances;
                        GetVividPunctualLightVectors(posWS, pl, L, distances);
                        if (distances.y <= 1e-6f || dot(N, L) <= 0.0f)
                            continue;
                        float atten = VividPunctualLightAttenuationWithDistanceModification(pl, posWS - pl.positionWS, distances);
                        if (atten <= 0.0f)
                            continue;
                        float3 radiance = max(pl.color, 0.0f.xxx) * atten;
                        spec += EvaluateGlassSpecBRDF(N, V, L, alpha, ior) * radiance;
                        if (coatWeight > 1e-3f)
                            spec += coatWeight * EvaluateGlassSpecBRDF(N, V, L, coatAlpha, 1.5f) * radiance;
                    }
                }

                // 3. Clustered Area Lights (representative-point specular)
                if (_ClusteredAreaLightGridEnabled != 0u && _AreaLightCount > 0u)
                {
                    VividClusteredLightCell areaCell = VividClusteredLighting::LoadAreaLightCell(pixelCoord, linearViewZ);
                    float3 R = reflect(-V, N);
                    [loop]
                    for (uint i = 0u; i < areaCell.count; ++i)
                    {
                        uint areaIdx = VividClusteredLighting::LoadLightIndex(areaCell, i);
                        if (areaIdx >= _AreaLightCount)
                            continue;
                        AreaLightData al = _AreaLights[areaIdx];
                        float3 toCenter = al.positionWS - posWS;
                        float centerDist = length(toCenter);
                        if (centerDist <= 1e-4f || centerDist > max(al.range, 1e-3f))
                            continue;
                        // Closest point on rect/tube along reflection ray for crisp glass highlight
                        float3 planeN = SafeNormalize(al.forwardWS);
                        float denom = dot(R, planeN);
                        float3 closest = al.positionWS;
                        if (abs(denom) > 1e-4f)
                        {
                            float tHit = dot(toCenter, planeN) / denom;
                            if (tHit > 0.0f)
                            {
                                float3 hitOffset = (posWS + R * tHit) - al.positionWS;
                                float halfW = max(al.width * 0.5f, 1e-3f);
                                float halfH = max(al.height * 0.5f, 1e-3f);
                                float px = clamp(dot(hitOffset, al.rightWS), -halfW, halfW);
                                float py = al.lightType == VIVID_AREA_LIGHT_TYPE_RECTANGLE
                                    ? clamp(dot(hitOffset, al.upWS), -halfH, halfH)
                                    : 0.0f;
                                closest = al.positionWS + al.rightWS * px + al.upWS * py;
                            }
                        }
                        float3 L = SafeNormalize(closest - posWS);
                        float distSq = max(dot(closest - posWS, closest - posWS), 1e-4f);
                        float atten = rcp(distSq) * saturate(1.0f - pow(centerDist / max(al.range, 1e-3f), 4.0f));
                        float solidAngle = max(al.width * max(al.height, 0.05f), 0.01f);
                        float3 radiance = max(al.color, 0.0f.xxx) * (atten * min(solidAngle, 4.0f));
                        spec += EvaluateGlassSpecBRDF(N, V, L, max(alpha, 0.04f), ior) * radiance;
                    }
                }

                return spec;
            }

            // Trace a single wavelength ray through the solid glass (front Snell -> projected exit Snell -> screen-space background)
            float3 SampleRefractedBackground(
                float3 posWS,
                float3 V,
                float3 frontN,
                float3 frontBackN,
                bool hasFrontBackface,
                float thickness,
                float ior,
                float perceptualRoughness,
                float2 screenUV,
                float frontViewZ,
                out bool hitTir)
            {
                hitTir = false;
                float invIor = rcp(max(ior, 1.001f));
                float3 T1 = refract(-V, frontN, invIor);
                if (dot(T1, T1) < 1e-4f)
                {
                    hitTir = true;
                    return VividApplyPreExposure(EvaluateGlassEnvironment(reflect(-V, frontN), perceptualRoughness));
                }
                T1 = normalize(T1);

                float3 exitPosWS = posWS + T1 * thickness;
                float3 rayDirWS = T1;

                // Double-sided exit refraction: sample BackfaceNormal at the projected exit point exitUV
                // (avoids false TIR on spheres and curved surfaces caused by unshifted front-pixel backface normals!)
                if (_ThinWalledTransmission < 0.5f && _RefractionEnableBackface != 0u && _RefractionHasBackfaceData != 0u)
                {
                    float4 exitClip = TransformWorldToHClip(exitPosWS);
                    float2 exitUV = exitClip.xy / max(abs(exitClip.w), 1e-5f) * 0.5f + 0.5f;
                    #if UNITY_UV_STARTS_AT_TOP
                        exitUV.y = 1.0f - exitUV.y;
                    #endif
                    exitUV = clamp(exitUV, 0.002f.xx, 0.998f.xx);

                    float4 exitBackData = _RefractionBackfaceData.SampleLevel(sampler_PointClamp, exitUV, 0);
                    float3 exitBackN = exitBackData.a > 0.5f
                        ? VividGlassDecodeOctNormal(exitBackData.xy)
                        : (hasFrontBackface ? frontBackN : -frontN);

                    // Ensure exit normal points outward against the incoming internal ray T1
                    if (dot(T1, exitBackN) < 0.0f)
                        exitBackN = -exitBackN;

                    float3 T2 = refract(T1, -exitBackN, ior);
                    if (dot(T2, T2) < 1e-4f)
                    {
                        // True Total Internal Reflection on sharp prism/dragon facets:
                        // bounce internally and blend reflected scene + studio environment instead of returning black
                        hitTir = true;
                        float3 tirDir = reflect(T1, -exitBackN);
                        float3 tirEnv = VividApplyPreExposure(EvaluateGlassEnvironment(tirDir, perceptualRoughness));
                        float3 tirBg = SampleOpaquePyramidFiltered(exitUV, min(1.5f + perceptualRoughness * 4.0f, _OpaqueColorPyramidMaxMip));
                        return lerp(tirBg * 0.75f, tirEnv, 0.65f);
                    }
                    rayDirWS = normalize(T2);
                }

                // Estimate distance from exit surface to opaque background behind the pixel
                float bgRawDepth = _OpaqueDepthTexture.SampleLevel(sampler_PointClamp, screenUV, 0).r;
                float bgLinearZ = LinearEyeDepth(bgRawDepth, _ZBufferParams);
                float bgDist = (bgRawDepth != UNITY_RAW_FAR_CLIP_VALUE)
                    ? clamp(bgLinearZ - (frontViewZ + thickness), 0.04f, 3.0f)
                    : 1.5f;

                float3 samplePosWS = exitPosWS + rayDirWS * (bgDist * _RefractionStrength);
                float4 sampleClip = TransformWorldToHClip(samplePosWS);
                float2 tapUV = sampleClip.xy / max(abs(sampleClip.w), 1e-5f) * 0.5f + 0.5f;
                #if UNITY_UV_STARTS_AT_TOP
                    tapUV.y = 1.0f - tapUV.y;
                #endif

                bool outOfBounds = any(tapUV < 0.002f) || any(tapUV > 0.998f);
                tapUV = clamp(tapUV, 0.002f.xx, 0.998f.xx);

                // Foreground rejection: never pull objects in front of the glass into the refraction
                float tapRawDepth = _OpaqueDepthTexture.SampleLevel(sampler_PointClamp, tapUV, 0).r;
                if (tapRawDepth != UNITY_RAW_FAR_CLIP_VALUE)
                {
                    float tapLinearZ = LinearEyeDepth(tapRawDepth, _ZBufferParams);
                    if (tapLinearZ < frontViewZ - 0.015f)
                        tapUV = screenUV;
                }

                // OpenPBR / Filament physical rough transmission cone mapping -> OpaqueColorPyramid Mip level
                float transRough = saturate(perceptualRoughness * saturate(0.65f * ior - 0.35f));
                float distFactor = saturate(sqrt(bgDist * 0.95f));
                float mipLevel = clamp(sqrt(transRough) * _OpaqueColorPyramidMaxMip * lerp(0.45f, 0.88f, distFactor), 0.0f, _OpaqueColorPyramidMaxMip * 0.85f);

                float3 bgColor = SampleOpaquePyramidFiltered(tapUV, mipLevel);
                if (outOfBounds)
                {
                    float3 envFallback = VividApplyPreExposure(EvaluateGlassEnvironment(rayDirWS, transRough));
                    bgColor = lerp(bgColor, envFallback, 0.45f);
                }

                return max(bgColor, 0.0f.xxx);
            }

            Varyings VertGlass(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 posWS = TransformObjectToWorld(input.positionOS);
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.tangentWS = float4(
                    TransformObjectToWorldDir(input.tangentOS.xyz),
                    input.tangentOS.w * GetOddNegativeScale());
                o.uv0 = input.uv0 * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return o;
            }

            float4 FragGlass(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                uint2 pixelCoord = (uint2)input.positionCS.xy;
                float2 screenUV = (float2(pixelCoord) + 0.5f) * _RefractionScreenSize.zw;
                float frontViewZ = max(-TransformWorldToView(input.positionWS).z, 1e-4f);
                float3 V = SafeNormalize(GetWorldSpaceCameraViewDir(input.positionWS));

                // Tangent-space normal mapping
                float3 geomN = SafeNormalize(input.normalWS);
                float3 T = input.tangentWS.xyz - geomN * dot(input.tangentWS.xyz, geomN);
                float tLenSq = dot(T, T);
                T = tLenSq > 1e-8f ? T * rsqrt(tLenSq) : float3(1.0f, 0.0f, 0.0f);
                float3 B = cross(geomN, T) * (input.tangentWS.w < 0.0f ? -1.0f : 1.0f);
                float3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv0), _BumpScale);
                float3 N = SafeNormalize(T * normalTS.x + B * normalTS.y + geomN * normalTS.z);
                float NdotV = max(dot(N, V), 1e-4f);

                float3 baseTint = saturate(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv0).rgb * _BaseColor.rgb);
                float perceptualRoughness = clamp(1.0f - _Smoothness, 0.02f, 1.0f);
                float alpha = max(perceptualRoughness * perceptualRoughness, 0.002f);
                float ior = max(_SpecularIOR, 1.01f);
                float transWeight = saturate(_TransmissionWeight);

                // Resolve solid thickness and exit backface normal
                float4 backface = (_RefractionHasBackfaceData != 0u)
                    ? _RefractionBackfaceData.Load(int3(pixelCoord, 0))
                    : 0.0f.xxxx;
                bool hasBackface = backface.a > 0.5f;
                float3 backN = hasBackface ? VividGlassDecodeOctNormal(backface.xy) : -N;
                float geomThickness = hasBackface
                    ? clamp(backface.z - frontViewZ, 0.002f, 4.0f)
                    : max(0.10f * NdotV, 0.005f);

                float bakedThickness = SAMPLE_TEXTURE2D(_ThicknessMap, sampler_ThicknessMap, input.uv0).g;
                float thickness = lerp(
                    geomThickness * _ThicknessScale,
                    max(bakedThickness * _ThicknessScale, 0.002f),
                    saturate(_UseThicknessMap));
                if (_ThinWalledTransmission >= 0.5f)
                    thickness = 0.002f;

                float3 volumeTransmittance = VividGlassBeerLambert(_TransmissionColor.rgb, _TransmissionDepth, thickness);

                // Debug visualizations
                if (_RefractionDebugMode == 1u)
                    return float4(VividApplyPreExposure(saturate(thickness * 4.0f).xxx), 1.0f);
                if (_RefractionDebugMode == 2u)
                    return float4(VividApplyPreExposure(hasBackface ? (backN * 0.5f + 0.5f) : float3(0.05f, 0.0f, 0.05f)), 1.0f);
                if (_RefractionDebugMode == 3u)
                    return float4(VividApplyPreExposure(volumeTransmittance), 1.0f);

                // Evaluate background refraction (monochromatic or 3-wavelength Cauchy/Abbe dispersion)
                float3 refractedPreExposed = 0.0f.xxx;
                if (_RefractionEnable != 0u && transWeight > 1e-3f)
                {
                    bool useDispersion = (_RefractionEnableDispersion != 0u) && (_Dispersion > 1e-3f) && (ior > 1.01f);
                    if (useDispersion)
                    {
                        float iorR = VividGlassDispersionIor(ior, _Dispersion, 656.3f); // C line (Red)
                        float iorG = VividGlassDispersionIor(ior, _Dispersion, 587.6f); // d line (Green)
                        float iorB = VividGlassDispersionIor(ior, _Dispersion, 486.1f); // F line (Blue)
                        bool tirR, tirG, tirB;
                        float r = SampleRefractedBackground(input.positionWS, V, N, backN, hasBackface, thickness, iorR, perceptualRoughness, screenUV, frontViewZ, tirR).r;
                        float g = SampleRefractedBackground(input.positionWS, V, N, backN, hasBackface, thickness, iorG, perceptualRoughness, screenUV, frontViewZ, tirG).g;
                        float b = SampleRefractedBackground(input.positionWS, V, N, backN, hasBackface, thickness, iorB, perceptualRoughness, screenUV, frontViewZ, tirB).b;
                        refractedPreExposed = float3(r, g, b);
                    }
                    else
                    {
                        bool hitTir;
                        refractedPreExposed = SampleRefractedBackground(
                            input.positionWS, V, N, backN, hasBackface, thickness, ior, perceptualRoughness, screenUV, frontViewZ, hitTir);
                    }
                }
                else
                {
                    refractedPreExposed = _OpaqueColorPyramid.SampleLevel(sampler_LinearClamp, screenUV, 0).rgb;
                }

                // Surface Fresnel + Clustered Direct Specular + Sky IBL Reflection + Optional ClearCoat
                float F = VividGlassFresnelDielectric(NdotV, ior);
                float coatWeight = saturate(_ClearCoatMask);
                float coatRough = clamp(1.0f - _ClearCoatSmoothness, 0.02f, 1.0f);
                float coatAlpha = max(coatRough * coatRough, 0.002f);

                float3 directSpec = EvaluateGlassDirectSpecular(
                    input.positionWS, N, V, screenUV, pixelCoord, frontViewZ, alpha, ior, coatWeight, coatAlpha);

                float3 R = reflect(-V, N);
                float3 envSpec = EvaluateGlassEnvironment(R, perceptualRoughness) * F;
                if (coatWeight > 1e-3f)
                {
                    float Fcoat = VividGlassFresnelDielectric(NdotV, 1.5f) * coatWeight;
                    envSpec += EvaluateGlassEnvironment(R, coatRough) * Fcoat;
                }

                // Subtle internal backface reflection cue at grazing angles (attenuated by Beer-Lambert)
                float3 internalCue = 0.0f.xxx;
                if (hasBackface && _BackfaceSpecularIntensity > 1e-3f)
                {
                    float3 Rback = reflect(-V, -backN);
                    float Fback = VividGlassFresnelDielectric(saturate(dot(-backN, V)), ior);
                    internalCue = EvaluateGlassEnvironment(Rback, min(perceptualRoughness + 0.08f, 1.0f))
                        * (Fback * (1.0f - F) * _BackfaceSpecularIntensity)
                        * volumeTransmittance;
                }

                // Combine refracted pre-exposed background with freshly pre-exposed surface reflection & emission
                float3 surfacePreExposed = VividApplyPreExposure(max(directSpec + envSpec + internalCue + max(_EmissionColor.rgb, 0.0f.xxx), 0.0f.xxx));
                float3 transmittedPreExposed = refractedPreExposed * ((1.0f - F) * transWeight) * (volumeTransmittance * baseTint);

                return float4(max(surfacePreExposed + transmittedPreExposed, 0.0f.xxx), 1.0f);
            }
            ENDHLSL
        }

        // =========================================================================
        // Pass 3: ShadowCaster
        // =========================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
            #pragma target 5.0
            #pragma use_dxc
            #pragma multi_compile_instancing
            #pragma vertex VertShadow
            #pragma fragment FragShadow

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings VertShadow(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(input.positionOS);
                return o;
            }

            void FragShadow(Varyings input)
            {
            }
            ENDHLSL
        }

        // =========================================================================
        // Pass 4: Reference Path Tracing Ground Truth (OpenPBR Transmission + Volume)
        // =========================================================================
        Pass
        {
            Name "ReferencedPathtracingDXR"
            Tags { "LightMode" = "ReferencedPathtracingDXR" }

            HLSLPROGRAM
                #pragma only_renderers d3d11 xboxseries ps5 switch2
                #pragma raytracing surface_shader
                #pragma multi_compile_raytracing _ VIVIDRP_REFERENCE_PT_FP16
                #pragma multi_compile _ INSTANCING_ON
                #pragma shader_feature_local_raytracing _NORMALMAP
                #pragma shader_feature_local_raytracing _CLEARCOAT

                #include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/ReferencedPathtracing.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
