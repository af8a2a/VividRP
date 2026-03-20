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
        public void OrderPassDefinitions_PreservesPreviewTextureFields_WhenPassesAreReordered()
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
                    PreviewTextureFields = { "Color" }
                }
            };

            var ordered = RenderGraphPassCompilationUtility.OrderPassDefinitions(passDefinitions);

            Assert.That(ordered[0].PassType, Is.EqualTo(GetPassTypeName<DrawObjectPass>()));
            Assert.That(ordered[0].PreviewTextureFields, Is.EquivalentTo(new[] { "Color" }));
            Assert.That(ordered[1].PreviewTextureFields, Is.Empty);
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
        [UseWithGraph(typeof(RenderGraphEditorGraph))]
        private sealed class DrawObjectPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(DrawObjectPass).AssemblyQualifiedName;
        }

        [Serializable]
        [UseWithGraph(typeof(RenderGraphEditorGraph))]
        private sealed class FinalBlitPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(FinalBlitPass).AssemblyQualifiedName;
        }

        [Serializable]
        [UseWithGraph(typeof(RenderGraphEditorGraph))]
        private sealed class RTASBuildPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(RTASBuildPass).AssemblyQualifiedName;
        }

        [Serializable]
        [UseWithGraph(typeof(RenderGraphEditorGraph))]
        private sealed class RayTracingConsumerPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(RayTracingConsumerPass).AssemblyQualifiedName;
        }

        [Test]
        public void Compile_OrdersPassesByExecutionDependencies_WhenPassFieldInputsAreConnected()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var finalBlitNode = new FinalBlitPassNode();
                var drawObjectNode = new DrawObjectPassNode();

                graph.AddNode(finalBlitNode);
                graph.AddNode(drawObjectNode);
                graph.Connect(
                    drawObjectNode.GetOutputPortByName("m_ColorTarget"),
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
        public void Compile_OrdersPassesByAccelerationStructureDependencies_WhenPassFieldInputsAreConnected()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var consumerNode = new RayTracingConsumerPassNode();
                var buildNode = new RTASBuildPassNode();

                graph.AddNode(consumerNode);
                graph.AddNode(buildNode);
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

        public override void Record(ComputeGraphContext context)
        {
        }

        public override void Dispose()
        {
        }
    }
}
