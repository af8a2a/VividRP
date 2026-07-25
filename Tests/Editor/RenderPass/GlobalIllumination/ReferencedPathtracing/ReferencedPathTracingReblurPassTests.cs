using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
    public sealed class ReferencedPathTracingReblurPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredReblurPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(ReferencedPathTracingReblurPass);
        }

        [Test]
        public void Pass_PreservesReblurHistoriesWithoutSameFrameConsumer()
        {
            Assert.That(
                typeof(IRenderGraphSideEffectPass).IsAssignableFrom(
                    typeof(ReferencedPathTracingReblurPass)),
                Is.True);
        }

        [Test]
        public void MainLightSolidAngleClassification_MatchesShaderDeltaCutoff()
        {
            Assert.That(
                ReferencedPathTracingLightSignatureUtility
                    .HasFiniteMainLightSolidAngle(0.0f),
                Is.False);
            Assert.That(
                ReferencedPathTracingLightSignatureUtility
                    .HasFiniteMainLightSolidAngle(1e-6f),
                Is.False);
            Assert.That(
                ReferencedPathTracingLightSignatureUtility
                    .HasFiniteMainLightSolidAngle(3e-6f),
                Is.True);
            Assert.That(
                ReferencedPathTracingLightSignatureUtility
                    .HasFiniteMainLightSolidAngle(
                        0.53f * Mathf.Deg2Rad),
                Is.True);
        }

        [Test]
        public void ReblurRouting_ConsumesPathTracerProducerHandshake()
        {
            var passSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathTracingPass.cs"));
            var reblurSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathTracingReblurPass.cs"));

            Assert.That(
                passSource,
                Does.Contain(
                    "pathTracingData.mainLightInDenoiserSignals"));
            Assert.That(
                reblurSource,
                Does.Contain(
                    "pathTracingData.mainLightInDenoiserSignals"));
            Assert.That(
                reblurSource,
                Does.Not.Contain(
                    "out var mainLightAngularDiameter"));
        }

        [Test]
        public void SignalEncoding_UsesLinearRgbAcrossProducerDenoiserAndResolve()
        {
            var encodingSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "NRD",
                "REBLUR",
                "VividReblurSignalEncoding.hlsli"));
            var configSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "NRD",
                "REBLUR",
                "Private",
                "REBLUR_Config.hlsli"));
            var rayGenerationSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "ReferencedPathtracing",
                "ReferencedPathtracing.rgen.hlsl"));
            var resolveSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "NRD",
                "REBLUR",
                "REBLUR_DiffuseSpecular_Resolve.compute"));

            Assert.That(
                encodingSource,
                Does.Contain("#define VIVID_REBLUR_SIGNAL_USE_YCOCG 0"));
            Assert.That(
                configSource,
                Does.Contain(
                    "#define REBLUR_USE_YCOCG"));
            Assert.That(
                configSource,
                Does.Contain("VIVID_REBLUR_SIGNAL_USE_YCOCG"));
            Assert.That(
                rayGenerationSource,
                Does.Contain("VividReblurEncodeRadiance(radiance)"));
            Assert.That(
                resolveSource,
                Does.Contain("VividReblurDecodeRadiance(diffuse.xyz)"));
            Assert.That(
                resolveSource,
                Does.Contain("VividReblurEncodeRadiance(diffuseRadiance)"));
        }

        [Test]
        public void CameraState_InvalidatesHistoryWhenPathTracingSignatureChanges()
        {
            var cameraObject = new GameObject(
                "ReferencedPathTracingReblurPassTests.SignatureCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var pass = new ReferencedPathTracingReblurPass();
            var settings = ReferencedPathTracingReblurSettings.CreateDefault();
            var pathTracingData = new VividReferencedPathTracingData
            {
                isValid = true,
                frameSignature = 17ul
            };
            var updateMethod =
                typeof(ReferencedPathTracingReblurPass).GetMethod(
                    "UpdateCameraSettings",
                    BindingFlags.Instance | BindingFlags.NonPublic);

            try
            {
                Assert.That(updateMethod, Is.Not.Null);
                Assert.That(
                    (bool)updateMethod.Invoke(
                        pass,
                        new object[]
                        {
                            camera,
                            settings,
                            pathTracingData
                        }),
                    Is.False);
                Assert.That(
                    (bool)updateMethod.Invoke(
                        pass,
                        new object[]
                        {
                            camera,
                            settings,
                            pathTracingData
                        }),
                    Is.True);

                pathTracingData.frameSignature = 18ul;
                Assert.That(
                    (bool)updateMethod.Invoke(
                        pass,
                        new object[]
                        {
                            camera,
                            settings,
                            pathTracingData
                        }),
                    Is.False);
            }
            finally
            {
                pass.Dispose();
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Initialize_DefinesDiffuseSpecularSignalsGuidesAndResolvedOutput()
        {
            IRenderPass renderPass = new ReferencedPathTracingReblurPass();

            var resources = renderPass.Initialize();

            AssertRead(resources, "DiffuseRadianceHitDistance", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(resources, "SpecularRadianceHitDistance", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(resources, "PathTracingDirectLighting", GraphicsFormat.R32G32B32A32_SFloat);
            AssertRead(resources, "PathTracingEmission", GraphicsFormat.R32G32B32A32_SFloat);
            AssertRead(resources, "NrdDiffuseMaterialFactor", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(resources, "NrdSpecularMaterialFactor", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(resources, "NrdViewZ", GraphicsFormat.R32_SFloat);
            AssertRead(resources, "MotionVectors", GraphicsFormat.R16G16B16A16_SFloat);
            AssertRead(
                resources,
                "NrdNormalRoughness",
                GraphicsFormat.A2B10G10R10_UNormPack32);

            AssertWrite(resources, "DenoisedDiffuseRadianceHitDistance", GraphicsFormat.R16G16B16A16_SFloat);
            AssertWrite(resources, "DenoisedSpecularRadianceHitDistance", GraphicsFormat.R16G16B16A16_SFloat);
            AssertWrite(resources, "ReblurResolvedColor", GraphicsFormat.R32G32B32A32_SFloat);
            Assert.That(
                resources.Textures.Any(resource =>
                    resource.Name.StartsWith("ReblurPrevious", StringComparison.Ordinal)
                    || resource.Name.StartsWith("ReblurCurrent", StringComparison.Ordinal)),
                Is.False);
        }

        [Test]
        public void SharedConstants_MatchOfficialReblurConstantBufferLayout()
        {
            Assert.That(Marshal.SizeOf<ReblurSharedConstants>(), Is.EqualTo(848));
        }

        [Test]
        public void Volume_DefaultsMatchNrdReblurDefaults()
        {
            var volume = ScriptableObject.CreateInstance<ReferencedPathTracingReblurVolume>();

            try
            {
                Assert.That(volume.enabled.value, Is.True);
                Assert.That(volume.maxAccumulatedFrameNum.value, Is.EqualTo(30));
                Assert.That(volume.maxFastAccumulatedFrameNum.value, Is.EqualTo(6));
                Assert.That(
                    volume.maxStabilizedFrameNum.value,
                    Is.EqualTo(ReferencedPathTracingReblurVolume.MaxHistoryFrameNum));
                Assert.That(volume.historyFixFrameNum.value, Is.EqualTo(3));
                Assert.That(volume.diffusePrepassBlurRadius.value, Is.EqualTo(30.0f));
                Assert.That(volume.specularPrepassBlurRadius.value, Is.EqualTo(50.0f));
                Assert.That(
                    volume.checkerboardMode.value,
                    Is.EqualTo(ReferencedPathTracingReblurCheckerboardMode.Off));
                Assert.That(
                    volume.hitDistanceReconstructionMode.value,
                    Is.EqualTo(
                        ReferencedPathTracingReblurHitDistanceReconstructionMode.Off));
                Assert.That(volume.minBlurRadius.value, Is.EqualTo(1.0f));
                Assert.That(volume.maxBlurRadius.value, Is.EqualTo(30.0f));
                Assert.That(volume.lobeAngleFraction.value, Is.EqualTo(0.15f));
                Assert.That(volume.roughnessFraction.value, Is.EqualTo(0.15f));
                Assert.That(volume.hitDistanceA.value, Is.EqualTo(3.0f));
                Assert.That(volume.hitDistanceB.value, Is.EqualTo(0.1f));
                Assert.That(volume.hitDistanceC.value, Is.EqualTo(20.0f));
                Assert.That(volume.hitDistanceD.value, Is.EqualTo(-25.0f));
                Assert.That(volume.IsActive(), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void SettingsResolver_UsesVolumeOverridesAndMaintainsNrdConstraints()
        {
            var cameraObject = new GameObject("ReferencedPathTracingReblurVolumeTests.Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var volume = profile.Add<ReferencedPathTracingReblurVolume>(true);
            volume.maxAccumulatedFrameNum.value = 12;
            volume.maxFastAccumulatedFrameNum.value = 1;
            volume.maxStabilizedFrameNum.value = 20;
            volume.historyFixFrameNum.value = 3;
            volume.responsiveAccumulationMinFrameNum.value = 3;
            volume.minBlurRadius.value = 5.0f;
            volume.maxBlurRadius.value = 2.0f;
            volume.hitDistanceA.value = 4.0f;
            volume.hitDistanceB.value = 0.2f;
            volume.hitDistanceC.value = 10.0f;
            volume.hitDistanceD.value = -12.0f;
            volume.enableAntiFirefly.value = true;
            volume.hitDistanceReconstructionMode.value =
                ReferencedPathTracingReblurHitDistanceReconstructionMode.Area5x5;
            volume.checkerboardMode.value =
                ReferencedPathTracingReblurCheckerboardMode.White;

            try
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var settings = ReferencedPathTracingReblurSettingsResolver.Resolve();

                Assert.That(settings.maxAccumulatedFrameNum, Is.EqualTo(12));
                Assert.That(settings.maxFastAccumulatedFrameNum, Is.EqualTo(1));
                Assert.That(settings.maxStabilizedFrameNum, Is.EqualTo(12));
                Assert.That(settings.historyFixFrameNum, Is.Zero);
                Assert.That(settings.responsiveAccumulationMinFrameNum, Is.Zero);
                Assert.That(settings.minBlurRadius, Is.EqualTo(2.0f));
                Assert.That(settings.maxBlurRadius, Is.EqualTo(2.0f));
                Assert.That(
                    settings.hitDistanceParameters,
                    Is.EqualTo(new Vector4(4.0f, 0.2f, 10.0f, -12.0f)));
                Assert.That(settings.enableAntiFirefly, Is.True);
                Assert.That(
                    settings.hitDistanceReconstructionMode,
                    Is.EqualTo(
                        ReferencedPathTracingReblurHitDistanceReconstructionMode.Area5x5));
                Assert.That(
                    settings.checkerboardMode,
                    Is.EqualTo(ReferencedPathTracingReblurCheckerboardMode.White));

                var pass = new ReferencedPathTracingPass();
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.SetCamera(camera);
                pass.Prepare(frameData);
                var hitDistanceField = typeof(ReferencedPathTracingPass).GetField(
                    "m_ReblurHitDistanceParameters",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(hitDistanceField, Is.Not.Null);
                Assert.That(
                    (Vector4)hitDistanceField.GetValue(pass),
                    Is.EqualTo(settings.hitDistanceParameters));
                var checkerboardField = typeof(ReferencedPathTracingPass).GetField(
                    "m_ReblurCheckerboardMode",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(checkerboardField, Is.Not.Null);
                Assert.That(
                    (ReferencedPathTracingReblurCheckerboardMode)checkerboardField.GetValue(pass),
                    Is.EqualTo(ReferencedPathTracingReblurCheckerboardMode.White));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void SharedConstants_UseResolvedReblurSettingsAndResetHistory()
        {
            var settings = ReferencedPathTracingReblurSettings.CreateDefault();
            settings.hitDistanceParameters = new Vector4(4.0f, 0.2f, 10.0f, -12.0f);
            settings.antilagParameters = new Vector2(2.0f, 1.5f);
            settings.maxAccumulatedFrameNum = 12;
            settings.maxFastAccumulatedFrameNum = 4;
            settings.maxStabilizedFrameNum = 8;
            settings.maxBlurRadius = 5.0f;
            settings.fastHistoryClampingSigmaScale = 1.5f;
            settings.lobeAngleFraction = 0.5f;
            settings.enableAntiFirefly = true;
            settings.usePrepassOnlyForSpecularMotionEstimation = true;
            settings.responsiveAccumulationRoughnessThreshold = 0.25f;
            settings.responsiveAccumulationMinFrameNum = 2;
            settings.returnHistoryLengthInsteadOfOcclusion = true;
            settings.checkerboardMode = ReferencedPathTracingReblurCheckerboardMode.Black;

            var constants = ReblurSharedConstants.Compute(
                null,
                null,
                320,
                180,
                true,
                settings);

            Assert.That(constants.gHitDistParams, Is.EqualTo(settings.hitDistanceParameters));
            Assert.That(constants.gAntilagParams, Is.EqualTo(settings.antilagParameters));
            Assert.That(constants.gMaxAccumulatedFrameNum, Is.EqualTo(12.0f));
            Assert.That(constants.gMaxFastAccumulatedFrameNum, Is.EqualTo(4.0f));
            Assert.That(constants.gStabilizationStrength, Is.EqualTo(8.0f / 9.0f));
            Assert.That(constants.gFastHistoryClampingSigmaScale, Is.EqualTo(1.5f));
            Assert.That(constants.gLobeAngleFraction, Is.EqualTo(0.25f));
            Assert.That(constants.gAntiFirefly, Is.EqualTo(1.0f));
            Assert.That(constants.gUsePrepassNotOnlyForSpecularMotionEstimation, Is.Zero);
            Assert.That(constants.gResponsiveAccumulationInvRoughnessThreshold, Is.EqualTo(4.0f));
            Assert.That(constants.gResponsiveAccumulationMinAccumulatedFrameNum, Is.EqualTo(2u));
            Assert.That(constants.gReturnHistoryLengthInsteadOfOcclusion, Is.EqualTo(1u));
            Assert.That(constants.gDiffCheckerboard, Is.EqualTo(0u));
            Assert.That(constants.gSpecCheckerboard, Is.EqualTo(1u));

            constants = ReblurSharedConstants.Compute(
                null,
                null,
                320,
                180,
                false,
                settings);
            Assert.That(constants.gMaxAccumulatedFrameNum, Is.Zero);
            Assert.That(constants.gMaxFastAccumulatedFrameNum, Is.Zero);
            Assert.That(constants.gStabilizationStrength, Is.Zero);
            Assert.That(constants.gResetHistory, Is.EqualTo(1u));
        }

        [Test]
        public void SettingsEquality_ChangesWhenTemporalStabilizationWindowChanges()
        {
            var settings = ReferencedPathTracingReblurSettings.CreateDefault();
            var changedSettings = settings;
            changedSettings.maxStabilizedFrameNum = 0;

            Assert.That(settings.Equals(changedSettings), Is.False);
        }

        [Test]
        public void SettingsEquality_ChangesWhenHitDistanceReconstructionModeChanges()
        {
            var settings = ReferencedPathTracingReblurSettings.CreateDefault();
            var reconstructedSettings = settings;
            reconstructedSettings.hitDistanceReconstructionMode =
                ReferencedPathTracingReblurHitDistanceReconstructionMode.Area3x3;

            Assert.That(settings.Equals(reconstructedSettings), Is.False);
        }

        [Test]
        public void SettingsEquality_ChangesWhenCheckerboardModeChanges()
        {
            var settings = ReferencedPathTracingReblurSettings.CreateDefault();
            var checkerboardSettings = settings;
            checkerboardSettings.checkerboardMode =
                ReferencedPathTracingReblurCheckerboardMode.Black;

            Assert.That(settings.Equals(checkerboardSettings), Is.False);
        }

        [Test]
        public void SharedConstants_DisableCheckerboardWhenReblurIsDisabled()
        {
            var settings = ReferencedPathTracingReblurSettings.CreateDefault();
            settings.enabled = false;
            settings.checkerboardMode = ReferencedPathTracingReblurCheckerboardMode.White;

            var constants = ReblurSharedConstants.Compute(
                null,
                null,
                320,
                180,
                false,
                settings);

            Assert.That(constants.gDiffCheckerboard, Is.EqualTo(2u));
            Assert.That(constants.gSpecCheckerboard, Is.EqualTo(2u));
        }

        [Test]
        public void Checkerboard_SkipsHitDistanceReconstructionDependency()
        {
            var pass = new ReferencedPathTracingReblurPass();
            var settings = ReferencedPathTracingReblurSettings.CreateDefault();
            settings.checkerboardMode = ReferencedPathTracingReblurCheckerboardMode.White;
            settings.hitDistanceReconstructionMode =
                ReferencedPathTracingReblurHitDistanceReconstructionMode.Area5x5;

            var settingsField = typeof(ReferencedPathTracingReblurPass).GetField(
                "m_Settings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var canExecuteMethod = typeof(ReferencedPathTracingReblurPass).GetMethod(
                "CanExecuteHitDistanceReconstruction",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var getShaderMethod = typeof(ReferencedPathTracingReblurPass).GetMethod(
                "GetHitDistanceReconstructionShader",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(settingsField, Is.Not.Null);
            Assert.That(canExecuteMethod, Is.Not.Null);
            Assert.That(getShaderMethod, Is.Not.Null);
            settingsField.SetValue(pass, settings);

            Assert.That((bool)canExecuteMethod.Invoke(pass, null), Is.True);
            Assert.That(getShaderMethod.Invoke(pass, null), Is.Null);
        }

        [Test]
        public void RenderGraphNode_ExposesExternalSignalsAndKeepsTransientsInternal()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredReblurPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_DiffuseInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_SpecularInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DirectLightingInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_EmissionInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DiffuseMaterialFactorInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_SpecularMaterialFactorInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ViewZInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_MotionVectorsInput"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_NormalRoughnessInput"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DiffuseOutput"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_SpecularOutput"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ResolvedColor"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_Tiles"), Is.Null);
                Assert.That(node.GetOutputPortByName("m_Tiles"), Is.Null);
                Assert.That(node.GetInputPortByName("m_PreparedDiffuse"), Is.Null);
                Assert.That(node.GetOutputPortByName("m_PreparedDiffuse"), Is.Null);
                Assert.That(
                    node.GetInputPortByName("m_TemporalStabilizationMotionVectors"),
                    Is.Null);
                Assert.That(
                    node.GetOutputPortByName("m_TemporalStabilizationMotionVectors"),
                    Is.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        private static void AssertRead(
            PassResource resources,
            string name,
            GraphicsFormat format)
        {
            var resource = resources.Textures.Single(texture => texture.Name == name);
            Assert.That(resource.Access, Is.EqualTo(AccessFlags.Read), name);
            Assert.That(resource.Texture.desc.ColorFormat, Is.EqualTo(format), name);
        }

        private static void AssertWrite(
            PassResource resources,
            string name,
            GraphicsFormat format)
        {
            var resource = resources.Textures.Single(texture => texture.Name == name);
            Assert.That(resource.Access, Is.EqualTo(AccessFlags.Write), name);
            Assert.That(resource.Texture.desc.ColorFormat, Is.EqualTo(format), name);
            Assert.That(resource.Texture.desc.EnableRandomWrite, Is.True, name);
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(ReferencedPathTracingReblurPass).Assembly);

            Assert.That(packageInfo, Is.Not.Null);
            return Path.Combine(packageInfo.resolvedPath, Path.Combine(relativeParts));
        }
    }
}
