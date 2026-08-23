using System;
using System.Collections.Generic;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class CompiledCoverageProgram
    {
        internal CompiledCoverageProgram(
            VividMaterialCoverageProgramID programID,
            MaterialValueSlice valueSlice,
            IReadOnlyList<MaterialParameter> requiredParameters,
            IReadOnlyList<MaterialTextureResource> requiredTextureResources,
            IReadOnlyList<MaterialExternalInput> requiredExternalInputs)
        {
            ProgramID = programID;
            ValueSlice = valueSlice ?? throw new ArgumentNullException(nameof(valueSlice));
            RequiredParameters = requiredParameters
                ?? throw new ArgumentNullException(nameof(requiredParameters));
            RequiredTextureResources = requiredTextureResources
                ?? throw new ArgumentNullException(nameof(requiredTextureResources));
            RequiredExternalInputs = requiredExternalInputs
                ?? throw new ArgumentNullException(nameof(requiredExternalInputs));
        }

        internal VividMaterialCoverageProgramID ProgramID { get; }

        internal MaterialValueSlice ValueSlice { get; }

        internal IReadOnlyList<MaterialParameter> RequiredParameters { get; }

        internal IReadOnlyList<MaterialTextureResource> RequiredTextureResources { get; }

        internal IReadOnlyList<MaterialExternalInput> RequiredExternalInputs { get; }
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

            CollectRequirements(
                valueSlice,
                out IReadOnlyList<MaterialParameter> parameters,
                out IReadOnlyList<MaterialTextureResource> textureResources,
                out IReadOnlyList<MaterialExternalInput> externalInputs);
            return new CompiledCoverageProgram(
                VividMaterialCoverageProgramID.BaseColorAlpha,
                valueSlice,
                parameters,
                textureResources,
                externalInputs);
        }

        private static bool MatchesBaseColorAlphaProgram(
            MaterialIRModule module,
            MaterialValueSlice valueSlice)
        {
            MaterialValueIR values = module.Values;
            MaterialValueNode alphaClipThreshold =
                values.GetNode(module.Outputs.AlphaClipThreshold);
            if (!MatchesSemantic(
                    alphaClipThreshold,
                    MaterialValueOpcode.Parameter,
                    MaterialValueType.Float,
                    (int) MaterialParameter.AlphaClipThreshold))
            {
                return false;
            }

            MaterialValueNode coverage = values.GetNode(module.Outputs.CoverageValue);
            if (coverage.Opcode != MaterialValueOpcode.Multiply
                || coverage.Type != MaterialValueType.Float4)
            {
                return false;
            }

            int sampleIndex;
            if (MatchesSemantic(
                    values.Nodes[coverage.Operand0],
                    MaterialValueOpcode.Parameter,
                    MaterialValueType.Float4,
                    (int) MaterialParameter.BaseColor))
            {
                sampleIndex = coverage.Operand1;
            }
            else if (MatchesSemantic(
                         values.Nodes[coverage.Operand1],
                         MaterialValueOpcode.Parameter,
                         MaterialValueType.Float4,
                         (int) MaterialParameter.BaseColor))
            {
                sampleIndex = coverage.Operand0;
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
                    (int) MaterialTextureResource.BaseColor)
                || !MatchesSemantic(
                    values.Nodes[sample.Operand1],
                    MaterialValueOpcode.ExternalInput,
                    MaterialValueType.Float2,
                    (int) MaterialExternalInput.UV0)
                || !MatchesDerivative(
                    values,
                    sample.Operand2,
                    MaterialValueOpcode.Ddx,
                    sample.Operand1)
                || !MatchesDerivative(
                    values,
                    sample.Operand3,
                    MaterialValueOpcode.Ddy,
                    sample.Operand1))
            {
                return false;
            }

            return valueSlice.NodeCount == 8;
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

        private static void CollectRequirements(
            MaterialValueSlice valueSlice,
            out IReadOnlyList<MaterialParameter> parameters,
            out IReadOnlyList<MaterialTextureResource> textureResources,
            out IReadOnlyList<MaterialExternalInput> externalInputs)
        {
            var parameterList = new List<MaterialParameter>();
            var textureResourceList = new List<MaterialTextureResource>();
            var externalInputList = new List<MaterialExternalInput>();
            for (int i = 0; i < valueSlice.NodeIndices.Count; i++)
            {
                MaterialValueNode node = valueSlice.Values.Nodes[valueSlice.NodeIndices[i]];
                switch (node.Opcode)
                {
                    case MaterialValueOpcode.Parameter:
                        parameterList.Add((MaterialParameter) node.Semantic);
                        break;
                    case MaterialValueOpcode.TextureResource:
                        textureResourceList.Add((MaterialTextureResource) node.Semantic);
                        break;
                    case MaterialValueOpcode.ExternalInput:
                        externalInputList.Add((MaterialExternalInput) node.Semantic);
                        break;
                }
            }

            parameters = parameterList.AsReadOnly();
            textureResources = textureResourceList.AsReadOnly();
            externalInputs = externalInputList.AsReadOnly();
        }
    }

    internal sealed class CompiledMaterialProgram
    {
        private CompiledMaterialProgram(
            MaterialIRModule module,
            CompiledCoverageProgram coverageProgram,
            VividMaterialProgramID programID,
            in VividMaterialProgramData runtimeData)
        {
            Module = module;
            CoverageProgram = coverageProgram;
            ProgramID = programID;
            RuntimeData = runtimeData;
        }

        internal MaterialIRModule Module { get; }

        internal CompiledCoverageProgram CoverageProgram { get; }

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

            VividMaterialProgramID programID;
            VividMaterialSurfaceProgramID surfaceProgramID;
            VividMaterialParameterLayoutID parameterLayoutID;
            VividMaterialResourceLayoutID resourceLayoutID;
            if (topology.ClosureCount == 1 && topology.OperatorCount == 0)
            {
                programID = VividMaterialProgramID.StandardSingleSlab;
                surfaceProgramID = VividMaterialSurfaceProgramID.StandardSingleSlab;
                parameterLayoutID = VividMaterialParameterLayoutID.LegacyMaterialData;
                resourceLayoutID = VividMaterialResourceLayoutID.LegacySurfaceBinding;
            }
            else if (topology.ClosureCount == 2 && topology.OperatorCount == 1)
            {
                ClosureOperatorKind operatorKind = topology.Operators[0].Kind;
                if (operatorKind != ClosureOperatorKind.HorizontalMix
                    && operatorKind != ClosureOperatorKind.VerticalLayer)
                {
                    throw new NotSupportedException(
                        $"Closure operator '{operatorKind}' cannot be lowered to Program 1.");
                }

                programID = VividMaterialProgramID.DualSlab;
                surfaceProgramID = VividMaterialSurfaceProgramID.DualSlab;
                parameterLayoutID = VividMaterialParameterLayoutID.DualSlabMaterialData;
                resourceLayoutID = VividMaterialResourceLayoutID.DualSurfaceBinding;
            }
            else
            {
                throw new NotSupportedException(
                    "Closure topology cannot be lowered to an existing material program ABI.");
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
                SurfaceProgramID = surfaceProgramID,
                TransportProgramID = VividMaterialTransportProgramID.None,
                ParameterLayoutID = parameterLayoutID,
                ResourceLayoutID = resourceLayoutID,
                CapabilityFlags = capabilities,
                ExecutionClass = VividMaterialExecutionClass.VisibilityDeferred,
            };
            return new CompiledMaterialProgram(module, coverageProgram, programID, runtimeData);
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
