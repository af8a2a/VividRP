using System;
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

    internal struct VividParticleCommon : IVividEcsSoaComponentData
    {
        public const int PositionFieldIndex = 0;
        public const int VelocityFieldIndex = 1;
        public const int StartLifetimeFieldIndex = 2;
        public const int RemainingLifetimeFieldIndex = 3;
        public const int StartColorFieldIndex = 4;
        public const int SizeFieldIndex = 5;
        public const int FieldCountValue = 6;
        public const int FloatSizeInBytes = sizeof(float);
        public const int Float3SizeInBytes = sizeof(float) * 3;
        public const int Float4SizeInBytes = sizeof(float) * 4;
        public const int PositionOffsetInPage = 0;
        public const int VelocityOffsetInPage = PositionOffsetInPage + Float3SizeInBytes * VividEcsConstants.PageEntryCount;
        public const int StartLifetimeOffsetInPage = VelocityOffsetInPage + Float3SizeInBytes * VividEcsConstants.PageEntryCount;
        public const int RemainingLifetimeOffsetInPage = StartLifetimeOffsetInPage + FloatSizeInBytes * VividEcsConstants.PageEntryCount;
        public const int StartColorOffsetInPage = RemainingLifetimeOffsetInPage + FloatSizeInBytes * VividEcsConstants.PageEntryCount;
        public const int SizeOffsetInPage = StartColorOffsetInPage + Float4SizeInBytes * VividEcsConstants.PageEntryCount;
        public const int TypeSizeInBytes = SizeOffsetInPage + FloatSizeInBytes * VividEcsConstants.PageEntryCount;

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
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
        }

        public static VividEcsSoaFieldInfo GetStaticFieldInfo(int index)
        {
            return default(VividParticleCommon).GetFieldInfo(index);
        }
    }
}
