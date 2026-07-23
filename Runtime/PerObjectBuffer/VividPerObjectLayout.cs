using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VividRP.Runtime
{
    public enum VividPerObjectPropertyType
    {
        Int,
        Float,
        Vector,
        Color,
        Matrix,
    }

    public sealed class VividPerObjectPropertyDefinition
    {
        internal VividPerObjectPropertyDefinition(
            string name,
            VividPerObjectPropertyType type,
            int intDefault = 0,
            float floatDefault = 0.0f,
            Vector4 vectorDefault = default,
            Color colorDefault = default,
            Matrix4x4 matrixDefault = default)
        {
            Name = name;
            Type = type;
            IntDefault = intDefault;
            FloatDefault = floatDefault;
            VectorDefault = vectorDefault;
            ColorDefault = colorDefault;
            MatrixDefault = matrixDefault;
        }

        public string Name { get; }

        public VividPerObjectPropertyType Type { get; }

        public int IntDefault { get; }

        public float FloatDefault { get; }

        public Vector4 VectorDefault { get; }

        public Color ColorDefault { get; }

        public Matrix4x4 MatrixDefault { get; }
    }

    /// <summary>
    /// Collects an immutable per-object record declaration from a layout type.
    /// Properties retain declaration order and are packed at four-byte granularity.
    /// </summary>
    public sealed class VividPerObjectLayoutBuilder
    {
        private readonly List<VividPerObjectPropertyDefinition> m_Properties = new();
        private bool m_IsSealed;

        public void AddInt(string name, int defaultValue = 0)
        {
            Add(new VividPerObjectPropertyDefinition(
                name,
                VividPerObjectPropertyType.Int,
                intDefault: defaultValue));
        }

        public void AddFloat(string name, float defaultValue = 0.0f)
        {
            Add(new VividPerObjectPropertyDefinition(
                name,
                VividPerObjectPropertyType.Float,
                floatDefault: defaultValue));
        }

        public void AddVector(string name, Vector4 defaultValue = default)
        {
            Add(new VividPerObjectPropertyDefinition(
                name,
                VividPerObjectPropertyType.Vector,
                vectorDefault: defaultValue));
        }

        public void AddColor(string name, Color defaultValue = default)
        {
            Add(new VividPerObjectPropertyDefinition(
                name,
                VividPerObjectPropertyType.Color,
                colorDefault: defaultValue));
        }

        public void AddMatrix(string name)
        {
            AddMatrix(name, Matrix4x4.identity);
        }

        public void AddMatrix(string name, Matrix4x4 defaultValue)
        {
            Add(new VividPerObjectPropertyDefinition(
                name,
                VividPerObjectPropertyType.Matrix,
                matrixDefault: defaultValue));
        }

        internal List<VividPerObjectPropertyDefinition> SealAndTakeProperties()
        {
            if (m_IsSealed)
                throw new InvalidOperationException("The per-object layout builder has already been consumed.");

            m_IsSealed = true;
            return m_Properties;
        }

        private void Add(VividPerObjectPropertyDefinition property)
        {
            if (m_IsSealed)
                throw new InvalidOperationException("A per-object layout cannot be modified after declaration.");
            m_Properties.Add(property);
        }
    }

    public readonly struct VividPerObjectPropertyHandle : IEquatable<VividPerObjectPropertyHandle>
    {
        internal VividPerObjectPropertyHandle(
            VividPerObjectLayout layout,
            int nameId,
            int offset,
            VividPerObjectPropertyType type,
            uint layoutSignature)
        {
            Layout = layout;
            NameId = nameId;
            Offset = offset;
            Type = type;
            LayoutSignature = layoutSignature;
        }

        internal VividPerObjectLayout Layout { get; }

        internal int NameId { get; }

        internal int Offset { get; }

        internal VividPerObjectPropertyType Type { get; }

        internal uint LayoutSignature { get; }

        public bool IsValid => Layout != null && Offset >= VividPerObjectLayout.HeaderSize;

        public bool Equals(VividPerObjectPropertyHandle other)
        {
            return ReferenceEquals(Layout, other.Layout)
                && NameId == other.NameId
                && Offset == other.Offset
                && Type == other.Type
                && LayoutSignature == other.LayoutSignature;
        }

        public override bool Equals(object obj)
        {
            return obj is VividPerObjectPropertyHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Layout != null ? RuntimeHelpers.GetHashCode(Layout) : 0;
                hash = (hash * 397) ^ NameId;
                hash = (hash * 397) ^ Offset;
                hash = (hash * 397) ^ (int)Type;
                hash = (hash * 397) ^ (int)LayoutSignature;
                return hash;
            }
        }

        public static bool operator ==(VividPerObjectPropertyHandle left, VividPerObjectPropertyHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VividPerObjectPropertyHandle left, VividPerObjectPropertyHandle right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Base class for code-declared per-object buffer layouts.
    /// </summary>
    public abstract class VividPerObjectLayout
    {
        internal const int HeaderSize = sizeof(uint);
        internal const int RecordAlignment = 16;

        private static readonly object s_SharedLayoutLock = new();
        private static readonly Dictionary<Type, VividPerObjectLayout> s_SharedLayouts = new();

        private LayoutCache m_Cache;

        public abstract string ShaderIdentifier { get; }

        public IReadOnlyList<VividPerObjectPropertyDefinition> Properties
        {
            get
            {
                EnsureCache();
                return m_Cache.Definitions;
            }
        }

        public uint Signature
        {
            get
            {
                EnsureCache();
                return m_Cache.Signature;
            }
        }

        public int RecordStride
        {
            get
            {
                EnsureCache();
                return m_Cache.RecordStride;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected abstract void Define(VividPerObjectLayoutBuilder builder);

        public VividPerObjectPropertyHandle GetProperty(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                throw new ArgumentException("A per-object property name is required.", nameof(propertyName));

            return GetProperty(Shader.PropertyToID(propertyName));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VividPerObjectPropertyHandle GetProperty(int propertyNameId)
        {
            EnsureCache();
            if (!m_Cache.PropertiesById.TryGetValue(propertyNameId, out PackedProperty property))
            {
                throw new ArgumentException(
                    $"Layout '{ShaderIdentifier}' does not contain property ID {propertyNameId}.",
                    nameof(propertyNameId));
            }

            return property.Handle;
        }

        public bool TryGetProperty(int propertyNameId, out VividPerObjectPropertyHandle propertyHandle)
        {
            EnsureCache();
            if (m_Cache.PropertiesById.TryGetValue(propertyNameId, out PackedProperty property))
            {
                propertyHandle = property.Handle;
                return true;
            }

            propertyHandle = default;
            return false;
        }

        public static TLayout GetShared<TLayout>()
            where TLayout : VividPerObjectLayout, new()
        {
            Type layoutType = typeof(TLayout);
            lock (s_SharedLayoutLock)
            {
                if (s_SharedLayouts.TryGetValue(layoutType, out VividPerObjectLayout existing))
                    return (TLayout)existing;

                var layout = new TLayout();
                layout.EnsureCache();
                s_SharedLayouts.Add(layoutType, layout);
                return layout;
            }
        }

        internal static VividPerObjectLayout GetShared(Type layoutType)
        {
            if (layoutType == null)
                throw new ArgumentNullException(nameof(layoutType));
            if (layoutType.IsAbstract
                || layoutType.ContainsGenericParameters
                || !typeof(VividPerObjectLayout).IsAssignableFrom(layoutType))
            {
                throw new ArgumentException(
                    $"Type '{layoutType.FullName}' is not a concrete VividPerObjectLayout.",
                    nameof(layoutType));
            }

            lock (s_SharedLayoutLock)
            {
                if (s_SharedLayouts.TryGetValue(layoutType, out VividPerObjectLayout existing))
                    return existing;

                VividPerObjectLayout layout;
                try
                {
                    layout = (VividPerObjectLayout)Activator.CreateInstance(layoutType, nonPublic: true);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Layout '{layoutType.FullName}' must have a parameterless constructor.",
                        exception);
                }

                layout.EnsureCache();
                s_SharedLayouts.Add(layoutType, layout);
                return layout;
            }
        }

        internal IReadOnlyList<PackedProperty> PackedProperties
        {
            get
            {
                EnsureCache();
                return m_Cache.PackedProperties;
            }
        }

        internal bool IsEquivalentTo(VividPerObjectLayout other)
        {
            return other != null
                && GetType() == other.GetType()
                && Signature == other.Signature;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Validate()
        {
            EnsureCache();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void InitializeRecord(byte[] destination, int baseAddress)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            EnsureCache();
            if (baseAddress < 0 || baseAddress + m_Cache.RecordStride > destination.Length)
                throw new ArgumentOutOfRangeException(nameof(baseAddress));

            Array.Clear(destination, baseAddress, m_Cache.RecordStride);
            WriteUInt32(destination, baseAddress, m_Cache.Signature);

            for (int i = 0; i < m_Cache.PackedProperties.Count; i++)
            {
                PackedProperty property = m_Cache.PackedProperties[i];
                int offset = baseAddress + property.Offset;
                VividPerObjectPropertyDefinition definition = property.Definition;
                switch (property.Type)
                {
                    case VividPerObjectPropertyType.Int:
                        WriteInt32(destination, offset, definition.IntDefault);
                        break;
                    case VividPerObjectPropertyType.Float:
                        WriteSingle(destination, offset, definition.FloatDefault);
                        break;
                    case VividPerObjectPropertyType.Vector:
                        WriteStruct(destination, offset, definition.VectorDefault);
                        break;
                    case VividPerObjectPropertyType.Color:
                        Color color = definition.ColorDefault;
                        WriteStruct(destination, offset, new Vector4(color.r, color.g, color.b, color.a));
                        break;
                    case VividPerObjectPropertyType.Matrix:
                        WriteStruct(destination, offset, definition.MatrixDefault);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        internal static int GetPropertySize(VividPerObjectPropertyType type)
        {
            return type switch
            {
                VividPerObjectPropertyType.Int => sizeof(int),
                VividPerObjectPropertyType.Float => sizeof(float),
                VividPerObjectPropertyType.Vector => sizeof(float) * 4,
                VividPerObjectPropertyType.Color => sizeof(float) * 4,
                VividPerObjectPropertyType.Matrix => sizeof(float) * 16,
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
        }

        internal static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            char first = value[0];
            if (!(first == '_' || IsAsciiLetter(first)))
                return false;

            for (int i = 1; i < value.Length; i++)
            {
                char character = value[i];
                if (!(character == '_' || IsAsciiLetter(character) || character is >= '0' and <= '9'))
                    return false;
            }

            return true;
        }

        private void EnsureCache()
        {
            if (m_Cache != null)
                return;

            ValidateIdentifier(ShaderIdentifier, nameof(ShaderIdentifier));
            var builder = new VividPerObjectLayoutBuilder();
            Define(builder);
            List<VividPerObjectPropertyDefinition> definitions = builder.SealAndTakeProperties();
            var packedProperties = new List<PackedProperty>(definitions.Count);
            var propertiesById = new Dictionary<int, PackedProperty>(definitions.Count);
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            int currentOffset = HeaderSize;

            for (int i = 0; i < definitions.Count; i++)
            {
                VividPerObjectPropertyDefinition definition = definitions[i]
                    ?? throw new InvalidOperationException(
                        $"Layout '{ShaderIdentifier}' contains a null property at index {i}.");
                ValidateIdentifier(definition.Name, $"property[{i}]");
                if (!propertyNames.Add(definition.Name))
                {
                    throw new InvalidOperationException(
                        $"Layout '{ShaderIdentifier}' contains duplicate property '{definition.Name}'.");
                }

                int propertyId = Shader.PropertyToID(definition.Name);
                if (propertiesById.ContainsKey(propertyId))
                {
                    throw new InvalidOperationException(
                        $"Layout '{ShaderIdentifier}' contains Shader.PropertyToID collision for '{definition.Name}'.");
                }

                int propertySize = GetPropertySize(definition.Type);
                var property = new PackedProperty(
                    definition,
                    propertyId,
                    currentOffset,
                    propertySize,
                    definition.Type);
                packedProperties.Add(property);
                propertiesById.Add(propertyId, property);
                currentOffset += propertySize;
            }

            int recordStride = AlignUp(currentOffset, RecordAlignment);
            uint signature = ComputeSignature(ShaderIdentifier, packedProperties, recordStride);
            for (int i = 0; i < packedProperties.Count; i++)
            {
                PackedProperty property = packedProperties[i];
                property.Handle = new VividPerObjectPropertyHandle(
                    this,
                    property.NameId,
                    property.Offset,
                    property.Type,
                    signature);
            }

            m_Cache = new LayoutCache(
                signature,
                recordStride,
                definitions.AsReadOnly(),
                packedProperties,
                propertiesById);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAsciiLetter(char character)
        {
            return character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateIdentifier(string value, string fieldName)
        {
            if (!IsValidIdentifier(value))
            {
                throw new InvalidOperationException(
                    $"Per-object layout {fieldName} '{value}' is not a valid ASCII HLSL identifier.");
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ComputeSignature(
            string shaderIdentifier,
            List<PackedProperty> properties,
            int recordStride)
        {
            uint hash = 2166136261u;
            AddString(ref hash, shaderIdentifier);
            AddInt32(ref hash, recordStride);
            for (int i = 0; i < properties.Count; i++)
            {
                PackedProperty property = properties[i];
                AddString(ref hash, property.Definition.Name);
                AddInt32(ref hash, (int)property.Type);
                AddInt32(ref hash, property.Offset);
                AddDefaultValue(ref hash, property.Definition);
            }

            return hash != 0u ? hash : 1u;
        }

        private static void AddDefaultValue(ref uint hash, VividPerObjectPropertyDefinition definition)
        {
            switch (definition.Type)
            {
                case VividPerObjectPropertyType.Int:
                    AddInt32(ref hash, definition.IntDefault);
                    break;
                case VividPerObjectPropertyType.Float:
                    AddSingle(ref hash, definition.FloatDefault);
                    break;
                case VividPerObjectPropertyType.Vector:
                    AddVector(ref hash, definition.VectorDefault);
                    break;
                case VividPerObjectPropertyType.Color:
                    Color color = definition.ColorDefault;
                    AddVector(ref hash, new Vector4(color.r, color.g, color.b, color.a));
                    break;
                case VividPerObjectPropertyType.Matrix:
                    Matrix4x4 matrix = definition.MatrixDefault;
                    for (int column = 0; column < 4; column++)
                    {
                        for (int row = 0; row < 4; row++)
                            AddSingle(ref hash, matrix[row, column]);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void AddVector(ref uint hash, Vector4 value)
        {
            AddSingle(ref hash, value.x);
            AddSingle(ref hash, value.y);
            AddSingle(ref hash, value.z);
            AddSingle(ref hash, value.w);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddString(ref uint hash, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                AddByte(ref hash, (byte)character);
                AddByte(ref hash, (byte)(character >> 8));
            }
            AddByte(ref hash, 0xff);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddSingle(ref uint hash, float value)
        {
            AddInt32(ref hash, BitConverter.SingleToInt32Bits(value));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddInt32(ref uint hash, int value)
        {
            unchecked
            {
                AddByte(ref hash, (byte)value);
                AddByte(ref hash, (byte)(value >> 8));
                AddByte(ref hash, (byte)(value >> 16));
                AddByte(ref hash, (byte)(value >> 24));
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddByte(ref uint hash, byte value)
        {
            hash ^= value;
            hash *= 16777619u;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int AlignUp(int value, int alignment)
        {
            return (value + alignment - 1) & ~(alignment - 1);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteUInt32(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteInt32(byte[] destination, int offset, int value)
        {
            WriteUInt32(destination, offset, unchecked((uint)value));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteSingle(byte[] destination, int offset, float value)
        {
            WriteInt32(destination, offset, BitConverter.SingleToInt32Bits(value));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void WriteStruct<T>(byte[] destination, int offset, T value)
            where T : unmanaged
        {
            fixed (byte* destinationPointer = &destination[offset])
                *(T*)destinationPointer = value;
        }

        internal sealed class PackedProperty
        {
            internal PackedProperty(
                VividPerObjectPropertyDefinition definition,
                int nameId,
                int offset,
                int size,
                VividPerObjectPropertyType type)
            {
                Definition = definition;
                NameId = nameId;
                Offset = offset;
                Size = size;
                Type = type;
            }

            internal VividPerObjectPropertyDefinition Definition { get; }

            internal int NameId { get; }

            internal int Offset { get; }

            internal int Size { get; }

            internal VividPerObjectPropertyType Type { get; }

            internal VividPerObjectPropertyHandle Handle { get; set; }
        }

        private sealed class LayoutCache
        {
            internal LayoutCache(
                uint signature,
                int recordStride,
                IReadOnlyList<VividPerObjectPropertyDefinition> definitions,
                List<PackedProperty> packedProperties,
                Dictionary<int, PackedProperty> propertiesById)
            {
                Signature = signature;
                RecordStride = recordStride;
                Definitions = definitions;
                PackedProperties = packedProperties;
                PropertiesById = propertiesById;
            }

            internal uint Signature { get; }

            internal int RecordStride { get; }

            internal IReadOnlyList<VividPerObjectPropertyDefinition> Definitions { get; }

            internal List<PackedProperty> PackedProperties { get; }

            internal Dictionary<int, PackedProperty> PropertiesById { get; }
        }
    }

    /// <summary>
    /// Adds a shared singleton instance to a code-declared layout type.
    /// </summary>
    public abstract class VividPerObjectLayout<TLayout> : VividPerObjectLayout
        where TLayout : VividPerObjectLayout<TLayout>, new()
    {
        public static TLayout Instance => GetShared<TLayout>();
    }
}
