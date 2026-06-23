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
    public class DrawObjectPassTests
    {
        private sealed class DerivedDrawObjectPass : DrawObjectPass
        {
        }

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
        public void Initialize_IncludesInheritedPrivateResources_WhenPassDerivesFromDrawObjectPass()
        {
            IRenderPass renderPass = new DerivedDrawObjectPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.RenderLists, Has.Length.EqualTo(1));
            Assert.That(resources.Textures, Has.Length.EqualTo(2));
            Assert.That(resources.Textures.Any(entry => entry.Name == "Color"), Is.True);
            Assert.That(resources.Textures.Any(entry => entry.Name == "Depth"), Is.True);
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

        [Test]
        public void Constructor_IncludesObjectMotionVectorRenderers_InDefaultRenderList()
        {
            var pass = new DrawObjectPass();
            var renderList = GetRenderListField(pass);

            Assert.That(renderList.desc.ExcludeObjectMotionVectors, Is.False);
        }

        [Test]
        public void Initialize_RegistersSequentialColorAttachments_WhenAdditionalTargetsAreAdded()
        {
            IRenderPass renderPass = new DrawObjectPass();
            var pass = (DrawObjectPass)renderPass;

            pass.AddColorTarget(new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R16G16_SFloat)
            });
            pass.AddColorTarget(new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.B10G11R11_UFloatPack32)
            });

            var resources = renderPass.Initialize();
            var colorEntries = resources.Textures
                .Where(entry => !entry.IsDepthAttachment)
                .OrderBy(entry => entry.AttachmentIndex)
                .ToArray();

            Assert.That(colorEntries, Has.Length.EqualTo(3));
            Assert.That(colorEntries[0].Name, Is.EqualTo("Color"));
            Assert.That(colorEntries[0].AttachmentIndex, Is.EqualTo(0));
            Assert.That(colorEntries[1].Name, Is.EqualTo("Color1"));
            Assert.That(colorEntries[1].AttachmentIndex, Is.EqualTo(1));
            Assert.That(colorEntries[2].Name, Is.EqualTo("Color2"));
            Assert.That(colorEntries[2].AttachmentIndex, Is.EqualTo(2));
        }

        [Test]
        public void SetColorTargets_MarksPassResourceLayoutDirty_WhenAttachmentsChange()
        {
            var pass = new DrawObjectPass();

            Assert.That(pass.IsPassResourceLayoutDirty, Is.False);

            pass.AddColorTarget(new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R16G16_SFloat)
            });

            Assert.That(pass.IsPassResourceLayoutDirty, Is.True);

            pass.ClearPassResourceLayoutDirty();
            pass.SetColorTargets(new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R8G8B8A8_UNorm)
            });

            Assert.That(pass.IsPassResourceLayoutDirty, Is.True);
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

        private static RenderGraphRenderList GetRenderListField(DrawObjectPass pass)
        {
            var field = typeof(DrawObjectPass).GetField("m_RenderList", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphRenderList)field.GetValue(pass);
        }
    }
}
