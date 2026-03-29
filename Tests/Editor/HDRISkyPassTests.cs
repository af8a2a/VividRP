using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using ResourcePathAttribute = VividRP.Runtime.ResourcePathAttribute;

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
        public void Prepare_CachesSkyDataFromFrameContext_WhenSkyDataIsAvailable()
        {
            var pass = new HDRISkyPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var skyData = frameData.GetOrCreate<VividSkyData>();
            var cubemap = new Cubemap(4, TextureFormat.RGBA32, false);

            try
            {
                cameraData.actualWidth = 320;
                cameraData.actualHeight = 180;
                skyData.activeSkyType = SkyType.HDRI;
                skyData.specularCubemap = cubemap;
                skyData.tint = Color.red;
                skyData.exposure = 2.0f;
                skyData.rotation = 45.0f;

                pass.Prepare(frameData);

                Assert.That(GetFieldValue<Cubemap>(pass, "m_Cubemap"), Is.SameAs(cubemap));
                Assert.That(GetFieldValue<Color>(pass, "m_Tint"), Is.EqualTo(Color.red));
                Assert.That(GetFieldValue<float>(pass, "m_Exposure"), Is.EqualTo(2.0f));
                Assert.That(GetFieldValue<float>(pass, "m_Rotation"), Is.EqualTo(45.0f));
            }
            finally
            {
                Object.DestroyImmediate(cubemap);
            }
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

        [Test]
        public void VividRPCoreResources_DeclaresDefaultHDRISkyCubemap()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.DefaultHDRISkyCubemap));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Texture/Default/DefaultHDRISky.exr"));
        }

        [Test]
        public void CreateInstance_AssignsDefaultSkyCubemap_WhenVolumeComponentIsCreated()
        {
            var component = ScriptableObject.CreateInstance<HDRISkyVolume>();

            try
            {
                Assert.That(HDRISkyVolume.GetDefaultSkyCubemap(), Is.Not.Null);
                Assert.That(component.skyCubemap.value, Is.SameAs(HDRISkyVolume.GetDefaultSkyCubemap()));
                Assert.That(component.HasSkyCubemap(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(component);
            }
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

        private static T GetFieldValue<T>(HDRISkyPass pass, string fieldName)
        {
            var field = typeof(HDRISkyPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }
    }
}
