using System;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class CompiledMaterialProgram
    {
        private CompiledMaterialProgram(
            MaterialValueIR valueIR,
            ClosureTopology topology,
            VividMaterialProgramID programID,
            in VividMaterialProgramData runtimeData)
        {
            ValueIR = valueIR;
            Topology = topology;
            ProgramID = programID;
            RuntimeData = runtimeData;
        }

        internal MaterialValueIR ValueIR { get; }

        internal ClosureTopology Topology { get; }

        internal VividMaterialProgramID ProgramID { get; }

        internal VividMaterialProgramData RuntimeData { get; }

        internal static CompiledMaterialProgram Compile(
            MaterialValueIR valueIR,
            ClosureTopology topology,
            uint programVersion)
        {
            if (valueIR == null)
                throw new ArgumentNullException(nameof(valueIR));
            if (topology == null)
                throw new ArgumentNullException(nameof(topology));
            if (!ReferenceEquals(valueIR, topology.ValueIR))
                throw new ArgumentException("Closure topology must reference the compiled value IR.", nameof(topology));
            if (!topology.IsWithinBudget)
                throw new InvalidOperationException("Closure topology exceeds its compilation budget.");

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
                CoverageProgramID = VividMaterialCoverageProgramID.BaseColorAlpha,
                SurfaceProgramID = surfaceProgramID,
                TransportProgramID = VividMaterialTransportProgramID.None,
                ParameterLayoutID = parameterLayoutID,
                ResourceLayoutID = resourceLayoutID,
                CapabilityFlags = capabilities,
                ExecutionClass = VividMaterialExecutionClass.VisibilityDeferred,
            };
            return new CompiledMaterialProgram(valueIR, topology, programID, runtimeData);
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
                MaterialValueParameter.BaseColorTexture,
                MaterialValueParameter.BaseColor);
            MaterialValue roughness = valueIR.Parameter(MaterialValueParameter.Roughness);
            MaterialValue metallic = valueIR.Parameter(MaterialValueParameter.Metallic);
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
            return CompiledMaterialProgram.Compile(valueIR, topology, programVersion);
        }

        internal static CompiledMaterialProgram BuildDualSlab(
            uint programVersion,
            VividDualSlabOperator layerOperator)
        {
            var valueIR = new MaterialValueIR();
            MaterialValue baseColor = BuildSampledBaseColor(
                valueIR,
                MaterialValueParameter.BaseColorTexture,
                MaterialValueParameter.BaseColor);
            MaterialValue topBaseColor = BuildSampledBaseColor(
                valueIR,
                MaterialValueParameter.TopBaseColorTexture,
                MaterialValueParameter.TopBaseColor);
            MaterialValue roughness = valueIR.Parameter(MaterialValueParameter.Roughness);
            MaterialValue topRoughness = valueIR.Parameter(MaterialValueParameter.TopRoughness);
            MaterialValue metallic = valueIR.Parameter(MaterialValueParameter.Metallic);
            MaterialValue topMetallic = valueIR.Parameter(MaterialValueParameter.TopMetallic);
            MaterialValue layerWeight = valueIR.Parameter(MaterialValueParameter.LayerWeight);
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
            return CompiledMaterialProgram.Compile(valueIR, topology, programVersion);
        }

        private static MaterialValue BuildSampledBaseColor(
            MaterialValueIR valueIR,
            MaterialValueParameter textureParameter,
            MaterialValueParameter colorParameter)
        {
            MaterialValue uv = valueIR.Parameter(MaterialValueParameter.UV0);
            MaterialValue texture = valueIR.Parameter(textureParameter);
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
                    valueIR.Parameter(MaterialValueParameter.GeometryNormalWS),
                    valueIR.Parameter(MaterialValueParameter.GeometryTangentWS)),
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
