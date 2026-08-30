using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    [Serializable]
    internal struct MaterialRuntimeParameterBindingDescriptor
    {
        [SerializeField]
        private string m_Symbol;

        [SerializeField]
        private MaterialValueType m_Type;

        [SerializeField]
        private int m_WordOffset;

        [SerializeField]
        private int m_WordCount;

        internal MaterialRuntimeParameterBindingDescriptor(
            in MaterialParameterDeclaration declaration,
            int wordOffset,
            int wordCount)
        {
            m_Symbol = declaration.Symbol;
            m_Type = declaration.Type;
            m_WordOffset = wordOffset;
            m_WordCount = wordCount;
        }

        internal MaterialParameterDeclaration Declaration =>
            new(m_Symbol, m_Type);

        internal string Symbol => m_Symbol;

        internal MaterialValueType Type => m_Type;

        internal int WordOffset => m_WordOffset;

        internal int WordCount => m_WordCount;

        internal bool PayloadEquals(
            in MaterialRuntimeParameterBindingDescriptor other)
        {
            return string.Equals(m_Symbol, other.m_Symbol, StringComparison.Ordinal)
                && m_Type == other.m_Type
                && m_WordOffset == other.m_WordOffset
                && m_WordCount == other.m_WordCount;
        }
    }

    [Serializable]
    internal struct MaterialRuntimeResourceBindingDescriptor
    {
        [SerializeField]
        private string m_Symbol;

        [SerializeField]
        private MaterialValueType m_Type;

        [SerializeField]
        private MaterialTextureSampleClass m_SampleClass;

        [SerializeField]
        private int m_Slot;

        internal MaterialRuntimeResourceBindingDescriptor(
            in MaterialResourceDeclaration declaration,
            int slot)
        {
            m_Symbol = declaration.Symbol;
            m_Type = declaration.Type;
            m_SampleClass = declaration.SampleClass;
            m_Slot = slot;
        }

        internal MaterialResourceDeclaration Declaration =>
            new(m_Symbol, m_Type, m_SampleClass);

        internal string Symbol => m_Symbol;

        internal MaterialValueType Type => m_Type;

        internal MaterialTextureSampleClass SampleClass => m_SampleClass;

        internal int Slot => m_Slot;

        internal bool PayloadEquals(
            in MaterialRuntimeResourceBindingDescriptor other)
        {
            return string.Equals(m_Symbol, other.m_Symbol, StringComparison.Ordinal)
                && m_Type == other.m_Type
                && m_SampleClass == other.m_SampleClass
                && m_Slot == other.m_Slot;
        }
    }

    internal sealed class MaterialProgramRuntimeBinding
    {
        private readonly IReadOnlyList<MaterialRuntimeParameterBindingDescriptor>
            m_ParameterBindings;
        private readonly IReadOnlyList<MaterialRuntimeResourceBindingDescriptor>
            m_ResourceBindings;

        internal MaterialProgramRuntimeBinding(
            MaterialProgramCatalog.ManifestEntry entry)
        {
            CatalogProgram = entry
                ?? throw new ArgumentNullException(nameof(entry));
            ProgramID = entry.ProgramID;
            StableName = entry.StableName;
            RuntimeData = entry.RuntimeData;
            CompiledHash = entry.Program.CompiledHash;
            LayoutFingerprint = entry.LayoutFingerprint;
            Topology = entry.Program.Lowering.SelectionKey.Topology;
            MaterialGenericLayout layout = entry.Program.Lowering.GenericLayout;
            ParameterStrideInWords = layout.ParameterStrideInWords;
            ResourceCount = layout.ResourceCount;
            CreateDescriptors(
                layout,
                out MaterialRuntimeParameterBindingDescriptor[] parameters,
                out MaterialRuntimeResourceBindingDescriptor[] resources);
            m_ParameterBindings = Array.AsReadOnly(parameters);
            m_ResourceBindings = Array.AsReadOnly(resources);
        }

        internal MaterialProgramRuntimeBinding(MaterialProgramCatalogAsset.Slot slot)
        {
            if (slot == null)
                throw new ArgumentNullException(nameof(slot));
            if (slot.IsReserved)
            {
                throw new ArgumentException(
                    "A reserved catalog slot has no runtime binding.",
                    nameof(slot));
            }
            if (!slot.ValidateRuntimeDescriptor(out string failure))
                throw new ArgumentException(failure, nameof(slot));

            ProgramID = slot.ProgramID;
            StableName = slot.StableName;
            RuntimeData = slot.RuntimeData;
            CompiledHash = slot.CompiledHash;
            LayoutFingerprint = slot.LayoutFingerprint;
            Topology = slot.Topology;
            ParameterStrideInWords = checked((int) slot.ParameterStrideInWords);
            ResourceCount = checked((int) slot.ResourceCount);
            m_ParameterBindings = Array.AsReadOnly(slot.CopyParameterBindings());
            m_ResourceBindings = Array.AsReadOnly(slot.CopyResourceBindings());
        }

        internal VividMaterialProgramID ProgramID { get; }

        internal string StableName { get; }

        internal VividMaterialProgramData RuntimeData { get; }

        internal CompiledMaterialProgramHash CompiledHash { get; }

        internal MaterialProgramLayoutFingerprint LayoutFingerprint { get; }

        internal MaterialProgramTopologySpecialization Topology { get; }

        internal int ParameterStrideInWords { get; }

        internal int ResourceCount { get; }

        internal IReadOnlyList<MaterialRuntimeParameterBindingDescriptor>
            ParameterBindings => m_ParameterBindings;

        internal IReadOnlyList<MaterialRuntimeResourceBindingDescriptor>
            ResourceBindings => m_ResourceBindings;

        internal MaterialProgramCatalog.ManifestEntry CatalogProgram { get; }

        private static void CreateDescriptors(
            MaterialGenericLayout layout,
            out MaterialRuntimeParameterBindingDescriptor[] parameters,
            out MaterialRuntimeResourceBindingDescriptor[] resources)
        {
            parameters = new MaterialRuntimeParameterBindingDescriptor[
                layout.ParameterBindings.Count];
            for (int bindingIndex = 0;
                 bindingIndex < parameters.Length;
                 bindingIndex++)
            {
                MaterialGenericParameterBinding genericBinding =
                    layout.ParameterBindings[bindingIndex];
                parameters[bindingIndex] =
                    new MaterialRuntimeParameterBindingDescriptor(
                        genericBinding.Declaration,
                        genericBinding.WordOffset,
                        genericBinding.WordCount);
            }

            resources = new MaterialRuntimeResourceBindingDescriptor[
                layout.ResourceBindings.Count];
            for (int bindingIndex = 0;
                 bindingIndex < resources.Length;
                 bindingIndex++)
            {
                MaterialGenericResourceBinding genericBinding =
                    layout.ResourceBindings[bindingIndex];
                resources[bindingIndex] =
                    new MaterialRuntimeResourceBindingDescriptor(
                        genericBinding.Declaration,
                        genericBinding.Slot);
            }
        }
    }

    internal sealed class MaterialProgramCatalogAsset : ScriptableObject
    {
        [Serializable]
        internal sealed class Slot
        {
            [SerializeField]
            private uint m_ProgramID;

            [SerializeField]
            private string m_StableName;

            [SerializeField]
            private bool m_IsReserved;

            [SerializeField]
            private uint m_CompiledHashVersion;

            [SerializeField]
            private ulong m_CompiledHash;

            [SerializeField]
            private uint m_LayoutFingerprintVersion;

            [SerializeField]
            private ulong m_LayoutFingerprint;

            [SerializeField]
            private uint m_DeferredExportFingerprintVersion;

            [SerializeField]
            private ulong m_DeferredExportFingerprint;

            [SerializeField]
            private ulong m_CoveragePayloadHash;

            [SerializeField]
            private ulong m_SurfacePayloadHash;

            [SerializeField]
            private uint m_RuntimeVersion;

            [SerializeField]
            private VividMaterialCoverageProgramID m_CoverageProgramID;

            [SerializeField]
            private VividMaterialSurfaceProgramID m_SurfaceProgramID;

            [SerializeField]
            private VividMaterialTransportProgramID m_TransportProgramID;

            [SerializeField]
            private VividMaterialParameterLayoutID m_ParameterLayoutID;

            [SerializeField]
            private VividMaterialResourceLayoutID m_ResourceLayoutID;

            [SerializeField]
            private VividMaterialProgramCapabilities m_CapabilityFlags;

            [SerializeField]
            private VividMaterialExecutionClass m_ExecutionClass;

            [SerializeField]
            private MaterialProgramTopologySpecialization m_Topology;

            [SerializeField]
            private uint m_ParameterStrideInWords;

            [SerializeField]
            private uint m_ResourceCount;

            [SerializeField]
            private MaterialRuntimeParameterBindingDescriptor[]
                m_ParameterBindings =
                    Array.Empty<MaterialRuntimeParameterBindingDescriptor>();

            [SerializeField]
            private MaterialRuntimeResourceBindingDescriptor[]
                m_ResourceBindings =
                    Array.Empty<MaterialRuntimeResourceBindingDescriptor>();

            internal VividMaterialProgramID ProgramID =>
                (VividMaterialProgramID) m_ProgramID;

            internal string StableName => m_StableName;

            internal bool IsReserved => m_IsReserved;

            internal CompiledMaterialProgramHash CompiledHash =>
                new CompiledMaterialProgramHash(
                    m_CompiledHashVersion,
                    m_CompiledHash);

            internal MaterialProgramLayoutFingerprint LayoutFingerprint =>
                new MaterialProgramLayoutFingerprint(
                    m_LayoutFingerprintVersion,
                    m_LayoutFingerprint);

            internal MaterialDeferredExportContractFingerprint
                DeferredExportFingerprint =>
                    new MaterialDeferredExportContractFingerprint(
                        m_DeferredExportFingerprintVersion,
                        m_DeferredExportFingerprint);

            internal ulong CoveragePayloadHash => m_CoveragePayloadHash;

            internal ulong SurfacePayloadHash => m_SurfacePayloadHash;

            internal VividMaterialProgramData RuntimeData =>
                new VividMaterialProgramData
                {
                    Version = m_RuntimeVersion,
                    CoverageProgramID = m_CoverageProgramID,
                    SurfaceProgramID = m_SurfaceProgramID,
                    TransportProgramID = m_TransportProgramID,
                    ParameterLayoutID = m_ParameterLayoutID,
                    ResourceLayoutID = m_ResourceLayoutID,
                    CapabilityFlags = m_CapabilityFlags,
                    ExecutionClass = m_ExecutionClass,
                };

            internal uint ParameterStrideInWords => m_ParameterStrideInWords;

            internal uint ResourceCount => m_ResourceCount;

            internal MaterialProgramTopologySpecialization Topology => m_Topology;

            internal static Slot Create(
                int slotIndex,
                string stableName,
                MaterialProgramCatalog.ManifestEntry entry)
            {
                var slot = new Slot
                {
                    m_ProgramID = checked((uint) slotIndex),
                    m_StableName = stableName,
                    m_IsReserved = entry == null,
                };
                if (entry == null)
                    return slot;

                CompiledMaterialProgram program = entry.Program;
                VividMaterialProgramData runtimeData = entry.RuntimeData;
                slot.m_CompiledHashVersion = program.CompiledHash.Version;
                slot.m_CompiledHash = program.CompiledHash.Value;
                slot.m_LayoutFingerprintVersion =
                    entry.LayoutFingerprint.Version;
                slot.m_LayoutFingerprint = entry.LayoutFingerprint.Value;
                slot.m_DeferredExportFingerprintVersion =
                    program.DeferredExportContract.Fingerprint.Version;
                slot.m_DeferredExportFingerprint =
                    program.DeferredExportContract.Fingerprint.Value;
                slot.m_CoveragePayloadHash = program.CoverageHlsl.PayloadHash;
                slot.m_SurfacePayloadHash = program.SurfaceHlsl.PayloadHash;
                slot.m_RuntimeVersion = runtimeData.Version;
                slot.m_CoverageProgramID = runtimeData.CoverageProgramID;
                slot.m_SurfaceProgramID = runtimeData.SurfaceProgramID;
                slot.m_TransportProgramID = runtimeData.TransportProgramID;
                slot.m_ParameterLayoutID = runtimeData.ParameterLayoutID;
                slot.m_ResourceLayoutID = runtimeData.ResourceLayoutID;
                slot.m_CapabilityFlags = runtimeData.CapabilityFlags;
                slot.m_ExecutionClass = runtimeData.ExecutionClass;
                MaterialGenericLayout genericLayout =
                    program.Lowering.GenericLayout;
                slot.m_Topology = program.Lowering.SelectionKey.Topology;
                slot.m_ParameterStrideInWords = checked(
                    (uint) genericLayout.ParameterStrideInWords);
                slot.m_ResourceCount = checked((uint) genericLayout.ResourceCount);
                var binding = new MaterialProgramRuntimeBinding(entry);
                slot.m_ParameterBindings = new MaterialRuntimeParameterBindingDescriptor[
                    binding.ParameterBindings.Count];
                for (int bindingIndex = 0;
                     bindingIndex < slot.m_ParameterBindings.Length;
                     bindingIndex++)
                {
                    slot.m_ParameterBindings[bindingIndex] =
                        binding.ParameterBindings[bindingIndex];
                }
                slot.m_ResourceBindings = new MaterialRuntimeResourceBindingDescriptor[
                    binding.ResourceBindings.Count];
                for (int bindingIndex = 0;
                     bindingIndex < slot.m_ResourceBindings.Length;
                     bindingIndex++)
                {
                    slot.m_ResourceBindings[bindingIndex] =
                        binding.ResourceBindings[bindingIndex];
                }
                return slot;
            }

            internal MaterialRuntimeParameterBindingDescriptor[]
                CopyParameterBindings()
            {
                return m_ParameterBindings != null
                    ? (MaterialRuntimeParameterBindingDescriptor[])
                        m_ParameterBindings.Clone()
                    : Array.Empty<MaterialRuntimeParameterBindingDescriptor>();
            }

            internal MaterialRuntimeResourceBindingDescriptor[]
                CopyResourceBindings()
            {
                return m_ResourceBindings != null
                    ? (MaterialRuntimeResourceBindingDescriptor[])
                        m_ResourceBindings.Clone()
                    : Array.Empty<MaterialRuntimeResourceBindingDescriptor>();
            }

            internal void AddPayloadSeal(ref ulong hash)
            {
                MaterialProgramHashUtility.Add(ref hash, m_ProgramID);
                AddNullableString(ref hash, m_StableName);
                MaterialProgramHashUtility.Add(ref hash, m_IsReserved);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    m_CompiledHashVersion);
                MaterialProgramHashUtility.Add(ref hash, m_CompiledHash);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    m_LayoutFingerprintVersion);
                MaterialProgramHashUtility.Add(ref hash, m_LayoutFingerprint);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    m_DeferredExportFingerprintVersion);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    m_DeferredExportFingerprint);
                MaterialProgramHashUtility.Add(ref hash, m_CoveragePayloadHash);
                MaterialProgramHashUtility.Add(ref hash, m_SurfacePayloadHash);
                MaterialProgramHashUtility.Add(ref hash, m_RuntimeVersion);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (uint) m_CoverageProgramID);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (uint) m_SurfaceProgramID);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (uint) m_TransportProgramID);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (uint) m_ParameterLayoutID);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (uint) m_ResourceLayoutID);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (uint) m_CapabilityFlags);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    (uint) m_ExecutionClass);
                MaterialProgramHashUtility.Add(ref hash, (uint) m_Topology);
                MaterialProgramHashUtility.Add(
                    ref hash,
                    m_ParameterStrideInWords);
                MaterialProgramHashUtility.Add(ref hash, m_ResourceCount);

                MaterialProgramHashUtility.Add(
                    ref hash,
                    m_ParameterBindings != null);
                if (m_ParameterBindings != null)
                {
                    MaterialProgramHashUtility.Add(
                        ref hash,
                        m_ParameterBindings.Length);
                    for (int bindingIndex = 0;
                         bindingIndex < m_ParameterBindings.Length;
                         bindingIndex++)
                    {
                        MaterialRuntimeParameterBindingDescriptor binding =
                            m_ParameterBindings[bindingIndex];
                        AddNullableString(ref hash, binding.Symbol);
                        MaterialProgramHashUtility.Add(
                            ref hash,
                            (uint) binding.Type);
                        MaterialProgramHashUtility.Add(
                            ref hash,
                            binding.WordOffset);
                        MaterialProgramHashUtility.Add(
                            ref hash,
                            binding.WordCount);
                    }
                }

                MaterialProgramHashUtility.Add(
                    ref hash,
                    m_ResourceBindings != null);
                if (m_ResourceBindings != null)
                {
                    MaterialProgramHashUtility.Add(
                        ref hash,
                        m_ResourceBindings.Length);
                    for (int bindingIndex = 0;
                         bindingIndex < m_ResourceBindings.Length;
                         bindingIndex++)
                    {
                        MaterialRuntimeResourceBindingDescriptor binding =
                            m_ResourceBindings[bindingIndex];
                        AddNullableString(ref hash, binding.Symbol);
                        MaterialProgramHashUtility.Add(
                            ref hash,
                            (uint) binding.Type);
                        MaterialProgramHashUtility.Add(
                            ref hash,
                            (uint) binding.SampleClass);
                        MaterialProgramHashUtility.Add(
                            ref hash,
                            binding.Slot);
                    }
                }
            }

            private static void AddNullableString(
                ref ulong hash,
                string value)
            {
                MaterialProgramHashUtility.Add(ref hash, value != null);
                if (value != null)
                    MaterialProgramHashUtility.Add(ref hash, value);
            }

            internal bool MatchesCompiledProgram(CompiledMaterialProgram program)
            {
                if (program == null || m_IsReserved)
                    return false;
                VividMaterialProgramData runtimeData = program.RuntimeData;
                return CompiledHash == program.CompiledHash
                    && LayoutFingerprint
                        == program.Lowering.LayoutFingerprint
                    && DeferredExportFingerprint
                        == program.DeferredExportContract.Fingerprint
                    && m_CoveragePayloadHash == program.CoverageHlsl.PayloadHash
                    && m_SurfacePayloadHash == program.SurfaceHlsl.PayloadHash
                    && m_RuntimeVersion == runtimeData.Version
                    && m_CoverageProgramID == runtimeData.CoverageProgramID
                    && m_SurfaceProgramID == runtimeData.SurfaceProgramID
                    && m_TransportProgramID == runtimeData.TransportProgramID
                    && m_ParameterLayoutID == runtimeData.ParameterLayoutID
                    && m_ResourceLayoutID == runtimeData.ResourceLayoutID
                    && m_CapabilityFlags == runtimeData.CapabilityFlags
                    && m_ExecutionClass == runtimeData.ExecutionClass
                    && m_Topology == program.Lowering.SelectionKey.Topology;
            }

            internal bool PayloadEquals(Slot other)
            {
                return other != null
                    && m_ProgramID == other.m_ProgramID
                    && string.Equals(
                        m_StableName,
                        other.m_StableName,
                        StringComparison.Ordinal)
                    && m_IsReserved == other.m_IsReserved
                    && m_CompiledHashVersion == other.m_CompiledHashVersion
                    && m_CompiledHash == other.m_CompiledHash
                    && m_LayoutFingerprintVersion
                        == other.m_LayoutFingerprintVersion
                    && m_LayoutFingerprint == other.m_LayoutFingerprint
                    && m_DeferredExportFingerprintVersion
                        == other.m_DeferredExportFingerprintVersion
                    && m_DeferredExportFingerprint
                        == other.m_DeferredExportFingerprint
                    && m_CoveragePayloadHash == other.m_CoveragePayloadHash
                    && m_SurfacePayloadHash == other.m_SurfacePayloadHash
                    && m_RuntimeVersion == other.m_RuntimeVersion
                    && m_CoverageProgramID == other.m_CoverageProgramID
                    && m_SurfaceProgramID == other.m_SurfaceProgramID
                    && m_TransportProgramID == other.m_TransportProgramID
                    && m_ParameterLayoutID == other.m_ParameterLayoutID
                    && m_ResourceLayoutID == other.m_ResourceLayoutID
                    && m_CapabilityFlags == other.m_CapabilityFlags
                    && m_ExecutionClass == other.m_ExecutionClass
                    && m_Topology == other.m_Topology
                    && m_ParameterStrideInWords == other.m_ParameterStrideInWords
                    && m_ResourceCount == other.m_ResourceCount
                    && ParameterBindingsEqual(other)
                    && ResourceBindingsEqual(other);
            }

            internal bool ValidateRuntimeDescriptor(out string failure)
            {
                if (m_IsReserved)
                {
                    failure = string.Empty;
                    return true;
                }
                if (m_RuntimeVersion != MaterialProgramContract.RuntimeAbiVersion
                    || m_ParameterLayoutID
                        != VividMaterialParameterLayoutID.GenericParameterLanes
                    || m_ResourceLayoutID
                        != VividMaterialResourceLayoutID.GenericResourceRecords)
                {
                    failure = $"Catalog program {(uint) ProgramID} has an incompatible runtime contract.";
                    return false;
                }
                if ((m_ParameterStrideInWords & 3u) != 0u
                    || m_ParameterBindings == null
                    || m_ResourceBindings == null
                    || m_ResourceBindings.Length != checked((int) m_ResourceCount))
                {
                    failure = $"Catalog program {(uint) ProgramID} has an invalid runtime binding layout.";
                    return false;
                }

                var parameterSymbols = new HashSet<string>(StringComparer.Ordinal);
                var occupiedWords = new bool[checked((int) m_ParameterStrideInWords)];
                for (int bindingIndex = 0;
                     bindingIndex < m_ParameterBindings.Length;
                     bindingIndex++)
                {
                    MaterialRuntimeParameterBindingDescriptor binding =
                        m_ParameterBindings[bindingIndex];
                    int expectedWordCount = GetParameterWordCount(binding.Type);
                    if (string.IsNullOrEmpty(binding.Symbol)
                        || !parameterSymbols.Add(binding.Symbol)
                        || expectedWordCount <= 0
                        || binding.WordOffset < 0
                        || binding.WordCount != expectedWordCount
                        || binding.WordOffset + binding.WordCount
                            > checked((int) m_ParameterStrideInWords))
                    {
                        failure = $"Catalog program {(uint) ProgramID} has an invalid declaration-addressed parameter binding.";
                        return false;
                    }
                    for (int wordIndex = binding.WordOffset;
                         wordIndex < binding.WordOffset + binding.WordCount;
                         wordIndex++)
                    {
                        if (occupiedWords[wordIndex])
                        {
                            failure = $"Catalog program {(uint) ProgramID} has overlapping parameter bindings.";
                            return false;
                        }
                        occupiedWords[wordIndex] = true;
                    }
                }

                var resourceSymbols = new HashSet<string>(StringComparer.Ordinal);
                var occupiedSlots = new bool[checked((int) m_ResourceCount)];
                for (int bindingIndex = 0;
                     bindingIndex < m_ResourceBindings.Length;
                     bindingIndex++)
                {
                    MaterialRuntimeResourceBindingDescriptor binding =
                        m_ResourceBindings[bindingIndex];
                    int slot = binding.Slot;
                    if (string.IsNullOrEmpty(binding.Symbol)
                        || binding.Type != MaterialValueType.Texture2D
                        || (uint) binding.SampleClass
                            > (uint) MaterialTextureSampleClass.Mask
                        || !resourceSymbols.Add(binding.Symbol)
                        || slot < 0
                        || slot >= checked((int) m_ResourceCount)
                        || occupiedSlots[slot])
                    {
                        failure = $"Catalog program {(uint) ProgramID} has an invalid declaration-addressed resource binding.";
                        return false;
                    }
                    occupiedSlots[slot] = true;
                }

                if (!ValidateCanonicalGenericLayout(out failure))
                    return false;

                failure = string.Empty;
                return true;
            }

            private bool ValidateCanonicalGenericLayout(out string failure)
            {
                var parameters = new MaterialParameterDeclaration[
                    m_ParameterBindings.Length];
                for (int bindingIndex = 0;
                     bindingIndex < parameters.Length;
                     bindingIndex++)
                {
                    parameters[bindingIndex] =
                        m_ParameterBindings[bindingIndex].Declaration;
                }

                var resources = new MaterialResourceDeclaration[
                    m_ResourceBindings.Length];
                for (int bindingIndex = 0;
                     bindingIndex < resources.Length;
                     bindingIndex++)
                {
                    resources[bindingIndex] =
                        m_ResourceBindings[bindingIndex].Declaration;
                }

                MaterialGenericLayout canonicalLayout;
                try
                {
                    canonicalLayout = new MaterialGenericLayout(
                        parameters,
                        resources);
                }
                catch (ArgumentException)
                {
                    failure = $"Catalog program {(uint) ProgramID} cannot reconstruct its canonical generic layout.";
                    return false;
                }

                if (canonicalLayout.ParameterStrideInWords
                        != checked((int) m_ParameterStrideInWords)
                    || canonicalLayout.ResourceCount
                        != checked((int) m_ResourceCount))
                {
                    failure = $"Catalog program {(uint) ProgramID} does not use its canonical generic layout size.";
                    return false;
                }

                for (int bindingIndex = 0;
                     bindingIndex < m_ParameterBindings.Length;
                     bindingIndex++)
                {
                    MaterialRuntimeParameterBindingDescriptor descriptor =
                        m_ParameterBindings[bindingIndex];
                    if (!canonicalLayout.TryGetParameterBinding(
                            descriptor.Declaration,
                            out MaterialGenericParameterBinding binding)
                        || descriptor.WordOffset != binding.WordOffset
                        || descriptor.WordCount != binding.WordCount)
                    {
                        failure = $"Catalog program {(uint) ProgramID} has a non-canonical parameter binding for '{descriptor.Symbol}'.";
                        return false;
                    }
                }

                for (int bindingIndex = 0;
                     bindingIndex < m_ResourceBindings.Length;
                     bindingIndex++)
                {
                    MaterialRuntimeResourceBindingDescriptor descriptor =
                        m_ResourceBindings[bindingIndex];
                    if (!canonicalLayout.TryGetResourceBinding(
                            descriptor.Declaration,
                            out MaterialGenericResourceBinding binding)
                        || descriptor.Slot != binding.Slot)
                    {
                        failure = $"Catalog program {(uint) ProgramID} has a non-canonical resource binding for '{descriptor.Symbol}'.";
                        return false;
                    }
                }

                MaterialProgramLayoutFingerprint canonicalFingerprint =
                    MaterialProgramLayoutFingerprintBuilder.Compute(
                        canonicalLayout);
                if (LayoutFingerprint != canonicalFingerprint)
                {
                    failure = $"Catalog program {(uint) ProgramID} layout fingerprint does not match its declaration-addressed descriptors.";
                    return false;
                }

                failure = string.Empty;
                return true;
            }

            internal bool MatchesRuntimeTemplate(
                MaterialProgramTemplateRegistry templates)
            {
                if (m_IsReserved)
                    return true;
                for (int templateIndex = 0;
                     templateIndex < templates.Templates.Count;
                     templateIndex++)
                {
                    MaterialProgramTemplate template =
                        templates.Templates[templateIndex];
                    MaterialProgramSelectionKey key = template.SelectionKey;
                    if (template.RuntimeAbiVersion != m_RuntimeVersion
                        || template.Capabilities != m_CapabilityFlags
                        || key.CoverageProgramID != m_CoverageProgramID
                        || key.SurfaceProgramID != m_SurfaceProgramID
                        || key.TransportProgramID != m_TransportProgramID
                        || key.ExecutionClass != m_ExecutionClass
                        || key.Topology != m_Topology)
                    {
                        continue;
                    }

                    return true;
                }
                return false;
            }

            private static int GetParameterWordCount(MaterialValueType type)
            {
                switch (type)
                {
                    case MaterialValueType.Bool:
                    case MaterialValueType.Float:
                        return 1;
                    case MaterialValueType.Float2:
                        return 2;
                    case MaterialValueType.Float3:
                        return 3;
                    case MaterialValueType.Float4:
                        return 4;
                    default:
                        return 0;
                }
            }

            private bool ParameterBindingsEqual(Slot other)
            {
                if (m_ParameterBindings == null
                    || other.m_ParameterBindings == null
                    || m_ParameterBindings.Length
                        != other.m_ParameterBindings.Length)
                {
                    return m_ParameterBindings == null
                        && other.m_ParameterBindings == null;
                }
                for (int bindingIndex = 0;
                     bindingIndex < m_ParameterBindings.Length;
                     bindingIndex++)
                {
                    if (!m_ParameterBindings[bindingIndex].PayloadEquals(
                            other.m_ParameterBindings[bindingIndex]))
                    {
                        return false;
                    }
                }
                return true;
            }

            private bool ResourceBindingsEqual(Slot other)
            {
                if (m_ResourceBindings == null
                    || other.m_ResourceBindings == null
                    || m_ResourceBindings.Length
                        != other.m_ResourceBindings.Length)
                {
                    return m_ResourceBindings == null
                        && other.m_ResourceBindings == null;
                }
                for (int bindingIndex = 0;
                     bindingIndex < m_ResourceBindings.Length;
                     bindingIndex++)
                {
                    if (!m_ResourceBindings[bindingIndex].PayloadEquals(
                            other.m_ResourceBindings[bindingIndex]))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        internal const uint AssetSchemaVersion = 6u;
        internal const string DefaultResourceName = "VividMaterialProgramCatalog";

        [SerializeField]
        private uint m_AssetSchemaVersion;

        [SerializeField]
        private uint m_ProgramCatalogVersion;

        [SerializeField]
        private uint m_ManifestVersion;

        [SerializeField]
        private uint m_RuntimeAbiVersion;

        [SerializeField]
        private uint m_ManifestHashVersion;

        [SerializeField]
        private ulong m_ManifestHash;

        [SerializeField]
        private bool m_IsCommitted;

        [SerializeField]
        private uint m_ArtifactSetHashVersion;

        [SerializeField]
        private ulong m_ArtifactSetHash;

        [SerializeField]
        private uint m_CatalogPayloadSealVersion;

        [SerializeField]
        private ulong m_CatalogPayloadSealHash;

        [SerializeField]
        private Slot[] m_Slots = Array.Empty<Slot>();

        [NonSerialized]
        private MaterialProgramCatalog m_StagedCatalog;

        internal uint SchemaVersion => m_AssetSchemaVersion;

        internal uint ProgramCatalogVersion => m_ProgramCatalogVersion;

        internal uint ManifestVersion => m_ManifestVersion;

        internal uint RuntimeAbiVersion => m_RuntimeAbiVersion;

        internal MaterialProgramCatalogManifestHash ManifestHash =>
            new MaterialProgramCatalogManifestHash(
                m_ManifestHashVersion,
                m_ManifestHash);

        internal bool IsCommitted => m_IsCommitted;

        internal MaterialProgramArtifactSetHash ArtifactSetHash =>
            new MaterialProgramArtifactSetHash(
                m_ArtifactSetHashVersion,
                m_ArtifactSetHash);

        internal MaterialProgramArtifactSetHash PublishedGeneration =>
            ArtifactSetHash;

        internal MaterialProgramCatalogPayloadSeal CatalogPayloadSeal =>
            new MaterialProgramCatalogPayloadSeal(
                m_CatalogPayloadSealVersion,
                m_CatalogPayloadSealHash);

        internal IReadOnlyList<Slot> Slots => m_Slots;

        internal bool TryGetSlot(
            VividMaterialProgramID programID,
            out Slot slot)
        {
            uint slotIndex = (uint) programID;
            if (m_Slots != null && slotIndex < (uint) m_Slots.Length)
            {
                slot = m_Slots[(int) slotIndex];
                return slot != null;
            }

            slot = null;
            return false;
        }

        internal void Apply(MaterialProgramCatalog catalog)
        {
            Apply(catalog, committed: true);
        }

        internal void Apply(
            MaterialProgramCatalog catalog,
            bool committed)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            var slots = new Slot[catalog.RuntimeTableLength];
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                slots[slotIndex] = Slot.Create(
                    slotIndex,
                    catalog.SlotNames[slotIndex],
                    catalog.Slots[slotIndex]);
            }

            // Publication is the final field transition. A serialized asset
            // observed while the payload is being replaced must fail closed.
            m_IsCommitted = false;
            m_AssetSchemaVersion = AssetSchemaVersion;
            m_ProgramCatalogVersion = MaterialProgramContract.ProgramCatalogVersion;
            m_ManifestVersion =
                MaterialProgramContract.ProgramCatalogManifestVersion;
            m_RuntimeAbiVersion = MaterialProgramContract.RuntimeAbiVersion;
            m_ManifestHashVersion = catalog.ManifestHash.Version;
            m_ManifestHash = catalog.ManifestHash.Value;
            m_Slots = slots;
            m_StagedCatalog = catalog;
            MaterialProgramArtifactSetHash artifactSetHash =
                MaterialProgramArtifactSetHashBuilder.Compute(catalog);
            m_ArtifactSetHashVersion = artifactSetHash.Version;
            m_ArtifactSetHash = artifactSetHash.Value;
            MaterialProgramCatalogPayloadSeal payloadSeal =
                ComputeCatalogPayloadSeal();
            m_CatalogPayloadSealVersion = payloadSeal.Version;
            m_CatalogPayloadSealHash = payloadSeal.Value;
            if (committed)
                Seal(catalog);
        }

        internal void Seal()
        {
            if (m_StagedCatalog == null)
                throw new InvalidOperationException(
                    "Frozen Material Program Catalog has no in-memory staged catalog to commit.");
            Seal(m_StagedCatalog);
        }

        internal void Seal(MaterialProgramCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (!MatchesPayload(catalog, requireCommitted: false, out string failure))
            {
                throw new InvalidOperationException(
                    $"Frozen Material Program Catalog cannot be committed: {failure}");
            }
            m_IsCommitted = true;
            m_StagedCatalog = null;
        }

        internal void InvalidatePublication()
        {
            // Keep the last-good payload as a recovery source, but make every
            // runtime/importer consumer reject it until a full artifact set is
            // published again.
            m_IsCommitted = false;
            m_StagedCatalog = null;
        }

        internal bool ValidatePublication(out string failure)
        {
            return ValidateArtifactSet(requireCommitted: true, out failure);
        }

        internal bool Matches(
            MaterialProgramCatalog catalog,
            out string failure)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            return MatchesPayload(catalog, requireCommitted: true, out failure);
        }

        private bool MatchesPayload(
            MaterialProgramCatalog catalog,
            bool requireCommitted,
            out string failure)
        {
            if (!ValidateArtifactSet(requireCommitted, out failure))
                return false;

            if (ManifestHash != catalog.ManifestHash)
            {
                failure = "Frozen catalog manifest hash does not match the compiled catalog.";
                return false;
            }
            if (m_Slots == null || m_Slots.Length != catalog.RuntimeTableLength)
            {
                failure = "Frozen catalog slot count does not match the compiled catalog.";
                return false;
            }

            for (int slotIndex = 0; slotIndex < m_Slots.Length; slotIndex++)
            {
                Slot expected = Slot.Create(
                    slotIndex,
                    catalog.SlotNames[slotIndex],
                    catalog.Slots[slotIndex]);
                if (m_Slots[slotIndex] == null
                    || !m_Slots[slotIndex].PayloadEquals(expected))
                {
                    failure = $"Frozen catalog slot {slotIndex} does not match the compiled manifest.";
                    return false;
                }
            }

            failure = string.Empty;
            return true;
        }

        internal VividMaterialProgramData[] CreateRuntimeProgramTable()
        {
            if (!ValidateArtifactSet(
                    requireCommitted: true,
                    out string publicationFailure))
            {
                throw new InvalidOperationException(
                    $"Frozen catalog asset is not a committed artifact set: {publicationFailure}");
            }

            var runtimePrograms = new VividMaterialProgramData[m_Slots.Length];
            for (int slotIndex = 0; slotIndex < m_Slots.Length; slotIndex++)
            {
                Slot slot = m_Slots[slotIndex]
                    ?? throw new InvalidOperationException(
                        $"Frozen catalog slot {slotIndex} is missing.");
                if ((uint) slot.ProgramID != (uint) slotIndex)
                {
                    throw new InvalidOperationException(
                        $"Frozen catalog slot {slotIndex} carries a different ProgramID.");
                }
                if (!slot.ValidateRuntimeDescriptor(out string descriptorFailure))
                {
                    throw new InvalidOperationException(
                        $"Frozen catalog slot {slotIndex} is invalid: {descriptorFailure}");
                }
                if (!slot.IsReserved)
                    runtimePrograms[slotIndex] = slot.RuntimeData;
            }
            return runtimePrograms;
        }

        internal bool ExtendsBuiltinCatalog(
            MaterialProgramCatalog builtinCatalog,
            out string failure)
        {
            if (builtinCatalog == null)
                throw new ArgumentNullException(nameof(builtinCatalog));
            if (!ValidateArtifactSet(requireCommitted: true, out failure))
                return false;
            if (m_Slots == null
                || m_Slots.Length < builtinCatalog.RuntimeTableLength)
            {
                failure = "Frozen catalog does not contain the complete builtin prefix.";
                return false;
            }

            var stableNames = new HashSet<string>(StringComparer.Ordinal);
            for (int slotIndex = 0; slotIndex < m_Slots.Length; slotIndex++)
            {
                Slot slot = m_Slots[slotIndex];
                if (slot == null || (uint) slot.ProgramID != (uint) slotIndex)
                {
                    failure = $"Frozen catalog slot {slotIndex} is missing or carries a different ProgramID.";
                    return false;
                }
                if (string.IsNullOrEmpty(slot.StableName)
                    || !stableNames.Add(slot.StableName))
                {
                    failure = $"Frozen catalog slot {slotIndex} has an invalid stable name.";
                    return false;
                }
                if (slotIndex < builtinCatalog.RuntimeTableLength)
                {
                    Slot expected = Slot.Create(
                        slotIndex,
                        builtinCatalog.SlotNames[slotIndex],
                        builtinCatalog.Slots[slotIndex]);
                    if (!slot.PayloadEquals(expected))
                    {
                        failure = $"Frozen catalog builtin slot {slotIndex} does not match the compiler contract.";
                        return false;
                    }
                }
                if (!slot.ValidateRuntimeDescriptor(out failure))
                    return false;
                if (!slot.MatchesRuntimeTemplate(builtinCatalog.Templates))
                {
                    failure = $"Frozen catalog slot {slotIndex} does not match a supported runtime template.";
                    return false;
                }
            }

            failure = string.Empty;
            return true;
        }

        private bool ValidateArtifactSet(
            bool requireCommitted,
            out string failure)
        {
            if (requireCommitted && !m_IsCommitted)
            {
                failure = "Frozen catalog artifact set is not committed.";
                return false;
            }
            if (m_AssetSchemaVersion != AssetSchemaVersion
                || m_ProgramCatalogVersion
                    != MaterialProgramContract.ProgramCatalogVersion
                || m_ManifestVersion
                    != MaterialProgramContract.ProgramCatalogManifestVersion
                || m_RuntimeAbiVersion != MaterialProgramContract.RuntimeAbiVersion)
            {
                failure = "Frozen catalog asset versions do not match the compiler contract.";
                return false;
            }
            if (m_ManifestHashVersion
                    != MaterialProgramContract.ProgramCatalogManifestVersion
                || m_ManifestHash == 0ul)
            {
                failure = "Frozen catalog manifest identity is invalid.";
                return false;
            }

            MaterialProgramArtifactSetHash expected =
                MaterialProgramArtifactSetHashBuilder.Compute(ManifestHash);
            if (!ArtifactSetHash.IsValid || ArtifactSetHash != expected)
            {
                failure = "Frozen catalog artifact-set seal does not match its manifest and backend contracts.";
                return false;
            }
            MaterialProgramCatalogPayloadSeal expectedPayloadSeal =
                ComputeCatalogPayloadSeal();
            if (!CatalogPayloadSeal.IsValid
                || CatalogPayloadSeal != expectedPayloadSeal)
            {
                failure = "Frozen catalog serialized payload seal does not match its manifest and slot table.";
                return false;
            }
            if (m_Slots == null)
            {
                failure = "Frozen catalog asset has no slot table.";
                return false;
            }

            var stableNames = new HashSet<string>(StringComparer.Ordinal);
            for (int slotIndex = 0; slotIndex < m_Slots.Length; slotIndex++)
            {
                Slot slot = m_Slots[slotIndex];
                if (slot == null || (uint) slot.ProgramID != (uint) slotIndex)
                {
                    failure = $"Frozen catalog slot {slotIndex} is missing or carries a different ProgramID.";
                    return false;
                }
                if (string.IsNullOrEmpty(slot.StableName)
                    || !stableNames.Add(slot.StableName))
                {
                    failure = $"Frozen catalog slot {slotIndex} has an invalid stable name.";
                    return false;
                }
                if (!slot.ValidateRuntimeDescriptor(out failure))
                    return false;
            }

            failure = string.Empty;
            return true;
        }

        private MaterialProgramCatalogPayloadSeal ComputeCatalogPayloadSeal()
        {
            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            MaterialProgramHashUtility.Add(
                ref hash,
                MaterialProgramContract.CatalogPayloadSealVersion);
            MaterialProgramHashUtility.Add(ref hash, m_AssetSchemaVersion);
            MaterialProgramHashUtility.Add(ref hash, m_ProgramCatalogVersion);
            MaterialProgramHashUtility.Add(ref hash, m_ManifestVersion);
            MaterialProgramHashUtility.Add(ref hash, m_RuntimeAbiVersion);
            MaterialProgramHashUtility.Add(ref hash, m_ManifestHashVersion);
            MaterialProgramHashUtility.Add(ref hash, m_ManifestHash);
            MaterialProgramHashUtility.Add(
                ref hash,
                m_ArtifactSetHashVersion);
            MaterialProgramHashUtility.Add(ref hash, m_ArtifactSetHash);
            MaterialProgramHashUtility.Add(ref hash, m_Slots != null);
            if (m_Slots != null)
            {
                MaterialProgramHashUtility.Add(ref hash, m_Slots.Length);
                for (int slotIndex = 0;
                     slotIndex < m_Slots.Length;
                     slotIndex++)
                {
                    Slot slot = m_Slots[slotIndex];
                    MaterialProgramHashUtility.Add(ref hash, slotIndex);
                    MaterialProgramHashUtility.Add(ref hash, slot != null);
                    slot?.AddPayloadSeal(ref hash);
                }
            }

            return new MaterialProgramCatalogPayloadSeal(
                MaterialProgramContract.CatalogPayloadSealVersion,
                hash);
        }

        internal static MaterialProgramCatalogAsset LoadDefault()
        {
            return Resources.Load<MaterialProgramCatalogAsset>(
                DefaultResourceName);
        }
    }
}
