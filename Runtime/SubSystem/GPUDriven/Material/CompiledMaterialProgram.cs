using System;
using System.Collections.Generic;

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

    internal sealed class CompiledMaterialProgram
    {
        private CompiledMaterialProgram(
            MaterialIRModule module,
            CompiledCoverageProgram coverageProgram,
            CompiledSurfaceProgram surfaceProgram,
            VividMaterialProgramID programID,
            in VividMaterialProgramData runtimeData)
        {
            Module = module;
            CoverageProgram = coverageProgram;
            SurfaceProgram = surfaceProgram;
            ProgramID = programID;
            RuntimeData = runtimeData;
        }

        internal MaterialIRModule Module { get; }

        internal CompiledCoverageProgram CoverageProgram { get; }

        internal CompiledSurfaceProgram SurfaceProgram { get; }

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

            VividMaterialProgramID programID;
            VividMaterialParameterLayoutID parameterLayoutID;
            VividMaterialResourceLayoutID resourceLayoutID;
            switch (surfaceProgram.ProgramID)
            {
                case VividMaterialSurfaceProgramID.StandardSingleSlab:
                    programID = VividMaterialProgramID.StandardSingleSlab;
                    parameterLayoutID = VividMaterialParameterLayoutID.LegacyMaterialData;
                    resourceLayoutID = VividMaterialResourceLayoutID.LegacySurfaceBinding;
                    break;
                case VividMaterialSurfaceProgramID.DualSlab:
                    programID = VividMaterialProgramID.DualSlab;
                    parameterLayoutID = VividMaterialParameterLayoutID.DualSlabMaterialData;
                    resourceLayoutID = VividMaterialResourceLayoutID.DualSurfaceBinding;
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
                ParameterLayoutID = parameterLayoutID,
                ResourceLayoutID = resourceLayoutID,
                CapabilityFlags = capabilities,
                ExecutionClass = VividMaterialExecutionClass.VisibilityDeferred,
            };
            return new CompiledMaterialProgram(
                module,
                coverageProgram,
                surfaceProgram,
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
