using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime
{
    internal enum DDGIVolumeTextureFormat : uint
    {
        U32 = 0,
        F16 = 1,
        F16x2 = 2,
        F16x4 = 3,
        F32 = 4,
        F32x2 = 5,
        F32x4 = 6,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DDGIInstanceData
    {
        public float4x4 ObjectToWorldMatrix;
        public float4x4 WorldToObjectMatrix;
        public uint FirstSubMeshIndex;
        public uint SubMeshCount;
        public uint Padding0;
        public uint Padding1;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DDGISubMeshData
    {
        public uint MaterialIndex;
        public uint PrimitiveOffset;
        public uint PrimitiveCount;
        public uint IndexOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DDGIMaterialData
    {
        public Vector4 BaseColor;
        public Vector4 EmissiveColor;
        public Vector4 BaseMapST;
        public float Metallic;
        public uint BaseMapIndex;
        public float Padding0;
        public float Padding1;

        public const uint InvalidTextureIndex = uint.MaxValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DDGIVertexData
    {
        public Vector4 PositionOS;
        public Vector4 NormalOS;
        public Vector4 TexCoord0;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DDGIRootConstants
    {
        public uint volumeIndex;
        public uint volumeConstantsIndex;
        public uint volumeResourceIndicesIndex;
        public uint reductionInputSizeX;
        public uint reductionInputSizeY;
        public uint reductionInputSizeZ;

        public static readonly int ConstantBufferShaderId = Shader.PropertyToID("DDGI");
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DDGIVolumeDescGPUPacked
    {
        public Vector3 origin;
        public float probeHysteresis;
        public Vector4 rotation;
        public Vector4 probeRayRotation;
        public float probeMaxRayDistance;
        public float probeNormalBias;
        public float probeViewBias;
        public float probeDistanceExponent;
        public float probeIrradianceEncodingGamma;
        public float probeIrradianceThreshold;
        public float probeBrightnessThreshold;
        public float probeMinFrontfaceDistance;
        public Vector3 probeSpacing;
        public uint packed0;
        public uint packed1;
        public uint packed2;
        public uint packed3;
        public uint packed4;
        public Vector4 reserved;

        public static DDGIVolumeDescGPUPacked Create(DDGIVolume volume, DDGIProfile profile)
        {
            Vector3Int probeCounts = volume != null ? volume.ProbeCounts : Vector3Int.one;
            float randomBackfaceThreshold = Mathf.Clamp01(profile.RandomBackfaceThreshold);
            float fixedBackfaceThreshold = Mathf.Clamp01(profile.FixedBackfaceThreshold);

            uint packed0 = (uint)Mathf.Clamp(probeCounts.x, 0, 1023);
            packed0 |= (uint)Mathf.Clamp(probeCounts.y, 0, 1023) << 10;
            packed0 |= (uint)Mathf.Clamp(probeCounts.z, 0, 1023) << 20;

            uint packed1 = (uint)Mathf.RoundToInt(randomBackfaceThreshold * 65535.0f);
            packed1 |= (uint)Mathf.RoundToInt(fixedBackfaceThreshold * 65535.0f) << 16;

            uint packed2 = (uint)profile.RaysPerProbe;
            packed2 |= (uint)profile.IrradianceInteriorTexelCount << 16;
            packed2 |= (uint)profile.DistanceInteriorTexelCount << 24;

            uint packed4 = 0u;
            packed4 |= ((uint)DDGIVolumeTextureFormat.F32x2) << 17;
            packed4 |= ((uint)DDGIVolumeTextureFormat.U32) << 20;

            return new DDGIVolumeDescGPUPacked
            {
                origin = volume != null ? volume.transform.position : Vector3.zero,
                probeHysteresis = profile.Hysteresis,
                rotation = new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                probeRayRotation = new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                probeMaxRayDistance = volume != null ? volume.ProbeMaxRayDistance : 0.0f,
                probeNormalBias = volume != null ? volume.ProbeNormalBias : 0.0f,
                probeViewBias = volume != null ? volume.ProbeViewBias : 0.0f,
                probeDistanceExponent = profile.DistanceExponent,
                probeIrradianceEncodingGamma = profile.IrradianceEncodingGamma,
                probeIrradianceThreshold = profile.IrradianceThreshold,
                probeBrightnessThreshold = profile.BrightnessThreshold,
                probeMinFrontfaceDistance = profile.MinFrontfaceDistance,
                probeSpacing = volume != null ? volume.ProbeSpacing : Vector3.one,
                packed0 = packed0,
                packed1 = packed1,
                packed2 = packed2,
                packed3 = 0u,
                packed4 = packed4,
                reserved = Vector4.zero,
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ShaderVariablesDDGI
    {
        public static readonly int ConstantBufferShaderId = Shader.PropertyToID("ShaderVariablesDDGI");

        public Vector4 _DDGIWorldAabbMin_BlendDistance;
        public Vector4 _DDGIWorldAabbMax_Enabled;
        public Vector4 _DDGIVolumeOrigin_ProbeNormalBias;
        public Vector4 _DDGIVolumeRotation;
        public Vector4 _DDGIProbeSpacing_ProbeViewBias;
        public Vector4 _DDGIProbeCounts_IrradianceInteriorTexels;
        public Vector4 _DDGIDistanceInteriorTexels_IrradianceGamma_IrradianceFormat;

        public static ShaderVariablesDDGI CreateDisabled()
        {
            return new ShaderVariablesDDGI
            {
                _DDGIWorldAabbMin_BlendDistance = Vector4.zero,
                _DDGIWorldAabbMax_Enabled = Vector4.zero,
                _DDGIVolumeOrigin_ProbeNormalBias = Vector4.zero,
                _DDGIVolumeRotation = new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                _DDGIProbeSpacing_ProbeViewBias = new Vector4(1.0f, 1.0f, 1.0f, 0.0f),
                _DDGIProbeCounts_IrradianceInteriorTexels = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
                _DDGIDistanceInteriorTexels_IrradianceGamma_IrradianceFormat =
                    new Vector4(1.0f, 1.0f, (float)DDGIVolumeTextureFormat.U32, 0.0f),
            };
        }

        public static ShaderVariablesDDGI Create(DDGIVolume volume, DDGIProfile profile)
        {
            Bounds worldBounds = volume != null ? volume.WorldBounds : default;
            Vector3Int probeCounts = volume != null ? volume.ProbeCounts : Vector3Int.one;
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;

            return new ShaderVariablesDDGI
            {
                _DDGIWorldAabbMin_BlendDistance = new Vector4(
                    min.x,
                    min.y,
                    min.z,
                    volume != null ? volume.BlendDistance : 0.0f),
                _DDGIWorldAabbMax_Enabled = new Vector4(
                    max.x,
                    max.y,
                    max.z,
                    volume != null ? 1.0f : 0.0f),
                _DDGIVolumeOrigin_ProbeNormalBias = new Vector4(
                    volume != null ? volume.transform.position.x : 0.0f,
                    volume != null ? volume.transform.position.y : 0.0f,
                    volume != null ? volume.transform.position.z : 0.0f,
                    volume != null ? volume.ProbeNormalBias : 0.0f),
                _DDGIVolumeRotation = new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                _DDGIProbeSpacing_ProbeViewBias = new Vector4(
                    volume != null ? volume.ProbeSpacing.x : 1.0f,
                    volume != null ? volume.ProbeSpacing.y : 1.0f,
                    volume != null ? volume.ProbeSpacing.z : 1.0f,
                    volume != null ? volume.ProbeViewBias : 0.0f),
                _DDGIProbeCounts_IrradianceInteriorTexels = new Vector4(
                    probeCounts.x,
                    probeCounts.y,
                    probeCounts.z,
                    profile.IrradianceInteriorTexelCount),
                _DDGIDistanceInteriorTexels_IrradianceGamma_IrradianceFormat = new Vector4(
                    profile.DistanceInteriorTexelCount,
                    profile.IrradianceEncodingGamma,
                    (float)DDGIVolumeTextureFormat.U32,
                    0.0f),
            };
        }
    }
}
