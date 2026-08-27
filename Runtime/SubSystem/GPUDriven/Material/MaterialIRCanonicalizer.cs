using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven
{
    internal sealed class CanonicalMaterialIR
    {
        private readonly byte[] m_Payload;
        private readonly int[] m_SourceValueNodeMap;
        private readonly int[] m_SourceClosureNodeMap;

        internal CanonicalMaterialIR(
            MaterialValueIR values,
            in MaterialOutputRoots outputs,
            ClosureExpressionGraph closureGraph,
            MaterialClosure surfaceClosure,
            ClosureTopology topology,
            MaterialFeatureMask materialFeatures,
            MaterialShadingModelMask shadingModels,
            byte[] payload,
            int[] sourceValueNodeMap,
            int[] sourceClosureNodeMap)
        {
            Values = values ?? throw new ArgumentNullException(nameof(values));
            ClosureGraph = closureGraph
                ?? throw new ArgumentNullException(nameof(closureGraph));
            Topology = topology ?? throw new ArgumentNullException(nameof(topology));
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            if (sourceValueNodeMap == null)
                throw new ArgumentNullException(nameof(sourceValueNodeMap));
            if (sourceClosureNodeMap == null)
                throw new ArgumentNullException(nameof(sourceClosureNodeMap));
            if (!Values.IsFrozen)
            {
                throw new InvalidOperationException(
                    "Canonical material value IR must be frozen.");
            }
            if (!ClosureGraph.IsFrozen)
            {
                throw new InvalidOperationException(
                    "Canonical closure expression graph must be frozen.");
            }

            Outputs = outputs;
            SurfaceClosure = surfaceClosure;
            MaterialFeatures = materialFeatures;
            ShadingModels = shadingModels;
            m_Payload = (byte[]) payload.Clone();
            m_SourceValueNodeMap = (int[]) sourceValueNodeMap.Clone();
            m_SourceClosureNodeMap = (int[]) sourceClosureNodeMap.Clone();
            PayloadHash = MaterialProgramHashUtility.Compute(m_Payload);
        }

        internal MaterialValueIR Values { get; }

        internal MaterialOutputRoots Outputs { get; }

        internal ClosureExpressionGraph ClosureGraph { get; }

        internal MaterialClosure SurfaceClosure { get; }

        internal ClosureTopology Topology { get; }

        internal MaterialFeatureMask MaterialFeatures { get; }

        internal MaterialShadingModelMask ShadingModels { get; }

        internal byte[] Payload => (byte[]) m_Payload.Clone();

        internal int PayloadLength => m_Payload.Length;

        internal ulong PayloadHash { get; }

        internal int GetCanonicalValueNodeIndex(int sourceNodeIndex)
        {
            return (uint) sourceNodeIndex < (uint) m_SourceValueNodeMap.Length
                ? m_SourceValueNodeMap[sourceNodeIndex]
                : -1;
        }

        internal int GetCanonicalClosureNodeIndex(int sourceNodeIndex)
        {
            return (uint) sourceNodeIndex < (uint) m_SourceClosureNodeMap.Length
                ? m_SourceClosureNodeMap[sourceNodeIndex]
                : -1;
        }

        internal bool PayloadEquals(CanonicalMaterialIR other)
        {
            return other != null && PayloadEquals(other.m_Payload);
        }

        internal bool PayloadEquals(byte[] other)
        {
            if (other == null || other.Length != m_Payload.Length)
                return false;

            for (int i = 0; i < m_Payload.Length; i++)
            {
                if (m_Payload[i] != other[i])
                    return false;
            }
            return true;
        }
    }

    internal static class MaterialIRCanonicalizer
    {
        private const int InvalidIndex = -1;
        private const uint PayloadMagic = 0x3352494Du; // MIR3

        internal static CanonicalMaterialIR CanonicalizeVerified(
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

            bool[] liveClosureNodes = FindLiveClosureNodes(
                closureGraph,
                surfaceClosure);
            bool[] liveNodes = FindLiveNodes(
                values,
                outputs,
                closureGraph,
                liveClosureNodes);
            int[] nodeHeights = ComputeNodeHeights(
                values,
                liveNodes,
                out int maximumHeight);

            BuildCanonicalDeclarations(
                values,
                liveNodes,
                out MaterialParameterDeclaration[] parameterDeclarations,
                out int[] parameterDeclarationMap,
                out MaterialResourceDeclaration[] resourceDeclarations,
                out int[] resourceDeclarationMap);

            var canonicalValues = new MaterialValueIR(
                parameterDeclarations,
                resourceDeclarations);
            int[] canonicalNodeMap = BuildCanonicalNodes(
                values,
                liveNodes,
                nodeHeights,
                maximumHeight,
                parameterDeclarationMap,
                resourceDeclarationMap,
                canonicalValues);

            MaterialOutputRoots canonicalOutputs = new(
                RemapValue(values, outputs.CoverageValue, canonicalValues, canonicalNodeMap),
                RemapValue(
                    values,
                    outputs.AlphaClipThreshold,
                    canonicalValues,
                    canonicalNodeMap),
                RemapValue(values, outputs.Emission, canonicalValues, canonicalNodeMap));
            BuildCanonicalClosureGraph(
                values,
                closureGraph,
                surfaceClosure,
                canonicalValues,
                canonicalNodeMap,
                out ClosureExpressionGraph canonicalClosureGraph,
                out MaterialClosure canonicalSurfaceClosure,
                out int[] canonicalClosureNodeMap);
            canonicalValues.Freeze();
            canonicalClosureGraph.Freeze();
            ClosureTopology canonicalTopology = ClosureTopologyLowerer.Lower(
                canonicalClosureGraph,
                canonicalSurfaceClosure,
                closureBudget);
            byte[] payload = BuildPayload(
                canonicalValues,
                canonicalOutputs,
                canonicalClosureGraph,
                canonicalSurfaceClosure,
                materialFeatures,
                shadingModels);

            return new CanonicalMaterialIR(
                canonicalValues,
                canonicalOutputs,
                canonicalClosureGraph,
                canonicalSurfaceClosure,
                canonicalTopology,
                materialFeatures,
                shadingModels,
                payload,
                canonicalNodeMap,
                canonicalClosureNodeMap);
        }

        private static bool[] FindLiveClosureNodes(
            ClosureExpressionGraph closureGraph,
            MaterialClosure surfaceClosure)
        {
            if (!closureGraph.Owns(surfaceClosure))
            {
                throw new InvalidOperationException(
                    "Canonical surface closure is not owned by its expression graph.");
            }

            var liveNodes = new bool[closureGraph.NodeCount];
            liveNodes[surfaceClosure.Index] = true;
            for (int nodeIndex = closureGraph.NodeCount - 1;
                 nodeIndex >= 0;
                 nodeIndex--)
            {
                if (!liveNodes[nodeIndex])
                    continue;

                ClosureExpressionNode node = closureGraph.Nodes[nodeIndex];
                if (node.Opcode == ClosureExpressionOpcode.Slab)
                    continue;

                MarkClosureOperand(node.Operand0, nodeIndex, liveNodes);
                MarkClosureOperand(node.Operand1, nodeIndex, liveNodes);
            }
            return liveNodes;
        }

        private static void MarkClosureOperand(
            int operand,
            int nodeIndex,
            bool[] liveNodes)
        {
            if ((uint) operand >= (uint) nodeIndex)
            {
                throw new InvalidOperationException(
                    "Canonicalization requires verified topological closure operands.");
            }
            liveNodes[operand] = true;
        }

        private static bool[] FindLiveNodes(
            MaterialValueIR values,
            in MaterialOutputRoots outputs,
            ClosureExpressionGraph closureGraph,
            bool[] liveClosureNodes)
        {
            var liveNodes = new bool[values.NodeCount];
            MarkRoot(values, outputs.CoverageValue, liveNodes);
            MarkRoot(values, outputs.AlphaClipThreshold, liveNodes);
            MarkRoot(values, outputs.Emission, liveNodes);

            for (int closureIndex = 0;
                 closureIndex < closureGraph.Nodes.Count;
                 closureIndex++)
            {
                if (!liveClosureNodes[closureIndex])
                    continue;

                ClosureExpressionNode closureNode = closureGraph.Nodes[closureIndex];
                if (closureNode.Opcode == ClosureExpressionOpcode.Slab)
                {
                    ClosureSlabExpression slab = closureNode.Slab;
                    MarkRoot(values, slab.BaseColor, liveNodes);
                    MarkRoot(values, slab.Roughness, liveNodes);
                    MarkRoot(values, slab.Metallic, liveNodes);
                    MarkRoot(values, slab.Normal, liveNodes);
                    MarkRoot(values, slab.Tangent, liveNodes);
                }
                else
                {
                    MarkRoot(values, closureNode.Weight, liveNodes);
                }
            }

            for (int nodeIndex = values.NodeCount - 1; nodeIndex >= 0; nodeIndex--)
            {
                if (!liveNodes[nodeIndex])
                    continue;

                MaterialValueNode node = values.Nodes[nodeIndex];
                MarkOperand(node.Operand0, nodeIndex, liveNodes);
                MarkOperand(node.Operand1, nodeIndex, liveNodes);
                MarkOperand(node.Operand2, nodeIndex, liveNodes);
                MarkOperand(node.Operand3, nodeIndex, liveNodes);
            }
            return liveNodes;
        }

        private static void MarkRoot(
            MaterialValueIR values,
            MaterialValue root,
            bool[] liveNodes)
        {
            if (!values.Owns(root))
            {
                throw new InvalidOperationException(
                    "Canonical material IR root is not owned by the source value IR.");
            }
            liveNodes[root.Index] = true;
        }

        private static void MarkOperand(
            int operand,
            int nodeIndex,
            bool[] liveNodes)
        {
            if (operand == InvalidIndex)
                return;
            if ((uint) operand >= (uint) nodeIndex)
            {
                throw new InvalidOperationException(
                    "Canonicalization requires verified topological operands.");
            }
            liveNodes[operand] = true;
        }

        private static int[] ComputeNodeHeights(
            MaterialValueIR values,
            bool[] liveNodes,
            out int maximumHeight)
        {
            var nodeHeights = new int[values.NodeCount];
            maximumHeight = 0;
            for (int nodeIndex = 0; nodeIndex < values.NodeCount; nodeIndex++)
            {
                if (!liveNodes[nodeIndex])
                    continue;

                MaterialValueNode node = values.Nodes[nodeIndex];
                int height = 0;
                AccumulateOperandHeight(node.Operand0, liveNodes, nodeHeights, ref height);
                AccumulateOperandHeight(node.Operand1, liveNodes, nodeHeights, ref height);
                AccumulateOperandHeight(node.Operand2, liveNodes, nodeHeights, ref height);
                AccumulateOperandHeight(node.Operand3, liveNodes, nodeHeights, ref height);
                nodeHeights[nodeIndex] = height;
                if (height > maximumHeight)
                    maximumHeight = height;
            }
            return nodeHeights;
        }

        private static void AccumulateOperandHeight(
            int operand,
            bool[] liveNodes,
            int[] nodeHeights,
            ref int height)
        {
            if (operand == InvalidIndex)
                return;
            if (!liveNodes[operand])
            {
                throw new InvalidOperationException(
                    "A live canonical node has a non-live operand.");
            }
            height = math.max(height, nodeHeights[operand] + 1);
        }

        private static void BuildCanonicalDeclarations(
            MaterialValueIR values,
            bool[] liveNodes,
            out MaterialParameterDeclaration[] parameterDeclarations,
            out int[] parameterDeclarationMap,
            out MaterialResourceDeclaration[] resourceDeclarations,
            out int[] resourceDeclarationMap)
        {
            var liveParameters = new bool[values.ParameterDeclarations.Count];
            var liveResources = new bool[values.ResourceDeclarations.Count];
            for (int nodeIndex = 0; nodeIndex < values.NodeCount; nodeIndex++)
            {
                if (!liveNodes[nodeIndex])
                    continue;

                MaterialValueNode node = values.Nodes[nodeIndex];
                if (node.Opcode == MaterialValueOpcode.Parameter)
                    liveParameters[node.Semantic] = true;
                else if (node.Opcode == MaterialValueOpcode.TextureResource)
                    liveResources[node.Semantic] = true;
            }

            var parameterEntries = new List<ParameterDeclarationEntry>();
            for (int declarationIndex = 0;
                 declarationIndex < liveParameters.Length;
                 declarationIndex++)
            {
                if (liveParameters[declarationIndex])
                {
                    parameterEntries.Add(new ParameterDeclarationEntry(
                        declarationIndex,
                        values.ParameterDeclarations[declarationIndex]));
                }
            }
            parameterEntries.Sort(CompareParameterDeclarations);
            parameterDeclarations = new MaterialParameterDeclaration[parameterEntries.Count];
            parameterDeclarationMap = CreateInvalidMap(liveParameters.Length);
            for (int canonicalIndex = 0;
                 canonicalIndex < parameterEntries.Count;
                 canonicalIndex++)
            {
                ParameterDeclarationEntry entry = parameterEntries[canonicalIndex];
                parameterDeclarations[canonicalIndex] = entry.Declaration;
                parameterDeclarationMap[entry.SourceIndex] = canonicalIndex;
            }

            var resourceEntries = new List<ResourceDeclarationEntry>();
            for (int declarationIndex = 0;
                 declarationIndex < liveResources.Length;
                 declarationIndex++)
            {
                if (liveResources[declarationIndex])
                {
                    resourceEntries.Add(new ResourceDeclarationEntry(
                        declarationIndex,
                        values.ResourceDeclarations[declarationIndex]));
                }
            }
            resourceEntries.Sort(CompareResourceDeclarations);
            resourceDeclarations = new MaterialResourceDeclaration[resourceEntries.Count];
            resourceDeclarationMap = CreateInvalidMap(liveResources.Length);
            for (int canonicalIndex = 0;
                 canonicalIndex < resourceEntries.Count;
                 canonicalIndex++)
            {
                ResourceDeclarationEntry entry = resourceEntries[canonicalIndex];
                resourceDeclarations[canonicalIndex] = entry.Declaration;
                resourceDeclarationMap[entry.SourceIndex] = canonicalIndex;
            }
        }

        private static int CompareParameterDeclarations(
            ParameterDeclarationEntry left,
            ParameterDeclarationEntry right)
        {
            int comparison = string.CompareOrdinal(
                left.Declaration.Symbol,
                right.Declaration.Symbol);
            if (comparison != 0)
                return comparison;
            return ((int) left.Declaration.Type).CompareTo((int) right.Declaration.Type);
        }

        private static int CompareResourceDeclarations(
            ResourceDeclarationEntry left,
            ResourceDeclarationEntry right)
        {
            int comparison = string.CompareOrdinal(
                left.Declaration.Symbol,
                right.Declaration.Symbol);
            if (comparison != 0)
                return comparison;
            return ((int) left.Declaration.Type).CompareTo((int) right.Declaration.Type);
        }

        private static int[] BuildCanonicalNodes(
            MaterialValueIR sourceValues,
            bool[] liveNodes,
            int[] nodeHeights,
            int maximumHeight,
            int[] parameterDeclarationMap,
            int[] resourceDeclarationMap,
            MaterialValueIR canonicalValues)
        {
            int[] canonicalNodeMap = CreateInvalidMap(sourceValues.NodeCount);
            var sourceNodesByHeight = new List<int>[maximumHeight + 1];
            for (int height = 0; height < sourceNodesByHeight.Length; height++)
                sourceNodesByHeight[height] = new List<int>();
            for (int nodeIndex = 0; nodeIndex < sourceValues.NodeCount; nodeIndex++)
            {
                if (liveNodes[nodeIndex])
                    sourceNodesByHeight[nodeHeights[nodeIndex]].Add(nodeIndex);
            }

            for (int height = 0; height < sourceNodesByHeight.Length; height++)
            {
                var sourcesByDescriptor =
                    new Dictionary<CanonicalNodeDescriptor, List<int>>();
                List<int> sourceNodes = sourceNodesByHeight[height];
                for (int i = 0; i < sourceNodes.Count; i++)
                {
                    int sourceNodeIndex = sourceNodes[i];
                    CanonicalNodeDescriptor descriptor = CreateDescriptor(
                        sourceValues.Nodes[sourceNodeIndex],
                        canonicalNodeMap,
                        parameterDeclarationMap,
                        resourceDeclarationMap);
                    if (!sourcesByDescriptor.TryGetValue(
                            descriptor,
                            out List<int> equivalentSources))
                    {
                        equivalentSources = new List<int>();
                        sourcesByDescriptor.Add(descriptor, equivalentSources);
                    }
                    equivalentSources.Add(sourceNodeIndex);
                }

                var descriptors = new List<CanonicalNodeDescriptor>(
                    sourcesByDescriptor.Keys);
                descriptors.Sort(CanonicalNodeDescriptorComparer.Instance);
                for (int descriptorIndex = 0;
                     descriptorIndex < descriptors.Count;
                     descriptorIndex++)
                {
                    CanonicalNodeDescriptor descriptor = descriptors[descriptorIndex];
                    int expectedNodeIndex = canonicalValues.NodeCount;
                    MaterialValue canonicalValue = canonicalValues.AppendVerifiedNode(
                        descriptor.ToNode());
                    if (canonicalValue.Index != expectedNodeIndex)
                    {
                        throw new InvalidOperationException(
                            "Canonical node descriptors did not produce unique nodes.");
                    }

                    List<int> equivalentSources = sourcesByDescriptor[descriptor];
                    for (int sourceIndex = 0;
                         sourceIndex < equivalentSources.Count;
                         sourceIndex++)
                    {
                        canonicalNodeMap[equivalentSources[sourceIndex]] =
                            canonicalValue.Index;
                    }
                }
            }
            return canonicalNodeMap;
        }

        private static CanonicalNodeDescriptor CreateDescriptor(
            in MaterialValueNode sourceNode,
            int[] canonicalNodeMap,
            int[] parameterDeclarationMap,
            int[] resourceDeclarationMap)
        {
            int semantic = sourceNode.Semantic;
            if (sourceNode.Opcode == MaterialValueOpcode.Parameter)
                semantic = parameterDeclarationMap[semantic];
            else if (sourceNode.Opcode == MaterialValueOpcode.TextureResource)
                semantic = resourceDeclarationMap[semantic];

            int operand0 = RemapOperand(sourceNode.Operand0, canonicalNodeMap);
            int operand1 = RemapOperand(sourceNode.Operand1, canonicalNodeMap);
            int operand2 = RemapOperand(sourceNode.Operand2, canonicalNodeMap);
            int operand3 = RemapOperand(sourceNode.Operand3, canonicalNodeMap);
            if (MaterialOpcodeTable.TryGetInfo(
                    sourceNode.Opcode,
                    out MaterialOpcodeInfo info)
                && (info.Flags & MaterialOpcodeFlags.Commutative) != 0)
            {
                SortActiveOperands(
                    ref operand0,
                    ref operand1,
                    ref operand2,
                    ref operand3);
            }

            uint4 constantBits = math.asuint(sourceNode.Constant);
            return new CanonicalNodeDescriptor(
                sourceNode.Opcode,
                sourceNode.Type,
                semantic,
                constantBits,
                operand0,
                operand1,
                operand2,
                operand3);
        }

        private static int RemapOperand(int operand, int[] canonicalNodeMap)
        {
            if (operand == InvalidIndex)
                return InvalidIndex;
            int canonicalOperand = canonicalNodeMap[operand];
            if (canonicalOperand == InvalidIndex)
            {
                throw new InvalidOperationException(
                    "Canonical node ordering did not emit an operand first.");
            }
            return canonicalOperand;
        }

        private static void SortActiveOperands(
            ref int operand0,
            ref int operand1,
            ref int operand2,
            ref int operand3)
        {
            var operands = new[] { operand0, operand1, operand2, operand3 };
            int operandCount = 0;
            while (operandCount < operands.Length
                && operands[operandCount] != InvalidIndex)
            {
                operandCount++;
            }
            Array.Sort(operands, 0, operandCount);
            operand0 = operands[0];
            operand1 = operands[1];
            operand2 = operands[2];
            operand3 = operands[3];
        }

        private static void BuildCanonicalClosureGraph(
            MaterialValueIR sourceValues,
            ClosureExpressionGraph sourceGraph,
            MaterialClosure sourceRoot,
            MaterialValueIR canonicalValues,
            int[] canonicalNodeMap,
            out ClosureExpressionGraph canonicalGraph,
            out MaterialClosure canonicalRoot,
            out int[] canonicalClosureNodeMap)
        {
            canonicalGraph = new ClosureExpressionGraph(canonicalValues);
            canonicalClosureNodeMap = CreateInvalidMap(sourceGraph.NodeCount);
            canonicalRoot = AppendCanonicalClosure(
                sourceValues,
                sourceGraph,
                sourceRoot.Index,
                canonicalValues,
                canonicalNodeMap,
                canonicalGraph,
                canonicalClosureNodeMap);
        }

        private static MaterialClosure AppendCanonicalClosure(
            MaterialValueIR sourceValues,
            ClosureExpressionGraph sourceGraph,
            int sourceIndex,
            MaterialValueIR canonicalValues,
            int[] canonicalNodeMap,
            ClosureExpressionGraph canonicalGraph,
            int[] canonicalClosureNodeMap)
        {
            ClosureExpressionNode sourceNode = sourceGraph.Nodes[sourceIndex];
            if (sourceNode.Opcode == ClosureExpressionOpcode.Slab)
            {
                ClosureSlabExpression slab = sourceNode.Slab;
                MaterialClosure canonicalSlab = canonicalGraph.Slab(
                    RemapValue(
                        sourceValues,
                        slab.BaseColor,
                        canonicalValues,
                        canonicalNodeMap),
                    RemapValue(
                        sourceValues,
                        slab.Roughness,
                        canonicalValues,
                        canonicalNodeMap),
                    RemapValue(
                        sourceValues,
                        slab.Metallic,
                        canonicalValues,
                        canonicalNodeMap),
                    RemapValue(
                        sourceValues,
                        slab.Normal,
                        canonicalValues,
                        canonicalNodeMap),
                    RemapValue(
                        sourceValues,
                        slab.Tangent,
                        canonicalValues,
                        canonicalNodeMap),
                    slab.Features);
                canonicalClosureNodeMap[sourceIndex] = canonicalSlab.Index;
                return canonicalSlab;
            }

            MaterialClosure operand0 = AppendCanonicalClosure(
                sourceValues,
                sourceGraph,
                sourceNode.Operand0,
                canonicalValues,
                canonicalNodeMap,
                canonicalGraph,
                canonicalClosureNodeMap);
            MaterialClosure operand1 = AppendCanonicalClosure(
                sourceValues,
                sourceGraph,
                sourceNode.Operand1,
                canonicalValues,
                canonicalNodeMap,
                canonicalGraph,
                canonicalClosureNodeMap);
            MaterialValue weight = RemapValue(
                sourceValues,
                sourceNode.Weight,
                canonicalValues,
                canonicalNodeMap);
            switch (sourceNode.Opcode)
            {
                case ClosureExpressionOpcode.HorizontalMix:
                {
                    MaterialClosure canonicalMix = canonicalGraph.HorizontalMix(
                        operand0,
                        operand1,
                        weight);
                    canonicalClosureNodeMap[sourceIndex] = canonicalMix.Index;
                    return canonicalMix;
                }
                case ClosureExpressionOpcode.VerticalLayer:
                {
                    MaterialClosure canonicalLayer = canonicalGraph.VerticalLayer(
                        operand0,
                        operand1,
                        weight);
                    canonicalClosureNodeMap[sourceIndex] = canonicalLayer.Index;
                    return canonicalLayer;
                }
                default:
                    throw new InvalidOperationException(
                        $"Closure expression opcode '{sourceNode.Opcode}' cannot be canonicalized.");
            }
        }

        private static MaterialValue RemapValue(
            MaterialValueIR sourceValues,
            MaterialValue sourceValue,
            MaterialValueIR canonicalValues,
            int[] canonicalNodeMap)
        {
            if (!sourceValues.Owns(sourceValue))
            {
                throw new InvalidOperationException(
                    "Canonical value remap received a foreign source value.");
            }

            int canonicalIndex = canonicalNodeMap[sourceValue.Index];
            if (canonicalIndex == InvalidIndex)
            {
                throw new InvalidOperationException(
                    "Canonical value remap received a non-live source value.");
            }
            MaterialValueNode node = canonicalValues.Nodes[canonicalIndex];
            return new MaterialValue(canonicalValues, canonicalIndex, node.Type);
        }

        private static byte[] BuildPayload(
            MaterialValueIR values,
            in MaterialOutputRoots outputs,
            ClosureExpressionGraph closureGraph,
            MaterialClosure surfaceClosure,
            MaterialFeatureMask materialFeatures,
            MaterialShadingModelMask shadingModels)
        {
            var writer = new CanonicalPayloadWriter();
            writer.WriteUInt32(PayloadMagic);
            writer.WriteUInt32(MaterialProgramContract.CanonicalIRVersion);
            writer.WriteUInt32(MaterialProgramContract.IRSchemaVersion);
            writer.WriteUInt32(MaterialProgramContract.SemanticHashVersion);
            writer.WriteUInt32(MaterialProgramContract.ClosureExpressionVersion);

            writer.WriteUInt32((uint) values.ParameterDeclarations.Count);
            for (int i = 0; i < values.ParameterDeclarations.Count; i++)
            {
                MaterialParameterDeclaration declaration =
                    values.ParameterDeclarations[i];
                writer.WriteString(declaration.Symbol);
                writer.WriteUInt32((uint) declaration.Type);
            }

            writer.WriteUInt32((uint) values.ResourceDeclarations.Count);
            for (int i = 0; i < values.ResourceDeclarations.Count; i++)
            {
                MaterialResourceDeclaration declaration =
                    values.ResourceDeclarations[i];
                writer.WriteString(declaration.Symbol);
                writer.WriteUInt32((uint) declaration.Type);
            }

            writer.WriteUInt32((uint) values.Nodes.Count);
            for (int i = 0; i < values.Nodes.Count; i++)
            {
                MaterialValueNode node = values.Nodes[i];
                uint4 constantBits = math.asuint(node.Constant);
                writer.WriteUInt32((uint) node.Opcode);
                writer.WriteUInt32((uint) node.Type);
                writer.WriteUInt32(unchecked((uint) node.Semantic));
                writer.WriteUInt32(constantBits.x);
                writer.WriteUInt32(constantBits.y);
                writer.WriteUInt32(constantBits.z);
                writer.WriteUInt32(constantBits.w);
                writer.WriteUInt32(unchecked((uint) node.Operand0));
                writer.WriteUInt32(unchecked((uint) node.Operand1));
                writer.WriteUInt32(unchecked((uint) node.Operand2));
                writer.WriteUInt32(unchecked((uint) node.Operand3));
            }

            writer.WriteUInt32((uint) outputs.CoverageValue.Index);
            writer.WriteUInt32((uint) outputs.AlphaClipThreshold.Index);
            writer.WriteUInt32((uint) outputs.Emission.Index);
            writer.WriteUInt32((uint) materialFeatures);
            writer.WriteUInt32((uint) shadingModels);

            writer.WriteUInt32((uint) closureGraph.Nodes.Count);
            for (int i = 0; i < closureGraph.Nodes.Count; i++)
            {
                ClosureExpressionNode node = closureGraph.Nodes[i];
                writer.WriteUInt32((uint) node.Opcode);
                if (node.Opcode == ClosureExpressionOpcode.Slab)
                {
                    ClosureSlabExpression slab = node.Slab;
                    writer.WriteUInt32((uint) slab.BaseColor.Index);
                    writer.WriteUInt32((uint) slab.Roughness.Index);
                    writer.WriteUInt32((uint) slab.Metallic.Index);
                    writer.WriteUInt32((uint) slab.Normal.Index);
                    writer.WriteUInt32((uint) slab.Tangent.Index);
                    writer.WriteUInt32((uint) slab.Features);
                    continue;
                }

                writer.WriteUInt32((uint) node.Operand0);
                writer.WriteUInt32((uint) node.Operand1);
                writer.WriteUInt32((uint) node.Weight.Index);
            }
            writer.WriteUInt32((uint) surfaceClosure.Index);
            return writer.ToArray();
        }

        private static int[] CreateInvalidMap(int count)
        {
            var map = new int[count];
            for (int i = 0; i < map.Length; i++)
                map[i] = InvalidIndex;
            return map;
        }

        private readonly struct ParameterDeclarationEntry
        {
            internal ParameterDeclarationEntry(
                int sourceIndex,
                in MaterialParameterDeclaration declaration)
            {
                SourceIndex = sourceIndex;
                Declaration = declaration;
            }

            internal int SourceIndex { get; }

            internal MaterialParameterDeclaration Declaration { get; }
        }

        private readonly struct ResourceDeclarationEntry
        {
            internal ResourceDeclarationEntry(
                int sourceIndex,
                in MaterialResourceDeclaration declaration)
            {
                SourceIndex = sourceIndex;
                Declaration = declaration;
            }

            internal int SourceIndex { get; }

            internal MaterialResourceDeclaration Declaration { get; }
        }

        private readonly struct CanonicalNodeDescriptor :
            IEquatable<CanonicalNodeDescriptor>
        {
            internal CanonicalNodeDescriptor(
                MaterialValueOpcode opcode,
                MaterialValueType type,
                int semantic,
                uint4 constantBits,
                int operand0,
                int operand1,
                int operand2,
                int operand3)
            {
                Opcode = opcode;
                Type = type;
                Semantic = semantic;
                Constant0 = constantBits.x;
                Constant1 = constantBits.y;
                Constant2 = constantBits.z;
                Constant3 = constantBits.w;
                Operand0 = operand0;
                Operand1 = operand1;
                Operand2 = operand2;
                Operand3 = operand3;
            }

            internal MaterialValueOpcode Opcode { get; }

            internal MaterialValueType Type { get; }

            internal int Semantic { get; }

            internal uint Constant0 { get; }

            internal uint Constant1 { get; }

            internal uint Constant2 { get; }

            internal uint Constant3 { get; }

            internal int Operand0 { get; }

            internal int Operand1 { get; }

            internal int Operand2 { get; }

            internal int Operand3 { get; }

            internal MaterialValueNode ToNode()
            {
                return new MaterialValueNode(
                    Opcode,
                    Type,
                    Semantic,
                    math.asfloat(new uint4(
                        Constant0,
                        Constant1,
                        Constant2,
                        Constant3)),
                    Operand0,
                    Operand1,
                    Operand2,
                    Operand3);
            }

            public bool Equals(CanonicalNodeDescriptor other)
            {
                return Opcode == other.Opcode
                    && Type == other.Type
                    && Semantic == other.Semantic
                    && Constant0 == other.Constant0
                    && Constant1 == other.Constant1
                    && Constant2 == other.Constant2
                    && Constant3 == other.Constant3
                    && Operand0 == other.Operand0
                    && Operand1 == other.Operand1
                    && Operand2 == other.Operand2
                    && Operand3 == other.Operand3;
            }

            public override bool Equals(object obj)
            {
                return obj is CanonicalNodeDescriptor other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = (int) Opcode;
                    hashCode = (hashCode * 397) ^ (int) Type;
                    hashCode = (hashCode * 397) ^ Semantic;
                    hashCode = (hashCode * 397) ^ (int) Constant0;
                    hashCode = (hashCode * 397) ^ (int) Constant1;
                    hashCode = (hashCode * 397) ^ (int) Constant2;
                    hashCode = (hashCode * 397) ^ (int) Constant3;
                    hashCode = (hashCode * 397) ^ Operand0;
                    hashCode = (hashCode * 397) ^ Operand1;
                    hashCode = (hashCode * 397) ^ Operand2;
                    hashCode = (hashCode * 397) ^ Operand3;
                    return hashCode;
                }
            }
        }

        private sealed class CanonicalNodeDescriptorComparer :
            IComparer<CanonicalNodeDescriptor>
        {
            internal static readonly CanonicalNodeDescriptorComparer Instance = new();

            public int Compare(
                CanonicalNodeDescriptor left,
                CanonicalNodeDescriptor right)
            {
                int comparison = ((int) left.Opcode).CompareTo((int) right.Opcode);
                if (comparison != 0)
                    return comparison;
                comparison = ((int) left.Type).CompareTo((int) right.Type);
                if (comparison != 0)
                    return comparison;
                comparison = left.Semantic.CompareTo(right.Semantic);
                if (comparison != 0)
                    return comparison;
                comparison = left.Constant0.CompareTo(right.Constant0);
                if (comparison != 0)
                    return comparison;
                comparison = left.Constant1.CompareTo(right.Constant1);
                if (comparison != 0)
                    return comparison;
                comparison = left.Constant2.CompareTo(right.Constant2);
                if (comparison != 0)
                    return comparison;
                comparison = left.Constant3.CompareTo(right.Constant3);
                if (comparison != 0)
                    return comparison;
                comparison = left.Operand0.CompareTo(right.Operand0);
                if (comparison != 0)
                    return comparison;
                comparison = left.Operand1.CompareTo(right.Operand1);
                if (comparison != 0)
                    return comparison;
                comparison = left.Operand2.CompareTo(right.Operand2);
                if (comparison != 0)
                    return comparison;
                return left.Operand3.CompareTo(right.Operand3);
            }
        }

        private sealed class CanonicalPayloadWriter
        {
            private readonly List<byte> m_Bytes = new(512);

            internal void WriteUInt32(uint value)
            {
                m_Bytes.Add((byte) value);
                m_Bytes.Add((byte) (value >> 8));
                m_Bytes.Add((byte) (value >> 16));
                m_Bytes.Add((byte) (value >> 24));
            }

            internal void WriteString(string value)
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                WriteUInt32((uint) value.Length);
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    m_Bytes.Add((byte) character);
                    m_Bytes.Add((byte) (character >> 8));
                }
            }

            internal byte[] ToArray()
            {
                return m_Bytes.ToArray();
            }
        }
    }
}
