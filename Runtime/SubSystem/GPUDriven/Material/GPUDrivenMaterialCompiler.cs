using System;
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
            : this(catalogProgram, runtimeHeader, legacyMaterialData, default)
        {
        }

        internal GPUDrivenCompiledMaterialInstance(
            MaterialProgramCatalog.ManifestEntry catalogProgram,
            in VividMaterialRuntimeHeader runtimeHeader,
            in VividMaterialData legacyMaterialData,
            in VividDualSlabMaterialData dualSlabMaterialData)
        {
            CatalogProgram = catalogProgram
                ?? throw new ArgumentNullException(nameof(catalogProgram));
            if (runtimeHeader.ProgramID != catalogProgram.ProgramID)
            {
                throw new ArgumentException(
                    "The runtime header ProgramID must match its cataloged material program.",
                    nameof(runtimeHeader));
            }
            RuntimeHeader = runtimeHeader;
            LegacyMaterialData = legacyMaterialData;
            DualSlabMaterialData = dualSlabMaterialData;
        }

        internal MaterialProgramCatalog.ManifestEntry CatalogProgram { get; }

        internal CompiledMaterialProgram MaterialProgram => CatalogProgram.Program;

        internal VividMaterialProgramID ProgramID => CatalogProgram.ProgramID;

        internal VividMaterialRuntimeHeader RuntimeHeader { get; }

        internal VividMaterialData LegacyMaterialData { get; }

        internal VividDualSlabMaterialData DualSlabMaterialData { get; }
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

        internal static VividMaterialProgramData[] CreateRuntimeProgramTable()
        {
            MaterialProgramCatalogAsset frozenCatalog =
                MaterialProgramCatalogAsset.LoadDefault();
            if (frozenCatalog == null)
                return s_MaterialProgramCatalog.CreateRuntimeProgramTable();
            if (frozenCatalog.Matches(s_MaterialProgramCatalog, out _))
                return frozenCatalog.CreateRuntimeProgramTable();

            // During an editor domain reload the delayed baker may not have updated
            // the asset yet. Player builds are strict; the build preprocessor bakes
            // the catalog before content is packed.
            if (Application.isEditor)
                return s_MaterialProgramCatalog.CreateRuntimeProgramTable();
            return CreateRuntimeProgramTable(frozenCatalog);
        }

        internal static VividMaterialProgramData[] CreateRuntimeProgramTable(
            MaterialProgramCatalogAsset frozenCatalog)
        {
            if (frozenCatalog == null)
                throw new ArgumentNullException(nameof(frozenCatalog));
            if (!frozenCatalog.Matches(
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
                out _,
                out validationMessage);
        }

        private static bool TryResolveMaterialProgram(
            GPUDrivenMaterialProxy materialProxy,
            out MaterialProgramCatalog.ManifestEntry materialProgram,
            out string validationMessage)
        {
            materialProgram = null;
            if (materialProxy == null)
            {
                validationMessage = "GPU-driven material proxy is null.";
                return false;
            }

            MaterialProgramTopologySpecialization expectedTopology =
                MaterialProgramTopologySpecialization.SingleSlab;
            VividMaterialParameterLayoutID expectedParameterLayout;
            VividMaterialResourceLayoutID expectedResourceLayout;
            switch (materialProxy.Model)
            {
                case GPUDrivenMaterialProxyModel.StandardLit:
                    expectedTopology =
                        MaterialProgramTopologySpecialization.SingleSlab;
                    expectedParameterLayout =
                        VividMaterialParameterLayoutID.LegacyMaterialData;
                    expectedResourceLayout =
                        VividMaterialResourceLayoutID.LegacySurfaceBinding;
                    break;
                case GPUDrivenMaterialProxyModel.DualSlab:
                    expectedParameterLayout =
                        VividMaterialParameterLayoutID.DualSlabMaterialData;
                    expectedResourceLayout =
                        VividMaterialResourceLayoutID.DualSurfaceBinding;
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
                materialProgram = materialProxy.Model
                    == GPUDrivenMaterialProxyModel.StandardLit
                        ? GetCatalogedMaterialProgram(
                            VividMaterialProgramID.StandardSingleSlab)
                        : GetDualSlabProgram(
                            materialProxy.DualSlabDefinition.Operator);
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
                if (graph.CatalogManifestHash
                    != s_MaterialProgramCatalog.ManifestHash)
                {
                    validationMessage =
                        "The assigned Material Graph was imported against a stale Frozen Material Program Catalog. Reimport the graph.";
                    return false;
                }

                uint programIndex = (uint) graph.ProgramID;
                if (programIndex >= (uint) s_MaterialProgramCatalog.RuntimeTableLength)
                {
                    validationMessage =
                        $"The assigned Material Graph references unknown ProgramID {programIndex}.";
                    return false;
                }
                materialProgram =
                    s_MaterialProgramCatalog.Slots[(int) programIndex];
                if (materialProgram == null)
                {
                    validationMessage =
                        $"The assigned Material Graph references reserved ProgramID {programIndex}.";
                    return false;
                }
                if (graph.CompiledProgramHash
                        != materialProgram.Program.CompiledHash
                    || graph.LayoutFingerprint
                        != materialProgram.LayoutFingerprint)
                {
                    validationMessage =
                        "The assigned Material Graph binding does not match the cataloged compiled payload. Reimport the graph.";
                    return false;
                }
            }

            VividMaterialProgramData runtimeData = materialProgram.RuntimeData;
            if (materialProgram.Program.Lowering.SelectionKey.Topology
                    != expectedTopology
                || runtimeData.ParameterLayoutID != expectedParameterLayout
                || runtimeData.ResourceLayoutID != expectedResourceLayout)
            {
                validationMessage =
                    $"Material Graph ProgramID {(uint) materialProgram.ProgramID} is not compatible with proxy model '{materialProxy.Model}'.";
                materialProgram = null;
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
            if (materialProxy == null)
                throw new ArgumentNullException(nameof(materialProxy));

            if (materialProxy.Model != GPUDrivenMaterialProxyModel.StandardLit)
            {
                throw new NotSupportedException(
                    $"GPU-driven material model '{materialProxy.Model}' is not supported by StandardLit programs.");
            }
            if (!TryResolveMaterialProgram(
                    materialProxy,
                    out MaterialProgramCatalog.ManifestEntry materialProgram,
                    out string validationMessage))
            {
                throw new InvalidOperationException(validationMessage);
            }
            var runtimeHeader = new VividMaterialRuntimeHeader
            {
                ProgramID = materialProgram.ProgramID,
                ParameterAddress = parameterAddress,
                ResourceBindingAddress = surfaceBindingIndex,
                Flags = GetRuntimeFlags(materialProxy),
            };
            return new GPUDrivenCompiledMaterialInstance(
                materialProgram,
                runtimeHeader,
                CreateLegacyMaterialData(materialProxy, surfaceBindingIndex));
        }

        internal static GPUDrivenCompiledMaterialInstance CompileStandardSingleSlab(
            in VividMaterialData materialData,
            uint parameterAddress,
            uint surfaceBindingIndex)
        {
            if (materialData.SurfaceBindingIndex != surfaceBindingIndex)
            {
                throw new ArgumentException(
                    "The StandardLit material data surface binding must match the runtime header binding.",
                    nameof(surfaceBindingIndex));
            }

            MaterialProgramCatalog.ManifestEntry materialProgram =
                GetCatalogedMaterialProgram(
                    VividMaterialProgramID.StandardSingleSlab);
            var runtimeHeader = new VividMaterialRuntimeHeader
            {
                ProgramID = materialProgram.ProgramID,
                ParameterAddress = parameterAddress,
                ResourceBindingAddress = surfaceBindingIndex,
                Flags = GetRuntimeFlags(materialData),
            };
            return new GPUDrivenCompiledMaterialInstance(
                materialProgram,
                runtimeHeader,
                materialData);
        }

        internal static GPUDrivenCompiledMaterialInstance CompileDualSlab(
            GPUDrivenMaterialProxy materialProxy,
            uint parameterAddress,
            uint baseSurfaceBindingIndex)
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
                    out MaterialProgramCatalog.ManifestEntry materialProgram,
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
                ResourceBindingAddress = baseSurfaceBindingIndex,
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
            return new GPUDrivenCompiledMaterialInstance(
                materialProgram,
                runtimeHeader,
                CreateLegacyMaterialData(materialProxy, baseSurfaceBindingIndex),
                dualSlabMaterialData);
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
