using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public class VisibilityBufferPassTests
    {
        [Test]
        public void Initialize_RegistersVisibleMeshletBuffersVisibilityTargetAndDepth()
        {
            IRenderPass renderPass = new VisibilityBufferPass();

            var resources = renderPass.Initialize();
            var visibilityEntry = resources.Textures.Single(entry => entry.Name == "VisibilityBuffer");
            var depthEntry = resources.Textures.Single(entry => entry.Name == "Depth");
            var visibleMeshletRequestsEntry = resources.Buffers.Single(entry => entry.Name == "VisibleMeshletRenderRequests");
            var indirectArgsEntry = resources.Buffers.Single(entry => entry.Name == "VisibleMeshletIndirectArgs");

            Assert.That(resources.Textures, Has.Length.EqualTo(2));
            Assert.That(resources.Buffers, Has.Length.EqualTo(2));
            Assert.That(renderPass, Is.InstanceOf<UnsafePass>());

            Assert.That(visibilityEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(visibilityEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(visibilityEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32_UInt));

            Assert.That(depthEntry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(depthEntry.IsDepthAttachment, Is.True);
            Assert.That(depthEntry.Texture.desc.DepthBufferBits, Is.EqualTo(DepthBits.Depth32));

            Assert.That(visibleMeshletRequestsEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(visibleMeshletRequestsEntry.Buffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Structured));

            Assert.That(indirectArgsEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(
                indirectArgsEntry.Buffer.desc.Target,
                Is.EqualTo(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments));
        }

        [Test]
        public void Prepare_ResizesDefaultOutputs_AndLeavesGPUDrivenBuffersUnbound_WhenFrameDataDoesNotProvideThem()
        {
            VividGPUDrivenSystem.Shutdown();

            try
            {
                var pass = new VisibilityBufferPass();
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.actualWidth = 1024;
                cameraData.actualHeight = 576;

                pass.Prepare(frameData);

                var visibilityTexture = GetTextureField(pass, "m_VisibilityBuffer");
                var depthTexture = GetTextureField(pass, "m_Depth");
                var renderRequestsBuffer = GetBufferField(pass, "m_VisibleMeshletRenderRequests");
                var indirectArgsBuffer = GetBufferField(pass, "m_VisibleMeshletIndirectArgs");

                Assert.That(visibilityTexture.desc.Width, Is.EqualTo(1024));
                Assert.That(visibilityTexture.desc.Height, Is.EqualTo(576));
                Assert.That(depthTexture.desc.Width, Is.EqualTo(1024));
                Assert.That(depthTexture.desc.Height, Is.EqualTo(576));
                Assert.That(renderRequestsBuffer.HasImportedBuffer, Is.False);
                Assert.That(indirectArgsBuffer.HasImportedBuffer, Is.False);
            }
            finally
            {
                VividGPUDrivenSystem.Shutdown();
            }
        }

        [Test]
        public void Prepare_DoesNotOverwriteOverriddenOutputDescriptors()
        {
            var pass = new VisibilityBufferPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            var externalVisibility = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 320,
                    Height = 240,
                    ColorFormat = GraphicsFormat.R32G32_UInt,
                }
            };
            var externalDepth = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 640,
                    Height = 360,
                    ColorFormat = GraphicsFormat.None,
                    DepthBufferBits = DepthBits.Depth16,
                }
            };

            SetTextureField(pass, "m_VisibilityBuffer", externalVisibility);
            SetTextureField(pass, "m_Depth", externalDepth);

            pass.Prepare(frameData);

            Assert.That(externalVisibility.desc.Width, Is.EqualTo(320));
            Assert.That(externalVisibility.desc.Height, Is.EqualTo(240));
            Assert.That(externalDepth.desc.Width, Is.EqualTo(640));
            Assert.That(externalDepth.desc.Height, Is.EqualTo(360));
        }

        private static RenderGraphTexture GetTextureField(VisibilityBufferPass pass, string fieldName)
        {
            var field = typeof(VisibilityBufferPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static void SetTextureField(VisibilityBufferPass pass, string fieldName, RenderGraphTexture value)
        {
            var field = typeof(VisibilityBufferPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            field.SetValue(pass, value);
        }

        private static RenderGraphBuffer GetBufferField(VisibilityBufferPass pass, string fieldName)
        {
            var field = typeof(VisibilityBufferPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphBuffer)field.GetValue(pass);
        }
    }
}
