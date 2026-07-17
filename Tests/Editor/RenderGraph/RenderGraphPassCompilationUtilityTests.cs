using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Runtime.RenderPass.Core.Sigma;

namespace VividRP.Editor.Tests
{
    public class RenderGraphPassCompilationUtilityTests
    {
        [Test]
        public void OrderPassDefinitions_SortsPassFieldDependencies_AndRemapsSourceIndices()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<FinalBlitPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "source",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            SourceKind = RenderGraphPassBindingSourceKind.PassField,
                            SourcePassIndex = 1,
                            SourceFieldName = "m_ColorTarget",
                        }
                    }
                },
                new()
                {
                    PassType = GetPassTypeName<DrawObjectPass>(),
                }
            };

            var ordered = RenderGraphPassCompilationUtility.OrderPassDefinitions(passDefinitions);

            Assert.That(ordered.Select(def => def.PassType), Is.EqualTo(new[]
            {
                GetPassTypeName<DrawObjectPass>(),
                GetPassTypeName<FinalBlitPass>(),
            }));
            Assert.That(ordered[1].ResourceBindings[0].SourcePassIndex, Is.EqualTo(0));
        }

        [Test]
        public void OrderPassDefinitions_SortsSharedResourceWriteBeforeRead()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<FinalBlitPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "source",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            ResourceIndex = 0,
                            SourceKind = RenderGraphPassBindingSourceKind.Resource,
                        }
                    }
                },
                new()
                {
                    PassType = GetPassTypeName<DrawObjectPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "m_ColorTarget",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            ResourceIndex = 0,
                            SourceKind = RenderGraphPassBindingSourceKind.Resource,
                        }
                    }
                }
            };

            var ordered = RenderGraphPassCompilationUtility.OrderPassDefinitions(passDefinitions);

            Assert.That(ordered.Select(def => def.PassType), Is.EqualTo(new[]
            {
                GetPassTypeName<DrawObjectPass>(),
                GetPassTypeName<FinalBlitPass>(),
            }));
        }

        [Test]
        public void OrderPassDefinitions_PreservesFloatParameters_WhenPassesAreReordered()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<FinalBlitPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "source",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            SourceKind = RenderGraphPassBindingSourceKind.PassField,
                            SourcePassIndex = 1,
                            SourceFieldName = "m_OutputTexture",
                        }
                    }
                },
                new()
                {
                    PassType = GetPassTypeName<SliderDebugPass>(),
                    FloatParameters =
                    {
                        new RenderGraphPassFloatParameter
                        {
                            FieldName = "m_Slider",
                            Value = 65f,
                        }
                    }
                }
            };

            var ordered = RenderGraphPassCompilationUtility.OrderPassDefinitions(passDefinitions);

            Assert.That(ordered[0].PassType, Is.EqualTo(GetPassTypeName<SliderDebugPass>()));
            Assert.That(ordered[0].FloatParameters, Has.Count.EqualTo(1));
            Assert.That(ordered[0].FloatParameters[0].FieldName, Is.EqualTo("m_Slider"));
            Assert.That(ordered[0].FloatParameters[0].Value, Is.EqualTo(65f));
            Assert.That(ordered[1].ResourceBindings[0].SourcePassIndex, Is.EqualTo(0));
        }

        [Test]
        public void OrderPassDefinitions_PreservesEnumParameters_WhenPassesAreReordered()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<FinalBlitPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "source",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            SourceKind = RenderGraphPassBindingSourceKind.PassField,
                            SourcePassIndex = 1,
                            SourceFieldName = "m_OutputTexture",
                        }
                    }
                },
                new()
                {
                    PassType = GetPassTypeName<RTASInstanceDebugPass>(),
                    EnumParameters =
                    {
                        new RenderGraphPassEnumParameter
                        {
                            FieldName = "m_VisualizationMode",
                            Value = (int)RTASInstanceDebugVisualizationMode.PrimitiveIndex,
                        }
                    }
                }
            };

            var ordered = RenderGraphPassCompilationUtility.OrderPassDefinitions(passDefinitions);

            Assert.That(ordered[0].PassType, Is.EqualTo(GetPassTypeName<RTASInstanceDebugPass>()));
            Assert.That(ordered[0].EnumParameters, Has.Count.EqualTo(1));
            Assert.That(ordered[0].EnumParameters[0].FieldName, Is.EqualTo("m_VisualizationMode"));
            Assert.That(
                ordered[0].EnumParameters[0].Value,
                Is.EqualTo((int)RTASInstanceDebugVisualizationMode.PrimitiveIndex));
            Assert.That(ordered[1].ResourceBindings[0].SourcePassIndex, Is.EqualTo(0));
        }

        [Test]
        public void OrderPassDefinitions_PreservesPassNameAndRenderListDescriptorParameters_WhenPassesAreReordered()
        {
            var descriptor = RenderGraphRenderListDesc.CreateTransparent("TransparentCharacter");
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<FinalBlitPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "source",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            SourceKind = RenderGraphPassBindingSourceKind.PassField,
                            SourcePassIndex = 1,
                            SourceFieldName = "m_ColorTarget",
                        }
                    }
                },
                new()
                {
                    PassType = GetPassTypeName<DrawObjectPass>(),
                    PassName = "Transparent Characters",
                    RenderListDescParameters =
                    {
                        new RenderGraphPassRenderListDescParameter
                        {
                            FieldName = "m_RenderListDesc",
                            Value = descriptor,
                        }
                    }
                }
            };

            var ordered = RenderGraphPassCompilationUtility.OrderPassDefinitions(passDefinitions);

            Assert.That(ordered[0].PassName, Is.EqualTo("Transparent Characters"));
            Assert.That(ordered[0].RenderListDescParameters, Has.Count.EqualTo(1));
            Assert.That(ordered[0].RenderListDescParameters[0].Value, Is.Not.SameAs(descriptor));
            Assert.That(
                ordered[0].RenderListDescParameters[0].Value.ShaderTagNames,
                Is.EqualTo(new[] { "TransparentCharacter" }));
            Assert.That(ordered[1].ResourceBindings[0].SourcePassIndex, Is.EqualTo(0));
        }

        [Test]
        public void OrderPassDefinitions_SortsSharedResourceWriterBeforeWriteOnlyInputConsumer()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<GBufferPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "m_GBufferDepth",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            ResourceIndex = 0,
                            SourceKind = RenderGraphPassBindingSourceKind.Resource,
                            ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
                        }
                    }
                },
                new()
                {
                    PassType = GetPassTypeName<DrawObjectPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "m_DepthTarget",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            ResourceIndex = 0,
                            SourceKind = RenderGraphPassBindingSourceKind.Resource,
                            ConnectionKind = RenderGraphPassBindingConnectionKind.Output,
                        }
                    }
                }
            };

            var ordered = RenderGraphPassCompilationUtility.OrderPassDefinitions(passDefinitions);

            Assert.That(ordered.Select(def => def.PassType), Is.EqualTo(new[]
            {
                GetPassTypeName<DrawObjectPass>(),
                GetPassTypeName<GBufferPass>(),
            }));
            Assert.That(ordered[1].ResourceBindings[0].ConnectionKind, Is.EqualTo(RenderGraphPassBindingConnectionKind.Input));
        }

        [Test]
        public void ResolveEffectiveAccess_UpgradesWriteOnlyBinding_WhenResourceIsConnectedThroughInput()
        {
            var binding = new RenderGraphPassResourceBinding
            {
                ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
            };

            var effectiveAccess = RenderGraphPassBindingUtility.ResolveEffectiveAccess(binding, AccessFlags.Write);

            Assert.That(effectiveAccess, Is.EqualTo(AccessFlags.ReadWrite));
        }

        [Test]
        public void OrderPassDefinitions_PreservesAsyncComputeFlag_WhenPassesAreReordered()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<FinalBlitPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "source",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            SourceKind = RenderGraphPassBindingSourceKind.PassField,
                            SourcePassIndex = 1,
                            SourceFieldName = "m_ColorTarget",
                        }
                    }
                },
                new()
                {
                    PassType = GetPassTypeName<ClassificationPass>(),
                    EnableAsyncCompute = true,
                }
            };

            var ordered = RenderGraphPassCompilationUtility.OrderPassDefinitions(passDefinitions);

            Assert.That(ordered[0].PassType, Is.EqualTo(GetPassTypeName<ClassificationPass>()));
            Assert.That(ordered[0].EnableAsyncCompute, Is.True);
            Assert.That(ordered[1].EnableAsyncCompute, Is.False);
        }

        [Test]
        public void OrderPassDefinitions_PreservesAsyncComputeFlag_WhenPassOrderDoesNotChange()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<ClassificationPass>(),
                    EnableAsyncCompute = true,
                }
            };

            var ordered = RenderGraphPassCompilationUtility.OrderPassDefinitions(passDefinitions);

            Assert.That(ordered, Has.Count.EqualTo(1));
            Assert.That(ordered[0].EnableAsyncCompute, Is.True);
        }

        [Test]
        public void OrderPassDefinitions_SortsSharedAccelerationStructureWriteBeforeRead()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<RayTracingConsumerPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "m_SceneAccelerationStructure",
                            ResourceKind = RenderGraphResourceKind.AccelerationStructure,
                            ResourceIndex = 0,
                            SourceKind = RenderGraphPassBindingSourceKind.Resource,
                            ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
                        }
                    }
                },
                new()
                {
                    PassType = GetPassTypeName<RTASBuildPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "m_SceneAccelerationStructure",
                            ResourceKind = RenderGraphResourceKind.AccelerationStructure,
                            ResourceIndex = 0,
                            SourceKind = RenderGraphPassBindingSourceKind.Resource,
                            ConnectionKind = RenderGraphPassBindingConnectionKind.Output,
                        }
                    }
                }
            };

            var ordered = RenderGraphPassCompilationUtility.OrderPassDefinitions(passDefinitions);

            Assert.That(ordered.Select(def => def.PassType), Is.EqualTo(new[]
            {
                GetPassTypeName<RTASBuildPass>(),
                GetPassTypeName<RayTracingConsumerPass>(),
            }));
        }

        private static string GetPassTypeName<T>()
        {
            var type = typeof(T);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }
    }

    public class RenderGraphCompilerTests
    {
        [Serializable]
        private sealed class DrawObjectPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(DrawObjectPass);
        }

        [Serializable]
        private sealed class FinalBlitPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(FinalBlitPass);
        }

        [Serializable]
        private sealed class RTASBuildPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(RTASBuildPass);
        }

        [Serializable]
        private sealed class RayTracingConsumerPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(RayTracingConsumerPass);
        }

        [Serializable]
        private sealed class DirectionalRayTracedShadowPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(DirectionalRayTracedShadowPass);
        }

        [Serializable]
        private sealed class SIGMAShadowDenoisePassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(SIGMAShadowDenoisePass);
        }

        [Test]
        public void Compile_OrdersPassesByExecutionDependencies_WhenPassFieldInputsAreConnected()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var finalBlitNode = new FinalBlitPassNode();
                var drawObjectNode = new DrawObjectPassNode();

                RenderGraphTestUtility.AddTestNode(graph, finalBlitNode);
                RenderGraphTestUtility.AddTestNode(graph, drawObjectNode);
                drawObjectNode.Title = string.Empty;
                finalBlitNode.Title = string.Empty;
                graph.Connect(
                    drawObjectNode.GetOutputPortByName("m_ColorTarget_Out"),
                    finalBlitNode.GetInputPortByName("source"));

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.ExecutionOrder.Select(pass => pass.PassTypeName), Is.EqualTo(new[]
                {
                    nameof(DrawObjectPass),
                    nameof(FinalBlitPass),
                }));
                Assert.That(result.Passes.Select(pass => pass.PassType), Is.EqualTo(new[]
                {
                    GetPassTypeName<DrawObjectPass>(),
                    GetPassTypeName<FinalBlitPass>(),
                }));
                Assert.That(result.Passes.Select(pass => pass.PassName), Is.EqualTo(new[]
                {
                    drawObjectNode.Title,
                    finalBlitNode.Title,
                }));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_StoresCustomNodeTitleAndEmbeddedRenderListDescriptor()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var finalBlitNode = new FinalBlitPassNode();
                var drawObjectNode = new DrawObjectPassNode();
                var descriptor = RenderGraphRenderListDesc.CreateTransparent("TransparentCharacter", "SpecialForward");
                descriptor.LayerMask = 1 << 6;

                RenderGraphTestUtility.AddTestNode(graph, finalBlitNode);
                RenderGraphTestUtility.AddTestNode(graph, drawObjectNode);
                drawObjectNode.Title = "Transparent Characters";
                var descriptorOption = drawObjectNode.GetNodeOptionByName(
                    RenderGraphPassRenderListDescParameterUtility.GetOptionName("m_RenderListDesc"));

                Assert.That(descriptorOption, Is.Not.Null);
                Assert.That(descriptorOption.TrySetValue(descriptor), Is.True);
                graph.Connect(
                    drawObjectNode.GetOutputPortByName("m_ColorTarget_Out"),
                    finalBlitNode.GetInputPortByName("source"));

                var result = RenderGraphCompiler.Compile(graph);
                var passDefinition = result.Passes[0];

                Assert.That(passDefinition.PassName, Is.EqualTo("Transparent Characters"));
                Assert.That(result.ExecutionOrder[0].DisplayName, Is.EqualTo("Transparent Characters"));
                Assert.That(passDefinition.RenderListDescParameters, Has.Count.EqualTo(1));
                Assert.That(passDefinition.RenderListDescParameters[0].FieldName, Is.EqualTo("m_RenderListDesc"));
                Assert.That(passDefinition.RenderListDescParameters[0].Value, Is.Not.SameAs(descriptor));
                Assert.That(passDefinition.RenderListDescParameters[0].Value.RenderQueueRange, Is.EqualTo(RenderGraphRenderQueueRange.Transparent));
                Assert.That(passDefinition.RenderListDescParameters[0].Value.LayerMask.value, Is.EqualTo(1 << 6));
                Assert.That(passDefinition.RenderListDescParameters[0].Value.ShaderTagNames, Is.EqualTo(descriptor.ShaderTagNames));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_CreatesRenderListBinding_WhenDrawObjectOverrideIsConnected()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var finalBlitNode = new FinalBlitPassNode();
                var drawObjectNode = new DrawObjectPassNode();
                var renderListNode = new RenderListResourceNodeData();
                RenderGraphTestUtility.AddTestNode(graph, finalBlitNode);
                RenderGraphTestUtility.AddTestNode(graph, drawObjectNode);
                RenderGraphTestUtility.AddTestNode(graph, renderListNode);

                var overrideOption = drawObjectNode.GetNodeOptionByName(
                    RenderPassPortUtility.GetOverrideOptionName("m_RenderList"));
                Assert.That(overrideOption, Is.Not.Null);
                Assert.That(overrideOption.TrySetValue(true), Is.True);
                drawObjectNode.DefineNode();

                Assert.That(
                    graph.Connect(
                        renderListNode.GetOutputPortByName(RenderListResourceNodeData.OutputPortName),
                        drawObjectNode.GetInputPortByName("m_RenderList")),
                    Is.True);
                Assert.That(
                    graph.Connect(
                        drawObjectNode.GetOutputPortByName("m_ColorTarget_Out"),
                        finalBlitNode.GetInputPortByName("source")),
                    Is.True);

                var result = RenderGraphCompiler.Compile(graph);
                var drawPass = result.Passes.Single(pass => pass.PassType.StartsWith(typeof(DrawObjectPass).FullName));
                var renderListBinding = drawPass.ResourceBindings.Single(binding => binding.FieldName == "m_RenderList");

                Assert.That(result.RenderListDescriptors, Has.Count.EqualTo(1));
                Assert.That(renderListBinding.ResourceKind, Is.EqualTo(RenderGraphResourceKind.RenderList));
                Assert.That(renderListBinding.SourceKind, Is.EqualTo(RenderGraphPassBindingSourceKind.Resource));
                Assert.That(renderListBinding.ResourceIndex, Is.EqualTo(0));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_KeepsEmbeddedDescriptorWithoutBinding_WhenDrawObjectOverrideIsUnconnected()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var finalBlitNode = new FinalBlitPassNode();
                var drawObjectNode = new DrawObjectPassNode();
                RenderGraphTestUtility.AddTestNode(graph, finalBlitNode);
                RenderGraphTestUtility.AddTestNode(graph, drawObjectNode);

                var overrideOption = drawObjectNode.GetNodeOptionByName(
                    RenderPassPortUtility.GetOverrideOptionName("m_RenderList"));
                Assert.That(overrideOption, Is.Not.Null);
                Assert.That(overrideOption.TrySetValue(true), Is.True);
                drawObjectNode.DefineNode();
                Assert.That(
                    graph.Connect(
                        drawObjectNode.GetOutputPortByName("m_ColorTarget_Out"),
                        finalBlitNode.GetInputPortByName("source")),
                    Is.True);

                var result = RenderGraphCompiler.Compile(graph);
                var drawPass = result.Passes.Single(pass => pass.PassType.StartsWith(typeof(DrawObjectPass).FullName));

                Assert.That(drawPass.ResourceBindings.Any(binding => binding.FieldName == "m_RenderList"), Is.False);
                Assert.That(drawPass.RenderListDescParameters, Has.Count.EqualTo(1));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_ReturnsEmptyExecutionOrder_WhenGraphHasNoValidRenderPassNodes()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.ExecutionOrder, Is.Empty);
                Assert.That(result.Passes, Is.Empty);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_CullsUnusedPasses_WhenOutputsAreNotConsumed()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var drawObjectNode = new DrawObjectPassNode();
                RenderGraphTestUtility.AddTestNode(graph, drawObjectNode);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.ExecutionOrder, Is.Empty);
                Assert.That(result.Passes, Is.Empty);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_CullsShadowChain_WhenDenoisedShadowIsUnused()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var shadowPassNode = new DirectionalRayTracedShadowPassNode();
                var sigmaPassNode = new SIGMAShadowDenoisePassNode();

                RenderGraphTestUtility.AddTestNode(graph, shadowPassNode);
                RenderGraphTestUtility.AddTestNode(graph, sigmaPassNode);
                graph.Connect(
                    shadowPassNode.GetOutputPortByName("m_DirectionalShadowTexture"),
                    sigmaPassNode.GetInputPortByName("m_RawShadowTexture"));

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.ExecutionOrder, Is.Empty);
                Assert.That(result.Passes, Is.Empty);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_OrdersPassesByAccelerationStructureDependencies_WhenPassFieldInputsAreConnected()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var consumerNode = new RayTracingConsumerPassNode();
                var buildNode = new RTASBuildPassNode();

                RenderGraphTestUtility.AddTestNode(graph, consumerNode);
                RenderGraphTestUtility.AddTestNode(graph, buildNode);
                graph.Connect(
                    buildNode.GetOutputPortByName("m_SceneAccelerationStructure"),
                    consumerNode.GetInputPortByName("m_SceneAccelerationStructure"));

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.ExecutionOrder.Select(pass => pass.PassTypeName), Is.EqualTo(new[]
                {
                    nameof(RTASBuildPass),
                    nameof(RayTracingConsumerPass),
                }));
                Assert.That(result.Passes.Select(pass => pass.PassType), Is.EqualTo(new[]
                {
                    GetPassTypeName<RTASBuildPass>(),
                    GetPassTypeName<RayTracingConsumerPass>(),
                }));
                Assert.That(result.Passes[1].ResourceBindings[0].ResourceKind, Is.EqualTo(RenderGraphResourceKind.AccelerationStructure));
                Assert.That(result.Passes[1].ResourceBindings[0].SourceKind, Is.EqualTo(RenderGraphPassBindingSourceKind.PassField));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        private static string GetPassTypeName<T>()
        {
            var type = typeof(T);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }
    }

    public sealed class RayTracingConsumerPass : ComputePass
    {
        [RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Read)]
        private RenderGraphAccelerationStructure m_SceneAccelerationStructure = new();

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(ComputePassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }
}
