using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class MaterialValueRequirements
    {
        private MaterialValueRequirements(
            IReadOnlyList<MaterialParameter> parameters,
            IReadOnlyList<MaterialTextureResource> textureResources,
            IReadOnlyList<MaterialExternalInput> externalInputs)
        {
            Parameters = parameters;
            TextureResources = textureResources;
            ExternalInputs = externalInputs;
        }

        internal IReadOnlyList<MaterialParameter> Parameters { get; }

        internal IReadOnlyList<MaterialTextureResource> TextureResources { get; }

        internal IReadOnlyList<MaterialExternalInput> ExternalInputs { get; }

        internal static MaterialValueRequirements Collect(MaterialValueSlice valueSlice)
        {
            if (valueSlice == null)
                throw new ArgumentNullException(nameof(valueSlice));

            var parameters = new List<MaterialParameter>();
            var textureResources = new List<MaterialTextureResource>();
            var externalInputs = new List<MaterialExternalInput>();
            for (int i = 0; i < valueSlice.NodeIndices.Count; i++)
            {
                MaterialValueNode node = valueSlice.Values.Nodes[valueSlice.NodeIndices[i]];
                switch (node.Opcode)
                {
                    case MaterialValueOpcode.Parameter:
                        parameters.Add((MaterialParameter) node.Semantic);
                        break;
                    case MaterialValueOpcode.TextureResource:
                        textureResources.Add((MaterialTextureResource) node.Semantic);
                        break;
                    case MaterialValueOpcode.ExternalInput:
                        externalInputs.Add((MaterialExternalInput) node.Semantic);
                        break;
                }
            }

            return new MaterialValueRequirements(
                parameters.AsReadOnly(),
                textureResources.AsReadOnly(),
                externalInputs.AsReadOnly());
        }

        internal static MaterialValueRequirements Merge(
            params MaterialValueRequirements[] requirements)
        {
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));

            var parameters = new List<MaterialParameter>();
            var textureResources = new List<MaterialTextureResource>();
            var externalInputs = new List<MaterialExternalInput>();
            for (int i = 0; i < requirements.Length; i++)
            {
                MaterialValueRequirements source = requirements[i]
                    ?? throw new ArgumentException(
                        "Material value requirements cannot contain null entries.",
                        nameof(requirements));
                AddUnique(parameters, source.Parameters);
                AddUnique(textureResources, source.TextureResources);
                AddUnique(externalInputs, source.ExternalInputs);
            }

            parameters.Sort((left, right) => ((int) left).CompareTo((int) right));
            textureResources.Sort((left, right) => ((int) left).CompareTo((int) right));
            externalInputs.Sort((left, right) => ((int) left).CompareTo((int) right));
            return new MaterialValueRequirements(
                parameters.AsReadOnly(),
                textureResources.AsReadOnly(),
                externalInputs.AsReadOnly());
        }

        private static void AddUnique<T>(List<T> destination, IReadOnlyList<T> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (!destination.Contains(source[i]))
                    destination.Add(source[i]);
            }
        }
    }

    internal static class MaterialValuePatternMatcher
    {
        internal static bool MatchesParameter(
            MaterialValueIR values,
            MaterialValue value,
            MaterialParameter parameter)
        {
            return MatchesSemantic(
                values.GetNode(value),
                MaterialValueOpcode.Parameter,
                value.Type,
                (int) parameter);
        }

        internal static bool MatchesExternalInput(
            MaterialValueIR values,
            MaterialValue value,
            MaterialExternalInput input)
        {
            return MatchesSemantic(
                values.GetNode(value),
                MaterialValueOpcode.ExternalInput,
                value.Type,
                (int) input);
        }

        internal static bool MatchesSampledColor(
            MaterialValueIR values,
            MaterialValue value,
            MaterialTextureResource textureResource,
            MaterialParameter colorParameter)
        {
            MaterialValueNode color = values.GetNode(value);
            if (color.Opcode != MaterialValueOpcode.Multiply
                || color.Type != MaterialValueType.Float4)
            {
                return false;
            }

            int sampleIndex;
            if (MatchesSemantic(
                    values.Nodes[color.Operand0],
                    MaterialValueOpcode.Parameter,
                    MaterialValueType.Float4,
                    (int) colorParameter))
            {
                sampleIndex = color.Operand1;
            }
            else if (MatchesSemantic(
                         values.Nodes[color.Operand1],
                         MaterialValueOpcode.Parameter,
                         MaterialValueType.Float4,
                         (int) colorParameter))
            {
                sampleIndex = color.Operand0;
            }
            else
            {
                return false;
            }

            MaterialValueNode sample = values.Nodes[sampleIndex];
            if (sample.Opcode != MaterialValueOpcode.TextureSampleGrad
                || sample.Type != MaterialValueType.Float4
                || !MatchesSemantic(
                    values.Nodes[sample.Operand0],
                    MaterialValueOpcode.TextureResource,
                    MaterialValueType.Texture2D,
                    (int) textureResource)
                || !MatchesSemantic(
                    values.Nodes[sample.Operand1],
                    MaterialValueOpcode.ExternalInput,
                    MaterialValueType.Float2,
                    (int) MaterialExternalInput.UV0))
            {
                return false;
            }

            return MatchesDerivative(
                    values,
                    sample.Operand2,
                    MaterialValueOpcode.Ddx,
                    sample.Operand1)
                && MatchesDerivative(
                    values,
                    sample.Operand3,
                    MaterialValueOpcode.Ddy,
                    sample.Operand1);
        }

        private static bool MatchesDerivative(
            MaterialValueIR values,
            int nodeIndex,
            MaterialValueOpcode opcode,
            int sourceIndex)
        {
            MaterialValueNode node = values.Nodes[nodeIndex];
            return node.Opcode == opcode
                && node.Type == MaterialValueType.Float2
                && node.Operand0 == sourceIndex;
        }

        private static bool MatchesSemantic(
            in MaterialValueNode node,
            MaterialValueOpcode opcode,
            MaterialValueType type,
            int semantic)
        {
            return node.Opcode == opcode
                && node.Type == type
                && node.Semantic == semantic;
        }
    }

    internal sealed class CompiledCoverageProgram
    {
        internal CompiledCoverageProgram(
            VividMaterialCoverageProgramID programID,
            MaterialValueSlice valueSlice,
            MaterialValueRequirements requirements)
        {
            ProgramID = programID;
            ValueSlice = valueSlice ?? throw new ArgumentNullException(nameof(valueSlice));
            Requirements = requirements
                ?? throw new ArgumentNullException(nameof(requirements));
        }

        internal VividMaterialCoverageProgramID ProgramID { get; }

        internal MaterialValueSlice ValueSlice { get; }

        internal MaterialValueRequirements Requirements { get; }

        internal IReadOnlyList<MaterialParameter> RequiredParameters =>
            Requirements.Parameters;

        internal IReadOnlyList<MaterialTextureResource> RequiredTextureResources =>
            Requirements.TextureResources;

        internal IReadOnlyList<MaterialExternalInput> RequiredExternalInputs =>
            Requirements.ExternalInputs;
    }

    internal static class CoverageProgramLowerer
    {
        internal static CompiledCoverageProgram Compile(MaterialIRModule module)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            MaterialValueSlice valueSlice = module.CreateValueSlice(
                module.Outputs.CoverageValue,
                module.Outputs.AlphaClipThreshold);
            if (!MatchesBaseColorAlphaProgram(module, valueSlice))
            {
                throw new NotSupportedException(
                    "Coverage value IR cannot be lowered to an existing coverage program ABI.");
            }

            MaterialValueRequirements requirements =
                MaterialValueRequirements.Collect(valueSlice);
            return new CompiledCoverageProgram(
                VividMaterialCoverageProgramID.BaseColorAlpha,
                valueSlice,
                requirements);
        }

        private static bool MatchesBaseColorAlphaProgram(
            MaterialIRModule module,
            MaterialValueSlice valueSlice)
        {
            MaterialValueIR values = module.Values;
            return MaterialValuePatternMatcher.MatchesParameter(
                    values,
                    module.Outputs.AlphaClipThreshold,
                    MaterialParameter.AlphaClipThreshold)
                && MaterialValuePatternMatcher.MatchesSampledColor(
                    values,
                    module.Outputs.CoverageValue,
                    MaterialTextureResource.BaseColor,
                    MaterialParameter.BaseColor)
                && valueSlice.NodeCount == 8;
        }
    }

    internal sealed class CompiledSurfaceProgram
    {
        internal CompiledSurfaceProgram(
            VividMaterialSurfaceProgramID programID,
            MaterialValueSlice valueSlice,
            MaterialValueRequirements requirements)
        {
            ProgramID = programID;
            ValueSlice = valueSlice ?? throw new ArgumentNullException(nameof(valueSlice));
            Requirements = requirements
                ?? throw new ArgumentNullException(nameof(requirements));
        }

        internal VividMaterialSurfaceProgramID ProgramID { get; }

        internal MaterialValueSlice ValueSlice { get; }

        internal MaterialValueRequirements Requirements { get; }

        internal IReadOnlyList<MaterialParameter> RequiredParameters =>
            Requirements.Parameters;

        internal IReadOnlyList<MaterialTextureResource> RequiredTextureResources =>
            Requirements.TextureResources;

        internal IReadOnlyList<MaterialExternalInput> RequiredExternalInputs =>
            Requirements.ExternalInputs;
    }

    internal static class SurfaceProgramMatcher
    {
        private const ClosureFeatureMask SupportedSlabFeatures =
            ClosureFeatureMask.BaseColorTexture
            | ClosureFeatureMask.NormalTexture
            | ClosureFeatureMask.MaskTexture;

        internal static CompiledSurfaceProgram Compile(MaterialIRModule module)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            MaterialValueSlice valueSlice = CreateSurfaceValueSlice(module);
            VividMaterialSurfaceProgramID programID;
            if (MatchesStandardSingleSlab(module, valueSlice))
            {
                programID = VividMaterialSurfaceProgramID.StandardSingleSlab;
            }
            else if (MatchesDualSlab(module, valueSlice))
            {
                programID = VividMaterialSurfaceProgramID.DualSlab;
            }
            else
            {
                throw new NotSupportedException(
                    "Closure topology and value IR cannot be matched to an existing surface program ABI.");
            }

            return new CompiledSurfaceProgram(
                programID,
                valueSlice,
                MaterialValueRequirements.Collect(valueSlice));
        }

        private static MaterialValueSlice CreateSurfaceValueSlice(MaterialIRModule module)
        {
            ClosureTopology topology = module.Topology;
            var roots = new List<MaterialValue>();
            for (int i = 0; i < topology.NormalBases.Count; i++)
            {
                roots.Add(topology.NormalBases[i].Normal);
                roots.Add(topology.NormalBases[i].Tangent);
            }
            for (int i = 0; i < topology.Slabs.Count; i++)
            {
                roots.Add(topology.Slabs[i].BaseColor);
                roots.Add(topology.Slabs[i].Roughness);
                roots.Add(topology.Slabs[i].Metallic);
            }
            for (int i = 0; i < topology.Operators.Count; i++)
                roots.Add(topology.Operators[i].Weight);

            return module.CreateValueSlice(roots.ToArray());
        }

        private static bool MatchesStandardSingleSlab(
            MaterialIRModule module,
            MaterialValueSlice valueSlice)
        {
            ClosureTopology topology = module.Topology;
            if (topology.ClosureCount != 1
                || topology.OperatorCount != 0
                || topology.NormalBases.Count != 1)
            {
                return false;
            }

            ClosureSlab slab = topology.Slabs[0];
            return slab.IsTop
                && slab.IsBottom
                && MatchesNormalBasis(module.Values, topology.NormalBases[0])
                && MatchesSlab(
                    module.Values,
                    slab,
                    MaterialTextureResource.BaseColor,
                    MaterialParameter.BaseColor,
                    MaterialParameter.Roughness,
                    MaterialParameter.Metallic)
                && valueSlice.NodeCount == 11;
        }

        private static bool MatchesDualSlab(
            MaterialIRModule module,
            MaterialValueSlice valueSlice)
        {
            ClosureTopology topology = module.Topology;
            if (topology.ClosureCount != 2
                || topology.OperatorCount != 1
                || topology.NormalBases.Count != 1)
            {
                return false;
            }

            ClosureSlab baseSlab = topology.Slabs[0];
            ClosureSlab topSlab = topology.Slabs[1];
            ClosureOperator closureOperator = topology.Operators[0];
            return !baseSlab.IsTop
                && baseSlab.IsBottom
                && topSlab.IsTop
                && !topSlab.IsBottom
                && MatchesNormalBasis(module.Values, topology.NormalBases[0])
                && MatchesSlab(
                    module.Values,
                    baseSlab,
                    MaterialTextureResource.BaseColor,
                    MaterialParameter.BaseColor,
                    MaterialParameter.Roughness,
                    MaterialParameter.Metallic)
                && MatchesSlab(
                    module.Values,
                    topSlab,
                    MaterialTextureResource.TopBaseColor,
                    MaterialParameter.TopBaseColor,
                    MaterialParameter.TopRoughness,
                    MaterialParameter.TopMetallic)
                && (closureOperator.Kind == ClosureOperatorKind.HorizontalMix
                    || closureOperator.Kind == ClosureOperatorKind.VerticalLayer)
                && closureOperator.BackgroundSlabIndex == 0
                && closureOperator.ForegroundSlabIndex == 1
                && MaterialValuePatternMatcher.MatchesParameter(
                    module.Values,
                    closureOperator.Weight,
                    MaterialParameter.LayerWeight)
                && valueSlice.NodeCount == 18;
        }

        private static bool MatchesNormalBasis(
            MaterialValueIR values,
            in ClosureNormalBasis normalBasis)
        {
            return MaterialValuePatternMatcher.MatchesExternalInput(
                    values,
                    normalBasis.Normal,
                    MaterialExternalInput.GeometryNormalWS)
                && MaterialValuePatternMatcher.MatchesExternalInput(
                    values,
                    normalBasis.Tangent,
                    MaterialExternalInput.GeometryTangentWS);
        }

        private static bool MatchesSlab(
            MaterialValueIR values,
            in ClosureSlab slab,
            MaterialTextureResource textureResource,
            MaterialParameter baseColorParameter,
            MaterialParameter roughnessParameter,
            MaterialParameter metallicParameter)
        {
            // Tiling/remap and optional Normal/Mask evaluation remain in the V1 layout ABI.
            return slab.NormalBasisIndex == 0
                && (slab.Features & ClosureFeatureMask.BaseColorTexture) != 0
                && (slab.Features & ~SupportedSlabFeatures) == 0
                && MaterialValuePatternMatcher.MatchesSampledColor(
                    values,
                    slab.BaseColor,
                    textureResource,
                    baseColorParameter)
                && MaterialValuePatternMatcher.MatchesParameter(
                    values,
                    slab.Roughness,
                    roughnessParameter)
                && MaterialValuePatternMatcher.MatchesParameter(
                    values,
                    slab.Metallic,
                    metallicParameter);
        }
    }

    internal enum MaterialLayoutValueType
    {
        Float,
        Float4,
        UInt,
    }

    internal enum MaterialRuntimeParameter
    {
        BaseColor,
        TopBaseColor,
        BaseTextureTilingOffset,
        TopTextureTilingOffset,
        Emission,
        BaseMetallicSmoothnessRemap,
        TopMetallicSmoothnessRemap,
        BaseAmbientOcclusionRemap,
        TopAmbientOcclusionRemap,
        BaseNormalsStrength,
        TopNormalsStrength,
        Roughness,
        TopRoughness,
        Metallic,
        TopMetallic,
        BaseMaskMode,
        TopMaskMode,
        LayerOperator,
        LayerWeight,
        AlphaClipThreshold,
    }

    internal readonly struct MaterialParameterLayoutBinding
    {
        internal MaterialParameterLayoutBinding(
            MaterialRuntimeParameter parameter,
            MaterialLayoutValueType type,
            int byteOffset)
        {
            Parameter = parameter;
            Type = type;
            ByteOffset = byteOffset;
        }

        internal MaterialRuntimeParameter Parameter { get; }

        internal MaterialLayoutValueType Type { get; }

        internal int ByteOffset { get; }
    }

    internal readonly struct MaterialResourceLayoutBinding
    {
        internal MaterialResourceLayoutBinding(
            MaterialTextureResource resource,
            int recordOffset,
            int byteOffset)
        {
            Resource = resource;
            RecordOffset = recordOffset;
            ByteOffset = byteOffset;
        }

        internal MaterialTextureResource Resource { get; }

        internal int RecordOffset { get; }

        internal int ByteOffset { get; }
    }

    internal sealed class CompiledParameterLayout
    {
        private readonly IReadOnlyList<MaterialParameterLayoutBinding> m_Bindings;

        internal CompiledParameterLayout(
            VividMaterialParameterLayoutID layoutID,
            int stride,
            MaterialParameterLayoutBinding[] bindings)
        {
            if (stride <= 0)
                throw new ArgumentOutOfRangeException(nameof(stride));
            if (bindings == null)
                throw new ArgumentNullException(nameof(bindings));

            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                MaterialParameterLayoutBinding binding = bindings[bindingIndex];
                int bindingSize = GetValueSize(binding.Type);
                if (binding.ByteOffset < 0
                    || binding.ByteOffset > stride - bindingSize)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(bindings),
                        $"Parameter '{binding.Parameter}' exceeds layout stride {stride}.");
                }
                for (int previousIndex = 0; previousIndex < bindingIndex; previousIndex++)
                {
                    if (bindings[previousIndex].Parameter == binding.Parameter)
                    {
                        throw new ArgumentException(
                            $"Parameter '{binding.Parameter}' has multiple layout bindings.",
                            nameof(bindings));
                    }
                }
            }

            LayoutID = layoutID;
            Stride = stride;
            m_Bindings = Array.AsReadOnly(
                (MaterialParameterLayoutBinding[]) bindings.Clone());
        }

        internal VividMaterialParameterLayoutID LayoutID { get; }

        internal int Stride { get; }

        internal IReadOnlyList<MaterialParameterLayoutBinding> Bindings => m_Bindings;

        internal bool TryGetBinding(
            MaterialRuntimeParameter parameter,
            out MaterialParameterLayoutBinding binding)
        {
            for (int i = 0; i < m_Bindings.Count; i++)
            {
                if (m_Bindings[i].Parameter != parameter)
                    continue;

                binding = m_Bindings[i];
                return true;
            }

            binding = default;
            return false;
        }

        private static int GetValueSize(MaterialLayoutValueType type)
        {
            switch (type)
            {
                case MaterialLayoutValueType.Float:
                case MaterialLayoutValueType.UInt:
                    return sizeof(uint);
                case MaterialLayoutValueType.Float4:
                    return sizeof(float) * 4;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }

    internal sealed class CompiledResourceLayout
    {
        private readonly IReadOnlyList<MaterialResourceLayoutBinding> m_Bindings;

        internal CompiledResourceLayout(
            VividMaterialResourceLayoutID layoutID,
            int recordStride,
            int recordCount,
            MaterialResourceLayoutBinding[] bindings)
        {
            if (recordStride <= 0)
                throw new ArgumentOutOfRangeException(nameof(recordStride));
            if (recordCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(recordCount));
            if (bindings == null)
                throw new ArgumentNullException(nameof(bindings));

            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                MaterialResourceLayoutBinding binding = bindings[bindingIndex];
                if (binding.RecordOffset < 0
                    || binding.RecordOffset >= recordCount
                    || binding.ByteOffset < 0
                    || binding.ByteOffset > recordStride - sizeof(uint))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(bindings),
                        $"Resource '{binding.Resource}' exceeds the resource record layout.");
                }
                for (int previousIndex = 0; previousIndex < bindingIndex; previousIndex++)
                {
                    if (bindings[previousIndex].Resource == binding.Resource)
                    {
                        throw new ArgumentException(
                            $"Resource '{binding.Resource}' has multiple layout bindings.",
                            nameof(bindings));
                    }
                }
            }

            LayoutID = layoutID;
            RecordStride = recordStride;
            RecordCount = recordCount;
            m_Bindings = Array.AsReadOnly(
                (MaterialResourceLayoutBinding[]) bindings.Clone());
        }

        internal VividMaterialResourceLayoutID LayoutID { get; }

        internal int RecordStride { get; }

        internal int RecordCount { get; }

        internal IReadOnlyList<MaterialResourceLayoutBinding> Bindings => m_Bindings;

        internal bool TryGetBinding(
            MaterialTextureResource resource,
            out MaterialResourceLayoutBinding binding)
        {
            for (int i = 0; i < m_Bindings.Count; i++)
            {
                if (m_Bindings[i].Resource != resource)
                    continue;

                binding = m_Bindings[i];
                return true;
            }

            binding = default;
            return false;
        }
    }

    internal sealed class CompiledMaterialLayout
    {
        internal CompiledMaterialLayout(
            MaterialValueRequirements requirements,
            CompiledParameterLayout parameterLayout,
            CompiledResourceLayout resourceLayout)
        {
            Requirements = requirements
                ?? throw new ArgumentNullException(nameof(requirements));
            ParameterLayout = parameterLayout
                ?? throw new ArgumentNullException(nameof(parameterLayout));
            ResourceLayout = resourceLayout
                ?? throw new ArgumentNullException(nameof(resourceLayout));
        }

        internal MaterialValueRequirements Requirements { get; }

        internal CompiledParameterLayout ParameterLayout { get; }

        internal CompiledResourceLayout ResourceLayout { get; }
    }

    internal static class MaterialLayoutLowerer
    {
        internal static CompiledMaterialLayout Compile(
            CompiledCoverageProgram coverageProgram,
            CompiledSurfaceProgram surfaceProgram)
        {
            if (coverageProgram == null)
                throw new ArgumentNullException(nameof(coverageProgram));
            if (surfaceProgram == null)
                throw new ArgumentNullException(nameof(surfaceProgram));

            MaterialValueRequirements requirements = MaterialValueRequirements.Merge(
                coverageProgram.Requirements,
                surfaceProgram.Requirements);
            return new CompiledMaterialLayout(
                requirements,
                LowerParameterLayout(surfaceProgram.ProgramID, requirements.Parameters),
                LowerResourceLayout(surfaceProgram.ProgramID, requirements.TextureResources));
        }

        private static CompiledParameterLayout LowerParameterLayout(
            VividMaterialSurfaceProgramID surfaceProgramID,
            IReadOnlyList<MaterialParameter> parameters)
        {
            switch (surfaceProgramID)
            {
                case VividMaterialSurfaceProgramID.StandardSingleSlab:
                    if (Matches(
                        parameters,
                        MaterialParameter.BaseColor,
                        MaterialParameter.Roughness,
                        MaterialParameter.Metallic,
                        MaterialParameter.AlphaClipThreshold))
                    {
                        return CreateLegacyParameterLayout();
                    }
                    break;
                case VividMaterialSurfaceProgramID.DualSlab:
                    if (Matches(
                        parameters,
                        MaterialParameter.BaseColor,
                        MaterialParameter.TopBaseColor,
                        MaterialParameter.Roughness,
                        MaterialParameter.TopRoughness,
                        MaterialParameter.Metallic,
                        MaterialParameter.TopMetallic,
                        MaterialParameter.LayerWeight,
                        MaterialParameter.AlphaClipThreshold))
                    {
                        return CreateDualSlabParameterLayout();
                    }
                    break;
            }

            throw new NotSupportedException(
                $"Material parameters cannot be lowered for surface program '{surfaceProgramID}'.");
        }

        private static CompiledResourceLayout LowerResourceLayout(
            VividMaterialSurfaceProgramID surfaceProgramID,
            IReadOnlyList<MaterialTextureResource> resources)
        {
            switch (surfaceProgramID)
            {
                case VividMaterialSurfaceProgramID.StandardSingleSlab:
                    if (Matches(resources, MaterialTextureResource.BaseColor))
                        return CreateLegacyResourceLayout();
                    break;
                case VividMaterialSurfaceProgramID.DualSlab:
                    if (Matches(
                        resources,
                        MaterialTextureResource.BaseColor,
                        MaterialTextureResource.TopBaseColor))
                    {
                        return CreateDualSlabResourceLayout();
                    }
                    break;
            }

            throw new NotSupportedException(
                $"Material resources cannot be lowered for surface program '{surfaceProgramID}'.");
        }

        private static CompiledParameterLayout CreateLegacyParameterLayout()
        {
            return new CompiledParameterLayout(
                VividMaterialParameterLayoutID.LegacyMaterialData,
                SizeOf<VividMaterialData>(),
                new[]
                {
                    ParameterBinding<VividMaterialData>(
                        MaterialRuntimeParameter.BaseColor,
                        MaterialLayoutValueType.Float4,
                        nameof(VividMaterialData.AlbedoColor)),
                    ParameterBinding<VividMaterialData>(
                        MaterialRuntimeParameter.BaseTextureTilingOffset,
                        MaterialLayoutValueType.Float4,
                        nameof(VividMaterialData.TextureTilingOffset)),
                    ParameterBinding<VividMaterialData>(
                        MaterialRuntimeParameter.Emission,
                        MaterialLayoutValueType.Float4,
                        nameof(VividMaterialData.Emission)),
                    ParameterBinding<VividMaterialData>(
                        MaterialRuntimeParameter.BaseMetallicSmoothnessRemap,
                        MaterialLayoutValueType.Float4,
                        nameof(VividMaterialData.MetallicSmoothnessRemap)),
                    ParameterBinding<VividMaterialData>(
                        MaterialRuntimeParameter.BaseAmbientOcclusionRemap,
                        MaterialLayoutValueType.Float4,
                        nameof(VividMaterialData.AmbientOcclusionRemap)),
                    ParameterBinding<VividMaterialData>(
                        MaterialRuntimeParameter.BaseNormalsStrength,
                        MaterialLayoutValueType.Float,
                        nameof(VividMaterialData.NormalsStrength)),
                    ParameterBinding<VividMaterialData>(
                        MaterialRuntimeParameter.Roughness,
                        MaterialLayoutValueType.Float,
                        nameof(VividMaterialData.Roughness)),
                    ParameterBinding<VividMaterialData>(
                        MaterialRuntimeParameter.Metallic,
                        MaterialLayoutValueType.Float,
                        nameof(VividMaterialData.Metallic)),
                    ParameterBinding<VividMaterialData>(
                        MaterialRuntimeParameter.BaseMaskMode,
                        MaterialLayoutValueType.UInt,
                        nameof(VividMaterialData.Padding0)),
                    ParameterBinding<VividMaterialData>(
                        MaterialRuntimeParameter.AlphaClipThreshold,
                        MaterialLayoutValueType.Float,
                        nameof(VividMaterialData.AlphaClipThreshold)),
                });
        }

        private static CompiledParameterLayout CreateDualSlabParameterLayout()
        {
            return new CompiledParameterLayout(
                VividMaterialParameterLayoutID.DualSlabMaterialData,
                SizeOf<VividDualSlabMaterialData>(),
                new[]
                {
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.BaseColor,
                        MaterialLayoutValueType.Float4,
                        nameof(VividDualSlabMaterialData.BaseAlbedoColor)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.BaseTextureTilingOffset,
                        MaterialLayoutValueType.Float4,
                        nameof(VividDualSlabMaterialData.BaseTextureTilingOffset)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.BaseMetallicSmoothnessRemap,
                        MaterialLayoutValueType.Float4,
                        nameof(VividDualSlabMaterialData.BaseMetallicSmoothnessRemap)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.BaseAmbientOcclusionRemap,
                        MaterialLayoutValueType.Float4,
                        nameof(VividDualSlabMaterialData.BaseAmbientOcclusionRemap)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.BaseNormalsStrength,
                        MaterialLayoutValueType.Float,
                        nameof(VividDualSlabMaterialData.BaseNormalsStrength)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.Roughness,
                        MaterialLayoutValueType.Float,
                        nameof(VividDualSlabMaterialData.BaseRoughness)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.Metallic,
                        MaterialLayoutValueType.Float,
                        nameof(VividDualSlabMaterialData.BaseMetallic)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.BaseMaskMode,
                        MaterialLayoutValueType.UInt,
                        nameof(VividDualSlabMaterialData.BaseMaskMode)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.TopBaseColor,
                        MaterialLayoutValueType.Float4,
                        nameof(VividDualSlabMaterialData.TopAlbedoColor)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.TopTextureTilingOffset,
                        MaterialLayoutValueType.Float4,
                        nameof(VividDualSlabMaterialData.TopTextureTilingOffset)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.TopMetallicSmoothnessRemap,
                        MaterialLayoutValueType.Float4,
                        nameof(VividDualSlabMaterialData.TopMetallicSmoothnessRemap)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.TopAmbientOcclusionRemap,
                        MaterialLayoutValueType.Float4,
                        nameof(VividDualSlabMaterialData.TopAmbientOcclusionRemap)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.TopNormalsStrength,
                        MaterialLayoutValueType.Float,
                        nameof(VividDualSlabMaterialData.TopNormalsStrength)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.TopRoughness,
                        MaterialLayoutValueType.Float,
                        nameof(VividDualSlabMaterialData.TopRoughness)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.TopMetallic,
                        MaterialLayoutValueType.Float,
                        nameof(VividDualSlabMaterialData.TopMetallic)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.TopMaskMode,
                        MaterialLayoutValueType.UInt,
                        nameof(VividDualSlabMaterialData.TopMaskMode)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.Emission,
                        MaterialLayoutValueType.Float4,
                        nameof(VividDualSlabMaterialData.Emission)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.LayerOperator,
                        MaterialLayoutValueType.UInt,
                        nameof(VividDualSlabMaterialData.LayerOperator)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.LayerWeight,
                        MaterialLayoutValueType.Float,
                        nameof(VividDualSlabMaterialData.LayerWeight)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialRuntimeParameter.AlphaClipThreshold,
                        MaterialLayoutValueType.Float,
                        nameof(VividDualSlabMaterialData.AlphaClipThreshold)),
                });
        }

        private static CompiledResourceLayout CreateLegacyResourceLayout()
        {
            return new CompiledResourceLayout(
                VividMaterialResourceLayoutID.LegacySurfaceBinding,
                SizeOf<VividSurfaceBindingData>(),
                recordCount: 1,
                bindings: new[]
                {
                    ResourceBinding(
                        MaterialTextureResource.BaseColor,
                        recordOffset: 0,
                        nameof(VividSurfaceBindingData.BaseColorResource)),
                    ResourceBinding(
                        MaterialTextureResource.BaseNormal,
                        recordOffset: 0,
                        nameof(VividSurfaceBindingData.NormalResource)),
                    ResourceBinding(
                        MaterialTextureResource.BaseMask,
                        recordOffset: 0,
                        nameof(VividSurfaceBindingData.MaskResource)),
                });
        }

        private static CompiledResourceLayout CreateDualSlabResourceLayout()
        {
            return new CompiledResourceLayout(
                VividMaterialResourceLayoutID.DualSurfaceBinding,
                SizeOf<VividSurfaceBindingData>(),
                recordCount: 2,
                bindings: new[]
                {
                    ResourceBinding(
                        MaterialTextureResource.BaseColor,
                        recordOffset: 0,
                        nameof(VividSurfaceBindingData.BaseColorResource)),
                    ResourceBinding(
                        MaterialTextureResource.BaseNormal,
                        recordOffset: 0,
                        nameof(VividSurfaceBindingData.NormalResource)),
                    ResourceBinding(
                        MaterialTextureResource.BaseMask,
                        recordOffset: 0,
                        nameof(VividSurfaceBindingData.MaskResource)),
                    ResourceBinding(
                        MaterialTextureResource.TopBaseColor,
                        recordOffset: 1,
                        nameof(VividSurfaceBindingData.BaseColorResource)),
                    ResourceBinding(
                        MaterialTextureResource.TopNormal,
                        recordOffset: 1,
                        nameof(VividSurfaceBindingData.NormalResource)),
                    ResourceBinding(
                        MaterialTextureResource.TopMask,
                        recordOffset: 1,
                        nameof(VividSurfaceBindingData.MaskResource)),
                });
        }

        private static MaterialParameterLayoutBinding ParameterBinding<T>(
            MaterialRuntimeParameter parameter,
            MaterialLayoutValueType type,
            string fieldName)
        {
            return new MaterialParameterLayoutBinding(
                parameter,
                type,
                OffsetOf<T>(fieldName));
        }

        private static MaterialResourceLayoutBinding ResourceBinding(
            MaterialTextureResource resource,
            int recordOffset,
            string fieldName)
        {
            return new MaterialResourceLayoutBinding(
                resource,
                recordOffset,
                OffsetOf<VividSurfaceBindingData>(fieldName));
        }

        private static bool Matches<T>(IReadOnlyList<T> values, params T[] expected)
        {
            if (values.Count != expected.Length)
                return false;

            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < expected.Length; i++)
            {
                bool found = false;
                for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
                {
                    if (!comparer.Equals(values[valueIndex], expected[i]))
                        continue;

                    found = true;
                    break;
                }
                if (!found)
                    return false;
            }
            return true;
        }

        private static int SizeOf<T>()
        {
            return Marshal.SizeOf(typeof(T));
        }

        private static int OffsetOf<T>(string fieldName)
        {
            return Marshal.OffsetOf(typeof(T), fieldName).ToInt32();
        }
    }

    internal readonly struct MaterialStageCost
    {
        internal MaterialStageCost(
            int valueNodeCount,
            int textureSampleCount,
            int derivativeCount,
            int arithmeticNodeCount,
            int parameterCount,
            int textureResourceCount,
            int externalInputCount)
        {
            ValueNodeCount = valueNodeCount;
            TextureSampleCount = textureSampleCount;
            DerivativeCount = derivativeCount;
            ArithmeticNodeCount = arithmeticNodeCount;
            ParameterCount = parameterCount;
            TextureResourceCount = textureResourceCount;
            ExternalInputCount = externalInputCount;
        }

        internal int ValueNodeCount { get; }

        internal int TextureSampleCount { get; }

        internal int DerivativeCount { get; }

        internal int ArithmeticNodeCount { get; }

        internal int ParameterCount { get; }

        internal int TextureResourceCount { get; }

        internal int ExternalInputCount { get; }
    }

    internal readonly struct MaterialProgramCost
    {
        internal MaterialProgramCost(
            in MaterialStageCost coverage,
            in MaterialStageCost surface,
            in MaterialStageCost combined,
            int closureCount,
            int operatorCount,
            int parameterBytes,
            int resourceBindingRecords)
        {
            Coverage = coverage;
            Surface = surface;
            Combined = combined;
            ClosureCount = closureCount;
            OperatorCount = operatorCount;
            ParameterBytes = parameterBytes;
            ResourceBindingRecords = resourceBindingRecords;
        }

        internal MaterialStageCost Coverage { get; }

        internal MaterialStageCost Surface { get; }

        internal MaterialStageCost Combined { get; }

        internal int ClosureCount { get; }

        internal int OperatorCount { get; }

        internal int ParameterBytes { get; }

        internal int ResourceBindingRecords { get; }
    }

    internal readonly struct MaterialProgramCostBudget
    {
        internal MaterialProgramCostBudget(
            int maxCombinedValueNodes,
            int maxTextureSamples,
            int maxParameters,
            int maxTextureResources,
            int maxClosures,
            int maxOperators,
            int maxParameterBytes,
            int maxResourceBindingRecords)
        {
            if (maxCombinedValueNodes < 0)
                throw new ArgumentOutOfRangeException(nameof(maxCombinedValueNodes));
            if (maxTextureSamples < 0)
                throw new ArgumentOutOfRangeException(nameof(maxTextureSamples));
            if (maxParameters < 0)
                throw new ArgumentOutOfRangeException(nameof(maxParameters));
            if (maxTextureResources < 0)
                throw new ArgumentOutOfRangeException(nameof(maxTextureResources));
            if (maxClosures < 0)
                throw new ArgumentOutOfRangeException(nameof(maxClosures));
            if (maxOperators < 0)
                throw new ArgumentOutOfRangeException(nameof(maxOperators));
            if (maxParameterBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maxParameterBytes));
            if (maxResourceBindingRecords < 0)
                throw new ArgumentOutOfRangeException(nameof(maxResourceBindingRecords));

            MaxCombinedValueNodes = maxCombinedValueNodes;
            MaxTextureSamples = maxTextureSamples;
            MaxParameters = maxParameters;
            MaxTextureResources = maxTextureResources;
            MaxClosures = maxClosures;
            MaxOperators = maxOperators;
            MaxParameterBytes = maxParameterBytes;
            MaxResourceBindingRecords = maxResourceBindingRecords;
        }

        internal static MaterialProgramCostBudget Prototype =>
            new MaterialProgramCostBudget(
                maxCombinedValueNodes: 24,
                maxTextureSamples: 2,
                maxParameters: 8,
                maxTextureResources: 2,
                maxClosures: 2,
                maxOperators: 1,
                maxParameterBytes: 192,
                maxResourceBindingRecords: 2);

        internal int MaxCombinedValueNodes { get; }

        internal int MaxTextureSamples { get; }

        internal int MaxParameters { get; }

        internal int MaxTextureResources { get; }

        internal int MaxClosures { get; }

        internal int MaxOperators { get; }

        internal int MaxParameterBytes { get; }

        internal int MaxResourceBindingRecords { get; }
    }

    internal enum MaterialProgramDiagnosticSeverity
    {
        Info,
        Warning,
        Error,
    }

    internal readonly struct MaterialProgramDiagnostic
    {
        internal MaterialProgramDiagnostic(
            MaterialProgramDiagnosticSeverity severity,
            string code,
            string message)
        {
            Severity = severity;
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        internal MaterialProgramDiagnosticSeverity Severity { get; }

        internal string Code { get; }

        internal string Message { get; }
    }

    internal sealed class MaterialProgramDiagnostics
    {
        private readonly IReadOnlyList<MaterialProgramDiagnostic> m_Entries;
        private readonly string m_DebugDump;

        internal MaterialProgramDiagnostics(
            in MaterialProgramCost cost,
            in MaterialProgramCostBudget budget,
            MaterialProgramDiagnostic[] entries)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            Cost = cost;
            Budget = budget;
            m_Entries = Array.AsReadOnly(
                (MaterialProgramDiagnostic[]) entries.Clone());
            IsWithinBudget = true;
            for (int i = 0; i < m_Entries.Count; i++)
            {
                if (m_Entries[i].Severity != MaterialProgramDiagnosticSeverity.Error)
                    continue;

                IsWithinBudget = false;
                break;
            }
            m_DebugDump = BuildDebugDump();
        }

        internal MaterialProgramCost Cost { get; }

        internal MaterialProgramCostBudget Budget { get; }

        internal IReadOnlyList<MaterialProgramDiagnostic> Entries => m_Entries;

        internal bool IsWithinBudget { get; }

        internal string GetDebugDump()
        {
            return m_DebugDump;
        }

        private string BuildDebugDump()
        {
            var builder = new StringBuilder();
            builder.AppendLine("material_program_diagnostics");
            builder.AppendLine("cost_model=typed_ir_structural_v1");
            AppendStageCost(builder, "coverage", Cost.Coverage);
            AppendStageCost(builder, "surface", Cost.Surface);
            AppendStageCost(builder, "combined", Cost.Combined);
            builder.Append("topology closures=").Append(Cost.ClosureCount)
                .Append(" operators=").Append(Cost.OperatorCount).AppendLine();
            builder.Append("layout parameter_bytes=").Append(Cost.ParameterBytes)
                .Append(" resource_records=").Append(Cost.ResourceBindingRecords)
                .AppendLine();
            builder.Append("budget combined_nodes=").Append(Budget.MaxCombinedValueNodes)
                .Append(" texture_samples=").Append(Budget.MaxTextureSamples)
                .Append(" parameters=").Append(Budget.MaxParameters)
                .Append(" texture_resources=").Append(Budget.MaxTextureResources)
                .Append(" closures=").Append(Budget.MaxClosures)
                .Append(" operators=").Append(Budget.MaxOperators)
                .Append(" parameter_bytes=").Append(Budget.MaxParameterBytes)
                .Append(" resource_records=").Append(Budget.MaxResourceBindingRecords)
                .AppendLine();
            builder.Append("status=").AppendLine(IsWithinBudget ? "ok" : "over_budget");
            builder.AppendLine("diagnostics:");
            for (int i = 0; i < m_Entries.Count; i++)
            {
                MaterialProgramDiagnostic entry = m_Entries[i];
                builder.Append("  ").Append(entry.Severity.ToString().ToLowerInvariant())
                    .Append(' ').Append(entry.Code).Append(": ")
                    .AppendLine(entry.Message);
            }
            return builder.ToString();
        }

        private static void AppendStageCost(
            StringBuilder builder,
            string stage,
            in MaterialStageCost cost)
        {
            builder.Append(stage).Append(" nodes=").Append(cost.ValueNodeCount)
                .Append(" texture_samples=").Append(cost.TextureSampleCount)
                .Append(" derivatives=").Append(cost.DerivativeCount)
                .Append(" arithmetic_nodes=").Append(cost.ArithmeticNodeCount)
                .Append(" parameters=").Append(cost.ParameterCount)
                .Append(" texture_resources=").Append(cost.TextureResourceCount)
                .Append(" external_inputs=").Append(cost.ExternalInputCount)
                .AppendLine();
        }
    }

    internal static class MaterialProgramCostAnalyzer
    {
        internal static MaterialProgramCost Analyze(
            MaterialIRModule module,
            CompiledCoverageProgram coverageProgram,
            CompiledSurfaceProgram surfaceProgram,
            CompiledMaterialLayout materialLayout)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));
            if (coverageProgram == null)
                throw new ArgumentNullException(nameof(coverageProgram));
            if (surfaceProgram == null)
                throw new ArgumentNullException(nameof(surfaceProgram));
            if (materialLayout == null)
                throw new ArgumentNullException(nameof(materialLayout));

            return new MaterialProgramCost(
                AnalyzeSlice(coverageProgram.ValueSlice),
                AnalyzeSlice(surfaceProgram.ValueSlice),
                AnalyzeCombined(
                    module.Values,
                    coverageProgram.ValueSlice,
                    surfaceProgram.ValueSlice),
                module.Topology.ClosureCount,
                module.Topology.OperatorCount,
                materialLayout.ParameterLayout.Stride,
                materialLayout.ResourceLayout.RecordCount);
        }

        private static MaterialStageCost AnalyzeSlice(MaterialValueSlice slice)
        {
            return AnalyzeNodes(slice.Values, slice.NodeIndices);
        }

        private static MaterialStageCost AnalyzeCombined(
            MaterialValueIR values,
            MaterialValueSlice coverage,
            MaterialValueSlice surface)
        {
            var included = new bool[values.NodeCount];
            Include(included, coverage.NodeIndices);
            Include(included, surface.NodeIndices);
            var nodeIndices = new List<int>();
            for (int i = 0; i < included.Length; i++)
            {
                if (included[i])
                    nodeIndices.Add(i);
            }
            return AnalyzeNodes(values, nodeIndices);
        }

        private static void Include(bool[] included, IReadOnlyList<int> nodeIndices)
        {
            for (int i = 0; i < nodeIndices.Count; i++)
                included[nodeIndices[i]] = true;
        }

        private static MaterialStageCost AnalyzeNodes(
            MaterialValueIR values,
            IReadOnlyList<int> nodeIndices)
        {
            int textureSamples = 0;
            int derivatives = 0;
            int arithmeticNodes = 0;
            int parameters = 0;
            int textureResources = 0;
            int externalInputs = 0;
            for (int i = 0; i < nodeIndices.Count; i++)
            {
                switch (values.Nodes[nodeIndices[i]].Opcode)
                {
                    case MaterialValueOpcode.TextureSampleGrad:
                        textureSamples++;
                        break;
                    case MaterialValueOpcode.Ddx:
                    case MaterialValueOpcode.Ddy:
                        derivatives++;
                        break;
                    case MaterialValueOpcode.Add:
                    case MaterialValueOpcode.Multiply:
                    case MaterialValueOpcode.Lerp:
                    case MaterialValueOpcode.Select:
                        arithmeticNodes++;
                        break;
                    case MaterialValueOpcode.Parameter:
                        parameters++;
                        break;
                    case MaterialValueOpcode.TextureResource:
                        textureResources++;
                        break;
                    case MaterialValueOpcode.ExternalInput:
                        externalInputs++;
                        break;
                }
            }

            return new MaterialStageCost(
                nodeIndices.Count,
                textureSamples,
                derivatives,
                arithmeticNodes,
                parameters,
                textureResources,
                externalInputs);
        }
    }

    internal static class MaterialProgramDiagnosticsBuilder
    {
        internal static MaterialProgramDiagnostics Build(
            MaterialIRModule module,
            CompiledCoverageProgram coverageProgram,
            CompiledSurfaceProgram surfaceProgram,
            CompiledMaterialLayout materialLayout,
            in MaterialProgramCostBudget budget)
        {
            MaterialProgramCost cost = MaterialProgramCostAnalyzer.Analyze(
                module,
                coverageProgram,
                surfaceProgram,
                materialLayout);
            var entries = new List<MaterialProgramDiagnostic>();
            AddExceeded(
                entries,
                "MPC1001",
                "combined value nodes",
                cost.Combined.ValueNodeCount,
                budget.MaxCombinedValueNodes);
            AddExceeded(
                entries,
                "MPC1002",
                "combined texture samples",
                cost.Combined.TextureSampleCount,
                budget.MaxTextureSamples);
            AddExceeded(
                entries,
                "MPC1003",
                "combined parameters",
                cost.Combined.ParameterCount,
                budget.MaxParameters);
            AddExceeded(
                entries,
                "MPC1004",
                "combined texture resources",
                cost.Combined.TextureResourceCount,
                budget.MaxTextureResources);
            AddExceeded(
                entries,
                "MPC1005",
                "closures",
                cost.ClosureCount,
                budget.MaxClosures);
            AddExceeded(
                entries,
                "MPC1006",
                "closure operators",
                cost.OperatorCount,
                budget.MaxOperators);
            AddExceeded(
                entries,
                "MPC1007",
                "parameter bytes",
                cost.ParameterBytes,
                budget.MaxParameterBytes);
            AddExceeded(
                entries,
                "MPC1008",
                "resource binding records",
                cost.ResourceBindingRecords,
                budget.MaxResourceBindingRecords);
            entries.Add(new MaterialProgramDiagnostic(
                MaterialProgramDiagnosticSeverity.Info,
                "MPC0001",
                "V1 counts typed MaterialValueIR structure and runtime ABI occupancy; "
                + "layout-driven tiling/remap and optional Normal/Mask/Emission HLSL work are not represented."));
            return new MaterialProgramDiagnostics(cost, budget, entries.ToArray());
        }

        private static void AddExceeded(
            List<MaterialProgramDiagnostic> entries,
            string code,
            string name,
            int actual,
            int maximum)
        {
            if (actual <= maximum)
                return;

            entries.Add(new MaterialProgramDiagnostic(
                MaterialProgramDiagnosticSeverity.Error,
                code,
                $"{name} cost {actual} exceeds prototype budget {maximum}."));
        }
    }

    internal sealed class CompiledMaterialProgram
    {
        private CompiledMaterialProgram(
            MaterialIRModule module,
            CompiledCoverageProgram coverageProgram,
            CompiledSurfaceProgram surfaceProgram,
            CompiledMaterialLayout materialLayout,
            MaterialProgramDiagnostics diagnostics,
            VividMaterialProgramID programID,
            in VividMaterialProgramData runtimeData)
        {
            Module = module;
            CoverageProgram = coverageProgram;
            SurfaceProgram = surfaceProgram;
            MaterialLayout = materialLayout;
            Diagnostics = diagnostics;
            ProgramID = programID;
            RuntimeData = runtimeData;
        }

        internal MaterialIRModule Module { get; }

        internal CompiledCoverageProgram CoverageProgram { get; }

        internal CompiledSurfaceProgram SurfaceProgram { get; }

        internal CompiledMaterialLayout MaterialLayout { get; }

        internal MaterialProgramDiagnostics Diagnostics { get; }

        internal VividMaterialProgramID ProgramID { get; }

        internal VividMaterialProgramData RuntimeData { get; }

        internal static CompiledMaterialProgram Compile(
            MaterialIRModule module,
            uint programVersion)
        {
            MaterialProgramCostBudget budget = MaterialProgramCostBudget.Prototype;
            return Compile(module, programVersion, budget);
        }

        internal static CompiledMaterialProgram Compile(
            MaterialIRModule module,
            uint programVersion,
            in MaterialProgramCostBudget costBudget)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            ClosureTopology topology = module.Topology;
            if (!topology.IsWithinBudget)
                throw new InvalidOperationException("Closure topology exceeds its compilation budget.");
            CompiledCoverageProgram coverageProgram = CoverageProgramLowerer.Compile(module);
            CompiledSurfaceProgram surfaceProgram = SurfaceProgramMatcher.Compile(module);
            CompiledMaterialLayout materialLayout = MaterialLayoutLowerer.Compile(
                coverageProgram,
                surfaceProgram);
            MaterialProgramDiagnostics diagnostics = MaterialProgramDiagnosticsBuilder.Build(
                module,
                coverageProgram,
                surfaceProgram,
                materialLayout,
                costBudget);
            if (!diagnostics.IsWithinBudget)
                throw new InvalidOperationException(diagnostics.GetDebugDump());

            VividMaterialProgramID programID = ResolveProgramID(
                surfaceProgram.ProgramID,
                topology);

            MaterialFeatureMask features = module.MaterialFeatures;
            VividMaterialProgramCapabilities capabilities =
                VividMaterialProgramCapabilities.LegacyGBufferExport;
            if ((features & MaterialFeatureMask.AlphaClip) != 0)
                capabilities |= VividMaterialProgramCapabilities.AlphaClip;
            if ((features & MaterialFeatureMask.Unlit) != 0)
                capabilities |= VividMaterialProgramCapabilities.Unlit;

            var runtimeData = new VividMaterialProgramData
            {
                Version = programVersion,
                CoverageProgramID = coverageProgram.ProgramID,
                SurfaceProgramID = surfaceProgram.ProgramID,
                TransportProgramID = VividMaterialTransportProgramID.None,
                ParameterLayoutID = materialLayout.ParameterLayout.LayoutID,
                ResourceLayoutID = materialLayout.ResourceLayout.LayoutID,
                CapabilityFlags = capabilities,
                ExecutionClass = VividMaterialExecutionClass.VisibilityDeferred,
            };
            return new CompiledMaterialProgram(
                module,
                coverageProgram,
                surfaceProgram,
                materialLayout,
                diagnostics,
                programID,
                runtimeData);
        }

        private static VividMaterialProgramID ResolveProgramID(
            VividMaterialSurfaceProgramID surfaceProgramID,
            ClosureTopology topology)
        {
            switch (surfaceProgramID)
            {
                case VividMaterialSurfaceProgramID.StandardSingleSlab:
                    return VividMaterialProgramID.StandardSingleSlab;
                case VividMaterialSurfaceProgramID.DualSlab:
                    switch (topology.Operators[0].Kind)
                    {
                        case ClosureOperatorKind.HorizontalMix:
                            return VividMaterialProgramID.DualSlabHorizontalMix;
                        case ClosureOperatorKind.VerticalLayer:
                            return VividMaterialProgramID.DualSlabVerticalLayer;
                        default:
                            throw new NotSupportedException(
                                $"Closure operator '{topology.Operators[0].Kind}' has no material program ABI.");
                    }
                default:
                    throw new NotSupportedException(
                        $"Surface program '{surfaceProgramID}' has no material program ABI.");
            }
        }
    }

    internal static class MaterialProgramPrototypeBuilder
    {
        private const ClosureFeatureMask SupportedSlabFeatures =
            ClosureFeatureMask.BaseColorTexture
            | ClosureFeatureMask.NormalTexture
            | ClosureFeatureMask.MaskTexture;

        private const MaterialFeatureMask SupportedMaterialFeatures =
            MaterialFeatureMask.AlphaClip
            | MaterialFeatureMask.Emission
            | MaterialFeatureMask.Unlit;

        internal static CompiledMaterialProgram BuildStandardSingleSlab(uint programVersion)
        {
            var valueIR = new MaterialValueIR();
            MaterialValue baseColor = BuildSampledBaseColor(
                valueIR,
                MaterialTextureResource.BaseColor,
                MaterialParameter.BaseColor);
            MaterialValue roughness = valueIR.Parameter(MaterialParameter.Roughness);
            MaterialValue metallic = valueIR.Parameter(MaterialParameter.Metallic);
            MaterialValue alphaClipThreshold =
                valueIR.Parameter(MaterialParameter.AlphaClipThreshold);
            ClosureNormalBasis[] normalBases = BuildGeometryNormalBasis(valueIR);
            var slabs = new[]
            {
                new ClosureSlab(
                    baseColor,
                    roughness,
                    metallic,
                    normalBasisIndex: 0,
                    features: SupportedSlabFeatures,
                    isTop: true,
                    isBottom: true),
            };
            var topology = new ClosureTopology(
                valueIR,
                normalBases,
                slabs,
                Array.Empty<ClosureOperator>(),
                ClosureTopologyBudget.Prototype);
            var module = new MaterialIRModule(
                valueIR,
                new MaterialOutputRoots(baseColor, alphaClipThreshold),
                topology,
                SupportedMaterialFeatures);
            return CompiledMaterialProgram.Compile(module, programVersion);
        }

        internal static CompiledMaterialProgram BuildDualSlab(
            uint programVersion,
            VividDualSlabOperator layerOperator)
        {
            var valueIR = new MaterialValueIR();
            MaterialValue baseColor = BuildSampledBaseColor(
                valueIR,
                MaterialTextureResource.BaseColor,
                MaterialParameter.BaseColor);
            MaterialValue topBaseColor = BuildSampledBaseColor(
                valueIR,
                MaterialTextureResource.TopBaseColor,
                MaterialParameter.TopBaseColor);
            MaterialValue roughness = valueIR.Parameter(MaterialParameter.Roughness);
            MaterialValue topRoughness = valueIR.Parameter(MaterialParameter.TopRoughness);
            MaterialValue metallic = valueIR.Parameter(MaterialParameter.Metallic);
            MaterialValue topMetallic = valueIR.Parameter(MaterialParameter.TopMetallic);
            MaterialValue layerWeight = valueIR.Parameter(MaterialParameter.LayerWeight);
            MaterialValue alphaClipThreshold =
                valueIR.Parameter(MaterialParameter.AlphaClipThreshold);
            ClosureNormalBasis[] normalBases = BuildGeometryNormalBasis(valueIR);
            var slabs = new[]
            {
                new ClosureSlab(
                    baseColor,
                    roughness,
                    metallic,
                    normalBasisIndex: 0,
                    features: SupportedSlabFeatures,
                    isTop: false,
                    isBottom: true),
                new ClosureSlab(
                    topBaseColor,
                    topRoughness,
                    topMetallic,
                    normalBasisIndex: 0,
                    features: SupportedSlabFeatures,
                    isTop: true,
                    isBottom: false),
            };
            var operators = new[]
            {
                new ClosureOperator(
                    ToClosureOperator(layerOperator),
                    backgroundSlabIndex: 0,
                    foregroundSlabIndex: 1,
                    weight: layerWeight),
            };
            var topology = new ClosureTopology(
                valueIR,
                normalBases,
                slabs,
                operators,
                ClosureTopologyBudget.Prototype);
            var module = new MaterialIRModule(
                valueIR,
                new MaterialOutputRoots(baseColor, alphaClipThreshold),
                topology,
                SupportedMaterialFeatures);
            return CompiledMaterialProgram.Compile(module, programVersion);
        }

        private static MaterialValue BuildSampledBaseColor(
            MaterialValueIR valueIR,
            MaterialTextureResource textureResource,
            MaterialParameter colorParameter)
        {
            MaterialValue uv = valueIR.ExternalInput(MaterialExternalInput.UV0);
            MaterialValue texture = valueIR.TextureResource(textureResource);
            MaterialValue sample = valueIR.TextureSampleGrad(
                texture,
                uv,
                valueIR.Ddx(uv),
                valueIR.Ddy(uv));
            return valueIR.Multiply(sample, valueIR.Parameter(colorParameter));
        }

        private static ClosureNormalBasis[] BuildGeometryNormalBasis(MaterialValueIR valueIR)
        {
            return new[]
            {
                new ClosureNormalBasis(
                    valueIR.ExternalInput(MaterialExternalInput.GeometryNormalWS),
                    valueIR.ExternalInput(MaterialExternalInput.GeometryTangentWS)),
            };
        }

        private static ClosureOperatorKind ToClosureOperator(VividDualSlabOperator layerOperator)
        {
            switch (layerOperator)
            {
                case VividDualSlabOperator.HorizontalMix:
                    return ClosureOperatorKind.HorizontalMix;
                case VividDualSlabOperator.VerticalLayer:
                    return ClosureOperatorKind.VerticalLayer;
                default:
                    throw new ArgumentOutOfRangeException(nameof(layerOperator), layerOperator, null);
            }
        }
    }
}
