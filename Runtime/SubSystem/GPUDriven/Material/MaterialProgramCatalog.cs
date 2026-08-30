using System;
using System.Collections.Generic;

namespace VividRP.Runtime.GPUDriven
{
    internal enum MaterialProgramTopologySpecialization
    {
        SingleSlab = 0,
        HorizontalMix = 1,
        VerticalLayer = 2,
    }

    internal readonly struct MaterialProgramSelectionKey :
        IEquatable<MaterialProgramSelectionKey>
    {
        internal MaterialProgramSelectionKey(
            MaterialProgramBackendKind backendKind,
            uint backendVersion,
            VividMaterialCoverageProgramID coverageProgramID,
            VividMaterialSurfaceProgramID surfaceProgramID,
            VividMaterialTransportProgramID transportProgramID,
            MaterialProgramTopologySpecialization topology,
            VividMaterialExecutionClass executionClass)
        {
            BackendKind = backendKind;
            BackendVersion = backendVersion;
            CoverageProgramID = coverageProgramID;
            SurfaceProgramID = surfaceProgramID;
            TransportProgramID = transportProgramID;
            Topology = topology;
            ExecutionClass = executionClass;
        }

        internal MaterialProgramBackendKind BackendKind { get; }

        internal uint BackendVersion { get; }

        internal VividMaterialCoverageProgramID CoverageProgramID { get; }

        internal VividMaterialSurfaceProgramID SurfaceProgramID { get; }

        internal VividMaterialTransportProgramID TransportProgramID { get; }

        internal MaterialProgramTopologySpecialization Topology { get; }

        internal VividMaterialExecutionClass ExecutionClass { get; }

        public bool Equals(MaterialProgramSelectionKey other)
        {
            return BackendKind == other.BackendKind
                && BackendVersion == other.BackendVersion
                && CoverageProgramID == other.CoverageProgramID
                && SurfaceProgramID == other.SurfaceProgramID
                && TransportProgramID == other.TransportProgramID
                && Topology == other.Topology
                && ExecutionClass == other.ExecutionClass;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialProgramSelectionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int) BackendKind;
                hashCode = (hashCode * 397) ^ (int) BackendVersion;
                hashCode = (hashCode * 397) ^ (int) CoverageProgramID;
                hashCode = (hashCode * 397) ^ (int) SurfaceProgramID;
                hashCode = (hashCode * 397) ^ (int) TransportProgramID;
                hashCode = (hashCode * 397) ^ (int) Topology;
                hashCode = (hashCode * 397) ^ (int) ExecutionClass;
                return hashCode;
            }
        }

        public static bool operator ==(
            MaterialProgramSelectionKey left,
            MaterialProgramSelectionKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            MaterialProgramSelectionKey left,
            MaterialProgramSelectionKey right)
        {
            return !left.Equals(right);
        }
    }

    // Templates describe lowering compatibility only. Runtime ProgramIDs are assigned
    // later, when a closed-world catalog manifest is baked.
    internal sealed class MaterialProgramTemplate
    {
        internal MaterialProgramTemplate(
            in MaterialProgramSelectionKey selectionKey,
            MaterialNativeTemplateLayoutSchema layoutSchema,
            VividMaterialProgramCapabilities capabilities,
            uint runtimeAbiVersion)
        {
            SelectionKey = selectionKey;
            LayoutSchema = layoutSchema
                ?? throw new ArgumentNullException(nameof(layoutSchema));
            Capabilities = capabilities;
            RuntimeAbiVersion = runtimeAbiVersion;
        }

        internal MaterialProgramSelectionKey SelectionKey { get; }

        internal MaterialNativeTemplateLayoutSchema LayoutSchema { get; }

        internal VividMaterialProgramCapabilities Capabilities { get; }

        internal uint RuntimeAbiVersion { get; }
    }

    internal sealed class MaterialProgramTemplateRegistry
    {
        private readonly IReadOnlyList<MaterialProgramTemplate> m_Templates;
        private readonly Dictionary<
            MaterialProgramSelectionKey,
            List<MaterialProgramTemplate>> m_TemplatesBySelectionKey;

        internal MaterialProgramTemplateRegistry(
            params MaterialProgramTemplate[] templates)
        {
            if (templates == null)
                throw new ArgumentNullException(nameof(templates));

            var templateCopy = (MaterialProgramTemplate[]) templates.Clone();
            m_TemplatesBySelectionKey = new Dictionary<
                MaterialProgramSelectionKey,
                List<MaterialProgramTemplate>>(templateCopy.Length);
            var uniqueTemplates = new HashSet<MaterialProgramTemplate>();
            for (int templateIndex = 0;
                 templateIndex < templateCopy.Length;
                 templateIndex++)
            {
                MaterialProgramTemplate template = templateCopy[templateIndex]
                    ?? throw new ArgumentException(
                        "Material program templates cannot contain null.",
                        nameof(templates));
                if (!uniqueTemplates.Add(template))
                {
                    throw new ArgumentException(
                        "The same material program template instance is registered more than once.",
                        nameof(templates));
                }

                if (!m_TemplatesBySelectionKey.TryGetValue(
                        template.SelectionKey,
                        out List<MaterialProgramTemplate> matchingTemplates))
                {
                    matchingTemplates = new List<MaterialProgramTemplate>();
                    m_TemplatesBySelectionKey.Add(
                        template.SelectionKey,
                        matchingTemplates);
                }
                matchingTemplates.Add(template);
            }

            m_Templates = Array.AsReadOnly(templateCopy);
        }

        internal IReadOnlyList<MaterialProgramTemplate> Templates => m_Templates;

        internal int Count => m_Templates.Count;

        internal bool Contains(MaterialProgramTemplate template)
        {
            if (template == null)
                return false;

            for (int templateIndex = 0;
                 templateIndex < m_Templates.Count;
                 templateIndex++)
            {
                if (ReferenceEquals(m_Templates[templateIndex], template))
                    return true;
            }
            return false;
        }

        internal MaterialProgramTemplate Resolve(
            in MaterialProgramSelectionKey selectionKey,
            MaterialValueRequirements requirements)
        {
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));
            if (!m_TemplatesBySelectionKey.TryGetValue(
                    selectionKey,
                    out List<MaterialProgramTemplate> templates))
            {
                throw new NotSupportedException(
                    "No material program template matches the lowering selection key.");
            }

            MaterialProgramTemplate match = null;
            for (int templateIndex = 0;
                 templateIndex < templates.Count;
                 templateIndex++)
            {
                MaterialProgramTemplate candidate = templates[templateIndex];
                if (!candidate.LayoutSchema.Matches(requirements))
                    continue;
                if (match != null)
                {
                    throw new InvalidOperationException(
                        "More than one material program template matches the lowering selection key and live layout.");
                }
                match = candidate;
            }

            if (match == null)
            {
                throw new NotSupportedException(
                    "Material requirements do not match any layout schema for the lowering selection key.");
            }
            return match;
        }
    }

    internal readonly struct MaterialProgramCatalogBakeSlot
    {
        private MaterialProgramCatalogBakeSlot(
            string stableName,
            CompiledMaterialProgram program,
            bool isReserved)
        {
            if (string.IsNullOrEmpty(stableName))
                throw new ArgumentException(
                    "A frozen catalog slot requires a stable name.",
                    nameof(stableName));
            if (isReserved == (program != null))
            {
                throw new ArgumentException(
                    "A catalog slot must contain either one compiled program or one reservation.",
                    nameof(program));
            }

            StableName = stableName;
            Program = program;
            IsReserved = isReserved;
        }

        internal string StableName { get; }

        internal CompiledMaterialProgram Program { get; }

        internal bool IsReserved { get; }

        internal static MaterialProgramCatalogBakeSlot ForProgram(
            string stableName,
            CompiledMaterialProgram program)
        {
            if (program == null)
                throw new ArgumentNullException(nameof(program));
            return new MaterialProgramCatalogBakeSlot(
                stableName,
                program,
                false);
        }

        internal static MaterialProgramCatalogBakeSlot Reserved(string stableName)
        {
            return new MaterialProgramCatalogBakeSlot(stableName, null, true);
        }
    }

    internal sealed class MaterialProgramCatalog
    {
        // This is the cataloged runtime handle and the only aggregate that owns a
        // ProgramID. The active-bake guard makes frozen bake the sole allocator.
        internal sealed class ManifestEntry
        {
            internal ManifestEntry(
                MaterialProgramCatalog catalog,
                VividMaterialProgramID programID,
                string stableName,
                CompiledMaterialProgram program)
            {
                if (programID == VividMaterialProgramID.Invalid
                    || (uint) programID > int.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(programID));
                }
                if (catalog == null
                    || !catalog.m_IsBaking
                    || catalog.m_ActiveBakeSlot != (int) (uint) programID)
                {
                    throw new InvalidOperationException(
                        "Manifest entries can only be created by an active frozen catalog bake.");
                }
                if (string.IsNullOrEmpty(stableName))
                {
                    throw new ArgumentException(
                        "A manifest entry requires a stable slot name.",
                        nameof(stableName));
                }

                ProgramID = programID;
                StableName = stableName;
                Program = program ?? throw new ArgumentNullException(nameof(program));
            }

            internal VividMaterialProgramID ProgramID { get; }

            internal string StableName { get; }

            internal CompiledMaterialProgram Program { get; }

            internal MaterialProgramLayoutFingerprint LayoutFingerprint =>
                Program.Lowering.LayoutFingerprint;

            internal VividMaterialProgramData RuntimeData => Program.RuntimeData;
        }

        private readonly IReadOnlyList<ManifestEntry> m_Entries;
        private readonly IReadOnlyList<ManifestEntry> m_Slots;
        private readonly IReadOnlyList<string> m_SlotNames;
        private readonly ManifestEntry[] m_EntriesByID;
        private bool m_IsBaking;
        private int m_ActiveBakeSlot = -1;

        private MaterialProgramCatalog(
            MaterialProgramTemplateRegistry templates,
            MaterialProgramCatalogBakeSlot[] slots)
        {
            Templates = templates ?? throw new ArgumentNullException(nameof(templates));
            if (slots == null)
                throw new ArgumentNullException(nameof(slots));

            var slotCopy = (MaterialProgramCatalogBakeSlot[]) slots.Clone();
            m_EntriesByID = new ManifestEntry[slotCopy.Length];
            var slotNames = new string[slotCopy.Length];
            var entries = new List<ManifestEntry>(slotCopy.Length);
            var stableNames = new HashSet<string>(StringComparer.Ordinal);
            var parameterLayouts = new Dictionary<
                VividMaterialParameterLayoutID,
                CompiledParameterLayout>();
            var resourceLayouts = new Dictionary<
                VividMaterialResourceLayoutID,
                CompiledResourceLayout>();

            m_IsBaking = true;
            for (int slotIndex = 0; slotIndex < slotCopy.Length; slotIndex++)
            {
                MaterialProgramCatalogBakeSlot slot = slotCopy[slotIndex];
                if (string.IsNullOrEmpty(slot.StableName))
                {
                    throw new ArgumentException(
                        $"Frozen catalog slot {slotIndex} is uninitialized.",
                        nameof(slots));
                }
                if (!stableNames.Add(slot.StableName))
                {
                    throw new ArgumentException(
                        $"Frozen catalog slot name '{slot.StableName}' is declared more than once.",
                        nameof(slots));
                }
                slotNames[slotIndex] = slot.StableName;
                if (slot.IsReserved)
                    continue;
                if (slot.Program == null)
                {
                    throw new ArgumentException(
                        $"Frozen catalog slot {slotIndex} has neither a program nor a reservation.",
                        nameof(slots));
                }

                var programID = (VividMaterialProgramID) (uint) slotIndex;
                if (programID == VividMaterialProgramID.Invalid)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(slots),
                        "A frozen catalog slot cannot use the invalid program ID.");
                }
                ValidateCandidate(slot.Program, Templates, nameof(slots));
                ValidateLayoutIDContracts(
                    slot.Program,
                    parameterLayouts,
                    resourceLayouts,
                    nameof(slots));

                for (int entryIndex = 0;
                     entryIndex < entries.Count;
                     entryIndex++)
                {
                    if (AreExactlyEquivalent(entries[entryIndex].Program, slot.Program))
                    {
                        throw new ArgumentException(
                            "Exactly equivalent compiled material payloads cannot occupy multiple frozen catalog slots.",
                            nameof(slots));
                    }
                }

                m_ActiveBakeSlot = slotIndex;
                var entry = new ManifestEntry(
                    this,
                    programID,
                    slot.StableName,
                    slot.Program);
                m_EntriesByID[slotIndex] = entry;
                entries.Add(entry);
            }
            m_ActiveBakeSlot = -1;
            m_IsBaking = false;

            m_Entries = entries.AsReadOnly();
            m_Slots = Array.AsReadOnly(m_EntriesByID);
            m_SlotNames = Array.AsReadOnly(slotNames);
            ManifestHash = MaterialProgramCatalogManifestHashBuilder.Compute(
                m_Slots,
                m_SlotNames);
        }

        internal MaterialProgramTemplateRegistry Templates { get; }

        internal IReadOnlyList<ManifestEntry> Entries => m_Entries;

        // Reserved ABI holes are represented by null entries and remain part of the hash.
        internal IReadOnlyList<ManifestEntry> Slots => m_Slots;

        internal IReadOnlyList<string> SlotNames => m_SlotNames;

        internal int Count => m_Entries.Count;

        internal int RuntimeTableLength => m_EntriesByID.Length;

        internal MaterialProgramCatalogManifestHash ManifestHash { get; }

        internal static MaterialProgramCatalog Bake(
            MaterialProgramTemplateRegistry templates,
            params MaterialProgramCatalogBakeSlot[] slots)
        {
            return new MaterialProgramCatalog(templates, slots);
        }

        internal ManifestEntry GetEntry(
            VividMaterialProgramID programID)
        {
            uint programIndex = (uint) programID;
            if (programIndex >= (uint) m_EntriesByID.Length
                || m_EntriesByID[programIndex] == null)
            {
                throw new ArgumentOutOfRangeException(nameof(programID), programID, null);
            }
            return m_EntriesByID[programIndex];
        }

        internal CompiledMaterialProgram GetMaterialProgram(
            VividMaterialProgramID programID)
        {
            return GetEntry(programID).Program;
        }

        internal VividMaterialProgramData[] CreateRuntimeProgramTable()
        {
            var runtimePrograms = new VividMaterialProgramData[m_EntriesByID.Length];
            for (int programIndex = 0;
                 programIndex < m_EntriesByID.Length;
                 programIndex++)
            {
                ManifestEntry entry =
                    m_EntriesByID[programIndex];
                if (entry != null)
                    runtimePrograms[programIndex] = entry.RuntimeData;
            }
            return runtimePrograms;
        }

        internal bool TryGetCatalogedProgram(
            CompiledMaterialProgram candidate,
            out ManifestEntry equivalent)
        {
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));

            for (int entryIndex = 0;
                 entryIndex < m_Entries.Count;
                 entryIndex++)
            {
                ManifestEntry entry = m_Entries[entryIndex];
                if (!AreExactlyEquivalent(entry.Program, candidate))
                    continue;

                equivalent = entry;
                return true;
            }

            equivalent = null;
            return false;
        }

        private static void ValidateCandidate(
            CompiledMaterialProgram program,
            MaterialProgramTemplateRegistry templates,
            string parameterName)
        {
            MaterialProgramLoweringResult lowering = program.Lowering;
            if (lowering == null || lowering.Template == null)
            {
                throw new ArgumentException(
                    "Cataloged material programs require a selected lowering template.",
                    parameterName);
            }
            if (!templates.Contains(lowering.Template))
            {
                throw new ArgumentException(
                    "A cataloged material program selected a template outside the supplied registry.",
                    parameterName);
            }
            if (lowering.Template.SelectionKey != lowering.SelectionKey
                || !lowering.Template.LayoutSchema.Matches(
                    program.MaterialLayout.Requirements))
            {
                throw new ArgumentException(
                    "A cataloged material program no longer satisfies its selected template.",
                    parameterName);
            }

            MaterialProgramLayoutFingerprint expectedFingerprint =
                MaterialProgramLayoutFingerprintBuilder.Compute(
                    lowering.GenericLayout,
                    lowering.Template.LayoutSchema);
            if (lowering.LayoutFingerprint != expectedFingerprint)
            {
                throw new ArgumentException(
                    "A cataloged material program carries a stale layout fingerprint.",
                    parameterName);
            }
            MaterialDeferredExportContract expectedDeferredExport =
                MaterialDeferredExportContractLowerer.Compile(
                    program.Module,
                    lowering.SelectionKey.Topology);
            if (!lowering.DeferredExportContract.PayloadEquals(
                    expectedDeferredExport)
                || lowering.DeferredExportContract.Fingerprint
                    != expectedDeferredExport.Fingerprint)
            {
                throw new ArgumentException(
                    "A cataloged material program carries a stale Deferred Export contract.",
                    parameterName);
            }
            ValidateRuntimeContract(program, lowering.Template, parameterName);
        }

        private static void ValidateRuntimeContract(
            CompiledMaterialProgram program,
            MaterialProgramTemplate template,
            string parameterName)
        {
            VividMaterialProgramData runtimeData = program.RuntimeData;
            MaterialProgramSelectionKey key = template.SelectionKey;
            if (runtimeData.Version != template.RuntimeAbiVersion
                || runtimeData.CoverageProgramID != key.CoverageProgramID
                || runtimeData.SurfaceProgramID != key.SurfaceProgramID
                || runtimeData.TransportProgramID != key.TransportProgramID
                || runtimeData.ParameterLayoutID
                    != VividMaterialParameterLayoutID.GenericParameterLanes
                || runtimeData.ResourceLayoutID
                    != VividMaterialResourceLayoutID.GenericResourceRecords
                || runtimeData.ExecutionClass != key.ExecutionClass
                || runtimeData.CapabilityFlags != template.Capabilities
                || !ReferenceEquals(
                    program.MaterialLayout.ParameterLayout,
                    template.LayoutSchema.ParameterLayout)
                || !ReferenceEquals(
                    program.MaterialLayout.ResourceLayout,
                    template.LayoutSchema.ResourceLayout)
                || !program.Lowering.GenericLayout.PayloadEquals(
                    template.LayoutSchema.LiveLayout))
            {
                throw new ArgumentException(
                    "Compiled material program does not satisfy its lowering template runtime contract.",
                    parameterName);
            }
        }

        private static void ValidateLayoutIDContracts(
            CompiledMaterialProgram program,
            Dictionary<VividMaterialParameterLayoutID, CompiledParameterLayout>
                parameterLayouts,
            Dictionary<VividMaterialResourceLayoutID, CompiledResourceLayout>
                resourceLayouts,
            string parameterName)
        {
            CompiledParameterLayout parameterLayout =
                program.MaterialLayout.ParameterLayout;
            if (parameterLayouts.TryGetValue(
                    parameterLayout.LayoutID,
                    out CompiledParameterLayout previousParameterLayout))
            {
                if (!ParameterLayoutsEqual(previousParameterLayout, parameterLayout))
                {
                    throw new ArgumentException(
                        $"Parameter layout ID '{parameterLayout.LayoutID}' maps to more than one physical payload.",
                        parameterName);
                }
            }
            else
            {
                parameterLayouts.Add(parameterLayout.LayoutID, parameterLayout);
            }

            CompiledResourceLayout resourceLayout =
                program.MaterialLayout.ResourceLayout;
            if (resourceLayouts.TryGetValue(
                    resourceLayout.LayoutID,
                    out CompiledResourceLayout previousResourceLayout))
            {
                if (!ResourceLayoutsEqual(previousResourceLayout, resourceLayout))
                {
                    throw new ArgumentException(
                        $"Resource layout ID '{resourceLayout.LayoutID}' maps to more than one physical payload.",
                        parameterName);
                }
            }
            else
            {
                resourceLayouts.Add(resourceLayout.LayoutID, resourceLayout);
            }
        }

        private static bool AreExactlyEquivalent(
            CompiledMaterialProgram left,
            CompiledMaterialProgram right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.CompiledHash != right.CompiledHash)
                return false;
            if (!left.Module.CanonicalIR.PayloadEquals(right.Module.CanonicalIR))
                return false;
            if (!left.CoverageHlsl.PayloadEquals(right.CoverageHlsl))
                return false;
            if (!left.SurfaceHlsl.PayloadEquals(right.SurfaceHlsl))
                return false;
            if (!left.DeferredExportContract.PayloadEquals(
                    right.DeferredExportContract)
                || left.DeferredExportContract.Fingerprint
                    != right.DeferredExportContract.Fingerprint)
            {
                return false;
            }
            if (left.Lowering.SelectionKey != right.Lowering.SelectionKey
                || left.Lowering.LayoutFingerprint
                    != right.Lowering.LayoutFingerprint)
            {
                return false;
            }

            MaterialProgramTemplate leftTemplate = left.Lowering.Template;
            MaterialProgramTemplate rightTemplate = right.Lowering.Template;
            if (leftTemplate.RuntimeAbiVersion != rightTemplate.RuntimeAbiVersion
                || leftTemplate.Capabilities != rightTemplate.Capabilities
                || !leftTemplate.LayoutSchema.MappingPayloadEquals(
                    rightTemplate.LayoutSchema))
            {
                return false;
            }
            if (!RuntimeDataEquals(left.RuntimeData, right.RuntimeData))
                return false;
            if (!left.Lowering.GenericLayout.PayloadEquals(right.Lowering.GenericLayout))
                return false;
            return ParameterLayoutsEqual(
                    left.MaterialLayout.ParameterLayout,
                    right.MaterialLayout.ParameterLayout)
                && ResourceLayoutsEqual(
                    left.MaterialLayout.ResourceLayout,
                    right.MaterialLayout.ResourceLayout);
        }

        private static bool RuntimeDataEquals(
            in VividMaterialProgramData left,
            in VividMaterialProgramData right)
        {
            return left.Version == right.Version
                && left.CoverageProgramID == right.CoverageProgramID
                && left.SurfaceProgramID == right.SurfaceProgramID
                && left.TransportProgramID == right.TransportProgramID
                && left.ParameterLayoutID == right.ParameterLayoutID
                && left.ResourceLayoutID == right.ResourceLayoutID
                && left.CapabilityFlags == right.CapabilityFlags
                && left.ExecutionClass == right.ExecutionClass;
        }

        private static bool ParameterLayoutsEqual(
            CompiledParameterLayout left,
            CompiledParameterLayout right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null
                || right == null
                || left.LayoutID != right.LayoutID
                || left.Stride != right.Stride
                || left.Bindings.Count != right.Bindings.Count)
            {
                return false;
            }

            for (int bindingIndex = 0; bindingIndex < left.Bindings.Count; bindingIndex++)
            {
                MaterialParameterLayoutBinding leftBinding = left.Bindings[bindingIndex];
                MaterialParameterLayoutBinding rightBinding = right.Bindings[bindingIndex];
                if (leftBinding.Parameter != rightBinding.Parameter
                    || leftBinding.Type != rightBinding.Type
                    || leftBinding.ByteOffset != rightBinding.ByteOffset)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ResourceLayoutsEqual(
            CompiledResourceLayout left,
            CompiledResourceLayout right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null
                || right == null
                || left.LayoutID != right.LayoutID
                || left.RecordStride != right.RecordStride
                || left.RecordCount != right.RecordCount
                || left.Bindings.Count != right.Bindings.Count)
            {
                return false;
            }

            for (int bindingIndex = 0; bindingIndex < left.Bindings.Count; bindingIndex++)
            {
                MaterialResourceLayoutBinding leftBinding = left.Bindings[bindingIndex];
                MaterialResourceLayoutBinding rightBinding = right.Bindings[bindingIndex];
                if (leftBinding.Resource != rightBinding.Resource
                    || leftBinding.RecordOffset != rightBinding.RecordOffset
                    || leftBinding.ByteOffset != rightBinding.ByteOffset)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
