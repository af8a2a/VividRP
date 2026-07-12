Shader "VividRP/Particles/Unlit"
{
    Properties
    {
        [Main(SurfaceOptions, _, on, off)] _SurfaceOptions("Surface Options", Float) = 1
        [SubEnum(SurfaceOptions, Opaque, 0, Transparent, 1)] _SurfaceType("Surface Type", Float) = 1.0
        [SubEnum(SurfaceOptions, Alpha, 0, Premultiply, 1, Additive, 2, Multiply, 3)] _BlendMode("Blend Mode", Float) = 0.0
        [SubToggle(SurfaceOptions, _)] _AlphaCutoffEnable("Alpha Clipping", Float) = 0.0
        [Sub(SurfaceOptions)] _AlphaCutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [SubEnum(SurfaceOptions, Back, 2, Front, 1, Off, 0)] _CullMode("Cull", Float) = 0.0
        [SubToggle(SurfaceOptions, _)] _TransparentZWrite("Transparent ZWrite", Float) = 0.0
        [Sub(SurfaceOptions)] _QueueOffset("Queue Offset", Float) = 0.0
        [HideInInspector] _TransparentSortPriority("Transparent Sort Priority", Float) = 0.0
        [HideInInspector] _DoubleSidedEnable("Double Sided Enable", Float) = 1.0

        [Main(SurfaceInputs, _, on, off)] _SurfaceInputs("Surface Inputs", Float) = 1
        [MainTexture] [Tex(SurfaceInputs, _UnlitColor)] _UnlitColorMap("Color Map", 2D) = "white" {}
        [HideInInspector] [MainColor] _UnlitColor("Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _AlphaRemapMin("Alpha Remap Min", Float) = 0.0
        [HideInInspector] _AlphaRemapMax("Alpha Remap Max", Float) = 1.0

        [HideInInspector] _SrcBlend("__src", Float) = 5.0
        [HideInInspector] _DstBlend("__dst", Float) = 10.0
        [HideInInspector] _AlphaSrcBlend("__alphaSrc", Float) = 1.0
        [HideInInspector] _AlphaDstBlend("__alphaDst", Float) = 10.0
        [HideInInspector] _ZWrite("__zw", Float) = 0.0

        [HideInInspector] _MainTex("Color Map", 2D) = "white" {}
        [HideInInspector] _Color("Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _BaseMap("Base Map", 2D) = "white" {}
        [HideInInspector] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _VividParticleRenderMode("Vivid Particle Render Mode", Float) = 0.0
        [HideInInspector] _VividParticleSharedData("Vivid Particle Shared Data", Vector) = (0, 0, 0, 0)
        [HideInInspector] _VividParticleSpanSharedData("Vivid Particle Span Shared Data", Vector) = (0, 0, 0, 0)
        [HideInInspector] _VividParticlePositionSize("Vivid Particle Position Size", Vector) = (0, 0, 0, 1)
        [HideInInspector] _VividParticleRotation("Vivid Particle Rotation", Vector) = (0, 0, 0, 1)
        [HideInInspector] _VividParticleVelocityStretch("Vivid Particle Velocity Stretch", Vector) = (0, 1, 0, 1)
        [HideInInspector] _VividParticleScale("Vivid Particle Scale", Vector) = (1, 1, 1, 1)
        [HideInInspector] _VividParticleUV("Vivid Particle UV", Vector) = (0, 0, 1, 1)
        [HideInInspector] _VividParticleCustomData1("Vivid Particle Custom Data 1", Vector) = (0, 0, 0, 0)
        [HideInInspector] _VividParticleCustomData2("Vivid Particle Custom Data 2", Vector) = (0, 0, 0, 0)
        [HideInInspector] _VividParticleMeshIndex("Vivid Particle Mesh Index", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "VividRenderPipeline"
        }

        HLSLINCLUDE
            #ifdef DOTS_INSTANCING_ON
                cbuffer UnityDOTSInstancing_BuiltinPropertyMetadata
                {
                    uint unity_DOTSInstancingF48_Metadataunity_ObjectToWorld;
                    uint unity_DOTSInstancingF48_Metadataunity_WorldToObject;
                    uint unity_DOTSInstancingF48_Metadataunity_MatrixPreviousM;
                    uint unity_DOTSInstancingF48_Metadataunity_MatrixPreviousMI;
                }

                #define unity_WorldTransformParams LoadDOTSInstancedData_WorldTransformParams()
                #define unity_RenderingLayer LoadDOTSInstancedData_RenderingLayer()
                #define UNITY_SETUP_DOTS_SH_COEFFS
                #define UNITY_SETUP_DOTS_RENDER_BOUNDS
            #endif

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/AutoExposure.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/MotionVectorsCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

        float4 _SelectionID;
        int _ObjectId;
        int _PassValue;

            CBUFFER_START(UnityPerMaterial)
                float4 _UnlitColor;
                float4 _UnlitColorMap_ST;
                float4 _BaseColor;
                float4 _VividParticleSharedData;
                float4 _VividParticleSpanSharedData;
                float4 _VividParticlePositionSize;
                float4 _VividParticleRotation;
                float4 _VividParticleVelocityStretch;
                float4 _VividParticleScale;
                float4 _VividParticleUV;
                float4 _VividParticleCustomData1;
                float4 _VividParticleCustomData2;
                float4 _VividParticleMeshIndex;
                float _AlphaCutoff;
                float _AlphaRemapMin;
                float _AlphaRemapMax;
                float _VividParticleRenderMode;
            CBUFFER_END

            TEXTURE2D(_UnlitColorMap);
            SAMPLER(sampler_UnlitColorMap);

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
                UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleSharedData)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleSpanSharedData)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticlePositionSize)
                    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleRotation)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleVelocityStretch)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleScale)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleUV)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleCustomData1)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleCustomData2)
                    UNITY_DOTS_INSTANCED_PROP(float4, _VividParticleMeshIndex)
                UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

                #define VIVID_PARTICLE_SHARED_DATA_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticleSharedData)
                #define VIVID_PARTICLE_SPAN_SHARED_DATA_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticleSpanSharedData)
                #define VIVID_PARTICLE_POSITION_SIZE_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticlePositionSize)
                #define VIVID_PARTICLE_INSTANCE_COLOR_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _BaseColor)
                #define VIVID_PARTICLE_ROTATION_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticleRotation)
                #define VIVID_PARTICLE_VELOCITY_STRETCH_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticleVelocityStretch)
                #define VIVID_PARTICLE_SCALE_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticleScale)
                #define VIVID_PARTICLE_UV_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticleUV)
                #define VIVID_PARTICLE_CUSTOM_DATA1_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticleCustomData1)
                #define VIVID_PARTICLE_CUSTOM_DATA2_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticleCustomData2)
                #define VIVID_PARTICLE_MESH_INDEX_METADATA UNITY_DOTS_INSTANCED_METADATA_NAME(float4, _VividParticleMeshIndex)
            #else
            #endif

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 particleSlot : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float particleVisible : TEXCOORD1;
                float4 instanceColor : TEXCOORD2;
                float4 positionCSNoJitter : TEXCOORD3;
                float4 previousPositionCSNoJitter : TEXCOORD4;
                float motionMode : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct VividParticleData
            {
                float4 positionSize;
                float4 instanceColor;
                float4 rotation;
                float4 velocityStretch;
                float4 scale;
                float4 uv;
                float4 customData1;
                float4 customData2;
                float4 meshIndex;
                float4 sharedSize;
                float4 rendererParameters;
                float4 localToWorld0;
                float4 localToWorld1;
                float4 localToWorld2;
                float4 localToWorld3;
                float3 pivot;
                float3 flip;
                float visible;
                float renderMode;
                float simulationSpace;
                uint particleIndex;
                uint sharpIndex;
            };

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
                static const uint VIVID_PARTICLE_PAGE_BASE_MASK = 0x00ffffffu;
                static const uint VIVID_PARTICLE_PAGE_COUNT_SHIFT = 24u;
                static const uint VIVID_PARTICLE_SHARED_DATA_STRIDE = 224u;
                static const uint VIVID_PARTICLE_BASE_COLOR_BIT = 1u << 3u;
                static const uint VIVID_PARTICLE_ROTATION_BIT = 1u << 4u;
                static const uint VIVID_PARTICLE_VELOCITY_STRETCH_BIT = 1u << 5u;
                static const uint VIVID_PARTICLE_SCALE_BIT = 1u << 6u;
                static const uint VIVID_PARTICLE_UV_BIT = 1u << 7u;
                static const uint VIVID_PARTICLE_CUSTOM_DATA1_BIT = 1u << 8u;
                static const uint VIVID_PARTICLE_CUSTOM_DATA2_BIT = 1u << 9u;
                static const uint VIVID_PARTICLE_SHARED_LOCAL_TO_WORLD_0 = 0u;
                static const uint VIVID_PARTICLE_SHARED_LOCAL_TO_WORLD_1 = 1u;
                static const uint VIVID_PARTICLE_SHARED_LOCAL_TO_WORLD_2 = 2u;
                static const uint VIVID_PARTICLE_SHARED_LOCAL_TO_WORLD_3 = 3u;
                static const uint VIVID_PARTICLE_SHARED_RENDERER_PARAMETERS = 6u;
                static const uint VIVID_PARTICLE_SHARED_VISIBILITY = 8u;
                static const uint VIVID_PARTICLE_SHARED_RUNTIME_FLAGS = 9u;
                static const uint VIVID_PARTICLE_SHARED_PIVOT = 12u;
                static const uint VIVID_PARTICLE_SHARED_FLIP = 13u;

                float4 VividLoadParticleFloat4(uint metadata, uint particleIndex, uint sharpIndex, uint dataPerSharpBits, uint dataBit, float4 defaultValue)
                {
                    if (metadata == 0u)
                        return defaultValue;

                    uint address = metadata & kAddressMask;
                    if (IsDOTSInstancedProperty(metadata))
                        address += particleIndex * 16u;
                    else if ((dataPerSharpBits & dataBit) != 0u)
                        address += sharpIndex * 16u;

                    return asfloat(DOTSInstanceData_Load4(address));
                }

                float3 VividLoadParticleFloat3(
                    uint metadata,
                    uint particleIndex,
                    uint sharpIndex,
                    uint dataPerSharpBits,
                    uint dataBit,
                    float3 defaultValue)
                {
                    if (metadata == 0u)
                        return defaultValue;

                    uint address = metadata & kAddressMask;
                    if (IsDOTSInstancedProperty(metadata))
                        address += particleIndex * 12u;
                    else if ((dataPerSharpBits & dataBit) != 0u)
                        address += sharpIndex * 12u;

                    return asfloat(DOTSInstanceData_Load3(address));
                }

                uint VividLoadParticlePackedColor(
                    uint metadata,
                    uint particleIndex,
                    uint sharpIndex,
                    uint dataPerSharpBits,
                    uint dataBit,
                    uint defaultValue)
                {
                    if (metadata == 0u)
                        return defaultValue;

                    uint address = metadata & kAddressMask;
                    if (IsDOTSInstancedProperty(metadata))
                        address += particleIndex * 4u;
                    else if ((dataPerSharpBits & dataBit) != 0u)
                        address += sharpIndex * 4u;

                    return DOTSInstanceData_Load(address);
                }

                float4 VividUnpackParticleColor(uint value)
                {
                    return float4(
                        value & 0xffu,
                        (value >> 8u) & 0xffu,
                        (value >> 16u) & 0xffu,
                        (value >> 24u) & 0xffu) * (1.0 / 255.0);
                }

                uint4 VividLoadParticleUint4(uint metadata, uint dataIndex, uint4 defaultValue)
                {
                    if (metadata == 0u)
                        return defaultValue;

                    uint address = metadata & kAddressMask;
                    if (IsDOTSInstancedProperty(metadata))
                        address += dataIndex * 16u;

                    return DOTSInstanceData_Load4(address);
                }

                float4 VividLoadParticleSharedFloat4(uint metadata, uint sharpIndex, uint elementIndex, float4 defaultValue)
                {
                    if (metadata == 0u)
                        return defaultValue;

                    uint address = (metadata & kAddressMask)
                        + sharpIndex * VIVID_PARTICLE_SHARED_DATA_STRIDE
                        + elementIndex * 16u;
                    return asfloat(DOTSInstanceData_Load4(address));
                }

                uint VividResolveParticleIndex(
                    Attributes input,
                    out float visible,
                    out uint sharpIndex,
                    out float4 rendererParameters)
                {
                    uint instanceIndex = GetDOTSInstanceIndex();
                    sharpIndex = 0u;
                    uint4 spanData = VividLoadParticleUint4(
                        VIVID_PARTICLE_SPAN_SHARED_DATA_METADATA,
                        instanceIndex,
                        uint4(
                            0u,
                            instanceIndex & VIVID_PARTICLE_PAGE_BASE_MASK,
                            instanceIndex >> VIVID_PARTICLE_PAGE_COUNT_SHIFT,
                            0u));
                    sharpIndex = spanData.x;
                    rendererParameters = VividLoadParticleSharedFloat4(
                        VIVID_PARTICLE_SHARED_DATA_METADATA,
                        sharpIndex,
                        VIVID_PARTICLE_SHARED_RENDERER_PARAMETERS,
                        float4(2.0, 0.0, 0.0, _VividParticleRenderMode));
                    if (rendererParameters.w < 3.5)
                    {
                        uint slot = (uint)(input.particleSlot.x + 0.5);
                        visible = slot <= spanData.z ? 1.0 : 0.0;
                        return spanData.y + min(slot, spanData.z);
                    }

                    visible = 1.0;
                    return spanData.y;
                }

                VividParticleData VividLoadParticleData(Attributes input)
                {
                    VividParticleData data;
                    uint particleIndex = VividResolveParticleIndex(
                        input,
                        data.visible,
                        data.sharpIndex,
                        data.rendererParameters);
                    data.particleIndex = particleIndex;
                    float4 sharedVisibility = VividLoadParticleSharedFloat4(
                        VIVID_PARTICLE_SHARED_DATA_METADATA,
                        data.sharpIndex,
                        VIVID_PARTICLE_SHARED_VISIBILITY,
                        float4(0.0, 0.0, 0.0, 1.0));
                    uint dataPerSharpBits = (uint)(sharedVisibility.x + 0.5);
                    data.positionSize = VividLoadParticleFloat4(VIVID_PARTICLE_POSITION_SIZE_METADATA, particleIndex, data.sharpIndex, dataPerSharpBits, 0u, float4(0.0, 0.0, 0.0, 1.0));
                    data.instanceColor = VividUnpackParticleColor(VividLoadParticlePackedColor(
                        VIVID_PARTICLE_INSTANCE_COLOR_METADATA,
                        particleIndex,
                        data.sharpIndex,
                        dataPerSharpBits,
                        VIVID_PARTICLE_BASE_COLOR_BIT,
                        0xffffffffu));
                    data.rotation = VividLoadParticleFloat4(VIVID_PARTICLE_ROTATION_METADATA, particleIndex, data.sharpIndex, dataPerSharpBits, VIVID_PARTICLE_ROTATION_BIT, float4(0.0, 0.0, 0.0, 1.0));
                    data.velocityStretch = float4(VividLoadParticleFloat3(
                        VIVID_PARTICLE_VELOCITY_STRETCH_METADATA,
                        particleIndex,
                        data.sharpIndex,
                        dataPerSharpBits,
                        VIVID_PARTICLE_VELOCITY_STRETCH_BIT,
                        float3(0.0, 1.0, 0.0)), 0.0);
                    data.scale = float4(VividLoadParticleFloat3(
                        VIVID_PARTICLE_SCALE_METADATA,
                        particleIndex,
                        data.sharpIndex,
                        dataPerSharpBits,
                        VIVID_PARTICLE_SCALE_BIT,
                        data.positionSize.www), 1.0);
                    data.uv = VividLoadParticleFloat4(VIVID_PARTICLE_UV_METADATA, particleIndex, data.sharpIndex, dataPerSharpBits, VIVID_PARTICLE_UV_BIT, float4(0.0, 0.0, 1.0, 1.0));
                    data.customData1 = VividLoadParticleFloat4(VIVID_PARTICLE_CUSTOM_DATA1_METADATA, particleIndex, data.sharpIndex, dataPerSharpBits, VIVID_PARTICLE_CUSTOM_DATA1_BIT, float4(0.0, 0.0, 0.0, 0.0));
                    data.customData2 = VividLoadParticleFloat4(VIVID_PARTICLE_CUSTOM_DATA2_METADATA, particleIndex, data.sharpIndex, dataPerSharpBits, VIVID_PARTICLE_CUSTOM_DATA2_BIT, float4(0.0, 0.0, 0.0, 0.0));
                    data.meshIndex = VividLoadParticleFloat4(VIVID_PARTICLE_MESH_INDEX_METADATA, particleIndex, data.sharpIndex, dataPerSharpBits, 0u, float4(0.0, 1.0, 0.0, 0.0));
                    data.sharedSize = VividLoadParticleSharedFloat4(
                        VIVID_PARTICLE_SHARED_DATA_METADATA,
                        data.sharpIndex,
                        5u,
                        float4(1.0, 0.0, 0.0, 1.0));
                    data.renderMode = data.rendererParameters.w;
                    data.localToWorld0 = VividLoadParticleSharedFloat4(
                        VIVID_PARTICLE_SHARED_DATA_METADATA,
                        data.sharpIndex,
                        VIVID_PARTICLE_SHARED_LOCAL_TO_WORLD_0,
                        float4(1.0, 0.0, 0.0, 0.0));
                    data.localToWorld1 = VividLoadParticleSharedFloat4(
                        VIVID_PARTICLE_SHARED_DATA_METADATA,
                        data.sharpIndex,
                        VIVID_PARTICLE_SHARED_LOCAL_TO_WORLD_1,
                        float4(0.0, 1.0, 0.0, 0.0));
                    data.localToWorld2 = VividLoadParticleSharedFloat4(
                        VIVID_PARTICLE_SHARED_DATA_METADATA,
                        data.sharpIndex,
                        VIVID_PARTICLE_SHARED_LOCAL_TO_WORLD_2,
                        float4(0.0, 0.0, 1.0, 0.0));
                    data.localToWorld3 = VividLoadParticleSharedFloat4(
                        VIVID_PARTICLE_SHARED_DATA_METADATA,
                        data.sharpIndex,
                        VIVID_PARTICLE_SHARED_LOCAL_TO_WORLD_3,
                        float4(0.0, 0.0, 0.0, 1.0));
                    float4 runtimeFlags = VividLoadParticleSharedFloat4(
                        VIVID_PARTICLE_SHARED_DATA_METADATA,
                        data.sharpIndex,
                        VIVID_PARTICLE_SHARED_RUNTIME_FLAGS,
                        float4(1.0, 0.0, 0.0, 0.0));
                    data.simulationSpace = runtimeFlags.x;
                    data.pivot = VividLoadParticleSharedFloat4(
                        VIVID_PARTICLE_SHARED_DATA_METADATA,
                        data.sharpIndex,
                        VIVID_PARTICLE_SHARED_PIVOT,
                        float4(0.0, 0.0, 0.0, 0.0)).xyz;
                    data.flip = VividLoadParticleSharedFloat4(
                        VIVID_PARTICLE_SHARED_DATA_METADATA,
                        data.sharpIndex,
                        VIVID_PARTICLE_SHARED_FLIP,
                        float4(0.0, 0.0, 0.0, 0.0)).xyz;
                    data.visible *= sharedVisibility.w >= 0.0 ? 1.0 : 0.0;
                    return data;
                }
            #else
                VividParticleData VividLoadParticleData(Attributes input)
                {
                    VividParticleData data;
                    data.positionSize = _VividParticlePositionSize;
                    data.instanceColor = _BaseColor;
                    data.rotation = _VividParticleRotation;
                    data.velocityStretch = _VividParticleVelocityStretch;
                    data.scale = _VividParticleScale;
                    data.uv = _VividParticleUV;
                    data.customData1 = _VividParticleCustomData1;
                    data.customData2 = _VividParticleCustomData2;
                    data.meshIndex = _VividParticleMeshIndex;
                    data.sharedSize = float4(1.0, 0.0, 0.0, 1.0);
                    data.rendererParameters = float4(2.0, 0.0, 0.0, _VividParticleRenderMode);
                    data.localToWorld0 = float4(1.0, 0.0, 0.0, 0.0);
                    data.localToWorld1 = float4(0.0, 1.0, 0.0, 0.0);
                    data.localToWorld2 = float4(0.0, 0.0, 1.0, 0.0);
                    data.localToWorld3 = float4(0.0, 0.0, 0.0, 1.0);
                    data.pivot = 0.0;
                    data.flip = 0.0;
                    data.visible = 1.0;
                    data.renderMode = _VividParticleRenderMode;
                    data.simulationSpace = 1.0;
                    data.particleIndex = 0u;
                    data.sharpIndex = 0u;
                    return data;
                }
            #endif

            float2 TransformParticleUnlitUV(float2 uv)
            {
                return uv * _UnlitColorMap_ST.xy + _UnlitColorMap_ST.zw;
            }

            float4 SampleParticleUnlitColor(float2 uv)
            {
                float4 colorSample = SAMPLE_TEXTURE2D(_UnlitColorMap, sampler_UnlitColorMap, uv);
                float alpha = lerp(_AlphaRemapMin, _AlphaRemapMax, colorSample.a) * _UnlitColor.a;
                return float4(colorSample.rgb * _UnlitColor.rgb, alpha);
            }

            void ApplyParticleUnlitAlphaClip(float alpha)
            {
            #if defined(_ALPHATEST_ON)
                clip(alpha - _AlphaCutoff);
            #endif
            }

            float3 RotateParticleVector(float3 value, float4 rotation)
            {
                float rotationLengthSq = dot(rotation, rotation);
                if (rotationLengthSq <= 0.000001)
                    return value;

                rotation *= rsqrt(rotationLengthSq);
                float3 t = 2.0 * cross(rotation.xyz, value);
                return value + rotation.w * t + cross(rotation.xyz, t);
            }

            float3 SafeNormalizeParticleVector(float3 value, float3 fallback)
            {
                float lengthSq = dot(value, value);
                return lengthSq > 0.000001 ? value * rsqrt(lengthSq) : fallback;
            }

            float3 TransformParticleSimulationPosition(VividParticleData particle, float3 position)
            {
                if (particle.simulationSpace >= 0.5)
                    return position;

                return particle.localToWorld0.xyz * position.x
                    + particle.localToWorld1.xyz * position.y
                    + particle.localToWorld2.xyz * position.z
                    + particle.localToWorld3.xyz;
            }

            float3 TransformParticleSimulationDirection(VividParticleData particle, float3 direction)
            {
                if (particle.simulationSpace >= 0.5)
                    return direction;

                return particle.localToWorld0.xyz * direction.x
                    + particle.localToWorld1.xyz * direction.y
                    + particle.localToWorld2.xyz * direction.z;
            }

            uint VividParticleHash(uint value)
            {
                value = ((value >> ((value >> 28u) + 4u)) ^ value) * 277803737u;
                return (value >> 22u) ^ value;
            }

            bool ShouldFlipParticleAxis(uint particleIndex, float probability, uint salt)
            {
                if (probability <= 0.0)
                    return false;

                if (probability >= 1.0)
                    return true;

                uint hash = VividParticleHash((particleIndex + 1u) * 747796405u + salt);
                return (float)hash * (1.0 / 4294967295.0) < probability;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VividParticleData particle = VividLoadParticleData(input);
                output.particleVisible = particle.visible;
                output.instanceColor = particle.instanceColor;
                output.uv = TransformParticleUnlitUV(input.uv * particle.uv.zw + particle.uv.xy);
                if (particle.visible <= 0.0)
                {
                    output.positionCS = float4(0.0, 0.0, 0.0, 1.0);
                    return output;
                }

                float4 positionSize = particle.positionSize;
                float4 rotation = particle.rotation;
                float4 velocityStretch = particle.velocityStretch;
                float3 centerRWS = TransformParticleSimulationPosition(particle, positionSize.xyz);
                float3 velocityRWS = TransformParticleSimulationDirection(particle, velocityStretch.xyz);
                float size = max(particle.scale.x, 0.0001);
                size = max(size, particle.sharedSize.y);
                if (particle.sharedSize.z > 0.0)
                    size = min(size, max(particle.sharedSize.z, particle.sharedSize.y));
                float4x4 viewToWorld = GetViewToWorldMatrix();
                float3 viewRight = normalize(float3(viewToWorld._m00, viewToWorld._m10, viewToWorld._m20));
                float3 viewUp = normalize(float3(viewToWorld._m01, viewToWorld._m11, viewToWorld._m21));
                float3 viewForward = -normalize(float3(viewToWorld._m02, viewToWorld._m12, viewToWorld._m22));
                float3 particleLocal = input.positionOS;
                particleLocal.x = ShouldFlipParticleAxis(particle.particleIndex, particle.flip.x, 0x9E3779B9u) ? -particleLocal.x : particleLocal.x;
                particleLocal.y = ShouldFlipParticleAxis(particle.particleIndex, particle.flip.y, 0xBB67AE85u) ? -particleLocal.y : particleLocal.y;
                particleLocal.z = ShouldFlipParticleAxis(particle.particleIndex, particle.flip.z, 0x3C6EF372u) ? -particleLocal.z : particleLocal.z;
                float3 rotatedParticleLocal = RotateParticleVector(
                    particleLocal - particle.pivot,
                    rotation);
                float3 meshLocal = rotatedParticleLocal * size;
                float3 positionRWS;

                if (particle.renderMode < 0.5)
                {
                    positionRWS = centerRWS
                        + viewRight * meshLocal.x
                        + viewUp * meshLocal.y;
                }
                else if (particle.renderMode < 1.5)
                {
                    float3 stretchUp = SafeNormalizeParticleVector(velocityRWS, viewUp);
                    float3 stretchRight = cross(viewForward, stretchUp);
                    stretchRight = SafeNormalizeParticleVector(stretchRight, viewRight);
                    float stretchLength = max(
                        size,
                        size * particle.rendererParameters.x
                            + length(velocityRWS) * particle.rendererParameters.y);
                    positionRWS = centerRWS
                        + stretchRight * meshLocal.x
                        + stretchUp * (rotatedParticleLocal.y * stretchLength);
                }
                else if (particle.renderMode < 2.5)
                {
                    positionRWS = centerRWS
                        + float3(1.0, 0.0, 0.0) * meshLocal.x
                        + float3(0.0, 0.0, 1.0) * meshLocal.y;
                }
                else if (particle.renderMode < 3.5)
                {
                    float3 cameraPositionRWS = float3(viewToWorld._m03, viewToWorld._m13, viewToWorld._m23);
                    float3 toCamera = cameraPositionRWS - centerRWS;
                    toCamera.y = 0.0;
                    float toCameraLength = length(toCamera);
                    float3 verticalNormal = toCameraLength > 0.0001 ? toCamera / toCameraLength : -float3(viewToWorld._m02, 0.0, viewToWorld._m22);
                    float3 verticalRight = cross(float3(0.0, 1.0, 0.0), verticalNormal);
                    verticalRight = length(verticalRight) > 0.0001 ? normalize(verticalRight) : viewRight;
                    positionRWS = centerRWS
                        + verticalRight * meshLocal.x
                        + float3(0.0, 1.0, 0.0) * meshLocal.y;
                }
                else
                {
                    positionRWS = centerRWS + meshLocal;
                }

                output.positionCS = TransformWorldToHClip(positionRWS);
                output.positionCSNoJitter = mul(
                    _NonJitteredViewProjMatrix,
                    float4(positionRWS, 1.0));
                output.previousPositionCSNoJitter = particle.rendererParameters.z > 1.5
                    ? output.positionCSNoJitter
                    : mul(_PrevViewProjMatrix, float4(positionRWS, 1.0));
                output.motionMode = particle.rendererParameters.z;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                clip(input.particleVisible - 0.5);
                float4 color = SampleParticleUnlitColor(input.uv) * input.instanceColor;
                ApplyParticleUnlitAlphaClip(color.a);
                return float4((color.rgb), color.a);
            }

            float4 FragPicking(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                clip(input.particleVisible - 0.5);
                float4 color = SampleParticleUnlitColor(input.uv) * input.instanceColor;
                ApplyParticleUnlitAlphaClip(color.a);
                return _SelectionID;
            }

            float4 FragSceneSelection(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                clip(input.particleVisible - 0.5);
                float4 color = SampleParticleUnlitColor(input.uv) * input.instanceColor;
                ApplyParticleUnlitAlphaClip(color.a);
                return float4(_ObjectId, _PassValue, 1.0, 1.0);
            }

            float4 FragMotionVectors(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                clip(input.particleVisible - 0.5);
                float4 color = SampleParticleUnlitColor(input.uv) * input.instanceColor;
                ApplyParticleUnlitAlphaClip(color.a);
                if (input.motionMode > 1.5)
                    return 0.0;

                return EncodeMotionVectorFromCsPositions(
                    input.positionCSNoJitter,
                    input.previousPositionCSNoJitter);
            }
        ENDHLSL

        Pass
        {
            Name "VividForward"
            Tags { "LightMode" = "VividForward" }

            Blend [_SrcBlend] [_DstBlend], [_AlphaSrcBlend] [_AlphaDstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_CullMode]

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma multi_compile _ DOTS_INSTANCING_ON
                #pragma shader_feature_local _SURFACE_TYPE_TRANSPARENT
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma vertex Vert
                #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "SRPDefaultUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend [_SrcBlend] [_DstBlend], [_AlphaSrcBlend] [_AlphaDstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_CullMode]

            HLSLPROGRAM
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma multi_compile _ DOTS_INSTANCING_ON
                #pragma shader_feature_local _SURFACE_TYPE_TRANSPARENT
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma vertex Vert
                #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            ColorMask RG
            ZWrite On
            ZTest LEqual
            Cull [_CullMode]
            Stencil
            {
                WriteMask 32
                Ref 32
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
                #define VIVID_PARTICLE_MOTION_VECTORS_PASS 1
                #pragma target 4.5
                #pragma multi_compile_instancing
                #pragma multi_compile _ DOTS_INSTANCING_ON
                #pragma shader_feature_local _SURFACE_TYPE_TRANSPARENT
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma vertex Vert
                #pragma fragment FragMotionVectors
            ENDHLSL
        }

        Pass
        {
            Name "ScenePickingPass"
            Tags { "LightMode" = "Picking" }

            ZWrite On
            ZTest LEqual
            Cull [_CullMode]

            HLSLPROGRAM

                #pragma target 4.5
                #pragma editor_sync_compilation
                #pragma multi_compile_instancing
                #pragma multi_compile _ DOTS_INSTANCING_ON
                #pragma shader_feature_local _SURFACE_TYPE_TRANSPARENT
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma vertex Vert
                #pragma fragment FragPicking
            ENDHLSL
        }

        Pass
        {
            Name "SceneSelectionPass"
            Tags { "LightMode" = "SceneSelectionPass" }

            ZWrite On
            ZTest LEqual
            Cull [_CullMode]

            HLSLPROGRAM

                #pragma target 4.5
                #pragma editor_sync_compilation
                #pragma multi_compile_instancing
                #pragma multi_compile _ DOTS_INSTANCING_ON
                #pragma shader_feature_local _SURFACE_TYPE_TRANSPARENT
                #pragma shader_feature_local_fragment _ALPHATEST_ON
                #pragma vertex Vert
                #pragma fragment FragSceneSelection
            ENDHLSL
        }
    }

    CustomEditor "VividRP.Editor.ParticleUnlitShaderGUI"

    FallBack Off
}
