using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VividRP.Runtime.GPUDriven
{
    [Flags]
    internal enum MaterialFeatureMask
    {
        None = 0,
        AlphaClip = 1 << 0,
    }

    [Flags]
    internal enum MaterialShadingModelMask
    {
        None = 0,
        StandardLit = 1 << 0,
        Unlit = 1 << 1,
    }

    internal readonly struct MaterialOutputRoots
    {
        internal MaterialOutputRoots(
            MaterialValue coverageValue,
            MaterialValue alphaClipThreshold,
            MaterialValue emission)
        {
            CoverageValue = coverageValue;
            AlphaClipThreshold = alphaClipThreshold;
            Emission = emission;
        }

        internal MaterialValue CoverageValue { get; }

        internal MaterialValue AlphaClipThreshold { get; }

        internal MaterialValue Emission { get; }
    }

    internal sealed class MaterialValueSlice
    {
        private readonly IReadOnlyList<int> m_NodeIndices;
        private readonly IReadOnlyList<MaterialValue> m_Roots;

        internal MaterialValueSlice(MaterialValueIR values, params MaterialValue[] roots)
        {
            Values = values ?? throw new ArgumentNullException(nameof(values));
            if (!Values.IsFrozen)
                throw new InvalidOperationException("Material value IR must be frozen before slicing.");
            if (roots == null)
                throw new ArgumentNullException(nameof(roots));

            var rootValues = new MaterialValue[roots.Length];
            Array.Copy(roots, rootValues, roots.Length);
            m_Roots = Array.AsReadOnly(rootValues);

            var reachable = new bool[Values.NodeCount];
            var pending = new Stack<int>();
            for (int i = 0; i < m_Roots.Count; i++)
            {
                MaterialValue root = m_Roots[i];
                if (!Values.Owns(root))
                    throw new ArgumentException("Material slice root is not owned by the value IR.", nameof(roots));
                pending.Push(root.Index);
            }

            while (pending.Count > 0)
            {
                int nodeIndex = pending.Pop();
                if (reachable[nodeIndex])
                    continue;

                reachable[nodeIndex] = true;
                MaterialValueNode node = Values.Nodes[nodeIndex];
                PushOperand(pending, node.Operand0);
                PushOperand(pending, node.Operand1);
                PushOperand(pending, node.Operand2);
                PushOperand(pending, node.Operand3);
            }

            var nodeIndices = new List<int>();
            for (int i = 0; i < reachable.Length; i++)
            {
                if (reachable[i])
                    nodeIndices.Add(i);
            }
            m_NodeIndices = nodeIndices.AsReadOnly();
        }

        internal MaterialValueIR Values { get; }

        internal IReadOnlyList<int> NodeIndices => m_NodeIndices;

        internal IReadOnlyList<MaterialValue> Roots => m_Roots;

        internal int NodeCount => m_NodeIndices.Count;

        internal bool Contains(MaterialValue value)
        {
            if (!Values.Owns(value))
                return false;

            for (int i = 0; i < m_NodeIndices.Count; i++)
            {
                if (m_NodeIndices[i] == value.Index)
                    return true;
            }
            return false;
        }

        private static void PushOperand(Stack<int> pending, int operand)
        {
            if (operand >= 0)
                pending.Push(operand);
        }
    }

    internal sealed class MaterialIRModule
    {
        private readonly string m_DebugDump;

        internal MaterialIRModule(
            MaterialValueIR values,
            in MaterialOutputRoots outputs,
            ClosureExpressionGraph closureGraph,
            MaterialClosure surfaceClosure,
            in ClosureTopologyBudget closureBudget,
            MaterialFeatureMask materialFeatures,
            MaterialShadingModelMask shadingModels)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (closureGraph == null)
                throw new ArgumentNullException(nameof(closureGraph));

            MaterialIRVerificationResult sourceVerification =
                MaterialIRVerifier.VerifyModule(
                    values,
                    outputs,
                    closureGraph,
                    surfaceClosure,
                    closureBudget,
                    materialFeatures,
                    shadingModels);
            sourceVerification.ThrowIfInvalid();
            values.Freeze();
            closureGraph.Freeze();

            CanonicalIR = MaterialIRCanonicalizer.CanonicalizeVerified(
                values,
                outputs,
                closureGraph,
                surfaceClosure,
                closureBudget,
                materialFeatures,
                shadingModels);
            Values = CanonicalIR.Values;
            Outputs = CanonicalIR.Outputs;
            ClosureGraph = CanonicalIR.ClosureGraph;
            SurfaceClosure = CanonicalIR.SurfaceClosure;
            Topology = CanonicalIR.Topology;
            MaterialFeatures = CanonicalIR.MaterialFeatures;
            ShadingModels = CanonicalIR.ShadingModels;

            Verification = MaterialIRVerifier.VerifyModule(
                Values,
                Outputs,
                ClosureGraph,
                SurfaceClosure,
                Topology.Budget,
                MaterialFeatures,
                ShadingModels);
            Verification.ThrowIfInvalid();
            Values.Freeze();
            ClosureGraph.Freeze();
            SemanticHash = new MaterialSemanticHash(
                MaterialProgramContract.IRSchemaVersion,
                MaterialProgramContract.SemanticHashVersion,
                CanonicalIR.PayloadHash);
            m_DebugDump = BuildDebugDump();
        }

        internal CanonicalMaterialIR CanonicalIR { get; }

        internal MaterialValueIR Values { get; }

        internal MaterialOutputRoots Outputs { get; }

        internal ClosureExpressionGraph ClosureGraph { get; }

        internal MaterialClosure SurfaceClosure { get; }

        internal ClosureTopology Topology { get; }

        internal MaterialFeatureMask MaterialFeatures { get; }

        internal MaterialShadingModelMask ShadingModels { get; }

        internal MaterialIRVerificationResult Verification { get; }

        internal MaterialSemanticHash SemanticHash { get; }

        internal ulong StructuralHash => SemanticHash.Value;

        internal string GetDebugDump()
        {
            return m_DebugDump;
        }

        internal MaterialValueSlice CreateValueSlice(params MaterialValue[] roots)
        {
            return new MaterialValueSlice(Values, roots);
        }

        internal MaterialStageLIR CreateStageLIR(
            MaterialEvaluationStage stage,
            params MaterialValue[] roots)
        {
            return MaterialStageLIRLowerer.Lower(
                CreateValueSlice(roots),
                stage);
        }

        private string BuildDebugDump()
        {
            var builder = new StringBuilder();
            builder.Append("canonical_material_ir version=")
                .Append(MaterialProgramContract.CanonicalIRVersion)
                .Append(" payload_bytes=").Append(CanonicalIR.PayloadLength)
                .Append(" hash=0x")
                .AppendLine(StructuralHash.ToString("X16", CultureInfo.InvariantCulture));
            builder.Append("semantic_identity ")
                .AppendLine(SemanticHash.ToString());
            builder.AppendLine("declarations:");
            for (int i = 0; i < Values.ParameterDeclarations.Count; i++)
            {
                MaterialParameterDeclaration declaration =
                    Values.ParameterDeclarations[i];
                builder.Append("  parameter @p").Append(i)
                    .Append(' ').Append(declaration.Symbol)
                    .Append(':').Append(declaration.Type)
                    .AppendLine();
            }
            for (int i = 0; i < Values.ResourceDeclarations.Count; i++)
            {
                MaterialResourceDeclaration declaration =
                    Values.ResourceDeclarations[i];
                builder.Append("  resource @r").Append(i)
                    .Append(' ').Append(declaration.Symbol)
                    .Append(':').Append(declaration.Type)
                    .Append('/').Append(declaration.SampleClass)
                    .AppendLine();
            }
            builder.AppendLine("values:");
            for (int i = 0; i < Values.Nodes.Count; i++)
            {
                MaterialValueNode node = Values.Nodes[i];
                builder.Append("  %").Append(i)
                    .Append(':').Append(node.Type)
                    .Append(" = ").Append(FormatOpcode(node));
                AppendOperands(builder, node);
                builder.AppendLine();
            }

            builder.AppendLine("outputs:");
            builder.Append("  coverage=%").Append(Outputs.CoverageValue.Index).AppendLine();
            builder.Append("  alpha_clip_threshold=%")
                .Append(Outputs.AlphaClipThreshold.Index)
                .AppendLine();
            builder.Append("  emission=%").Append(Outputs.Emission.Index).AppendLine();
            builder.Append("material_features=").Append(MaterialFeatures).AppendLine();
            builder.Append("shading_models=").Append(ShadingModels).AppendLine();
            builder.Append("surface_closure=@c")
                .Append(SurfaceClosure.Index)
                .AppendLine();
            builder.Append("closure_graph: nodes=").Append(ClosureGraph.NodeCount)
                .Append(" closures=").Append(Topology.ClosureCount)
                .Append(" operators=").Append(Topology.OperatorCount)
                .AppendLine();

            for (int i = 0; i < ClosureGraph.Nodes.Count; i++)
            {
                ClosureExpressionNode node = ClosureGraph.Nodes[i];
                builder.Append("  @c").Append(i).Append(" = ");
                if (node.Opcode == ClosureExpressionOpcode.Slab)
                {
                    ClosureSlabExpression slab = node.Slab;
                    builder.Append("slab")
                        .Append(" base_color=%").Append(slab.BaseColor.Index)
                        .Append(" roughness=%").Append(slab.Roughness.Index)
                        .Append(" metallic=%").Append(slab.Metallic.Index)
                        .Append(" normal=%").Append(slab.Normal.Index)
                        .Append(" tangent=%").Append(slab.Tangent.Index)
                        .Append(" features=").Append(slab.Features)
                        .AppendLine();
                    continue;
                }

                string firstRole = node.Opcode == ClosureExpressionOpcode.VerticalLayer
                    ? "bottom"
                    : "background";
                string secondRole = node.Opcode == ClosureExpressionOpcode.VerticalLayer
                    ? "top"
                    : "foreground";
                builder.Append(node.Opcode)
                    .Append(' ').Append(firstRole).Append("=@c").Append(node.Operand0)
                    .Append(' ').Append(secondRole).Append("=@c").Append(node.Operand1)
                    .Append(" weight=%").Append(node.Weight.Index)
                    .AppendLine();
            }
            builder.Append("topology_projection: normal_bases=")
                .Append(Topology.NormalBases.Count)
                .Append(" closures=").Append(Topology.ClosureCount)
                .Append(" operators=").Append(Topology.OperatorCount)
                .AppendLine();
            return builder.ToString();
        }

        private string FormatOpcode(in MaterialValueNode node)
        {
            switch (node.Opcode)
            {
                case MaterialValueOpcode.Constant:
                    return "constant " + FormatConstant(node);
                case MaterialValueOpcode.ExternalInput:
                    return "external_input " + (MaterialExternalInput) node.Semantic;
                case MaterialValueOpcode.Parameter:
                    return "parameter "
                        + Values.ParameterDeclarations[node.Semantic].Symbol;
                case MaterialValueOpcode.TextureResource:
                    return "texture_resource "
                        + Values.ResourceDeclarations[node.Semantic].Symbol;
                case MaterialValueOpcode.Swizzle:
                    return "swizzle " + FormatSwizzle(node.Semantic);
                case MaterialValueOpcode.Compare:
                    return "compare " + (MaterialComparison) node.Semantic;
                default:
                    return node.Opcode.ToString();
            }
        }

        private static string FormatSwizzle(int packedMask)
        {
            if (!MaterialSwizzleMask.TryDecode(packedMask, out MaterialSwizzleMask mask))
                return "<invalid>";

            const string components = "xyzw";
            var builder = new StringBuilder(".");
            for (int i = 0; i < mask.ComponentCount; i++)
                builder.Append(components[mask.GetComponent(i)]);
            return builder.ToString();
        }

        private static string FormatConstant(in MaterialValueNode node)
        {
            int componentCount = GetComponentCount(node.Type);
            var builder = new StringBuilder("(");
            for (int i = 0; i < componentCount; i++)
            {
                if (i > 0)
                    builder.Append(',');
                builder.Append(node.Constant[i].ToString("R", CultureInfo.InvariantCulture));
            }
            return builder.Append(')').ToString();
        }

        private static int GetComponentCount(MaterialValueType type)
        {
            switch (type)
            {
                case MaterialValueType.Bool:
                case MaterialValueType.Float:
                    return 1;
                case MaterialValueType.Float2:
                    return 2;
                case MaterialValueType.Float3:
                    return 3;
                case MaterialValueType.Float4:
                    return 4;
                default:
                    return 0;
            }
        }

        private static void AppendOperands(StringBuilder builder, in MaterialValueNode node)
        {
            int[] operands = { node.Operand0, node.Operand1, node.Operand2, node.Operand3 };
            bool hasOperand = false;
            foreach (int operand in operands)
            {
                if (operand < 0)
                    continue;

                builder.Append(hasOperand ? ", %" : " %").Append(operand);
                hasOperand = true;
            }
        }
    }
}
