using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
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

        private static string GetPassTypeName<T>()
        {
            var type = typeof(T);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }
    }
}
