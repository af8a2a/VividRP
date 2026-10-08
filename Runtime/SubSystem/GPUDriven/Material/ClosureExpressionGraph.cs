using System;
using System.Collections.Generic;

namespace VividRP.Runtime.GPUDriven
{
    internal enum ClosureExpressionOpcode
    {
        Slab = 0,
        HorizontalMix = 1,
        VerticalLayer = 2,
        OpenPBROpaque = 3,
    }

    internal readonly struct MaterialClosure : IEquatable<MaterialClosure>
    {
        internal MaterialClosure(ClosureExpressionGraph owner, int index)
        {
            Owner = owner;
            Index = index;
        }

        internal ClosureExpressionGraph Owner { get; }

        internal int Index { get; }

        internal bool IsValid => Owner != null && Index >= 0;

        public bool Equals(MaterialClosure other)
        {
            return ReferenceEquals(Owner, other.Owner) && Index == other.Index;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialClosure other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Owner != null ? Owner.GetHashCode() : 0;
                return (hashCode * 397) ^ Index;
            }
        }

        public static bool operator ==(MaterialClosure left, MaterialClosure right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MaterialClosure left, MaterialClosure right)
        {
            return !left.Equals(right);
        }
    }

    internal readonly struct ClosureSlabExpression
    {
        internal ClosureSlabExpression(
            MaterialValue baseColor,
            MaterialValue roughness,
            MaterialValue metallic,
            MaterialValue normal,
            MaterialValue tangent,
            ClosureFeatureMask features)
        {
            BaseColor = baseColor;
            Roughness = roughness;
            Metallic = metallic;
            Normal = normal;
            Tangent = tangent;
            Features = features;
        }

        internal MaterialValue BaseColor { get; }

        internal MaterialValue Roughness { get; }

        internal MaterialValue Metallic { get; }

        internal MaterialValue Normal { get; }

        internal MaterialValue Tangent { get; }

        internal ClosureFeatureMask Features { get; }
    }

    internal readonly struct ClosureOpenPBROpaqueExpression
    {
        internal ClosureOpenPBROpaqueExpression(
            MaterialValue baseWeight,
            MaterialValue baseColor,
            MaterialValue baseDiffuseRoughness,
            MaterialValue baseMetalness,
            MaterialValue specularWeight,
            MaterialValue specularColor,
            MaterialValue specularRoughness,
            MaterialValue specularIor,
            MaterialValue normalWS,
            MaterialValue emissionLuminance,
            MaterialValue emissionColor)
        {
            BaseWeight = baseWeight;
            BaseColor = baseColor;
            BaseDiffuseRoughness = baseDiffuseRoughness;
            BaseMetalness = baseMetalness;
            SpecularWeight = specularWeight;
            SpecularColor = specularColor;
            SpecularRoughness = specularRoughness;
            SpecularIor = specularIor;
            NormalWS = normalWS;
            EmissionLuminance = emissionLuminance;
            EmissionColor = emissionColor;
        }

        internal MaterialValue BaseWeight { get; }
        internal MaterialValue BaseColor { get; }
        internal MaterialValue BaseDiffuseRoughness { get; }
        internal MaterialValue BaseMetalness { get; }
        internal MaterialValue SpecularWeight { get; }
        internal MaterialValue SpecularColor { get; }
        internal MaterialValue SpecularRoughness { get; }
        internal MaterialValue SpecularIor { get; }
        internal MaterialValue NormalWS { get; }
        internal MaterialValue EmissionLuminance { get; }
        internal MaterialValue EmissionColor { get; }

        // The frozen semantic order also defines canonical payload field order.
        internal MaterialValue GetValue(int fieldIndex)
        {
            switch ((OpenPBROpaqueFieldSemantic) fieldIndex)
            {
                case OpenPBROpaqueFieldSemantic.BaseWeight: return BaseWeight;
                case OpenPBROpaqueFieldSemantic.BaseColor: return BaseColor;
                case OpenPBROpaqueFieldSemantic.BaseDiffuseRoughness: return BaseDiffuseRoughness;
                case OpenPBROpaqueFieldSemantic.BaseMetalness: return BaseMetalness;
                case OpenPBROpaqueFieldSemantic.SpecularWeight: return SpecularWeight;
                case OpenPBROpaqueFieldSemantic.SpecularColor: return SpecularColor;
                case OpenPBROpaqueFieldSemantic.SpecularRoughness: return SpecularRoughness;
                case OpenPBROpaqueFieldSemantic.SpecularIor: return SpecularIor;
                case OpenPBROpaqueFieldSemantic.NormalWS: return NormalWS;
                case OpenPBROpaqueFieldSemantic.EmissionLuminance: return EmissionLuminance;
                case OpenPBROpaqueFieldSemantic.EmissionColor: return EmissionColor;
                default: throw new ArgumentOutOfRangeException(nameof(fieldIndex));
            }
        }

        internal static MaterialValueType GetFieldType(int fieldIndex)
        {
            if ((uint) fieldIndex >= OpenPBROpaqueContract.FieldCount)
                throw new ArgumentOutOfRangeException(nameof(fieldIndex));
            return fieldIndex == (int) OpenPBROpaqueFieldSemantic.BaseColor
                || fieldIndex == (int) OpenPBROpaqueFieldSemantic.SpecularColor
                || fieldIndex == (int) OpenPBROpaqueFieldSemantic.NormalWS
                || fieldIndex == (int) OpenPBROpaqueFieldSemantic.EmissionColor
                ? MaterialValueType.Float3
                : MaterialValueType.Float;
        }
    }

    internal readonly struct ClosureExpressionNode
    {
        internal ClosureExpressionNode(
            ClosureExpressionOpcode opcode,
            in ClosureSlabExpression slab,
            int operand0,
            int operand1,
            MaterialValue weight)
        {
            Opcode = opcode;
            Slab = slab;
            OpenPBROpaque = default;
            Operand0 = operand0;
            Operand1 = operand1;
            Weight = weight;
        }

        internal ClosureExpressionNode(
            in ClosureOpenPBROpaqueExpression openPBROpaque)
        {
            Opcode = ClosureExpressionOpcode.OpenPBROpaque;
            Slab = default;
            OpenPBROpaque = openPBROpaque;
            Operand0 = -1;
            Operand1 = -1;
            Weight = default;
        }

        internal ClosureExpressionOpcode Opcode { get; }

        internal ClosureSlabExpression Slab { get; }

        internal ClosureOpenPBROpaqueExpression OpenPBROpaque { get; }

        internal int Operand0 { get; }

        internal int Operand1 { get; }

        internal MaterialValue Weight { get; }
    }

    internal sealed class ClosureExpressionGraph
    {
        private const int InvalidOperand = -1;

        private readonly List<ClosureExpressionNode> m_Nodes = new();
        private readonly IReadOnlyList<ClosureExpressionNode> m_NodesView;

        internal ClosureExpressionGraph(MaterialValueIR valueIR)
        {
            ValueIR = valueIR ?? throw new ArgumentNullException(nameof(valueIR));
            m_NodesView = m_Nodes.AsReadOnly();
        }

        internal MaterialValueIR ValueIR { get; }

        internal IReadOnlyList<ClosureExpressionNode> Nodes => m_NodesView;

        internal int NodeCount => m_Nodes.Count;

        internal bool IsFrozen { get; private set; }

        internal MaterialClosure Slab(in ClosureSlabExpression slab)
        {
            return Emit(new ClosureExpressionNode(
                ClosureExpressionOpcode.Slab,
                slab,
                InvalidOperand,
                InvalidOperand,
                default));
        }

        internal MaterialClosure Slab(
            MaterialValue baseColor,
            MaterialValue roughness,
            MaterialValue metallic,
            MaterialValue normal,
            MaterialValue tangent,
            ClosureFeatureMask features)
        {
            return Slab(new ClosureSlabExpression(
                baseColor,
                roughness,
                metallic,
                normal,
                tangent,
                features));
        }

        internal MaterialClosure OpenPBROpaque(
            in ClosureOpenPBROpaqueExpression inputs)
        {
            return Emit(new ClosureExpressionNode(inputs));
        }

        internal MaterialClosure OpenPBROpaque(
            MaterialValue baseWeight,
            MaterialValue baseColor,
            MaterialValue baseDiffuseRoughness,
            MaterialValue baseMetalness,
            MaterialValue specularWeight,
            MaterialValue specularColor,
            MaterialValue specularRoughness,
            MaterialValue specularIor,
            MaterialValue normalWS,
            MaterialValue emissionLuminance,
            MaterialValue emissionColor)
        {
            return OpenPBROpaque(new ClosureOpenPBROpaqueExpression(
                baseWeight, baseColor, baseDiffuseRoughness, baseMetalness,
                specularWeight, specularColor, specularRoughness, specularIor,
                normalWS, emissionLuminance, emissionColor));
        }

        internal MaterialClosure HorizontalMix(
            MaterialClosure background,
            MaterialClosure foreground,
            MaterialValue weight)
        {
            return EmitOperator(
                ClosureExpressionOpcode.HorizontalMix,
                background,
                foreground,
                weight);
        }

        internal MaterialClosure VerticalLayer(
            MaterialClosure bottom,
            MaterialClosure top,
            MaterialValue weight)
        {
            return EmitOperator(
                ClosureExpressionOpcode.VerticalLayer,
                bottom,
                top,
                weight);
        }

        internal ClosureExpressionNode GetNode(MaterialClosure closure)
        {
            if (!Owns(closure))
            {
                throw new ArgumentException(
                    "Material closure is not owned by this expression graph.",
                    nameof(closure));
            }
            return m_Nodes[closure.Index];
        }

        internal bool Owns(MaterialClosure closure)
        {
            return closure.IsValid
                && ReferenceEquals(closure.Owner, this)
                && closure.Index < m_Nodes.Count;
        }

        internal void Freeze()
        {
            IsFrozen = true;
        }

        internal static ClosureExpressionGraph FromTopology(
            ClosureTopology topology,
            out MaterialClosure root)
        {
            if (topology == null)
                throw new ArgumentNullException(nameof(topology));

            root = default;
            var graph = new ClosureExpressionGraph(topology.ValueIR);
            if (topology.OpenPBROpaqueClosures.Count == 1)
            {
                root = graph.OpenPBROpaque(topology.OpenPBROpaqueClosures[0]);
                return graph;
            }
            var slabClosures = new MaterialClosure[topology.Slabs.Count];
            for (int slabIndex = 0; slabIndex < topology.Slabs.Count; slabIndex++)
            {
                ClosureSlab slab = topology.Slabs[slabIndex];
                ClosureNormalBasis basis = topology.NormalBases[slab.NormalBasisIndex];
                slabClosures[slabIndex] = graph.Slab(
                    slab.BaseColor,
                    slab.Roughness,
                    slab.Metallic,
                    basis.Normal,
                    basis.Tangent,
                    slab.Features);
            }

            if (slabClosures.Length == 1 && topology.Operators.Count == 0)
            {
                root = slabClosures[0];
                return graph;
            }

            if (slabClosures.Length != 2 || topology.Operators.Count != 1)
            {
                throw new InvalidOperationException(
                    "A closure topology must contain one slab or two slabs connected by one operator.");
            }

            ClosureOperator closureOperator = topology.Operators[0];
            MaterialClosure background =
                slabClosures[closureOperator.BackgroundSlabIndex];
            MaterialClosure foreground =
                slabClosures[closureOperator.ForegroundSlabIndex];
            switch (closureOperator.Kind)
            {
                case ClosureOperatorKind.HorizontalMix:
                    root = graph.HorizontalMix(
                        background,
                        foreground,
                        closureOperator.Weight);
                    break;
                case ClosureOperatorKind.VerticalLayer:
                    root = graph.VerticalLayer(
                        background,
                        foreground,
                        closureOperator.Weight);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Closure operator '{closureOperator.Kind}' cannot be migrated to an expression graph.");
            }
            return graph;
        }

        private MaterialClosure EmitOperator(
            ClosureExpressionOpcode opcode,
            MaterialClosure operand0,
            MaterialClosure operand1,
            MaterialValue weight)
        {
            ValidateClosure(operand0, nameof(operand0));
            ValidateClosure(operand1, nameof(operand1));
            return Emit(new ClosureExpressionNode(
                opcode,
                default,
                operand0.Index,
                operand1.Index,
                weight));
        }

        private void ValidateClosure(
            MaterialClosure closure,
            string parameterName)
        {
            if (!Owns(closure))
            {
                throw new ArgumentException(
                    "Material closure is not owned by this expression graph.",
                    parameterName);
            }
        }

        private MaterialClosure Emit(in ClosureExpressionNode node)
        {
            if (IsFrozen)
            {
                throw new InvalidOperationException(
                    "Cannot modify a frozen closure expression graph.");
            }

            MaterialIRVerifier.VerifyCandidateClosureNode(this, node).ThrowIfInvalid();
            int index = m_Nodes.Count;
            m_Nodes.Add(node);
            return new MaterialClosure(this, index);
        }
    }

    internal static class ClosureTopologyLowerer
    {
        internal static ClosureTopology Lower(
            ClosureExpressionGraph graph,
            MaterialClosure root,
            in ClosureTopologyBudget budget)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            if (!graph.Owns(root))
            {
                throw new ArgumentException(
                    "Closure root is not owned by the expression graph.",
                    nameof(root));
            }

            var normalBases = new List<ClosureNormalBasis>();
            var slabs = new List<ClosureSlab>();
            var operators = new List<ClosureOperator>();
            var openPBROpaqueClosures = new List<ClosureOpenPBROpaqueExpression>();
            ClosureExpressionNode rootNode = graph.GetNode(root);
            switch (rootNode.Opcode)
            {
                case ClosureExpressionOpcode.OpenPBROpaque:
                    openPBROpaqueClosures.Add(rootNode.OpenPBROpaque);
                    break;
                case ClosureExpressionOpcode.Slab:
                    AppendSlab(
                        rootNode.Slab,
                        isTop: true,
                        isBottom: true,
                        normalBases,
                        slabs);
                    break;
                case ClosureExpressionOpcode.HorizontalMix:
                    AppendOperatorSlabs(
                        graph,
                        rootNode,
                        ClosureOperatorKind.HorizontalMix,
                        backgroundIsTop: true,
                        backgroundIsBottom: true,
                        foregroundIsTop: true,
                        foregroundIsBottom: true,
                        normalBases,
                        slabs,
                        operators);
                    break;
                case ClosureExpressionOpcode.VerticalLayer:
                    AppendOperatorSlabs(
                        graph,
                        rootNode,
                        ClosureOperatorKind.VerticalLayer,
                        backgroundIsTop: false,
                        backgroundIsBottom: true,
                        foregroundIsTop: true,
                        foregroundIsBottom: false,
                        normalBases,
                        slabs,
                        operators);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Closure expression opcode '{rootNode.Opcode}' cannot be lowered.");
            }

            return new ClosureTopology(
                graph.ValueIR,
                normalBases.ToArray(),
                slabs.ToArray(),
                operators.ToArray(),
                budget,
                openPBROpaqueClosures.ToArray());
        }

        private static void AppendOperatorSlabs(
            ClosureExpressionGraph graph,
            in ClosureExpressionNode operatorNode,
            ClosureOperatorKind operatorKind,
            bool backgroundIsTop,
            bool backgroundIsBottom,
            bool foregroundIsTop,
            bool foregroundIsBottom,
            List<ClosureNormalBasis> normalBases,
            List<ClosureSlab> slabs,
            List<ClosureOperator> operators)
        {
            ClosureExpressionNode background = GetSlabOperand(
                graph,
                operatorNode.Operand0,
                "background");
            ClosureExpressionNode foreground = GetSlabOperand(
                graph,
                operatorNode.Operand1,
                "foreground");
            AppendSlab(
                background.Slab,
                backgroundIsTop,
                backgroundIsBottom,
                normalBases,
                slabs);
            AppendSlab(
                foreground.Slab,
                foregroundIsTop,
                foregroundIsBottom,
                normalBases,
                slabs);
            operators.Add(new ClosureOperator(
                operatorKind,
                backgroundSlabIndex: 0,
                foregroundSlabIndex: 1,
                weight: operatorNode.Weight));
        }

        private static ClosureExpressionNode GetSlabOperand(
            ClosureExpressionGraph graph,
            int operandIndex,
            string role)
        {
            if ((uint) operandIndex >= (uint) graph.NodeCount)
            {
                throw new InvalidOperationException(
                    $"Closure operator {role} operand %{operandIndex} is invalid.");
            }

            ClosureExpressionNode operand = graph.Nodes[operandIndex];
            if (operand.Opcode != ClosureExpressionOpcode.Slab)
            {
                throw new NotSupportedException(
                    "The prototype closure ABI supports only one operator with two direct Slab operands.");
            }
            return operand;
        }

        private static void AppendSlab(
            in ClosureSlabExpression expression,
            bool isTop,
            bool isBottom,
            List<ClosureNormalBasis> normalBases,
            List<ClosureSlab> slabs)
        {
            int normalBasisIndex = FindOrAddNormalBasis(expression, normalBases);
            slabs.Add(new ClosureSlab(
                expression.BaseColor,
                expression.Roughness,
                expression.Metallic,
                normalBasisIndex,
                expression.Features,
                isTop,
                isBottom));
        }

        private static int FindOrAddNormalBasis(
            in ClosureSlabExpression expression,
            List<ClosureNormalBasis> normalBases)
        {
            for (int basisIndex = 0; basisIndex < normalBases.Count; basisIndex++)
            {
                ClosureNormalBasis basis = normalBases[basisIndex];
                if (basis.Normal == expression.Normal
                    && basis.Tangent == expression.Tangent)
                {
                    return basisIndex;
                }
            }

            int index = normalBases.Count;
            normalBases.Add(new ClosureNormalBasis(
                expression.Normal,
                expression.Tangent));
            return index;
        }
    }
}
