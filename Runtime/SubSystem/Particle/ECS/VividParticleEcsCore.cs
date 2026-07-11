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
            VividEcsTypeManager.RegisterSoa<VividParticleAnimatedMotion>();
            VividEcsTypeManager.RegisterSoa<VividParticleNoiseState>();
            VividEcsTypeManager.RegisterSoa<VividParticleInheritVelocityState>();
            VividEcsTypeManager.RegisterSoa<VividParticleTriggerState>();
            VividEcsTypeManager.RegisterShared<VividParticleSystemId>();
            VividEcsTypeManager.RegisterShared<VividParticleModuleSharedKey>();
            VividEcsTypeManager.RegisterShared<VividParticleSimulationKernelSharedKey>();
            VividEcsTypeManager.RegisterShared<VividParticleRenderKernelSharedKey>();
            VividEcsTypeManager.RegisterShared<VividParticleRendererSharedKey>();
            VividEcsTypeManager.RegisterShared<VividParticleRendererHandle>();
            VividEcsTypeManager.RegisterTag<VividParticleSimulationActive>();
            VividEcsTypeManager.RegisterTag<VividParticleRendererActive>();
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

    internal readonly struct VividParticleRendererHandle : IVividEcsSharedComponentData, IEquatable<VividParticleRendererHandle>
    {
        public static readonly VividParticleRendererHandle Invalid = new(-1, -1);

        public VividParticleRendererHandle(int recordSlot, int recordVersion)
        {
            RecordSlot = recordSlot;
            RecordVersion = recordVersion;
        }

        public int RecordSlot { get; }

        public int RecordVersion { get; }

        public bool IsValid => RecordSlot >= 0 && RecordVersion >= 0;

        public bool Equals(VividParticleRendererHandle other)
        {
            return RecordSlot == other.RecordSlot && RecordVersion == other.RecordVersion;
        }

        public override bool Equals(object obj)
        {
            return obj is VividParticleRendererHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (RecordSlot * 397) ^ RecordVersion;
            }
        }
    }

    internal struct VividParticleRendererActive : IVividEcsTagComponentData
    {
    }

    internal struct VividParticleSimulationActive : IVividEcsTagComponentData
    {
    }

    [Flags]
    internal enum VividParticleModuleFlags : uint
    {
        None = 0u,
        ForceOverLifetime = 1u << 0,
        VelocityOverLifetime = 1u << 1,
        ColorOverLifetime = 1u << 2,
        SizeOverLifetime = 1u << 3,
        RotationOverLifetime = 1u << 4,
        MeshRenderer = 1u << 5,
        StretchRenderer = 1u << 6,
        Sorting = 1u << 7,
        TextureSheetAnimation = 1u << 8,
        LimitVelocityOverLifetime = 1u << 9,
        ColorBySpeed = 1u << 10,
        SizeBySpeed = 1u << 11,
        RotationBySpeed = 1u << 12,
        Noise = 1u << 13,
        InheritVelocity = 1u << 14,
        CustomData = 1u << 15,
        ExternalForces = 1u << 16,
        Collision = 1u << 17,
        Trigger = 1u << 18,
    }

    internal readonly struct VividParticleModuleSharedKey : IVividEcsSharedComponentData,
        IEquatable<VividParticleModuleSharedKey>
    {
        public static readonly VividParticleModuleSharedKey None = new(VividParticleModuleFlags.None);

        public VividParticleModuleSharedKey(VividParticleModuleFlags enabledFlags)
        {
            EnabledFlags = enabledFlags;
        }

        public VividParticleModuleFlags EnabledFlags { get; }

        public bool Has(VividParticleModuleFlags flags)
        {
            return (EnabledFlags & flags) == flags;
        }

        public bool Equals(VividParticleModuleSharedKey other)
        {
            return EnabledFlags == other.EnabledFlags;
        }

        public override bool Equals(object obj)
        {
            return obj is VividParticleModuleSharedKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)EnabledFlags;
        }
    }

    internal readonly struct VividParticleSimulationKernelSharedKey : IVividEcsSharedComponentData,
        IEquatable<VividParticleSimulationKernelSharedKey>
    {
        public static readonly VividParticleSimulationKernelSharedKey Base = new(
            VividParticleModuleFlags.None);

        public VividParticleSimulationKernelSharedKey(VividParticleModuleFlags enabledFlags)
        {
            EnabledFlags = enabledFlags & (
                VividParticleModuleFlags.VelocityOverLifetime
                | VividParticleModuleFlags.LimitVelocityOverLifetime
                | VividParticleModuleFlags.RotationBySpeed
                | VividParticleModuleFlags.Noise
                | VividParticleModuleFlags.InheritVelocity
                | VividParticleModuleFlags.ExternalForces
                | VividParticleModuleFlags.Collision
                | VividParticleModuleFlags.Trigger);
        }

        public VividParticleModuleFlags EnabledFlags { get; }

        public bool Has(VividParticleModuleFlags flags)
        {
            return (EnabledFlags & flags) == flags;
        }

        public bool Equals(VividParticleSimulationKernelSharedKey other)
        {
            return EnabledFlags == other.EnabledFlags;
        }

        public override bool Equals(object obj)
        {
            return obj is VividParticleSimulationKernelSharedKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)EnabledFlags;
        }
    }

    internal readonly struct VividParticleRenderKernelSharedKey : IVividEcsSharedComponentData,
        IEquatable<VividParticleRenderKernelSharedKey>
    {
        private const VividParticleModuleFlags RenderKernelMask =
            VividParticleModuleFlags.VelocityOverLifetime
            | VividParticleModuleFlags.ColorOverLifetime
            | VividParticleModuleFlags.SizeOverLifetime
            | VividParticleModuleFlags.RotationOverLifetime
            | VividParticleModuleFlags.TextureSheetAnimation
            | VividParticleModuleFlags.ColorBySpeed
            | VividParticleModuleFlags.SizeBySpeed
            | VividParticleModuleFlags.RotationBySpeed
            | VividParticleModuleFlags.Noise
            | VividParticleModuleFlags.InheritVelocity
            | VividParticleModuleFlags.CustomData;

        public static readonly VividParticleRenderKernelSharedKey Base = new(
            VividParticleModuleFlags.None);

        public VividParticleRenderKernelSharedKey(VividParticleModuleFlags enabledFlags)
        {
            EnabledFlags = enabledFlags & RenderKernelMask;
        }

        public VividParticleModuleFlags EnabledFlags { get; }

        public bool Has(VividParticleModuleFlags flags)
        {
            return (EnabledFlags & flags) == flags;
        }

        public bool Equals(VividParticleRenderKernelSharedKey other)
        {
            return EnabledFlags == other.EnabledFlags;
        }

        public override bool Equals(object obj)
        {
            return obj is VividParticleRenderKernelSharedKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)EnabledFlags;
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
        public const int AccumulatedRotationFieldIndex = 7;
        public const int FieldCountValue = 8;
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
        public const int AccumulatedRotationOffsetInPage =
            MeshIndexOffsetInPage + IntSizeInBytes * VividEcsConstants.PageEntryCount;
        public const int TypeSizeInBytes =
            AccumulatedRotationOffsetInPage + Float3SizeInBytes * VividEcsConstants.PageEntryCount;

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
                AccumulatedRotationFieldIndex =>
                    new VividEcsSoaFieldInfo(AccumulatedRotationOffsetInPage, Float3SizeInBytes),
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
        }

        public static VividEcsSoaFieldInfo GetStaticFieldInfo(int index)
        {
            return default(VividParticleCommon).GetFieldInfo(index);
        }
    }

    internal struct VividParticleAnimatedMotion : IVividEcsSoaComponentData
    {
        public const int VelocityFieldIndex = 0;
        public const int FieldCountValue = 1;
        public const int Float3SizeInBytes = sizeof(float) * 3;
        public const int VelocityOffsetInPage = 0;
        public const int TypeSizeInBytes = Float3SizeInBytes * VividEcsConstants.PageEntryCount;

        public int FieldCount => FieldCountValue;

        public int TypeSize => TypeSizeInBytes;

        public VividEcsSoaFieldInfo GetFieldInfo(int index)
        {
            return index == VelocityFieldIndex
                ? new VividEcsSoaFieldInfo(VelocityOffsetInPage, Float3SizeInBytes)
                : throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    internal struct VividParticleNoiseState : IVividEcsSoaComponentData
    {
        public const int PhaseFieldIndex = 0;
        public const int SizeMultiplierFieldIndex = 1;
        public const int FieldCountValue = 2;
        public const int Float3SizeInBytes = sizeof(float) * 3;
        public const int FloatSizeInBytes = sizeof(float);
        public const int PhaseOffsetInPage = 0;
        public const int SizeMultiplierOffsetInPage =
            PhaseOffsetInPage + Float3SizeInBytes * VividEcsConstants.PageEntryCount;
        public const int TypeSizeInBytes = SizeMultiplierOffsetInPage
            + FloatSizeInBytes * VividEcsConstants.PageEntryCount;

        public int FieldCount => FieldCountValue;

        public int TypeSize => TypeSizeInBytes;

        public VividEcsSoaFieldInfo GetFieldInfo(int index)
        {
            return index switch
            {
                PhaseFieldIndex => new VividEcsSoaFieldInfo(PhaseOffsetInPage, Float3SizeInBytes),
                SizeMultiplierFieldIndex => new VividEcsSoaFieldInfo(
                    SizeMultiplierOffsetInPage,
                    FloatSizeInBytes),
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
        }
    }

    internal struct VividParticleInheritVelocityState : IVividEcsSoaComponentData
    {
        public const int InitialVelocityFieldIndex = 0;
        public const int FieldCountValue = 1;
        public const int Float3SizeInBytes = sizeof(float) * 3;
        public const int InitialVelocityOffsetInPage = 0;
        public const int TypeSizeInBytes = Float3SizeInBytes * VividEcsConstants.PageEntryCount;

        public int FieldCount => FieldCountValue;

        public int TypeSize => TypeSizeInBytes;

        public VividEcsSoaFieldInfo GetFieldInfo(int index)
        {
            return index == InitialVelocityFieldIndex
                ? new VividEcsSoaFieldInfo(InitialVelocityOffsetInPage, Float3SizeInBytes)
                : throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    internal struct VividParticleTriggerState : IVividEcsSoaComponentData
    {
        public const int PreviousInsideFieldIndex = 0;
        public const int CurrentInsideFieldIndex = 1;
        public const int ColliderEntityIdFieldIndex = 2;
        public const int FieldCountValue = 3;
        public const int ByteSizeInBytes = sizeof(byte);
        public const int UlongSizeInBytes = sizeof(ulong);
        public const int PreviousInsideOffsetInPage = 0;
        public const int CurrentInsideOffsetInPage =
            PreviousInsideOffsetInPage + ByteSizeInBytes * VividEcsConstants.PageEntryCount;
        public const int ColliderEntityIdOffsetInPage =
            CurrentInsideOffsetInPage + ByteSizeInBytes * VividEcsConstants.PageEntryCount;
        public const int TypeSizeInBytes =
            ColliderEntityIdOffsetInPage + UlongSizeInBytes * VividEcsConstants.PageEntryCount;

        public int FieldCount => FieldCountValue;
        public int TypeSize => TypeSizeInBytes;

        public VividEcsSoaFieldInfo GetFieldInfo(int index)
        {
            return index switch
            {
                PreviousInsideFieldIndex =>
                    new VividEcsSoaFieldInfo(PreviousInsideOffsetInPage, ByteSizeInBytes),
                CurrentInsideFieldIndex =>
                    new VividEcsSoaFieldInfo(CurrentInsideOffsetInPage, ByteSizeInBytes),
                ColliderEntityIdFieldIndex =>
                    new VividEcsSoaFieldInfo(ColliderEntityIdOffsetInPage, UlongSizeInBytes),
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
        }
    }
}
