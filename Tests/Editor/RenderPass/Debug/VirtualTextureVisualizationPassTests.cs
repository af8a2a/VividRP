using System.IO;
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
    public sealed class VirtualTextureVisualizationPassTests
    {
        [Test]
        public void Initialize_RegistersSourceAndOutputTextures()
        {
            IRenderPass renderPass = new VirtualTextureVisualizationPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures, Has.Length.EqualTo(2));
            Assert.That(resources.Textures[0].Name, Is.EqualTo("SourceTexture"));
            Assert.That(resources.Textures[0].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures[1].Name, Is.EqualTo("OutputTexture"));
            Assert.That(resources.Textures[1].Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(resources.Textures[1].AttachmentIndex, Is.EqualTo(0));
        }

        [Test]
        public void Prepare_UsesSourceTextureDescriptor_WhenConfigured()
        {
            var pass = new VirtualTextureVisualizationPass();
            var sourceTexture = GetTextureField(pass, "m_SourceTexture");
            var outputTexture = GetTextureField(pass, "m_OutputTexture");

            sourceTexture.desc.Width = 960;
            sourceTexture.desc.Height = 540;
            sourceTexture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(960));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(540));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(GetVectorField(pass, "m_OverlayRect"), Is.EqualTo(new Vector4(0.65f, 0.65f, 0.35f, 0.35f)));
        }

        [Test]
        public void ResolveVisualizationMode_UsesPassDefault_WhenDebuggerUsesPassSettings()
        {
            var resolved = VirtualTextureVisualizationPass.ResolveVisualizationMode(
                new VividRenderingDebugSettingsData
                {
                    virtualTextureVisualizationMode = VirtualTextureVisualizationMode.UsePassSettings,
                },
                VirtualTextureVisualizationMode.PhysicalCache);

            Assert.That(resolved, Is.EqualTo(VirtualTextureVisualizationMode.PhysicalCache));
        }

        [Test]
        public void ResolveVisualizationMode_UsesDebuggerOverride_WhenPresent()
        {
            var resolved = VirtualTextureVisualizationPass.ResolveVisualizationMode(
                new VividRenderingDebugSettingsData
                {
                    virtualTextureVisualizationMode = VirtualTextureVisualizationMode.PageTableResidency,
                },
                VirtualTextureVisualizationMode.PhysicalCache);

            Assert.That(resolved, Is.EqualTo(VirtualTextureVisualizationMode.PageTableResidency));
        }

        [Test]
        public void ResolveOverlayRect_GrowsOverlayTowardFullscreen()
        {
            Assert.That(
                VirtualTextureVisualizationPass.ResolveOverlayRect(0f),
                Is.EqualTo(new Vector4(0.65f, 0.65f, 0.35f, 0.35f)));
            Assert.That(
                VirtualTextureVisualizationPass.ResolveOverlayRect(1f),
                Is.EqualTo(new Vector4(0f, 0f, 1f, 1f)));
        }

        [Test]
        public void VisualizationShader_DeclaresPhysicalCacheAndPageTableViews()
        {
            string source = File.ReadAllText(GetShaderSourcePath());

            Assert.That(source, Does.Contain("Shader \"Hidden/VividRP/VirtualTextureVisualization\""));
            Assert.That(source, Does.Contain("#define VIVID_VT_VISUALIZATION_PHYSICAL_CACHE 2"));
            Assert.That(source, Does.Contain("#define VIVID_VT_VISUALIZATION_PAGE_TABLE_RESIDENCY 3"));
            Assert.That(source, Does.Contain("EvaluatePhysicalCacheColor"));
            Assert.That(source, Does.Contain("EvaluatePageTableResidencyColor"));
            Assert.That(source, Does.Contain("_VTOverlayRect"));
            Assert.That(source, Does.Contain("_VTVisualizationAvailable"));
            Assert.That(source, Does.Contain("SAMPLE_TEXTURE2D_ARRAY(_VTPhysicalCache"));
            Assert.That(source, Does.Contain("_VTPageTable[flatIndex]"));
        }

        private static RenderGraphTexture GetTextureField(VirtualTextureVisualizationPass pass, string fieldName)
        {
            FieldInfo field = typeof(VirtualTextureVisualizationPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            return texture;
        }

        private static Vector4 GetVectorField(VirtualTextureVisualizationPass pass, string fieldName)
        {
            FieldInfo field = typeof(VirtualTextureVisualizationPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (Vector4)field.GetValue(pass);
        }

        private static string GetShaderSourcePath()
        {
            string customPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "Custom_URP",
                "Shaders",
                "Core",
                "Private",
                "Debug",
                "VirtualTextureVisualization.shader"));
            if (File.Exists(customPath))
                return customPath;

            string vividPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "VividRP",
                "Shaders",
                "Core",
                "Private",
                "Debug",
                "VirtualTextureVisualization.shader"));
            if (File.Exists(vividPath))
                return vividPath;

            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                "Shaders",
                "Core",
                "Private",
                "Debug",
                "VirtualTextureVisualization.shader"));
        }
    }
}
