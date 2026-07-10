using System;
using UnityEngine;
using VividRP.Runtime.ECS;

namespace VividRP.Runtime.Particle.ECS
{
    internal static class VividParticleEcsBootstrap
    {
        private static bool s_Registered;

        public static void RegisterTypes()
        {
            if (s_Registered)
                return;

            VividEcsTypeManager.RegisterSoa<VividParticleCommon>();
            VividEcsTypeManager.RegisterShared<VividParticleSystemId>();
            VividEcsTypeManager.RegisterShared<VividParticleRendererSharedKey>();
            s_Registered = true;
        }
    }

    internal readonly struct VividParticleSystemId : IVividEcsSharedComponentData, IEquatable<VividParticleSystemId>
    {
        public static readonly VividParticleSystemId Invalid = new(-1);

        public VividParticleSystemId(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value >= 0;

        public bool Equals(VividParticleSystemId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is VividParticleSystemId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }
    }

    internal readonly struct VividParticleRendererSharedKey : IVividEcsSharedComponentData, IEquatable<VividParticleRendererSharedKey>
    {
        public static readonly VividParticleRendererSharedKey Invalid = new(
            0,
            0,
            -1,
            -1,
            0,
            0u,
            0,
            0,
            uint.MaxValue,
            false,
            false,
            0,
            0,
            (int)MotionVectorGenerationMode.ForceNoMotion);

        public VividParticleRendererSharedKey(
            int materialId,
            int meshId,
            int renderMode,
            int layer,
            int gpuDataLayoutHash,
            uint dataPerSharpBits,
            int shadowCastingMode,
            int sortMode,
            uint renderingLayerMask,
            bool receiveShadows,
            bool staticShadowCaster = false,
            int sortingPriority = 0,
            int batchLayer = 0,
            int motionMode = (int)MotionVectorGenerationMode.ForceNoMotion)
        {
            MaterialId = materialId;
            MeshId = meshId;
            RenderMode = renderMode;
            Layer = layer;
            GpuDataLayoutHash = gpuDataLayoutHash;
            DataPerSharpBits = dataPerSharpBits;
            ShadowCastingMode = shadowCastingMode;
            SortMode = sortMode;
            RenderingLayerMask = renderingLayerMask;
            ReceiveShadows = receiveShadows;
            StaticShadowCaster = staticShadowCaster;
            SortingPriority = sortingPriority;
            BatchLayer = batchLayer;
            MotionMode = motionMode;
        }

        public int MaterialId { get; }

        public int MeshId { get; }

        public int RenderMode { get; }

        public int Layer { get; }

        public int GpuDataLayoutHash { get; }

        public uint DataPerSharpBits { get; }

        public int ShadowCastingMode { get; }

        public int SortMode { get; }

        public uint RenderingLayerMask { get; }

        public bool ReceiveShadows { get; }

        public bool StaticShadowCaster { get; }

        public int SortingPriority { get; }

        public int BatchLayer { get; }

        public int MotionMode { get; }

        public bool Equals(VividParticleRendererSharedKey other)
        {
            return MaterialId == other.MaterialId
                && MeshId == other.MeshId
                && RenderMode == other.RenderMode
                && Layer == other.Layer
                && GpuDataLayoutHash == other.GpuDataLayoutHash
                && DataPerSharpBits == other.DataPerSharpBits
                && ShadowCastingMode == other.ShadowCastingMode
                && SortMode == other.SortMode
                && RenderingLayerMask == other.RenderingLayerMask
                && ReceiveShadows == other.ReceiveShadows
                && StaticShadowCaster == other.StaticShadowCaster
                && SortingPriority == other.SortingPriority
                && BatchLayer == other.BatchLayer
                && MotionMode == other.MotionMode;
        }

        public override bool Equals(object obj)
        {
            return obj is VividParticleRendererSharedKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MaterialId;
                hash = (hash * 397) ^ MeshId;
                hash = (hash * 397) ^ RenderMode;
                hash = (hash * 397) ^ Layer;
                hash = (hash * 397) ^ GpuDataLayoutHash;
                hash = (hash * 397) ^ (int)DataPerSharpBits;
                hash = (hash * 397) ^ ShadowCastingMode;
                hash = (hash * 397) ^ SortMode;
                hash = (hash * 397) ^ (int)RenderingLayerMask;
                hash = (hash * 397) ^ ReceiveShadows.GetHashCode();
                hash = (hash * 397) ^ StaticShadowCaster.GetHashCode();
                hash = (hash * 397) ^ SortingPriority;
                hash = (hash * 397) ^ BatchLayer;
                hash = (hash * 397) ^ MotionMode;
                return hash;
            }
        }
    }

