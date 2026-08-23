using System;
using System.Globalization;

namespace VividRP.Runtime.GPUDriven
{
    internal static class MaterialProgramContract
    {
        internal const uint IRSchemaVersion = 3u;
        internal const uint CanonicalIRVersion = 2u;
        internal const uint ClosureExpressionVersion = 1u;
        internal const uint SemanticHashVersion = 4u;
        internal const uint CompiledHashVersion = 1u;
        internal const uint CompilerVersion = 4u;
        internal const uint NativeTemplateBackendVersion = 2u;
        internal const uint VerifierVersion = 2u;
        internal const uint RuntimeAbiVersion = 1u;

        internal const int BuiltinProgramCount = 3;
    }

    internal enum MaterialProgramBackendKind : uint
    {
        NativeTemplate = 0u,
    }

    // Hashes are deterministic 64-bit fingerprints. Dynamic catalogs must compare
    // canonical payloads after a hash match; the fixed native catalog uses golden tests.
    internal readonly struct MaterialSemanticHash : IEquatable<MaterialSemanticHash>
    {
        internal MaterialSemanticHash(
            uint irSchemaVersion,
            uint version,
            ulong value)
        {
            IRSchemaVersion = irSchemaVersion;
            Version = version;
            Value = value;
        }

        internal uint IRSchemaVersion { get; }

        internal uint Version { get; }

        internal ulong Value { get; }

        public bool Equals(MaterialSemanticHash other)
        {
            return IRSchemaVersion == other.IRSchemaVersion
                && Version == other.Version
                && Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialSemanticHash other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int) IRSchemaVersion;
                hashCode = (hashCode * 397) ^ (int) Version;
                hashCode = (hashCode * 397) ^ (int) Value;
                hashCode = (hashCode * 397) ^ (int) (Value >> 32);
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "ir={0} hash_v={1} 0x{2:X16}",
                IRSchemaVersion,
                Version,
                Value);
        }

        public static bool operator ==(
            MaterialSemanticHash left,
            MaterialSemanticHash right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            MaterialSemanticHash left,
            MaterialSemanticHash right)
        {
            return !left.Equals(right);
        }
    }

    internal readonly struct CompiledMaterialProgramHash :
        IEquatable<CompiledMaterialProgramHash>
    {
        internal CompiledMaterialProgramHash(uint version, ulong value)
        {
            Version = version;
            Value = value;
        }

        internal uint Version { get; }

        internal ulong Value { get; }

        public bool Equals(CompiledMaterialProgramHash other)
        {
            return Version == other.Version && Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is CompiledMaterialProgramHash other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int) Version;
                hashCode = (hashCode * 397) ^ (int) Value;
                hashCode = (hashCode * 397) ^ (int) (Value >> 32);
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "hash_v={0} 0x{1:X16}",
                Version,
                Value);
        }

        public static bool operator ==(
            CompiledMaterialProgramHash left,
            CompiledMaterialProgramHash right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            CompiledMaterialProgramHash left,
            CompiledMaterialProgramHash right)
        {
            return !left.Equals(right);
        }
    }

    internal static class MaterialProgramHashUtility
    {
        internal const ulong OffsetBasis = 14695981039346656037ul;
        private const ulong Prime = 1099511628211ul;

        internal static ulong Compute(byte[] payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            ulong hash = OffsetBasis;
            for (int byteIndex = 0; byteIndex < payload.Length; byteIndex++)
            {
                hash ^= payload[byteIndex];
                hash *= Prime;
            }
            return hash;
        }

        internal static void Add(ref ulong hash, bool value)
        {
            Add(ref hash, value ? 1u : 0u);
        }

        internal static void Add(ref ulong hash, int value)
        {
            Add(ref hash, unchecked((uint) value));
        }

        internal static void Add(ref ulong hash, uint value)
        {
            for (int byteIndex = 0; byteIndex < sizeof(uint); byteIndex++)
            {
                hash ^= (byte) (value >> (byteIndex * 8));
                hash *= Prime;
            }
        }

        internal static void Add(ref ulong hash, ulong value)
        {
            for (int byteIndex = 0; byteIndex < sizeof(ulong); byteIndex++)
            {
                hash ^= (byte) (value >> (byteIndex * 8));
                hash *= Prime;
            }
        }

        internal static void Add(ref ulong hash, string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            Add(ref hash, value.Length);
            for (int characterIndex = 0; characterIndex < value.Length; characterIndex++)
                Add(ref hash, (uint) value[characterIndex]);
        }
    }

    internal static class CompiledMaterialProgramHashBuilder
    {
        internal static CompiledMaterialProgramHash ComputeNativeTemplate(
            in MaterialSemanticHash semanticHash,
            in VividMaterialProgramData runtimeData,
            CompiledMaterialLayout materialLayout)
        {
            if (materialLayout == null)
                throw new ArgumentNullException(nameof(materialLayout));

            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            MaterialProgramHashUtility.Add(
                ref hash,
                MaterialProgramContract.CompiledHashVersion);
            MaterialProgramHashUtility.Add(ref hash, semanticHash.IRSchemaVersion);
            MaterialProgramHashUtility.Add(ref hash, semanticHash.Version);
            MaterialProgramHashUtility.Add(ref hash, semanticHash.Value);
            MaterialProgramHashUtility.Add(
                ref hash,
                MaterialProgramContract.CompilerVersion);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) MaterialProgramBackendKind.NativeTemplate);
            MaterialProgramHashUtility.Add(
                ref hash,
                MaterialProgramContract.NativeTemplateBackendVersion);

            AddRuntimeData(ref hash, runtimeData);
            AddParameterLayout(ref hash, materialLayout.ParameterLayout);
            AddResourceLayout(ref hash, materialLayout.ResourceLayout);
            return new CompiledMaterialProgramHash(
                MaterialProgramContract.CompiledHashVersion,
                hash);
        }

        private static void AddRuntimeData(
            ref ulong hash,
            in VividMaterialProgramData runtimeData)
        {
            MaterialProgramHashUtility.Add(ref hash, runtimeData.Version);
            MaterialProgramHashUtility.Add(ref hash, (uint) runtimeData.ExecutionClass);
            MaterialProgramHashUtility.Add(ref hash, (uint) runtimeData.CoverageProgramID);
            MaterialProgramHashUtility.Add(ref hash, (uint) runtimeData.SurfaceProgramID);
            MaterialProgramHashUtility.Add(ref hash, (uint) runtimeData.TransportProgramID);
            MaterialProgramHashUtility.Add(ref hash, (uint) runtimeData.ParameterLayoutID);
            MaterialProgramHashUtility.Add(ref hash, (uint) runtimeData.ResourceLayoutID);
            MaterialProgramHashUtility.Add(ref hash, (uint) runtimeData.CapabilityFlags);
        }

        private static void AddParameterLayout(
            ref ulong hash,
            CompiledParameterLayout layout)
        {
            MaterialProgramHashUtility.Add(ref hash, (uint) layout.LayoutID);
            MaterialProgramHashUtility.Add(ref hash, layout.Stride);
            MaterialProgramHashUtility.Add(ref hash, layout.Bindings.Count);

            var bindings = new MaterialParameterLayoutBinding[layout.Bindings.Count];
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                bindings[bindingIndex] = layout.Bindings[bindingIndex];
            Array.Sort(bindings, CompareParameterBindings);

            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                MaterialParameterLayoutBinding binding = bindings[bindingIndex];
                MaterialProgramHashUtility.Add(ref hash, binding.ByteOffset);
                MaterialProgramHashUtility.Add(ref hash, (int) binding.Parameter);
                MaterialProgramHashUtility.Add(ref hash, (int) binding.Type);
            }
        }

        private static void AddResourceLayout(
            ref ulong hash,
            CompiledResourceLayout layout)
        {
            MaterialProgramHashUtility.Add(ref hash, (uint) layout.LayoutID);
            MaterialProgramHashUtility.Add(ref hash, layout.RecordStride);
            MaterialProgramHashUtility.Add(ref hash, layout.RecordCount);
            MaterialProgramHashUtility.Add(ref hash, layout.Bindings.Count);

            var bindings = new MaterialResourceLayoutBinding[layout.Bindings.Count];
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                bindings[bindingIndex] = layout.Bindings[bindingIndex];
            Array.Sort(bindings, CompareResourceBindings);

            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                MaterialResourceLayoutBinding binding = bindings[bindingIndex];
                MaterialProgramHashUtility.Add(ref hash, binding.RecordOffset);
                MaterialProgramHashUtility.Add(ref hash, binding.ByteOffset);
                MaterialProgramHashUtility.Add(ref hash, (int) binding.Resource);
            }
        }

        private static int CompareParameterBindings(
            MaterialParameterLayoutBinding left,
            MaterialParameterLayoutBinding right)
        {
            int result = left.ByteOffset.CompareTo(right.ByteOffset);
            return result != 0
                ? result
                : ((int) left.Parameter).CompareTo((int) right.Parameter);
        }

        private static int CompareResourceBindings(
            MaterialResourceLayoutBinding left,
            MaterialResourceLayoutBinding right)
        {
            int result = left.RecordOffset.CompareTo(right.RecordOffset);
            if (result != 0)
                return result;

            result = left.ByteOffset.CompareTo(right.ByteOffset);
            return result != 0
                ? result
                : ((int) left.Resource).CompareTo((int) right.Resource);
        }
    }
}
