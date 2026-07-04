using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

namespace VividRP.Runtime.Particle.ECS
{
    internal static class VividParticleEcsConstants
    {
        public const int PageEntryCount = VividParticleStorage.PageSize;
    }

    internal readonly struct VividParticleTypeIndex : IEquatable<VividParticleTypeIndex>
    {
        public static readonly VividParticleTypeIndex Invalid = new(-1);

        public VividParticleTypeIndex(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value >= 0;

        public bool Equals(VividParticleTypeIndex other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is VividParticleTypeIndex other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return IsValid ? Value.ToString() : "Invalid";
        }

        public static bool operator ==(VividParticleTypeIndex left, VividParticleTypeIndex right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VividParticleTypeIndex left, VividParticleTypeIndex right)
        {
            return !left.Equals(right);
        }
    }

    internal readonly struct VividParticleSoaFieldInfo : IEquatable<VividParticleSoaFieldInfo>
    {
        public VividParticleSoaFieldInfo(int offsetInPage, int elementSize)
        {
            if (offsetInPage < 0)
                throw new ArgumentOutOfRangeException(nameof(offsetInPage));

            if (elementSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(elementSize));

            OffsetInPage = offsetInPage;
            ElementSize = elementSize;
        }

        public int OffsetInPage { get; }

        public int ElementSize { get; }

        public bool Equals(VividParticleSoaFieldInfo other)
        {
            return OffsetInPage == other.OffsetInPage && ElementSize == other.ElementSize;
        }

        public override bool Equals(object obj)
        {
            return obj is VividParticleSoaFieldInfo other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (OffsetInPage * 397) ^ ElementSize;
            }
        }
    }

    internal interface IVividParticleComponentData
    {
    }

    internal interface IVividParticleSoaComponentData : IVividParticleComponentData
    {
        int FieldCount { get; }

        int TypeSize { get; }

        VividParticleSoaFieldInfo GetFieldInfo(int index);
    }

