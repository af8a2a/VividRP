using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
        private const ClosureFeatureMask SupportedFeatures =
            ClosureFeatureMask.BaseColorTexture
            | ClosureFeatureMask.NormalTexture
            | ClosureFeatureMask.MaskTexture
            | ClosureFeatureMask.AlphaClip
            | ClosureFeatureMask.Emission
            | ClosureFeatureMask.Unlit;

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
                && (slab.Features & ~SupportedFeatures) == 0
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

    internal readonly struct MaterialParameterLayoutBinding
    {
        internal MaterialParameterLayoutBinding(
            MaterialParameter parameter,
            MaterialValueType type,
            int byteOffset)
        {
            Parameter = parameter;
            Type = type;
            ByteOffset = byteOffset;
        }

        internal MaterialParameter Parameter { get; }

        internal MaterialValueType Type { get; }

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

            LayoutID = layoutID;
            Stride = stride;
            m_Bindings = Array.AsReadOnly(
                (MaterialParameterLayoutBinding[]) bindings.Clone());
        }

        internal VividMaterialParameterLayoutID LayoutID { get; }

        internal int Stride { get; }

        internal IReadOnlyList<MaterialParameterLayoutBinding> Bindings => m_Bindings;

        internal bool TryGetBinding(
            MaterialParameter parameter,
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
                        MaterialParameter.BaseColor,
                        MaterialValueType.Float4,
                        nameof(VividMaterialData.AlbedoColor)),
                    ParameterBinding<VividMaterialData>(
                        MaterialParameter.Roughness,
                        MaterialValueType.Float,
                        nameof(VividMaterialData.Roughness)),
                    ParameterBinding<VividMaterialData>(
                        MaterialParameter.Metallic,
                        MaterialValueType.Float,
                        nameof(VividMaterialData.Metallic)),
                    ParameterBinding<VividMaterialData>(
                        MaterialParameter.AlphaClipThreshold,
                        MaterialValueType.Float,
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
                        MaterialParameter.BaseColor,
                        MaterialValueType.Float4,
                        nameof(VividDualSlabMaterialData.BaseAlbedoColor)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialParameter.TopBaseColor,
                        MaterialValueType.Float4,
                        nameof(VividDualSlabMaterialData.TopAlbedoColor)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialParameter.Roughness,
                        MaterialValueType.Float,
                        nameof(VividDualSlabMaterialData.BaseRoughness)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialParameter.TopRoughness,
                        MaterialValueType.Float,
                        nameof(VividDualSlabMaterialData.TopRoughness)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialParameter.Metallic,
                        MaterialValueType.Float,
                        nameof(VividDualSlabMaterialData.BaseMetallic)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialParameter.TopMetallic,
                        MaterialValueType.Float,
                        nameof(VividDualSlabMaterialData.TopMetallic)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialParameter.LayerWeight,
                        MaterialValueType.Float,
                        nameof(VividDualSlabMaterialData.LayerWeight)),
                    ParameterBinding<VividDualSlabMaterialData>(
                        MaterialParameter.AlphaClipThreshold,
                        MaterialValueType.Float,
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
                        recordOffset: 0),
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
                        recordOffset: 0),
                    ResourceBinding(
                        MaterialTextureResource.TopBaseColor,
                        recordOffset: 1),
                });
        }

        private static MaterialParameterLayoutBinding ParameterBinding<T>(
            MaterialParameter parameter,
            MaterialValueType type,
            string fieldName)
        {
            return new MaterialParameterLayoutBinding(
                parameter,
                type,
                OffsetOf<T>(fieldName));
        }

        private static MaterialResourceLayoutBinding ResourceBinding(
            MaterialTextureResource resource,
            int recordOffset)
        {
            return new MaterialResourceLayoutBinding(
                resource,
                recordOffset,
                OffsetOf<VividSurfaceBindingData>(
                    nameof(VividSurfaceBindingData.BaseColorResource)));
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

    internal sealed class CompiledMaterialProgram
    {
        private CompiledMaterialProgram(
            MaterialIRModule module,
            CompiledCoverageProgram coverageProgram,
            CompiledSurfaceProgram surfaceProgram,
            CompiledMaterialLayout materialLayout,
            VividMaterialProgramID programID,
            in VividMaterialProgramData runtimeData)
        {
            Module = module;
            CoverageProgram = coverageProgram;
            SurfaceProgram = surfaceProgram;
            MaterialLayout = materialLayout;
            ProgramID = programID;
            RuntimeData = runtimeData;
        }

        internal MaterialIRModule Module { get; }

        internal CompiledCoverageProgram CoverageProgram { get; }

        internal CompiledSurfaceProgram SurfaceProgram { get; }

        internal CompiledMaterialLayout MaterialLayout { get; }

        internal VividMaterialProgramID ProgramID { get; }

        internal VividMaterialProgramData RuntimeData { get; }

        internal static CompiledMaterialProgram Compile(
            MaterialIRModule module,
            uint programVersion)
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

            VividMaterialProgramID programID;
            switch (surfaceProgram.ProgramID)
            {
                case VividMaterialSurfaceProgramID.StandardSingleSlab:
                    programID = VividMaterialProgramID.StandardSingleSlab;
                    break;
                case VividMaterialSurfaceProgramID.DualSlab:
                    programID = VividMaterialProgramID.DualSlab;
                    break;
                default:
                    throw new NotSupportedException(
                        $"Surface program '{surfaceProgram.ProgramID}' has no material program ABI.");
            }

            ClosureFeatureMask features = topology.FeatureMask;
            VividMaterialProgramCapabilities capabilities =
                VividMaterialProgramCapabilities.LegacyGBufferExport;
            if ((features & ClosureFeatureMask.AlphaClip) != 0)
                capabilities |= VividMaterialProgramCapabilities.AlphaClip;
            if ((features & ClosureFeatureMask.Unlit) != 0)
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
                programID,
                runtimeData);
        }
    }

    internal static class MaterialProgramPrototypeBuilder
    {
        private const ClosureFeatureMask SupportedSlabFeatures =
            ClosureFeatureMask.BaseColorTexture
            | ClosureFeatureMask.NormalTexture
            | ClosureFeatureMask.MaskTexture
            | ClosureFeatureMask.AlphaClip
            | ClosureFeatureMask.Emission
            | ClosureFeatureMask.Unlit;

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
                topology);
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
                topology);
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
