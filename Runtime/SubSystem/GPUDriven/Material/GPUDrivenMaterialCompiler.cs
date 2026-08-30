using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Scripting.LifecycleManagement;

namespace VividRP.Runtime.GPUDriven
{
    internal readonly struct GPUDrivenCompiledMaterialInstance
    {
        internal GPUDrivenCompiledMaterialInstance(
            MaterialProgramCatalog.ManifestEntry catalogProgram,
            in VividMaterialRuntimeHeader runtimeHeader,
            in VividMaterialData legacyMaterialData)
            : this(
                catalogProgram,
                runtimeHeader,
                legacyMaterialData,
                default,
                Array.Empty<uint4>())
        {
        }

        internal GPUDrivenCompiledMaterialInstance(
            MaterialProgramCatalog.ManifestEntry catalogProgram,
            in VividMaterialRuntimeHeader runtimeHeader,
            in VividMaterialData legacyMaterialData,
            in VividDualSlabMaterialData dualSlabMaterialData,
            uint4[] parameterLanes)
            : this(
                new MaterialProgramRuntimeBinding(catalogProgram),
                runtimeHeader,
                legacyMaterialData,
                dualSlabMaterialData,
                parameterLanes)
        {
        }

        internal GPUDrivenCompiledMaterialInstance(
            MaterialProgramRuntimeBinding programBinding,
            in VividMaterialRuntimeHeader runtimeHeader,
            in VividMaterialData legacyMaterialData,
            in VividDualSlabMaterialData dualSlabMaterialData,
            uint4[] parameterLanes)
        {
            ProgramBinding = programBinding
                ?? throw new ArgumentNullException(nameof(programBinding));
            if (runtimeHeader.ProgramID != programBinding.ProgramID)
            {
                throw new ArgumentException(
                    "The runtime header ProgramID must match its cataloged material program.",
                    nameof(runtimeHeader));
            }
            RuntimeHeader = runtimeHeader;
            LegacyMaterialData = legacyMaterialData;
            DualSlabMaterialData = dualSlabMaterialData;
            ParameterLanes = parameterLanes
                ?? throw new ArgumentNullException(nameof(parameterLanes));
        }

        internal MaterialProgramRuntimeBinding ProgramBinding { get; }

        internal MaterialProgramCatalog.ManifestEntry CatalogProgram =>
            ProgramBinding.CatalogProgram;

        internal CompiledMaterialProgram MaterialProgram =>
            CatalogProgram?.Program
            ?? throw new InvalidOperationException(
                "Frozen runtime-only material programs do not retain compiler IR.");

        internal VividMaterialProgramID ProgramID => ProgramBinding.ProgramID;

        internal VividMaterialRuntimeHeader RuntimeHeader { get; }

        internal VividMaterialData LegacyMaterialData { get; }

        internal VividDualSlabMaterialData DualSlabMaterialData { get; }

        internal IReadOnlyList<uint4> ParameterLanes { get; }
    }

    internal static class GPUDrivenMaterialCompiler
    {
        internal const uint RuntimeAbiVersion = MaterialProgramContract.RuntimeAbiVersion;

        // Compatibility name used by the current HLSL contract and existing callers.
        internal const uint ProgramVersion = RuntimeAbiVersion;

        [NoAutoStaticsCleanup]
        private static readonly MaterialProgramCatalog s_MaterialProgramCatalog =
            CreateProductionProgramCatalog();

        internal static MaterialProgramCatalog ProgramCatalog =>
            s_MaterialProgramCatalog;

        internal static MaterialProgramCatalog.ManifestEntry GetCatalogedMaterialProgram(
            VividMaterialProgramID programID)
        {
            return s_MaterialProgramCatalog.GetEntry(programID);
        }

        internal static CompiledMaterialProgram GetMaterialProgram(
            VividMaterialProgramID programID)
        {
            return s_MaterialProgramCatalog.GetMaterialProgram(programID);
        }

        internal static MaterialProgramRuntimeBinding GetRuntimeProgramBinding(
            VividMaterialProgramID programID)
        {
            MaterialProgramCatalogAsset frozenCatalog =
                MaterialProgramCatalogAsset.LoadDefault();
            if (frozenCatalog == null)
            {
                return new MaterialProgramRuntimeBinding(
                    s_MaterialProgramCatalog.GetEntry(programID));
            }
            return GetRuntimeProgramBinding(programID, frozenCatalog);
        }

