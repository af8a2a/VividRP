using System;
using System.Globalization;

namespace VividRP.Runtime.GPUDriven
{
    internal static class MaterialProgramContract
    {
        internal const uint IRSchemaVersion = 3u;
        internal const uint CanonicalIRVersion = 2u;
        internal const uint ClosureExpressionVersion = 1u;
        internal const uint StageLIRVersion = 1u;
        internal const uint DerivativeLegalizationVersion = 1u;
        internal const uint ProgramLoweringVersion = 4u;
        internal const uint GenericLayoutVersion = 1u;
        internal const uint LayoutFingerprintVersion = 1u;
        internal const uint DeferredExportContractVersion = 1u;
        internal const uint DeferredExportFingerprintVersion = 1u;
        internal const uint ProgramCatalogVersion = 3u;
        internal const uint ProgramCatalogManifestVersion = 2u;
        internal const uint SurfaceHlslArtifactVersion = 3u;
        internal const uint SurfaceHlslBackendVersion = 4u;
        internal const uint CoverageHlslArtifactVersion = 1u;
        internal const uint CoverageHlslBackendVersion = 1u;
        internal const uint SemanticHashVersion = 4u;
        internal const uint CompiledHashVersion = 6u;
        internal const uint CompilerVersion = 11u;
        internal const uint NativeTemplateBackendVersion = 6u;
        internal const uint VerifierVersion = 3u;
        internal const uint RuntimeAbiVersion = 1u;

        internal const int BuiltinProgramCount = 3;
    }

    internal enum MaterialProgramBackendKind : uint
    {
        NativeTemplate = 0u,
    }

    // Hashes are deterministic 64-bit fingerprints. A catalog hash match never proves
    // identity; exact canonical and compiled payloads must still be compared.
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

    internal readonly struct MaterialProgramCatalogManifestHash :
        IEquatable<MaterialProgramCatalogManifestHash>
    {
        internal MaterialProgramCatalogManifestHash(uint version, ulong value)
        {
            Version = version;
            Value = value;
        }

        internal uint Version { get; }

        internal ulong Value { get; }

        public bool Equals(MaterialProgramCatalogManifestHash other)
        {
            return Version == other.Version && Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialProgramCatalogManifestHash other
                && Equals(other);
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
                "manifest_v={0} 0x{1:X16}",
                Version,
                Value);
        }

        public static bool operator ==(
            MaterialProgramCatalogManifestHash left,
            MaterialProgramCatalogManifestHash right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            MaterialProgramCatalogManifestHash left,
            MaterialProgramCatalogManifestHash right)
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

