using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureVisualizationPassTests
    {
        private sealed class TestProducer : VTProducer
        {
            public string Name => nameof(TestProducer);
        }

        [SetUp]
        public void SetUp()
        {
            VividRenderingDebugDisplaySettings.Data.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            VirtualTextureSystem.Deinitialize();
            VividRenderingDebugDisplaySettings.Data.Reset();
        }

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
        public void ResolveVisualizationMode_ReturnsNone_WhenDebuggerDataIsUnavailable()
        {
            var resolved = VirtualTextureVisualizationPass.ResolveVisualizationMode(null);

            Assert.That(resolved, Is.EqualTo(VirtualTextureVisualizationMode.None));
        }

        [Test]
        public void ResolveVisualizationMode_UsesRenderingDebuggerValue()
        {
            var resolved = VirtualTextureVisualizationPass.ResolveVisualizationMode(
                new VividRenderingDebugSettingsData
                {
                    virtualTextureVisualizationMode = VirtualTextureVisualizationMode.PageTableResidency,
                });

            Assert.That(resolved, Is.EqualTo(VirtualTextureVisualizationMode.PageTableResidency));
        }

        [Test]
        public void Prepare_UsesRenderingDebuggerForAllVisualizationSettings()
        {
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationMode =
                VirtualTextureVisualizationMode.PhysicalCache;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationTarget =
                VirtualTextureVisualizationTarget.FirstPublic;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationLayer =
                VirtualTextureVisualizationLayer.Mask;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationOverlayAmount = 0.5f;
            VividRenderingDebugDisplaySettings.Data.virtualTextureVisualizationOpacity = 0.25f;
            var pass = new VirtualTextureVisualizationPass();

            pass.Prepare(new ContextContainer());

            Assert.That(
                GetField<VirtualTextureVisualizationMode>(pass, "m_ResolvedVisualizationMode"),
                Is.EqualTo(VirtualTextureVisualizationMode.PhysicalCache));
            Assert.That(
                GetField<VirtualTextureVisualizationTarget>(pass, "m_ResolvedVisualizationTarget"),
                Is.EqualTo(VirtualTextureVisualizationTarget.FirstPublic));
            Assert.That(
                GetField<VirtualTextureVisualizationLayer>(pass, "m_ResolvedVisualizationLayer"),
                Is.EqualTo(VirtualTextureVisualizationLayer.Mask));
            Assert.That(GetField<float>(pass, "m_ResolvedOpacity"), Is.EqualTo(0.25f));
            Assert.That(
                GetVectorField(pass, "m_OverlayRect"),
                Is.EqualTo(new Vector4(0.325f, 0.325f, 0.675f, 0.675f)));
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
        public void ResolveVisualizationBinding_AutoPrefersGPUDrivenPrivateSpace()
        {
            VirtualTextureSpaceDesc privateDesc = CreateDesc(VirtualTextureGPUDrivenTextureBackend.SpaceName);
            VTProducerHandle producerHandle = VirtualTextureSystem.RegisterProducer(privateDesc, new TestProducer());
            VTAllocatedVirtualTexture privateAllocation = VirtualTextureSystem.AllocateVirtualTexture(
                new VTAllocationDesc(
                    privateDesc.SpaceName,
                    privateDesc,
                    producerHandle,
                    privateSpace: true));
            int publicSpaceId = VirtualTextureSystem.RegisterSpace(CreateDesc("Visualization.Public"));
            var frameData = new ContextContainer();
            var commandBuffer = new CommandBuffer();

            try
            {
                VirtualTextureSystem.Update(frameData, commandBuffer);
                VividVirtualTextureFrameData virtualTextureFrameData = frameData.Get<VividVirtualTextureFrameData>();

                Assert.That(
                    VirtualTextureVisualizationPass.TryResolveVisualizationBinding(
                        virtualTextureFrameData,
                        VirtualTextureVisualizationTarget.Auto,
                        privateAllocation.AllocationId,
                        out VirtualTextureSpaceBinding autoBinding),
                    Is.True);
                Assert.That(autoBinding.AllocationId, Is.EqualTo(privateAllocation.AllocationId));
                Assert.That(autoBinding.PrivateSpace, Is.True);

                Assert.That(
                    VirtualTextureVisualizationPass.TryResolveVisualizationBinding(
                        virtualTextureFrameData,
                        VirtualTextureVisualizationTarget.GPUDriven,
                        gpuDrivenAllocationId: 0,
                        out VirtualTextureSpaceBinding namedBinding),
                    Is.True);
                Assert.That(namedBinding.SpaceName, Is.EqualTo(VirtualTextureGPUDrivenTextureBackend.SpaceName));

                Assert.That(
                    VirtualTextureVisualizationPass.TryResolveVisualizationBinding(
                        virtualTextureFrameData,
                        VirtualTextureVisualizationTarget.FirstPublic,
                        privateAllocation.AllocationId,
                        out VirtualTextureSpaceBinding publicBinding),
                    Is.True);
                Assert.That(publicBinding.SpaceId, Is.EqualTo(publicSpaceId));
                Assert.That(publicBinding.PrivateSpace, Is.False);
            }
            finally
            {
                commandBuffer.Dispose();
            }
        }

        [Test]
        public void VisualizationShader_DeclaresPhysicalCacheAndPageTableViews()
        {
            string source = File.ReadAllText(GetShaderSourcePath());

            Assert.That(source, Does.Contain("Shader \"Hidden/VividRP/VirtualTextureVisualization\""));
            Assert.That(source, Does.Contain("#define VIVID_VT_VISUALIZATION_NONE 0"));
            Assert.That(source, Does.Not.Contain("VIVID_VT_VISUALIZATION_USE_PASS_SETTINGS"));
            Assert.That(source, Does.Contain("#define VIVID_VT_VISUALIZATION_PHYSICAL_CACHE 2"));
            Assert.That(source, Does.Contain("#define VIVID_VT_VISUALIZATION_PAGE_TABLE_RESIDENCY 3"));
            Assert.That(source, Does.Contain("#define VIVID_VT_VISUALIZATION_PAGE_TABLE_RESOLVED_MIP 5"));
            Assert.That(source, Does.Contain("#define VIVID_VT_VISUALIZATION_PAGE_TABLE_PHYSICAL_PAGE 6"));
            Assert.That(source, Does.Contain("EvaluatePhysicalCacheColor"));
            Assert.That(source, Does.Contain("EvaluatePageTableResidencyColor"));
            Assert.That(source, Does.Contain("EvaluateResolvedMipColor"));
            Assert.That(source, Does.Contain("EvaluatePhysicalPageColor"));
            Assert.That(source, Does.Contain("EvaluateUnavailableColor"));
            Assert.That(source, Does.Contain("_VTOverlayRect"));
            Assert.That(source, Does.Contain("_VTVisualizationLayer"));
            Assert.That(source, Does.Contain("_VTVisualizationAvailable"));
            Assert.That(source, Does.Contain("VTSamplePhysicalCacheGroup"));
            Assert.That(source, Does.Contain("_VTPageTable[flatIndex]"));
        }

        private static VirtualTextureSpaceDesc CreateDesc(string name)
        {
            return new VirtualTextureSpaceDesc(
                name,
                pageSize: 16,
                borderSize: 1,
                virtualPageCountX: 2,
                virtualPageCountY: 2,
                mipCount: 2,
                cachePageCount: 2,
                graphicsFormat: GraphicsFormat.R8G8B8A8_UNorm,
                maxUploadsPerFrame: 1,
                feedbackCapacity: 8);
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

        private static T GetField<T>(VirtualTextureVisualizationPass pass, string fieldName)
        {
            FieldInfo field = typeof(VirtualTextureVisualizationPass).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
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