    internal struct VividParticleCommon : IVividEcsSoaComponentData
    {
        public const int PositionFieldIndex = 0;
        public const int VelocityFieldIndex = 1;
        public const int StartLifetimeFieldIndex = 2;
        public const int RemainingLifetimeFieldIndex = 3;
        public const int StartColorFieldIndex = 4;
        public const int SizeFieldIndex = 5;
        public const int MeshIndexFieldIndex = 6;
        public const int FieldCountValue = 7;
        public const int FloatSizeInBytes = sizeof(float);
        public const int IntSizeInBytes = sizeof(int);
        public const int Float3SizeInBytes = sizeof(float) * 3;
        public const int Float4SizeInBytes = sizeof(float) * 4;
        public const int PositionOffsetInPage = 0;
        public const int VelocityOffsetInPage = PositionOffsetInPage + Float3SizeInBytes * VividEcsConstants.PageEntryCount;
        public const int StartLifetimeOffsetInPage = VelocityOffsetInPage + Float3SizeInBytes * VividEcsConstants.PageEntryCount;
        public const int RemainingLifetimeOffsetInPage = StartLifetimeOffsetInPage + FloatSizeInBytes * VividEcsConstants.PageEntryCount;
        public const int StartColorOffsetInPage = RemainingLifetimeOffsetInPage + FloatSizeInBytes * VividEcsConstants.PageEntryCount;
        public const int SizeOffsetInPage = StartColorOffsetInPage + Float4SizeInBytes * VividEcsConstants.PageEntryCount;
        public const int MeshIndexOffsetInPage = SizeOffsetInPage + FloatSizeInBytes * VividEcsConstants.PageEntryCount;
        public const int TypeSizeInBytes = MeshIndexOffsetInPage + IntSizeInBytes * VividEcsConstants.PageEntryCount;

        public int FieldCount => FieldCountValue;

        public int TypeSize => TypeSizeInBytes;

        public VividEcsSoaFieldInfo GetFieldInfo(int index)
        {
            return index switch
            {
                PositionFieldIndex => new VividEcsSoaFieldInfo(PositionOffsetInPage, Float3SizeInBytes),
                VelocityFieldIndex => new VividEcsSoaFieldInfo(VelocityOffsetInPage, Float3SizeInBytes),
                StartLifetimeFieldIndex => new VividEcsSoaFieldInfo(StartLifetimeOffsetInPage, FloatSizeInBytes),
                RemainingLifetimeFieldIndex => new VividEcsSoaFieldInfo(RemainingLifetimeOffsetInPage, FloatSizeInBytes),
                StartColorFieldIndex => new VividEcsSoaFieldInfo(StartColorOffsetInPage, Float4SizeInBytes),
                SizeFieldIndex => new VividEcsSoaFieldInfo(SizeOffsetInPage, FloatSizeInBytes),
                MeshIndexFieldIndex => new VividEcsSoaFieldInfo(MeshIndexOffsetInPage, IntSizeInBytes),
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
        }

        public static VividEcsSoaFieldInfo GetStaticFieldInfo(int index)
        {
            return default(VividParticleCommon).GetFieldInfo(index);
        }
    }
}