        internal static MaterialProgramRuntimeBinding GetRuntimeProgramBinding(
            VividMaterialProgramID programID,
            MaterialProgramCatalogAsset frozenCatalog)
        {
            if (frozenCatalog == null)
                throw new ArgumentNullException(nameof(frozenCatalog));
            if (!frozenCatalog.ExtendsBuiltinCatalog(
                    s_MaterialProgramCatalog,
                    out string failure))
            {
                throw new InvalidOperationException(
                    $"Frozen Material Program Catalog is stale: {failure}");
            }
            if (!frozenCatalog.TryGetSlot(
                    programID,
                    out MaterialProgramCatalogAsset.Slot slot)
                || slot.IsReserved)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(programID),
                    programID,
                    null);
            }
            return new MaterialProgramRuntimeBinding(slot);
        }

        internal static VividMaterialProgramData[] CreateRuntimeProgramTable()
        {
            MaterialProgramCatalogAsset frozenCatalog =
                MaterialProgramCatalogAsset.LoadDefault();
            if (frozenCatalog == null)
                return s_MaterialProgramCatalog.CreateRuntimeProgramTable();
            return CreateRuntimeProgramTable(frozenCatalog);
        }

        internal static VividMaterialProgramData[] CreateRuntimeProgramTable(
            MaterialProgramCatalogAsset frozenCatalog)
        {
            if (frozenCatalog == null)
                throw new ArgumentNullException(nameof(frozenCatalog));
            if (!frozenCatalog.ExtendsBuiltinCatalog(
                    s_MaterialProgramCatalog,
                    out string failure))
            {
                throw new InvalidOperationException(
                    $"Frozen Material Program Catalog is stale: {failure}");
            }
            return frozenCatalog.CreateRuntimeProgramTable();
        }

        internal static bool TryValidateMaterialProxy(
            GPUDrivenMaterialProxy materialProxy,
            out string validationMessage)
        {
            return TryResolveMaterialProgram(
                materialProxy,
                MaterialProgramCatalogAsset.LoadDefault(),
                out _,
                out validationMessage);
        }

        internal static bool TryValidateMaterialProxy(
            GPUDrivenMaterialProxy materialProxy,
            MaterialProgramCatalogAsset frozenCatalog,
            out string validationMessage)
        {
            return TryResolveMaterialProgram(
                materialProxy,
                frozenCatalog,
                out _,
                out validationMessage);
        }

        private static bool TryResolveMaterialProgram(
            GPUDrivenMaterialProxy materialProxy,
            MaterialProgramCatalogAsset frozenCatalog,
            out MaterialProgramRuntimeBinding materialProgram,
            out string validationMessage)
        {
            materialProgram = null;
            if (materialProxy == null)
            {
                validationMessage = "GPU-driven material proxy is null.";
                return false;
            }
            if (frozenCatalog != null
                && !frozenCatalog.ExtendsBuiltinCatalog(
                    s_MaterialProgramCatalog,
                    out string frozenCatalogFailure))
            {
                validationMessage =
                    $"The Frozen Material Program Catalog is incompatible: {frozenCatalogFailure}";
                return false;
            }

            MaterialProgramTopologySpecialization expectedTopology =
                MaterialProgramTopologySpecialization.SingleSlab;
            switch (materialProxy.Model)
            {
                case GPUDrivenMaterialProxyModel.StandardLit:
                    expectedTopology =
                        MaterialProgramTopologySpecialization.SingleSlab;
                    break;
                case GPUDrivenMaterialProxyModel.DualSlab:
                    break;
                default:
                    validationMessage =
                        $"GPU-driven material model '{materialProxy.Model}' is not supported.";
                    return false;
            }

            if (materialProxy.Model == GPUDrivenMaterialProxyModel.DualSlab)
            {
                GPUDrivenDualSlabMaterialDefinition definition =
                    materialProxy.DualSlabDefinition;
                if (definition == null)
                {
                    validationMessage = "Dual Slab materials require a definition.";
                    return false;
                }

                GPUDrivenMaterialProxy topSlab = definition.TopSlab;
                if (topSlab == null)
                {
                    validationMessage =
                        "Dual Slab definitions require a StandardLit top-slab proxy.";
                    return false;
                }
                if (ReferenceEquals(topSlab, materialProxy))
                {
                    validationMessage =
                        "Dual Slab definitions cannot use the base proxy as their top Slab.";
                    return false;
                }
                if (topSlab.Model != GPUDrivenMaterialProxyModel.StandardLit)
                {
                    validationMessage =
                        "Dual Slab definitions require a StandardLit top-slab proxy; nested Dual Slab topology is not supported.";
                    return false;
                }

                switch (definition.Operator)
                {
                    case VividDualSlabOperator.HorizontalMix:
                        expectedTopology =
                            MaterialProgramTopologySpecialization.HorizontalMix;
                        break;
                    case VividDualSlabOperator.VerticalLayer:
                        expectedTopology =
                            MaterialProgramTopologySpecialization.VerticalLayer;
                        break;
                    default:
                        validationMessage =
                            $"Dual Slab operator '{definition.Operator}' is not supported.";
                        return false;
                }
            }

            MaterialGraphImportAsset graph = materialProxy.MaterialGraph;
            if (graph == null)
            {
                MaterialProgramCatalog.ManifestEntry builtinProgram =
                    materialProxy.Model
                    == GPUDrivenMaterialProxyModel.StandardLit
                        ? GetCatalogedMaterialProgram(
                            VividMaterialProgramID.StandardSingleSlab)
                        : GetDualSlabProgram(
                            materialProxy.DualSlabDefinition.Operator);
                materialProgram = new MaterialProgramRuntimeBinding(builtinProgram);
            }
            else
            {
                if (!graph.Succeeded)
                {
                    validationMessage =
                        "The assigned Material Graph did not compile successfully.";
                    return false;
                }
                if (graph.ProgramVersion != ProgramVersion)
                {
                    validationMessage =
                        $"The assigned Material Graph targets program version {graph.ProgramVersion}, but runtime version {ProgramVersion} is required. Reimport the graph.";
                    return false;
                }
                if (!graph.IsCataloged
                    || graph.ProgramID == VividMaterialProgramID.Invalid)
                {
                    validationMessage =
                        "The assigned Material Graph is not present in the Frozen Material Program Catalog.";
                    return false;
                }
                MaterialProgramCatalogManifestHash expectedManifestHash =
                    frozenCatalog != null
                        ? frozenCatalog.ManifestHash
                        : s_MaterialProgramCatalog.ManifestHash;
                MaterialProgramArtifactSetHash expectedArtifactSetHash =
                    frozenCatalog != null
                        ? frozenCatalog.ArtifactSetHash
                        : MaterialProgramArtifactSetHashBuilder.Compute(
                            s_MaterialProgramCatalog);
                if (graph.CatalogManifestHash != expectedManifestHash
                    || graph.ArtifactSetHash != expectedArtifactSetHash)
                {
                    validationMessage =
                        "The assigned Material Graph was imported against a stale or unpublished Material Program artifact set. Reimport the graph.";
                    return false;
                }

                MaterialProgramCatalog.ManifestEntry builtinEntry = null;
                MaterialProgramCatalogAsset.Slot frozenSlot = null;
                if (frozenCatalog != null)
                {
                    frozenCatalog.TryGetSlot(graph.ProgramID, out frozenSlot);
                }
                else
                {
                    uint builtinIndex = (uint) graph.ProgramID;
                    if (builtinIndex
                        < (uint) s_MaterialProgramCatalog.RuntimeTableLength)
                    {
                        builtinEntry =
                            s_MaterialProgramCatalog.Slots[(int) builtinIndex];
                    }
                }

                uint programIndex = (uint) graph.ProgramID;
                if (builtinEntry == null && frozenSlot == null)
                {
                    validationMessage =
                        $"The assigned Material Graph references unknown ProgramID {programIndex}.";
                    return false;
                }
                if (frozenSlot != null && frozenSlot.IsReserved)
                {
                    validationMessage =
                        $"The assigned Material Graph references reserved ProgramID {programIndex}.";
                    return false;
                }
                materialProgram = builtinEntry != null
                    ? new MaterialProgramRuntimeBinding(builtinEntry)
                    : new MaterialProgramRuntimeBinding(frozenSlot);
                if (graph.CompiledProgramHash != materialProgram.CompiledHash
                    || graph.LayoutFingerprint
                        != materialProgram.LayoutFingerprint)
                {
                    validationMessage =
                        "The assigned Material Graph binding does not match the cataloged compiled payload. Reimport the graph.";
                    return false;
                }
            }

            if (materialProgram.Topology != expectedTopology)
            {
                validationMessage =
                    $"Material Graph ProgramID {(uint) materialProgram.ProgramID} is not compatible with proxy model '{materialProxy.Model}'.";
                materialProgram = null;
                return false;
            }

            GPUDrivenMaterialProxy topSlabProxy =
                materialProxy.Model == GPUDrivenMaterialProxyModel.DualSlab
                    ? materialProxy.DualSlabDefinition.TopSlab
                    : null;
            if (!TryValidateDeclarationBindings(
                    materialProgram,
                    materialProxy,
                    topSlabProxy,
                    out validationMessage))
            {
                materialProgram = null;
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        private static bool TryValidateDeclarationBindings(
            MaterialProgramRuntimeBinding materialProgram,
            GPUDrivenMaterialProxy materialProxy,
            GPUDrivenMaterialProxy topSlabProxy,
            out string validationMessage)
        {
            try
            {
                for (int bindingIndex = 0;
                     bindingIndex < materialProgram.ParameterBindings.Count;
                     bindingIndex++)
                {
                    ResolveProxyParameter(
                        materialProgram.ParameterBindings[bindingIndex].Declaration,
                        materialProxy,
                        topSlabProxy);
                }

                for (int bindingIndex = 0;
                     bindingIndex < materialProgram.ResourceBindings.Count;
                     bindingIndex++)
                {
                    MaterialResourceDeclaration declaration = materialProgram
                        .ResourceBindings[bindingIndex]
                        .Declaration;
                    if (materialProxy.TryGetTextureOverride(declaration, out _))
                        continue;
                    if (!MaterialNativeTemplateDeclarationAdapter.TryGetTexture(
                            declaration,
                            out MaterialTextureResource compatibilityResource))
                    {
                        validationMessage =
                            $"Material resource '{declaration.Symbol}' ({declaration.Type}) has no texture override on proxy '{materialProxy.name}'.";
                        return false;
                    }
                    if (MaterialNativeTemplateDeclarationAdapter.IsTopSlabTexture(
                            compatibilityResource)
                        && topSlabProxy == null)
                    {
                        validationMessage =
                            $"StandardLit compatibility resource '{declaration.Symbol}' requires a top-slab proxy.";
                        return false;
                    }
                }
            }
            catch (InvalidOperationException exception)
            {
                validationMessage = exception.Message;
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }

        private static MaterialProgramCatalog CreateProductionProgramCatalog()
        {
            MaterialProgramTemplateRegistry templates =
                MaterialProgramBuiltinCatalog.Templates;
            if (templates.Count != MaterialProgramContract.BuiltinProgramCount)
            {
                throw new InvalidOperationException(
                    "The builtin native material template registry must contain exactly "
                    + $"{MaterialProgramContract.BuiltinProgramCount} templates.");
            }

            CompiledMaterialProgram standard =
                MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                    ProgramVersion);
            CompiledMaterialProgram horizontal =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    ProgramVersion,
                    VividDualSlabOperator.HorizontalMix);
            CompiledMaterialProgram vertical =
                MaterialProgramPrototypeBuilder.BuildDualSlab(
                    ProgramVersion,
                    VividDualSlabOperator.VerticalLayer);
            CompiledMaterialProgram genericSingleSlabProof =
                MaterialProgramPrototypeBuilder.BuildGenericSingleSlabProof(
                    ProgramVersion);
            MaterialProgramCatalog catalog = MaterialProgramCatalog.Bake(
                templates,
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "P0.StandardSingleSlab",
                    standard),
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "P1.DualSlabHorizontalMix",
                    horizontal),
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "P2.DualSlabVerticalLayer",
                    vertical),
                MaterialProgramCatalogBakeSlot.ForProgram(
                    "P3.GenericSingleSlabProof",
                    genericSingleSlabProof));
            if (catalog.RuntimeTableLength
                != MaterialProgramContract.ProductionCatalogProgramCount)
            {
                throw new InvalidOperationException(
                    "The production material catalog does not match its frozen program count.");
            }
            return catalog;
        }

        internal static GPUDrivenCompiledMaterialInstance CompileStandardSingleSlab(
            GPUDrivenMaterialProxy materialProxy,
            uint parameterAddress,
            uint surfaceBindingIndex)
        {
            return CompileStandardSingleSlab(
                materialProxy,
                parameterAddress,
                surfaceBindingIndex,
                surfaceBindingIndex);
        }

        internal static GPUDrivenCompiledMaterialInstance CompileStandardSingleSlab(
            GPUDrivenMaterialProxy materialProxy,
            uint parameterAddress,
            uint resourceBindingAddress,
            uint legacySurfaceBindingIndex)
        {
            return CompileStandardSingleSlab(
                materialProxy,
                parameterAddress,
                resourceBindingAddress,
                legacySurfaceBindingIndex,
                MaterialProgramCatalogAsset.LoadDefault());
        }

        internal static GPUDrivenCompiledMaterialInstance CompileStandardSingleSlab(
            GPUDrivenMaterialProxy materialProxy,
            uint parameterAddress,
            uint resourceBindingAddress,
            uint legacySurfaceBindingIndex,
            MaterialProgramCatalogAsset frozenCatalog)
        {
            if (materialProxy == null)
                throw new ArgumentNullException(nameof(materialProxy));

            if (materialProxy.Model != GPUDrivenMaterialProxyModel.StandardLit)
            {
                throw new NotSupportedException(
                    $"GPU-driven material model '{materialProxy.Model}' is not supported by StandardLit programs.");
            }
            if (!TryResolveMaterialProgram(
                    materialProxy,
                    frozenCatalog,
                    out MaterialProgramRuntimeBinding materialProgram,
                    out string validationMessage))
            {
                throw new InvalidOperationException(validationMessage);
            }
            var runtimeHeader = new VividMaterialRuntimeHeader
            {
                ProgramID = materialProgram.ProgramID,
                ParameterAddress = parameterAddress,
                ResourceBindingAddress = resourceBindingAddress,
                Flags = GetRuntimeFlags(materialProxy),
            };
            VividMaterialData legacyMaterialData = CreateLegacyMaterialData(
                materialProxy,
                legacySurfaceBindingIndex);
            return new GPUDrivenCompiledMaterialInstance(
                materialProgram,
                runtimeHeader,
                legacyMaterialData,
                default,
                CreateGenericParameterLanes(
                    materialProgram,
                    materialProxy,
                    null));
        }

        internal static GPUDrivenCompiledMaterialInstance CompileStandardSingleSlab(
            in VividMaterialData materialData,
            uint parameterAddress,
            uint surfaceBindingIndex)
        {
            return CompileStandardSingleSlab(
                materialData,
                parameterAddress,
                surfaceBindingIndex,
                surfaceBindingIndex);
        }

        internal static GPUDrivenCompiledMaterialInstance CompileStandardSingleSlab(
            in VividMaterialData materialData,
            uint parameterAddress,
            uint resourceBindingAddress,
            uint legacySurfaceBindingIndex)
        {
            if (materialData.SurfaceBindingIndex != legacySurfaceBindingIndex)
            {
                throw new ArgumentException(
                    "The StandardLit material data surface binding must match the runtime header binding.",
                    nameof(legacySurfaceBindingIndex));
            }

            MaterialProgramCatalog.ManifestEntry materialProgram =
                GetCatalogedMaterialProgram(
                    VividMaterialProgramID.StandardSingleSlab);
            var runtimeHeader = new VividMaterialRuntimeHeader
            {
                ProgramID = materialProgram.ProgramID,
                ParameterAddress = parameterAddress,
                ResourceBindingAddress = resourceBindingAddress,
                Flags = GetRuntimeFlags(materialData),
            };
            return new GPUDrivenCompiledMaterialInstance(
                materialProgram,
                runtimeHeader,
                materialData,
                default,
                CreateGenericParameterLanes(
                    materialProgram,
                    materialData,
                    default,
                    isDualSlab: false));
        }

        internal static GPUDrivenCompiledMaterialInstance CompileDualSlab(
            GPUDrivenMaterialProxy materialProxy,
            uint parameterAddress,
            uint baseSurfaceBindingIndex)
        {
            return CompileDualSlab(
                materialProxy,
                parameterAddress,
                baseSurfaceBindingIndex,
                baseSurfaceBindingIndex);
        }

        internal static GPUDrivenCompiledMaterialInstance CompileDualSlab(
            GPUDrivenMaterialProxy materialProxy,
            uint parameterAddress,
            uint resourceBindingAddress,
            uint legacyBaseSurfaceBindingIndex)
        {
            if (materialProxy == null)
                throw new ArgumentNullException(nameof(materialProxy));
            if (materialProxy.Model != GPUDrivenMaterialProxyModel.DualSlab)
            {
                throw new NotSupportedException(
                    $"GPU-driven material model '{materialProxy.Model}' is not supported by Dual Slab programs.");
            }
            if (!TryResolveMaterialProgram(
                    materialProxy,
                    MaterialProgramCatalogAsset.LoadDefault(),
                    out MaterialProgramRuntimeBinding materialProgram,
                    out string validationMessage))
            {
                throw new InvalidOperationException(validationMessage);
            }

            GPUDrivenDualSlabMaterialDefinition definition =
                materialProxy.DualSlabDefinition;
            GPUDrivenMaterialProxy topSlab = definition.TopSlab;
            var runtimeHeader = new VividMaterialRuntimeHeader
            {
                ProgramID = materialProgram.ProgramID,
                ParameterAddress = parameterAddress,
                ResourceBindingAddress = resourceBindingAddress,
                Flags = GetRuntimeFlags(materialProxy),
            };
            VividSlabMaterialData baseSlab = CreateSlabMaterialData(materialProxy);
            VividSlabMaterialData topSlabData = CreateSlabMaterialData(topSlab);
            var dualSlabMaterialData = new VividDualSlabMaterialData
            {
                BaseAlbedoColor = baseSlab.AlbedoColor,
                BaseTextureTilingOffset = baseSlab.TextureTilingOffset,
                BaseMetallicSmoothnessRemap = baseSlab.MetallicSmoothnessRemap,
                BaseAmbientOcclusionRemap = baseSlab.AmbientOcclusionRemap,
                BaseNormalsStrength = baseSlab.NormalsStrength,
                BaseRoughness = baseSlab.Roughness,
                BaseMetallic = baseSlab.Metallic,
                BaseMaskMode = baseSlab.MaskMode,
                TopAlbedoColor = topSlabData.AlbedoColor,
                TopTextureTilingOffset = topSlabData.TextureTilingOffset,
                TopMetallicSmoothnessRemap = topSlabData.MetallicSmoothnessRemap,
                TopAmbientOcclusionRemap = topSlabData.AmbientOcclusionRemap,
                TopNormalsStrength = topSlabData.NormalsStrength,
                TopRoughness = topSlabData.Roughness,
                TopMetallic = topSlabData.Metallic,
                TopMaskMode = topSlabData.MaskMode,
                Emission = ConvertMaterialColorForGPU(materialProxy.EmissionColor),
                LayerOperator = definition.Operator,
                LayerWeight = Mathf.Clamp01(materialProxy.LayerWeight),
                AlphaClipThreshold = materialProxy.AlphaClip ? materialProxy.Cutoff : 0.0f,
                Padding0 = 0u,
            };
            VividMaterialData legacyMaterialData = CreateLegacyMaterialData(
                materialProxy,
                legacyBaseSurfaceBindingIndex);
            return new GPUDrivenCompiledMaterialInstance(
                materialProgram,
                runtimeHeader,
                legacyMaterialData,
                dualSlabMaterialData,
                CreateGenericParameterLanes(
                    materialProgram,
                    materialProxy,
                    topSlab));
        }

        internal static uint4[] CreateGenericParameterLanes(
            MaterialProgramCatalog.ManifestEntry materialProgram,
            in VividMaterialData legacyMaterialData,
            in VividDualSlabMaterialData dualSlabMaterialData,
            bool isDualSlab)
        {
            return CreateGenericParameterLanes(
                new MaterialProgramRuntimeBinding(materialProgram),
                legacyMaterialData,
                dualSlabMaterialData,
                isDualSlab);
        }

        internal static uint4[] CreateGenericParameterLanes(
            MaterialProgramRuntimeBinding materialProgram,
            in VividMaterialData legacyMaterialData,
            in VividDualSlabMaterialData dualSlabMaterialData,
            bool isDualSlab)
        {
            VividMaterialData legacyCopy = legacyMaterialData;
            VividDualSlabMaterialData dualSlabCopy = dualSlabMaterialData;
            return CreateGenericParameterLanes(
                materialProgram,
                declaration => ResolveCompatibilityParameter(
                    declaration,
                    legacyCopy,
                    dualSlabCopy,
                    isDualSlab));
        }

        internal static uint4[] CreateGenericParameterLanes(
            MaterialProgramRuntimeBinding materialProgram,
            GPUDrivenMaterialProxy materialProxy,
            GPUDrivenMaterialProxy topSlabProxy)
        {
            if (materialProxy == null)
                throw new ArgumentNullException(nameof(materialProxy));
            return CreateGenericParameterLanes(
                materialProgram,
                declaration => ResolveProxyParameter(
                    declaration,
                    materialProxy,
                    topSlabProxy));
        }

        internal static uint4[] CreatePreviewParameterLanes(
            MaterialProgramRuntimeBinding materialProgram,
            in VividMaterialData legacyMaterialData,
            in VividDualSlabMaterialData dualSlabMaterialData,
            bool isDualSlab)
        {
            VividMaterialData legacyCopy = legacyMaterialData;
            VividDualSlabMaterialData dualSlabCopy = dualSlabMaterialData;
            return CreateGenericParameterLanes(
                materialProgram,
                declaration =>
                    MaterialNativeTemplateDeclarationAdapter.TryGetParameter(
                        declaration,
                        out _)
                        ? ResolveCompatibilityParameter(
                            declaration,
                            legacyCopy,
                            dualSlabCopy,
                            isDualSlab)
                        : GetNeutralPreviewValue(declaration.Type));
        }

        private static uint4[] CreateGenericParameterLanes(
            MaterialProgramRuntimeBinding materialProgram,
            Func<MaterialParameterDeclaration, float4> resolveParameter)
        {
            if (materialProgram == null)
                throw new ArgumentNullException(nameof(materialProgram));
            if (resolveParameter == null)
                throw new ArgumentNullException(nameof(resolveParameter));
            int laneCount = materialProgram.ParameterStrideInWords / 4;
            var words = new uint[materialProgram.ParameterStrideInWords];
            for (int bindingIndex = 0;
                 bindingIndex < materialProgram.ParameterBindings.Count;
                 bindingIndex++)
            {
                MaterialRuntimeParameterBindingDescriptor binding =
                    materialProgram.ParameterBindings[bindingIndex];

                float4 value = resolveParameter(binding.Declaration);
                WriteParameterWords(words, binding, value);
            }

            var lanes = new uint4[laneCount];
            for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
            {
                int wordOffset = laneIndex * 4;
                lanes[laneIndex] = new uint4(
                    words[wordOffset],
                    words[wordOffset + 1],
                    words[wordOffset + 2],
                    words[wordOffset + 3]);
            }
            return lanes;
        }

        private static void WriteParameterWords(
            uint[] words,
            in MaterialRuntimeParameterBindingDescriptor binding,
            in float4 value)
        {
            uint4 bits = math.asuint(value);
            for (int wordIndex = 0; wordIndex < binding.WordCount; wordIndex++)
                words[binding.WordOffset + wordIndex] = bits[wordIndex];
        }

        private static float4 ResolveCompatibilityParameter(
            in MaterialParameterDeclaration declaration,
            in VividMaterialData legacy,
            in VividDualSlabMaterialData dual,
            bool isDualSlab)
        {
            if (!MaterialNativeTemplateDeclarationAdapter.TryGetParameter(
                    declaration,
                    out MaterialParameter parameter))
            {
                throw new InvalidOperationException(
                    $"Material parameter '{declaration.Symbol}' requires an instance override; legacy material data only supplies StandardLit declarations.");
            }
            switch (parameter)
            {
                case MaterialParameter.BaseColor:
                    return isDualSlab ? dual.BaseAlbedoColor : legacy.AlbedoColor;
                case MaterialParameter.TopBaseColor:
                    return dual.TopAlbedoColor;
                case MaterialParameter.Emission:
                    return isDualSlab ? dual.Emission : legacy.Emission;
                case MaterialParameter.Roughness:
                    return new float4(isDualSlab ? dual.BaseRoughness : legacy.Roughness);
                case MaterialParameter.TopRoughness:
                    return new float4(dual.TopRoughness);
                case MaterialParameter.Metallic:
                    return new float4(isDualSlab ? dual.BaseMetallic : legacy.Metallic);
                case MaterialParameter.TopMetallic:
                    return new float4(dual.TopMetallic);
                case MaterialParameter.LayerWeight:
                    return new float4(dual.LayerWeight);
                case MaterialParameter.AlphaClipThreshold:
                    return new float4(isDualSlab
                        ? dual.AlphaClipThreshold
                        : legacy.AlphaClipThreshold);
                default:
                    throw new NotSupportedException(
                        $"StandardLit compatibility does not supply material parameter '{declaration.Symbol}'.");
            }
        }

        private static float4 ResolveProxyParameter(
            in MaterialParameterDeclaration declaration,
            GPUDrivenMaterialProxy materialProxy,
            GPUDrivenMaterialProxy topSlabProxy)
        {
            if (materialProxy.TryGetParameterOverride(
                    declaration,
                    out Vector4 overrideValue))
            {
                return ToFloat4(overrideValue);
            }

            if (!MaterialNativeTemplateDeclarationAdapter.TryGetParameter(
                    declaration,
                    out MaterialParameter parameter))
            {
                throw new InvalidOperationException(
                    $"Material parameter '{declaration.Symbol}' ({declaration.Type}) has no value on proxy '{materialProxy.name}'. Add a declaration-matched parameter override.");
            }

            switch (parameter)
            {
                case MaterialParameter.BaseColor:
                    return ConvertMaterialColorForGPU(materialProxy.BaseColor);
                case MaterialParameter.TopBaseColor:
                    return ConvertMaterialColorForGPU(
                        RequireTopSlabProxy(topSlabProxy, declaration).BaseColor);
                case MaterialParameter.Roughness:
                    return new float4(materialProxy.Roughness);
                case MaterialParameter.TopRoughness:
                    return new float4(
                        RequireTopSlabProxy(topSlabProxy, declaration).Roughness);
                case MaterialParameter.Metallic:
                    return new float4(materialProxy.Metallic);
                case MaterialParameter.TopMetallic:
                    return new float4(
                        RequireTopSlabProxy(topSlabProxy, declaration).Metallic);
                case MaterialParameter.LayerWeight:
                    return new float4(materialProxy.LayerWeight);
                case MaterialParameter.AlphaClipThreshold:
                    return new float4(
                        materialProxy.AlphaClip ? materialProxy.Cutoff : 0.0f);
                case MaterialParameter.Emission:
                    return ConvertMaterialColorForGPU(materialProxy.EmissionColor);
                default:
                    throw new NotSupportedException(
                        $"StandardLit compatibility does not supply material parameter '{declaration.Symbol}'.");
            }
        }

        private static GPUDrivenMaterialProxy RequireTopSlabProxy(
            GPUDrivenMaterialProxy topSlabProxy,
            in MaterialParameterDeclaration declaration)
        {
            if (topSlabProxy != null)
                return topSlabProxy;
            throw new InvalidOperationException(
                $"Material parameter '{declaration.Symbol}' requires a top-slab proxy.");
        }

        private static float4 GetNeutralPreviewValue(MaterialValueType type)
        {
            switch (type)
            {
                case MaterialValueType.Bool:
                    return new float4(1.0f, 0.0f, 0.0f, 0.0f);
                case MaterialValueType.Float:
                    return new float4(0.5f, 0.0f, 0.0f, 0.0f);
                case MaterialValueType.Float2:
                    return new float4(0.5f, 0.5f, 0.0f, 0.0f);
                case MaterialValueType.Float3:
                    return new float4(0.5f, 0.5f, 0.5f, 0.0f);
                case MaterialValueType.Float4:
                    return new float4(1.0f);
                default:
                    throw new NotSupportedException(
                        $"Material preview cannot provide a value for parameter type '{type}'.");
            }
        }

        private static MaterialProgramCatalog.ManifestEntry GetDualSlabProgram(
            VividDualSlabOperator layerOperator)
        {
            switch (layerOperator)
            {
                case VividDualSlabOperator.HorizontalMix:
                    return GetCatalogedMaterialProgram(
                        VividMaterialProgramID.DualSlabHorizontalMix);
                case VividDualSlabOperator.VerticalLayer:
                    return GetCatalogedMaterialProgram(
                        VividMaterialProgramID.DualSlabVerticalLayer);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(layerOperator),
                        layerOperator,
                        null);
            }
        }

        private static VividMaterialData CreateLegacyMaterialData(
            GPUDrivenMaterialProxy materialProxy,
            uint surfaceBindingIndex)
        {
            return new VividMaterialData
            {
                AlbedoColor = ConvertMaterialColorForGPU(materialProxy.BaseColor),
                TextureTilingOffset = ToFloat4(materialProxy.TextureTilingOffset),
                Emission = ConvertMaterialColorForGPU(materialProxy.EmissionColor),
                MetallicSmoothnessRemap = new float4(
                    materialProxy.MetallicRemap.x,
                    materialProxy.MetallicRemap.y,
                    materialProxy.SmoothnessRemap.x,
                    materialProxy.SmoothnessRemap.y),
                AmbientOcclusionRemap = new float4(
                    materialProxy.AmbientOcclusionRemap.x,
                    materialProxy.AmbientOcclusionRemap.y,
                    0.0f,
                    0.0f),
                SurfaceBindingIndex = surfaceBindingIndex,
                NormalsStrength = materialProxy.BumpScale,
                Roughness = materialProxy.Roughness,
                Metallic = materialProxy.Metallic,
                SpecularAAScreenSpaceVariance = 0.0f,
                SpecularAAThreshold = 0.0f,
                GeometryFlags = VividGeometryFlags.None,
                MaterialFlags = materialProxy.DisableLighting
                    ? VividMaterialFlags.Unlit
                    : VividMaterialFlags.None,
                RendererListID = GetRendererListID(materialProxy),
                AlphaClipThreshold = materialProxy.AlphaClip ? materialProxy.Cutoff : 0.0f,
                Padding0 = (uint) materialProxy.MaskMode,
                Padding1 = 0u,
            };
        }

        private static VividSlabMaterialData CreateSlabMaterialData(
            GPUDrivenMaterialProxy materialProxy)
        {
            return new VividSlabMaterialData
            {
                AlbedoColor = ConvertMaterialColorForGPU(materialProxy.BaseColor),
                TextureTilingOffset = ToFloat4(materialProxy.TextureTilingOffset),
                MetallicSmoothnessRemap = new float4(
                    materialProxy.MetallicRemap.x,
                    materialProxy.MetallicRemap.y,
                    materialProxy.SmoothnessRemap.x,
                    materialProxy.SmoothnessRemap.y),
                AmbientOcclusionRemap = new float4(
                    materialProxy.AmbientOcclusionRemap.x,
                    materialProxy.AmbientOcclusionRemap.y,
                    0.0f,
                    0.0f),
                NormalsStrength = materialProxy.BumpScale,
                Roughness = materialProxy.Roughness,
                Metallic = materialProxy.Metallic,
                MaskMode = (uint) materialProxy.MaskMode,
            };
        }

        private static VividMaterialRuntimeFlags GetRuntimeFlags(
            GPUDrivenMaterialProxy materialProxy)
        {
            VividMaterialRuntimeFlags runtimeFlags = VividMaterialRuntimeFlags.None;
            if (materialProxy.AlphaClip)
                runtimeFlags |= VividMaterialRuntimeFlags.AlphaClip;
            if (materialProxy.DisableLighting)
                runtimeFlags |= VividMaterialRuntimeFlags.Unlit;
            return runtimeFlags;
        }

        private static VividMaterialRuntimeFlags GetRuntimeFlags(
            in VividMaterialData materialData)
        {
            VividMaterialRuntimeFlags runtimeFlags = VividMaterialRuntimeFlags.None;
            if (materialData.AlphaClipThreshold > 0.0f
                || (materialData.RendererListID & VividRendererListID.AlphaTest) != 0)
            {
                runtimeFlags |= VividMaterialRuntimeFlags.AlphaClip;
            }
            if ((materialData.MaterialFlags & VividMaterialFlags.Unlit) != 0)
                runtimeFlags |= VividMaterialRuntimeFlags.Unlit;
            return runtimeFlags;
        }

        internal static VividMaterialRuntimeHeader CreateLegacyFallbackHeader(
            uint parameterAddress,
            uint surfaceBindingIndex)
        {
            return new VividMaterialRuntimeHeader
            {
                ProgramID = VividMaterialProgramID.Invalid,
                ParameterAddress = parameterAddress,
                ResourceBindingAddress = surfaceBindingIndex,
                Flags = VividMaterialRuntimeFlags.None,
            };
        }

        internal static float4 ConvertMaterialColorForGPU(Color color)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                Color linearColor = color.linear;
                return new float4(linearColor.r, linearColor.g, linearColor.b, color.a);
            }

            return new float4(color.r, color.g, color.b, color.a);
        }

        private static VividRendererListID GetRendererListID(GPUDrivenMaterialProxy materialProxy)
        {
            VividRendererListID rendererListID = VividRendererListID.Default;
            if (materialProxy.CullMode == CullMode.Front)
                rendererListID |= VividRendererListID.CullFront;
            else if (materialProxy.CullMode == CullMode.Off)
                rendererListID |= VividRendererListID.CullOff;

            if (materialProxy.AlphaClip)
                rendererListID |= VividRendererListID.AlphaTest;

            return rendererListID;
        }

        private static float4 ToFloat4(Vector4 value)
        {
            return new float4(value.x, value.y, value.z, value.w);
        }
    }
}
