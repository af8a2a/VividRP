using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public class VividRenderPipelineAssetGpuDrivenTests
    {
        [Test]
        public void Asset_DefaultsToGpuDrivenDisabled()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                Assert.That(asset.ColorGradingSpace, Is.EqualTo(ColorGradingSpace.sRGB));
                Assert.That(asset.AutoExposureImplementation, Is.EqualTo(AutoExposureImplementationPath.Unreal));
                Assert.That(asset.EnableGPUDriven, Is.False);
                Assert.That(asset.EnableGPUDrivenOcclusionCulling, Is.True);
                Assert.That(asset.EnableTerrainRuntimeVirtualTexture, Is.False);
                Assert.That(asset.DecalTechnique, Is.EqualTo(VividDecalTechnique.ClusteredBindless));
                Assert.That(asset.GPUDrivenTextureBackend, Is.EqualTo(GPUDrivenTextureBackendMode.VirtualTexture));
                Assert.That(
                    asset.GPUDrivenVirtualTexturePhysicalPoolQuality,
                    Is.EqualTo(GPUDrivenVirtualTexturePhysicalPoolQuality.Medium));
                Assert.That(asset.VirtualTextureMaxResidencyAllocationsPerFrame, Is.EqualTo(64));
                Assert.That(asset.VirtualTextureMaxPrefetchAllocationsPerFrame, Is.Zero);
                Assert.That(asset.VirtualTextureMaxPageUploadsPerFrame, Is.EqualTo(64));
                Assert.That(asset.VirtualTextureMaxUploadBytesPerFrameMiB, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void SerializedObject_UpdatesGpuDrivenProperty()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                var serializedObject = new SerializedObject(asset);
                var colorGradingSpaceProperty = serializedObject.FindProperty("m_ColorGradingSpace");
                var implementationProperty = serializedObject.FindProperty("m_AutoExposureImplementation");
                var property = serializedObject.FindProperty("m_EnableGPUDriven");
                var occlusionProperty = serializedObject.FindProperty("m_EnableGPUDrivenOcclusionCulling");
                var terrainRVTProperty = serializedObject.FindProperty("m_EnableTerrainRuntimeVirtualTexture");
                var decalTechniqueProperty = serializedObject.FindProperty("m_DecalTechnique");
                var textureBackendProperty = serializedObject.FindProperty("m_GPUDrivenTextureBackend");
                var virtualTexturePhysicalPoolQualityProperty = serializedObject.FindProperty(
                    "m_GPUDrivenVirtualTexturePhysicalPoolQuality");
                var maxResidencyProperty = serializedObject.FindProperty(
                    "m_VirtualTextureMaxResidencyAllocationsPerFrame");
                var maxPrefetchProperty = serializedObject.FindProperty(
                    "m_VirtualTextureMaxPrefetchAllocationsPerFrame");
                var maxPageUploadsProperty = serializedObject.FindProperty(
                    "m_VirtualTextureMaxPageUploadsPerFrame");
                var maxUploadMiBProperty = serializedObject.FindProperty(
                    "m_VirtualTextureMaxUploadBytesPerFrameMiB");

                Assert.That(colorGradingSpaceProperty, Is.Not.Null);
                Assert.That(implementationProperty, Is.Not.Null);
                Assert.That(property, Is.Not.Null);
                Assert.That(occlusionProperty, Is.Not.Null);
                Assert.That(terrainRVTProperty, Is.Not.Null);
                Assert.That(decalTechniqueProperty, Is.Not.Null);
                Assert.That(textureBackendProperty, Is.Not.Null);
                Assert.That(virtualTexturePhysicalPoolQualityProperty, Is.Not.Null);
                Assert.That(maxResidencyProperty, Is.Not.Null);
                Assert.That(maxPrefetchProperty, Is.Not.Null);
                Assert.That(maxPageUploadsProperty, Is.Not.Null);
                Assert.That(maxUploadMiBProperty, Is.Not.Null);

                colorGradingSpaceProperty.enumValueIndex = (int)ColorGradingSpace.AcesCg;
                implementationProperty.enumValueIndex = (int)AutoExposureImplementationPath.HDRP;
                property.boolValue = true;
                occlusionProperty.boolValue = false;
                terrainRVTProperty.boolValue = true;
                decalTechniqueProperty.enumValueIndex = (int)VividDecalTechnique.TerrainRuntimeVirtualTexture;
                textureBackendProperty.enumValueIndex = (int) GPUDrivenTextureBackendMode.Bindless;
                virtualTexturePhysicalPoolQualityProperty.enumValueIndex =
                    (int)GPUDrivenVirtualTexturePhysicalPoolQuality.High;
                maxResidencyProperty.intValue = 41;
                maxPrefetchProperty.intValue = 7;
                maxPageUploadsProperty.intValue = 13;
                maxUploadMiBProperty.intValue = 23;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(asset.ColorGradingSpace, Is.EqualTo(ColorGradingSpace.AcesCg));
                Assert.That(asset.AutoExposureImplementation, Is.EqualTo(AutoExposureImplementationPath.HDRP));
                Assert.That(asset.EnableGPUDriven, Is.True);
                Assert.That(asset.EnableGPUDrivenOcclusionCulling, Is.False);
                Assert.That(asset.EnableTerrainRuntimeVirtualTexture, Is.True);
                Assert.That(
                    asset.DecalTechnique,
                    Is.EqualTo(VividDecalTechnique.TerrainRuntimeVirtualTexture));
                Assert.That(asset.GPUDrivenTextureBackend, Is.EqualTo(GPUDrivenTextureBackendMode.Bindless));
                Assert.That(
                    asset.GPUDrivenVirtualTexturePhysicalPoolQuality,
                    Is.EqualTo(GPUDrivenVirtualTexturePhysicalPoolQuality.High));
                Assert.That(asset.VirtualTextureMaxResidencyAllocationsPerFrame, Is.EqualTo(41));
                Assert.That(asset.VirtualTextureMaxPrefetchAllocationsPerFrame, Is.EqualTo(7));
                Assert.That(asset.VirtualTextureMaxPageUploadsPerFrame, Is.EqualTo(13));
                Assert.That(asset.VirtualTextureMaxUploadBytesPerFrameMiB, Is.EqualTo(23));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Asset_DoesNotExposeLegacyGpuDrivenDebugOverlayToggle()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                var serializedObject = new SerializedObject(asset);

                Assert.That(serializedObject.FindProperty("m_EnableGPUDrivenDebugOverlay"), Is.Null);
                Assert.That(typeof(VividRenderPipelineAsset).GetProperty("EnableGPUDrivenDebugOverlay"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Asset_DefaultShader_IsStandardLit()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                Assert.That(asset.defaultShader, Is.Not.Null);
                Assert.That(asset.defaultShader.name, Is.EqualTo("VividRP/Material/StandardLit"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Asset_DefaultMaterial_UsesPrecreatedStandardLitMaterial()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                Material expectedMaterial = Resources.Load<Material>("DefaultMaterial");
                Material material = asset.defaultMaterial;

                Assert.That(expectedMaterial, Is.Not.Null);
                Assert.That(material, Is.Not.Null);
                Assert.That(material, Is.SameAs(expectedMaterial));
                Assert.That(material.name, Is.EqualTo("DefaultMaterial"));
                Assert.That(material.shader, Is.Not.Null);
                Assert.That(material.shader.name, Is.EqualTo("VividRP/Material/StandardLit"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }

    public class VividGPUDrivenSystemLifecycleTests
    {
        [SetUp]
        public void SetUp()
        {
            VividGPUDrivenSystem.Deinitialize();
            FrameContextSystem.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            VividGPUDrivenSystem.Deinitialize();
            FrameContextSystem.Clear();
        }

        [Test]
        public void ResolveConfiguredTextureBackendMode_DefaultsToVirtualTextureIndependentlyOfFeatureState()
        {
            Assert.That(
                VividGPUDrivenSystem.ResolveConfiguredTextureBackendMode(null),
                Is.EqualTo(GPUDrivenTextureBackendMode.VirtualTexture));

            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
            try
            {
                Assert.That(asset.EnableGPUDriven, Is.False);
                Assert.That(
                    VividGPUDrivenSystem.ResolveConfiguredTextureBackendMode(asset),
                    Is.EqualTo(GPUDrivenTextureBackendMode.VirtualTexture));

                asset.GPUDrivenTextureBackend = GPUDrivenTextureBackendMode.Bindless;
                Assert.That(
                    VividGPUDrivenSystem.ResolveConfiguredTextureBackendMode(asset),
                    Is.EqualTo(GPUDrivenTextureBackendMode.Bindless));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void TerrainRuntimeVirtualTextureDecals_ValidateRequiredPipelineDependenciesInOrder()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
            try
            {
                Assert.That(
                    asset.TryValidateTerrainRuntimeVirtualTextureDecals(out string gpuReason),
                    Is.False);
                Assert.That(gpuReason, Does.Contain("GPUDriven"));

                asset.EnableGPUDriven = true;
                asset.GPUDrivenTextureBackend = GPUDrivenTextureBackendMode.Bindless;
                Assert.That(
                    asset.TryValidateTerrainRuntimeVirtualTextureDecals(out string backendReason),
                    Is.False);
                Assert.That(backendReason, Does.Contain("Virtual Texture"));

                asset.GPUDrivenTextureBackend = GPUDrivenTextureBackendMode.VirtualTexture;
                Assert.That(
                    asset.TryValidateTerrainRuntimeVirtualTextureDecals(out string terrainReason),
                    Is.False);
                Assert.That(terrainReason, Does.Contain("Terrain Runtime Virtual Texture"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ResolveConfiguredVirtualTextureDescriptorProfile_UsesMediumByDefaultAndAssetQuality()
        {
            GPUDrivenVirtualTextureDescriptorProfile defaultProfile =
                VividGPUDrivenSystem.ResolveConfiguredVirtualTextureDescriptorProfile(null);
            Assert.That(defaultProfile.CachePageCount, Is.EqualTo(512));

            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
            try
            {
                asset.GPUDrivenVirtualTexturePhysicalPoolQuality =
                    GPUDrivenVirtualTexturePhysicalPoolQuality.High;

                GPUDrivenVirtualTextureDescriptorProfile highProfile =
                    VividGPUDrivenSystem.ResolveConfiguredVirtualTextureDescriptorProfile(asset);

                Assert.That(highProfile.CachePageCount, Is.EqualTo(1024));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void RequiresTextureBackendRecreation_TracksBackendModeOnly()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
            try
            {
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.VirtualTexture,
                        asset),
                    Is.False);

                asset.GPUDrivenVirtualTexturePhysicalPoolQuality =
                    GPUDrivenVirtualTexturePhysicalPoolQuality.High;
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.VirtualTexture,
                        asset),
                    Is.False,
                    "Physical-pool quality takes effect on the next backend initialization.");

                asset.GPUDrivenTextureBackend = GPUDrivenTextureBackendMode.Bindless;
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.VirtualTexture,
                        asset),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void RequiresTextureBackendRecreation_TracksTerrainRVTOptInForVirtualTextureBackend()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
            try
            {
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.VirtualTexture,
                        terrainRuntimeVirtualTextureRequested: false,
                        asset),
                    Is.False);

                asset.EnableTerrainRuntimeVirtualTexture = true;
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.VirtualTexture,
                        terrainRuntimeVirtualTextureRequested: false,
                        asset),
                    Is.True);
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.VirtualTexture,
                        terrainRuntimeVirtualTextureRequested: true,
                        asset),
                    Is.False);

                asset.GPUDrivenTextureBackend = GPUDrivenTextureBackendMode.Bindless;
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.Bindless,
                        terrainRuntimeVirtualTextureRequested: false,
                        asset),
                    Is.False,
                    "Bindless ignores the experimental Terrain RVT toggle.");
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Deinitialize_DisposesCurrentSingletonInstance()
        {
            var system = VividGPUDrivenSystem.instance;

            Assert.That(system, Is.Not.Null);
            Assert.That(VividGPUDrivenSystem.HasInstance, Is.True);

            VividGPUDrivenSystem.Deinitialize();

            Assert.That(VividGPUDrivenSystem.HasInstance, Is.False);
        }

        [Test]
        public void Instance_RecreatesSingleton_AfterDeinitialize()
        {
            var first = VividGPUDrivenSystem.instance;

            VividGPUDrivenSystem.Deinitialize();

            var second = VividGPUDrivenSystem.instance;

            Assert.That(second, Is.Not.Null);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(VividGPUDrivenSystem.HasInstance, Is.True);
        }

        [Test]
        public void ShouldPrepareFrame_ReturnsTrue_ForRepeatedEditorFrameIndex()
        {
            Assert.That(VividGPUDrivenSystem.ShouldPrepareFrame(12, 12, isPlaying: false), Is.True);
        }

        [Test]
        public void ShouldPrepareFrame_ReturnsFalse_ForRepeatedPlayModeFrameIndex()
        {
            Assert.That(VividGPUDrivenSystem.ShouldPrepareFrame(12, 12, isPlaying: true), Is.False);
        }

        [Test]
        public void RenderCamera_SchedulesCpuCullAfterBeginCameraCallbacksAndBeforeUnityCull()
        {
            string source = ReadRuntimeSource(
                "Runtime",
                "RenderPipeline",
                "VividRenderPipeline.cs");
            string renderCamera = SliceSource(
                source,
                "private void RenderCamera(",
                "private void DispatchBeginCameraRendering(");

            int beginCamera = renderCamera.IndexOf("DispatchBeginCameraRendering(context, camera)");
            int gpuDrivenCull = renderCamera.IndexOf(
                "VividGPUDrivenSystem.ScheduleCullForCamera(camera, frameIndex)");
            int decalCull = renderCamera.IndexOf("DecalSystem.ScheduleCullForCamera(camera)");
            int unityCull = renderCamera.IndexOf("context.Cull(ref cullingParameters)");
            int prepareFrame = renderCamera.IndexOf("PassRecorder.PrepareFrame(graphAsset, cmdBuffer)");

            Assert.That(beginCamera, Is.GreaterThanOrEqualTo(0));
            Assert.That(gpuDrivenCull, Is.GreaterThan(beginCamera));
            Assert.That(decalCull, Is.GreaterThan(gpuDrivenCull));
            Assert.That(unityCull, Is.GreaterThan(decalCull));
            Assert.That(prepareFrame, Is.GreaterThan(unityCull));
        }

        [Test]
        public void PrepareFrame_InvalidatesDrawSetReadersBeforePrimitiveSceneSynchronization()
        {
            string source = ReadRuntimeSource(
                "Runtime",
                "SubSystem",
                "GPUDriven",
                "VividGPUDrivenSystem.cs");
            string prepareFrame = SliceSource(
                source,
                "public void PrepareFrame(bool reportStats = true)",
                "public void Cull(");

            int invalidateBuilds = prepareFrame.IndexOf(
                "m_PrimitiveDrawSetSystem.CompleteAndInvalidateAllBuilds()");
            int invalidateShadowBuilds = prepareFrame.IndexOf(
                "m_ShadowPrimitiveDrawSetSystem.CompleteAndInvalidateAllBuilds()");
            int synchronizeScene = prepareFrame.IndexOf("m_PrimitiveSceneAdapter.Synchronize(");

            Assert.That(invalidateBuilds, Is.GreaterThanOrEqualTo(0));
            Assert.That(invalidateShadowBuilds, Is.GreaterThan(invalidateBuilds));
            Assert.That(synchronizeScene, Is.GreaterThan(invalidateBuilds));
            Assert.That(synchronizeScene, Is.GreaterThan(invalidateShadowBuilds));
        }

        [Test]
        public void ShadowDrawSetScheduling_UsesUnifiedCascadeMatricesAndShadowSemantics()
        {
            string source = ReadRuntimeSource(
                "Runtime",
                "SubSystem",
                "GPUDriven",
                "VividGPUDrivenSystem.cs");
            string schedule = SliceSource(
                source,
                "private VividPrimitiveDrawSet ScheduleShadowDrawSet(",
                "internal VividPrimitiveDrawSet CompleteShadowDrawSet(");

            StringAssert.Contains("PassRecorder.HasCascadedShadowCasterPass", schedule);
            StringAssert.Contains("shadowData.isCSMActive", schedule);
            StringAssert.Contains("shadowData.viewMatrices", schedule);
            StringAssert.Contains("shadowData.projMatrices", schedule);
            StringAssert.Contains("VividInstancePassMask.Shadows", schedule);
            StringAssert.Contains("cullAgainstNearPlane: false", schedule);
            StringAssert.Contains("m_ShadowPrimitiveDrawSetSystem.Schedule(", schedule);
            StringAssert.DoesNotContain("CompleteScheduledBuild", schedule);
        }

        [Test]
        public void GPUDrivenPreRender_SchedulesShadowDrawSetBeforePublishingFrameData()
        {
            string source = ReadRuntimeSource(
                "Runtime",
                "SubSystem",
                "GPUDriven",
                "VividGPUDrivenSystem.cs");
            string updateCore = SliceSource(
                source,
                "private static void UpdateCore(",
                "private static void PrepareFrameIfNeeded(");

            int schedule = updateCore.IndexOf("gpuDrivenSystem.ScheduleShadowDrawSet(");
            int mainViewCull = updateCore.IndexOf("gpuDrivenSystem.CullMainView(");
            int publishFrameData = updateCore.IndexOf("PassRecorder.SetGPUDrivenFrameData(");

            Assert.That(schedule, Is.GreaterThanOrEqualTo(0));
            Assert.That(mainViewCull, Is.GreaterThan(schedule));
            Assert.That(publishFrameData, Is.GreaterThan(mainViewCull));
            StringAssert.DoesNotContain("CompleteShadowDrawSet", updateCore);
        }

        [Test]
        public void CSMShadowRecord_CompletesDrawSetImmediatelyBeforeShadowGpuCull()
        {
            string source = ReadRuntimeSource(
                "Runtime",
                "RenderPass",
                "Core",
                "CSMShadowPass.cs");
            string prepareMeshletDraws = SliceSource(
                source,
                "private bool TryPrepareMeshletShadowDraws(",
                "private void DrawMeshletShadowCascade(");

            int buildContexts = prepareMeshletDraws.IndexOf("BuildShadowCullingContext(");
            int complete = prepareMeshletDraws.IndexOf("system.CompleteShadowDrawSet(");
            int gpuCull = prepareMeshletDraws.IndexOf("system.CullShadowCascades(");

            Assert.That(buildContexts, Is.GreaterThanOrEqualTo(0));
            Assert.That(complete, Is.GreaterThan(buildContexts));
            Assert.That(gpuCull, Is.GreaterThan(complete));
            StringAssert.Contains("m_PrimitiveShadowDrawSet", prepareMeshletDraws);
        }

        [Test]
        public void CSMShadowPass_DrawsUnityAndMeshletCastersWithOneMatrixArrayAndPerCascadeSlices()
        {
            string source = ReadRuntimeSource(
                "Runtime",
                "RenderPass",
                "Core",
                "CSMShadowPass.cs");
            string shaderVariables = ReadRuntimeSource(
                "Shaders",
                "Core",
                "Public",
                "ShaderVariablesGlobal.hlsl");
            string record = SliceSource(
                source,
                "public override void Record(",
                "private void PrepareMeshletRendering(");
            string drawMeshletShadowCascade = SliceSource(
                source,
                "private void DrawMeshletShadowCascade(",
                "private void BuildShadowCullingContext(");
            string buildCullingContext = SliceSource(
                source,
                "private void BuildShadowCullingContext(",
                "private static void ConfigureMaterial(");

            StringAssert.Contains("m_ShadowAtlas.desc.Dimension = TextureDimension.Tex2DArray", source);
            StringAssert.Contains("m_ShadowAtlas.desc.Slices = VividShadowData.MaxCascadeCount", source);
            StringAssert.Contains("ConstantBuffer.PushGlobal(", record);
            StringAssert.Contains("ShadowMatricesConstantBufferId", record);
            StringAssert.Contains("CBUFFER_START(ShaderVariablesShadowMatrices)", shaderVariables);
            StringAssert.Contains("float4x4 _VividShadowVP[4]", shaderVariables);
            StringAssert.DoesNotContain("SetGlobalMatrixArray", record);
            StringAssert.Contains("depthSlice: cascadeIndex", record);
            StringAssert.DoesNotContain("RenderBufferLoadAction.DontCare", record);
            StringAssert.Contains("SetGlobalInt(ShadowCascadeIndexId, cascadeIndex)", record);
            StringAssert.DoesNotContain("SetViewProjectionMatrices", record);
            StringAssert.DoesNotContain("SetViewport", record);
            StringAssert.DoesNotContain("EnableScissorRect", record);
            StringAssert.Contains("m_ShadowData.viewMatrices", buildCullingContext);
            StringAssert.Contains("m_ShadowData.projMatrices", buildCullingContext);
            StringAssert.Contains("nativeCmd.DrawRendererList(rendererList)", record);
            StringAssert.Contains("DrawMeshletShadowCascade(", record);
            StringAssert.Contains(
                "GPUDrivenVirtualTextureBindingUtility.BindSpaceProperties(",
                drawMeshletShadowCascade);
        }

        [Test]
        public void CSMShadowPass_SetsPerDrawStateWithoutReuploadingShadowMatrices()
        {
            string source = ReadRuntimeSource(
                "Runtime",
                "RenderPass",
                "Core",
                "CSMShadowPass.cs");
            string prepare = SliceSource(
                source,
                "private bool TryPrepareMeshletShadowDraws(",
                "private void DrawMeshletShadowCascade(");
            string draw = SliceSource(
                source,
                "private void DrawMeshletShadowCascade(",
                "private void BuildShadowCullingContext(");

            StringAssert.Contains(
                "m_DrawProperties.SetInteger(ShadowCascadeIndexId, cascadeIndex)",
                draw);
            StringAssert.DoesNotContain("ShadowMatricesConstantBufferId", draw);
            StringAssert.DoesNotContain("SetGlobalMatrixArray", draw);
            StringAssert.Contains("BindSpaceProperties", draw);
            StringAssert.Contains("VirtualTextureSpaceBinding", prepare);
        }

        [Test]
        public void CSMShadowPass_UsesPerCascadeSplitDataWithoutBatchShadowCulling()
        {
            string csmSource = ReadRuntimeSource(
                "Runtime",
                "RenderPass",
                "Core",
                "CSMShadowPass.cs");
            string recorderSource = ReadRuntimeSource(
                "Runtime",
                "RenderGraph",
                "PassRecorder.Execution.cs");
            string shadowDataSource = ReadRuntimeSource(
                "Runtime",
                "RenderGraph",
                "FrameContext",
                "VividShadowData.cs");

            StringAssert.Contains("settings.splitData = shadowData.splitData[i]", csmSource);
            StringAssert.Contains("settings.splitIndex = -1", csmSource);
            StringAssert.DoesNotContain("CullShadowCasters", recorderSource);
            StringAssert.DoesNotContain("CullShadowCasters", shadowDataSource);
        }

        [Test]
        public void VirtualShadowMapPrototype_UsesNoCachePageTableAndHardShadowResolve()
        {
            string passSource = ReadRuntimeSource(
                "Runtime",
                "RenderPass",
                "Core",
                "CSMShadowPass.cs");
            string resolvePassSource = ReadRuntimeSource(
                "Runtime",
                "RenderPass",
                "Core",
                "CSMShadowResolvePass.cs");
            string casterSource = ReadRuntimeSource(
                "Shaders",
                "Core",
                "Private",
                "GPUDriven",
                "VisibilityBufferShadowCasterPass.shader");
            string resolveSource = ReadRuntimeSource(
                "Shaders",
                "Core",
                "Private",
                "CSMShadowResolve.compute");

            StringAssert.Contains("GraphicsFormat.R32_UInt", passSource);
            StringAssert.Contains("GraphicsFormatUsage.Render", passSource);
            StringAssert.Contains("GraphicsFormatUsage.LoadStore", passSource);
            StringAssert.DoesNotContain(
                "GraphicsFormatUsage.Sample | GraphicsFormatUsage.LoadStore",
                passSource);
            StringAssert.Contains("nativeCmd.SetRandomWriteTarget(0, physicalPage)", passSource);
            StringAssert.Contains("nativeCmd.SetBufferData(", passSource);
            StringAssert.Contains("BuildFullyResidentPageTable(", passSource);
            StringAssert.Contains("TextureDimension.Tex2DArray", passSource);
            StringAssert.Contains("AccessFlags.Write", passSource);
            StringAssert.Contains("m_VirtualShadowMapPrototypeMaterials", passSource);
            StringAssert.Contains(
                "m_VirtualShadowMapPrototypeMaterials);",
                passSource);
            StringAssert.DoesNotContain(
                "SetVirtualShadowMapPrototypeKeyword(",
                passSource);
            StringAssert.Contains("#pragma require randomwrite", casterSource);
            StringAssert.Contains("RWTexture2D<uint> _VSMPrototypePhysicalPage : register(u0)", casterSource);
            StringAssert.Contains("StructuredBuffer<uint> _VSMPrototypePageTable", casterSource);
            StringAssert.Contains("encodedPhysicalPage - 1u", casterSource);
            StringAssert.Contains("InterlockedMax(", casterSource);
            StringAssert.Contains("AccessFlags.Read", resolvePassSource);
            StringAssert.Contains("Texture2D<uint> _VSMPrototypePhysicalPage", resolveSource);
            StringAssert.Contains("StructuredBuffer<uint> _VSMPrototypePageTable", resolveSource);
            StringAssert.Contains("TryResolveVSMPhysicalTexel(", resolveSource);
            StringAssert.Contains("return SampleCSMShadowMap(shadowUV, receiverDepth, cascadeIndex)", resolveSource);
            StringAssert.Contains("asfloat(", resolveSource);
            StringAssert.Contains("UseVirtualShadowMapPrototype(cascadeIndex)", resolveSource);
            StringAssert.Contains("EnsurePhysicalPageForBinding()", resolvePassSource);
            StringAssert.Contains("virtualShadowMapPage", resolvePassSource);
        }

        [Test]
        public void VirtualShadowMapPrototypeShaders_ImportWithoutErrors()
        {
            const string computeAssetPath =
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute";
            const string casterShaderName =
                "Hidden/VividRP/GPUDriven/VisibilityBufferShadowCasterPass";

            ComputeShader resolveCompute =
                AssetDatabase.LoadAssetAtPath<ComputeShader>(computeAssetPath);
            Assert.That(resolveCompute, Is.Not.Null, computeAssetPath);
            Assert.That(resolveCompute.HasKernel("CSMShadowResolve"), Is.True);

            ShaderMessage[] computeErrors = ShaderUtil
                .GetComputeShaderMessages(resolveCompute)
                .Where(message => message.severity.ToString() == "Error")
                .ToArray();
            Assert.That(
                computeErrors,
                Is.Empty,
                string.Join(
                    "\n",
                    computeErrors.Select(message =>
                        $"{message.file}:{message.line}: {message.message}")));

            Shader casterShader = Shader.Find(casterShaderName);
            Assert.That(casterShader, Is.Not.Null, casterShaderName);
            var casterMaterial = new Material(casterShader);
            try
            {
                casterMaterial.EnableKeyword("VIVID_VSM_PROTOTYPE");
                ShaderUtil.CompilePass(casterMaterial, 0);
                ShaderMessage[] casterErrors = ShaderUtil
                    .GetShaderMessages(casterShader)
                    .Where(message => message.severity.ToString() == "Error")
                    .ToArray();
                Assert.That(
                    casterErrors,
                    Is.Empty,
                    string.Join(
                        "\n",
                        casterErrors.Select(message =>
                            $"{message.file}:{message.line}: {message.message}")));
            }
            finally
            {
                Object.DestroyImmediate(casterMaterial);
            }
        }

        [Test]
        public void ScheduledDrawSetToken_RequiresExactPendingCameraFrameAndRevisionMatch()
        {
            string source = ReadRuntimeSource(
                "Runtime",
                "SubSystem",
                "GPUDriven",
                "VividGPUDrivenSystem.cs");
            string consumeToken = SliceSource(
                source,
                "private bool TryConsumeScheduledMainViewDrawSet(",
                "private void ClearScheduledMainViewDrawSet()");

            StringAssert.Contains(
                "m_ScheduledMainViewRenderingCameraId.Equals(renderingCamera.GetEntityId())",
                consumeToken);
            StringAssert.Contains(
                "m_ScheduledMainViewCullingCameraId.Equals(cullingCamera.GetEntityId())",
                consumeToken);
            StringAssert.Contains(
                "m_ScheduledMainViewFrameIndex == resolvedFrameIndex",
                consumeToken);
            StringAssert.Contains(
                "m_ScheduledMainViewSceneRevision == PrimitiveScene.SceneRevision",
                consumeToken);
            StringAssert.Contains("drawSet.MatchesPendingBuild(", consumeToken);
            Assert.That(
                consumeToken.IndexOf("ClearScheduledMainViewDrawSet();"),
                Is.GreaterThan(consumeToken.IndexOf("bool matches =")),
                "The pending token must be cleared after every consumption attempt.");
        }

        [Test]
        public void ScheduleCullForCamera_SkipsPreviewWithoutChangingGpuDrivenSystemLifetime()
        {
            var preview = new PreviewRenderUtility();
            bool hadInstance = VividGPUDrivenSystem.HasInstance;
            try
            {
                Assert.That(preview.camera.cameraType, Is.EqualTo(CameraType.Preview));
                Assert.That(
                    VividGPUDrivenSystem.ScheduleCullForCamera(preview.camera, frameIndex: 3),
                    Is.False);
                Assert.That(VividGPUDrivenSystem.HasInstance, Is.EqualTo(hadInstance));
            }
            finally
            {
                preview.Cleanup();
            }
        }

        [Test]
        public void FrameContextClear_KeepsGPUDrivenPreRenderCallbackRegistered_InEditor()
        {
            VividGPUDrivenSystem.Initialize();

            FrameContextSystem.Clear();

            Assert.That(
                HasFrameContextSubscriber(
                    "SubsystemPreRender",
                    typeof(VividSubsystem<VividGPUDrivenSystem>),
                    "DispatchUpdate"),
                Is.True);
        }

        [Test]
        public void FrameContextClear_DoesNotRegisterLegacyGpuDrivenDebugOverlayCallback_InEditor()
        {
            VividGPUDrivenSystem.Initialize();

            FrameContextSystem.Clear();

            Assert.That(
                HasFrameContextSubscriber("SubsystemPostRender", typeof(VividGPUDrivenSystem), "RenderDebugOverlay"),
                Is.False);
        }

        private static bool HasFrameContextSubscriber(string eventName, Type declaringType, string methodName)
        {
            FieldInfo eventField = typeof(FrameContextSystem).GetField(
                eventName,
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(eventField, Is.Not.Null);

            var multicastDelegate = eventField.GetValue(null) as MulticastDelegate;
            return multicastDelegate != null
                && multicastDelegate.GetInvocationList().Any(
                    callback => callback.Method.DeclaringType == declaringType
                        && callback.Method.Name == methodName);
        }

        private static string ReadRuntimeSource(params string[] relativeSegments)
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VividRenderPipelineAsset).Assembly);
            Assert.That(package, Is.Not.Null);

            string path = package.resolvedPath;
            for (int index = 0; index < relativeSegments.Length; index++)
                path = Path.Combine(path, relativeSegments[index]);

            Assert.That(File.Exists(path), Is.True, path);
            return File.ReadAllText(path);
        }

        private static string SliceSource(string source, string startMarker, string endMarker)
        {
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), startMarker);
            int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start), endMarker);
            return source.Substring(start, end - start);
        }
    }
}
