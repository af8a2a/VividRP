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
    public class HDRISkyPassTests
    {
        [Test]
        public void Initialize_RegistersDepthInputAndColorOutput_WhenPassIsCreated()
        {
            IRenderPass renderPass = new HDRISkyPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "Color", "Depth" }));

            var colorEntry = textureEntries.Single(entry => entry.Name == "Color");
            var depthEntry = textureEntries.Single(entry => entry.Name == "Depth");

            Assert.That(colorEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(colorEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(colorEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_SRGB));

            Assert.That(depthEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(depthEntry.AttachmentIndex, Is.EqualTo(-1));
            Assert.That(depthEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(depthEntry.Texture.desc.DepthBufferBits, Is.EqualTo(DepthBits.None));
        }

        [Test]
        public void Prepare_ResizesColorAndDepthTexturesToCameraSize_WhenCameraDimensionsAreAvailable()
        {
            var pass = new HDRISkyPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 640;
            cameraData.actualHeight = 360;

            pass.Prepare(frameData);

            AssertTextureSize(pass, "m_ColorTarget", 640, 360);
            AssertTextureSize(pass, "m_DepthTexture", 640, 360);
        }

        [Test]
        public void BuildSkyParam_UsesHdrpCompatibleLayout_WhenVolumeSettingsAreApplied()
        {
            var skyParam = HDRISkyPass.BuildSkyParam(2.5f, 45f);

            Assert.That(skyParam.x, Is.EqualTo(0f));
            Assert.That(skyParam.y, Is.EqualTo(2.5f));
            Assert.That(skyParam.z, Is.EqualTo(-45f));
            Assert.That(skyParam.w, Is.EqualTo(0f));
        }

        private static void AssertTextureSize(HDRISkyPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var field = typeof(HDRISkyPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
        }
    }
}
