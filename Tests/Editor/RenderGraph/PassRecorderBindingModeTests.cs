using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

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
        public void Compile_DoesNotCreateFallbackPass_WhenAuthoredGraphHasNoPasses()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();

            try
            {
                Compile(graphAsset);

                var passes = GetCompiledPasses();
                Assert.That(passes, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graphAsset);
            }
        }

        [Test]
        public void GetPassActiveStatesBuffer_ReusesArray_WhenCapacityIsEnough()
        {
            var buffer = PassRecorder.GetPassActiveStatesBuffer(4);
            Assert.That(buffer.Length, Is.GreaterThanOrEqualTo(4));

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var reusedBuffer = true;
            for (var index = 0; index < 32; index++)
                reusedBuffer &= ReferenceEquals(PassRecorder.GetPassActiveStatesBuffer(4), buffer);

            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(reusedBuffer, Is.True);
            Assert.That(allocatedBytes, Is.Zero);
        }

        [Test]
        public void GetPassActiveStatesBuffer_GrowsArray_WhenCapacityIsInsufficient()
        {
            var buffer = PassRecorder.GetPassActiveStatesBuffer(4);
            var grownBuffer = PassRecorder.GetPassActiveStatesBuffer(8);

            Assert.That(grownBuffer, Is.Not.SameAs(buffer));
            Assert.That(grownBuffer.Length, Is.GreaterThanOrEqualTo(8));
        }

        [Test]
        public void AreImportedBufferHandlesEqual_DoesNotAllocateOrRequireResourceRegistry()
        {
            var constructor = typeof(BufferHandle).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(int), typeof(bool) },
                null);
            Assert.That(constructor, Is.Not.Null);

            var left = (BufferHandle)constructor.Invoke(new object[] { 1, false });
            var right = (BufferHandle)constructor.Invoke(new object[] { 2, false });
            Assert.That(left.IsValid(), Is.True);
            Assert.That(right.IsValid(), Is.True);
            Assert.That(PassRecorder.AreImportedBufferHandlesEqual(left, left), Is.True);
            Assert.That(PassRecorder.AreImportedBufferHandlesEqual(left, right), Is.False);

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var sameHandlesEqual = true;
            var differentHandlesEqual = false;
            for (var index = 0; index < 32; index++)
            {
                sameHandlesEqual &= PassRecorder.AreImportedBufferHandlesEqual(left, left);
                differentHandlesEqual |= PassRecorder.AreImportedBufferHandlesEqual(left, right);
            }

            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(sameHandlesEqual, Is.True);
            Assert.That(differentHandlesEqual, Is.False);
            Assert.That(allocatedBytes, Is.Zero);
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

        [Test]
        public void Compile_PreservesWriteOnlyColorAttachmentAccess_WhenLegacyInputBindingExists()
        {
            var graphAsset = ScriptableObject.CreateInstance<RenderGraphData>();
            graphAsset.TextureDescriptors.Add(RenderGraphTextureDesc.CreateColorTarget(
                1,
                1,
                GraphicsFormat.R8G8B8A8_UNorm));
            graphAsset.Passes.Add(new RenderGraphPassDefinition
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
                        ConnectionKind = RenderGraphPassBindingConnectionKind.Input,
                    }
                }
            });

            try
            {
                Compile(graphAsset);

                var pass = GetCompiledPasses().Single();
                var resources = GetCurrentPassResources(pass);
                var colorEntry = resources.Textures.Single(entry => entry.Name == "Color");

                Assert.That(colorEntry.Access, Is.EqualTo(AccessFlags.Write));
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

        private static PassResource GetCurrentPassResources(IRenderPass pass)
        {
            var method = typeof(PassRecorder).GetMethod("GetCurrentPassResources", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (PassResource)method.Invoke(null, new object[] { pass, null });
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

        public override void Record(RasterPassContext context)
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

        public override void Record(RasterPassContext context)
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

        public override void Record(ComputePassContext context)
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

        public override void Record(ComputePassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }
}
