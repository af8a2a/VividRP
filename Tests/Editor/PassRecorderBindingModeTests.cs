using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class PassRecorderBindingModeTests
    {
        [SetUp]
        public void SetUp()
        {
            PassRecorder.Dispose();
        }

        [TearDown]
        public void TearDown()
        {
            PassRecorder.Dispose();
        }

        [Test]
        public void Compile_PreservesCtorOwnedResource_WhenNoOverrideBindingExists()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<RuntimePassOwnedTextureProducerPass>(),
            });

            try
            {
                Compile(graphAsset);

                var passes = GetCompiledPasses();
                Assert.That(passes, Has.Count.EqualTo(1));

                var pass = passes[0] as RuntimePassOwnedTextureProducerPass;
                var texture = GetTextureField(pass, RuntimePassOwnedTextureProducerPass.ColorFieldName);

                Assert.That(pass, Is.Not.Null);
                Assert.That(texture, Is.Not.Null);
                Assert.That(texture.desc.Name, Is.EqualTo("CtorOwnedColor"));

                var originalTexture = texture;
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.actualWidth = 320;
                cameraData.actualHeight = 180;

                pass.Prepare(frameData);

                Assert.That(GetTextureField(pass, RuntimePassOwnedTextureProducerPass.ColorFieldName), Is.SameAs(originalTexture));
                Assert.That(texture.desc.Width, Is.EqualTo(320));
                Assert.That(texture.desc.Height, Is.EqualTo(180));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void Compile_SharesCtorOwnedResource_WithDownstreamPassFieldBinding()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<RuntimePassOwnedTextureProducerPass>(),
            });
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<RuntimePassOwnedTextureConsumerPass>(),
                ResourceBindings =
                {
                    new RenderGraphPassResourceBinding
                    {
                        FieldName = RuntimePassOwnedTextureConsumerPass.SourceFieldName,
                        ResourceKind = RenderGraphResourceKind.Texture,
                        SourceKind = RenderGraphPassBindingSourceKind.PassField,
                        SourcePassIndex = 0,
                        SourceFieldName = RuntimePassOwnedTextureProducerPass.ColorFieldName,
                        ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
                    }
                }
            });

            try
            {
                Compile(graphAsset);

                var passes = GetCompiledPasses();
                Assert.That(passes, Has.Count.EqualTo(2));

                var producer = passes[0] as RuntimePassOwnedTextureProducerPass;
                var consumer = passes[1] as RuntimePassOwnedTextureConsumerPass;

                Assert.That(producer, Is.Not.Null);
                Assert.That(consumer, Is.Not.Null);
                Assert.That(
                    GetTextureField(consumer, RuntimePassOwnedTextureConsumerPass.SourceFieldName),
                    Is.SameAs(GetTextureField(producer, RuntimePassOwnedTextureProducerPass.ColorFieldName)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void Compile_SharesCtorOwnedAccelerationStructure_WithDownstreamPassFieldBinding()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<RuntimePassOwnedAccelerationStructureProducerPass>(),
            });
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<RuntimePassOwnedAccelerationStructureConsumerPass>(),
                ResourceBindings =
                {
                    new RenderGraphPassResourceBinding
                    {
                        FieldName = RuntimePassOwnedAccelerationStructureConsumerPass.SourceFieldName,
                        ResourceKind = RenderGraphResourceKind.AccelerationStructure,
                        SourceKind = RenderGraphPassBindingSourceKind.PassField,
                        SourcePassIndex = 0,
                        SourceFieldName = RuntimePassOwnedAccelerationStructureProducerPass.AccelerationStructureFieldName,
                        ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
                    }
                }
            });

            try
            {
                Compile(graphAsset);

                var passes = GetCompiledPasses();
                Assert.That(passes, Has.Count.EqualTo(2));

                var producer = passes[0] as RuntimePassOwnedAccelerationStructureProducerPass;
                var consumer = passes[1] as RuntimePassOwnedAccelerationStructureConsumerPass;

                Assert.That(producer, Is.Not.Null);
                Assert.That(consumer, Is.Not.Null);
                Assert.That(
                    GetAccelerationStructureField(consumer, RuntimePassOwnedAccelerationStructureConsumerPass.SourceFieldName),
                    Is.SameAs(GetAccelerationStructureField(producer, RuntimePassOwnedAccelerationStructureProducerPass.AccelerationStructureFieldName)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void Compile_BindsSharedAccelerationStructureResource_WhenStandaloneBindingExists()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.AccelerationStructureDescriptors.Add(RenderGraphAccelerationStructureDesc.Create("SharedSceneRTAS"));
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<RuntimePassOwnedAccelerationStructureProducerPass>(),
                ResourceBindings =
                {
                    new RenderGraphPassResourceBinding
                    {
                        FieldName = RuntimePassOwnedAccelerationStructureProducerPass.AccelerationStructureFieldName,
                        ResourceKind = RenderGraphResourceKind.AccelerationStructure,
                        ResourceIndex = 0,
                        SourceKind = RenderGraphPassBindingSourceKind.Resource,
                        ConnectionKind = RenderGraphPassBindingConnectionKind.Output,
                    }
                }
            });
            graphAsset.Passes.Add(new RenderGraphPassDefinition
            {
                PassType = GetPassTypeName<RuntimePassOwnedAccelerationStructureConsumerPass>(),
                ResourceBindings =
                {
                    new RenderGraphPassResourceBinding
                    {
                        FieldName = RuntimePassOwnedAccelerationStructureConsumerPass.SourceFieldName,
                        ResourceKind = RenderGraphResourceKind.AccelerationStructure,
                        ResourceIndex = 0,
                        SourceKind = RenderGraphPassBindingSourceKind.Resource,
                        ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
                    }
                }
            });

            try
            {
                Compile(graphAsset);

                var passes = GetCompiledPasses();
                var producer = passes[0] as RuntimePassOwnedAccelerationStructureProducerPass;
                var consumer = passes[1] as RuntimePassOwnedAccelerationStructureConsumerPass;

                var producerAccelerationStructure = GetAccelerationStructureField(
                    producer,
                    RuntimePassOwnedAccelerationStructureProducerPass.AccelerationStructureFieldName);
                var consumerAccelerationStructure = GetAccelerationStructureField(
                    consumer,
                    RuntimePassOwnedAccelerationStructureConsumerPass.SourceFieldName);

                Assert.That(producerAccelerationStructure, Is.Not.Null);
                Assert.That(consumerAccelerationStructure, Is.SameAs(producerAccelerationStructure));
                Assert.That(producerAccelerationStructure.desc.Name, Is.EqualTo("SharedSceneRTAS"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graphAsset);
            }
        }

        private static void Compile(RenderGraphData graphAsset)
        {
            var method = typeof(PassRecorder).GetMethod("Compile", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { graphAsset });
        }

        private static IList<IRenderPass> GetCompiledPasses()
        {
            var field = typeof(PassRecorder).GetField("s_RenderPasses", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null);
            return (IList<IRenderPass>)field.GetValue(null);
        }

        private static RenderGraphTexture GetTextureField(object pass, string fieldName)
        {
            Assert.That(pass, Is.Not.Null);

            var field = pass.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static RenderGraphAccelerationStructure GetAccelerationStructureField(object pass, string fieldName)
        {
            Assert.That(pass, Is.Not.Null);

            var field = pass.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (RenderGraphAccelerationStructure)field.GetValue(pass);
        }

        private static string GetPassTypeName<T>()
        {
            var type = typeof(T);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }
    }

    public sealed class RuntimePassOwnedTextureProducerPass : RasterPass
    {
        internal const string ColorFieldName = "m_Color";

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Write, AttachmentIndex = 0, BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_Color;

        public RuntimePassOwnedTextureProducerPass()
        {
            m_Color = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 1,
                    Height = 1,
                    ColorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    Name = "CtorOwnedColor"
                }
            };
        }

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_Color.desc.Width = cameraData.actualWidth;
            m_Color.desc.Height = cameraData.actualHeight;
        }

        public override void Record(RasterGraphContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    public sealed class RuntimePassOwnedTextureConsumerPass : RasterPass
    {
        internal const string SourceFieldName = "m_Source";

        [RenderGraphResource(Name = "Source", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Source = new RenderGraphTexture();

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(RasterGraphContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    public sealed class RuntimePassOwnedAccelerationStructureProducerPass : ComputePass
    {
        internal const string AccelerationStructureFieldName = "m_SceneAccelerationStructure";

        [RenderGraphResource(
            Name = "SceneRTAS",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphAccelerationStructure m_SceneAccelerationStructure;

        public RuntimePassOwnedAccelerationStructureProducerPass()
        {
            m_SceneAccelerationStructure = new RenderGraphAccelerationStructure
            {
                desc = RenderGraphAccelerationStructureDesc.Create("CtorOwnedSceneRTAS")
            };
        }

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

    public sealed class RuntimePassOwnedAccelerationStructureConsumerPass : ComputePass
    {
        internal const string SourceFieldName = "m_Source";

        [RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Read)]
        private RenderGraphAccelerationStructure m_Source = new();

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
