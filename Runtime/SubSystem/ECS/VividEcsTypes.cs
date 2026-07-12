using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

namespace VividRP.Runtime.ECS
{
    internal static class VividEcsConstants
    {
        public const int PageEntryCount = 256;

        public static int AlignToPage(int value)
        {
            int clampedValue = Math.Max(1, value);
            return Math.Max(PageEntryCount, ((clampedValue + PageEntryCount - 1) / PageEntryCount) * PageEntryCount);
        }
    }

    [Flags]
    internal enum VividEcsComponentKind
    {
        None = 0,
        Data = 1 << 0,
        Shared = 1 << 1,
        Tag = 1 << 2,
        Soa = 1 << 3,
        Bit = 1 << 4,
    }

    internal readonly struct VividEcsTypeIndex : IEquatable<VividEcsTypeIndex>, IComparable<VividEcsTypeIndex>
    {
        private const int TypeIdMask = (1 << 16) - 1;

        public static readonly VividEcsTypeIndex Null = new(0);
        public static readonly VividEcsTypeIndex Invalid = new(-1);

        public VividEcsTypeIndex(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public int TypeId => Value & TypeIdMask;

        public bool IsValid => Value > 0;

        public bool IsDataComponentType => (Value & ((int)VividEcsComponentKind.Data << 16)) != 0;

        public bool IsSharedComponentType => (Value & ((int)VividEcsComponentKind.Shared << 16)) != 0;

        public bool IsTagComponentType => (Value & ((int)VividEcsComponentKind.Tag << 16)) != 0;

        public bool IsSoaComponentType => (Value & ((int)VividEcsComponentKind.Soa << 16)) != 0;

        public bool IsBitComponentType => (Value & ((int)VividEcsComponentKind.Bit << 16)) != 0;

        public bool Equals(VividEcsTypeIndex other)
        {
            return Value == other.Value;
        }

        public int CompareTo(VividEcsTypeIndex other)
        {
            return Value.CompareTo(other.Value);
        }

        public override bool Equals(object obj)
        {
            return obj is VividEcsTypeIndex other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return IsValid ? Value.ToString() : "Invalid";
        }

        public static bool operator ==(VividEcsTypeIndex left, VividEcsTypeIndex right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VividEcsTypeIndex left, VividEcsTypeIndex right)
        {
            return !left.Equals(right);
        }

        public static bool operator <(VividEcsTypeIndex left, VividEcsTypeIndex right)
        {
            return left.Value < right.Value;
        }

        public static bool operator >(VividEcsTypeIndex left, VividEcsTypeIndex right)
        {
            return left.Value > right.Value;
        }

        internal static VividEcsTypeIndex Create(int typeId, VividEcsComponentKind kind)
        {
            return new VividEcsTypeIndex(typeId | ((int)kind << 16));
        }
    }

    internal readonly struct VividEcsSoaFieldInfo : IEquatable<VividEcsSoaFieldInfo>
    {
        public VividEcsSoaFieldInfo(int offsetInPage, int elementSize)
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

        public int TypeSize => ElementSize;

        public bool Equals(VividEcsSoaFieldInfo other)
        {
            return OffsetInPage == other.OffsetInPage && ElementSize == other.ElementSize;
        }

        public override bool Equals(object obj)
        {
            return obj is VividEcsSoaFieldInfo other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (OffsetInPage * 397) ^ ElementSize;
            }
        }
    }

    internal interface IVividEcsComponentData
    {
    }

    internal interface IVividEcsSharedComponentData : IVividEcsComponentData
    {
    }

    internal interface IVividEcsLineAttachmentData
    {
    }

    internal interface IVividEcsTagComponentData : IVividEcsComponentData
    {
    }

    internal interface IVividEcsBitComponentData : IVividEcsComponentData
    {
    }

    internal interface IVividEcsSoaComponentData : IVividEcsComponentData
    {
        int FieldCount { get; }

        int TypeSize { get; }

        VividEcsSoaFieldInfo GetFieldInfo(int index);
    }

