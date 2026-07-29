using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class FinalBlitPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredFinalBlitPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(FinalBlitPass);
        }

        [Test]
        public void GetCameraBackBufferTextureUVOrigin_UsesBottomLeft_ForSceneAndPreviewAndTargetTexture()
        {
            var method = typeof(FinalBlitPass).GetMethod(
                "GetCameraBackBufferTextureUVOrigin",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            Assert.That(
                (TextureUVOrigin)method.Invoke(null, new object[] { CameraType.SceneView, false }),
                Is.EqualTo(TextureUVOrigin.BottomLeft));
            Assert.That(
                (TextureUVOrigin)method.Invoke(null, new object[] { CameraType.Preview, false }),
                Is.EqualTo(TextureUVOrigin.BottomLeft));
            Assert.That(
                (TextureUVOrigin)method.Invoke(null, new object[] { CameraType.Game, true }),
                Is.EqualTo(TextureUVOrigin.BottomLeft));
        }

        [Test]
        public void GetCameraBackBufferTextureUVOrigin_UsesPlatformBackBufferOrientation_ForGameCamera()
        {
            var method = typeof(FinalBlitPass).GetMethod(
                "GetCameraBackBufferTextureUVOrigin",
                BindingFlags.Static | BindingFlags.NonPublic);
            var expected = SystemInfo.graphicsUVStartsAtTop ? TextureUVOrigin.TopLeft : TextureUVOrigin.BottomLeft;

            Assert.That(method, Is.Not.Null);
            Assert.That(
                (TextureUVOrigin)method.Invoke(null, new object[] { CameraType.Game, false }),
                Is.EqualTo(expected));
        }

        [Test]
        public void GetFinalBlitScaleBias_FlipsY_WhenOriginsDiffer()
        {
            var method = typeof(FinalBlitPass).GetMethod(
                "GetFinalBlitScaleBias",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            Assert.That(
                (Vector4)method.Invoke(null, new object[] { new Vector2(1f, 1f), TextureUVOrigin.BottomLeft, TextureUVOrigin.TopLeft }),
                Is.EqualTo(new Vector4(1f, -1f, 0f, 1f)));
            Assert.That(
                (Vector4)method.Invoke(null, new object[] { new Vector2(1f, 1f), TextureUVOrigin.BottomLeft, TextureUVOrigin.BottomLeft }),
                Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
        }

        [Test]
        public void ShouldSetViewport_IsFalse_ForSceneView()
        {
            var method = typeof(FinalBlitPass).GetMethod(
                "ShouldSetViewport",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            Assert.That((bool)method.Invoke(null, new object[] { CameraType.SceneView }), Is.False);
            Assert.That((bool)method.Invoke(null, new object[] { CameraType.Game }), Is.True);
        }

        [Test]
        public void GetViewport_UsesCameraPixelRect_WhenAvailable()
        {
            var method = typeof(FinalBlitPass).GetMethod(
                "GetViewport",
                BindingFlags.Static | BindingFlags.NonPublic);
            var cameraData = new VividCameraData
            {
                pixelRect = new Rect(12f, 34f, 320f, 180f),
                actualWidth = 640,
                actualHeight = 360,
            };

            Assert.That(method, Is.Not.Null);
            Assert.That(
                (Rect)method.Invoke(null, new object[] { cameraData }),
                Is.EqualTo(new Rect(12f, 34f, 320f, 180f)));
        }

        [Test]
        public void Initialize_RegistersReadOnlySourceAndColorGradingTextures()
        {
            IRenderPass renderPass = new FinalBlitPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures, Has.Length.EqualTo(3));
            Assert.That(resources.Buffers, Is.Empty);
            Assert.That(resources.Textures[0].Name, Is.EqualTo("source"));
            Assert.That(resources.Textures[0].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures[0].AttachmentIndex, Is.EqualTo(-1));
            Assert.That(resources.Textures[0].IsDepthAttachment, Is.False);
            Assert.That(resources.Textures[1].Name, Is.EqualTo("ColorGradingTexture"));
            Assert.That(resources.Textures[1].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures[1].AttachmentIndex, Is.EqualTo(-1));
            Assert.That(resources.Textures[1].IsDepthAttachment, Is.False);
            Assert.That(resources.Textures[2].Name, Is.EqualTo("BloomTexture"));
            Assert.That(resources.Textures[2].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures[2].AttachmentIndex, Is.EqualTo(-1));
            Assert.That(resources.Textures[2].IsDepthAttachment, Is.False);
        }

        [Test]
        public void FinalBlitPass_UsesStableResourceLayout_ForSourceOverrides()
        {
            Assert.That(typeof(IStablePassResourceLayout).IsAssignableFrom(typeof(FinalBlitPass)), Is.True);
        }

        [Test]
        public void Prepare_CachesViewportFromCameraDimensions()
        {
            var pass = new FinalBlitPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.pixelRect = new Rect(4f, 8f, 320f, 180f);

            pass.Prepare(frameData);

            var viewportField = typeof(FinalBlitPass).GetField("m_Viewport", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(viewportField, Is.Not.Null);
            Assert.That((Rect)viewportField.GetValue(pass), Is.EqualTo(new Rect(4f, 8f, 320f, 180f)));
        }

        [Test]
        public void Prepare_PreservesHDROutputSettings_WhenPostProcessingIsDisabled()
        {
            var pass = new FinalBlitPass();
            using var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.pixelRect = new Rect(0f, 0f, 320f, 180f);
            cameraData.hdrOutputActive = true;
            cameraData.hdrDisplayColorGamut = ColorGamut.sRGB;
            cameraData.hdrDisplayInformation = new HDROutputUtils.HDRDisplayInformation(1000, 1200, 1, 250f);

            pass.Prepare(frameData);

            var settingsField = typeof(FinalBlitPass).GetField(
                "m_ColorGradingSettings",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(settingsField, Is.Not.Null);
            var settings = (ColorGradingSettingsData)settingsField.GetValue(pass);
            Assert.That(settings.hdrOutputActive, Is.True);
            Assert.That(settings.RequiresLut, Is.True);
        }

        [Test]
        public void SetSourceTexture_MarksPassResourceLayoutDirty()
        {
            var pass = new FinalBlitPass();
            var setMethod = typeof(FinalBlitPass).GetMethod("SetSourceTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var restoreMethod = typeof(FinalBlitPass).GetMethod("RestoreSourceTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var sourceField = typeof(FinalBlitPass).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic);
            var originalSource = RenderGraphTexture.CreateInput("OriginalSource", GraphicsFormat.R16G16B16A16_SFloat);
            var injectedSource = RenderGraphTexture.CreateInput("InjectedSource", GraphicsFormat.R16G16B16A16_SFloat);

            Assert.That(setMethod, Is.Not.Null);
            Assert.That(restoreMethod, Is.Not.Null);
            Assert.That(sourceField, Is.Not.Null);
            sourceField.SetValue(pass, originalSource);

            setMethod.Invoke(pass, new object[] { injectedSource });

            Assert.That(pass.IsPassResourceLayoutDirty, Is.True);
            Assert.That(sourceField.GetValue(pass), Is.SameAs(injectedSource));

            pass.ClearPassResourceLayoutDirty();
            Assert.That(pass.IsPassResourceLayoutDirty, Is.False);

            restoreMethod.Invoke(pass, Array.Empty<object>());

            Assert.That(pass.IsPassResourceLayoutDirty, Is.True);
            Assert.That(sourceField.GetValue(pass), Is.SameAs(originalSource));
        }

        [Test]
        public void FinalBlitShader_ContainsColorGradingLogic_AndSharedBlitShaderDoesNot()
        {
            var finalBlitShaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "FinalBlit.shader"));
            var blitShaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Blit.shader"));
            var passSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "FinalBlitPass.cs"));
            var frameContextSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "FrameContextSystem.cs"));
            var autoExposureSystemSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposureRuntimeUtility.cs"));

            Assert.That(finalBlitShaderSource, Does.Contain("Shader \"Hidden/VividRP/FinalBlit\""));
            Assert.That(finalBlitShaderSource, Does.Contain("_VividColorGradingLut"));
            Assert.That(finalBlitShaderSource, Does.Contain("_VividColorGradingParams"));
            Assert.That(finalBlitShaderSource, Does.Contain("_VividAutoExposureBuffer"));
            Assert.That(finalBlitShaderSource, Does.Contain("_VividAutoExposurePreExposureBuffer"));
            Assert.That(finalBlitShaderSource, Does.Contain("_VividAutoExposureParams"));
            Assert.That(finalBlitShaderSource, Does.Contain("oneOverPreExposure"));
            Assert.That(finalBlitShaderSource, Does.Contain("ApplyLut3D("));

            Assert.That(blitShaderSource, Does.Contain("Shader \"Hidden/VividRP/Blit\""));
            Assert.That(blitShaderSource, Does.Not.Contain("_VividColorGradingLut"));
            Assert.That(blitShaderSource, Does.Not.Contain("_VividColorGradingParams"));
            Assert.That(blitShaderSource, Does.Not.Contain("ApplyLut3D("));

            Assert.That(passSource, Does.Contain("resources.FinalBlitShader"));
            Assert.That(passSource, Does.Contain("m_EnableExposure"));
            Assert.That(passSource, Does.Contain("m_ExposureData?.frameExposureBuffer ?? defaultExposureBuffer"));
            Assert.That(passSource, Does.Contain("PassRecorder.ImportBufferForPass("));
            Assert.That(passSource, Does.Contain("AccessFlags.Read"));
            Assert.That(passSource, Does.Not.Contain("SetBuffer(AutoExposurePreExposureBufferId"));
            Assert.That(passSource, Does.Not.Contain("ExecuteAutoExposure("));
            Assert.That(passSource, Does.Not.Contain("RefreshAutoExposureImplementation("));
            Assert.That(passSource, Does.Not.Contain("m_AutoExposureCompute"));
            Assert.That(frameContextSource, Does.Not.Contain("BindFrameGlobals(cmd, frameData.Get<VividExposureData>());"));
            Assert.That(autoExposureSystemSource, Does.Contain("BindFrameGlobals(cmd, frameData.Get<VividExposureData>());"));
            Assert.That(passSource, Does.Not.Contain("AutoExposureStatsReadbackBridge.Request("));
            Assert.That(passSource, Does.Not.Contain("AutoExposureRuntimeManager.CommitFrame("));
            Assert.That(passSource, Does.Contain("VividAutoExposureSystem.CommitFrame("));
            Assert.That(passSource, Does.Not.Contain("resources.BlitShader"));
        }

        [Test]
        public void PassRecorderSource_DoesNotInjectStopNaNPass()
        {
            var passRecorderSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "PassRecorder.Execution.cs"));

            Assert.That(passRecorderSource, Does.Not.Contain("ShouldInjectStopNaNPass"));
            Assert.That(passRecorderSource, Does.Not.Contain("GetOrCreateInjectedStopNaNPass"));
            Assert.That(passRecorderSource, Does.Not.Contain("RecordInjectedStopNaNPass"));
            Assert.That(passRecorderSource, Does.Not.Contain("StopNaNPass (Injected)"));
        }

        [Test]
        public void FinalBlitPassNode_DoesNotExposeAsyncComputeOption()
        {
            var node = new AutoRegisteredFinalBlitPassNode();

            Assert.That(node.HasAsyncComputeOption(), Is.False);
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsFalse_ForFinalBlitPass()
        {
            Assert.That(RenderGraphPassExecutionUtility.SupportsAsyncCompute(typeof(FinalBlitPass)), Is.False);
        }

        [Test]
        public void StopNaNPass_UsesDedicatedFramebufferFetchShader()
        {
            var stopNaNShader = LoadPackageAsset<Shader>("Shaders/Core/Private/StopNaN.shader");
            var blitShader = LoadPackageAsset<Shader>("Shaders/Core/Private/Blit.shader");

            Assert.That(stopNaNShader, Is.Not.Null);
            Assert.That(stopNaNShader.name, Is.EqualTo("Hidden/VividRP/StopNaN"));
            Assert.That(blitShader, Is.Not.Null);
            Assert.That(blitShader.name, Is.EqualTo("Hidden/VividRP/Blit"));

            var container = ScriptableObject.CreateInstance<PipelineResourcesContainer>();
            try
            {
                PipelineResourceUpdater.UpdateContainerResources(container);
                var stopNaNEntry = container.Entries.FirstOrDefault(entry =>
                    entry.ResourceName == "Shaders/Core/Private/StopNaN");

                Assert.That(stopNaNEntry, Is.Not.Null);
                Assert.That(stopNaNEntry.ResourceObject, Is.SameAs(stopNaNShader));

                var pass = new StopNaNPass();
                try
                {
                    PipelineResourceManager.Cleanup();
                    pass.Create();

                    var materialField = typeof(StopNaNPass).GetField(
                        "m_Material",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    Assert.That(materialField, Is.Not.Null);
                    var material = (Material)materialField.GetValue(pass);
                    Assert.That(material, Is.Not.Null);
                    Assert.That(material.shader, Is.SameAs(stopNaNShader));
                }
                finally
                {
                    pass.Dispose();
                    PipelineResourceManager.Cleanup();
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(container);
            }
        }

        [Test]
        public void StopNaNPass_RegistersReadOnlyInput_WriteOnlyOutput_AndBypassRule()
        {
            IRenderPass renderPass = new StopNaNPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures;

            Assert.That(typeof(RasterPass).IsAssignableFrom(typeof(StopNaNPass)), Is.True);
            Assert.That(textureEntries, Has.Length.EqualTo(2));
            Assert.That(textureEntries[0].Name, Is.EqualTo("m_Source"));
            Assert.That(textureEntries[0].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries[0].AttachmentIndex, Is.EqualTo(-1));
            Assert.That(textureEntries[0].InputAttachmentIndex, Is.EqualTo(0));
            Assert.That(textureEntries[1].Name, Is.EqualTo("StopNaNOutput"));
            Assert.That(textureEntries[1].Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(textureEntries[1].AttachmentIndex, Is.EqualTo(0));
            Assert.That(textureEntries[1].InputAttachmentIndex, Is.EqualTo(-1));
            Assert.That(resources.BypassRules, Has.Length.EqualTo(1));
            Assert.That(resources.BypassRules[0].SourceFieldName, Is.EqualTo("m_Source"));
            Assert.That(resources.BypassRules[0].OutputFieldName, Is.EqualTo("m_OutputTexture"));
            Assert.That(resources.BypassRules[0].ResourceType, Is.EqualTo(PassResourceType.Texture));
        }

        [Test]
        public void StopNaNPass_Prepare_ClonesInputDescriptorToOutput()
        {
            var pass = new StopNaNPass();
            var sourceField = typeof(StopNaNPass).GetField("m_Source", BindingFlags.Instance | BindingFlags.NonPublic);
            var outputField = typeof(StopNaNPass).GetField("m_OutputTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var input = RenderGraphTexture.CreateInput("InjectedSource", GraphicsFormat.B10G11R11_UFloatPack32);
            input.desc.Width = 320;
            input.desc.Height = 180;
            input.desc.UseDynamicScale = true;

            Assert.That(sourceField, Is.Not.Null);
            Assert.That(outputField, Is.Not.Null);
            sourceField.SetValue(pass, input);

            pass.Prepare(new ContextContainer());

            var outputTexture = (RenderGraphTexture)outputField.GetValue(pass);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(320));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(180));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(outputTexture.desc.Name, Is.EqualTo("StopNaNOutput"));
            Assert.That(outputTexture.desc.UseDynamicScale, Is.True);
        }

        [Test]
        public void BloomPass_SetSourceTexture_MarksPassResourceLayoutDirty_AndRestoreRecoversOriginalSource()
        {
            AssertSourceOverrideBehavior(
                new BloomPass(),
                "source",
                typeof(BloomPass),
                "SetSourceTexture",
                "RestoreSourceTexture");
        }

        [Test]
        public void AutoExposurePass_SetSourceTexture_MarksPassResourceLayoutDirty_AndRestoreRecoversOriginalSource()
        {
            AssertSourceOverrideBehavior(
                new AutoExposurePass(),
                "source",
                typeof(AutoExposurePass),
                "SetSourceTexture",
                "RestoreSourceTexture");
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

        private static T LoadPackageAsset<T>(string relativeAssetPath)
            where T : UnityEngine.Object
        {
            foreach (var candidatePath in VividPackagePathUtility.GetCandidateAssetPaths(relativeAssetPath))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(candidatePath);
                if (asset != null)
                    return asset;
            }

            return null;
        }

        private static void AssertSourceOverrideBehavior(
            IDynamicPassResourceLayout pass,
            string fieldName,
            Type passType,
            string setMethodName,
            string restoreMethodName)
        {
            var setMethod = passType.GetMethod(setMethodName, BindingFlags.Instance | BindingFlags.NonPublic);
            var restoreMethod = passType.GetMethod(restoreMethodName, BindingFlags.Instance | BindingFlags.NonPublic);
            var sourceField = passType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            var originalSource = RenderGraphTexture.CreateInput("OriginalSource", GraphicsFormat.R16G16B16A16_SFloat);
            var injectedSource = RenderGraphTexture.CreateInput("InjectedSource", GraphicsFormat.R16G16B16A16_SFloat);

            Assert.That(setMethod, Is.Not.Null);
            Assert.That(restoreMethod, Is.Not.Null);
            Assert.That(sourceField, Is.Not.Null);
            sourceField.SetValue(pass, originalSource);

            setMethod.Invoke(pass, new object[] { injectedSource });

            Assert.That(pass.IsPassResourceLayoutDirty, Is.True);
            Assert.That(sourceField.GetValue(pass), Is.SameAs(injectedSource));

            pass.ClearPassResourceLayoutDirty();
            Assert.That(pass.IsPassResourceLayoutDirty, Is.False);

            restoreMethod.Invoke(pass, Array.Empty<object>());

            Assert.That(pass.IsPassResourceLayoutDirty, Is.True);
            Assert.That(sourceField.GetValue(pass), Is.SameAs(originalSource));
        }

    }
}
