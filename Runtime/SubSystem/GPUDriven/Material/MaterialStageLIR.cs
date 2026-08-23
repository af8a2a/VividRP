using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven
{
    internal enum MaterialEvaluationStage
    {
        Coverage = 0,
        Surface = 1,
    }

    internal enum MaterialStageExecutionModel
    {
        RasterFragment = 0,
        VisibilityResolve = 1,
    }

    internal enum MaterialStageDerivativeProvider
    {
        NativeQuad = 0,
        VisibilityBuffer = 1,
    }

    internal enum MaterialStageInput
    {
        UV0 = 0,
        UV0Ddx = 1,
        UV0Ddy = 2,
        GeometryNormalWS = 3,
        GeometryTangentWS = 4,
    }

    internal enum MaterialStageLIROpcode
    {
        StageInput = 0,
        Constant = 1,
        Parameter = 2,
        TextureResource = 3,
        TextureSampleGrad = 4,
        Add = 5,
        Multiply = 6,
        Lerp = 7,
        Select = 8,
        Swizzle = 9,
        Compose = 10,
        Subtract = 11,
        Divide = 12,
        Min = 13,
        Max = 14,
        Saturate = 15,
        OneMinus = 16,
        Dot = 17,
        Normalize = 18,
        Compare = 19,
    }

    internal enum MaterialDerivativeLegalizationRule
    {
        Unsupported = 0,
        Zero = 1,
        StageInput = 2,
        Add = 3,
        Subtract = 4,
        Multiply = 5,
        Swizzle = 6,
        OneMinus = 7,
        Divide = 8,
        Lerp = 9,
        Compose = 10,
        Dot = 11,
        Select = 12,
    }

    internal static class MaterialDerivativeLegalizationRules
    {
        internal static MaterialDerivativeLegalizationRule GetRule(
            MaterialValueOpcode opcode)
        {
            switch (opcode)
            {
                case MaterialValueOpcode.Constant:
                case MaterialValueOpcode.Parameter:
                    return MaterialDerivativeLegalizationRule.Zero;
                case MaterialValueOpcode.ExternalInput:
                    return MaterialDerivativeLegalizationRule.StageInput;
                case MaterialValueOpcode.Add:
                    return MaterialDerivativeLegalizationRule.Add;
                case MaterialValueOpcode.Subtract:
                    return MaterialDerivativeLegalizationRule.Subtract;
                case MaterialValueOpcode.Multiply:
                    return MaterialDerivativeLegalizationRule.Multiply;
                case MaterialValueOpcode.Divide:
                    return MaterialDerivativeLegalizationRule.Divide;
                case MaterialValueOpcode.Lerp:
                    return MaterialDerivativeLegalizationRule.Lerp;
                case MaterialValueOpcode.Select:
                    return MaterialDerivativeLegalizationRule.Select;
                case MaterialValueOpcode.Swizzle:
                    return MaterialDerivativeLegalizationRule.Swizzle;
                case MaterialValueOpcode.Compose:
                    return MaterialDerivativeLegalizationRule.Compose;
                case MaterialValueOpcode.OneMinus:
                    return MaterialDerivativeLegalizationRule.OneMinus;
                case MaterialValueOpcode.Dot:
                    return MaterialDerivativeLegalizationRule.Dot;
                default:
                    return MaterialDerivativeLegalizationRule.Unsupported;
            }
        }
    }

    internal enum MaterialStageUniformity
    {
        Unknown = 0,
        Uniform = 1,
        Varying = 2,
    }

    internal static class MaterialStageUniformityAnalyzer
    {
        internal static MaterialStageUniformity Analyze(
            MaterialValueIR values,
            int nodeIndex,
            MaterialStageUniformity[] states)
        {
            MaterialStageUniformity cached = states[nodeIndex];
            if (cached != MaterialStageUniformity.Unknown)
                return cached;

            MaterialValueNode node = values.Nodes[nodeIndex];
            MaterialStageUniformity result;
            switch (node.Opcode)
            {
                case MaterialValueOpcode.Constant:
                case MaterialValueOpcode.Parameter:
                case MaterialValueOpcode.TextureResource:
                    result = MaterialStageUniformity.Uniform;
                    break;
                case MaterialValueOpcode.Add:
                case MaterialValueOpcode.Multiply:
                case MaterialValueOpcode.Lerp:
                case MaterialValueOpcode.Select:
                case MaterialValueOpcode.Swizzle:
                case MaterialValueOpcode.Compose:
                case MaterialValueOpcode.Subtract:
                case MaterialValueOpcode.Divide:
                case MaterialValueOpcode.Min:
                case MaterialValueOpcode.Max:
                case MaterialValueOpcode.Saturate:
                case MaterialValueOpcode.OneMinus:
                case MaterialValueOpcode.Dot:
                case MaterialValueOpcode.Normalize:
                case MaterialValueOpcode.Compare:
                    result = AreOperandsUniform(values, node, states)
                        ? MaterialStageUniformity.Uniform
                        : MaterialStageUniformity.Varying;
                    break;
                default:
                    result = MaterialStageUniformity.Varying;
                    break;
            }

            states[nodeIndex] = result;
            return result;
        }

        private static bool AreOperandsUniform(
            MaterialValueIR values,
            in MaterialValueNode node,
            MaterialStageUniformity[] states)
        {
            return IsUniformOperand(values, node.Operand0, states)
                && IsUniformOperand(values, node.Operand1, states)
                && IsUniformOperand(values, node.Operand2, states)
                && IsUniformOperand(values, node.Operand3, states);
        }

        private static bool IsUniformOperand(
            MaterialValueIR values,
            int operand,
            MaterialStageUniformity[] states)
        {
            return operand < 0
                || Analyze(values, operand, states) == MaterialStageUniformity.Uniform;
        }
    }

    internal readonly struct MaterialStageValue : IEquatable<MaterialStageValue>
    {
        internal MaterialStageValue(
            MaterialStageLIR owner,
            int index,
            MaterialValueType type)
        {
            Owner = owner;
            Index = index;
            Type = type;
        }

        internal MaterialStageLIR Owner { get; }

        internal int Index { get; }

        internal MaterialValueType Type { get; }

        internal bool IsValid => Owner != null && Index >= 0;

        public bool Equals(MaterialStageValue other)
        {
            return ReferenceEquals(Owner, other.Owner)
                && Index == other.Index
                && Type == other.Type;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialStageValue other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Owner != null ? Owner.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ Index;
                hashCode = (hashCode * 397) ^ (int) Type;
                return hashCode;
            }
        }
    }

    internal readonly struct MaterialStageLIRNode
    {
        internal MaterialStageLIRNode(
            MaterialStageLIROpcode opcode,
            MaterialValueType type,
            int semantic,
            float4 constant,
            int sourceNodeIndex,
            int operandCount,
            int operand0,
            int operand1,
            int operand2,
            int operand3)
        {
            Opcode = opcode;
            Type = type;
            Semantic = semantic;
            Constant = constant;
            SourceNodeIndex = sourceNodeIndex;
            OperandCount = operandCount;
            Operand0 = operand0;
            Operand1 = operand1;
            Operand2 = operand2;
            Operand3 = operand3;
        }

        internal MaterialStageLIROpcode Opcode { get; }

        internal MaterialValueType Type { get; }

        internal int Semantic { get; }

        internal float4 Constant { get; }

        internal int SourceNodeIndex { get; }

        internal int OperandCount { get; }

        internal int Operand0 { get; }

        internal int Operand1 { get; }

        internal int Operand2 { get; }

        internal int Operand3 { get; }

        internal int GetOperand(int operandIndex)
        {
            switch (operandIndex)
            {
                case 0: return Operand0;
                case 1: return Operand1;
                case 2: return Operand2;
                case 3: return Operand3;
                default: throw new ArgumentOutOfRangeException(nameof(operandIndex));
            }
        }
    }

    internal sealed class MaterialStageLIR
    {
        private readonly IReadOnlyList<MaterialStageLIRNode> m_Nodes;
        private readonly IReadOnlyList<MaterialStageValue> m_Roots;
        private readonly int[] m_SourceValueMap;
        private readonly string m_DebugDump;

        internal MaterialStageLIR(
            MaterialEvaluationStage stage,
            MaterialStageExecutionModel executionModel,
            MaterialStageDerivativeProvider derivativeProvider,
            MaterialValueSlice sourceSlice,
            MaterialStageLIRNode[] nodes,
            int[] rootIndices,
            int[] sourceValueMap)
        {
            SourceSlice = sourceSlice
                ?? throw new ArgumentNullException(nameof(sourceSlice));
            if (nodes == null)
                throw new ArgumentNullException(nameof(nodes));
            if (rootIndices == null)
                throw new ArgumentNullException(nameof(rootIndices));
            if (sourceValueMap == null)
                throw new ArgumentNullException(nameof(sourceValueMap));

            Stage = stage;
            ExecutionModel = executionModel;
            DerivativeProvider = derivativeProvider;
            m_Nodes = Array.AsReadOnly((MaterialStageLIRNode[]) nodes.Clone());
            m_SourceValueMap = (int[]) sourceValueMap.Clone();

            var roots = new MaterialStageValue[rootIndices.Length];
            for (int rootIndex = 0; rootIndex < rootIndices.Length; rootIndex++)
            {
                int nodeIndex = rootIndices[rootIndex];
                MaterialValueType rootType = (uint) nodeIndex < (uint) m_Nodes.Count
                    ? m_Nodes[nodeIndex].Type
                    : default;
                roots[rootIndex] = new MaterialStageValue(
                    this,
                    nodeIndex,
                    rootType);
            }
            m_Roots = Array.AsReadOnly(roots);
            m_DebugDump = BuildDebugDump();
        }

        internal MaterialEvaluationStage Stage { get; }

        internal MaterialStageExecutionModel ExecutionModel { get; }

        internal MaterialStageDerivativeProvider DerivativeProvider { get; }

        internal MaterialValueSlice SourceSlice { get; }

        internal MaterialValueIR Values => SourceSlice.Values;

        internal IReadOnlyList<MaterialStageLIRNode> Nodes => m_Nodes;

        internal IReadOnlyList<MaterialStageValue> Roots => m_Roots;

        internal int NodeCount => m_Nodes.Count;

        internal int SourceValueMapCount => m_SourceValueMap.Length;

        internal bool IsFrozen => true;

        internal int GetMappedNodeIndex(int sourceNodeIndex)
        {
            if ((uint) sourceNodeIndex >= (uint) m_SourceValueMap.Length)
                throw new ArgumentOutOfRangeException(nameof(sourceNodeIndex));
            return m_SourceValueMap[sourceNodeIndex];
        }

        internal MaterialStageValue GetValue(MaterialValue sourceValue)
        {
            if (!TryGetValue(sourceValue, out MaterialStageValue value))
            {
                throw new ArgumentException(
                    "Material value is not part of this stage LIR.",
                    nameof(sourceValue));
            }
            return value;
        }

        internal bool TryGetValue(
            MaterialValue sourceValue,
            out MaterialStageValue value)
        {
            if (!Values.Owns(sourceValue)
                || (uint) sourceValue.Index >= (uint) m_SourceValueMap.Length)
            {
                value = default;
                return false;
            }

            int nodeIndex = m_SourceValueMap[sourceValue.Index];
            if ((uint) nodeIndex >= (uint) m_Nodes.Count)
            {
                value = default;
                return false;
            }
            if (m_Nodes[nodeIndex].Type != sourceValue.Type)
            {
                value = default;
                return false;
            }

            value = new MaterialStageValue(this, nodeIndex, m_Nodes[nodeIndex].Type);
            return true;
        }

        internal MaterialStageLIRNode GetNode(MaterialStageValue value)
        {
            if (!Owns(value))
                throw new ArgumentException("Stage value is not owned by this LIR.", nameof(value));
            return m_Nodes[value.Index];
        }

        internal bool Owns(MaterialStageValue value)
        {
            return value.IsValid
                && ReferenceEquals(value.Owner, this)
                && value.Index < m_Nodes.Count
                && m_Nodes[value.Index].Type == value.Type;
        }

        internal string GetDebugDump()
        {
            return m_DebugDump;
        }

        private string BuildDebugDump()
        {
            var builder = new StringBuilder();
            builder.Append("material_stage_lir version=")
                .Append(MaterialProgramContract.StageLIRVersion)
                .Append(" derivative_legalization=")
                .Append(MaterialProgramContract.DerivativeLegalizationVersion)
                .Append(" stage=").Append(Stage)
                .Append(" execution=").Append(ExecutionModel)
                .Append(" derivative_provider=").Append(DerivativeProvider)
                .Append(" nodes=").Append(NodeCount)
                .Append(" roots=").Append(Roots.Count)
                .AppendLine();
            for (int nodeIndex = 0; nodeIndex < Nodes.Count; nodeIndex++)
            {
                MaterialStageLIRNode node = Nodes[nodeIndex];
                builder.Append("  %").Append(nodeIndex)
                    .Append(':').Append(node.Type)
                    .Append(" = ").Append(node.Opcode);
                AppendSemantic(builder, node);
                int operandCount = Math.Min(Math.Max(node.OperandCount, 0), 4);
                for (int operandIndex = 0; operandIndex < operandCount; operandIndex++)
                    builder.Append(operandIndex == 0 ? " %" : ", %")
                        .Append(node.GetOperand(operandIndex));
                builder.Append(" source=%")
                    .Append(node.SourceNodeIndex.ToString(CultureInfo.InvariantCulture))
                    .AppendLine();
            }
            builder.Append("  roots");
            for (int rootIndex = 0; rootIndex < Roots.Count; rootIndex++)
                builder.Append(rootIndex == 0 ? " %" : ", %")
                    .Append(Roots[rootIndex].Index);
            builder.AppendLine();
            return builder.ToString();
        }

        private void AppendSemantic(
            StringBuilder builder,
            in MaterialStageLIRNode node)
        {
            switch (node.Opcode)
            {
                case MaterialStageLIROpcode.StageInput:
                    builder.Append(' ').Append((MaterialStageInput) node.Semantic);
                    break;
                case MaterialStageLIROpcode.Parameter:
                    builder.Append(" @p").Append(node.Semantic);
                    break;
                case MaterialStageLIROpcode.TextureResource:
                    builder.Append(" @r").Append(node.Semantic);
                    break;
                case MaterialStageLIROpcode.Swizzle:
                case MaterialStageLIROpcode.Compare:
                    builder.Append(" semantic=").Append(node.Semantic);
                    break;
            }
        }
    }

    internal static class MaterialStageLIRLowerer
    {
        private const int InvalidOperand = -1;

        internal static MaterialStageLIR Lower(
            MaterialValueSlice sourceSlice,
            MaterialEvaluationStage stage)
        {
            if (sourceSlice == null)
                throw new ArgumentNullException(nameof(sourceSlice));

            MaterialIRVerifier.VerifyStageSlice(sourceSlice, stage).ThrowIfInvalid();
            MaterialStageLIR stageLIR = BuildCanonicalUnchecked(sourceSlice, stage);
            MaterialIRVerifier.VerifyStageLIRStructure(stageLIR).ThrowIfInvalid();
            return stageLIR;
        }

        internal static MaterialStageLIR BuildCanonicalUnchecked(
            MaterialValueSlice sourceSlice,
            MaterialEvaluationStage stage)
        {
            var builder = new Builder(sourceSlice, stage);
            return builder.Build();
        }

        private enum DerivativeStateKind
        {
            Unknown = 0,
            Zero = 1,
            Value = 2,
        }

        private readonly struct DerivativeState
        {
            private DerivativeState(DerivativeStateKind kind, int valueIndex)
            {
                Kind = kind;
                ValueIndex = valueIndex;
            }

            internal static DerivativeState Zero =>
                new DerivativeState(DerivativeStateKind.Zero, InvalidOperand);

            internal static DerivativeState Value(int valueIndex) =>
                new DerivativeState(DerivativeStateKind.Value, valueIndex);

            internal DerivativeStateKind Kind { get; }

            internal int ValueIndex { get; }
        }

        private sealed class Builder
        {
            private readonly MaterialValueSlice m_SourceSlice;
            private readonly MaterialEvaluationStage m_Stage;
            private readonly List<MaterialStageLIRNode> m_Nodes = new();
            private readonly int[] m_SourceValueMap;
            private readonly DerivativeState[] m_DdxStates;
            private readonly DerivativeState[] m_DdyStates;
            private readonly MaterialStageUniformity[] m_UniformityStates;

            internal Builder(
                MaterialValueSlice sourceSlice,
                MaterialEvaluationStage stage)
            {
                m_SourceSlice = sourceSlice;
                m_Stage = stage;
                m_SourceValueMap = CreateIndexMap(sourceSlice.Values.NodeCount);
                m_DdxStates = new DerivativeState[sourceSlice.Values.NodeCount];
                m_DdyStates = new DerivativeState[sourceSlice.Values.NodeCount];
                m_UniformityStates = new MaterialStageUniformity[
                    sourceSlice.Values.NodeCount];
            }

            internal MaterialStageLIR Build()
            {
                for (int i = 0; i < m_SourceSlice.NodeIndices.Count; i++)
                {
                    int sourceNodeIndex = m_SourceSlice.NodeIndices[i];
                    MaterialValueNode sourceNode =
                        m_SourceSlice.Values.Nodes[sourceNodeIndex];
                    m_SourceValueMap[sourceNodeIndex] = sourceNode.Opcode
                        == MaterialValueOpcode.Ddx
                        ? MaterializeDerivative(sourceNode.Operand0, isDdx: true, sourceNodeIndex)
                        : sourceNode.Opcode == MaterialValueOpcode.Ddy
                            ? MaterializeDerivative(sourceNode.Operand0, isDdx: false, sourceNodeIndex)
                            : EmitSourceNode(sourceNode, sourceNodeIndex);
                }

                var rootIndices = new int[m_SourceSlice.Roots.Count];
                for (int rootIndex = 0; rootIndex < m_SourceSlice.Roots.Count; rootIndex++)
                    rootIndices[rootIndex] = m_SourceValueMap[
                        m_SourceSlice.Roots[rootIndex].Index];

                CompactReachableNodes(
                    rootIndices,
                    out MaterialStageLIRNode[] nodes,
                    out int[] compactRoots,
                    out int[] sourceValueMap);

                return new MaterialStageLIR(
                    m_Stage,
                    GetExecutionModel(m_Stage),
                    GetDerivativeProvider(m_Stage),
                    m_SourceSlice,
                    nodes,
                    compactRoots,
                    sourceValueMap);
            }

            private void CompactReachableNodes(
                int[] roots,
                out MaterialStageLIRNode[] nodes,
                out int[] compactRoots,
                out int[] sourceValueMap)
            {
                var reachable = new bool[m_Nodes.Count];
                var pending = new Stack<int>();
                for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                    pending.Push(roots[rootIndex]);
                while (pending.Count > 0)
                {
                    int nodeIndex = pending.Pop();
                    if (reachable[nodeIndex])
                        continue;

                    reachable[nodeIndex] = true;
                    MaterialStageLIRNode node = m_Nodes[nodeIndex];
                    for (int operandIndex = 0;
                         operandIndex < node.OperandCount;
                         operandIndex++)
                    {
                        pending.Push(node.GetOperand(operandIndex));
                    }
                }

                int[] oldToNew = CreateIndexMap(m_Nodes.Count);
                var compactNodes = new List<MaterialStageLIRNode>();
                for (int oldIndex = 0; oldIndex < m_Nodes.Count; oldIndex++)
                {
                    if (!reachable[oldIndex])
                        continue;

                    MaterialStageLIRNode node = m_Nodes[oldIndex];
                    oldToNew[oldIndex] = compactNodes.Count;
                    compactNodes.Add(new MaterialStageLIRNode(
                        node.Opcode,
                        node.Type,
                        node.Semantic,
                        node.Constant,
                        node.SourceNodeIndex,
                        node.OperandCount,
                        RemapOperand(node.Operand0, oldToNew),
                        RemapOperand(node.Operand1, oldToNew),
                        RemapOperand(node.Operand2, oldToNew),
                        RemapOperand(node.Operand3, oldToNew)));
                }

                compactRoots = new int[roots.Length];
                for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                    compactRoots[rootIndex] = oldToNew[roots[rootIndex]];

                sourceValueMap = CreateIndexMap(m_SourceValueMap.Length);
                for (int sourceIndex = 0;
                     sourceIndex < m_SourceValueMap.Length;
                     sourceIndex++)
                {
                    int oldIndex = m_SourceValueMap[sourceIndex];
                    if (oldIndex >= 0 && reachable[oldIndex])
                        sourceValueMap[sourceIndex] = oldToNew[oldIndex];
                }
                nodes = compactNodes.ToArray();
            }

            private static int RemapOperand(int operand, int[] oldToNew)
            {
                return operand < 0 ? InvalidOperand : oldToNew[operand];
            }

            private int EmitSourceNode(
                in MaterialValueNode sourceNode,
                int sourceNodeIndex)
            {
                if (sourceNode.Opcode == MaterialValueOpcode.ExternalInput)
                {
                    return Emit(
                        MaterialStageLIROpcode.StageInput,
                        sourceNode.Type,
                        (int) MapStageInput((MaterialExternalInput) sourceNode.Semantic),
                        default,
                        sourceNodeIndex);
                }

                return Emit(
                    MapOpcode(sourceNode.Opcode),
                    sourceNode.Type,
                    sourceNode.Semantic,
                    sourceNode.Constant,
                    sourceNodeIndex,
                    MapOperand(sourceNode.Operand0),
                    MapOperand(sourceNode.Operand1),
                    MapOperand(sourceNode.Operand2),
                    MapOperand(sourceNode.Operand3));
            }

            private int MaterializeDerivative(
                int sourceNodeIndex,
                bool isDdx,
                int requestNodeIndex)
            {
                DerivativeState state = Differentiate(sourceNodeIndex, isDdx, requestNodeIndex);
                if (state.Kind == DerivativeStateKind.Value)
                    return state.ValueIndex;

                MaterialValueType type = m_SourceSlice.Values.Nodes[sourceNodeIndex].Type;
                return Emit(
                    MaterialStageLIROpcode.Constant,
                    type,
                    semantic: 0,
                    constant: default,
                    sourceNodeIndex: requestNodeIndex);
            }

            private DerivativeState Differentiate(
                int sourceNodeIndex,
                bool isDdx,
                int requestNodeIndex)
            {
                DerivativeState[] states = isDdx ? m_DdxStates : m_DdyStates;
                DerivativeState cached = states[sourceNodeIndex];
                if (cached.Kind != DerivativeStateKind.Unknown)
                    return cached;

                if (MaterialStageUniformityAnalyzer.Analyze(
                        m_SourceSlice.Values,
                        sourceNodeIndex,
                        m_UniformityStates)
                    == MaterialStageUniformity.Uniform)
                {
                    states[sourceNodeIndex] = DerivativeState.Zero;
                    return DerivativeState.Zero;
                }

                MaterialValueNode source = m_SourceSlice.Values.Nodes[sourceNodeIndex];
                DerivativeState result;
                switch (MaterialDerivativeLegalizationRules.GetRule(source.Opcode))
                {
                    case MaterialDerivativeLegalizationRule.Zero:
                        result = DerivativeState.Zero;
                        break;
                    case MaterialDerivativeLegalizationRule.StageInput:
                        result = DerivativeState.Value(Emit(
                            MaterialStageLIROpcode.StageInput,
                            source.Type,
                            (int) (isDdx
                                ? MaterialStageInput.UV0Ddx
                                : MaterialStageInput.UV0Ddy),
                            default,
                            requestNodeIndex));
                        break;
                    case MaterialDerivativeLegalizationRule.Add:
                    case MaterialDerivativeLegalizationRule.Subtract:
                        result = DifferentiateAddOrSubtract(
                            source,
                            isDdx,
                            requestNodeIndex);
                        break;
                    case MaterialDerivativeLegalizationRule.Multiply:
                        result = DifferentiateMultiply(
                            source,
                            isDdx,
                            requestNodeIndex);
                        break;
                    case MaterialDerivativeLegalizationRule.Divide:
                        result = DifferentiateDivide(
                            source,
                            isDdx,
                            requestNodeIndex);
                        break;
                    case MaterialDerivativeLegalizationRule.Lerp:
                        result = DifferentiateLerp(
                            source,
                            isDdx,
                            requestNodeIndex);
                        break;
                    case MaterialDerivativeLegalizationRule.Select:
                        result = DifferentiateSelect(
                            source,
                            isDdx,
                            requestNodeIndex);
                        break;
                    case MaterialDerivativeLegalizationRule.Swizzle:
                        result = DifferentiateSwizzle(
                            source,
                            isDdx,
                            requestNodeIndex);
                        break;
                    case MaterialDerivativeLegalizationRule.Compose:
                        result = DifferentiateCompose(
                            source,
                            isDdx,
                            requestNodeIndex);
                        break;
                    case MaterialDerivativeLegalizationRule.OneMinus:
                        result = DifferentiateOneMinus(
                            source,
                            isDdx,
                            requestNodeIndex);
                        break;
                    case MaterialDerivativeLegalizationRule.Dot:
                        result = DifferentiateDot(
                            source,
                            isDdx,
                            requestNodeIndex);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Verified derivative source %{sourceNodeIndex} cannot be legalized.");
                }

                states[sourceNodeIndex] = result;
                return result;
            }

            private DerivativeState DifferentiateAddOrSubtract(
                in MaterialValueNode source,
                bool isDdx,
                int requestNodeIndex)
            {
                DerivativeState left = Differentiate(
                    source.Operand0,
                    isDdx,
                    requestNodeIndex);
                DerivativeState right = Differentiate(
                    source.Operand1,
                    isDdx,
                    requestNodeIndex);
                bool isAdd = source.Opcode == MaterialValueOpcode.Add;
                if (left.Kind == DerivativeStateKind.Zero)
                {
                    if (isAdd || right.Kind == DerivativeStateKind.Zero)
                        return right;
                    return DerivativeState.Value(EmitNegate(
                        right.ValueIndex,
                        source.Type,
                        requestNodeIndex));
                }
                if (right.Kind == DerivativeStateKind.Zero)
                    return left;
                return DerivativeState.Value(Emit(
                    isAdd
                        ? MaterialStageLIROpcode.Add
                        : MaterialStageLIROpcode.Subtract,
                    source.Type,
                    semantic: 0,
                    constant: default,
                    sourceNodeIndex: requestNodeIndex,
                    left.ValueIndex,
                    right.ValueIndex));
            }

            private DerivativeState DifferentiateMultiply(
                in MaterialValueNode source,
                bool isDdx,
                int requestNodeIndex)
            {
                DerivativeState left = Differentiate(
                    source.Operand0,
                    isDdx,
                    requestNodeIndex);
                DerivativeState right = Differentiate(
                    source.Operand1,
                    isDdx,
                    requestNodeIndex);
                if (left.Kind == DerivativeStateKind.Zero
                    && right.Kind == DerivativeStateKind.Zero)
                {
                    return DerivativeState.Zero;
                }
                if (left.Kind == DerivativeStateKind.Value
                    && right.Kind == DerivativeStateKind.Value)
                {
                    throw new InvalidOperationException(
                        "Verified derivative multiplication must have a stage-uniform operand.");
                }

                if (left.Kind == DerivativeStateKind.Value)
                {
                    return DerivativeState.Value(Emit(
                        MaterialStageLIROpcode.Multiply,
                        source.Type,
                        semantic: 0,
                        constant: default,
                        sourceNodeIndex: requestNodeIndex,
                        left.ValueIndex,
                        m_SourceValueMap[source.Operand1]));
                }
                return DerivativeState.Value(Emit(
                    MaterialStageLIROpcode.Multiply,
                    source.Type,
                    semantic: 0,
                    constant: default,
                    sourceNodeIndex: requestNodeIndex,
                    m_SourceValueMap[source.Operand0],
                    right.ValueIndex));
            }

            private DerivativeState DifferentiateDivide(
                in MaterialValueNode source,
                bool isDdx,
                int requestNodeIndex)
            {
                if (!IsUniform(source.Operand1))
                {
                    throw new InvalidOperationException(
                        "Verified derivative division must have a stage-uniform denominator.");
                }

                DerivativeState numerator = Differentiate(
                    source.Operand0,
                    isDdx,
                    requestNodeIndex);
                if (numerator.Kind == DerivativeStateKind.Zero)
                    return numerator;
                return DerivativeState.Value(Emit(
                    MaterialStageLIROpcode.Divide,
                    source.Type,
                    semantic: 0,
                    constant: default,
                    sourceNodeIndex: requestNodeIndex,
                    numerator.ValueIndex,
                    m_SourceValueMap[source.Operand1]));
            }

            private DerivativeState DifferentiateLerp(
                in MaterialValueNode source,
                bool isDdx,
                int requestNodeIndex)
            {
                if (IsUniform(source.Operand2))
                {
                    DerivativeState left = Differentiate(
                        source.Operand0,
                        isDdx,
                        requestNodeIndex);
                    DerivativeState right = Differentiate(
                        source.Operand1,
                        isDdx,
                        requestNodeIndex);
                    if (left.Kind == DerivativeStateKind.Zero
                        && right.Kind == DerivativeStateKind.Zero)
                    {
                        return DerivativeState.Zero;
                    }
                    return DerivativeState.Value(Emit(
                        MaterialStageLIROpcode.Lerp,
                        source.Type,
                        semantic: 0,
                        constant: default,
                        sourceNodeIndex: requestNodeIndex,
                        MaterializeDerivativeValue(
                            left,
                            source.Type,
                            requestNodeIndex),
                        MaterializeDerivativeValue(
                            right,
                            source.Type,
                            requestNodeIndex),
                        m_SourceValueMap[source.Operand2]));
                }

                if (!IsUniform(source.Operand0)
                    || !IsUniform(source.Operand1))
                {
                    throw new InvalidOperationException(
                        "Verified derivative lerp must have a uniform factor or uniform endpoints.");
                }

                DerivativeState factor = Differentiate(
                    source.Operand2,
                    isDdx,
                    requestNodeIndex);
                if (factor.Kind == DerivativeStateKind.Zero)
                    return factor;
                int difference = Emit(
                    MaterialStageLIROpcode.Subtract,
                    source.Type,
                    semantic: 0,
                    constant: default,
                    sourceNodeIndex: requestNodeIndex,
                    m_SourceValueMap[source.Operand1],
                    m_SourceValueMap[source.Operand0]);
                return DerivativeState.Value(Emit(
                    MaterialStageLIROpcode.Lerp,
                    source.Type,
                    semantic: 0,
                    constant: default,
                    sourceNodeIndex: requestNodeIndex,
                    EmitZero(source.Type, requestNodeIndex),
                    difference,
                    factor.ValueIndex));
            }

            private DerivativeState DifferentiateSelect(
                in MaterialValueNode source,
                bool isDdx,
                int requestNodeIndex)
            {
                if (!IsUniform(source.Operand0))
                {
                    throw new InvalidOperationException(
                        "Verified derivative select must have a stage-uniform condition.");
                }

                DerivativeState whenTrue = Differentiate(
                    source.Operand1,
                    isDdx,
                    requestNodeIndex);
                DerivativeState whenFalse = Differentiate(
                    source.Operand2,
                    isDdx,
                    requestNodeIndex);
                if (whenTrue.Kind == DerivativeStateKind.Zero
                    && whenFalse.Kind == DerivativeStateKind.Zero)
                {
                    return DerivativeState.Zero;
                }
                return DerivativeState.Value(Emit(
                    MaterialStageLIROpcode.Select,
                    source.Type,
                    semantic: 0,
                    constant: default,
                    sourceNodeIndex: requestNodeIndex,
                    m_SourceValueMap[source.Operand0],
                    MaterializeDerivativeValue(
                        whenTrue,
                        source.Type,
                        requestNodeIndex),
                    MaterializeDerivativeValue(
                        whenFalse,
                        source.Type,
                        requestNodeIndex)));
            }

            private DerivativeState DifferentiateCompose(
                in MaterialValueNode source,
                bool isDdx,
                int requestNodeIndex)
            {
                var operands = new[]
                {
                    source.Operand0,
                    source.Operand1,
                    source.Operand2,
                    source.Operand3,
                };
                var derivatives = new int[4];
                int derivativeCount = 0;
                bool hasVaryingOperand = false;
                for (int operandIndex = 0;
                     operandIndex < operands.Length && operands[operandIndex] >= 0;
                     operandIndex++)
                {
                    DerivativeState derivative = Differentiate(
                        operands[operandIndex],
                        isDdx,
                        requestNodeIndex);
                    hasVaryingOperand |= derivative.Kind == DerivativeStateKind.Value;
                    derivatives[derivativeCount++] = MaterializeDerivativeValue(
                        derivative,
                        MaterialValueType.Float,
                        requestNodeIndex);
                }
                if (!hasVaryingOperand)
                    return DerivativeState.Zero;

                var activeDerivatives = new int[derivativeCount];
                Array.Copy(derivatives, activeDerivatives, derivativeCount);
                return DerivativeState.Value(Emit(
                    MaterialStageLIROpcode.Compose,
                    source.Type,
                    semantic: 0,
                    constant: default,
                    sourceNodeIndex: requestNodeIndex,
                    activeDerivatives));
            }

            private DerivativeState DifferentiateDot(
                in MaterialValueNode source,
                bool isDdx,
                int requestNodeIndex)
            {
                DerivativeState left = Differentiate(
                    source.Operand0,
                    isDdx,
                    requestNodeIndex);
                DerivativeState right = Differentiate(
                    source.Operand1,
                    isDdx,
                    requestNodeIndex);
                if (left.Kind == DerivativeStateKind.Value
                    && right.Kind == DerivativeStateKind.Value)
                {
                    throw new InvalidOperationException(
                        "Verified derivative dot must have a stage-uniform operand.");
                }
                if (left.Kind == DerivativeStateKind.Zero
                    && right.Kind == DerivativeStateKind.Zero)
                {
                    return DerivativeState.Zero;
                }
                return DerivativeState.Value(Emit(
                    MaterialStageLIROpcode.Dot,
                    source.Type,
                    semantic: 0,
                    constant: default,
                    sourceNodeIndex: requestNodeIndex,
                    left.Kind == DerivativeStateKind.Value
                        ? left.ValueIndex
                        : m_SourceValueMap[source.Operand0],
                    right.Kind == DerivativeStateKind.Value
                        ? right.ValueIndex
                        : m_SourceValueMap[source.Operand1]));
            }

            private DerivativeState DifferentiateSwizzle(
                in MaterialValueNode source,
                bool isDdx,
                int requestNodeIndex)
            {
                DerivativeState operand = Differentiate(
                    source.Operand0,
                    isDdx,
                    requestNodeIndex);
                if (operand.Kind == DerivativeStateKind.Zero)
                    return operand;
                return DerivativeState.Value(Emit(
                    MaterialStageLIROpcode.Swizzle,
                    source.Type,
                    source.Semantic,
                    default,
                    requestNodeIndex,
                    operand.ValueIndex));
            }

            private DerivativeState DifferentiateOneMinus(
                in MaterialValueNode source,
                bool isDdx,
                int requestNodeIndex)
            {
                DerivativeState operand = Differentiate(
                    source.Operand0,
                    isDdx,
                    requestNodeIndex);
                if (operand.Kind == DerivativeStateKind.Zero)
                    return operand;
                return DerivativeState.Value(EmitNegate(
                    operand.ValueIndex,
                    source.Type,
                    requestNodeIndex));
            }

            private int EmitNegate(
                int valueIndex,
                MaterialValueType type,
                int sourceNodeIndex)
            {
                int zero = Emit(
                    MaterialStageLIROpcode.Constant,
                    type,
                    semantic: 0,
                    constant: default,
                    sourceNodeIndex: sourceNodeIndex);
                return Emit(
                    MaterialStageLIROpcode.Subtract,
                    type,
                    semantic: 0,
                    constant: default,
                    sourceNodeIndex: sourceNodeIndex,
                    zero,
                    valueIndex);
            }

            private int MaterializeDerivativeValue(
                DerivativeState state,
                MaterialValueType type,
                int sourceNodeIndex)
            {
                return state.Kind == DerivativeStateKind.Value
                    ? state.ValueIndex
                    : EmitZero(type, sourceNodeIndex);
            }

            private int EmitZero(
                MaterialValueType type,
                int sourceNodeIndex)
            {
                return Emit(
                    MaterialStageLIROpcode.Constant,
                    type,
                    semantic: 0,
                    constant: default,
                    sourceNodeIndex: sourceNodeIndex);
            }

            private bool IsUniform(int sourceNodeIndex)
            {
                return MaterialStageUniformityAnalyzer.Analyze(
                        m_SourceSlice.Values,
                        sourceNodeIndex,
                        m_UniformityStates)
                    == MaterialStageUniformity.Uniform;
            }

            private int Emit(
                MaterialStageLIROpcode opcode,
                MaterialValueType type,
                int semantic,
                float4 constant,
                int sourceNodeIndex,
                params int[] operands)
            {
                int operandCount = 0;
                var encodedOperands = new[]
                {
                    InvalidOperand,
                    InvalidOperand,
                    InvalidOperand,
                    InvalidOperand,
                };
                for (int operandIndex = 0; operandIndex < operands.Length; operandIndex++)
                {
                    int operand = operands[operandIndex];
                    if (operand < 0)
                        continue;
                    encodedOperands[operandCount++] = operand;
                }

                int nodeIndex = m_Nodes.Count;
                m_Nodes.Add(new MaterialStageLIRNode(
                    opcode,
                    type,
                    semantic,
                    constant,
                    sourceNodeIndex,
                    operandCount,
                    encodedOperands[0],
                    encodedOperands[1],
                    encodedOperands[2],
                    encodedOperands[3]));
                return nodeIndex;
            }

            private int MapOperand(int sourceOperand)
            {
                return sourceOperand < 0
                    ? InvalidOperand
                    : m_SourceValueMap[sourceOperand];
            }
        }

        private static int[] CreateIndexMap(int count)
        {
            var indices = new int[count];
            for (int index = 0; index < indices.Length; index++)
                indices[index] = InvalidOperand;
            return indices;
        }

        private static MaterialStageExecutionModel GetExecutionModel(
            MaterialEvaluationStage stage)
        {
            switch (stage)
            {
                case MaterialEvaluationStage.Coverage:
                    return MaterialStageExecutionModel.RasterFragment;
                case MaterialEvaluationStage.Surface:
                    return MaterialStageExecutionModel.VisibilityResolve;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }

        private static MaterialStageDerivativeProvider GetDerivativeProvider(
            MaterialEvaluationStage stage)
        {
            switch (stage)
            {
                case MaterialEvaluationStage.Coverage:
                    return MaterialStageDerivativeProvider.NativeQuad;
                case MaterialEvaluationStage.Surface:
                    return MaterialStageDerivativeProvider.VisibilityBuffer;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }

        internal static MaterialStageInput MapStageInput(MaterialExternalInput input)
        {
            switch (input)
            {
                case MaterialExternalInput.UV0:
                    return MaterialStageInput.UV0;
                case MaterialExternalInput.GeometryNormalWS:
                    return MaterialStageInput.GeometryNormalWS;
                case MaterialExternalInput.GeometryTangentWS:
                    return MaterialStageInput.GeometryTangentWS;
                default:
                    throw new ArgumentOutOfRangeException(nameof(input), input, null);
            }
        }

        internal static MaterialStageLIROpcode MapOpcode(MaterialValueOpcode opcode)
        {
            switch (opcode)
            {
                case MaterialValueOpcode.Constant: return MaterialStageLIROpcode.Constant;
                case MaterialValueOpcode.Parameter: return MaterialStageLIROpcode.Parameter;
                case MaterialValueOpcode.TextureResource: return MaterialStageLIROpcode.TextureResource;
                case MaterialValueOpcode.TextureSampleGrad: return MaterialStageLIROpcode.TextureSampleGrad;
                case MaterialValueOpcode.Add: return MaterialStageLIROpcode.Add;
                case MaterialValueOpcode.Multiply: return MaterialStageLIROpcode.Multiply;
                case MaterialValueOpcode.Lerp: return MaterialStageLIROpcode.Lerp;
                case MaterialValueOpcode.Select: return MaterialStageLIROpcode.Select;
                case MaterialValueOpcode.Swizzle: return MaterialStageLIROpcode.Swizzle;
                case MaterialValueOpcode.Compose: return MaterialStageLIROpcode.Compose;
                case MaterialValueOpcode.Subtract: return MaterialStageLIROpcode.Subtract;
                case MaterialValueOpcode.Divide: return MaterialStageLIROpcode.Divide;
                case MaterialValueOpcode.Min: return MaterialStageLIROpcode.Min;
                case MaterialValueOpcode.Max: return MaterialStageLIROpcode.Max;
                case MaterialValueOpcode.Saturate: return MaterialStageLIROpcode.Saturate;
                case MaterialValueOpcode.OneMinus: return MaterialStageLIROpcode.OneMinus;
                case MaterialValueOpcode.Dot: return MaterialStageLIROpcode.Dot;
                case MaterialValueOpcode.Normalize: return MaterialStageLIROpcode.Normalize;
                case MaterialValueOpcode.Compare: return MaterialStageLIROpcode.Compare;
                default:
                    throw new InvalidOperationException(
                        $"Verified source opcode {opcode} has no Stage LIR mapping.");
            }
        }
    }
}
