using System;
using Unity.GraphToolkit.Editor;
using Unity.Mathematics;
using UnityEngine;
using VividRP.Runtime.GPUDriven;
using RuntimeMaterialGraph = VividRP.Runtime.GPUDriven.MaterialGraph;

namespace VividRP.Editor.GPUDriven
{
    internal static class MaterialGraphEditorCompiler
    {
        internal static MaterialGraphCompilationResult Compile(
            MaterialGraphEditorGraph graph)
        {
            return Compile(graph, GPUDrivenMaterialCompiler.ProgramVersion);
        }

        internal static MaterialGraphCompilationResult Compile(
            MaterialGraphEditorGraph graph,
            uint programVersion)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            var source = new RuntimeMaterialGraph();
            string currentNodeId = string.Empty;
            try
            {
                foreach (INode node in graph.GetNodes())
                {
                    currentNodeId = GetNodeId(node);
                    AddNode(source, node, currentNodeId);
                }
            }
            catch (ArgumentException exception)
            {
                return CreateAdapterFailure(currentNodeId, exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return CreateAdapterFailure(currentNodeId, exception.Message);
            }
            catch (NotSupportedException exception)
            {
                return CreateAdapterFailure(currentNodeId, exception.Message);
            }

            return MaterialGraphCompiler.Compile(source, programVersion);
        }

        private static void AddNode(
            RuntimeMaterialGraph graph,
            INode node,
            string nodeId)
        {
            switch (node)
            {
                case MaterialParameterNode parameter:
                    graph.Parameter(nodeId, parameter.GetParameter());
                    return;
                case MaterialNamedParameterNode namedParameter:
                    graph.Parameter(nodeId, namedParameter.GetDeclaration());
                    return;
                case MaterialExternalInputNode externalInput:
                    graph.ExternalInput(nodeId, externalInput.GetInput());
                    return;
                case MaterialTextureResourceNode textureResource:
                    graph.TextureResource(nodeId, textureResource.GetResource());
                    return;
                case MaterialNamedTextureResourceNode namedTextureResource:
                    graph.TextureResource(nodeId, namedTextureResource.GetDeclaration());
                    return;
                case MaterialConstantNode constant:
                    AddConstant(graph, nodeId, constant);
                    return;
                case MaterialTextureSampleNode textureSample:
                    graph.TextureSample(
                        nodeId,
                        GetValueInput(graph, textureSample, MaterialTextureSampleNode.TexturePortName),
                        GetValueInput(graph, textureSample, MaterialTextureSampleNode.UVPortName));
                    return;
                case MaterialUnaryNode unary:
                    AddUnary(graph, nodeId, unary);
                    return;
                case MaterialBinaryNode binary:
                    AddBinary(graph, nodeId, binary);
                    return;
                case MaterialSwizzleNode swizzle:
                    graph.Swizzle(
                        nodeId,
                        GetValueInput(graph, swizzle, MaterialSwizzleNode.InputPortName),
                        GetSwizzleMask(swizzle.GetSwizzle()));
                    return;
                case MaterialStandardSlabNode slab:
                    graph.Slab(
                        nodeId,
                        GetValueInput(graph, slab, MaterialStandardSlabNode.BaseColorPortName),
                        GetValueInput(graph, slab, MaterialStandardSlabNode.RoughnessPortName),
                        GetValueInput(graph, slab, MaterialStandardSlabNode.MetallicPortName),
                        GetValueInput(graph, slab, MaterialStandardSlabNode.NormalPortName),
                        GetValueInput(graph, slab, MaterialStandardSlabNode.TangentPortName),
                        slab.GetFeatureMask());
                    return;
                case MaterialHorizontalMixNode horizontalMix:
                    graph.HorizontalMix(
                        nodeId,
                        GetClosureInput(
                            graph,
                            horizontalMix,
                            MaterialHorizontalMixNode.BackgroundPortName),
                        GetClosureInput(
                            graph,
                            horizontalMix,
                            MaterialHorizontalMixNode.ForegroundPortName),
                        GetValueInput(
                            graph,
                            horizontalMix,
                            MaterialHorizontalMixNode.WeightPortName));
                    return;
                case MaterialVerticalLayerNode verticalLayer:
                    graph.VerticalLayer(
                        nodeId,
                        GetClosureInput(
                            graph,
                            verticalLayer,
                            MaterialVerticalLayerNode.BottomPortName),
                        GetClosureInput(
                            graph,
                            verticalLayer,
                            MaterialVerticalLayerNode.TopPortName),
                        GetValueInput(
                            graph,
                            verticalLayer,
                            MaterialVerticalLayerNode.WeightPortName));
                    return;
                case MaterialOutputNode output:
                    graph.Output(
                        nodeId,
                        GetClosureInput(graph, output, MaterialOutputNode.SurfacePortName),
                        GetValueInput(graph, output, MaterialOutputNode.CoveragePortName),
                        GetValueInput(
                            graph,
                            output,
                            MaterialOutputNode.AlphaClipThresholdPortName),
                        GetValueInput(graph, output, MaterialOutputNode.EmissionPortName),
                        output.GetMaterialFeatures(),
                        output.GetShadingModels());
                    return;
            }
        }