    internal readonly struct VividEcsTypeInfo
    {
        private readonly VividEcsSoaFieldInfo[] m_SoaFields;

        public VividEcsTypeInfo(
            VividEcsTypeIndex typeIndex,
            Type managedType,
            VividEcsComponentKind kind,
            int elementSize,
            int alignment,
            int sizeInPage,
            VividEcsSoaFieldInfo[] soaFields)
        {
            TypeIndex = typeIndex;
            ManagedType = managedType ?? throw new ArgumentNullException(nameof(managedType));
            Kind = kind;
            ElementSize = elementSize;
            Alignment = alignment;
            SizeInPage = sizeInPage;
            m_SoaFields = soaFields ?? Array.Empty<VividEcsSoaFieldInfo>();
        }

        public VividEcsTypeIndex TypeIndex { get; }

        public Type ManagedType { get; }

        public VividEcsComponentKind Kind { get; }

        public bool IsData => (Kind & VividEcsComponentKind.Data) != 0;

        public bool IsShared => (Kind & VividEcsComponentKind.Shared) != 0;

        public bool IsTag => (Kind & VividEcsComponentKind.Tag) != 0;

        public bool IsSoa => (Kind & VividEcsComponentKind.Soa) != 0;

        public bool IsBit => (Kind & VividEcsComponentKind.Bit) != 0;

        public int ElementSize { get; }

        public int Alignment { get; }

        public int SizeInPage { get; }

        public int SoaFieldCount => m_SoaFields.Length;

        public VividEcsSoaFieldInfo GetSoaFieldInfo(int index)
        {
            if ((uint)index >= (uint)m_SoaFields.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return m_SoaFields[index];
        }
    }

    internal static class VividEcsTypeManager
    {
        public const int MaximumTypesCount = 1 << 13;

        private static readonly Dictionary<Type, VividEcsTypeIndex> s_TypeToIndex = new();
        private static readonly List<VividEcsTypeInfo> s_TypeInfos = new() { default };

        public static int RegisteredTypeCount => s_TypeInfos.Count - 1;

        public static VividEcsTypeIndex GetTypeIndex<T>()
            where T : struct, IVividEcsComponentData
        {
            return s_TypeToIndex.TryGetValue(typeof(T), out VividEcsTypeIndex index)
                ? index
                : VividEcsTypeIndex.Invalid;
        }

        public static VividEcsTypeIndex GetTypeIndex(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            return s_TypeToIndex.TryGetValue(type, out VividEcsTypeIndex index)
                ? index
                : VividEcsTypeIndex.Invalid;
        }

        public static VividEcsTypeInfo GetTypeInfo<T>()
            where T : struct, IVividEcsComponentData
        {
            return GetTypeInfo(GetTypeIndex<T>());
        }

        public static VividEcsTypeInfo GetTypeInfo(VividEcsTypeIndex typeIndex)
        {
            if (!typeIndex.IsValid || typeIndex.TypeId <= 0 || typeIndex.TypeId >= s_TypeInfos.Count)
                throw new ArgumentOutOfRangeException(nameof(typeIndex));

            return s_TypeInfos[typeIndex.TypeId];
        }

        public static VividEcsTypeIndex RegisterComponent<T>()
            where T : struct, IVividEcsComponentData
        {
            return Register<T>(VividEcsComponentKind.Data, Array.Empty<VividEcsSoaFieldInfo>(), UnsafeUtility.SizeOf<T>());
        }

        public static VividEcsTypeIndex RegisterShared<T>()
            where T : struct, IVividEcsSharedComponentData
        {
            return Register<T>(VividEcsComponentKind.Shared, Array.Empty<VividEcsSoaFieldInfo>(), UnsafeUtility.SizeOf<T>());
        }

        public static VividEcsTypeIndex RegisterTag<T>()
            where T : struct, IVividEcsTagComponentData
        {
            return Register<T>(VividEcsComponentKind.Tag, Array.Empty<VividEcsSoaFieldInfo>(), 0);
        }

        public static VividEcsTypeIndex RegisterBit<T>()
            where T : struct, IVividEcsBitComponentData
        {
            return Register<T>(VividEcsComponentKind.Bit, Array.Empty<VividEcsSoaFieldInfo>(), sizeof(ulong) * 4);
        }

        public static VividEcsTypeIndex RegisterSoa<T>()
            where T : struct, IVividEcsSoaComponentData
        {
            T component = default;
            var fields = new VividEcsSoaFieldInfo[component.FieldCount];
            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                fields[fieldIndex] = component.GetFieldInfo(fieldIndex);

            return Register<T>(VividEcsComponentKind.Soa, fields, component.TypeSize);
        }

        public static int SortAndRemoveDuplicateTypes(VividEcsTypeIndex[] types, int count)
        {
            if (types == null)
                throw new ArgumentNullException(nameof(types));

            if (count < 0 || count > types.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            Array.Sort(types, 0, count);
            int writeIndex = 0;
            for (int readIndex = 0; readIndex < count; readIndex++)
            {
                VividEcsTypeIndex type = types[readIndex];
                if (!type.IsValid)
                    continue;

                if (writeIndex > 0 && types[writeIndex - 1] == type)
                    continue;

                types[writeIndex++] = type;
            }

            return writeIndex;
        }

        private static VividEcsTypeIndex Register<T>(
            VividEcsComponentKind kind,
            VividEcsSoaFieldInfo[] fields,
            int sizeInPage)
            where T : struct, IVividEcsComponentData
        {
            Type type = typeof(T);
            if (s_TypeToIndex.TryGetValue(type, out VividEcsTypeIndex existingIndex))
                return existingIndex;

            if (s_TypeInfos.Count >= MaximumTypesCount)
                throw new InvalidOperationException($"Vivid ECS maximum type count reached ({MaximumTypesCount}).");

            int typeId = s_TypeInfos.Count;
            VividEcsTypeIndex typeIndex = VividEcsTypeIndex.Create(typeId, kind);
            int elementSize = UnsafeUtility.SizeOf<T>();
            int alignment = UnsafeUtility.AlignOf<T>();
            int pageSize = (kind & (VividEcsComponentKind.Soa | VividEcsComponentKind.Bit | VividEcsComponentKind.Tag)) != 0
                ? Math.Max(0, sizeInPage)
                : Math.Max(0, sizeInPage) * VividEcsConstants.PageEntryCount;

            s_TypeToIndex.Add(type, typeIndex);
            s_TypeInfos.Add(new VividEcsTypeInfo(
                typeIndex,
                type,
                kind,
                elementSize,
                alignment,
                pageSize,
                fields));
            return typeIndex;
        }
    }
}
