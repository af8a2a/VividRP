using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven
{
    [Flags]
    internal enum MaterialFeatureMask
    {
        None = 0,
        AlphaClip = 1 << 0,
        Emission = 1 << 1,
        Unlit = 1 << 2,
    }

    internal readonly struct MaterialOutputRoots
    {
        internal MaterialOutputRoots(
            MaterialValue coverageValue,
            MaterialValue alphaClipThreshold)
        {
            CoverageValue = coverageValue;
            AlphaClipThreshold = alphaClipThreshold;
        }

        internal MaterialValue CoverageValue { get; }

        internal MaterialValue AlphaClipThreshold { get; }
    }

    internal sealed class MaterialValueSlice
    {
        private readonly IReadOnlyList<int> m_NodeIndices;

        internal MaterialValueSlice(MaterialValueIR values, params MaterialValue[] roots)
        {
            Values = values ?? throw new ArgumentNullException(nameof(values));
            if (!Values.IsFrozen)
                throw new InvalidOperationException("Material value IR must be frozen before slicing.");
            if (roots == null)
                throw new ArgumentNullException(nameof(roots));

            var reachable = new bool[Values.NodeCount];
            var pending = new Stack<int>();
            for (int i = 0; i < roots.Length; i++)
            {
                MaterialValue root = roots[i];
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
            ClosureTopology topology,
            MaterialFeatureMask materialFeatures)
        {
            Values = values ?? throw new ArgumentNullException(nameof(values));
            Topology = topology ?? throw new ArgumentNullException(nameof(topology));
            Outputs = outputs;
            MaterialFeatures = materialFeatures;

            Validate();
            Values.Freeze();
            SemanticHash = new MaterialSemanticHash(
                MaterialProgramContract.IRSchemaVersion,
                MaterialProgramContract.SemanticHashVersion,
                ComputeStructuralHash());
            m_DebugDump = BuildDebugDump();
        }

        internal MaterialValueIR Values { get; }

        internal MaterialOutputRoots Outputs { get; }

        internal ClosureTopology Topology { get; }

        internal MaterialFeatureMask MaterialFeatures { get; }

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

        private void Validate()
        {
            if (!ReferenceEquals(Values, Topology.ValueIR))
                throw new ArgumentException("Closure topology must reference the module value IR.");

            RequireOutputType(
                Outputs.CoverageValue,
                MaterialValueType.Float4,
                nameof(MaterialOutputRoots.CoverageValue));
            RequireOutputType(
                Outputs.AlphaClipThreshold,
                MaterialValueType.Float,
                nameof(MaterialOutputRoots.AlphaClipThreshold));
        }

        private void RequireOutputType(
            MaterialValue value,
            MaterialValueType expectedType,
            string outputName)
        {
            if (!Values.Owns(value))
                throw new ArgumentException($"Material output '{outputName}' is not owned by the module value IR.");
            if (value.Type != expectedType)
            {
                throw new ArgumentException(
                    $"Material output '{outputName}' must be {expectedType}, got {value.Type}.");
            }
        }

        private ulong ComputeStructuralHash()
        {
            var valueHashes = new ulong[Values.NodeCount];
            var hasValueHash = new bool[Values.NodeCount];
            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            AddHash(ref hash, MaterialProgramContract.SemanticHashVersion);
            AddValueHash(ref hash, Outputs.CoverageValue, valueHashes, hasValueHash);
            AddValueHash(ref hash, Outputs.AlphaClipThreshold, valueHashes, hasValueHash);
            AddHash(ref hash, (int) MaterialFeatures);

            AddHash(ref hash, Topology.ClosureCount);
            foreach (ClosureSlab slab in Topology.Slabs)
            {
                AddValueHash(ref hash, slab.BaseColor, valueHashes, hasValueHash);
                AddValueHash(ref hash, slab.Roughness, valueHashes, hasValueHash);
                AddValueHash(ref hash, slab.Metallic, valueHashes, hasValueHash);
                ClosureNormalBasis normalBasis = Topology.NormalBases[slab.NormalBasisIndex];
                AddValueHash(ref hash, normalBasis.Normal, valueHashes, hasValueHash);
                AddValueHash(ref hash, normalBasis.Tangent, valueHashes, hasValueHash);
                AddHash(ref hash, (int) slab.Features);
                AddHash(ref hash, slab.IsTop);
                AddHash(ref hash, slab.IsBottom);
            }

            AddHash(ref hash, Topology.OperatorCount);
            foreach (ClosureOperator closureOperator in Topology.Operators)
            {
                AddHash(ref hash, (int) closureOperator.Kind);
                AddHash(ref hash, closureOperator.BackgroundSlabIndex);
                AddHash(ref hash, closureOperator.ForegroundSlabIndex);
                AddValueHash(ref hash, closureOperator.Weight, valueHashes, hasValueHash);
            }
            return hash;
        }

        private void AddValueHash(
            ref ulong hash,
            MaterialValue value,
            ulong[] valueHashes,
            bool[] hasValueHash)
        {
            AddHash(ref hash, ComputeValueHash(value.Index, valueHashes, hasValueHash));
        }

        private ulong ComputeValueHash(
            int nodeIndex,
            ulong[] valueHashes,
            bool[] hasValueHash)
        {
            if (hasValueHash[nodeIndex])
                return valueHashes[nodeIndex];

            MaterialValueNode node = Values.Nodes[nodeIndex];
            ulong hash = MaterialProgramHashUtility.OffsetBasis;
            AddHash(ref hash, (int) node.Opcode);
            AddHash(ref hash, (int) node.Type);
            AddHash(ref hash, node.Semantic);
            uint4 constantBits = math.asuint(node.Constant);
            AddHash(ref hash, constantBits.x);
            AddHash(ref hash, constantBits.y);
            AddHash(ref hash, constantBits.z);
            AddHash(ref hash, constantBits.w);
            AddOperandHash(ref hash, node.Operand0, valueHashes, hasValueHash);
            AddOperandHash(ref hash, node.Operand1, valueHashes, hasValueHash);
            AddOperandHash(ref hash, node.Operand2, valueHashes, hasValueHash);
            AddOperandHash(ref hash, node.Operand3, valueHashes, hasValueHash);

            valueHashes[nodeIndex] = hash;
            hasValueHash[nodeIndex] = true;
            return hash;
        }

        private void AddOperandHash(
            ref ulong hash,
            int operand,
            ulong[] valueHashes,
            bool[] hasValueHash)
        {
            bool isValid = operand >= 0;
            AddHash(ref hash, isValid);
            if (isValid)
                AddHash(ref hash, ComputeValueHash(operand, valueHashes, hasValueHash));
        }

        private string BuildDebugDump()
        {
            var builder = new StringBuilder();
            builder.Append("material_ir_module hash=0x")
                .AppendLine(StructuralHash.ToString("X16", CultureInfo.InvariantCulture));
            builder.Append("semantic_identity ")
                .AppendLine(SemanticHash.ToString());
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
            builder.Append("material_features=").Append(MaterialFeatures).AppendLine();
            builder.Append("topology: closures=").Append(Topology.ClosureCount)
                .Append(" operators=").Append(Topology.OperatorCount)
                .Append(" budget=").Append(Topology.Budget.MaxClosureCount)
                .Append('/').Append(Topology.Budget.MaxOperatorCount)
                .AppendLine();

            for (int i = 0; i < Topology.NormalBases.Count; i++)
            {
                ClosureNormalBasis basis = Topology.NormalBases[i];
                builder.Append("  normal_basis ").Append(i)
                    .Append(" normal=%").Append(basis.Normal.Index)
                    .Append(" tangent=%").Append(basis.Tangent.Index)
                    .AppendLine();
            }

            for (int i = 0; i < Topology.Slabs.Count; i++)
            {
                ClosureSlab slab = Topology.Slabs[i];
                builder.Append("  slab ").Append(i)
                    .Append(" base_color=%").Append(slab.BaseColor.Index)
                    .Append(" roughness=%").Append(slab.Roughness.Index)
                    .Append(" metallic=%").Append(slab.Metallic.Index)
                    .Append(" normal_basis=").Append(slab.NormalBasisIndex)
                    .Append(" features=").Append(slab.Features)
                    .Append(" top=").Append(slab.IsTop ? '1' : '0')
                    .Append(" bottom=").Append(slab.IsBottom ? '1' : '0')
                    .AppendLine();
            }

            for (int i = 0; i < Topology.Operators.Count; i++)
            {
                ClosureOperator closureOperator = Topology.Operators[i];
                builder.Append("  operator ").Append(i)
                    .Append(" kind=").Append(closureOperator.Kind)
                    .Append(" background=").Append(closureOperator.BackgroundSlabIndex)
                    .Append(" foreground=").Append(closureOperator.ForegroundSlabIndex)
                    .Append(" weight=%").Append(closureOperator.Weight.Index)
                    .AppendLine();
            }
            return builder.ToString();
        }

        private static string FormatOpcode(in MaterialValueNode node)
        {
            switch (node.Opcode)
            {
                case MaterialValueOpcode.Constant:
                    return "constant " + FormatConstant(node);
                case MaterialValueOpcode.ExternalInput:
                    return "external_input " + (MaterialExternalInput) node.Semantic;
                case MaterialValueOpcode.Parameter:
                    return "parameter " + (MaterialParameter) node.Semantic;
                case MaterialValueOpcode.TextureResource:
                    return "texture_resource " + (MaterialTextureResource) node.Semantic;
                default:
                    return node.Opcode.ToString();
            }
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

        private static void AddHash(ref ulong hash, bool value)
        {
            MaterialProgramHashUtility.Add(ref hash, value);
        }

        private static void AddHash(ref ulong hash, int value)
        {
            MaterialProgramHashUtility.Add(ref hash, value);
        }

        private static void AddHash(ref ulong hash, uint value)
        {
            MaterialProgramHashUtility.Add(ref hash, value);
        }

        private static void AddHash(ref ulong hash, ulong value)
        {
            MaterialProgramHashUtility.Add(ref hash, value);
        }
    }
}
