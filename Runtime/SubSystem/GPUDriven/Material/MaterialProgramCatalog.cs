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

    internal sealed class MaterialProgramCatalogEntry
    {
        internal MaterialProgramCatalogEntry(
            VividMaterialProgramID programID,
            in MaterialProgramSelectionKey selectionKey,
            MaterialNativeTemplateLayoutSchema layoutSchema,
            VividMaterialProgramCapabilities capabilities,
            uint runtimeAbiVersion)
        {
            if (programID == VividMaterialProgramID.Invalid)
                throw new ArgumentOutOfRangeException(nameof(programID));
            if (ReferenceEquals(layoutSchema, null))
                throw new ArgumentNullException(nameof(layoutSchema));

            ProgramID = programID;
            SelectionKey = selectionKey;
            LayoutSchema = layoutSchema;
            Capabilities = capabilities;
            RuntimeAbiVersion = runtimeAbiVersion;
        }

        internal VividMaterialProgramID ProgramID { get; }

        internal MaterialProgramSelectionKey SelectionKey { get; }

        internal MaterialNativeTemplateLayoutSchema LayoutSchema { get; }

        internal VividMaterialProgramCapabilities Capabilities { get; }

        internal uint RuntimeAbiVersion { get; }
    }

    internal sealed class MaterialProgramCatalogDefinition
    {
        private readonly IReadOnlyList<MaterialProgramCatalogEntry> m_Entries;
        private readonly Dictionary<VividMaterialProgramID, MaterialProgramCatalogEntry>
            m_EntriesByID;
        private readonly Dictionary<MaterialProgramSelectionKey, MaterialProgramCatalogEntry>
            m_EntriesBySelectionKey;

        internal MaterialProgramCatalogDefinition(
            params MaterialProgramCatalogEntry[] entries)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            var entryCopy = (MaterialProgramCatalogEntry[]) entries.Clone();
            m_EntriesByID = new Dictionary<
                VividMaterialProgramID,
                MaterialProgramCatalogEntry>(entryCopy.Length);
            m_EntriesBySelectionKey = new Dictionary<
                MaterialProgramSelectionKey,
                MaterialProgramCatalogEntry>(entryCopy.Length);
            for (int entryIndex = 0; entryIndex < entryCopy.Length; entryIndex++)
            {
                MaterialProgramCatalogEntry entry = entryCopy[entryIndex]
                    ?? throw new ArgumentException(
                        "Material program catalog entries cannot contain null.",
                        nameof(entries));
                if (entry.ProgramID == VividMaterialProgramID.Invalid)
                {
                    throw new ArgumentException(
                        "Material program catalog entries cannot use the invalid program ID.",
                        nameof(entries));
                }
                if (!m_EntriesByID.TryAdd(entry.ProgramID, entry))
                {
                    throw new ArgumentException(
                        $"Material program catalog ID '{entry.ProgramID}' is declared more than once.",
                        nameof(entries));
                }
                if (!m_EntriesBySelectionKey.TryAdd(entry.SelectionKey, entry))
                {
                    throw new ArgumentException(
                        $"Material program selection key for catalog ID '{entry.ProgramID}' is declared more than once.",
                        nameof(entries));
                }
            }

            Array.Sort(
                entryCopy,
                (left, right) => ((uint) left.ProgramID).CompareTo((uint) right.ProgramID));
            m_Entries = Array.AsReadOnly(entryCopy);
        }

        internal IReadOnlyList<MaterialProgramCatalogEntry> Entries => m_Entries;

        internal int Count => m_Entries.Count;

        internal MaterialProgramCatalogEntry GetEntry(VividMaterialProgramID programID)
        {
            if (!m_EntriesByID.TryGetValue(programID, out MaterialProgramCatalogEntry entry))
                throw new ArgumentOutOfRangeException(nameof(programID), programID, null);
            return entry;
        }

        internal bool TryGetEntry(
            VividMaterialProgramID programID,
            out MaterialProgramCatalogEntry entry)
        {
            return m_EntriesByID.TryGetValue(programID, out entry);
        }

        internal MaterialProgramCatalogEntry Resolve(
            in MaterialProgramSelectionKey selectionKey,
            MaterialValueRequirements requirements)
        {
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));
            if (!m_EntriesBySelectionKey.TryGetValue(
                    selectionKey,
                    out MaterialProgramCatalogEntry entry))
            {
                throw new NotSupportedException(
                    "No material program catalog entry matches the lowering selection key.");
            }
            if (!entry.LayoutSchema.Matches(requirements))
            {
                throw new NotSupportedException(
                    $"Material requirements do not match catalog layout schema for program '{entry.ProgramID}'.");
            }
            return entry;
        }
    }

    internal sealed class MaterialProgramCatalog
    {
        private readonly IReadOnlyList<CompiledMaterialProgram> m_Programs;
        private readonly CompiledMaterialProgram[] m_ProgramsByID;
        private readonly bool[] m_HasProgramByID;

        internal MaterialProgramCatalog(
            MaterialProgramCatalogDefinition definition,
            CompiledMaterialProgram[] programs)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (programs == null)
                throw new ArgumentNullException(nameof(programs));
            if (programs.Length != Definition.Count)
            {
                throw new ArgumentException(
                    "A frozen material program catalog requires exactly one compiled program per definition entry.",
                    nameof(programs));
            }

            uint maxProgramID = 0u;
            for (int entryIndex = 0; entryIndex < Definition.Entries.Count; entryIndex++)
            {
                uint programID = (uint) Definition.Entries[entryIndex].ProgramID;
                if (programID > int.MaxValue - 1u)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(definition),
                        "Material program catalog IDs must fit a managed runtime table index.");
                }
                maxProgramID = Math.Max(maxProgramID, programID);
            }

            int tableLength = Definition.Count == 0 ? 0 : (int) maxProgramID + 1;
            m_ProgramsByID = new CompiledMaterialProgram[tableLength];
            m_HasProgramByID = new bool[tableLength];
            var programCopy = (CompiledMaterialProgram[]) programs.Clone();
            for (int programIndex = 0; programIndex < programCopy.Length; programIndex++)
            {
                CompiledMaterialProgram program = programCopy[programIndex]
                    ?? throw new ArgumentException(
                        "Material program catalogs cannot contain null compiled programs.",
                        nameof(programs));
                MaterialProgramLoweringResult lowering = program.Lowering;
                if (ReferenceEquals(lowering, null))
                {
                    throw new ArgumentException(
                        "Cataloged material programs require a lowering result.",
                        nameof(programs));
                }
                MaterialProgramCatalogEntry selectedEntry = lowering.CatalogEntry
                    ?? throw new ArgumentException(
                        "Cataloged material programs require a selected catalog entry.",
                        nameof(programs));
                MaterialProgramCatalogEntry resolvedEntry = Definition.Resolve(
                    lowering.SelectionKey,
                    program.MaterialLayout.Requirements);
                if (!ReferenceEquals(selectedEntry, resolvedEntry))
                {
                    throw new ArgumentException(
                        "A material lowering result selected an entry outside the supplied catalog definition.",
                        nameof(programs));
                }

                ValidateRuntimeContract(program, resolvedEntry);
                int catalogIndex = checked((int) (uint) resolvedEntry.ProgramID);
                if (m_HasProgramByID[catalogIndex])
                {
                    throw new ArgumentException(
                        $"Material program catalog ID '{resolvedEntry.ProgramID}' has multiple compiled programs.",
                        nameof(programs));
                }

                for (int previousIndex = 0; previousIndex < programIndex; previousIndex++)
                {
                    CompiledMaterialProgram previous = programCopy[previousIndex];
                    if (!AreExactlyEquivalent(previous, program))
                        continue;

                    VividMaterialProgramID previousID =
                        previous.Lowering.CatalogEntry.ProgramID;
                    if (previousID != resolvedEntry.ProgramID)
                    {
                        throw new ArgumentException(
                            $"Equivalent compiled material programs cannot use different catalog IDs "
                            + $"('{previousID}' and '{resolvedEntry.ProgramID}').",
                            nameof(programs));
                    }
                }

                m_ProgramsByID[catalogIndex] = program;
                m_HasProgramByID[catalogIndex] = true;
            }

            for (int entryIndex = 0; entryIndex < Definition.Entries.Count; entryIndex++)
            {
                int catalogIndex = checked(
                    (int) (uint) Definition.Entries[entryIndex].ProgramID);
                if (!m_HasProgramByID[catalogIndex])
                {
                    throw new ArgumentException(
                        $"Material program catalog entry '{Definition.Entries[entryIndex].ProgramID}' has no compiled program.",
                        nameof(programs));
                }
            }

            Array.Sort(
                programCopy,
                (left, right) => ((uint) left.Lowering.CatalogEntry.ProgramID)
                    .CompareTo((uint) right.Lowering.CatalogEntry.ProgramID));
            m_Programs = Array.AsReadOnly(programCopy);
        }

        internal MaterialProgramCatalogDefinition Definition { get; }

        internal IReadOnlyList<CompiledMaterialProgram> Programs => m_Programs;

        internal CompiledMaterialProgram GetMaterialProgram(
            VividMaterialProgramID programID)
        {
            uint programIndex = (uint) programID;
            if (programIndex >= (uint) m_ProgramsByID.Length
                || !m_HasProgramByID[programIndex])
            {
                throw new ArgumentOutOfRangeException(nameof(programID), programID, null);
            }
            return m_ProgramsByID[programIndex];
        }

        internal VividMaterialProgramData[] CreateRuntimeProgramTable()
        {
            var runtimePrograms = new VividMaterialProgramData[m_ProgramsByID.Length];
            for (int programIndex = 0; programIndex < m_ProgramsByID.Length; programIndex++)
            {
                if (m_HasProgramByID[programIndex])
                    runtimePrograms[programIndex] = m_ProgramsByID[programIndex].RuntimeData;
            }
            return runtimePrograms;
        }

        internal bool TryGetEquivalent(
            CompiledMaterialProgram candidate,
            out CompiledMaterialProgram equivalent)
        {
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));

            for (int programIndex = 0; programIndex < m_Programs.Count; programIndex++)
            {
                CompiledMaterialProgram program = m_Programs[programIndex];
                if (!AreExactlyEquivalent(program, candidate))
                    continue;

                equivalent = program;
                return true;
            }

            equivalent = null;
            return false;
        }

        private static void ValidateRuntimeContract(
            CompiledMaterialProgram program,
            MaterialProgramCatalogEntry entry)
        {
            VividMaterialProgramData runtimeData = program.RuntimeData;
            MaterialProgramSelectionKey key = entry.SelectionKey;
            if (runtimeData.Version != entry.RuntimeAbiVersion
                || runtimeData.CoverageProgramID != key.CoverageProgramID
                || runtimeData.SurfaceProgramID != key.SurfaceProgramID
                || runtimeData.TransportProgramID != key.TransportProgramID
                || runtimeData.ParameterLayoutID
                    != entry.LayoutSchema.ParameterLayout.LayoutID
                || runtimeData.ResourceLayoutID
                    != entry.LayoutSchema.ResourceLayout.LayoutID
                || runtimeData.ExecutionClass != key.ExecutionClass
                || runtimeData.CapabilityFlags != entry.Capabilities
                || !ReferenceEquals(
                    program.MaterialLayout.ParameterLayout,
                    entry.LayoutSchema.ParameterLayout)
                || !ReferenceEquals(
                    program.MaterialLayout.ResourceLayout,
                    entry.LayoutSchema.ResourceLayout)
                || !program.Lowering.GenericLayout.PayloadEquals(
                    entry.LayoutSchema.LiveLayout))
            {
                throw new ArgumentException(
                    $"Compiled material program does not satisfy catalog entry '{entry.ProgramID}'.",
                    nameof(program));
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
            if (left.Lowering.SelectionKey != right.Lowering.SelectionKey)
                return false;
            MaterialProgramCatalogEntry leftEntry = left.Lowering.CatalogEntry;
            MaterialProgramCatalogEntry rightEntry = right.Lowering.CatalogEntry;
            if (leftEntry.RuntimeAbiVersion != rightEntry.RuntimeAbiVersion
                || leftEntry.Capabilities != rightEntry.Capabilities
                || !leftEntry.LayoutSchema.MappingPayloadEquals(
                    rightEntry.LayoutSchema))
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
