using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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
        private sealed class AutoRegisteredDeferredLightingPassNode : RenderPassNodeData
        {
            internal override System.Type GetRegisteredPassType() => typeof(DeferredLightingPass);
        }

        [Test]
        public void GTAO_IsActive_ReturnsEnabledStateAndPositiveRadius()
        {
            var gtao = ScriptableObject.CreateInstance<GTAO>();

            try
            {
                gtao.enabled.value = false;
                gtao.radius.value = 0.5f;
                Assert.That(gtao.IsActive(), Is.False);

                gtao.enabled.value = true;
                gtao.radius.value = 0.0f;
                Assert.That(gtao.IsActive(), Is.False);

                gtao.radius.value = 0.5f;
                Assert.That(gtao.IsActive(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gtao);
            }
        }

        [Test]
        public void GTAOSettingsResolver_Resolve_ReturnsConfiguredValues_WhenComponentActive()
        {
            var cameraObject = new GameObject("GTAOCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var gtao = profile.Add<GTAO>(true);

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
        public void Initialize_RegistersGTAOInputOutputAndHiddenTextures()
        {
            IRenderPass renderPass = new GTAOPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "Depth",
                "GBuffer1",
                "GTAOTexture",
                "GTAOWorkingAOTerm",
                "GTAOWorkingAOTermPong",
                "GTAOWorkingDepth",
                "GTAOWorkingEdges"
            }));
            Assert.That(textureEntries.Single(entry => entry.Name == "Depth").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "GBuffer1").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries.Single(entry => entry.Name == "GTAOTexture").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(
                textureEntries
                    .Where(entry => entry.Name.StartsWith("GTAOWorking", System.StringComparison.Ordinal))
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

            AssertTextureSize(pass, "m_GTAOTexture", 320, 180);
            AssertTextureSize(pass, "m_WorkingDepthTexture", 320, 180);
            AssertTextureSize(pass, "m_WorkingAOTermTexture", 320, 180);
            AssertTextureSize(pass, "m_WorkingAOTermPongTexture", 320, 180);
            AssertTextureSize(pass, "m_WorkingEdgesTexture", 320, 180);

            var workingDepth = GetFieldValue<RenderGraphTexture>(pass, "m_WorkingDepthTexture");
            var output = GetFieldValue<RenderGraphTexture>(pass, "m_GTAOTexture");

            Assert.That(workingDepth.desc.UseMipMap, Is.True);
            Assert.That(workingDepth.desc.MipCount, Is.EqualTo(5));
            Assert.That(workingDepth.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16_SFloat));
            Assert.That(output.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8_UNorm));
            Assert.That(output.desc.ClearColor, Is.EqualTo(Color.white));
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
        public void BuildRegistrations_IncludesGTAOPass()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(new[] { typeof(GTAOPass) });

            Assert.That(registrations.Select(registration => registration.NodeClassName), Does.Contain(nameof(GTAOPass)));
        }

        [Test]
        public void Compile_OrdersGTAOBeforeDeferredLighting_WhenGTAOTextureIsConnected()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var deferredNode = new AutoRegisteredDeferredLightingPassNode();
                var gtaoNode = new AutoRegisteredGTAOPassNode();
                var gbufferNode = new AutoRegisteredGBufferPassNode();
                var copyDepthNode = new AutoRegisteredCopyDepthPassNode();

                RenderGraphTestUtility.AddTestNode(graph, deferredNode);
                RenderGraphTestUtility.AddTestNode(graph, gtaoNode);
                RenderGraphTestUtility.AddTestNode(graph, gbufferNode);
                RenderGraphTestUtility.AddTestNode(graph, copyDepthNode);

                Assert.That(graph.Connect(
                    gbufferNode.GetOutputPortByName("m_GBuffer1"),
                    gtaoNode.GetInputPortByName("m_GBuffer1")), Is.True);
                Assert.That(graph.Connect(
                    copyDepthNode.GetOutputPortByName("m_DepthTexture"),
                    gtaoNode.GetInputPortByName("m_DepthTexture")), Is.True);
                Assert.That(graph.Connect(
                    gtaoNode.GetOutputPortByName("m_GTAOTexture"),
                    deferredNode.GetInputPortByName("m_GTAOTexture")), Is.True);

                var result = RenderGraphCompiler.Compile(graph);
                var executionOrder = result.ExecutionOrder.Select(pass => pass.PassTypeName).ToList();

                Assert.That(executionOrder.IndexOf(nameof(GTAOPass)), Is.GreaterThanOrEqualTo(0));
                Assert.That(executionOrder.IndexOf(nameof(DeferredLightingPass)), Is.GreaterThanOrEqualTo(0));
                Assert.That(executionOrder.IndexOf(nameof(CopyDepthPass)), Is.LessThan(executionOrder.IndexOf(nameof(GTAOPass))));
                Assert.That(executionOrder.IndexOf(nameof(GBufferPass)), Is.LessThan(executionOrder.IndexOf(nameof(GTAOPass))));
                Assert.That(executionOrder.IndexOf(nameof(GTAOPass)), Is.LessThan(executionOrder.IndexOf(nameof(DeferredLightingPass))));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void GTAOShaderSources_UseVividNormalDecodeAndUnormFinalResolve()
        {
            var computeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "GTAO", "GTAO.compute"));
            var hlsliSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "GTAO", "XeGTAO.hlsli"));

            Assert.That(computeSource, Does.Contain("#pragma kernel CSPrefilterDepths16x16"));
            Assert.That(computeSource, Does.Contain("#pragma kernel CSGTAOHigh"));
            Assert.That(computeSource, Does.Contain("#pragma kernel CSDenoiseLastPass"));
            Assert.That(computeSource, Does.Contain("DecodeVividNormalOct"));
            Assert.That(computeSource, Does.Contain("GetLeftHandedViewSpaceMatrices"));
            Assert.That(computeSource, Does.Contain("RWTexture2D<unorm float> _GTAOTexture;"));
            Assert.That(computeSource, Does.Contain("XeGTAO_MainPass("));
            Assert.That(computeSource, Does.Contain("XeGTAO_Denoise("));
            Assert.That(hlsliSource, Does.Contain("return LinearEyeDepth( screenDepth, _ZBufferParams );"));
            Assert.That(hlsliSource, Does.Contain("RWTexture2D<unorm float> outputTexture"));
            Assert.That(hlsliSource, Does.Not.Contain("VA_SATURATE"));
            Assert.That(hlsliSource, Does.Not.Contain("#include \"vaShared.hlsl\""));
        }

        [Test]
        public void ActiveRenderGraph_WiresGTAOIntoDeferredLighting()
        {
            var graphSource = File.ReadAllText(GetAssetFilePath("Assets", "Vivid Render Graph 1.vrdg"));

            Assert.That(graphSource, Does.Contain("type: {class: GTAOPass, ns: VividRP.Editor.RenderGraph.Generated, asm: VividRP.Editor}"));
            AssertWireExists(graphSource, "m_GTAOTexture", "GTAOTexture");
        }

        private static void AssertWireExists(string graphSource, string uniqueId, string title)
        {
            var pattern =
                $@"m_FromPortReference:\s*[\s\S]*?m_UniqueId: {System.Text.RegularExpressions.Regex.Escape(uniqueId)}\s*[\s\S]*?m_Title: {System.Text.RegularExpressions.Regex.Escape(title)} \(W\)\s*[\s\S]*?m_ToPortReference:\s*[\s\S]*?m_UniqueId: {System.Text.RegularExpressions.Regex.Escape(uniqueId)}\s*[\s\S]*?m_Title: {System.Text.RegularExpressions.Regex.Escape(title)} \(R\)";

            Assert.That(graphSource, Does.Match(pattern), $"Expected a GTAO -> DeferredLighting wire for '{uniqueId}'.");
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

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }

        private static string GetAssetFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, Path.Combine(relativeParts));
        }
    }
}