    internal readonly struct VividParticleTypeInfo
    {
        private readonly VividParticleSoaFieldInfo[] m_Fields;

        public VividParticleTypeInfo(
            VividParticleTypeIndex typeIndex,
            Type managedType,
            bool isSoa,
            int sizeInPage,
            VividParticleSoaFieldInfo[] fields)
        {
            TypeIndex = typeIndex;
            ManagedType = managedType ?? throw new ArgumentNullException(nameof(managedType));
            IsSoa = isSoa;
            SizeInPage = sizeInPage;
            m_Fields = fields ?? Array.Empty<VividParticleSoaFieldInfo>();
        }

        public VividParticleTypeIndex TypeIndex { get; }

        public Type ManagedType { get; }

        public bool IsSoa { get; }

        public int SizeInPage { get; }

        public int FieldCount => m_Fields.Length;

        public VividParticleSoaFieldInfo GetFieldInfo(int index)
        {
            if ((uint)index >= (uint)m_Fields.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return m_Fields[index];
        }
    }

    internal static class VividParticleTypeManager
    {
        private static readonly Dictionary<Type, VividParticleTypeIndex> s_TypeToIndex = new();
        private static readonly List<VividParticleTypeInfo> s_TypeInfos = new();
        private static bool s_Initialized;
        private static bool s_Initializing;

        public static int RegisteredTypeCount
        {
            get
            {
                EnsureInitialized();
                return s_TypeInfos.Count;
            }
        }

        public static VividParticleTypeIndex GetTypeIndex<T>()
            where T : struct, IVividParticleComponentData
        {
            EnsureInitialized();
            return s_TypeToIndex.TryGetValue(typeof(T), out VividParticleTypeIndex index)
                ? index
                : VividParticleTypeIndex.Invalid;
        }

        public static VividParticleTypeInfo GetTypeInfo<T>()
            where T : struct, IVividParticleComponentData
        {
            return GetTypeInfo(GetTypeIndex<T>());
        }

        public static VividParticleTypeInfo GetTypeInfo(VividParticleTypeIndex typeIndex)
        {
            EnsureInitialized();
            if (!typeIndex.IsValid || typeIndex.Value >= s_TypeInfos.Count)
                throw new ArgumentOutOfRangeException(nameof(typeIndex));

            return s_TypeInfos[typeIndex.Value];
        }

        public static VividParticleTypeIndex RegisterComponent<T>()
            where T : struct, IVividParticleComponentData
        {
            if (!s_Initialized && !s_Initializing)
                EnsureInitialized();

            Type type = typeof(T);
            if (s_TypeToIndex.TryGetValue(type, out VividParticleTypeIndex existingIndex))
                return existingIndex;

            VividParticleTypeIndex typeIndex = new(s_TypeInfos.Count);
            s_TypeToIndex.Add(type, typeIndex);
            s_TypeInfos.Add(new VividParticleTypeInfo(
                typeIndex,
                type,
                isSoa: false,
                UnsafeUtility.SizeOf<T>(),
                Array.Empty<VividParticleSoaFieldInfo>()));
            return typeIndex;
        }

        public static VividParticleTypeIndex RegisterSoa<T>()
            where T : struct, IVividParticleSoaComponentData
        {
            if (!s_Initialized && !s_Initializing)
                EnsureInitialized();

            Type type = typeof(T);
            if (s_TypeToIndex.TryGetValue(type, out VividParticleTypeIndex existingIndex))
                return existingIndex;

            T component = default;
            var fields = new VividParticleSoaFieldInfo[component.FieldCount];
            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                fields[fieldIndex] = component.GetFieldInfo(fieldIndex);

            VividParticleTypeIndex typeIndex = new(s_TypeInfos.Count);
            s_TypeToIndex.Add(type, typeIndex);
            s_TypeInfos.Add(new VividParticleTypeInfo(
                typeIndex,
                type,
                isSoa: true,
                component.TypeSize,
                fields));
            return typeIndex;
        }

        private static void EnsureInitialized()
        {
            if (s_Initialized)
                return;

            s_Initialized = true;
            s_Initializing = true;
            try
            {
                RegisterSoa<VividParticleCommon>();
                RegisterComponent<VividParticleSystemId>();
            }
            finally
            {
                s_Initializing = false;
            }
        }
    }

    internal readonly struct VividParticleColumn
    {
        public VividParticleColumn(
            VividParticleTypeIndex typeIndex,
            int fieldIndex,
            VividParticleSoaFieldInfo fieldInfo)
        {
            TypeIndex = typeIndex;
            FieldIndex = fieldIndex;
            FieldInfo = fieldInfo;
        }

        public VividParticleTypeIndex TypeIndex { get; }

        public int FieldIndex { get; }

        public VividParticleSoaFieldInfo FieldInfo { get; }
    }

    internal readonly struct VividParticleSystemId : IVividParticleComponentData, IEquatable<VividParticleSystemId>
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

    internal struct VividParticleCommon : IVividParticleSoaComponentData
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
        public const int VelocityOffsetInPage = PositionOffsetInPage + Float3SizeInBytes * VividParticleEcsConstants.PageEntryCount;
        public const int StartLifetimeOffsetInPage = VelocityOffsetInPage + Float3SizeInBytes * VividParticleEcsConstants.PageEntryCount;
        public const int RemainingLifetimeOffsetInPage = StartLifetimeOffsetInPage + FloatSizeInBytes * VividParticleEcsConstants.PageEntryCount;
        public const int StartColorOffsetInPage = RemainingLifetimeOffsetInPage + FloatSizeInBytes * VividParticleEcsConstants.PageEntryCount;
        public const int SizeOffsetInPage = StartColorOffsetInPage + Float4SizeInBytes * VividParticleEcsConstants.PageEntryCount;
        public const int TypeSizeInBytes = SizeOffsetInPage + FloatSizeInBytes * VividParticleEcsConstants.PageEntryCount;

        public int FieldCount => FieldCountValue;

        public int TypeSize => TypeSizeInBytes;

        public VividParticleSoaFieldInfo GetFieldInfo(int index)
        {
            return index switch
            {
                PositionFieldIndex => new VividParticleSoaFieldInfo(PositionOffsetInPage, Float3SizeInBytes),
                VelocityFieldIndex => new VividParticleSoaFieldInfo(VelocityOffsetInPage, Float3SizeInBytes),
                StartLifetimeFieldIndex => new VividParticleSoaFieldInfo(StartLifetimeOffsetInPage, FloatSizeInBytes),
                RemainingLifetimeFieldIndex => new VividParticleSoaFieldInfo(RemainingLifetimeOffsetInPage, FloatSizeInBytes),
                StartColorFieldIndex => new VividParticleSoaFieldInfo(StartColorOffsetInPage, Float4SizeInBytes),
                SizeFieldIndex => new VividParticleSoaFieldInfo(SizeOffsetInPage, FloatSizeInBytes),
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
        }

        public static VividParticleSoaFieldInfo GetStaticFieldInfo(int index)
        {
            return default(VividParticleCommon).GetFieldInfo(index);
        }
    }
}
