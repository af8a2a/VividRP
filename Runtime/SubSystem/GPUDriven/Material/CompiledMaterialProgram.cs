using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class MaterialValueRequirements
    {
        private MaterialValueRequirements(
            IReadOnlyList<MaterialParameterDeclaration> parameterDeclarations,
            IReadOnlyList<MaterialResourceDeclaration> resourceDeclarations,
            IReadOnlyList<MaterialParameter> nativeParameters,
            IReadOnlyList<MaterialTextureResource> nativeTextureResources,
            IReadOnlyList<MaterialExternalInput> externalInputs)
        {
            ParameterDeclarations = parameterDeclarations;
            ResourceDeclarations = resourceDeclarations;
            Parameters = nativeParameters;
            TextureResources = nativeTextureResources;
            ExternalInputs = externalInputs;
        }

        internal IReadOnlyList<MaterialParameterDeclaration> ParameterDeclarations { get; }

        internal IReadOnlyList<MaterialResourceDeclaration> ResourceDeclarations { get; }

        // Native Template compatibility views. Generic backends consume declarations.
        internal IReadOnlyList<MaterialParameter> Parameters { get; }

        internal IReadOnlyList<MaterialTextureResource> TextureResources { get; }

        internal IReadOnlyList<MaterialExternalInput> ExternalInputs { get; }

        internal static MaterialValueRequirements Collect(MaterialValueSlice valueSlice)
        {
            if (valueSlice == null)
                throw new ArgumentNullException(nameof(valueSlice));

            var parameterDeclarations = new List<MaterialParameterDeclaration>();
            var resourceDeclarations = new List<MaterialResourceDeclaration>();
            var externalInputs = new List<MaterialExternalInput>();
            for (int i = 0; i < valueSlice.NodeIndices.Count; i++)
            {
                MaterialValueNode node = valueSlice.Values.Nodes[valueSlice.NodeIndices[i]];
                switch (node.Opcode)
                {
                    case MaterialValueOpcode.Parameter:
                        if (!valueSlice.Values.TryGetParameterDeclaration(
                                node.Semantic,
                                out MaterialParameterDeclaration parameter))
                        {
                            throw new InvalidOperationException(
                                "Verified material IR contains an invalid parameter declaration.");
                        }
                        AddUnique(parameterDeclarations, parameter);
                        break;
                    case MaterialValueOpcode.TextureResource:
                        if (!valueSlice.Values.TryGetResourceDeclaration(
                                node.Semantic,
                                out MaterialResourceDeclaration resource))
                        {
                            throw new InvalidOperationException(
                                "Verified material IR contains an invalid resource declaration.");
                        }
                        AddUnique(resourceDeclarations, resource);
                        break;
                    case MaterialValueOpcode.ExternalInput:
                        AddUnique(externalInputs, (MaterialExternalInput) node.Semantic);
                        break;
                }
            }

            SortDeclarations(parameterDeclarations, resourceDeclarations);
            externalInputs.Sort((left, right) => ((int) left).CompareTo((int) right));
            return Create(parameterDeclarations, resourceDeclarations, externalInputs);
        }

        internal static MaterialValueRequirements Merge(
            params MaterialValueRequirements[] requirements)
        {
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));

            var parameterDeclarations = new List<MaterialParameterDeclaration>();
            var resourceDeclarations = new List<MaterialResourceDeclaration>();
            var externalInputs = new List<MaterialExternalInput>();
            for (int i = 0; i < requirements.Length; i++)
            {
                MaterialValueRequirements source = requirements[i]
                    ?? throw new ArgumentException(
                        "Material value requirements cannot contain null entries.",
                        nameof(requirements));
                AddUnique(parameterDeclarations, source.ParameterDeclarations);
                AddUnique(resourceDeclarations, source.ResourceDeclarations);
                AddUnique(externalInputs, source.ExternalInputs);
            }

            SortDeclarations(parameterDeclarations, resourceDeclarations);
            externalInputs.Sort((left, right) => ((int) left).CompareTo((int) right));
            return Create(parameterDeclarations, resourceDeclarations, externalInputs);
        }

        private static MaterialValueRequirements Create(
            List<MaterialParameterDeclaration> parameterDeclarations,
            List<MaterialResourceDeclaration> resourceDeclarations,
            List<MaterialExternalInput> externalInputs)
        {
            var nativeParameters = new List<MaterialParameter>();
            for (int i = 0; i < parameterDeclarations.Count; i++)
            {
                if (!MaterialNativeTemplateDeclarationAdapter.TryGetParameter(
                        parameterDeclarations[i],
                        out MaterialParameter parameter))
                {
                    throw new NotSupportedException(
                        $"Material parameter '{parameterDeclarations[i].Symbol}' has no Native Template ABI binding.");
                }
                nativeParameters.Add(parameter);
            }

            var nativeTextureResources = new List<MaterialTextureResource>();
            for (int i = 0; i < resourceDeclarations.Count; i++)
            {
                if (!MaterialNativeTemplateDeclarationAdapter.TryGetTexture(
                        resourceDeclarations[i],
                        out MaterialTextureResource resource))
                {
                    throw new NotSupportedException(
                        $"Material resource '{resourceDeclarations[i].Symbol}' has no Native Template ABI binding.");
                }
                nativeTextureResources.Add(resource);
            }

            nativeParameters.Sort((left, right) => ((int) left).CompareTo((int) right));
            nativeTextureResources.Sort((left, right) => ((int) left).CompareTo((int) right));
            return new MaterialValueRequirements(
                parameterDeclarations.AsReadOnly(),
                resourceDeclarations.AsReadOnly(),
                nativeParameters.AsReadOnly(),
                nativeTextureResources.AsReadOnly(),
                externalInputs.AsReadOnly());
        }

        private static void SortDeclarations(
            List<MaterialParameterDeclaration> parameters,
            List<MaterialResourceDeclaration> resources)
        {
            parameters.Sort((left, right) =>
            {
                int result = string.CompareOrdinal(left.Symbol, right.Symbol);
                return result != 0
                    ? result
                    : ((int) left.Type).CompareTo((int) right.Type);
            });
            resources.Sort((left, right) =>
            {
                int result = string.CompareOrdinal(left.Symbol, right.Symbol);
                return result != 0
                    ? result
                    : ((int) left.Type).CompareTo((int) right.Type);
            });
        }

        private static void AddUnique<T>(List<T> destination, IReadOnlyList<T> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (!destination.Contains(source[i]))
                    destination.Add(source[i]);
            }
        }

        private static void AddUnique<T>(List<T> destination, T value)
        {
            if (!destination.Contains(value))
                destination.Add(value);
        }
    }

    internal static class MaterialValuePatternMatcher
    {
        internal static bool MatchesParameter(
            MaterialValueIR values,
            MaterialValue value,
            MaterialParameter parameter)
        {
            return MatchesParameterNode(values, values.GetNode(value), parameter);
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
            if (MatchesParameterNode(
                    values,
                    values.Nodes[color.Operand0],
                    colorParameter))
            {
                sampleIndex = color.Operand1;
            }
            else if (MatchesParameterNode(
                         values,
                         values.Nodes[color.Operand1],
                         colorParameter))
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
                || !MatchesResourceNode(
                    values,
                    values.Nodes[sample.Operand0],
                    textureResource)
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

        internal static bool MatchesSampledColorComponent(
            MaterialValueIR values,
            MaterialValue value,
            int component,
            MaterialTextureResource textureResource,
            MaterialParameter colorParameter)
        {
            MaterialValueNode swizzle = values.GetNode(value);
            if (swizzle.Opcode != MaterialValueOpcode.Swizzle
                || swizzle.Type != MaterialValueType.Float
                || !MaterialSwizzleMask.TryDecode(
                    swizzle.Semantic,
                    out MaterialSwizzleMask mask)
                || mask.ComponentCount != 1
                || mask.GetComponent(0) != component)
            {
                return false;
            }

            MaterialValueNode source = values.Nodes[swizzle.Operand0];
            return MatchesSampledColor(
                values,
                new MaterialValue(values, swizzle.Operand0, source.Type),
                textureResource,
                colorParameter);
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

        private static bool MatchesParameterNode(
            MaterialValueIR values,
            in MaterialValueNode node,
            MaterialParameter parameter)
        {
            return node.Opcode == MaterialValueOpcode.Parameter
                && values.TryGetParameterDeclaration(
                    node.Semantic,
                    out MaterialParameterDeclaration declaration)
                && declaration
                    == MaterialNativeTemplateDeclarationAdapter.GetParameter(parameter);
        }

        private static bool MatchesResourceNode(
            MaterialValueIR values,
            in MaterialValueNode node,
            MaterialTextureResource resource)
        {
            return node.Opcode == MaterialValueOpcode.TextureResource
                && values.TryGetResourceDeclaration(
                    node.Semantic,
                    out MaterialResourceDeclaration declaration)
                && declaration
                    == MaterialNativeTemplateDeclarationAdapter.GetTexture(resource);
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
                && MaterialValuePatternMatcher.MatchesSampledColorComponent(
                    values,
                    module.Outputs.CoverageValue,
                    component: 3,
                    MaterialTextureResource.BaseColor,
                    MaterialParameter.BaseColor)
                && valueSlice.NodeCount == 9;
        }
    }

    internal sealed class CompiledSurfaceProgram
    {
        internal CompiledSurfaceProgram(
            VividMaterialSurfaceProgramID programID,
            VividMaterialProgramID materialProgramID,
            MaterialValueSlice valueSlice,
            MaterialValueRequirements requirements)
        {
            ProgramID = programID;
            MaterialProgramID = materialProgramID;
            ValueSlice = valueSlice ?? throw new ArgumentNullException(nameof(valueSlice));
            Requirements = requirements
                ?? throw new ArgumentNullException(nameof(requirements));
        }

        internal VividMaterialSurfaceProgramID ProgramID { get; }

        internal VividMaterialProgramID MaterialProgramID { get; }

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
            VividMaterialProgramID materialProgramID;
            ClosureExpressionNode root =
                module.ClosureGraph.GetNode(module.SurfaceClosure);
            if (root.Opcode == ClosureExpressionOpcode.Slab
                && MatchesStandardSingleSlab(module, root.Slab, valueSlice))
            {
                programID = VividMaterialSurfaceProgramID.StandardSingleSlab;
                materialProgramID = VividMaterialProgramID.StandardSingleSlab;
            }
            else if (MatchesDualSlab(module, root, valueSlice))
            {
                programID = VividMaterialSurfaceProgramID.DualSlab;
                materialProgramID = root.Opcode == ClosureExpressionOpcode.HorizontalMix
                    ? VividMaterialProgramID.DualSlabHorizontalMix
                    : VividMaterialProgramID.DualSlabVerticalLayer;
            }
            else
            {
                throw new NotSupportedException(
                    "Closure expression and value IR cannot be matched to an existing surface program ABI.");
            }

            return new CompiledSurfaceProgram(
                programID,
                materialProgramID,
                valueSlice,
                MaterialValueRequirements.Collect(valueSlice));
        }

        private static MaterialValueSlice CreateSurfaceValueSlice(MaterialIRModule module)
        {
            var roots = new List<MaterialValue>();
            IReadOnlyList<ClosureExpressionNode> nodes = module.ClosureGraph.Nodes;
            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                ClosureExpressionNode node = nodes[nodeIndex];
                if (node.Opcode == ClosureExpressionOpcode.Slab)
                {
                    roots.Add(node.Slab.BaseColor);
                    roots.Add(node.Slab.Roughness);
                    roots.Add(node.Slab.Metallic);
                    roots.Add(node.Slab.Normal);
                    roots.Add(node.Slab.Tangent);
                }
                else
                {
                    roots.Add(node.Weight);
                }
            }
            roots.Add(module.Outputs.Emission);

            return module.CreateValueSlice(roots.ToArray());
        }

        private static bool MatchesStandardSingleSlab(
            MaterialIRModule module,
            in ClosureSlabExpression slab,
            MaterialValueSlice valueSlice)
        {
            return MatchesNormalBasis(module.Values, slab)
                && MatchesSlab(
                    module.Values,
                    slab,
                    MaterialTextureResource.BaseColor,
                    MaterialParameter.BaseColor,
                    MaterialParameter.Roughness,
                    MaterialParameter.Metallic)
                && MaterialValuePatternMatcher.MatchesParameter(
                    module.Values,
                    module.Outputs.Emission,
                    MaterialParameter.Emission)
                && valueSlice.NodeCount == 12;
        }

        private static bool MatchesDualSlab(
            MaterialIRModule module,
            in ClosureExpressionNode root,
            MaterialValueSlice valueSlice)
        {
            if (root.Opcode != ClosureExpressionOpcode.HorizontalMix
                && root.Opcode != ClosureExpressionOpcode.VerticalLayer)
            {
                return false;
            }

            ClosureExpressionNode baseNode =
                module.ClosureGraph.Nodes[root.Operand0];
            ClosureExpressionNode topNode =
                module.ClosureGraph.Nodes[root.Operand1];
            if (baseNode.Opcode != ClosureExpressionOpcode.Slab
                || topNode.Opcode != ClosureExpressionOpcode.Slab)
            {
                return false;
            }

            ClosureSlabExpression baseSlab = baseNode.Slab;
            ClosureSlabExpression topSlab = topNode.Slab;
            return MatchesNormalBasis(module.Values, baseSlab)
                && MatchesNormalBasis(module.Values, topSlab)
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
                && MaterialValuePatternMatcher.MatchesParameter(
                    module.Values,
                    root.Weight,
                    MaterialParameter.LayerWeight)
                && MaterialValuePatternMatcher.MatchesParameter(
                    module.Values,
                    module.Outputs.Emission,
                    MaterialParameter.Emission)
                && valueSlice.NodeCount == 19;
        }

        private static bool MatchesNormalBasis(
            MaterialValueIR values,
            in ClosureSlabExpression slab)
        {
            return MaterialValuePatternMatcher.MatchesExternalInput(
                    values,
                    slab.Normal,
                    MaterialExternalInput.GeometryNormalWS)
                && MaterialValuePatternMatcher.MatchesExternalInput(
                    values,
                    slab.Tangent,
                    MaterialExternalInput.GeometryTangentWS);
        }

        private static bool MatchesSlab(
            MaterialValueIR values,
            in ClosureSlabExpression slab,
            MaterialTextureResource textureResource,
            MaterialParameter baseColorParameter,
            MaterialParameter roughnessParameter,
            MaterialParameter metallicParameter)
        {
            // Tiling/remap and optional Normal/Mask evaluation remain in the V1 layout ABI.
            return (slab.Features & ClosureFeatureMask.BaseColorTexture) != 0
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
        Float = 0,
        Float4 = 1,
        UInt = 2,
    }

    internal enum MaterialRuntimeParameter
    {
        BaseColor = 0,
        TopBaseColor = 1,
        BaseTextureTilingOffset = 2,
        TopTextureTilingOffset = 3,
        Emission = 4,
        BaseMetallicSmoothnessRemap = 5,
        TopMetallicSmoothnessRemap = 6,
        BaseAmbientOcclusionRemap = 7,
        TopAmbientOcclusionRemap = 8,
        BaseNormalsStrength = 9,
        TopNormalsStrength = 10,
        Roughness = 11,
        TopRoughness = 12,
        Metallic = 13,
        TopMetallic = 14,
        BaseMaskMode = 15,
        TopMaskMode = 16,
        LayerOperator = 17,
        LayerWeight = 18,
        AlphaClipThreshold = 19,
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
                        MaterialParameter.Emission,
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
                        MaterialParameter.Emission,
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
            int worstCaseCoverageTextureSamples,
            int worstCaseSurfaceTextureSamples,
            int worstCaseTotalTextureSamples,
            int closureCount,
            int operatorCount,
            int parameterBindingCount,
            int resourceBindingCount,
            int parameterBytes,
            int resourceBindingRecords)
        {
            Coverage = coverage;
            Surface = surface;
            Combined = combined;
            WorstCaseCoverageTextureSamples = worstCaseCoverageTextureSamples;
            WorstCaseSurfaceTextureSamples = worstCaseSurfaceTextureSamples;
            WorstCaseTotalTextureSamples = worstCaseTotalTextureSamples;
            ClosureCount = closureCount;
            OperatorCount = operatorCount;
            ParameterBindingCount = parameterBindingCount;
            ResourceBindingCount = resourceBindingCount;
            ParameterBytes = parameterBytes;
            ResourceBindingRecords = resourceBindingRecords;
        }

        internal MaterialStageCost Coverage { get; }

        internal MaterialStageCost Surface { get; }

        internal MaterialStageCost Combined { get; }

        internal int WorstCaseCoverageTextureSamples { get; }

        internal int WorstCaseSurfaceTextureSamples { get; }

        internal int WorstCaseTotalTextureSamples { get; }

        internal int ClosureCount { get; }

        internal int OperatorCount { get; }

        internal int ParameterBindingCount { get; }

        internal int ResourceBindingCount { get; }

        internal int ParameterBytes { get; }

        internal int ResourceBindingRecords { get; }
    }

    internal readonly struct MaterialProgramCostBudget
    {
        internal MaterialProgramCostBudget(
            int maxCombinedValueNodes,
            int maxCoverageTextureSamples,
            int maxSurfaceTextureSamples,
            int maxTotalTextureSamples,
            int maxParameterBindings,
            int maxResourceBindings,
            int maxClosures,
            int maxOperators,
            int maxParameterBytes,
            int maxResourceBindingRecords)
        {
            if (maxCombinedValueNodes < 0)
                throw new ArgumentOutOfRangeException(nameof(maxCombinedValueNodes));
            if (maxCoverageTextureSamples < 0)
                throw new ArgumentOutOfRangeException(nameof(maxCoverageTextureSamples));
            if (maxSurfaceTextureSamples < 0)
                throw new ArgumentOutOfRangeException(nameof(maxSurfaceTextureSamples));
            if (maxTotalTextureSamples < 0)
                throw new ArgumentOutOfRangeException(nameof(maxTotalTextureSamples));
            if (maxParameterBindings < 0)
                throw new ArgumentOutOfRangeException(nameof(maxParameterBindings));
            if (maxResourceBindings < 0)
                throw new ArgumentOutOfRangeException(nameof(maxResourceBindings));
            if (maxClosures < 0)
                throw new ArgumentOutOfRangeException(nameof(maxClosures));
            if (maxOperators < 0)
                throw new ArgumentOutOfRangeException(nameof(maxOperators));
            if (maxParameterBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maxParameterBytes));
            if (maxResourceBindingRecords < 0)
                throw new ArgumentOutOfRangeException(nameof(maxResourceBindingRecords));

            MaxCombinedValueNodes = maxCombinedValueNodes;
            MaxCoverageTextureSamples = maxCoverageTextureSamples;
            MaxSurfaceTextureSamples = maxSurfaceTextureSamples;
            MaxTotalTextureSamples = maxTotalTextureSamples;
            MaxParameterBindings = maxParameterBindings;
            MaxResourceBindings = maxResourceBindings;
            MaxClosures = maxClosures;
            MaxOperators = maxOperators;
            MaxParameterBytes = maxParameterBytes;
            MaxResourceBindingRecords = maxResourceBindingRecords;
        }

        internal static MaterialProgramCostBudget Prototype =>
            new MaterialProgramCostBudget(
                maxCombinedValueNodes: 24,
                maxCoverageTextureSamples: 1,
                maxSurfaceTextureSamples: 6,
                maxTotalTextureSamples: 7,
                maxParameterBindings: 20,
                maxResourceBindings: 6,
                maxClosures: 2,
                maxOperators: 1,
                maxParameterBytes: 192,
                maxResourceBindingRecords: 2);

        internal int MaxCombinedValueNodes { get; }

        internal int MaxCoverageTextureSamples { get; }

        internal int MaxSurfaceTextureSamples { get; }

        internal int MaxTotalTextureSamples { get; }

        internal int MaxParameterBindings { get; }

        internal int MaxResourceBindings { get; }

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
            builder.AppendLine("cost_model=lowered_program_worst_case_v2");
            builder.AppendLine("typed_ir:");
            AppendStageCost(builder, "coverage", Cost.Coverage);
            AppendStageCost(builder, "surface", Cost.Surface);
            AppendStageCost(builder, "combined", Cost.Combined);
            builder.Append("lowered texture_samples coverage=")
                .Append(Cost.WorstCaseCoverageTextureSamples)
                .Append(" surface=").Append(Cost.WorstCaseSurfaceTextureSamples)
                .Append(" total=").Append(Cost.WorstCaseTotalTextureSamples)
                .AppendLine();
            builder.Append("topology closures=").Append(Cost.ClosureCount)
                .Append(" operators=").Append(Cost.OperatorCount).AppendLine();
            builder.Append("layout parameter_bindings=").Append(Cost.ParameterBindingCount)
                .Append(" parameter_bytes=").Append(Cost.ParameterBytes)
                .Append(" resource_bindings=").Append(Cost.ResourceBindingCount)
                .Append(" resource_records=").Append(Cost.ResourceBindingRecords)
                .AppendLine();
            builder.Append("budget combined_ir_nodes=").Append(Budget.MaxCombinedValueNodes)
                .Append(" coverage_texture_samples=")
                .Append(Budget.MaxCoverageTextureSamples)
                .Append(" surface_texture_samples=")
                .Append(Budget.MaxSurfaceTextureSamples)
                .Append(" total_texture_samples=").Append(Budget.MaxTotalTextureSamples)
                .Append(" parameter_bindings=").Append(Budget.MaxParameterBindings)
                .Append(" resource_bindings=").Append(Budget.MaxResourceBindings)
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

            MaterialStageCost coverageCost = AnalyzeSlice(coverageProgram.ValueSlice);
            MaterialStageCost surfaceCost = AnalyzeSlice(surfaceProgram.ValueSlice);
            int worstCaseCoverageTextureSamples =
                AnalyzeWorstCaseCoverageTextureSamples(module, coverageCost);
            int worstCaseSurfaceTextureSamples =
                AnalyzeWorstCaseSurfaceTextureSamples(module, surfaceCost);
            return new MaterialProgramCost(
                coverageCost,
                surfaceCost,
                AnalyzeCombined(
                    module.Values,
                    coverageProgram.ValueSlice,
                    surfaceProgram.ValueSlice),
                worstCaseCoverageTextureSamples,
                worstCaseSurfaceTextureSamples,
                worstCaseCoverageTextureSamples + worstCaseSurfaceTextureSamples,
                module.Topology.ClosureCount,
                module.Topology.OperatorCount,
                materialLayout.ParameterLayout.Bindings.Count,
                materialLayout.ResourceLayout.Bindings.Count,
                materialLayout.ParameterLayout.Stride,
                materialLayout.ResourceLayout.RecordCount);
        }

        private static int AnalyzeWorstCaseCoverageTextureSamples(
            MaterialIRModule module,
            in MaterialStageCost coverageCost)
        {
            return (module.MaterialFeatures & MaterialFeatureMask.AlphaClip) != 0
                ? coverageCost.TextureSampleCount
                : 0;
        }

        private static int AnalyzeWorstCaseSurfaceTextureSamples(
            MaterialIRModule module,
            in MaterialStageCost surfaceCost)
        {
            int textureSamples = surfaceCost.TextureSampleCount;
            IReadOnlyList<ClosureExpressionNode> nodes = module.ClosureGraph.Nodes;
            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                ClosureExpressionNode node = nodes[nodeIndex];
                if (node.Opcode != ClosureExpressionOpcode.Slab)
                    continue;

                ClosureFeatureMask features = node.Slab.Features;
                if ((features & ClosureFeatureMask.NormalTexture) != 0)
                    textureSamples++;
                if ((features & ClosureFeatureMask.MaskTexture) != 0)
                    textureSamples++;
            }
            return textureSamples;
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
                MaterialValueOpcode opcode = values.Nodes[nodeIndices[i]].Opcode;
                if (!MaterialOpcodeTable.TryGetInfo(opcode, out MaterialOpcodeInfo info))
                    throw new InvalidOperationException($"Verified material IR contains opcode {opcode}.");

                MaterialOpcodeFlags flags = info.Flags;
                if ((flags & MaterialOpcodeFlags.TextureSample) != 0)
                    textureSamples++;
                if ((flags & MaterialOpcodeFlags.Derivative) != 0)
                    derivatives++;
                if ((flags & MaterialOpcodeFlags.Arithmetic) != 0)
                    arithmeticNodes++;
                if ((flags & MaterialOpcodeFlags.Parameter) != 0)
                    parameters++;
                if ((flags & MaterialOpcodeFlags.TextureResource) != 0)
                    textureResources++;
                if ((flags & MaterialOpcodeFlags.ExternalInput) != 0)
                    externalInputs++;
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
                "coverage texture samples",
                cost.WorstCaseCoverageTextureSamples,
                budget.MaxCoverageTextureSamples);
            AddExceeded(
                entries,
                "MPC1003",
                "surface texture samples",
                cost.WorstCaseSurfaceTextureSamples,
                budget.MaxSurfaceTextureSamples);
            AddExceeded(
                entries,
                "MPC1004",
                "total texture samples",
                cost.WorstCaseTotalTextureSamples,
                budget.MaxTotalTextureSamples);
            AddExceeded(
                entries,
                "MPC1005",
                "parameter bindings",
                cost.ParameterBindingCount,
                budget.MaxParameterBindings);
            AddExceeded(
                entries,
                "MPC1006",
                "resource bindings",
                cost.ResourceBindingCount,
                budget.MaxResourceBindings);
            AddExceeded(
                entries,
                "MPC1007",
                "closures",
                cost.ClosureCount,
                budget.MaxClosures);
            AddExceeded(
                entries,
                "MPC1008",
                "closure operators",
                cost.OperatorCount,
                budget.MaxOperators);
            AddExceeded(
                entries,
                "MPC1009",
                "parameter bytes",
                cost.ParameterBytes,
                budget.MaxParameterBytes);
            AddExceeded(
                entries,
                "MPC1010",
                "resource binding records",
                cost.ResourceBindingRecords,
                budget.MaxResourceBindingRecords);
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
                $"{name} cost {actual} exceeds budget {maximum}."));
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
            in VividMaterialProgramData runtimeData,
            in CompiledMaterialProgramHash compiledHash)
        {
            Module = module;
            CoverageProgram = coverageProgram;
            SurfaceProgram = surfaceProgram;
            MaterialLayout = materialLayout;
            Diagnostics = diagnostics;
            ProgramID = programID;
            RuntimeData = runtimeData;
            CompiledHash = compiledHash;
        }

        internal MaterialIRModule Module { get; }

        internal CompiledCoverageProgram CoverageProgram { get; }

        internal CompiledSurfaceProgram SurfaceProgram { get; }

        internal CompiledMaterialLayout MaterialLayout { get; }

        internal MaterialProgramDiagnostics Diagnostics { get; }

        internal VividMaterialProgramID ProgramID { get; }

        internal VividMaterialProgramData RuntimeData { get; }

        internal MaterialSemanticHash SemanticHash => Module.SemanticHash;

        internal CompiledMaterialProgramHash CompiledHash { get; }

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
            if (programVersion != MaterialProgramContract.RuntimeAbiVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(programVersion),
                    programVersion,
                    $"Only material runtime ABI version "
                    + $"{MaterialProgramContract.RuntimeAbiVersion} is supported.");
            }

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

            VividMaterialProgramID programID = surfaceProgram.MaterialProgramID;

            VividMaterialProgramCapabilities capabilities =
                VividMaterialProgramCapabilities.LegacyGBufferExport;
            if ((module.MaterialFeatures & MaterialFeatureMask.AlphaClip) != 0)
                capabilities |= VividMaterialProgramCapabilities.AlphaClip;
            if ((module.ShadingModels & MaterialShadingModelMask.Unlit) != 0)
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
            CompiledMaterialProgramHash compiledHash =
                CompiledMaterialProgramHashBuilder.ComputeNativeTemplate(
                    module.SemanticHash,
                    runtimeData,
                    materialLayout);
            return new CompiledMaterialProgram(
                module,
                coverageProgram,
                surfaceProgram,
                materialLayout,
                diagnostics,
                programID,
                runtimeData,
                compiledHash);
        }
    }

    internal static class MaterialProgramPrototypeBuilder
    {
        private const ClosureFeatureMask SupportedSlabFeatures =
            ClosureFeatureMask.BaseColorTexture
            | ClosureFeatureMask.NormalTexture
            | ClosureFeatureMask.MaskTexture;

        private const MaterialFeatureMask SupportedMaterialFeatures =
            MaterialFeatureMask.AlphaClip;

        private const MaterialShadingModelMask SupportedShadingModels =
            MaterialShadingModelMask.StandardLit
            | MaterialShadingModelMask.Unlit;

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
            MaterialValue emission = valueIR.Parameter(MaterialParameter.Emission);
            MaterialValue coverage = valueIR.Swizzle(baseColor, MaterialSwizzleMask.W);
            MaterialValue normal =
                valueIR.ExternalInput(MaterialExternalInput.GeometryNormalWS);
            MaterialValue tangent =
                valueIR.ExternalInput(MaterialExternalInput.GeometryTangentWS);
            var closureGraph = new ClosureExpressionGraph(valueIR);
            MaterialClosure surfaceClosure = closureGraph.Slab(
                baseColor,
                roughness,
                metallic,
                normal,
                tangent,
                SupportedSlabFeatures);
            var module = new MaterialIRModule(
                valueIR,
                new MaterialOutputRoots(coverage, alphaClipThreshold, emission),
                closureGraph,
                surfaceClosure,
                ClosureTopologyBudget.Prototype,
                SupportedMaterialFeatures,
                SupportedShadingModels);
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
            MaterialValue emission = valueIR.Parameter(MaterialParameter.Emission);
            MaterialValue coverage = valueIR.Swizzle(baseColor, MaterialSwizzleMask.W);
            MaterialValue normal =
                valueIR.ExternalInput(MaterialExternalInput.GeometryNormalWS);
            MaterialValue tangent =
                valueIR.ExternalInput(MaterialExternalInput.GeometryTangentWS);
            var closureGraph = new ClosureExpressionGraph(valueIR);
            MaterialClosure baseSlab = closureGraph.Slab(
                baseColor,
                roughness,
                metallic,
                normal,
                tangent,
                SupportedSlabFeatures);
            MaterialClosure topSlab = closureGraph.Slab(
                topBaseColor,
                topRoughness,
                topMetallic,
                normal,
                tangent,
                SupportedSlabFeatures);
            MaterialClosure surfaceClosure;
            switch (layerOperator)
            {
                case VividDualSlabOperator.HorizontalMix:
                    surfaceClosure = closureGraph.HorizontalMix(
                        baseSlab,
                        topSlab,
                        layerWeight);
                    break;
                case VividDualSlabOperator.VerticalLayer:
                    surfaceClosure = closureGraph.VerticalLayer(
                        baseSlab,
                        topSlab,
                        layerWeight);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(layerOperator),
                        layerOperator,
                        null);
            }
            var module = new MaterialIRModule(
                valueIR,
                new MaterialOutputRoots(coverage, alphaClipThreshold, emission),
                closureGraph,
                surfaceClosure,
                ClosureTopologyBudget.Prototype,
                SupportedMaterialFeatures,
                SupportedShadingModels);
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
    }
}
