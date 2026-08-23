using System;
using Unity.Scripting.LifecycleManagement;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class CompiledTransportProgram
    {
        [NoAutoStaticsCleanup]
        private static readonly CompiledTransportProgram s_None =
            new CompiledTransportProgram(
                VividMaterialTransportProgramID.None,
                MaterialValueRequirements.CreateEmpty());

        private CompiledTransportProgram(
            VividMaterialTransportProgramID programID,
            MaterialValueRequirements requirements)
        {
            ProgramID = programID;
            Requirements = requirements
                ?? throw new ArgumentNullException(nameof(requirements));
        }

        internal static CompiledTransportProgram None => s_None;

        internal VividMaterialTransportProgramID ProgramID { get; }

        internal MaterialValueRequirements Requirements { get; }
    }

    internal sealed class MaterialProgramLoweringResult
    {
        internal MaterialProgramLoweringResult(
            CompiledCoverageProgram coverageProgram,
            CompiledSurfaceProgram surfaceProgram,
            CompiledTransportProgram transportProgram,
            MaterialValueRequirements requirements,
            MaterialGenericLayout genericLayout,
            in MaterialProgramSelectionKey selectionKey,
            MaterialProgramCatalogEntry catalogEntry,
            CompiledMaterialLayout materialLayout,
            in VividMaterialProgramData runtimeData)
        {
            CoverageProgram = coverageProgram
                ?? throw new ArgumentNullException(nameof(coverageProgram));
            SurfaceProgram = surfaceProgram
                ?? throw new ArgumentNullException(nameof(surfaceProgram));
            TransportProgram = transportProgram
                ?? throw new ArgumentNullException(nameof(transportProgram));
            Requirements = requirements
                ?? throw new ArgumentNullException(nameof(requirements));
            GenericLayout = genericLayout
                ?? throw new ArgumentNullException(nameof(genericLayout));
            CatalogEntry = catalogEntry
                ?? throw new ArgumentNullException(nameof(catalogEntry));
            MaterialLayout = materialLayout
                ?? throw new ArgumentNullException(nameof(materialLayout));
            if (catalogEntry.SelectionKey != selectionKey)
            {
                throw new ArgumentException(
                    "The selected material program catalog entry does not match the lowering key.",
                    nameof(catalogEntry));
            }

            SelectionKey = selectionKey;
            RuntimeData = runtimeData;
        }

        internal CompiledCoverageProgram CoverageProgram { get; }

        internal CompiledSurfaceProgram SurfaceProgram { get; }

        internal CompiledTransportProgram TransportProgram { get; }

        internal MaterialValueRequirements Requirements { get; }

        internal MaterialGenericLayout GenericLayout { get; }

        internal MaterialProgramSelectionKey SelectionKey { get; }

        internal MaterialProgramCatalogEntry CatalogEntry { get; }

        internal CompiledMaterialLayout MaterialLayout { get; }

        internal VividMaterialProgramData RuntimeData { get; }

        internal VividMaterialProgramID ProgramID => CatalogEntry.ProgramID;
    }

    internal static class MaterialProgramLowerer
    {
        internal static MaterialProgramLoweringResult Lower(
            MaterialIRModule module,
            uint programVersion,
            MaterialProgramCatalogDefinition catalogDefinition)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));
            if (catalogDefinition == null)
                throw new ArgumentNullException(nameof(catalogDefinition));

            CompiledCoverageProgram coverageProgram =
                CoverageProgramLowerer.Compile(module);
            CompiledSurfaceProgram surfaceProgram =
                SurfaceProgramMatcher.Compile(module);
            CompiledTransportProgram transportProgram =
                CompiledTransportProgram.None;
            MaterialValueRequirements requirements = MaterialValueRequirements.Merge(
                coverageProgram.Requirements,
                surfaceProgram.Requirements,
                transportProgram.Requirements);
            var genericLayout = new MaterialGenericLayout(requirements);

            var selectionKey = new MaterialProgramSelectionKey(
                MaterialProgramBackendKind.NativeTemplate,
                MaterialProgramContract.NativeTemplateBackendVersion,
                coverageProgram.ProgramID,
                surfaceProgram.ProgramID,
                transportProgram.ProgramID,
                GetTopologySpecialization(module),
                VividMaterialExecutionClass.VisibilityDeferred);
            MaterialProgramCatalogEntry catalogEntry = catalogDefinition.Resolve(
                selectionKey,
                requirements);
            if (catalogEntry.RuntimeAbiVersion != programVersion)
            {
                throw new NotSupportedException(
                    $"Material program '{catalogEntry.ProgramID}' targets runtime ABI "
                    + $"{catalogEntry.RuntimeAbiVersion}, not requested ABI {programVersion}.");
            }

            CompiledMaterialLayout materialLayout = MaterialLayoutLowerer.Compile(
                requirements,
                genericLayout,
                catalogEntry.LayoutSchema);
            VividMaterialProgramCapabilities requiredCapabilities =
                VividMaterialProgramCapabilities.LegacyGBufferExport;
            if ((module.MaterialFeatures & MaterialFeatureMask.AlphaClip) != 0)
                requiredCapabilities |= VividMaterialProgramCapabilities.AlphaClip;
            if ((module.ShadingModels & MaterialShadingModelMask.Unlit) != 0)
                requiredCapabilities |= VividMaterialProgramCapabilities.Unlit;
            if ((requiredCapabilities & ~catalogEntry.Capabilities) != 0)
            {
                throw new NotSupportedException(
                    $"Material program '{catalogEntry.ProgramID}' does not provide all required capabilities.");
            }

            var runtimeData = new VividMaterialProgramData
            {
                Version = programVersion,
                CoverageProgramID = coverageProgram.ProgramID,
                SurfaceProgramID = surfaceProgram.ProgramID,
                TransportProgramID = transportProgram.ProgramID,
                ParameterLayoutID = materialLayout.ParameterLayout.LayoutID,
                ResourceLayoutID = materialLayout.ResourceLayout.LayoutID,
                CapabilityFlags = catalogEntry.Capabilities,
                ExecutionClass = selectionKey.ExecutionClass,
            };
            return new MaterialProgramLoweringResult(
                coverageProgram,
                surfaceProgram,
                transportProgram,
                requirements,
                genericLayout,
                selectionKey,
                catalogEntry,
                materialLayout,
                runtimeData);
        }

        private static MaterialProgramTopologySpecialization GetTopologySpecialization(
            MaterialIRModule module)
        {
            ClosureExpressionNode root =
                module.ClosureGraph.GetNode(module.SurfaceClosure);
            switch (root.Opcode)
            {
                case ClosureExpressionOpcode.Slab:
                    return MaterialProgramTopologySpecialization.SingleSlab;
                case ClosureExpressionOpcode.HorizontalMix:
                    return MaterialProgramTopologySpecialization.HorizontalMix;
                case ClosureExpressionOpcode.VerticalLayer:
                    return MaterialProgramTopologySpecialization.VerticalLayer;
                default:
                    throw new NotSupportedException(
                        $"Closure topology '{root.Opcode}' cannot select a material program.");
            }
        }
    }

    internal static class MaterialProgramBuiltinCatalog
    {
        [NoAutoStaticsCleanup]
        private static readonly MaterialProgramCatalogDefinition s_Definition =
            CreateDefinition();

        internal static MaterialProgramCatalogDefinition Definition => s_Definition;

        private static MaterialProgramCatalogDefinition CreateDefinition()
        {
            MaterialNativeTemplateLayoutSchema legacyLayout =
                MaterialLayoutLowerer.CreateLegacyLayoutSchema();
            MaterialNativeTemplateLayoutSchema dualSlabLayout =
                MaterialLayoutLowerer.CreateDualSlabLayoutSchema();
            return new MaterialProgramCatalogDefinition(
                CreateEntry(
                    VividMaterialProgramID.StandardSingleSlab,
                    VividMaterialSurfaceProgramID.StandardSingleSlab,
                    MaterialProgramTopologySpecialization.SingleSlab,
                    legacyLayout),
                CreateEntry(
                    VividMaterialProgramID.DualSlabHorizontalMix,
                    VividMaterialSurfaceProgramID.DualSlab,
                    MaterialProgramTopologySpecialization.HorizontalMix,
                    dualSlabLayout),
                CreateEntry(
                    VividMaterialProgramID.DualSlabVerticalLayer,
                    VividMaterialSurfaceProgramID.DualSlab,
                    MaterialProgramTopologySpecialization.VerticalLayer,
                    dualSlabLayout));
        }

        private static MaterialProgramCatalogEntry CreateEntry(
            VividMaterialProgramID programID,
            VividMaterialSurfaceProgramID surfaceProgramID,
            MaterialProgramTopologySpecialization topology,
            MaterialNativeTemplateLayoutSchema layoutSchema)
        {
            var selectionKey = new MaterialProgramSelectionKey(
                MaterialProgramBackendKind.NativeTemplate,
                MaterialProgramContract.NativeTemplateBackendVersion,
                VividMaterialCoverageProgramID.BaseColorAlpha,
                surfaceProgramID,
                VividMaterialTransportProgramID.None,
                topology,
                VividMaterialExecutionClass.VisibilityDeferred);
            return new MaterialProgramCatalogEntry(
                programID,
                selectionKey,
                layoutSchema,
                VividMaterialProgramCapabilities.LegacyGBufferExport
                | VividMaterialProgramCapabilities.AlphaClip
                | VividMaterialProgramCapabilities.Unlit,
                MaterialProgramContract.RuntimeAbiVersion);
        }
    }
}
