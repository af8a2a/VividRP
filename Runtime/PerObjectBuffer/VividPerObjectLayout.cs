using System;
using System.Collections.Generic;
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

    [Serializable]
    public sealed class VividPerObjectPropertyDefinition
    {
        [SerializeField]
        private string m_Name = "_Property";

        [SerializeField]
        private VividPerObjectPropertyType m_Type;

        [SerializeField]
        private int m_IntDefault;

        [SerializeField]
        private float m_FloatDefault;

        [SerializeField]
        private Vector4 m_VectorDefault;

        [SerializeField]
        private Color m_ColorDefault = Color.white;

        [SerializeField]
        private Matrix4x4 m_MatrixDefault = Matrix4x4.identity;

        public string Name => m_Name;

        public VividPerObjectPropertyType Type => m_Type;

        public int IntDefault => m_IntDefault;

        public float FloatDefault => m_FloatDefault;

        public Vector4 VectorDefault => m_VectorDefault;

        public Color ColorDefault => m_ColorDefault;

        public Matrix4x4 MatrixDefault => m_MatrixDefault;

        internal void Configure(
            string name,
            VividPerObjectPropertyType type,
            int intDefault = 0,
            float floatDefault = 0.0f,
            Vector4 vectorDefault = default,
            Color colorDefault = default,
            Matrix4x4 matrixDefault = default)
        {
            m_Name = name;
            m_Type = type;
            m_IntDefault = intDefault;
            m_FloatDefault = floatDefault;
            m_VectorDefault = vectorDefault;
            m_ColorDefault = colorDefault;
            m_MatrixDefault = matrixDefault;
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
                int hash = Layout != null ? Layout.GetEntityId().GetHashCode() : 0;
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

    [CreateAssetMenu(menuName = "VividRP/Per Object Buffer Layout", fileName = "PerObjectLayout")]
    public sealed class VividPerObjectLayout : ScriptableObject
    {
        internal const int HeaderSize = sizeof(uint);
        internal const int RecordAlignment = 16;

        [SerializeField]
        private string m_ShaderIdentifier = "PerObject";

        [SerializeField]
        private List<VividPerObjectPropertyDefinition> m_Properties = new();

        [SerializeField, HideInInspector]
        private string m_GeneratedIncludePath;

        [NonSerialized]
        private LayoutCache m_Cache;

        public string ShaderIdentifier => m_ShaderIdentifier;

        public IReadOnlyList<VividPerObjectPropertyDefinition> Properties => m_Properties;

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

        public string GeneratedIncludePath => m_GeneratedIncludePath;

        public VividPerObjectPropertyHandle GetProperty(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                throw new ArgumentException("A per-object property name is required.", nameof(propertyName));

            return GetProperty(Shader.PropertyToID(propertyName));
        }

        public VividPerObjectPropertyHandle GetProperty(int propertyNameId)
        {
            EnsureCache();
            if (!m_Cache.PropertiesById.TryGetValue(propertyNameId, out var property))
                throw new ArgumentException($"Layout '{name}' does not contain property ID {propertyNameId}.", nameof(propertyNameId));

            return property.Handle;
        }

        public bool TryGetProperty(int propertyNameId, out VividPerObjectPropertyHandle propertyHandle)
        {
            EnsureCache();
            if (m_Cache.PropertiesById.TryGetValue(propertyNameId, out var property))
            {
                propertyHandle = property.Handle;
                return true;
            }

            propertyHandle = default;
            return false;
        }

        internal IReadOnlyList<PackedProperty> PackedProperties
        {
            get
            {
                EnsureCache();
                return m_Cache.PackedProperties;
            }
        }

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
                        var color = definition.ColorDefault;
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

        internal void SetGeneratedIncludePath(string assetPath)
        {
            m_GeneratedIncludePath = assetPath ?? string.Empty;
        }

        internal void ConfigureForTests(
            string shaderIdentifier,
            IEnumerable<VividPerObjectPropertyDefinition> properties)
        {
            m_ShaderIdentifier = shaderIdentifier;
            m_Properties = properties != null
                ? new List<VividPerObjectPropertyDefinition>(properties)
                : new List<VividPerObjectPropertyDefinition>();
            InvalidateCache();
        }

        internal void ValidateAndRebuild()
        {
            InvalidateCache();
            EnsureCache();
        }

        private void OnValidate()
        {
            InvalidateCache();
        }

        private void InvalidateCache()
        {
            m_Cache = null;
        }

        private void EnsureCache()
        {
            if (m_Cache != null)
                return;

            ValidateIdentifier(m_ShaderIdentifier, nameof(m_ShaderIdentifier));

            var packedProperties = new List<PackedProperty>(m_Properties?.Count ?? 0);
            var propertiesById = new Dictionary<int, PackedProperty>(m_Properties?.Count ?? 0);
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            int currentOffset = HeaderSize;

            if (m_Properties != null)
            {
                for (int i = 0; i < m_Properties.Count; i++)
                {
                    VividPerObjectPropertyDefinition definition = m_Properties[i]
                        ?? throw new InvalidOperationException($"Layout '{name}' contains a null property at index {i}.");
                    ValidateIdentifier(definition.Name, $"property[{i}]");
                    if (!propertyNames.Add(definition.Name))
                        throw new InvalidOperationException($"Layout '{name}' contains duplicate property '{definition.Name}'.");

                    int propertyId = Shader.PropertyToID(definition.Name);
                    if (propertiesById.ContainsKey(propertyId))
                    {
                        throw new InvalidOperationException(
                            $"Layout '{name}' contains Shader.PropertyToID collision for '{definition.Name}'.");
                    }

                    int propertySize = GetPropertySize(definition.Type);
                    var property = new PackedProperty(
                        definition,
                        propertyId,
                        currentOffset,
                        propertySize,
                        definition.Type);
                    property.Handle = new VividPerObjectPropertyHandle(
                        this,
                        propertyId,
                        currentOffset,
                        definition.Type,
                        0u);
                    packedProperties.Add(property);
                    propertiesById.Add(propertyId, property);
                    currentOffset += propertySize;
                }
            }

            int recordStride = AlignUp(currentOffset, RecordAlignment);
            uint signature = ComputeSignature(m_ShaderIdentifier, packedProperties, recordStride);
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

            m_Cache = new LayoutCache(signature, recordStride, packedProperties, propertiesById);
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

        private static bool IsAsciiLetter(char character)
        {
            return character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
        }

        private static void ValidateIdentifier(string value, string fieldName)
        {
            if (!IsValidIdentifier(value))
            {
                throw new InvalidOperationException(
                    $"Per-object layout {fieldName} '{value}' is not a valid ASCII HLSL identifier.");
            }
        }

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

        private static void AddSingle(ref uint hash, float value)
        {
            AddInt32(ref hash, BitConverter.SingleToInt32Bits(value));
        }

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

        private static void AddByte(ref uint hash, byte value)
        {
            hash ^= value;
            hash *= 16777619u;
        }

        private static int AlignUp(int value, int alignment)
        {
            return (value + alignment - 1) & ~(alignment - 1);
        }

        private static void WriteUInt32(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteInt32(byte[] destination, int offset, int value)
        {
            WriteUInt32(destination, offset, unchecked((uint)value));
        }

        private static void WriteSingle(byte[] destination, int offset, float value)
        {
            WriteInt32(destination, offset, BitConverter.SingleToInt32Bits(value));
        }

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
                List<PackedProperty> packedProperties,
                Dictionary<int, PackedProperty> propertiesById)
            {
                Signature = signature;
                RecordStride = recordStride;
                PackedProperties = packedProperties;
                PropertiesById = propertiesById;
            }

            internal uint Signature { get; }

            internal int RecordStride { get; }

            internal List<PackedProperty> PackedProperties { get; }

            internal Dictionary<int, PackedProperty> PropertiesById { get; }
        }
    }
}
