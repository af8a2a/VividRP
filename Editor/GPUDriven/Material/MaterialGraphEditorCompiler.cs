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
                case MaterialNormalMapDecodeNode normalDecode:
                    graph.UnpackNormal(
                        nodeId,
                        GetValueInput(graph, normalDecode, MaterialNormalMapDecodeNode.NormalMapPortName),
                        GetValueInputOrDefault(graph, normalDecode, MaterialNormalMapDecodeNode.StrengthPortName, 1.0f));
                    return;
                case MaterialClampNode clamp:
                    graph.Clamp(
                        nodeId,
                        GetValueInput(graph, clamp, MaterialClampNode.InputPortName),
                        GetValueInput(graph, clamp, MaterialClampNode.MinPortName),
                        GetValueInput(graph, clamp, MaterialClampNode.MaxPortName));
                    return;
                case MaterialSmoothstepNode smoothstep:
                    graph.Smoothstep(
                        nodeId,
                        GetValueInput(graph, smoothstep, MaterialSmoothstepNode.Edge0PortName),
                        GetValueInput(graph, smoothstep, MaterialSmoothstepNode.Edge1PortName),
                        GetValueInput(graph, smoothstep, MaterialSmoothstepNode.InputPortName));
                    return;
                case MaterialRemapNode remap:
                    graph.Remap(
                        nodeId,
                        GetValueInput(graph, remap, MaterialRemapNode.InputPortName),
                        GetValueInput(graph, remap, MaterialRemapNode.InMinPortName),
                        GetValueInput(graph, remap, MaterialRemapNode.InMaxPortName),
                        GetValueInput(graph, remap, MaterialRemapNode.OutMinPortName),
                        GetValueInput(graph, remap, MaterialRemapNode.OutMaxPortName));
                    return;
                case MaterialNoise2DNode noise2D:
                {
                    MaterialGraphValue uv = GetValueInput(graph, noise2D, MaterialNoise2DNode.UVPortName);
                    MaterialGraphValue scale = GetValueInputOrDefault(graph, noise2D, MaterialNoise2DNode.ScalePortName, 8.0f);
                    if (noise2D.GetMode() == MaterialGraphNoiseMode.Gradient)
                        graph.GradientNoise2D(nodeId, uv, scale);
                    else
                        graph.ValueNoise2D(nodeId, uv, scale);
                    return;
                }
                case MaterialSubgraphNode subgraphNode:
                    AddSubgraph(graph, nodeId, subgraphNode);
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
                case MaterialOpenPBROpaqueNode openPbr:
                    graph.OpenPBROpaque(
                        nodeId,
                        GetOpenPBROpaqueInput(graph, openPbr, OpenPBROpaqueFieldSemantic.BaseWeight),
                        GetOpenPBROpaqueInput(graph, openPbr, OpenPBROpaqueFieldSemantic.BaseColor),
                        GetOpenPBROpaqueInput(graph, openPbr, OpenPBROpaqueFieldSemantic.BaseDiffuseRoughness),
                        GetOpenPBROpaqueInput(graph, openPbr, OpenPBROpaqueFieldSemantic.BaseMetalness),
                        GetOpenPBROpaqueInput(graph, openPbr, OpenPBROpaqueFieldSemantic.SpecularWeight),
                        GetOpenPBROpaqueInput(graph, openPbr, OpenPBROpaqueFieldSemantic.SpecularColor),
                        GetOpenPBROpaqueInput(graph, openPbr, OpenPBROpaqueFieldSemantic.SpecularRoughness),
                        GetOpenPBROpaqueInput(graph, openPbr, OpenPBROpaqueFieldSemantic.SpecularIor),
                        GetOpenPBROpaqueInput(graph, openPbr, OpenPBROpaqueFieldSemantic.NormalWS),
                        GetOpenPBROpaqueInput(graph, openPbr, OpenPBROpaqueFieldSemantic.EmissionLuminance),
                        GetOpenPBROpaqueInput(graph, openPbr, OpenPBROpaqueFieldSemantic.EmissionColor));
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
                case MaterialOpenPBROpaqueOutputNode openPbrOutput:
                    graph.Output(
                        nodeId,
                        GetClosureInput(graph, openPbrOutput, MaterialOpenPBROpaqueOutputNode.SurfacePortName),
                        GetValueInputOrDefault(
                            graph, openPbrOutput, MaterialOpenPBROpaqueOutputNode.CoveragePortName, 1.0f),
                        GetValueInputOrDefault(
                            graph, openPbrOutput, MaterialOpenPBROpaqueOutputNode.AlphaClipThresholdPortName, 0.0f),
                        graph.Constant(nodeId + ".LegacyEmission", float3.zero),
                        openPbrOutput.GetMaterialFeatures(),
                        MaterialShadingModelMask.OpenPBROpaque);
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
                case MaterialGraphUnaryOperator.Abs:
                    graph.Abs(nodeId, input);
                    break;
                case MaterialGraphUnaryOperator.Sin:
                    graph.Sin(nodeId, input);
                    break;
                case MaterialGraphUnaryOperator.Cos:
                    graph.Cos(nodeId, input);
                    break;
                case MaterialGraphUnaryOperator.Floor:
                    graph.Floor(nodeId, input);
                    break;
                case MaterialGraphUnaryOperator.Frac:
                    graph.Frac(nodeId, input);
                    break;
                case MaterialGraphUnaryOperator.Sqrt:
                    graph.Sqrt(nodeId, input);
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
                case MaterialGraphBinaryOperator.Pow:
                    graph.Pow(nodeId, left, right);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Material binary operator '{node.GetOperator()}' is not supported.");
            }
        }

        private static void AddSubgraph(
            RuntimeMaterialGraph graph,
            string nodeId,
            MaterialSubgraphNode node)
        {
            string assetPath = node.GetSubgraphAssetPath();
            if (string.IsNullOrEmpty(assetPath))
                throw new InvalidOperationException("Subgraph Reference node requires a SubgraphAssetPath.");

            var subEditorGraph = GraphDatabase.LoadGraph<MaterialGraphEditorGraph>(assetPath);
            if (subEditorGraph == null)
                throw new InvalidOperationException($"Subgraph asset '{assetPath}' could not be loaded.");

            var subRuntimeGraph = new RuntimeMaterialGraph();
            string subOutputSourceId = null;
            foreach (INode subNode in subEditorGraph.GetNodes())
            {
                if (subNode is MaterialSubgraphOutputNode subOutput)
                {
                    INode connected = GetConnectedNode(subOutput, MaterialSubgraphOutputNode.InputPortName);
                    subOutputSourceId = connected != null ? GetNodeId(connected) : null;
                    continue;
                }
                if (subNode is MaterialSubgraphNode)
                    throw new InvalidOperationException("Nested MaterialSubgraphNode recursion is not supported in v1.");
                AddNode(subRuntimeGraph, subNode, GetNodeId(subNode));
            }
            if (string.IsNullOrEmpty(subOutputSourceId))
                throw new InvalidOperationException($"Subgraph '{assetPath}' has no connected MaterialSubgraphOutputNode.");

            var bindings = new System.Collections.Generic.Dictionary<string, MaterialGraphValue>(StringComparer.Ordinal);
            MaterialGraphValue inputA = GetValueInput(graph, node, MaterialSubgraphNode.InputAPortName);
            if (inputA.IsValid && !string.IsNullOrEmpty(node.GetInputASymbol()))
                bindings[node.GetInputASymbol()] = inputA;
            MaterialGraphValue inputB = GetValueInput(graph, node, MaterialSubgraphNode.InputBPortName);
            if (inputB.IsValid && !string.IsNullOrEmpty(node.GetInputBSymbol()))
                bindings[node.GetInputBSymbol()] = inputB;

            graph.InlineValueSubgraph(nodeId, subRuntimeGraph, subOutputSourceId, bindings);
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

        private static MaterialGraphValue GetOpenPBROpaqueInput(
            RuntimeMaterialGraph graph,
            MaterialOpenPBROpaqueNode node,
            OpenPBROpaqueFieldSemantic semantic)
        {
            INode source = GetConnectedNode(
                node, MaterialOpenPBROpaqueAuthoring.GetPortName(semantic));
            return source != null
                ? graph.Value(GetNodeId(source))
                : MaterialOpenPBROpaqueAuthoring.CreateDefaultInput(
                    graph, GetNodeId(node), semantic);
        }

        private static MaterialGraphValue GetValueInputOrDefault(
            RuntimeMaterialGraph graph, INode node, string portName, float defaultValue)
        {
            INode source = GetConnectedNode(node, portName);
            return source != null
                ? graph.Value(GetNodeId(source))
                : graph.Constant(GetNodeId(node) + ".Default." + portName, defaultValue);
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
