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
            ParameterLanes = parameterLanes
                ?? throw new ArgumentNullException(nameof(parameterLanes));
        }

        internal MaterialProgramCatalog.ManifestEntry CatalogProgram { get; }

        internal CompiledMaterialProgram MaterialProgram => CatalogProgram.Program;

        internal VividMaterialProgramID ProgramID => CatalogProgram.ProgramID;

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

            if (materialProgram.Program.Lowering.SelectionKey.Topology
                    != expectedTopology)
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
                    legacyMaterialData,
                    default,
                    isDualSlab: false));
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
                    legacyMaterialData,
                    dualSlabMaterialData,
                    isDualSlab: true));
        }

        internal static uint4[] CreateGenericParameterLanes(
            MaterialProgramCatalog.ManifestEntry materialProgram,
            in VividMaterialData legacyMaterialData,
            in VividDualSlabMaterialData dualSlabMaterialData,
            bool isDualSlab)
        {
            MaterialGenericLayout layout =
                materialProgram.Program.Lowering.GenericLayout;
            int laneCount = layout.ParameterStrideInWords / 4;
            var words = new uint[layout.ParameterStrideInWords];
            MaterialNativeTemplateLayoutSchema schema =
                materialProgram.Program.Lowering.Template.LayoutSchema;
            for (int bindingIndex = 0;
                 bindingIndex < layout.ParameterBindings.Count;
                 bindingIndex++)
            {
                MaterialGenericParameterBinding genericBinding =
                    layout.ParameterBindings[bindingIndex];
                if (!schema.TryGetParameterBinding(
                        genericBinding.Declaration,
                        out MaterialNativeParameterBinding nativeBinding))
                {
                    throw new InvalidOperationException(
                        $"Catalog program '{materialProgram.StableName}' has no runtime source for parameter '{genericBinding.Declaration.Symbol}'.");
                }

                float4 value = GetRuntimeParameterValue(
                    nativeBinding.Target,
                    legacyMaterialData,
                    dualSlabMaterialData,
                    isDualSlab);
                WriteParameterWords(words, genericBinding, value);
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
            in MaterialGenericParameterBinding binding,
            in float4 value)
        {
            uint4 bits = math.asuint(value);
            for (int wordIndex = 0; wordIndex < binding.WordCount; wordIndex++)
                words[binding.WordOffset + wordIndex] = bits[wordIndex];
        }

        private static float4 GetRuntimeParameterValue(
            MaterialRuntimeParameter parameter,
            in VividMaterialData legacy,
            in VividDualSlabMaterialData dual,
            bool isDualSlab)
        {
            switch (parameter)
            {
                case MaterialRuntimeParameter.BaseColor:
                    return isDualSlab ? dual.BaseAlbedoColor : legacy.AlbedoColor;
                case MaterialRuntimeParameter.TopBaseColor:
                    return dual.TopAlbedoColor;
                case MaterialRuntimeParameter.Emission:
                    return isDualSlab ? dual.Emission : legacy.Emission;
                case MaterialRuntimeParameter.Roughness:
                    return new float4(isDualSlab ? dual.BaseRoughness : legacy.Roughness);
                case MaterialRuntimeParameter.TopRoughness:
                    return new float4(dual.TopRoughness);
                case MaterialRuntimeParameter.Metallic:
                    return new float4(isDualSlab ? dual.BaseMetallic : legacy.Metallic);
                case MaterialRuntimeParameter.TopMetallic:
                    return new float4(dual.TopMetallic);
                case MaterialRuntimeParameter.LayerWeight:
                    return new float4(dual.LayerWeight);
                case MaterialRuntimeParameter.AlphaClipThreshold:
                    return new float4(isDualSlab
                        ? dual.AlphaClipThreshold
                        : legacy.AlphaClipThreshold);
                default:
                    throw new NotSupportedException(
                        $"Generic material parameter packing does not support runtime source '{parameter}'.");
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