    internal static class MaterialProgramCatalogManifestHashBuilder
    {
        internal static MaterialProgramCatalogManifestHash Compute(
            System.Collections.Generic.IReadOnlyList<
                MaterialProgramCatalog.ManifestEntry> slots,
            System.Collections.Generic.IReadOnlyList<string> slotNames)
        {
            if (slots == null)
                throw new ArgumentNullException(nameof(slots));
            if (slotNames == null)
                throw new ArgumentNullException(nameof(slotNames));
            if (slots.Count != slotNames.Count)
            {
                throw new ArgumentException(
                    "Manifest slot entries and stable names must have the same length.",
                    nameof(slotNames));
            }

            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            MaterialProgramHashUtility.Add(
                ref hash,
                MaterialProgramContract.ProgramCatalogManifestVersion);
            MaterialProgramHashUtility.Add(
                ref hash,
                MaterialProgramContract.ProgramCatalogVersion);
            MaterialProgramHashUtility.Add(ref hash, slots.Count);
            for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
            {
                MaterialProgramCatalog.ManifestEntry entry = slots[slotIndex];
                MaterialProgramHashUtility.Add(ref hash, slotIndex);
                MaterialProgramHashUtility.Add(ref hash, slotNames[slotIndex]);
                MaterialProgramHashUtility.Add(ref hash, entry != null);
                if (entry == null)
                    continue;
                if ((uint) entry.ProgramID != (uint) slotIndex)
                {
                    throw new ArgumentException(
                        "Manifest entry ProgramID must equal its frozen table index.",
                        nameof(slots));
                }

                MaterialProgramHashUtility.Add(ref hash, (uint) entry.ProgramID);
                if (!string.Equals(
                        entry.StableName,
                        slotNames[slotIndex],
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Manifest entry stable name must match its frozen slot name.",
                        nameof(slotNames));
                }
                MaterialProgramHashUtility.Add(
                    ref hash,
                    entry.Program.CompiledHash.Version);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    entry.Program.CompiledHash.Value);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    entry.LayoutFingerprint.Version);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    entry.LayoutFingerprint.Value);
                AddDeferredExportContract(
                    ref hash,
                    entry.Program.DeferredExportContract);
                AddRuntimeData(ref hash, entry.RuntimeData);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    entry.Program.CoverageHlsl.PayloadHash);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    entry.Program.SurfaceHlsl.PayloadHash);
            }

            return new MaterialProgramCatalogManifestHash(
                MaterialProgramContract.ProgramCatalogManifestVersion,
                hash);
        }

        private static void AddRuntimeData(
            ref ulong hash,
            in VividMaterialProgramData runtimeData)
        {
            MaterialProgramHashUtility.Add(ref hash, runtimeData.Version);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) runtimeData.ExecutionClass);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) runtimeData.CoverageProgramID);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) runtimeData.SurfaceProgramID);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) runtimeData.TransportProgramID);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) runtimeData.ParameterLayoutID);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) runtimeData.ResourceLayoutID);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) runtimeData.CapabilityFlags);
        }

        private static void AddDeferredExportContract(
            ref ulong hash,
            MaterialDeferredExportContract contract)
        {
            MaterialProgramHashUtility.Add(ref hash, contract.Fingerprint.Version);
            MaterialProgramHashUtility.Add(ref hash, contract.Fingerprint.Value);
            MaterialDeferredExportContractHashBuilder.AddPayload(ref hash, contract);
        }
    }

    internal static class CompiledMaterialProgramHashBuilder
    {
        internal static CompiledMaterialProgramHash ComputeNativeTemplate(
            in MaterialSemanticHash semanticHash,
            MaterialProgramLoweringResult lowering,
            MaterialCoverageHlslArtifact coverageHlsl,
            MaterialSurfaceHlslArtifact surfaceHlsl)
        {
            if (lowering == null)
                throw new ArgumentNullException(nameof(lowering));
            if (coverageHlsl == null)
                throw new ArgumentNullException(nameof(coverageHlsl));
            if (surfaceHlsl == null)
                throw new ArgumentNullException(nameof(surfaceHlsl));

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
                MaterialProgramContract.ProgramLoweringVersion);
            MaterialProgramHashUtility.Add(
                ref hash,
                MaterialProgramContract.ProgramCatalogVersion);

            AddSelectionKey(ref hash, lowering.SelectionKey);
            AddDeferredExportContract(
                ref hash,
                lowering.DeferredExportContract);
            AddGenericLayout(ref hash, lowering.GenericLayout);
            AddNativeLayoutSchema(ref hash, lowering.Template);
            MaterialProgramHashUtility.Add(
                ref hash,
                lowering.LayoutFingerprint.Version);
            MaterialProgramHashUtility.Add(
                ref hash,
                lowering.LayoutFingerprint.Value);
            AddRuntimeData(ref hash, lowering.RuntimeData);
            AddParameterLayout(
                ref hash,
                lowering.MaterialLayout.ParameterLayout);
            AddResourceLayout(
                ref hash,
                lowering.MaterialLayout.ResourceLayout);
            AddCoverageHlslArtifact(ref hash, coverageHlsl);
            AddSurfaceHlslArtifact(ref hash, surfaceHlsl);
            return new CompiledMaterialProgramHash(
                MaterialProgramContract.CompiledHashVersion,
                hash);
        }

        private static void AddCoverageHlslArtifact(
            ref ulong hash,
            MaterialCoverageHlslArtifact artifact)
        {
            MaterialProgramHashUtility.Add(ref hash, artifact.Version);
            MaterialProgramHashUtility.Add(ref hash, artifact.BackendVersion);
            MaterialProgramHashUtility.Add(ref hash, (int) artifact.PhysicalContract);
            MaterialProgramHashUtility.Add(ref hash, artifact.BindingHash);
            MaterialProgramHashUtility.Add(ref hash, artifact.CodeHash);
            MaterialProgramHashUtility.Add(ref hash, artifact.EntryPoint);
            MaterialProgramHashUtility.Add(ref hash, artifact.Source);
        }

        private static void AddSurfaceHlslArtifact(
            ref ulong hash,
            MaterialSurfaceHlslArtifact artifact)
        {
            MaterialProgramHashUtility.Add(ref hash, artifact.Version);
            MaterialProgramHashUtility.Add(ref hash, artifact.BackendVersion);
            MaterialProgramHashUtility.Add(ref hash, (int) artifact.Topology);
            MaterialProgramHashUtility.Add(ref hash, (int) artifact.PhysicalContract);
            MaterialProgramHashUtility.Add(ref hash, artifact.BindingHash);
            MaterialProgramHashUtility.Add(ref hash, artifact.CodeHash);
            MaterialProgramHashUtility.Add(ref hash, artifact.EntryPoint);
            MaterialProgramHashUtility.Add(ref hash, artifact.Source);
        }

        private static void AddSelectionKey(
            ref ulong hash,
            in MaterialProgramSelectionKey selectionKey)
        {
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) selectionKey.BackendKind);
            MaterialProgramHashUtility.Add(ref hash, selectionKey.BackendVersion);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) selectionKey.CoverageProgramID);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) selectionKey.SurfaceProgramID);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) selectionKey.TransportProgramID);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) selectionKey.Topology);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) selectionKey.ExecutionClass);
        }

        private static void AddDeferredExportContract(
            ref ulong hash,
            MaterialDeferredExportContract contract)
        {
            MaterialProgramHashUtility.Add(ref hash, contract.Fingerprint.Version);
            MaterialProgramHashUtility.Add(ref hash, contract.Fingerprint.Value);
            MaterialDeferredExportContractHashBuilder.AddPayload(ref hash, contract);
        }

        private static void AddGenericLayout(
            ref ulong hash,
            MaterialGenericLayout layout)
        {
            MaterialProgramHashUtility.Add(ref hash, layout.Version);
            MaterialProgramHashUtility.Add(
                ref hash,
                layout.ParameterStrideInWords);
            MaterialProgramHashUtility.Add(
                ref hash,
                layout.ParameterBindings.Count);
            for (int bindingIndex = 0;
                 bindingIndex < layout.ParameterBindings.Count;
                 bindingIndex++)
            {
                MaterialGenericParameterBinding binding =
                    layout.ParameterBindings[bindingIndex];
                MaterialProgramHashUtility.Add(
                    ref hash,
                    binding.Declaration.Symbol);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (int) binding.Declaration.Type);
                MaterialProgramHashUtility.Add(ref hash, binding.WordOffset);
                MaterialProgramHashUtility.Add(ref hash, binding.WordCount);
            }

            MaterialProgramHashUtility.Add(
                ref hash,
                layout.ResourceBindings.Count);
            for (int bindingIndex = 0;
                 bindingIndex < layout.ResourceBindings.Count;
                 bindingIndex++)
            {
                MaterialGenericResourceBinding binding =
                    layout.ResourceBindings[bindingIndex];
                MaterialProgramHashUtility.Add(
                    ref hash,
                    binding.Declaration.Symbol);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (int) binding.Declaration.Type);
                MaterialProgramHashUtility.Add(ref hash, binding.Slot);
            }
        }

        private static void AddNativeLayoutSchema(
            ref ulong hash,
            MaterialProgramTemplate template)
        {
            MaterialProgramHashUtility.Add(
                ref hash,
                template.RuntimeAbiVersion);
            MaterialProgramHashUtility.Add(
                ref hash,
                (uint) template.Capabilities);
            MaterialNativeTemplateLayoutSchema schema =
                template.LayoutSchema;
            MaterialProgramHashUtility.Add(
                ref hash,
                schema.ParameterBindings.Count);
            for (int bindingIndex = 0;
                 bindingIndex < schema.ParameterBindings.Count;
                 bindingIndex++)
            {
                MaterialNativeParameterBinding binding =
                    schema.ParameterBindings[bindingIndex];
                MaterialProgramHashUtility.Add(
                    ref hash,
                    binding.Declaration.Symbol);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (int) binding.Declaration.Type);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (int) binding.Target);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (int) binding.Conversion);
            }

            MaterialProgramHashUtility.Add(
                ref hash,
                schema.ResourceBindings.Count);
            for (int bindingIndex = 0;
                 bindingIndex < schema.ResourceBindings.Count;
                 bindingIndex++)
            {
                MaterialNativeResourceBinding binding =
                    schema.ResourceBindings[bindingIndex];
                MaterialProgramHashUtility.Add(
                    ref hash,
                    binding.Declaration.Symbol);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (int) binding.Declaration.Type);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (int) binding.Target);
            }
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
