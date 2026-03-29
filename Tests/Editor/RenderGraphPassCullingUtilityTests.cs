using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Runtime.RenderPass.Core.Sigma;

namespace VividRP.Editor.Tests
{
    public class RenderGraphPassCullingUtilityTests
    {
        [Test]
        public void GetLivePassIndices_ReturnsEmpty_WhenWritablePassHasNoFinalConsumer()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
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
                            ConnectionKind = RenderGraphPassBindingConnectionKind.Output,
                        }
                    }
                }
            };

            var livePassIndices = RenderGraphPassCullingUtility.GetLivePassIndices(
                passDefinitions,
                includePreviewConsumers: false);

            Assert.That(livePassIndices, Is.Empty);
        }

        [Test]
        public void GetLivePassIndices_PreservesProducerChain_WhenFinalPassConsumesOutput()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
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
                            ConnectionKind = RenderGraphPassBindingConnectionKind.Output,
                        }
                    }
                },
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
                            ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
                        }
                    }
                }
            };

            var livePassIndices = RenderGraphPassCullingUtility.GetLivePassIndices(
                passDefinitions,
                includePreviewConsumers: false);

            Assert.That(livePassIndices, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void GetLivePassIndices_PreservesPreviewProducer_WhenPreviewConsumersAreIncluded()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<DrawObjectPass>(),
                    PreviewTextureFields = { "Color" }
                }
            };

            var livePassIndices = RenderGraphPassCullingUtility.GetLivePassIndices(
                passDefinitions,
                includePreviewConsumers: true);

            Assert.That(livePassIndices, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void GetLivePassIndices_CullsPreviewProducer_WhenPreviewConsumersAreExcluded()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<DrawObjectPass>(),
                    PreviewTextureFields = { "Color" }
                }
            };

            var livePassIndices = RenderGraphPassCullingUtility.GetLivePassIndices(
                passDefinitions,
                includePreviewConsumers: false);

            Assert.That(livePassIndices, Is.Empty);
        }

        [Test]
        public void GetLivePassIndices_PreservesPassWithoutWritableResources()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<FinalBlitPass>(),
                }
            };

            var livePassIndices = RenderGraphPassCullingUtility.GetLivePassIndices(
                passDefinitions,
                includePreviewConsumers: false);

            Assert.That(livePassIndices, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void GetLivePassIndices_PreservesGlobalStatePass_WhenGraphOutputsAreUnused()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<ColorGradingPass>(),
                }
            };

            var livePassIndices = RenderGraphPassCullingUtility.GetLivePassIndices(
                passDefinitions,
                includePreviewConsumers: false);

            Assert.That(livePassIndices, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void GetLivePassIndices_CullsPassWithHiddenIntermediateWrites_WhenExternalOutputHasNoConsumer()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<SIGMAShadowDenoisePass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "m_DenoisedShadowTexture",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            ResourceIndex = 0,
                            SourceKind = RenderGraphPassBindingSourceKind.Resource,
                            ConnectionKind = RenderGraphPassBindingConnectionKind.Output,
                        }
                    }
                }
            };

            var livePassIndices = RenderGraphPassCullingUtility.GetLivePassIndices(
                passDefinitions,
                includePreviewConsumers: false);

            Assert.That(livePassIndices, Is.Empty);
        }

        [Test]
        public void GetLivePassIndices_CullsShadowProducerChain_WhenDenoisedShadowHasNoConsumer()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<DirectionalRayTracedShadowPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "m_DirectionalShadowTexture",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            ResourceIndex = 0,
                            SourceKind = RenderGraphPassBindingSourceKind.Resource,
                            ConnectionKind = RenderGraphPassBindingConnectionKind.Output,
                        }
                    }
                },
                new()
                {
                    PassType = GetPassTypeName<SIGMAShadowDenoisePass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "m_RawShadowTexture",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            ResourceIndex = 0,
                            SourceKind = RenderGraphPassBindingSourceKind.Resource,
                            ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
                        },
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "m_DenoisedShadowTexture",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            ResourceIndex = 1,
                            SourceKind = RenderGraphPassBindingSourceKind.Resource,
                            ConnectionKind = RenderGraphPassBindingConnectionKind.Output,
                        }
                    }
                }
            };

            var livePassIndices = RenderGraphPassCullingUtility.GetLivePassIndices(
                passDefinitions,
                includePreviewConsumers: false);

            Assert.That(livePassIndices, Is.Empty);
        }

        [Test]
        public void GetLivePassIndices_PreservesHistoryCurrentWriter_WhenNoSameFrameConsumerExists()
        {
            var passDefinitions = new List<RenderGraphPassDefinition>
            {
                new()
                {
                    PassType = GetPassTypeName<TemporalAAPass>(),
                    ResourceBindings =
                    {
                        new RenderGraphPassResourceBinding
                        {
                            FieldName = "m_HistoryColorCurrent",
                            ResourceKind = RenderGraphResourceKind.Texture,
                            ResourceIndex = 0,
                            ResourceBindingVariant = RenderGraphResourceBindingVariant.HistoryCurrent,
                            SourceKind = RenderGraphPassBindingSourceKind.Resource,
                            ConnectionKind = RenderGraphPassBindingConnectionKind.Output,
                        }
                    }
                }
            };

            var livePassIndices = RenderGraphPassCullingUtility.GetLivePassIndices(
                passDefinitions,
                includePreviewConsumers: false);

            Assert.That(livePassIndices, Is.EqualTo(new[] { 0 }));
        }

        private static string GetPassTypeName<T>()
        {
            var type = typeof(T);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }
    }
}
