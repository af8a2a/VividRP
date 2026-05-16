using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using UnityRenderGraph = UnityEngine.Rendering.RenderGraphModule.RenderGraph;

namespace VividRP.Editor.Tests
{
    public class DepthOfFieldPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredDepthOfFieldPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(DepthOfFieldPass);

            internal bool HasOverrideOption(string fieldName)
            {
                return GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName(fieldName)) != null;
            }
        }

        [Test]
        public void Initialize_RegistersPhysicalDepthOfFieldInputsHiddenResources_AndOutput()
        {
            IRenderPass renderPass = new DepthOfFieldPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures, Has.Length.EqualTo(11));
            Assert.That(resources.Buffers, Is.Empty);
            Assert.That(Array.Exists(resources.Textures, texture => texture.Name == "source" && texture.Access == AccessFlags.Read), Is.True);
            Assert.That(Array.Exists(resources.Textures, texture => texture.Name == "LinearDepth" && texture.Access == AccessFlags.Read), Is.True);
            Assert.That(Array.Exists(resources.Textures, texture => texture.Name == "MotionVectors" && texture.Access == AccessFlags.Read), Is.True);
            Assert.That(Array.Exists(resources.Textures, texture => texture.Name == "DepthOfFieldCoC" && texture.Access == AccessFlags.ReadWrite), Is.True);
            Assert.That(Array.Exists(resources.Textures, texture => texture.Name == "DepthOfFieldTileMinMaxPing" && texture.Access == AccessFlags.ReadWrite), Is.True);
            Assert.That(Array.Exists(resources.Textures, texture => texture.Name == "DepthOfFieldOutput" && texture.Access == AccessFlags.Write), Is.True);
            Assert.That(resources.BypassRules, Has.Length.EqualTo(1));
            Assert.That(resources.BypassRules[0].SourceFieldName, Is.EqualTo("source"));
            Assert.That(resources.BypassRules[0].OutputFieldName, Is.EqualTo("output"));
            Assert.That(resources.BypassRules[0].ResourceType, Is.EqualTo(PassResourceType.Texture));
        }

        [Test]
        public void Prepare_ConfiguresOutputDescriptor_FromSourceTexture()
        {
            var pass = new DepthOfFieldPass();
            var sourceField = typeof(DepthOfFieldPass).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic);
            var outputField = typeof(DepthOfFieldPass).GetField("output", BindingFlags.Instance | BindingFlags.NonPublic);
            var sourceTexture = RenderGraphTexture.CreateInput("InjectedSource", GraphicsFormat.B10G11R11_UFloatPack32);
            sourceTexture.desc.Width = 320;
            sourceTexture.desc.Height = 180;
            sourceTexture.desc.UseDynamicScale = true;
            sourceTexture.desc.FilterMode = FilterMode.Point;

            Assert.That(sourceField, Is.Not.Null);
            Assert.That(outputField, Is.Not.Null);
            sourceField.SetValue(pass, sourceTexture);

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 320;
            cameraData.actualHeight = 180;

            pass.Prepare(frameData);

            var outputTexture = (RenderGraphTexture)outputField.GetValue(pass);

            Assert.That(outputTexture, Is.Not.Null);
            Assert.That(outputTexture.desc.Width, Is.EqualTo(320));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(180));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(outputTexture.desc.Name, Is.EqualTo("DepthOfFieldOutput"));
            Assert.That(outputTexture.desc.UseDynamicScale, Is.True);
            Assert.That(outputTexture.desc.ClearBuffer, Is.False);
        }

        [Test]
        public void InactiveBypassDescriptors_CopiesSourceDescriptorToOutput()
        {
            var pass = new DepthOfFieldPass();
            var sourceField = typeof(DepthOfFieldPass).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic);
            var outputField = typeof(DepthOfFieldPass).GetField("output", BindingFlags.Instance | BindingFlags.NonPublic);
            var sourceTexture = RenderGraphTexture.CreateInput("SceneColor", GraphicsFormat.R16G16B16A16_SFloat);
            sourceTexture.desc.Width = 640;
            sourceTexture.desc.Height = 360;
            sourceTexture.desc.UseDynamicScale = true;

            Assert.That(sourceField, Is.Not.Null);
            Assert.That(outputField, Is.Not.Null);
            sourceField.SetValue(pass, sourceTexture);

            var resources = ((IRenderPass)pass).Initialize();
            var outputTexture = (RenderGraphTexture)outputField.GetValue(pass);
            outputTexture.desc.Width = 1;
            outputTexture.desc.Height = 1;
            outputTexture.desc.UseDynamicScale = false;

            InvokeApplyInactivePassBypassDescriptors(pass, resources);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(640));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(360));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(outputTexture.desc.UseDynamicScale, Is.True);
        }

        [Test]
        public void InactiveBypassHandles_ForwardsSourceHandleToOutput()
        {
            var renderGraph = new UnityRenderGraph("VividRP DepthOfField Bypass Test");
            var pass = new DepthOfFieldPass();
            var sourceField = typeof(DepthOfFieldPass).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic);
            var outputField = typeof(DepthOfFieldPass).GetField("output", BindingFlags.Instance | BindingFlags.NonPublic);
            var sourceTexture = RenderGraphTexture.CreateInput("SceneColor", GraphicsFormat.R16G16B16A16_SFloat);

            Assert.That(sourceField, Is.Not.Null);
            Assert.That(outputField, Is.Not.Null);
            sourceField.SetValue(pass, sourceTexture);

            var resources = ((IRenderPass)pass).Initialize();
            var outputTexture = (RenderGraphTexture)outputField.GetValue(pass);

            try
            {
                InvokeApplyInactivePassBypassHandles(renderGraph, pass, resources);

                Assert.That(sourceTexture.innerHandle.IsValid(), Is.True);
                Assert.That(outputTexture.innerHandle.Equals(sourceTexture.innerHandle), Is.True);
            }
            finally
            {
                renderGraph.Cleanup();
            }
        }

        [Test]
        public void SetSourceTexture_MarksPassResourceLayoutDirty_AndRestoreRecoversOriginalSource()
        {
            var pass = new DepthOfFieldPass();
            var setMethod = typeof(DepthOfFieldPass).GetMethod("SetSourceTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var restoreMethod = typeof(DepthOfFieldPass).GetMethod("RestoreSourceTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var sourceField = typeof(DepthOfFieldPass).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic);
            var outputField = typeof(DepthOfFieldPass).GetField("output", BindingFlags.Instance | BindingFlags.NonPublic);
            var originalSource = RenderGraphTexture.CreateInput("OriginalSource", GraphicsFormat.R16G16B16A16_SFloat);
            var injectedSource = RenderGraphTexture.CreateInput("InjectedSource", GraphicsFormat.B10G11R11_UFloatPack32);
            injectedSource.desc.Width = 256;
            injectedSource.desc.Height = 128;

            Assert.That(setMethod, Is.Not.Null);
            Assert.That(restoreMethod, Is.Not.Null);
            Assert.That(sourceField, Is.Not.Null);
            Assert.That(outputField, Is.Not.Null);
            sourceField.SetValue(pass, originalSource);
            var initialOutputDesc = ((RenderGraphTexture)outputField.GetValue(pass)).desc;

            setMethod.Invoke(pass, new object[] { injectedSource });

            Assert.That(pass.IsPassResourceLayoutDirty, Is.True);
            Assert.That(sourceField.GetValue(pass), Is.SameAs(injectedSource));

            var outputTexture = (RenderGraphTexture)outputField.GetValue(pass);
            Assert.That(outputTexture.desc, Is.SameAs(initialOutputDesc));
            Assert.That(outputTexture.desc, Is.Not.SameAs(injectedSource.desc));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(outputTexture.desc.Width, Is.EqualTo(256));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(128));

            pass.ClearPassResourceLayoutDirty();
            Assert.That(pass.IsPassResourceLayoutDirty, Is.False);

            restoreMethod.Invoke(pass, Array.Empty<object>());

            Assert.That(pass.IsPassResourceLayoutDirty, Is.True);
            Assert.That(sourceField.GetValue(pass), Is.SameAs(originalSource));
        }

        [Test]
        public void RefreshResourceReferences_UpdatesCachedSourceEntry_WhenSourceOverrideChanges()
        {
            var pass = new DepthOfFieldPass();
            var setMethod = typeof(DepthOfFieldPass).GetMethod("SetSourceTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var sourceField = typeof(DepthOfFieldPass).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic);
            var originalSource = RenderGraphTexture.CreateInput("OriginalSource", GraphicsFormat.R16G16B16A16_SFloat);
            var injectedSource = RenderGraphTexture.CreateInput("InjectedSource", GraphicsFormat.B10G11R11_UFloatPack32);

            Assert.That(setMethod, Is.Not.Null);
            Assert.That(sourceField, Is.Not.Null);
            sourceField.SetValue(pass, originalSource);

            var resources = ((IRenderPass)pass).Initialize();
            var sourceEntry = Array.Find(resources.Textures, texture => texture.Name == "source");

            Assert.That(sourceEntry, Is.Not.Null);
            Assert.That(sourceEntry.Texture, Is.SameAs(originalSource));

            setMethod.Invoke(pass, new object[] { injectedSource });

            Assert.That(pass.IsPassResourceLayoutDirty, Is.True);
            Assert.That(PassResourceReferenceRefreshUtility.TryRefresh(pass, resources), Is.True);
            Assert.That(sourceEntry.Texture, Is.SameAs(injectedSource));
        }

        [Test]
        public void CreateHistoryDescriptor_ReusesDescriptorInstance()
        {
            var pass = new DepthOfFieldPass();
            var createHistoryDescriptor = typeof(DepthOfFieldPass).GetMethod("CreateHistoryDescriptor", BindingFlags.Instance | BindingFlags.NonPublic);
            var widthField = typeof(DepthOfFieldPass).GetField("m_Width", BindingFlags.Instance | BindingFlags.NonPublic);
            var heightField = typeof(DepthOfFieldPass).GetField("m_Height", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(createHistoryDescriptor, Is.Not.Null);
            Assert.That(widthField, Is.Not.Null);
            Assert.That(heightField, Is.Not.Null);

            widthField.SetValue(pass, 640);
            heightField.SetValue(pass, 360);
            var firstDescriptor = (RenderGraphTextureDesc)createHistoryDescriptor.Invoke(pass, Array.Empty<object>());

            widthField.SetValue(pass, 1280);
            heightField.SetValue(pass, 720);
            var secondDescriptor = (RenderGraphTextureDesc)createHistoryDescriptor.Invoke(pass, Array.Empty<object>());

            Assert.That(secondDescriptor, Is.SameAs(firstDescriptor));
            Assert.That(secondDescriptor.Width, Is.EqualTo(1280));
            Assert.That(secondDescriptor.Height, Is.EqualTo(720));
            Assert.That(secondDescriptor.Name, Is.EqualTo("DepthOfFieldCoCHistoryCurrent"));
        }

        [Test]
        public void DepthOfFieldPassNode_ExposesSourceDepthMotionInputs_AndOwnedOutputPort()
        {
            var node = new AutoRegisteredDepthOfFieldPassNode();

            Assert.That(node.GetInputPortByName("source"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("linearDepth"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("motionVectors"), Is.Not.Null);
            Assert.That(node.HasOverrideOption("output"), Is.True);
            Assert.That(node.GetInputPortByName("output_In"), Is.Null);
            Assert.That(node.GetOutputPortByName("output"), Is.Not.Null);
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsFalse_ForDepthOfFieldPass()
        {
            Assert.That(RenderGraphPassExecutionUtility.SupportsAsyncCompute(typeof(DepthOfFieldPass)), Is.False);
        }

        [Test]
        public void DepthOfFieldPass_UsesStandardUnsafePassRecording()
        {
            Assert.That(typeof(IRenderGraphRecordingPass).IsAssignableFrom(typeof(DepthOfFieldPass)), Is.False);
        }

        [Test]
        public void Prepare_UsesEffectiveAntialiasingData_ForTemporalHistory()
        {
            var gameObject = new GameObject("DepthOfFieldPassTests_AAResolver");
            var camera = gameObject.AddComponent<Camera>();
            var additionalData = gameObject.AddComponent<VividAdditionalCameraData>();
            additionalData.antialiasing = VividAntialiasingMode.TemporalAntiAliasing;

            try
            {
                var pass = new DepthOfFieldPass();
                var usesTemporalField = typeof(DepthOfFieldPass).GetField(
                    "m_UsesTemporalAntialiasing",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(usesTemporalField, Is.Not.Null);

                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.camera = camera;
                cameraData.additionalData = additionalData;

                var antialiasingData = frameData.GetOrCreate<VividAntialiasingData>();
                antialiasingData.effectiveMode = VividAntialiasingMode.None;
                antialiasingData.usesTemporalJitter = false;

                pass.Prepare(frameData);

                Assert.That((bool)usesTemporalField.GetValue(pass), Is.False);

                antialiasingData.effectiveMode = VividAntialiasingMode.TemporalAntiAliasing;
                antialiasingData.usesTemporalJitter = true;

                pass.Prepare(frameData);

                Assert.That((bool)usesTemporalField.GetValue(pass), Is.True);
            }
            finally
            {
                GameObject.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void DepthOfFieldCompute_ContainsPhysicalPipelineKernels_AndResourceBindings()
        {
            var computeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "DepthOfField.compute"));
            var passSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "DepthOfField", "DepthOfFieldPass.cs"));
            var registrySource = File.ReadAllText(GetPackageFilePath("Editor", "RenderGraph", "GeneratedRenderPassNodes.g.cs"));
            var pipelineResourcesSource = File.ReadAllText(GetPackageFilePath("Runtime", "Resources", "PipelineResources.asset"));

            Assert.That(computeSource, Does.Contain("#pragma kernel KCoCPhysical"));
            Assert.That(computeSource, Does.Contain("#pragma kernel KComputeSlowTiles"));
            Assert.That(computeSource, Does.Contain("#pragma kernel KGatherFastTiles"));
            Assert.That(computeSource, Does.Contain("#pragma kernel KCombineFastTiles"));
            Assert.That(computeSource, Does.Contain("ComputePhysicalCoC"));
            Assert.That(computeSource, Does.Contain("GetTileClass"));
            Assert.That(computeSource, Does.Contain("EvaluatePhysicalBlur"));
            Assert.That(passSource, Does.Contain("m_Settings.physicallyBased"));
            Assert.That(passSource, Does.Contain("m_Settings.coCStabilization"));
            Assert.That(passSource, Does.Contain("DispatchCoCMinMax"));
            Assert.That(passSource, Does.Contain("DispatchSlowTiles"));
            Assert.That(passSource, Does.Contain("DispatchCombine"));
            Assert.That(passSource, Does.Contain("DepthOfFieldPass : ComputePass"));
            Assert.That(passSource, Does.Contain("[PassBypass(nameof(source))]"));
            Assert.That(passSource, Does.Contain("public override bool IsActive(ContextContainer frameData)"));
            Assert.That(passSource, Does.Not.Contain("IRenderGraphRecordingPass"));
            Assert.That(passSource, Does.Not.Contain("TryRegisterPassthrough"));
            Assert.That(passSource, Does.Not.Contain("context.RegisterTextureHandle(output, sourceHandle)"));
            Assert.That(passSource, Does.Contain("m_ComputeSlowTilesKernel, InputLinearDepthId"));
            Assert.That(passSource, Does.Contain("m_GatherFastTilesKernel, InputLinearDepthId"));
            Assert.That(passSource, Does.Contain("ResolvePhysicalMaxCoC"));
            Assert.That(passSource, Does.Contain("DepthOfFieldCompute"));
            Assert.That(registrySource, Does.Contain("internal sealed class DepthOfFieldPass : RenderPassNodeData"));
            Assert.That(pipelineResourcesSource, Does.Contain("Shaders/Core/Private/DepthOfField.compute"));
        }

        [Test]
        public void BuildRegistrations_IncludesDepthOfFieldPass()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(new[] { typeof(DepthOfFieldPass) });

            Assert.That(registrations, Has.Count.EqualTo(1));
            Assert.That(registrations[0].NodeClassName, Is.EqualTo("DepthOfFieldPass"));
            Assert.That(registrations[0].PassType, Is.EqualTo(typeof(DepthOfFieldPass)));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
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

        private static void InvokeApplyInactivePassBypassDescriptors(
            IRenderPass pass,
            PassResource resources,
            RenderGraphPassDefinition passDefinition = null)
        {
            var method = typeof(PassRecorder).GetMethod(
                "ApplyInactivePassBypassDescriptors",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { pass, resources, passDefinition });
        }

        private static void InvokeApplyInactivePassBypassHandles(
            UnityRenderGraph renderGraph,
            IRenderPass pass,
            PassResource resources,
            RenderGraphPassDefinition passDefinition = null)
        {
            var method = typeof(PassRecorder).GetMethod(
                "ApplyInactivePassBypassHandles",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            method.Invoke(null, new object[]
            {
                renderGraph,
                pass,
                resources,
                passDefinition,
                new Dictionary<RenderGraphTexture, TextureHandle>(),
                new Dictionary<RenderGraphBuffer, BufferHandle>(),
            });
        }
    }
}
