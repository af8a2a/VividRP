using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class DrawObjectPassTests
    {
        [Test]
        public void Initialize_RegistersRenderListAndAttachments()
        {
            IRenderPass renderPass = new DrawObjectPass();

            var resources = renderPass.Initialize();
            var colorEntry = resources.Textures.Single(entry => entry.Name == "Color");
            var depthEntry = resources.Textures.Single(entry => entry.Name == "Depth");

            Assert.That(resources.RenderLists, Has.Length.EqualTo(1));
            Assert.That(resources.Buffers, Is.Empty);
            Assert.That(resources.Textures, Has.Length.EqualTo(2));
            Assert.That(resources.RenderLists[0].Name, Is.EqualTo("RenderList"));
            Assert.That(resources.RenderLists[0].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(colorEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(colorEntry.IsDepthAttachment, Is.False);
            Assert.That(depthEntry.AttachmentIndex, Is.EqualTo(-1));
            Assert.That(depthEntry.IsDepthAttachment, Is.True);
        }

        [Test]
        public void Prepare_UpdatesInternalAttachmentDescriptors_WhenUsingDefaultTargets()
        {
            var pass = new DrawObjectPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 640;
            cameraData.actualHeight = 360;

            pass.Prepare(frameData);

            var colorTarget = GetTextureField(pass, "m_ColorTarget");
            var depthTarget = GetTextureField(pass, "m_DepthTarget");

            Assert.That(colorTarget.desc.Width, Is.EqualTo(640));
            Assert.That(colorTarget.desc.Height, Is.EqualTo(360));
            Assert.That(depthTarget.desc.Width, Is.EqualTo(640));
            Assert.That(depthTarget.desc.Height, Is.EqualTo(360));
        }

        [Test]
        public void Prepare_DoesNotOverwriteExternalAttachmentDescriptors_WhenTargetsAreBound()
        {
            var pass = new DrawObjectPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 640;
            cameraData.actualHeight = 360;

            var externalColor = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 128,
                    Height = 64,
                }
            };
            var externalDepth = new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 96,
                    Height = 48,
                }
            };

            SetTextureField(pass, "m_ColorTarget", externalColor);
            SetTextureField(pass, "m_DepthTarget", externalDepth);

            pass.Prepare(frameData);

            Assert.That(externalColor.desc.Width, Is.EqualTo(128));
            Assert.That(externalColor.desc.Height, Is.EqualTo(64));
            Assert.That(externalDepth.desc.Width, Is.EqualTo(96));
            Assert.That(externalDepth.desc.Height, Is.EqualTo(48));
        }

        private static RenderGraphTexture GetTextureField(DrawObjectPass pass, string fieldName)
        {
            var field = typeof(DrawObjectPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static void SetTextureField(DrawObjectPass pass, string fieldName, RenderGraphTexture value)
        {
            var field = typeof(DrawObjectPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            field.SetValue(pass, value);
        }
    }
}
