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

        private static string GetPassTypeName<T>()
        {
            var type = typeof(T);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }
    }
}
