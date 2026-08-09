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
    public class TileDebugPassTests
    {
        [Test]
        public void Initialize_RegistersSourceTextureTileBuffersAndColorOutput()
        {
            IRenderPass renderPass = new TileDebugPass();

            var resources = renderPass.Initialize();
            var sourceEntry = resources.Textures.Single(entry => entry.Name == "SourceTexture");
            var outputEntry = resources.Textures.Single(entry => entry.Name == "OutputTexture");
            var tileIndicesEntry = resources.Buffers.Single(entry => entry.Name == "TileIndices");
            var indirectArgsEntry = resources.Buffers.Single(entry => entry.Name == "IndirectArgs");

            Assert.That(resources.Textures, Has.Length.EqualTo(2));
            Assert.That(resources.Buffers, Has.Length.EqualTo(2));
            Assert.That(sourceEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(outputEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(outputEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(tileIndicesEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(indirectArgsEntry.Access, Is.EqualTo(AccessFlags.Read));
        }

        [Test]
        public void Prepare_UsesSourceTextureSizeAndFormat_WhenConfigured()
        {
            var pass = new TileDebugPass();
            var sourceTexture = GetTextureField(pass, "m_SourceTexture");
            var outputTexture = GetTextureField(pass, "m_OutputTexture");

            sourceTexture.desc.Width = 1280;
            sourceTexture.desc.Height = 720;
            sourceTexture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(1280));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(720));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(GetVectorField(pass, "m_TileDebugScreenSize"), Is.EqualTo(new Vector4(1280f, 720f, 1f / 1280f, 1f / 720f)));
        }

        [Test]
        public void Prepare_FallsBackToCameraSize_WhenSourceTextureUsesPlaceholderDescriptor()
        {
            var pass = new TileDebugPass();
            var outputTexture = GetTextureField(pass, "m_OutputTexture");

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 960;
            cameraData.actualHeight = 540;

            pass.Prepare(frameData);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(960));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(540));
            Assert.That(GetVectorField(pass, "m_TileDebugScreenSize"), Is.EqualTo(new Vector4(960f, 540f, 1f / 960f, 1f / 540f)));
        }

        private static RenderGraphTexture GetTextureField(TileDebugPass pass, string fieldName)
        {
            var field = typeof(TileDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            return texture;
        }

        private static Vector4 GetVectorField(TileDebugPass pass, string fieldName)
        {
            var field = typeof(TileDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (Vector4)field.GetValue(pass);
        }
    }
}
