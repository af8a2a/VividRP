using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
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
            private uint m_ParameterStrideInWords;

            [SerializeField]
            private uint m_ResourceCount;

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
                slot.m_ParameterStrideInWords = checked(
                    (uint) genericLayout.ParameterStrideInWords);
                slot.m_ResourceCount = checked((uint) genericLayout.ResourceCount);
                return slot;
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
                    && m_ParameterStrideInWords == other.m_ParameterStrideInWords
                    && m_ResourceCount == other.m_ResourceCount;
            }
        }

        internal const uint AssetSchemaVersion = 2u;
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
        private Slot[] m_Slots = Array.Empty<Slot>();

        internal uint SchemaVersion => m_AssetSchemaVersion;

        internal uint ProgramCatalogVersion => m_ProgramCatalogVersion;

        internal uint ManifestVersion => m_ManifestVersion;

        internal uint RuntimeAbiVersion => m_RuntimeAbiVersion;

        internal MaterialProgramCatalogManifestHash ManifestHash =>
            new MaterialProgramCatalogManifestHash(
                m_ManifestHashVersion,
                m_ManifestHash);

        internal IReadOnlyList<Slot> Slots => m_Slots;

        internal void Apply(MaterialProgramCatalog catalog)
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

            m_AssetSchemaVersion = AssetSchemaVersion;
            m_ProgramCatalogVersion = MaterialProgramContract.ProgramCatalogVersion;
            m_ManifestVersion =
                MaterialProgramContract.ProgramCatalogManifestVersion;
            m_RuntimeAbiVersion = MaterialProgramContract.RuntimeAbiVersion;
            m_ManifestHashVersion = catalog.ManifestHash.Version;
            m_ManifestHash = catalog.ManifestHash.Value;
            m_Slots = slots;
        }

        internal bool Matches(
            MaterialProgramCatalog catalog,
            out string failure)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

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
            if (m_AssetSchemaVersion != AssetSchemaVersion
                || m_RuntimeAbiVersion != MaterialProgramContract.RuntimeAbiVersion)
            {
                throw new InvalidOperationException(
                    "Frozen catalog asset is incompatible with the runtime ABI.");
            }
            if (m_Slots == null)
            {
                throw new InvalidOperationException(
                    "Frozen catalog asset has no slot table.");
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
                if (!slot.IsReserved)
                    runtimePrograms[slotIndex] = slot.RuntimeData;
            }
            return runtimePrograms;
        }

        internal static MaterialProgramCatalogAsset LoadDefault()
        {
            return Resources.Load<MaterialProgramCatalogAsset>(
                DefaultResourceName);
        }
    }
}
