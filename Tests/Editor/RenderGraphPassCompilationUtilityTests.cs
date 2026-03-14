using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
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
            protected override string RegisteredPassTypeName => typeof(DrawObjectPass).AssemblyQualifiedName;
        }

        [Serializable]
        private sealed class FinalBlitPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(FinalBlitPass).AssemblyQualifiedName;
        }

        [Test]
        public void Compile_OrdersPassesByExecutionDependencies_WhenPassFieldInputsAreConnected()
        {
            var graph = new RenderGraphEditorGraph();
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

        [Test]
        public void Compile_ReturnsEmptyExecutionOrder_WhenGraphHasNoValidRenderPassNodes()
        {
            var graph = new RenderGraphEditorGraph();

            var result = RenderGraphCompiler.Compile(graph);

            Assert.That(result.ExecutionOrder, Is.Empty);
            Assert.That(result.Passes, Is.Empty);
        }

        private static string GetPassTypeName<T>()
        {
            var type = typeof(T);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }
    }
}
