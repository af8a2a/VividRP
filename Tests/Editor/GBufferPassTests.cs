using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class GBufferPassTests
    {
        [Test]
        public void Initialize_RegistersFourColorAttachmentsAndDepth_WhenPassIsCreated()
        {
            IRenderPass renderPass = new GBufferPass();

            var resources = renderPass.Initialize();
            var colorEntries = resources.Textures
                .Where(entry => !entry.IsDepthAttachment)
                .OrderBy(entry => entry.AttachmentIndex)
                .ToArray();
            var depthEntry = resources.Textures.Single(entry => entry.IsDepthAttachment);

            Assert.That(resources.RenderLists, Has.Length.EqualTo(1));
            Assert.That(resources.Textures, Has.Length.EqualTo(5));
            Assert.That(resources.RenderLists[0].RenderList.desc.ShaderTagNames, Is.EqualTo(new[] { "VividGBuffer" }));

            Assert.That(colorEntries, Has.Length.EqualTo(4));
            Assert.That(colorEntries.Select(entry => entry.AttachmentIndex), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(colorEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "GBuffer0", "GBuffer1", "GBuffer2", "GBuffer3" }));

            Assert.That(colorEntries[0].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(colorEntries[1].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16_SFloat));
            Assert.That(colorEntries[2].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(colorEntries[3].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(depthEntry.Texture.desc.DepthBufferBits, Is.EqualTo(DepthBits.Depth32));
        }

        [Test]
        public void Prepare_ResizesAllGBufferTargets_WhenCameraSizeChanges()
        {
            var pass = new GBufferPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 960;
            cameraData.actualHeight = 540;

            pass.Prepare(frameData);

            AssertTextureSize(pass, "m_GBuffer0", 960, 540);
            AssertTextureSize(pass, "m_GBuffer1", 960, 540);
            AssertTextureSize(pass, "m_GBuffer2", 960, 540);
            AssertTextureSize(pass, "m_GBuffer3", 960, 540);
            AssertTextureSize(pass, "m_GBufferDepth", 960, 540);
        }

        private static void AssertTextureSize(GBufferPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var field = typeof(GBufferPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);

            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
        }
    }
}
