using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class AntialiasingPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredAntialiasingPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(AntialiasingPass);
        }

        [Test]
        public void Initialize_RegistersStableTextureResources()
        {
            IRenderPass renderPass = new AntialiasingPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "AntialiasingOutput",
                "AntialiasingTAAHistoryColor",
                "AntialiasingTAAHistoryColorCurrent",
                "CameraDepth",
                "Color",
                "MotionVectors",
            }));
        }

        [Test]
        public void Initialize_UsesStableReadAndWriteAccess()
        {
            IRenderPass renderPass = new AntialiasingPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.First(entry => entry.Name == "Color").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.First(entry => entry.Name == "MotionVectors").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.First(entry => entry.Name == "CameraDepth").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.First(entry => entry.Name == "AntialiasingOutput").Access, Is.EqualTo(AccessFlags.Write));
        }

        [Test]
        public void AntialiasingPassNode_DefinesExplicitAAPorts()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredAntialiasingPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("Color"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("MotionVectors"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("CameraDepth"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("AntialiasingOutput"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void AntialiasingPassNode_HidesHistoryPorts()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredAntialiasingPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_TaaHistoryColorPrevious"), Is.Null);
                Assert.That(node.GetInputPortByName("m_TaaHistoryColorCurrent_In"), Is.Null);
                Assert.That(node.GetOutputPortByName("m_TaaHistoryColorCurrent_Out"), Is.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Prepare_ConfiguresNoneOutputDimensionsFromRenderSize()
        {
            var pass = new AntialiasingPass();
            var frameData = CreateFrameData(640, 360, VividAntialiasingMode.None, new Vector2Int(640, 360), new Vector2Int(640, 360));

            pass.Prepare(frameData);

            var outputTexture = GetTextureField(pass, "AntialiasingOutput");
            Assert.That(outputTexture.desc.Width, Is.EqualTo(640));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(360));
            Assert.That(outputTexture.desc.EnableRandomWrite, Is.False);
        }

        [Test]
        public void Prepare_ConfiguresTaaOutputDimensionsFromRenderSize()
        {
            var pass = new AntialiasingPass();
            var frameData = CreateFrameData(
                1024,
                512,
                VividAntialiasingMode.TemporalAntiAliasing,
                new Vector2Int(1024, 512),
                new Vector2Int(1024, 512));

            pass.Prepare(frameData);

            var outputTexture = GetTextureField(pass, "AntialiasingOutput");
            Assert.That(outputTexture.desc.Width, Is.EqualTo(1024));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(512));
        }

        [Test]
        public void Prepare_ConfiguresFsr3OutputDimensionsFromOutputSize()
        {
            var pass = new AntialiasingPass();
            var frameData = CreateFrameData(
                1280,
                720,
                VividAntialiasingMode.FidelityFXSuperResolution3,
                new Vector2Int(1280, 720),
                new Vector2Int(1920, 1080));

            pass.Prepare(frameData);

            var outputTexture = GetTextureField(pass, "AntialiasingOutput");
            Assert.That(outputTexture.desc.Width, Is.EqualTo(1920));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(1080));
        }

        [Test]
        public void Prepare_ConfiguresTsrOutputDimensionsFromOutputSize()
        {
            var pass = new AntialiasingPass();
            var frameData = CreateFrameData(
                1280,
                720,
                VividAntialiasingMode.TemporalSuperResolution,
                new Vector2Int(1129, 636),
                new Vector2Int(1920, 1080));

            pass.Prepare(frameData);

            var outputTexture = GetTextureField(pass, "AntialiasingOutput");
            Assert.That(outputTexture.desc.Width, Is.EqualTo(1920));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(1080));
            Assert.That(outputTexture.desc.EnableRandomWrite, Is.True);
        }

        [Test]
        public void Prepare_ReusesOutputDescriptorInstance()
        {
            var pass = new AntialiasingPass();
            var frameData = CreateFrameData(
                1024,
                512,
                VividAntialiasingMode.TemporalAntiAliasing,
                new Vector2Int(1024, 512),
                new Vector2Int(1024, 512));

            pass.Prepare(frameData);

            var outputTexture = GetTextureField(pass, "AntialiasingOutput");
            var descriptor = outputTexture.desc;

            frameData = CreateFrameData(
                1280,
                720,
                VividAntialiasingMode.TemporalSuperResolution,
                new Vector2Int(1129, 636),
                new Vector2Int(1920, 1080));

            pass.Prepare(frameData);

            Assert.That(outputTexture.desc, Is.SameAs(descriptor));
            Assert.That(outputTexture.desc.Width, Is.EqualTo(1920));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(1080));
        }

        [Test]
        public void Cmaa2PassResources_ReusesCachedResources_WhenLayoutIsClean()
        {
            var pass = new AntialiasingPass();
            SetField(pass, "m_Cmaa2Pass", new CMAA2Pass());

            try
            {
                var resources = pass.GetCmaa2PassResources();
                Assert.That(resources, Is.Not.Null);

                GC.Collect();
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                for (var index = 0; index < 32; index++)
                {
                    var currentResources = pass.GetCmaa2PassResources();
                    if (!ReferenceEquals(currentResources, resources))
                        Assert.Fail("CMAA2 pass resources should be reused while the layout is clean.");
                }

                var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Assert.That(allocatedBytes, Is.Zero);
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void Resolver_DisablesTemporalWork_WhenGraphHasNoAntialiasingPass()
        {
            var cameraObject = new GameObject("AA Resolver Test Camera");

            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var additionalData = cameraObject.AddComponent<VividAdditionalCameraData>();
                additionalData.antialiasing = VividAntialiasingMode.FidelityFXSuperResolution3;
                additionalData.fsr3Quality = VividFsr3QualityMode.Performance;

                var data = new VividAntialiasingData();
                InvokeResolverClear();
                InvokeResolver(camera, additionalData, hasAntialiasingPass: false, data);

                Assert.That(data.requestedMode, Is.EqualTo(VividAntialiasingMode.FidelityFXSuperResolution3));
                Assert.That(data.effectiveMode, Is.EqualTo(VividAntialiasingMode.None));
                Assert.That(data.renderSize, Is.EqualTo(data.outputSize));
                Assert.That(data.usesTemporalJitter, Is.False);
                Assert.That(data.resetHistory, Is.False);
            }
            finally
            {
                InvokeResolverClear();
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Resolver_ResetsHistory_WhenEffectiveModeChanges()
        {
            var cameraObject = new GameObject("AA Resolver Switch Camera");

            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var additionalData = cameraObject.AddComponent<VividAdditionalCameraData>();
                var data = new VividAntialiasingData();

                InvokeResolverClear();

                additionalData.antialiasing = VividAntialiasingMode.TemporalAntiAliasing;
                InvokeResolver(camera, additionalData, hasAntialiasingPass: true, data);
                Assert.That(data.effectiveMode, Is.EqualTo(VividAntialiasingMode.TemporalAntiAliasing));
                Assert.That(data.resetHistory, Is.True);

                InvokeResolver(camera, additionalData, hasAntialiasingPass: true, data);
                Assert.That(data.resetHistory, Is.False);

                additionalData.antialiasing = VividAntialiasingMode.None;
                InvokeResolver(camera, additionalData, hasAntialiasingPass: true, data);
                Assert.That(data.effectiveMode, Is.EqualTo(VividAntialiasingMode.None));
                Assert.That(data.resetHistory, Is.False);

                additionalData.antialiasing = VividAntialiasingMode.TemporalAntiAliasing;
                InvokeResolver(camera, additionalData, hasAntialiasingPass: true, data);
                Assert.That(data.resetHistory, Is.True);
            }
            finally
            {
                InvokeResolverClear();
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Resolver_DoesNotAllocate_ForCachedTemporalSuperResolutionPath()
        {
            var cameraObject = new GameObject("AA Resolver Allocation Camera");

            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var additionalData = cameraObject.AddComponent<VividAdditionalCameraData>();
                additionalData.antialiasing = VividAntialiasingMode.TemporalSuperResolution;
                additionalData.tsrQuality = VividTsrQualityMode.Balanced;

                var data = new VividAntialiasingData();
                VividAntialiasingRuntimeUtility.Clear();
                VividAntialiasingRuntimeUtility.Resolve(camera, additionalData, hasAntialiasingPass: true, data);

                GC.Collect();
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                for (var index = 0; index < 32; index++)
                    VividAntialiasingRuntimeUtility.Resolve(camera, additionalData, hasAntialiasingPass: true, data);

                var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Assert.That(allocatedBytes, Is.Zero);
            }
            finally
            {
                VividAntialiasingRuntimeUtility.Clear();
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void HasAntialiasingPass_DoesNotAllocate_WhenListIsReadThroughInterface()
        {
            var pass = new AntialiasingPass();
            IReadOnlyList<IRenderPass> renderPasses = new List<IRenderPass> { pass };

            try
            {
                Assert.That(PassRecorder.HasAntialiasingPass(renderPasses), Is.True);

                GC.Collect();
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                for (var index = 0; index < 64; index++)
                {
                    if (!PassRecorder.HasAntialiasingPass(renderPasses))
                        Assert.Fail("Antialiasing pass should be detected without allocating.");
                }

                var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Assert.That(allocatedBytes, Is.Zero);
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void SourceFiles_RemoveLegacyAAPassRecorderInjection()
        {
            var passRecorderSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderGraph",
                "PassRecorder.Execution.cs"));
            var renderGraphPassSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderGraph",
                "RenderGraphPass.cs"));

            Assert.That(passRecorderSource, Does.Not.Contain("ShouldInjectCmaa2Pass"));
            Assert.That(passRecorderSource, Does.Not.Contain("ShouldInjectStpPass"));
            Assert.That(passRecorderSource, Does.Not.Contain("ShouldInjectFsr3Pass"));
            Assert.That(passRecorderSource, Does.Not.Contain("ShouldInjectDlssPass"));
            Assert.That(passRecorderSource, Does.Not.Contain("RecordInjectedCmaa2Pass"));
            Assert.That(passRecorderSource, Does.Not.Contain("RecordInjectedStpPass"));
            Assert.That(passRecorderSource, Does.Not.Contain("RecordInjectedFsr3Pass"));
            Assert.That(passRecorderSource, Does.Not.Contain("RecordInjectedDlssPass"));
            Assert.That(passRecorderSource, Does.Contain("pass is IRenderGraphRecordingPass graphRecordingPass"));
            Assert.That(renderGraphPassSource, Does.Contain("public interface IRenderGraphRecordingPass"));
        }

        [Test]
        public void SourceFiles_RegisterPassthrough_WhenEffectiveModeIsNone()
        {
            var antialiasingPassSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "AntialiasingPass.cs"));

            Assert.That(antialiasingPassSource, Does.Contain("if (m_EffectiveMode == VividAntialiasingMode.None)"));
            Assert.That(antialiasingPassSource, Does.Contain("TryRegisterPassthrough(context)"));
            Assert.That(antialiasingPassSource, Does.Contain("context.RegisterTextureHandle(AntialiasingOutput, sourceHandle)"));
        }

        [Test]
        public void SourceFiles_RecordTsrThroughAntialiasingPassWithTemporalInputs()
        {
            var antialiasingPassSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "AntialiasingPass.cs"));

            Assert.That(antialiasingPassSource, Does.Contain("TryRecordTsrPass"));
            Assert.That(antialiasingPassSource, Does.Contain("m_TsrPass.Record"));
            Assert.That(antialiasingPassSource, Does.Contain("VividAntialiasingMode.TemporalSuperResolution"));
            Assert.That(antialiasingPassSource, Does.Contain("if (!HasTemporalInputs())"));
            Assert.That(antialiasingPassSource, Does.Contain("AntialiasingOutput"));
        }

        private static ContextContainer CreateFrameData(
            int width,
            int height,
            VividAntialiasingMode effectiveMode,
            Vector2Int renderSize,
            Vector2Int outputSize)
        {
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = width;
            cameraData.actualHeight = height;
            cameraData.pixelWidth = width;
            cameraData.pixelHeight = height;
            var antialiasingData = frameData.GetOrCreate<VividAntialiasingData>();
            antialiasingData.hasAntialiasingPass = true;
            antialiasingData.requestedMode = effectiveMode;
            antialiasingData.effectiveMode = effectiveMode;
            antialiasingData.renderSize = renderSize;
            antialiasingData.outputSize = outputSize;
            antialiasingData.usesTemporalJitter = effectiveMode != VividAntialiasingMode.None;
            frameData.GetOrCreate<VividTemporalData>();
            return frameData;
        }

        private static RenderGraphTexture GetTextureField(AntialiasingPass pass, string fieldName)
        {
            var field = typeof(AntialiasingPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static void SetField<T>(AntialiasingPass pass, string fieldName, T value)
        {
            var field = typeof(AntialiasingPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(pass, value);
        }

        private static void InvokeResolver(
            Camera camera,
            VividAdditionalCameraData additionalData,
            bool hasAntialiasingPass,
            VividAntialiasingData data)
        {
            var utilityType = typeof(VividAntialiasingData).Assembly.GetType("VividRP.Runtime.VividAntialiasingRuntimeUtility");
            Assert.That(utilityType, Is.Not.Null);
            var resolveMethod = utilityType.GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(resolveMethod, Is.Not.Null);
            resolveMethod.Invoke(null, new object[] { camera, additionalData, hasAntialiasingPass, data });
        }

        private static void InvokeResolverClear()
        {
            var utilityType = typeof(VividAntialiasingData).Assembly.GetType("VividRP.Runtime.VividAntialiasingRuntimeUtility");
            Assert.That(utilityType, Is.Not.Null);
            var clearMethod = utilityType.GetMethod("Clear", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(clearMethod, Is.Not.Null);
            clearMethod.Invoke(null, Array.Empty<object>());
        }

        private static string GetPackageFilePath(params string[] parts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(parts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(parts));
        }
    }
}