        private static void AddConstant(
            RuntimeMaterialGraph graph,
            string nodeId,
            MaterialConstantNode node)
        {
            Vector4 value = node.GetValue();
            switch (node.GetConstantType())
            {
                case MaterialGraphConstantType.Bool:
                    graph.Constant(nodeId, value.x != 0.0f);
                    break;
                case MaterialGraphConstantType.Float:
                    graph.Constant(nodeId, value.x);
                    break;
                case MaterialGraphConstantType.Float2:
                    graph.Constant(nodeId, new float2(value.x, value.y));
                    break;
                case MaterialGraphConstantType.Float3:
                    graph.Constant(nodeId, new float3(value.x, value.y, value.z));
                    break;
                case MaterialGraphConstantType.Float4:
                    graph.Constant(
                        nodeId,
                        new float4(value.x, value.y, value.z, value.w));
                    break;
                default:
                    throw new NotSupportedException(
                        $"Material constant type '{node.GetConstantType()}' is not supported.");
            }
        }

        private static void AddUnary(
            RuntimeMaterialGraph graph,
            string nodeId,
            MaterialUnaryNode node)
        {
            MaterialGraphValue input = GetValueInput(
                graph,
                node,
                MaterialUnaryNode.InputPortName);
            switch (node.GetOperator())
            {
                case MaterialGraphUnaryOperator.Saturate:
                    graph.Saturate(nodeId, input);
                    break;
                case MaterialGraphUnaryOperator.OneMinus:
                    graph.OneMinus(nodeId, input);
                    break;
                case MaterialGraphUnaryOperator.Normalize:
                    graph.Normalize(nodeId, input);
                    break;
                case MaterialGraphUnaryOperator.Ddx:
                    graph.Ddx(nodeId, input);
                    break;
                case MaterialGraphUnaryOperator.Ddy:
                    graph.Ddy(nodeId, input);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Material unary operator '{node.GetOperator()}' is not supported.");
            }
        }

        private static void AddBinary(
            RuntimeMaterialGraph graph,
            string nodeId,
            MaterialBinaryNode node)
        {
            MaterialGraphValue left = GetValueInput(
                graph,
                node,
                MaterialBinaryNode.LeftPortName);
            MaterialGraphValue right = GetValueInput(
                graph,
                node,
                MaterialBinaryNode.RightPortName);
            switch (node.GetOperator())
            {
                case MaterialGraphBinaryOperator.Add:
                    graph.Add(nodeId, left, right);
                    break;
                case MaterialGraphBinaryOperator.Multiply:
                    graph.Multiply(nodeId, left, right);
                    break;
                case MaterialGraphBinaryOperator.Subtract:
                    graph.Subtract(nodeId, left, right);
                    break;
                case MaterialGraphBinaryOperator.Divide:
                    graph.Divide(nodeId, left, right);
                    break;
                case MaterialGraphBinaryOperator.Min:
                    graph.Min(nodeId, left, right);
                    break;
                case MaterialGraphBinaryOperator.Max:
                    graph.Max(nodeId, left, right);
                    break;
                case MaterialGraphBinaryOperator.Dot:
                    graph.Dot(nodeId, left, right);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Material binary operator '{node.GetOperator()}' is not supported.");
            }
        }

        private static MaterialGraphValue GetValueInput(
            RuntimeMaterialGraph graph,
            INode node,
            string portName)
        {
            INode source = GetConnectedNode(node, portName);
            return source != null
                ? graph.Value(GetNodeId(source))
                : default;
        }

        private static MaterialGraphClosure GetClosureInput(
            RuntimeMaterialGraph graph,
            INode node,
            string portName)
        {
            INode source = GetConnectedNode(node, portName);
            return source != null
                ? graph.Closure(GetNodeId(source))
                : default;
        }

        private static INode GetConnectedNode(INode node, string portName)
        {
            return node.GetInputPortByName(portName)
                ?.FirstConnectedPort
                ?.GetNode();
        }

        private static string GetNodeId(INode node)
        {
            return node.ID.ToString();
        }

        private static MaterialSwizzleMask GetSwizzleMask(MaterialGraphSwizzle swizzle)
        {
            switch (swizzle)
            {
                case MaterialGraphSwizzle.X:
                    return MaterialSwizzleMask.X;
                case MaterialGraphSwizzle.Y:
                    return MaterialSwizzleMask.Y;
                case MaterialGraphSwizzle.Z:
                    return MaterialSwizzleMask.Z;
                case MaterialGraphSwizzle.W:
                    return MaterialSwizzleMask.W;
                case MaterialGraphSwizzle.XYZ:
                    return MaterialSwizzleMask.XYZ;
                default:
                    throw new NotSupportedException(
                        $"Material swizzle '{swizzle}' is not supported.");
            }
        }

        private static MaterialGraphCompilationResult CreateAdapterFailure(
            string nodeId,
            string message)
        {
            return new MaterialGraphCompilationResult(
                null,
                null,
                null,
                new[]
                {
                    new MaterialGraphDiagnostic(
                        MaterialIRDiagnosticSeverity.Error,
                        MaterialGraphDiagnosticCodes.InvalidNode,
                        message,
                        nodeId,
                        "Out"),
                });
        }
    }
}
