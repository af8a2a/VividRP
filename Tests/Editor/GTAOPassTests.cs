using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Scripting.APIUpdating;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class GTAOPassTests
    {
        [System.Serializable]
        private sealed class AutoRegisteredGTAOPassNode : RenderPassNodeData
        {
            internal override System.Type GetRegisteredPassType() => typeof(GTAOPass);
        }

        [System.Serializable]
        private sealed class AutoRegisteredGBufferPassNode : RenderPassNodeData
        {
            internal override System.Type GetRegisteredPassType() => typeof(GBufferPass);
        }

        [System.Serializable]
        private sealed class AutoRegisteredCopyDepthPassNode : RenderPassNodeData
        {
            internal override System.Type GetRegisteredPassType() => typeof(CopyDepthPass);
        }

        [System.Serializable]
        private sealed class AutoRegisteredHZBGeneratePassNode : RenderPassNodeData
        {
            internal override System.Type GetRegisteredPassType() => typeof(HZBGeneratePass);
        }

        [System.Serializable]
        private sealed class AutoRegisteredDeferredLightingPassNode : RenderPassNodeData
        {
            internal override System.Type GetRegisteredPassType() => typeof(DeferredLightingPass);
        }

        [Test]
        public void AmbientOcclusion_IsActive_ReturnsEnabledStateAndPositiveRadius()
        {
            var ambientOcclusion = ScriptableObject.CreateInstance<AmbientOcclusion>();

            try
            {
                ambientOcclusion.enabled.value = false;
                ambientOcclusion.radius.value = 0.5f;
                Assert.That(ambientOcclusion.IsActive(), Is.False);

                ambientOcclusion.enabled.value = true;
                ambientOcclusion.radius.value = 0.0f;
                Assert.That(ambientOcclusion.IsActive(), Is.False);

                ambientOcclusion.radius.value = 0.5f;
                Assert.That(ambientOcclusion.IsActive(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(ambientOcclusion);
            }
        }

        [Test]
        public void AmbientOcclusion_DeclaresMigrationFromGTAO()
        {
            var attributes = typeof(AmbientOcclusion).GetCustomAttributes(
                typeof(MovedFromAttribute),
                inherit: false);

            Assert.That(attributes, Has.Length.EqualTo(1));
        }

        [Test]
        public void GTAOSettingsResolver_Resolve_ReturnsConfiguredValues_WhenComponentActive()
        {
            var cameraObject = new GameObject("GTAOCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var gtao = profile.Add<AmbientOcclusion>(true);

            gtao.enabled.value = true;
            gtao.qualityLevel.value = 3;
            gtao.denoisePasses.value = 2;
            gtao.radius.value = 1.25f;
            gtao.falloffRange.value = 0.45f;
            gtao.finalValuePower.value = 3.1f;

            try
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var settings = GTAOSettingsResolver.Resolve();

                Assert.That(settings.enabled, Is.True);
                Assert.That(settings.implementation, Is.EqualTo(AmbientOcclusionImplementation.GTAO));
                Assert.That(settings.qualityLevel, Is.EqualTo(3));
                Assert.That(settings.denoisePasses, Is.EqualTo(2));
                Assert.That(settings.radius, Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(settings.falloffRange, Is.EqualTo(0.45f).Within(0.0001f));
                Assert.That(settings.finalValuePower, Is.EqualTo(3.1f).Within(0.0001f));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void GTAOSettingsResolver_Resolve_ReturnsCACAOSettings_WhenCACAOSelected()
        {
            var cameraObject = new GameObject("CACAOCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var gtao = profile.Add<AmbientOcclusion>(true);

            gtao.enabled.value = true;
            gtao.implementation.value = AmbientOcclusionImplementation.FidelityFXCACAO;
            gtao.qualityLevel.value = 4;
            gtao.radius.value = 1.2f;
            gtao.cacaoDownsampled.value = true;
            gtao.cacaoBlurPasses.value = 6;
            gtao.cacaoFadeOutFrom.value = 20.0f;
            gtao.cacaoFadeOutTo.value = 80.0f;
            gtao.cacaoAdaptiveQualityLimit.value = 0.7f;

            try
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var settings = GTAOSettingsResolver.Resolve();

                Assert.That(settings.enabled, Is.True);
                Assert.That(
                    settings.implementation,
                    Is.EqualTo(AmbientOcclusionImplementation.FidelityFXCACAO));
                Assert.That(settings.qualityLevel, Is.EqualTo(4));
                Assert.That(settings.cacaoDownsampled, Is.True);
                Assert.That(settings.cacaoBlurPasses, Is.EqualTo(6));
                Assert.That(settings.cacaoFadeOutFrom, Is.EqualTo(20.0f).Within(0.0001f));
                Assert.That(settings.cacaoFadeOutTo, Is.EqualTo(80.0f).Within(0.0001f));
                Assert.That(
                    settings.cacaoAdaptiveQualityLimit,
                    Is.EqualTo(0.7f).Within(0.0001f));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Initialize_RegistersGTAOInputOutputAndHiddenTextures()
        {
            IRenderPass renderPass = new GTAOPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "CACAODeinterleavedDepths",
                "CACAODeinterleavedNormals",
                "CACAOImportanceMap",
                "CACAOImportanceMapPong",
                "CACAOLoadCounter",
                "CACAOSSAOPing",
                "CACAOSSAOPong",
                "GBuffer1",
                "GTAOTexture",
                "GTAOWorkingAOTerm",
                "GTAOWorkingAOTermPong",
                "GTAOWorkingEdges",
                "HZB"
            }));
            Assert.That(textureEntries.Single(entry => entry.Name == "HZB").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "GBuffer1").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "GTAOTexture").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(
                textureEntries
                    .Where(entry => entry.Name.StartsWith("GTAOWorking", System.StringComparison.Ordinal))
                    .Select(entry => entry.Access)
                    .Distinct(),
                Is.EqualTo(new[] { AccessFlags.ReadWrite }));
            Assert.That(
                textureEntries
                    .Where(entry => entry.Name.StartsWith("CACAO", System.StringComparison.Ordinal))
                    .Select(entry => entry.Access)
                    .Distinct(),
                Is.EqualTo(new[] { AccessFlags.ReadWrite }));
        }

        [Test]
        public void Prepare_ResizesWorkingTexturesAndOutput_WhenCameraSizeChanges()
        {
            var pass = new GTAOPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 320;
            cameraData.actualHeight = 180;

            pass.Prepare(frameData);

            AssertTextureSize(pass, "m_HzbTexture", 320, 180);
            AssertTextureSize(pass, "m_GTAOTexture", 320, 180);
            AssertTextureSize(pass, "m_WorkingAOTermTexture", 320, 180);
            AssertTextureSize(pass, "m_WorkingAOTermPongTexture", 320, 180);
            AssertTextureSize(pass, "m_WorkingEdgesTexture", 320, 180);

            var hzb = GetFieldValue<RenderGraphTexture>(pass, "m_HzbTexture");
            var output = GetFieldValue<RenderGraphTexture>(pass, "m_GTAOTexture");

            Assert.That(hzb.desc.UseMipMap, Is.True);
            Assert.That(hzb.desc.MipCount, Is.EqualTo(6));
            Assert.That(hzb.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16_SFloat));
            Assert.That(output.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8_UNorm));
            Assert.That(output.desc.ClearColor, Is.EqualTo(Color.white));
        }

        [Test]
        public void Prepare_ResizesCACAOTextures_ForDownsampledCACAO()
        {
            var cameraObject = new GameObject("CACAOPrepareCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var gtao = profile.Add<AmbientOcclusion>(true);
            gtao.enabled.value = true;
            gtao.implementation.value = AmbientOcclusionImplementation.FidelityFXCACAO;
            gtao.qualityLevel.value = 4;
            gtao.cacaoDownsampled.value = true;

            try
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var pass = new GTAOPass();
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.camera = camera;
                cameraData.actualWidth = 321;
                cameraData.actualHeight = 181;

                pass.Prepare(frameData);

                AssertTextureSize(pass, "m_GTAOTexture", 321, 181);
                AssertTextureSize(pass, "m_CacaoDeinterleavedDepths", 81, 46);
                AssertTextureSize(pass, "m_CacaoDeinterleavedNormals", 81, 46);
                AssertTextureSize(pass, "m_CacaoSsaoPing", 81, 46);
                AssertTextureSize(pass, "m_CacaoImportanceMap", 41, 23);
                AssertTextureSize(pass, "m_WorkingAOTermTexture", 1, 1);

                var depths = GetFieldValue<RenderGraphTexture>(
                    pass,
                    "m_CacaoDeinterleavedDepths");
                Assert.That(depths.desc.Dimension, Is.EqualTo(TextureDimension.Tex2DArray));
                Assert.That(depths.desc.Slices, Is.EqualTo(4));
                Assert.That(depths.desc.UseMipMap, Is.True);
                Assert.That(depths.desc.MipCount, Is.EqualTo(4));
                Assert.That(depths.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16_SFloat));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Prepare_DoesNotAllocate_WhenCameraDataIsStable()
        {
            var pass = new GTAOPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 320;
            cameraData.actualHeight = 180;

            pass.Prepare(frameData);

            var allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 32; index++)
                pass.Prepare(frameData);

            var allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.That(allocatedBytes, Is.Zero);
        }

        [Test]
        public void PrepareCACAO_DoesNotAllocate_WhenCameraDataIsStable()
        {
            var cameraObject = new GameObject("CACAOAllocationCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var gtao = profile.Add<AmbientOcclusion>(true);
            gtao.enabled.value = true;
            gtao.implementation.value = AmbientOcclusionImplementation.FidelityFXCACAO;

            try
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var pass = new GTAOPass();
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.camera = camera;
                cameraData.actualWidth = 320;
                cameraData.actualHeight = 180;

                pass.Prepare(frameData);

                var allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
                for (var index = 0; index < 32; index++)
                    pass.Prepare(frameData);

                var allocatedBytes =
                    global::System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Assert.That(allocatedBytes, Is.Zero);
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void BuildRegistrations_IncludesGTAOPass()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(new[] { typeof(GTAOPass) });

            Assert.That(registrations.Select(registration => registration.NodeClassName), Does.Contain(nameof(GTAOPass)));
        }

        [Test]
        public void Compile_OrdersHZBBeforeGTAO_WhenSharedHZBIsConnected()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var deferredNode = new AutoRegisteredDeferredLightingPassNode();
                var gtaoNode = new AutoRegisteredGTAOPassNode();
                var gbufferNode = new AutoRegisteredGBufferPassNode();
                var copyDepthNode = new AutoRegisteredCopyDepthPassNode();
                var hzbNode = new AutoRegisteredHZBGeneratePassNode();

                RenderGraphTestUtility.AddTestNode(graph, deferredNode);
                RenderGraphTestUtility.AddTestNode(graph, gtaoNode);
                RenderGraphTestUtility.AddTestNode(graph, gbufferNode);
                RenderGraphTestUtility.AddTestNode(graph, copyDepthNode);
                RenderGraphTestUtility.AddTestNode(graph, hzbNode);

                Assert.That(graph.Connect(
                    gbufferNode.GetOutputPortByName("m_GBuffer1"),
                    gtaoNode.GetInputPortByName("m_GBuffer1")), Is.True);
                Assert.That(graph.Connect(
                    copyDepthNode.GetOutputPortByName("m_DepthTexture"),
                    hzbNode.GetInputPortByName("m_DepthTexture")), Is.True);
                Assert.That(graph.Connect(
                    hzbNode.GetOutputPortByName("m_HzbTexture"),
                    gtaoNode.GetInputPortByName("m_HzbTexture")), Is.True);
                Assert.That(graph.Connect(
                    gtaoNode.GetOutputPortByName("m_GTAOTexture"),
                    deferredNode.GetInputPortByName("m_GTAOTexture")), Is.True);

                var result = RenderGraphCompiler.Compile(graph);
                var executionOrder = result.ExecutionOrder.Select(pass => pass.PassTypeName).ToList();

                Assert.That(executionOrder.IndexOf(nameof(GTAOPass)), Is.GreaterThanOrEqualTo(0));
                Assert.That(executionOrder.IndexOf(nameof(HZBGeneratePass)), Is.GreaterThanOrEqualTo(0));
                Assert.That(executionOrder.IndexOf(nameof(DeferredLightingPass)), Is.GreaterThanOrEqualTo(0));
                Assert.That(executionOrder.IndexOf(nameof(CopyDepthPass)), Is.LessThan(executionOrder.IndexOf(nameof(HZBGeneratePass))));
                Assert.That(executionOrder.IndexOf(nameof(HZBGeneratePass)), Is.LessThan(executionOrder.IndexOf(nameof(GTAOPass))));
                Assert.That(executionOrder.IndexOf(nameof(GBufferPass)), Is.LessThan(executionOrder.IndexOf(nameof(GTAOPass))));
                Assert.That(executionOrder.IndexOf(nameof(GTAOPass)), Is.LessThan(executionOrder.IndexOf(nameof(DeferredLightingPass))));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void CACAOConstantBuffer_MatchesOfficial384ByteLayout()
        {
            var constantBufferType = typeof(GTAOPass).GetNestedType(
                "CacaoConstantBufferData",
                BindingFlags.NonPublic);

            Assert.That(constantBufferType, Is.Not.Null);
            Assert.That(Marshal.SizeOf(constantBufferType), Is.EqualTo(384));
        }

        private static void AssertTextureSize(GTAOPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var texture = GetFieldValue<RenderGraphTexture>(pass, fieldName);

            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
        }

        private static T GetFieldValue<T>(GTAOPass pass, string fieldName)
        {
            var field = typeof(GTAOPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}' on {nameof(GTAOPass)}.");
            return (T)field.GetValue(pass);
        }
    }
}
