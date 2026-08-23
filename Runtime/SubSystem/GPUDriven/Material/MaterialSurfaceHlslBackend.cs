using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven
{
    internal enum MaterialSurfaceHlslPhysicalContract
    {
        LegacySingleSlab = 0,
        DualSlab = 1,
    }

    internal sealed class MaterialSurfaceHlslArtifact
    {
        internal MaterialSurfaceHlslArtifact(
            string entryPoint,
            string source,
            MaterialProgramTopologySpecialization topology,
            MaterialSurfaceHlslPhysicalContract physicalContract,
            ulong bindingHash)
        {
            if (string.IsNullOrEmpty(entryPoint))
                throw new ArgumentException("A Surface HLSL entry point is required.", nameof(entryPoint));
            if (string.IsNullOrEmpty(source))
                throw new ArgumentException("Surface HLSL source is required.", nameof(source));

            EntryPoint = entryPoint;
            Source = NormalizeLineEndings(source);
            Topology = topology;
            PhysicalContract = physicalContract;
            BindingHash = bindingHash;
            PayloadHash = ComputePayloadHash();
        }

        internal uint Version => MaterialProgramContract.SurfaceHlslArtifactVersion;

        internal uint BackendVersion => MaterialProgramContract.SurfaceHlslBackendVersion;

        internal string EntryPoint { get; }

        internal string Source { get; }

        internal MaterialProgramTopologySpecialization Topology { get; }

        internal MaterialSurfaceHlslPhysicalContract PhysicalContract { get; }

        internal ulong BindingHash { get; }

        internal ulong PayloadHash { get; }

        internal bool PayloadEquals(MaterialSurfaceHlslArtifact other)
        {
            return ReferenceEquals(this, other)
                || other != null
                && Version == other.Version
                && BackendVersion == other.BackendVersion
                && Topology == other.Topology
                && PhysicalContract == other.PhysicalContract
                && BindingHash == other.BindingHash
                && string.Equals(EntryPoint, other.EntryPoint, StringComparison.Ordinal)
                && string.Equals(Source, other.Source, StringComparison.Ordinal);
        }

        private ulong ComputePayloadHash()
        {
            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            MaterialProgramHashUtility.Add(ref hash, Version);
            MaterialProgramHashUtility.Add(ref hash, BackendVersion);
            MaterialProgramHashUtility.Add(ref hash, (int) Topology);
            MaterialProgramHashUtility.Add(ref hash, (int) PhysicalContract);
            MaterialProgramHashUtility.Add(ref hash, BindingHash);
            MaterialProgramHashUtility.Add(ref hash, EntryPoint);
            MaterialProgramHashUtility.Add(ref hash, Source);
            return hash;
        }

        private static string NormalizeLineEndings(string value)
        {
            return value.Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }

    internal static class MaterialSurfaceHlslBackend
    {
        private const string MaterialVariable = "materialParameters";
        private const string ContextVariable = "context";

        internal static MaterialSurfaceHlslArtifact Compile(
            MaterialIRModule module,
            MaterialProgramLoweringResult lowering)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));
            if (lowering == null)
                throw new ArgumentNullException(nameof(lowering));
            if (!ReferenceEquals(module.Values, lowering.SurfaceProgram.StageLIR.Values))
            {
                throw new ArgumentException(
                    "Surface Stage LIR does not belong to the material module.",
                    nameof(lowering));
            }
            if (lowering.SelectionKey.BackendKind != MaterialProgramBackendKind.NativeTemplate)
            {
                throw new NotSupportedException(
                    $"Surface HLSL backend does not support '{lowering.SelectionKey.BackendKind}'.");
            }

            MaterialStageLIR stageLIR = lowering.SurfaceProgram.StageLIR;
            MaterialIRVerifier.VerifyStageLIR(stageLIR).ThrowIfInvalid();
            if (stageLIR.Stage != MaterialEvaluationStage.Surface
                || stageLIR.ExecutionModel != MaterialStageExecutionModel.VisibilityResolve
                || stageLIR.DerivativeProvider != MaterialStageDerivativeProvider.VisibilityBuffer)
            {
                throw new NotSupportedException(
                    "Surface AOT HLSL requires VisibilityResolve Stage LIR with VisibilityBuffer derivatives.");
            }

            MaterialNativeTemplateLayoutSchema schema =
                lowering.CatalogEntry.LayoutSchema;
            MaterialSurfaceHlslPhysicalContract physicalContract =
                GetPhysicalContract(schema);
            ValidateResourceUses(stageLIR);

            string entryPoint = GetEntryPoint(lowering.SelectionKey.Topology);
            var builder = new StringBuilder(Math.Max(2048, stageLIR.NodeCount * 96));
            builder.Append("// Surface AOT HLSL artifact v")
                .Append(MaterialProgramContract.SurfaceHlslArtifactVersion)
                .Append(", backend v")
                .Append(MaterialProgramContract.SurfaceHlslBackendVersion)
                .AppendLine(".");
            AppendFunctionSignature(builder, entryPoint, physicalContract);
            builder.AppendLine("{");
            AppendNodes(builder, stageLIR, schema, physicalContract);
            AppendOutput(builder, module, stageLIR, lowering.SelectionKey.Topology);
            builder.AppendLine("}");

            return new MaterialSurfaceHlslArtifact(
                entryPoint,
                builder.ToString(),
                lowering.SelectionKey.Topology,
                physicalContract,
                ComputeBindingHash(schema));
        }

        private static void AppendFunctionSignature(
            StringBuilder builder,
            string entryPoint,
            MaterialSurfaceHlslPhysicalContract physicalContract)
        {
            builder.Append("VividAOTSurfaceProgramOutput ")
                .Append(entryPoint)
                .AppendLine("(");
            builder.Append("    const ")
                .Append(physicalContract == MaterialSurfaceHlslPhysicalContract.LegacySingleSlab
                    ? "VividMaterialData"
                    : "VividDualSlabMaterialData")
                .Append(' ')
                .Append(MaterialVariable)
                .AppendLine(",");
            builder.AppendLine("    const VividSurfaceBindingData surfaceBinding0,");
            if (physicalContract == MaterialSurfaceHlslPhysicalContract.DualSlab)
                builder.AppendLine("    const VividSurfaceBindingData surfaceBinding1,");
            builder.AppendLine("    const VividAOTSurfaceContext context)");
        }

        private static void AppendNodes(
            StringBuilder builder,
            MaterialStageLIR stageLIR,
            MaterialNativeTemplateLayoutSchema schema,
            MaterialSurfaceHlslPhysicalContract physicalContract)
        {
            for (int nodeIndex = 0; nodeIndex < stageLIR.Nodes.Count; nodeIndex++)
            {
                MaterialStageLIRNode node = stageLIR.Nodes[nodeIndex];
                if (node.Opcode == MaterialStageLIROpcode.TextureResource)
                    continue;

                if (node.Opcode == MaterialStageLIROpcode.TextureSampleGrad)
                {
                    AppendTextureSample(
                        builder,
                        stageLIR,
                        node,
                        nodeIndex,
                        schema,
                        physicalContract);
                    continue;
                }

                builder.Append("    const ")
                    .Append(GetHlslType(node.Type))
                    .Append(' ')
                    .Append(GetValueName(nodeIndex))
                    .Append(" = ")
                    .Append(GetNodeExpression(
                        stageLIR,
                        node,
                        schema,
                        physicalContract))
                    .AppendLine(";");
            }
            builder.AppendLine();
        }

        private static string GetNodeExpression(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node,
            MaterialNativeTemplateLayoutSchema schema,
            MaterialSurfaceHlslPhysicalContract physicalContract)
        {
            switch (node.Opcode)
            {
                case MaterialStageLIROpcode.StageInput:
                    return GetStageInputExpression((MaterialStageInput) node.Semantic);
                case MaterialStageLIROpcode.Constant:
                    return FormatConstant(node.Type, node.Constant);
                case MaterialStageLIROpcode.Parameter:
                    return GetParameterExpression(
                        stageLIR,
                        node.Semantic,
                        schema,
                        physicalContract);
                case MaterialStageLIROpcode.Add:
                    return Binary(node, "+");
                case MaterialStageLIROpcode.Multiply:
                    return Binary(node, "*");
                case MaterialStageLIROpcode.Subtract:
                    return Binary(node, "-");
                case MaterialStageLIROpcode.Divide:
                    return Binary(node, "/");
                case MaterialStageLIROpcode.Lerp:
                    return Call("lerp", node, 3);
                case MaterialStageLIROpcode.Select:
                    return $"({Value(node.Operand0)} ? {Value(node.Operand1)} : {Value(node.Operand2)})";
                case MaterialStageLIROpcode.Swizzle:
                    return GetSwizzleExpression(node);
                case MaterialStageLIROpcode.Compose:
                    return Compose(node);
                case MaterialStageLIROpcode.Min:
                    return Call("min", node, 2);
                case MaterialStageLIROpcode.Max:
                    return Call("max", node, 2);
                case MaterialStageLIROpcode.Saturate:
                    return Call("saturate", node, 1);
                case MaterialStageLIROpcode.OneMinus:
                    return $"(1.0f - {Value(node.Operand0)})";
                case MaterialStageLIROpcode.Dot:
                    return Call("dot", node, 2);
                case MaterialStageLIROpcode.Normalize:
                    return Call("normalize", node, 1);
                case MaterialStageLIROpcode.Compare:
                    return GetCompareExpression(stageLIR, node);
                default:
                    throw new NotSupportedException(
                        $"Surface AOT HLSL does not support Stage LIR opcode '{node.Opcode}'.");
            }
        }

        private static void AppendTextureSample(
            StringBuilder builder,
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode sample,
            int sampleIndex,
            MaterialNativeTemplateLayoutSchema schema,
            MaterialSurfaceHlslPhysicalContract physicalContract)
        {
            MaterialStageLIRNode resourceNode = stageLIR.Nodes[sample.Operand0];
            if (!stageLIR.Values.TryGetResourceDeclaration(
                    resourceNode.Semantic,
                    out MaterialResourceDeclaration declaration)
                || !schema.TryGetResourceBinding(
                    declaration,
                    out MaterialNativeResourceBinding binding))
            {
                throw new NotSupportedException(
                    $"Surface texture declaration @{resourceNode.Semantic} has no native binding.");
            }

            GetResourceExpressions(
                binding.Target,
                physicalContract,
                out string surfaceBinding,
                out string slabData,
                out string sampleFunction);
            string contextName = $"vivid_sample_context_{sampleIndex}";
            string uvName = $"vivid_sample_uv_{sampleIndex}";
            string ddxName = $"vivid_sample_ddx_{sampleIndex}";
            string ddyName = $"vivid_sample_ddy_{sampleIndex}";
            builder.Append("    const VividSlabMaterialData vivid_sample_slab_")
                .Append(sampleIndex)
                .Append(" = ")
                .Append(slabData)
                .AppendLine(";");
            builder.Append("    const float2 ").Append(uvName).Append(" = ")
                .Append(Value(sample.Operand1))
                .Append(" * vivid_sample_slab_").Append(sampleIndex)
                .Append(".TextureTilingOffset.xy + vivid_sample_slab_")
                .Append(sampleIndex).AppendLine(".TextureTilingOffset.zw;");
            builder.Append("    const float2 ").Append(ddxName).Append(" = ")
                .Append(Value(sample.Operand2))
                .Append(" * vivid_sample_slab_").Append(sampleIndex)
                .AppendLine(".TextureTilingOffset.xy;");
            builder.Append("    const float2 ").Append(ddyName).Append(" = ")
                .Append(Value(sample.Operand3))
                .Append(" * vivid_sample_slab_").Append(sampleIndex)
                .AppendLine(".TextureTilingOffset.xy;");
            builder.Append("    const VividSurfaceSampleContext ")
                .Append(contextName)
                .Append(" = VividCreateSurfaceSampleContextGrad(")
                .Append(surfaceBinding).Append(", ")
                .Append(uvName).Append(", ")
                .Append(ddxName).Append(", ")
                .Append(ddyName).Append(", ")
                .Append(ContextVariable).AppendLine(".PositionCS);");
            builder.Append("    const float4 ")
                .Append(GetValueName(sampleIndex)).Append(" = ")
                .Append(sampleFunction).Append('(')
                .Append(surfaceBinding).Append(", ")
                .Append(contextName).AppendLine(");");
        }

        private static void AppendOutput(
            StringBuilder builder,
            MaterialIRModule module,
            MaterialStageLIR stageLIR,
            MaterialProgramTopologySpecialization topology)
        {
            builder.AppendLine("    VividAOTSurfaceProgramOutput output = (VividAOTSurfaceProgramOutput) 0;");
            ClosureExpressionNode root =
                module.ClosureGraph.GetNode(module.SurfaceClosure);
            if (root.Opcode == ClosureExpressionOpcode.Slab)
            {
                if (topology != MaterialProgramTopologySpecialization.SingleSlab)
                    throw new InvalidOperationException("Surface topology and closure root disagree.");
                AppendSlabOutput(builder, stageLIR, root.Slab, "BaseSlab");
                builder.AppendLine("    output.ClosureCount = 1u;");
                builder.AppendLine("    output.LayerOperator = 0u;");
            }
            else
            {
                if (root.Operand0 < 0
                    || root.Operand1 < 0
                    || module.ClosureGraph.Nodes[root.Operand0].Opcode
                        != ClosureExpressionOpcode.Slab
                    || module.ClosureGraph.Nodes[root.Operand1].Opcode
                        != ClosureExpressionOpcode.Slab)
                {
                    throw new NotSupportedException(
                        "Surface AOT HLSL currently supports at most two direct Slab operands.");
                }

                ClosureExpressionOpcode expectedOpcode =
                    topology == MaterialProgramTopologySpecialization.HorizontalMix
                        ? ClosureExpressionOpcode.HorizontalMix
                        : topology == MaterialProgramTopologySpecialization.VerticalLayer
                            ? ClosureExpressionOpcode.VerticalLayer
                            : ClosureExpressionOpcode.Slab;
                if (root.Opcode != expectedOpcode)
                    throw new InvalidOperationException("Surface topology and closure root disagree.");

                AppendSlabOutput(
                    builder,
                    stageLIR,
                    module.ClosureGraph.Nodes[root.Operand0].Slab,
                    "BaseSlab");
                AppendSlabOutput(
                    builder,
                    stageLIR,
                    module.ClosureGraph.Nodes[root.Operand1].Slab,
                    "TopSlab");
                RequireType(root.Weight, MaterialValueType.Float, "closure weight");
                builder.Append("    output.LayerWeight = ")
                    .Append(GetMappedValueName(stageLIR, root.Weight))
                    .AppendLine(";");
                builder.AppendLine("    output.ClosureCount = 2u;");
                builder.Append("    output.LayerOperator = ")
                    .Append(topology == MaterialProgramTopologySpecialization.HorizontalMix
                        ? "1u"
                        : "2u")
                    .AppendLine(";");
            }

            RequireType(module.Outputs.Emission, MaterialValueType.Float3, "emission");
            builder.Append("    output.Emission = ")
                .Append(GetMappedValueName(stageLIR, module.Outputs.Emission))
                .AppendLine(";");
            builder.AppendLine("    return output;");
        }

        private static void AppendSlabOutput(
            StringBuilder builder,
            MaterialStageLIR stageLIR,
            in ClosureSlabExpression slab,
            string field)
        {
            RequireType(slab.BaseColor, MaterialValueType.Float4, "Slab base color");
            RequireType(slab.Roughness, MaterialValueType.Float, "Slab roughness");
            RequireType(slab.Metallic, MaterialValueType.Float, "Slab metallic");
            RequireType(slab.Normal, MaterialValueType.Float3, "Slab normal");
            RequireType(slab.Tangent, MaterialValueType.Float4, "Slab tangent");
            AppendOutputAssignment(builder, stageLIR, field, "BaseColor", slab.BaseColor);
            AppendOutputAssignment(builder, stageLIR, field, "PerceptualRoughness", slab.Roughness);
            AppendOutputAssignment(builder, stageLIR, field, "Metallic", slab.Metallic);
            AppendOutputAssignment(builder, stageLIR, field, "NormalWS", slab.Normal);
            AppendOutputAssignment(builder, stageLIR, field, "TangentWS", slab.Tangent);
            builder.Append("    output.").Append(field).Append(".FeatureMask = ")
                .Append(((uint) slab.Features).ToString(CultureInfo.InvariantCulture))
                .AppendLine("u;");
        }

        private static void AppendOutputAssignment(
            StringBuilder builder,
            MaterialStageLIR stageLIR,
            string slabField,
            string valueField,
            MaterialValue value)
        {
            builder.Append("    output.").Append(slabField).Append('.')
                .Append(valueField).Append(" = ")
                .Append(GetMappedValueName(stageLIR, value))
                .AppendLine(";");
        }

        private static string GetParameterExpression(
            MaterialStageLIR stageLIR,
            int declarationIndex,
            MaterialNativeTemplateLayoutSchema schema,
            MaterialSurfaceHlslPhysicalContract physicalContract)
        {
            if (!stageLIR.Values.TryGetParameterDeclaration(
                    declarationIndex,
                    out MaterialParameterDeclaration declaration)
                || !schema.TryGetParameterBinding(
                    declaration,
                    out MaterialNativeParameterBinding binding))
            {
                throw new NotSupportedException(
                    $"Surface parameter declaration @{declarationIndex} has no native binding.");
            }

            string expression = GetRuntimeParameterExpression(
                binding.Target,
                physicalContract);
            switch (binding.Conversion)
            {
                case MaterialParameterStorageConversion.None:
                    return expression;
                case MaterialParameterStorageConversion.Float3ToFloat4:
                    return expression + ".xyz";
                default:
                    throw new NotSupportedException(
                        $"Surface parameter conversion '{binding.Conversion}' is not supported by HLSL codegen.");
            }
        }

        private static string GetRuntimeParameterExpression(
            MaterialRuntimeParameter target,
            MaterialSurfaceHlslPhysicalContract physicalContract)
        {
            if (physicalContract == MaterialSurfaceHlslPhysicalContract.LegacySingleSlab)
            {
                switch (target)
                {
                    case MaterialRuntimeParameter.BaseColor: return MaterialVariable + ".AlbedoColor";
                    case MaterialRuntimeParameter.Emission: return MaterialVariable + ".Emission";
                    case MaterialRuntimeParameter.Roughness: return MaterialVariable + ".Roughness";
                    case MaterialRuntimeParameter.Metallic: return MaterialVariable + ".Metallic";
                    case MaterialRuntimeParameter.AlphaClipThreshold:
                        return MaterialVariable + ".AlphaClipThreshold";
                    default:
                        throw new NotSupportedException(
                            $"Legacy Surface HLSL has no field mapping for '{target}'.");
                }
            }

            switch (target)
            {
                case MaterialRuntimeParameter.BaseColor: return MaterialVariable + ".BaseAlbedoColor";
                case MaterialRuntimeParameter.TopBaseColor: return MaterialVariable + ".TopAlbedoColor";
                case MaterialRuntimeParameter.Emission: return MaterialVariable + ".Emission";
                case MaterialRuntimeParameter.Roughness: return MaterialVariable + ".BaseRoughness";
                case MaterialRuntimeParameter.TopRoughness: return MaterialVariable + ".TopRoughness";
                case MaterialRuntimeParameter.Metallic: return MaterialVariable + ".BaseMetallic";
                case MaterialRuntimeParameter.TopMetallic: return MaterialVariable + ".TopMetallic";
                case MaterialRuntimeParameter.LayerWeight: return MaterialVariable + ".LayerWeight";
                case MaterialRuntimeParameter.AlphaClipThreshold:
                    return MaterialVariable + ".AlphaClipThreshold";
                default:
                    throw new NotSupportedException(
                        $"Dual-Slab Surface HLSL has no field mapping for '{target}'.");
            }
        }

        private static void GetResourceExpressions(
            MaterialTextureResource target,
            MaterialSurfaceHlslPhysicalContract physicalContract,
            out string surfaceBinding,
            out string slabData,
            out string sampleFunction)
        {
            bool isTop;
            switch (target)
            {
                case MaterialTextureResource.BaseColor:
                    isTop = false;
                    sampleFunction = "VividSampleBaseColorGrad";
                    break;
                case MaterialTextureResource.BaseNormal:
                    isTop = false;
                    sampleFunction = "VividSampleNormalGrad";
                    break;
                case MaterialTextureResource.BaseMask:
                    isTop = false;
                    sampleFunction = "VividSampleMaskGrad";
                    break;
                case MaterialTextureResource.TopBaseColor:
                    isTop = true;
                    sampleFunction = "VividSampleBaseColorGrad";
                    break;
                case MaterialTextureResource.TopNormal:
                    isTop = true;
                    sampleFunction = "VividSampleNormalGrad";
                    break;
                case MaterialTextureResource.TopMask:
                    isTop = true;
                    sampleFunction = "VividSampleMaskGrad";
                    break;
                default:
                    throw new NotSupportedException(
                        $"Surface texture target '{target}' is not supported by HLSL codegen.");
            }

            if (physicalContract == MaterialSurfaceHlslPhysicalContract.LegacySingleSlab)
            {
                if (isTop)
                    throw new NotSupportedException("Legacy Surface HLSL cannot bind a Top Slab texture.");
                surfaceBinding = "surfaceBinding0";
                slabData = $"VividCreateSlabMaterialData({MaterialVariable})";
                return;
            }

            surfaceBinding = isTop ? "surfaceBinding1" : "surfaceBinding0";
            slabData = isTop
                ? $"VividGetTopSlabMaterialData({MaterialVariable})"
                : $"VividGetBaseSlabMaterialData({MaterialVariable})";
        }

        private static MaterialSurfaceHlslPhysicalContract GetPhysicalContract(
            MaterialNativeTemplateLayoutSchema schema)
        {
            if (schema.ParameterLayout.LayoutID
                    == VividMaterialParameterLayoutID.LegacyMaterialData
                && schema.ResourceLayout.LayoutID
                    == VividMaterialResourceLayoutID.LegacySurfaceBinding)
            {
                return MaterialSurfaceHlslPhysicalContract.LegacySingleSlab;
            }
            if (schema.ParameterLayout.LayoutID
                    == VividMaterialParameterLayoutID.DualSlabMaterialData
                && schema.ResourceLayout.LayoutID
                    == VividMaterialResourceLayoutID.DualSurfaceBinding)
            {
                return MaterialSurfaceHlslPhysicalContract.DualSlab;
            }

            throw new NotSupportedException(
                $"Surface AOT HLSL has no native adapter for parameter layout "
                + $"'{schema.ParameterLayout.LayoutID}' and resource layout "
                + $"'{schema.ResourceLayout.LayoutID}'.");
        }

        private static string GetEntryPoint(MaterialProgramTopologySpecialization topology)
        {
            switch (topology)
            {
                case MaterialProgramTopologySpecialization.SingleSlab:
                    return "VividEvaluateAOTSurface_StandardSingleSlab";
                case MaterialProgramTopologySpecialization.HorizontalMix:
                    return "VividEvaluateAOTSurface_DualSlabHorizontalMix";
                case MaterialProgramTopologySpecialization.VerticalLayer:
                    return "VividEvaluateAOTSurface_DualSlabVerticalLayer";
                default:
                    throw new NotSupportedException(
                        $"Surface topology '{topology}' has no AOT HLSL entry point.");
            }
        }

        private static string GetStageInputExpression(MaterialStageInput input)
        {
            switch (input)
            {
                case MaterialStageInput.UV0: return ContextVariable + ".UV0";
                case MaterialStageInput.UV0Ddx: return ContextVariable + ".UV0Ddx";
                case MaterialStageInput.UV0Ddy: return ContextVariable + ".UV0Ddy";
                case MaterialStageInput.GeometryNormalWS:
                    return ContextVariable + ".GeometryNormalWS";
                case MaterialStageInput.GeometryTangentWS:
                    return ContextVariable + ".GeometryTangentWS";
                default:
                    throw new NotSupportedException(
                        $"Surface Stage input '{input}' is not supported by HLSL codegen.");
            }
        }

        private static string GetCompareExpression(
            MaterialStageLIR stageLIR,
            in MaterialStageLIRNode node)
        {
            if (stageLIR.Nodes[node.Operand0].Type != MaterialValueType.Float)
            {
                throw new NotSupportedException(
                    "Surface AOT HLSL only defines scalar Float comparison semantics.");
            }
            string operation;
            switch ((MaterialComparison) node.Semantic)
            {
                case MaterialComparison.Equal: operation = "=="; break;
                case MaterialComparison.NotEqual: operation = "!="; break;
                case MaterialComparison.Less: operation = "<"; break;
                case MaterialComparison.LessOrEqual: operation = "<="; break;
                case MaterialComparison.Greater: operation = ">"; break;
                case MaterialComparison.GreaterOrEqual: operation = ">="; break;
                default:
                    throw new NotSupportedException(
                        $"Surface comparison '{node.Semantic}' is not supported by HLSL codegen.");
            }
            return $"({Value(node.Operand0)} {operation} {Value(node.Operand1)})";
        }

        private static string GetSwizzleExpression(in MaterialStageLIRNode node)
        {
            if (!MaterialSwizzleMask.TryDecode(
                    node.Semantic,
                    out MaterialSwizzleMask mask))
            {
                throw new InvalidOperationException("Verified Surface LIR contains an invalid swizzle.");
            }
            const string components = "xyzw";
            var builder = new StringBuilder(16);
            builder.Append(Value(node.Operand0)).Append('.');
            for (int componentIndex = 0;
                 componentIndex < mask.ComponentCount;
                 componentIndex++)
            {
                builder.Append(components[mask.GetComponent(componentIndex)]);
            }
            return builder.ToString();
        }

        private static string Compose(in MaterialStageLIRNode node)
        {
            return GetHlslType(node.Type) + "(" + JoinOperands(node, node.OperandCount) + ")";
        }

        private static string Binary(in MaterialStageLIRNode node, string operation)
        {
            return $"({Value(node.Operand0)} {operation} {Value(node.Operand1)})";
        }

        private static string Call(
            string function,
            in MaterialStageLIRNode node,
            int operandCount)
        {
            return function + "(" + JoinOperands(node, operandCount) + ")";
        }

        private static string JoinOperands(
            in MaterialStageLIRNode node,
            int operandCount)
        {
            var builder = new StringBuilder(48);
            for (int operandIndex = 0; operandIndex < operandCount; operandIndex++)
            {
                if (operandIndex > 0)
                    builder.Append(", ");
                builder.Append(Value(node.GetOperand(operandIndex)));
            }
            return builder.ToString();
        }

        private static string FormatConstant(MaterialValueType type, float4 constant)
        {
            uint4 bits = math.asuint(constant);
            if (type == MaterialValueType.Bool)
                return bits.x == 0u ? "false" : "true";

            int componentCount = GetComponentCount(type);
            if (componentCount == 1)
                return FormatFloat(bits.x);

            var builder = new StringBuilder(80);
            builder.Append(GetHlslType(type)).Append('(');
            for (int componentIndex = 0; componentIndex < componentCount; componentIndex++)
            {
                if (componentIndex > 0)
                    builder.Append(", ");
                builder.Append(FormatFloat(bits[componentIndex]));
            }
            return builder.Append(')').ToString();
        }

        private static string FormatFloat(uint bits)
        {
            return $"asfloat(0x{bits:X8}u)";
        }

        private static string GetHlslType(MaterialValueType type)
        {
            switch (type)
            {
                case MaterialValueType.Bool: return "bool";
                case MaterialValueType.Float: return "float";
                case MaterialValueType.Float2: return "float2";
                case MaterialValueType.Float3: return "float3";
                case MaterialValueType.Float4: return "float4";
                default:
                    throw new NotSupportedException(
                        $"Material value type '{type}' cannot be emitted as a Surface HLSL local.");
            }
        }

        private static int GetComponentCount(MaterialValueType type)
        {
            switch (type)
            {
                case MaterialValueType.Float: return 1;
                case MaterialValueType.Float2: return 2;
                case MaterialValueType.Float3: return 3;
                case MaterialValueType.Float4: return 4;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private static void ValidateResourceUses(MaterialStageLIR stageLIR)
        {
            for (int resourceIndex = 0; resourceIndex < stageLIR.Nodes.Count; resourceIndex++)
            {
                if (stageLIR.Nodes[resourceIndex].Opcode
                    != MaterialStageLIROpcode.TextureResource)
                {
                    continue;
                }

                for (int nodeIndex = 0; nodeIndex < stageLIR.Nodes.Count; nodeIndex++)
                {
                    MaterialStageLIRNode node = stageLIR.Nodes[nodeIndex];
                    for (int operandIndex = 0;
                         operandIndex < node.OperandCount;
                         operandIndex++)
                    {
                        if (node.GetOperand(operandIndex) != resourceIndex)
                            continue;
                        if (node.Opcode == MaterialStageLIROpcode.TextureSampleGrad
                            && operandIndex == 0)
                        {
                            continue;
                        }
                        throw new NotSupportedException(
                            "Surface Texture2D values may only be used as TextureSampleGrad operand 0.");
                    }
                }
            }
        }

        private static void RequireType(
            MaterialValue value,
            MaterialValueType expectedType,
            string name)
        {
            if (value.Type != expectedType)
            {
                throw new NotSupportedException(
                    $"Surface {name} must be {expectedType}, got {value.Type}.");
            }
        }

        private static string GetMappedValueName(
            MaterialStageLIR stageLIR,
            MaterialValue value)
        {
            return GetValueName(stageLIR.GetValue(value).Index);
        }

        private static string GetValueName(int nodeIndex)
        {
            return "vivid_v" + nodeIndex.ToString("D4", CultureInfo.InvariantCulture);
        }

        private static string Value(int nodeIndex)
        {
            return GetValueName(nodeIndex);
        }

        private static ulong ComputeBindingHash(MaterialNativeTemplateLayoutSchema schema)
        {
            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            MaterialProgramHashUtility.Add(ref hash, (uint) schema.ParameterLayout.LayoutID);
            MaterialProgramHashUtility.Add(ref hash, schema.ParameterLayout.Stride);
            MaterialProgramHashUtility.Add(
                ref hash,
                schema.ParameterLayout.Bindings.Count);
            for (int bindingIndex = 0;
                 bindingIndex < schema.ParameterLayout.Bindings.Count;
                 bindingIndex++)
            {
                MaterialParameterLayoutBinding binding =
                    schema.ParameterLayout.Bindings[bindingIndex];
                MaterialProgramHashUtility.Add(ref hash, (int) binding.Parameter);
                MaterialProgramHashUtility.Add(ref hash, (int) binding.Type);
                MaterialProgramHashUtility.Add(ref hash, binding.ByteOffset);
            }
            MaterialProgramHashUtility.Add(ref hash, schema.ParameterBindings.Count);
            for (int bindingIndex = 0;
                 bindingIndex < schema.ParameterBindings.Count;
                 bindingIndex++)
            {
                MaterialNativeParameterBinding binding =
                    schema.ParameterBindings[bindingIndex];
                MaterialProgramHashUtility.Add(ref hash, binding.Declaration.Symbol);
                MaterialProgramHashUtility.Add(ref hash, (int) binding.Declaration.Type);
                MaterialProgramHashUtility.Add(ref hash, (int) binding.Target);
                MaterialProgramHashUtility.Add(ref hash, (int) binding.Conversion);
            }
            MaterialProgramHashUtility.Add(ref hash, (uint) schema.ResourceLayout.LayoutID);
            MaterialProgramHashUtility.Add(ref hash, schema.ResourceLayout.RecordStride);
            MaterialProgramHashUtility.Add(ref hash, schema.ResourceLayout.RecordCount);
            MaterialProgramHashUtility.Add(
                ref hash,
                schema.ResourceLayout.Bindings.Count);
            for (int bindingIndex = 0;
                 bindingIndex < schema.ResourceLayout.Bindings.Count;
                 bindingIndex++)
            {
                MaterialResourceLayoutBinding binding =
                    schema.ResourceLayout.Bindings[bindingIndex];
                MaterialProgramHashUtility.Add(ref hash, (int) binding.Resource);
                MaterialProgramHashUtility.Add(ref hash, binding.RecordOffset);
                MaterialProgramHashUtility.Add(ref hash, binding.ByteOffset);
            }
            MaterialProgramHashUtility.Add(ref hash, schema.ResourceBindings.Count);
            for (int bindingIndex = 0;
                 bindingIndex < schema.ResourceBindings.Count;
                 bindingIndex++)
            {
                MaterialNativeResourceBinding binding =
                    schema.ResourceBindings[bindingIndex];
                MaterialProgramHashUtility.Add(ref hash, binding.Declaration.Symbol);
                MaterialProgramHashUtility.Add(ref hash, (int) binding.Declaration.Type);
                MaterialProgramHashUtility.Add(ref hash, (int) binding.Target);
            }
            return hash;
        }
    }

    internal static class MaterialSurfaceHlslSourceBuilder
    {
        internal static string BuildSource(
            IReadOnlyList<CompiledMaterialProgram> programs)
        {
            if (programs == null)
                throw new ArgumentNullException(nameof(programs));

            var sortedPrograms = new List<CompiledMaterialProgram>(programs.Count);
            var programIDs = new HashSet<VividMaterialProgramID>();
            for (int programIndex = 0; programIndex < programs.Count; programIndex++)
            {
                CompiledMaterialProgram program = programs[programIndex]
                    ?? throw new ArgumentException(
                        $"Material program collection contains null at index {programIndex}.",
                        nameof(programs));
                if (!programIDs.Add(program.ProgramID))
                {
                    throw new ArgumentException(
                        $"Material program ID '{program.ProgramID}' is emitted more than once.",
                        nameof(programs));
                }
                sortedPrograms.Add(program);
            }
            sortedPrograms.Sort((left, right) =>
                ((uint) left.ProgramID).CompareTo((uint) right.ProgramID));

            var builder = new StringBuilder(Math.Max(4096, sortedPrograms.Count * 3072));
            builder.AppendLine("// <auto-generated by MaterialSurfaceHlslGenerator>");
            builder.AppendLine("// Generated from canonical Surface Stage LIR; do not edit.");
            builder.AppendLine("#ifndef VIVID_MATERIAL_SURFACE_AOT_GENERATED_INCLUDED");
            builder.AppendLine("#define VIVID_MATERIAL_SURFACE_AOT_GENERATED_INCLUDED");
            builder.AppendLine();
            builder.Append("#define VIVID_MATERIAL_SURFACE_HLSL_BACKEND_VERSION ")
                .Append(MaterialProgramContract.SurfaceHlslBackendVersion)
                .AppendLine("u");
            builder.AppendLine();
            AppendAbi(builder);

            var artifacts = new Dictionary<string, MaterialSurfaceHlslArtifact>(
                StringComparer.Ordinal);
            for (int programIndex = 0;
                 programIndex < sortedPrograms.Count;
                 programIndex++)
            {
                MaterialSurfaceHlslArtifact artifact =
                    sortedPrograms[programIndex].SurfaceHlsl;
                if (artifacts.TryGetValue(
                        artifact.EntryPoint,
                        out MaterialSurfaceHlslArtifact existing))
                {
                    if (!existing.PayloadEquals(artifact))
                    {
                        throw new InvalidOperationException(
                            $"Surface HLSL entry point '{artifact.EntryPoint}' has conflicting artifacts.");
                    }
                    continue;
                }
                artifacts.Add(artifact.EntryPoint, artifact);
                builder.Append(artifact.Source);
                if (!artifact.Source.EndsWith("\n", StringComparison.Ordinal))
                    builder.AppendLine();
                builder.AppendLine();
            }

            AppendDispatcher(builder, sortedPrograms);
            builder.AppendLine();
            builder.AppendLine("#endif // VIVID_MATERIAL_SURFACE_AOT_GENERATED_INCLUDED");
            return builder.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static void AppendAbi(StringBuilder builder)
        {
            builder.AppendLine("struct VividAOTSurfaceContext");
            builder.AppendLine("{");
            builder.AppendLine("    float2 UV0;");
            builder.AppendLine("    float2 UV0Ddx;");
            builder.AppendLine("    float2 UV0Ddy;");
            builder.AppendLine("    float3 GeometryNormalWS;");
            builder.AppendLine("    float4 GeometryTangentWS;");
            builder.AppendLine("    float4 PositionCS;");
            builder.AppendLine("};");
            builder.AppendLine();
            builder.AppendLine("struct VividAOTSurfaceSlabValues");
            builder.AppendLine("{");
            builder.AppendLine("    float4 BaseColor;");
            builder.AppendLine("    float PerceptualRoughness;");
            builder.AppendLine("    float Metallic;");
            builder.AppendLine("    float3 NormalWS;");
            builder.AppendLine("    float4 TangentWS;");
            builder.AppendLine("    uint FeatureMask;");
            builder.AppendLine("};");
            builder.AppendLine();
            builder.AppendLine("struct VividAOTSurfaceProgramOutput");
            builder.AppendLine("{");
            builder.AppendLine("    VividAOTSurfaceSlabValues BaseSlab;");
            builder.AppendLine("    VividAOTSurfaceSlabValues TopSlab;");
            builder.AppendLine("    float3 Emission;");
            builder.AppendLine("    float LayerWeight;");
            builder.AppendLine("    uint ClosureCount;");
            builder.AppendLine("    uint LayerOperator;");
            builder.AppendLine("};");
            builder.AppendLine();
        }

        private static void AppendDispatcher(
            StringBuilder builder,
            IReadOnlyList<CompiledMaterialProgram> programs)
        {
            builder.AppendLine("bool VividTryEvaluateAOTSurfaceProgram(");
            builder.AppendLine("    const uint programID,");
            builder.AppendLine("    const VividMaterialData materialParameters,");
            builder.AppendLine("    const VividDualSlabMaterialData dualSlabMaterialParameters,");
            builder.AppendLine("    const VividSurfaceBindingData surfaceBinding0,");
            builder.AppendLine("    const VividSurfaceBindingData surfaceBinding1,");
            builder.AppendLine("    const VividAOTSurfaceContext context,");
            builder.AppendLine("    out VividAOTSurfaceProgramOutput output)");
            builder.AppendLine("{");
            builder.AppendLine("    output = (VividAOTSurfaceProgramOutput) 0;");
            builder.AppendLine("    switch (programID)");
            builder.AppendLine("    {");
            for (int programIndex = 0; programIndex < programs.Count; programIndex++)
            {
                CompiledMaterialProgram program = programs[programIndex];
                MaterialSurfaceHlslArtifact artifact = program.SurfaceHlsl;
                builder.Append("        case ")
                    .Append(((uint) program.ProgramID).ToString(CultureInfo.InvariantCulture))
                    .AppendLine("u:");
                builder.Append("            output = ").Append(artifact.EntryPoint).AppendLine("(");
                builder.Append("                ")
                    .Append(artifact.PhysicalContract
                        == MaterialSurfaceHlslPhysicalContract.LegacySingleSlab
                            ? "materialParameters"
                            : "dualSlabMaterialParameters")
                    .AppendLine(",");
                builder.AppendLine("                surfaceBinding0,");
                if (artifact.PhysicalContract == MaterialSurfaceHlslPhysicalContract.DualSlab)
                    builder.AppendLine("                surfaceBinding1,");
                builder.AppendLine("                context);");
                builder.AppendLine("            return true;");
            }
            builder.AppendLine("        default:");
            builder.AppendLine("            return false;");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }
    }
}
