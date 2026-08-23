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
            CompiledMaterialProgram materialProgram,
            in VividMaterialRuntimeHeader runtimeHeader,
            in VividMaterialData legacyMaterialData)
            : this(materialProgram, runtimeHeader, legacyMaterialData, default)
        {
        }

        internal GPUDrivenCompiledMaterialInstance(
            CompiledMaterialProgram materialProgram,
            in VividMaterialRuntimeHeader runtimeHeader,
            in VividMaterialData legacyMaterialData,
            in VividDualSlabMaterialData dualSlabMaterialData)
        {
            MaterialProgram = materialProgram
                ?? throw new ArgumentNullException(nameof(materialProgram));
            RuntimeHeader = runtimeHeader;
            LegacyMaterialData = legacyMaterialData;
            DualSlabMaterialData = dualSlabMaterialData;
        }

        internal CompiledMaterialProgram MaterialProgram { get; }

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
            CreateBuiltinProgramCatalog();

        internal static CompiledMaterialProgram GetMaterialProgram(
            VividMaterialProgramID programID)
        {
            return s_MaterialProgramCatalog.GetMaterialProgram(programID);
        }

        internal static VividMaterialProgramData[] CreateRuntimeProgramTable()
        {
            return s_MaterialProgramCatalog.CreateRuntimeProgramTable();
        }

        private static MaterialProgramCatalog CreateBuiltinProgramCatalog()
        {
            MaterialProgramCatalogDefinition definition =
                MaterialProgramBuiltinCatalog.Definition;
            if (definition.Count != MaterialProgramContract.BuiltinProgramCount)
            {
                throw new InvalidOperationException(
                    $"The native material program catalog must contain exactly "
                    + $"{MaterialProgramContract.BuiltinProgramCount} programs.");
            }
            for (int programIndex = 0;
                 programIndex < definition.Entries.Count;
                 programIndex++)
            {
                var expectedProgramID =
                    (VividMaterialProgramID) (uint) programIndex;
                if (definition.Entries[programIndex].ProgramID != expectedProgramID)
                {
                    throw new InvalidOperationException(
                        $"Builtin material program '{expectedProgramID}' must occupy "
                        + $"its frozen ABI index {programIndex}.");
                }
            }

            return new MaterialProgramCatalog(
                definition,
                new[]
                {
                    MaterialProgramPrototypeBuilder.BuildStandardSingleSlab(
                        ProgramVersion),
                    MaterialProgramPrototypeBuilder.BuildDualSlab(
                        ProgramVersion,
                        VividDualSlabOperator.HorizontalMix),
                    MaterialProgramPrototypeBuilder.BuildDualSlab(
                        ProgramVersion,
                        VividDualSlabOperator.VerticalLayer),
                });
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
                    $"GPU-driven material model '{materialProxy.Model}' is not supported by Program 0.");
            }

            CompiledMaterialProgram materialProgram = GetMaterialProgram(
                VividMaterialProgramID.StandardSingleSlab);
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

            GPUDrivenDualSlabMaterialDefinition definition =
                materialProxy.DualSlabDefinition;
            if (definition == null)
                throw new InvalidOperationException("Dual Slab materials require a definition.");

            GPUDrivenMaterialProxy topSlab = definition.TopSlab;
            if (topSlab == null || topSlab.Model != GPUDrivenMaterialProxyModel.StandardLit)
            {
                throw new InvalidOperationException(
                    "Dual Slab definitions require a StandardLit top-slab proxy.");
            }
            CompiledMaterialProgram materialProgram = GetDualSlabProgram(definition.Operator);

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

        private static CompiledMaterialProgram GetDualSlabProgram(
            VividDualSlabOperator layerOperator)
        {
            switch (layerOperator)
            {
                case VividDualSlabOperator.HorizontalMix:
                    return GetMaterialProgram(
                        VividMaterialProgramID.DualSlabHorizontalMix);
                case VividDualSlabOperator.VerticalLayer:
                    return GetMaterialProgram(
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
