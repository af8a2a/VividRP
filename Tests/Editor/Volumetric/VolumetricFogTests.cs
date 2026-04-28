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
using ResourcePathAttribute = VividRP.Runtime.ResourcePathAttribute;

namespace VividRP.Editor.Tests
{
    public sealed class VolumetricFogTests
    {
        [Test]
        public void VividVolumetricFogVolume_IsActive_ReturnsEnabledState()
        {
            var fog = ScriptableObject.CreateInstance<VividVolumetricFogVolume>();

            try
            {
                fog.enabled.value = false;
                Assert.That(fog.IsActive(), Is.False);

                fog.enabled.value = true;
                Assert.That(fog.IsActive(), Is.True);

                fog.meanFreePath.value = 0.0f;
                Assert.That(fog.IsActive(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(fog);
            }
        }

        [Test]
        public void ResolveQuality_UsesManualResolutionAndSlices_WhenManual()
        {
            var fog = ScriptableObject.CreateInstance<VividVolumetricFogVolume>();

            try
            {
                fog.fogControlMode.value = VividVolumetricFogControlMode.Manual;
                fog.screenResolutionPercentage.value = 25.0f;
                fog.volumeSliceCount.value = 32;

                VividVolumetricUtility.ResolveQuality(fog, out var screenPercentage, out var sliceCount);

                Assert.That(screenPercentage, Is.EqualTo(25.0f).Within(0.0001f));
                Assert.That(sliceCount, Is.EqualTo(32));
            }
            finally
            {
                Object.DestroyImmediate(fog);
            }
        }

        [Test]
        public void ResolveQuality_UsesHdrpBalanceFormula_WhenBalance()
        {
            var fog = ScriptableObject.CreateInstance<VividVolumetricFogVolume>();

            try
            {
                fog.fogControlMode.value = VividVolumetricFogControlMode.Balance;
                fog.volumetricFogBudget.value = 0.25f;
                fog.resolutionDepthRatio.value = 0.5f;

                VividVolumetricUtility.ResolveQuality(fog, out var screenPercentage, out var sliceCount);

                Assert.That(screenPercentage, Is.EqualTo(11.71875f).Within(0.0001f));
                Assert.That(sliceCount, Is.EqualTo(64));
            }
            finally
            {
                Object.DestroyImmediate(fog);
            }
        }

        [Test]
        public void ComputeVBufferParameters_UsesHdrpDefaultFractionAndEncodesDepth()
        {
            var parameters = VividVolumetricUtility.ComputeVBufferParameters(
                1920,
                1080,
                VividVolumetricFogVolume.DefaultScreenResolutionPercentage,
                64,
                100.0f,
                0.5f);

            Assert.That(parameters.ViewportWidth, Is.EqualTo(240));
            Assert.That(parameters.ViewportHeight, Is.EqualTo(135));
            Assert.That(parameters.SliceCount, Is.EqualTo(64));
            Assert.That(parameters.DepthEncodingParams.z, Is.EqualTo(-0.7f).Within(0.0001f));
            Assert.That(parameters.DepthDecodingParams.x, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(parameters.DecodeLogarithmicDepth(parameters.EncodeLogarithmicDepth(10.0f)), Is.EqualTo(10.0f).Within(0.0001f));
            Assert.That(parameters.ComputeSliceLength(63), Is.GreaterThan(parameters.ComputeSliceLength(0)));
            Assert.That(parameters.LastSliceDistance, Is.GreaterThan(parameters.NearClipPlane));
            Assert.That(parameters.LastSliceDistance, Is.LessThan(parameters.NearClipPlane + parameters.DepthExtent));
        }

        [Test]
        public void BuildShaderVariables_EncodesHdrpVBufferGeometry()
        {
            var gameObject = new GameObject("Volumetric Camera");
            var camera = gameObject.AddComponent<Camera>();
            camera.fieldOfView = 60.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000.0f;
            var cameraData = new VividCameraData
            {
                camera = camera,
                actualWidth = 1920,
                actualHeight = 1080,
                pixelWidth = 1920,
                pixelHeight = 1080
            };

            try
            {
                var settings = VividVolumetricFogSettings.Disabled(1920, 1080);
                var shaderVariables = VividVolumetricUtility.BuildShaderVariables(settings, 1920, 1080, 0, cameraData);
                var vBuffer = settings.VBufferParameters;

                Assert.That(shaderVariables._VBufferDepthEncodingParams, Is.EqualTo(vBuffer.DepthEncodingParams));
                Assert.That(shaderVariables._VBufferDepthDecodingParams, Is.EqualTo(vBuffer.DepthDecodingParams));
                Assert.That(shaderVariables._VBufferGeometryParams.x, Is.EqualTo(vBuffer.UnitDepthTexelSpacing).Within(0.0001f));
                Assert.That(shaderVariables._VBufferGeometryParams.z, Is.EqualTo(vBuffer.LastSliceDistance).Within(0.0001f));
                Assert.That(shaderVariables._VBufferGeometryParams.w, Is.EqualTo(0.0f).Within(0.0001f));
                Assert.That(shaderVariables._VBufferLocalFogParams.z, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(shaderVariables._VBufferCoordToViewDirWS.m00, Is.Not.EqualTo(0.0f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void BuildShaderVariables_EncodesHdrpHeightFogDensityModel()
        {
            var vBuffer = VividVolumetricUtility.ComputeVBufferParameters(
                1920,
                1080,
                VividVolumetricFogVolume.DefaultScreenResolutionPercentage,
                64,
                100.0f,
                0.5f);
            var settings = new VividVolumetricFogSettings(
                true,
                Vector3.one,
                1.0f,
                10.0f,
                60.0f,
                0.25f,
                1.0f,
                100.0f,
                0.5f,
                VividVolumetricFogDenoisingMode.None,
                false,
                0.0f,
                vBuffer);

            var shaderVariables = VividVolumetricUtility.BuildShaderVariables(settings, 1920, 1080, 0);
            var scaleHeight = VividVolumetricUtility.ComputeHeightFogScaleHeight(10.0f, 60.0f);

            Assert.That(scaleHeight, Is.EqualTo(50.0f * VividVolumetricUtility.HeightFogScaleHeightFromLayerDepth).Within(0.0001f));
            Assert.That(VividVolumetricUtility.ComputeHeightFogMultiplier(5.0f, 10.0f, 60.0f), Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(VividVolumetricUtility.ComputeHeightFogMultiplier(10.0f, 10.0f, 60.0f), Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(VividVolumetricUtility.ComputeHeightFogMultiplier(60.0f, 10.0f, 60.0f), Is.EqualTo(0.001f).Within(0.0001f));
            Assert.That(shaderVariables._VBufferFogHeightParams.x, Is.EqualTo(10.0f).Within(0.0001f));
            Assert.That(shaderVariables._VBufferFogHeightParams.y, Is.EqualTo(60.0f).Within(0.0001f));
            Assert.That(shaderVariables._VBufferFogHeightParams.z, Is.EqualTo(1.0f / scaleHeight).Within(0.0001f));
            Assert.That(shaderVariables._VBufferFogHeightParams.w, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void BuildShaderVariables_ClampsDensityCutoffToNonNegative()
        {
            var vBuffer = VividVolumetricUtility.ComputeVBufferParameters(
                1920,
                1080,
                VividVolumetricFogVolume.DefaultScreenResolutionPercentage,
                64,
                100.0f,
                0.5f);
            var settings = new VividVolumetricFogSettings(
                true,
                Vector3.one,
                1.0f,
                0.0f,
                50.0f,
                0.0f,
                1.0f,
                100.0f,
                0.5f,
                VividVolumetricFogDenoisingMode.None,
                false,
                -1.0f,
                vBuffer);

            var shaderVariables = VividVolumetricUtility.BuildShaderVariables(settings, 1920, 1080, 0);

            Assert.That(shaderVariables._VBufferFogControlParams.z, Is.EqualTo(0.0f).Within(0.0001f));
        }

        [Test]
        public void ComputeMaxZDilationRadius_UsesHdrpScreenRatioThresholds()
        {
            Assert.That(VividVolumetricUtility.ComputeMaxZDilationRadius(6.25f), Is.EqualTo(2));
            Assert.That(VividVolumetricUtility.ComputeMaxZDilationRadius(12.5f), Is.EqualTo(1));
            Assert.That(VividVolumetricUtility.ComputeMaxZDilationRadius(50.0f), Is.EqualTo(0));
        }

        [Test]
        public void LocalVolumetricFog_ConvertToEngineData_EncodesScatteringAndFade()
        {
            var gameObject = new GameObject("Local Volumetric Fog");
            var fog = gameObject.AddComponent<VividLocalVolumetricFog>();
            SetLocalFogBoundProxy(fog, new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                size = new Vector3(10.0f, 20.0f, 40.0f)
            });

            try
            {
                var parameters = VividLocalVolumetricFogArtistParameters.CreateDefault();
                parameters.albedo = new Color(0.5f, 0.25f, 0.125f);
                parameters.meanFreePath = 10.0f;
                parameters.positiveFade = new Vector3(0.1f, 0.2f, 0.4f);
                parameters.negativeFade = new Vector3(0.2f, 0.4f, 0.8f);
                fog.parameters = parameters;

                var data = fog.ConvertToEngineData(null);

                Assert.That(data.scatteringExtinction.w, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(data.scatteringExtinction.x, Is.EqualTo(0.05f).Within(0.0001f));
                Assert.That(data.positiveFade.x, Is.EqualTo(10.0f).Within(0.0001f));
                Assert.That(data.negativeFade.z, Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(data.distanceFade.x, Is.GreaterThan(0.0f));
                Assert.That(data.parameters.y, Is.EqualTo((float)VividLocalVolumetricFogBlendingMode.Additive));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LocalVolumetricFog_EnumsMatchHdrpBlendOrdering()
        {
            Assert.That((int)VividLocalVolumetricFogBlendingMode.Overwrite, Is.EqualTo(0));
            Assert.That((int)VividLocalVolumetricFogBlendingMode.Additive, Is.EqualTo(1));
            Assert.That((int)VividLocalVolumetricFogBlendingMode.Multiply, Is.EqualTo(2));
            Assert.That((int)VividLocalVolumetricFogBlendingMode.Min, Is.EqualTo(3));
            Assert.That((int)VividLocalVolumetricFogBlendingMode.Max, Is.EqualTo(4));
        }

        [Test]
        public void LocalVolumetricFog_DefaultsMatchHdrpArtistParameters()
        {
            var parameters = VividLocalVolumetricFogArtistParameters.CreateDefault();

            Assert.That(parameters.blendingMode, Is.EqualTo(VividLocalVolumetricFogBlendingMode.Additive));
            Assert.That(parameters.maskMode, Is.EqualTo(VividLocalVolumetricFogMaskMode.Texture));
            Assert.That(parameters.positiveFade, Is.EqualTo(Vector3.one * 0.1f));
            Assert.That(parameters.negativeFade, Is.EqualTo(Vector3.one * 0.1f));
            Assert.That(parameters.distanceFadeStart, Is.EqualTo(10000.0f));
            Assert.That(parameters.distanceFadeEnd, Is.EqualTo(10000.0f));
        }

        [Test]
        public void LocalVolumetricFog_TryCreateBoundProxyWorldData_UsesLocalVolumetricFogFeature()
        {
            var gameObject = new GameObject("Local Volumetric Fog Bounds");
            gameObject.transform.position = new Vector3(3.0f, 4.0f, 5.0f);
            gameObject.transform.rotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
            var fog = gameObject.AddComponent<VividLocalVolumetricFog>();
            var shape = new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                center = new Vector3(1.0f, 2.0f, 3.0f),
                size = new Vector3(10.0f, 20.0f, 40.0f),
                radius = 8.0f
            };
            SetLocalFogBoundProxy(fog, shape);

            try
            {
                bool created = fog.TryCreateBoundProxyWorldData(out BoundProxyWorldData worldData);

                Assert.That(created, Is.True);
                Assert.That(worldData.feature, Is.EqualTo(BoundProxyFeature.LocalVolumetricFog));
                Assert.That(worldData.shape, Is.EqualTo(BoundProxyShapeType.Box));
                AssertVector3(worldData.worldCenter, gameObject.transform.position + gameObject.transform.rotation * shape.center);
                AssertVector3(worldData.boxSize, shape.size);
                Assert.That(worldData.sphereRadius, Is.EqualTo(0.0f));
                Assert.That(worldData.worldAabb.size.x, Is.GreaterThan(0.0f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LocalVolumetricFog_GetBounds_UsesBoundProxyWorldAabb()
        {
            var gameObject = new GameObject("Local Volumetric Fog Bounds");
            gameObject.transform.position = new Vector3(3.0f, 4.0f, 5.0f);
            gameObject.transform.rotation = Quaternion.Euler(0.0f, 45.0f, 0.0f);
            var fog = gameObject.AddComponent<VividLocalVolumetricFog>();
            var shape = new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                center = new Vector3(0.0f, 1.0f, 0.0f),
                size = new Vector3(2.0f, 4.0f, 6.0f)
            };
            SetLocalFogBoundProxy(fog, shape);

            try
            {
                Bounds expected = BoundProxyUtility.CalculateWorldAabb(gameObject.transform, shape);

                Bounds actual = fog.GetBounds();

                AssertVector3(actual.center, expected.center);
                AssertVector3(actual.size, expected.size);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LocalVolumetricFog_ConvertToVolumeBounds_UsesHdrpOrientedBoxPacking()
        {
            var gameObject = new GameObject("Local Volumetric Fog Volume Bounds");
            gameObject.transform.position = new Vector3(1.0f, 2.0f, 3.0f);
            gameObject.transform.rotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
            var fog = gameObject.AddComponent<VividLocalVolumetricFog>();
            SetLocalFogBoundProxy(fog, new BoundProxyShape
            {
                shape = BoundProxyShapeType.Box,
                center = new Vector3(0.0f, 1.0f, 0.0f),
                size = new Vector3(2.0f, 4.0f, 6.0f)
            });

            try
            {
                var bounds = fog.ConvertToVolumeBounds();

                Assert.That(VividVolumetricMaterialBounds.Stride, Is.EqualTo(48));
                Assert.That(VividVolumetricMaterialRenderingData.Stride, Is.EqualTo(160));
                AssertVector3(bounds.center, gameObject.transform.position + gameObject.transform.rotation * new Vector3(0.0f, 1.0f, 0.0f));
                AssertVector3(new Vector3(bounds.extentX, bounds.extentY, bounds.extentZ), new Vector3(1.0f, 2.0f, 3.0f));
                Assert.That(bounds.right.magnitude, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(bounds.up.magnitude, Is.EqualTo(1.0f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void VolumetricDensityPass_InitializesStableResources()
        {
            IRenderPass renderPass = new VolumetricDensityPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "CameraDepth",
                "VBufferDensity"
            }));
            Assert.That(resources.RenderLists.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "FogVolumeVFXRenderList"
            }));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "VolumeBounds",
                "VolumetricVisibleGlobalIndices",
                "VolumetricGlobalIndirectArgs",
                "VolumetricGlobalIndirection",
                "VolumetricMaterialData"
            }));
            Assert.That(resources.Textures.Single(entry => entry.Name == "CameraDepth").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferDensity").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(resources.RenderLists.Single(entry => entry.Name == "FogVolumeVFXRenderList").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumeBounds").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricVisibleGlobalIndices").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricGlobalIndirectArgs").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricGlobalIndirection").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricMaterialData").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricVisibleGlobalIndices").Buffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Raw));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricGlobalIndirectArgs").Buffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.IndirectArguments));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "VolumetricGlobalIndirection").Buffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Raw));
        }

        [Test]
        public void VolumetricMaxZPass_InitializesDepthAwareResource()
        {
            var pass = new VolumetricMaxZPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1280;
            cameraData.actualHeight = 720;

            pass.Prepare(frameData);

            var resources = ((IRenderPass)pass).Initialize();
            var maxZ = resources.Textures.Single(entry => entry.Name == "VBufferMaxZ");

            Assert.That(resources.Textures.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "CameraDepth",
                "VBufferMaxZ8x",
                "VBufferMaxZFinalMask",
                "VBufferMaxZ"
            }));
            Assert.That(maxZ.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(maxZ.Texture.desc.Dimension, Is.EqualTo(TextureDimension.Tex2D));
            Assert.That(maxZ.Texture.desc.Width, Is.EqualTo(80));
            Assert.That(maxZ.Texture.desc.Height, Is.EqualTo(45));
            Assert.That(maxZ.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32_SFloat));
            Assert.That(maxZ.Texture.desc.EnableRandomWrite, Is.True);

            var maxZ8x = resources.Textures.Single(entry => entry.Name == "VBufferMaxZ8x");
            var finalMask = resources.Textures.Single(entry => entry.Name == "VBufferMaxZFinalMask");
            Assert.That(maxZ8x.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(maxZ8x.IsTransient, Is.True);
            Assert.That(maxZ8x.Texture.desc.Width, Is.EqualTo(160));
            Assert.That(maxZ8x.Texture.desc.Height, Is.EqualTo(90));
            Assert.That(finalMask.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(finalMask.IsTransient, Is.True);
            Assert.That(finalMask.Texture.desc.Width, Is.EqualTo(80));
            Assert.That(finalMask.Texture.desc.Height, Is.EqualTo(45));
        }

        [Test]
        public void VolumetricLightingPass_InitializesVBufferAndLightingResources()
        {
            IRenderPass renderPass = new VolumetricLightingPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "CameraDepth",
                "DirectionalShadowTexture",
                "VBufferMaxZ",
                "VBufferDensity",
                "VBufferLighting",
                "VBufferLightingFiltered"
            }));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "DirectionalLights",
                "PunctualLights",
                "AreaLights",
                "BigTileLightList",
                "LayeredOffset",
                "LayeredLightList",
                "LogBaseBuffer"
            }));
            Assert.That(resources.Textures.Single(entry => entry.Name == "CameraDepth").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "DirectionalShadowTexture").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferMaxZ").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferDensity").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferLighting").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferLightingFiltered").Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferLightingFiltered").IsTransient, Is.True);
            Assert.That(resources.Buffers.Single(entry => entry.Name == "BigTileLightList").Access, Is.EqualTo(AccessFlags.Read));
        }

        [Test]
        public void VolumetricFogCompositePass_InitializesCompositePorts()
        {
            IRenderPass renderPass = new VolumetricFogCompositePass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "CameraDepth",
                "Color",
                "OutputColor",
                "VBufferLighting"
            }));
            Assert.That(resources.Textures.Single(entry => entry.Name == "OutputColor").Access, Is.EqualTo(AccessFlags.Write));
        }

        [Test]
        public void VolumetricDensityPass_Prepare_ConfiguresThreeDimensionalVBuffer()
        {
            var pass = new VolumetricDensityPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1280;
            cameraData.actualHeight = 720;

            pass.Prepare(frameData);

            var resources = ((IRenderPass)pass).Initialize();
            var vBuffer = resources.Textures.Single(entry => entry.Name == "VBufferDensity").Texture;

            Assert.That(vBuffer.desc.Dimension, Is.EqualTo(TextureDimension.Tex3D));
            Assert.That(vBuffer.desc.Width, Is.EqualTo(160));
            Assert.That(vBuffer.desc.Height, Is.EqualTo(90));
            Assert.That(vBuffer.desc.Slices, Is.EqualTo(VividVolumetricFogVolume.DefaultVolumeSliceCount));
            Assert.That(vBuffer.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(vBuffer.desc.EnableRandomWrite, Is.True);
        }

        [Test]
        public void BuildRegistrations_IncludesVolumetricPasses()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(new[]
            {
                typeof(VolumetricDensityPass),
                typeof(VolumetricMaxZPass),
                typeof(VolumetricLightingPass),
                typeof(VolumetricFogCompositePass)
            });

            var nodeNames = registrations.Select(registration => registration.NodeClassName).ToArray();

            Assert.That(nodeNames, Does.Contain(nameof(VolumetricDensityPass)));
            Assert.That(nodeNames, Does.Contain(nameof(VolumetricMaxZPass)));
            Assert.That(nodeNames, Does.Contain(nameof(VolumetricLightingPass)));
            Assert.That(nodeNames, Does.Contain(nameof(VolumetricFogCompositePass)));
        }

        [Test]
        public void VolumetricShaderSources_DefineExpectedKernelsAndResources()
        {
            var densitySource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VolumetricDensity.compute"));
            var maxZSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VolumetricMaxZ.compute"));
            var materialSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VolumetricMaterial.compute"));
            var localVoxelizeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "LocalVolumetricFogVoxelize.shader"));
            var lightingSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VolumetricLighting.compute"));
            var compositeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VolumetricFogComposite.shader"));
            var densityPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Volumetric", "VolumetricDensityPass.cs"));
            var localFogSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Volumetric", "VividLocalVolumetricFog.cs"));
            var localFogManagerSource = File.ReadAllText(GetPackageFilePath("Runtime", "SubSystem", "Volumetric", "VividLocalVolumetricFogManager.cs"));
            var lightingPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Volumetric", "VolumetricLightingPass.cs"));
            var lightGridPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Lighting", "LightGridPass.cs"));
            var clusteredLightingDataSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "VividClusteredLightingData.cs"));

            Assert.That(densitySource, Does.Contain("#pragma kernel ClearVBufferDensity"));
            Assert.That(densitySource, Does.Contain("#pragma kernel VoxelizeVBufferDensity"));
            Assert.That(densitySource, Does.Contain("[numthreads(8, 8, 1)]"));
            Assert.That(densitySource, Does.Contain("for (uint slice = 0; slice < (uint)_VBufferSliceCount; slice++)"));
            Assert.That(densitySource, Does.Not.Contain("_LocalVolumetricFogs"));
            Assert.That(densitySource, Does.Not.Contain("_LocalVolumetricFogMask0"));
            Assert.That(densitySource, Does.Not.Contain("AccumulateLocalFog"));
            Assert.That(densitySource, Does.Contain("ComputeHeightFogMultiplier"));
            Assert.That(densitySource, Does.Contain("exp(-heightAboveBase * rcpScaleHeight)"));
            Assert.That(densitySource, Does.Contain("_VBufferFogRcpScaleHeight"));
            Assert.That(densitySource, Does.Not.Contain("_VBufferDensityCutoff"));
            Assert.That(densitySource, Does.Not.Contain("extinction <= _VBufferDensityCutoff"));
            Assert.That(localVoxelizeSource, Does.Contain("Hidden/VividRP/LocalVolumetricFogVoxelize"));
            Assert.That(localVoxelizeSource, Does.Contain("Tags { \"LightMode\" = \"FogVolumeVoxelize\" }"));
            Assert.That(localVoxelizeSource, Does.Contain("Blend [_FogVolumeSrcColorBlend] [_FogVolumeDstColorBlend]"));
            Assert.That(localVoxelizeSource, Does.Contain("StructuredBuffer<VividVolumetricMaterialRenderingData> _VolumetricMaterialData"));
            Assert.That(localVoxelizeSource, Does.Contain("ByteAddressBuffer _VolumetricGlobalIndirectionBuffer"));
            Assert.That(localVoxelizeSource, Does.Contain("SV_RenderTargetArrayIndex"));
            Assert.That(localVoxelizeSource, Does.Contain("ComputeVolumeFadeFactor"));
            Assert.That(localVoxelizeSource, Does.Contain("pow(abs(fade - 1.0), 2.2)"));
            Assert.That(localVoxelizeSource, Does.Contain("_VolumetricMask"));
            Assert.That(localVoxelizeSource, Does.Contain("LOCALVOLUMETRICFOGBLENDINGMODE_MULTIPLY"));
            Assert.That(maxZSource, Does.Contain("#pragma kernel ComputeMaxZ"));
            Assert.That(maxZSource, Does.Contain("#pragma kernel ComputeFinalMask"));
            Assert.That(maxZSource, Does.Contain("#pragma kernel DilateMask"));
            Assert.That(maxZSource, Does.Contain("Texture2D<float> _InputTexture"));
            Assert.That(maxZSource, Does.Contain("RWTexture2D<float> _OutputTexture"));
            Assert.That(maxZSource, Does.Contain("groupshared float gs_MaxDepth"));
            Assert.That(maxZSource, Does.Contain("_SrcOffsetAndLimit"));
            Assert.That(maxZSource, Does.Contain("_DilationWidth"));
            Assert.That(maxZSource, Does.Contain("GetDepthToDownsample"));
            Assert.That(maxZSource, Does.Contain("IsVBufferFarDepth"));
            Assert.That(maxZSource, Does.Contain("LinearEyeDepth(deviceDepth, _ZBufferParams)"));
            Assert.That(maxZSource, Does.Contain("VBUFFER_MAX_Z_FAR_DEPTH"));
            Assert.That(maxZSource, Does.Contain("LoadInputMaxZ"));
            Assert.That(maxZSource, Does.Contain("DilateMask"));
            Assert.That(materialSource, Does.Contain("#pragma kernel ClearVolumetricMaterialRenderingParameters"));
            Assert.That(materialSource, Does.Contain("#pragma kernel ComputeVolumetricMaterialRenderingParameters"));
            Assert.That(materialSource, Does.Contain("StructuredBuffer<VividVolumetricMaterialBounds> _VolumeBounds"));
            Assert.That(materialSource, Does.Contain("ByteAddressBuffer _VolumetricVisibleGlobalIndicesBuffer"));
            Assert.That(materialSource, Does.Contain("RWBuffer<uint> _VolumetricGlobalIndirectArgsBuffer"));
            Assert.That(materialSource, Does.Contain("RWByteAddressBuffer _VolumetricGlobalIndirectionBuffer"));
            Assert.That(materialSource, Does.Contain("RWStructuredBuffer<VividVolumetricMaterialRenderingData> _VolumetricMaterialData"));
            Assert.That(materialSource, Does.Contain("uint _ViewCount"));
            Assert.That(materialSource, Does.Not.Contain("_VolumetricViewCount"));
            Assert.That(materialSource, Does.Contain("DistanceToSlice"));
            Assert.That(materialSource, Does.Contain("DepthDistance"));
            Assert.That(materialSource, Does.Contain("DistanceDistanceToOBB"));
            Assert.That(materialSource, Does.Contain("float3 forward = normalize(cross(right, up))"));
            Assert.That(materialSource, Does.Contain("ComputeCubeVerticesOrder"));
            Assert.That(materialSource, Does.Contain("#if USE_VERTEX_CUBE_SLICING"));
            Assert.That(materialSource, Does.Contain("ComputeCubeVerticesOrder(volumeIndex)"));
            Assert.That(materialSource, Does.Contain("_VolumetricVisibleGlobalIndicesBuffer.Load(volumeIndex << 2)"));
            Assert.That(materialSource, Does.Contain("TransformWorldToHClip"));
            Assert.That(materialSource, Does.Contain("_VolumetricGlobalIndirectionBuffer.Store"));
            Assert.That(lightingSource, Does.Contain("#pragma kernel VolumetricLighting"));
            Assert.That(lightingSource, Does.Contain("#pragma kernel FilterVolumetricLighting"));
            Assert.That(lightingSource, Does.Contain("Texture2D<float> _VBufferMaxZ"));
            Assert.That(lightingSource, Does.Contain("float _VBufferMaxZEnabled"));
            Assert.That(lightingSource, Does.Contain("GetVBufferMaxOpaqueGeometryDistance"));
            Assert.That(lightingSource, Does.Contain("_VBufferMaxZ.GetDimensions"));
            Assert.That(lightingSource, Does.Contain("float maxDistance = max(maxLinearEyeDepth / forwardDistance, fallbackDistance)"));
            Assert.That(lightingSource, Does.Contain("LightingLoop.hlsl"));
            Assert.That(lightingSource, Does.Contain("GeometricTools.hlsl"));
            Assert.That(lightingSource, Does.Contain("[numthreads(8, 8, 1)]"));
            Assert.That(lightingSource, Does.Contain("for (uint slice = 0; slice < sliceCount; slice++)"));
            Assert.That(lightingSource, Does.Contain("ShouldEvaluateVBufferLighting"));
            Assert.That(lightingSource, Does.Contain("extinction > _VBufferDensityCutoff"));
            Assert.That(lightingSource, Does.Contain("float3 scattering = max(density.rgb, 0.0)"));
            Assert.That(lightingSource, Does.Contain("float extinction = max(density.a, 0.0)"));
            Assert.That(lightingSource, Does.Contain("voxelOpticalDepth = extinction * dt"));
            Assert.That(lightingSource, Does.Contain("TransmittanceIntegralHomogeneousMedium"));
            Assert.That(lightingSource, Does.Contain("totalRadiance += transmittanceToSlice * scattering"));
            Assert.That(lightingSource, Does.Not.Contain("totalRadiance += transmittanceToSlice * density.rgb"));
            Assert.That(lightingSource, Does.Contain("opticalDepth += 0.5 * voxelOpticalDepth"));
            Assert.That(lightingSource, Does.Contain("StoreIntegratedVBufferLighting"));
            Assert.That(lightingSource, Does.Contain("light.affectVolumetric"));
            Assert.That(lightingSource, Does.Contain("light.volumetricDimmer"));
            Assert.That(lightingSource, Does.Contain("light.volumetricFadeDistance"));
            Assert.That(lightingSource, Does.Contain("directionalLight.volumetricShadowDimmer"));
            Assert.That(lightingSource, Does.Contain("uint _PunctualLightCount"));
            Assert.That(lightingSource, Does.Contain("uint _AreaLightCount"));
            Assert.That(lightingSource, Does.Contain("StructuredBuffer<uint> g_vBigTileLightList"));
            Assert.That(lightingSource, Does.Contain("uint _VolumetricUseBigTileLightList"));
            Assert.That(lightingSource, Does.Contain("uint _NumTileBigTileX"));
            Assert.That(lightingSource, Does.Contain("uint _NumTileBigTileY"));
            Assert.That(lightingSource, Does.Contain("GetVolumetricBigTileIndex"));
            Assert.That(lightingSource, Does.Contain("GetBigTileLightCount"));
            Assert.That(lightingSource, Does.Contain("FetchBigTileLightIndex"));
            Assert.That(lightingSource, Does.Contain("FetchBigTileLightIndex(bigTileIndex, lightOffset)"));
            Assert.That(lightingSource, Does.Contain("if (lightIndex >= _PunctualLightCount)"));
            Assert.That(lightingSource, Does.Contain("VBUFFER_PUNCTUAL_SAMPLE_COUNT"));
            Assert.That(lightingSource, Does.Contain("EvaluatePunctualLightingIntegral"));
            Assert.That(lightingSource, Does.Contain("ImportanceSamplePunctualLight"));
            Assert.That(lightingSource, Does.Contain("IntersectRayCone"));
            Assert.That(lightingSource, Does.Contain("IntersectRaySphere"));
            Assert.That(lightingSource, Does.Contain("VIVID_PUNCTUAL_LIGHT_TYPE_SPOT"));
            Assert.That(lightingSource, Does.Contain("angleScale"));
            Assert.That(lightingSource, Does.Contain("angleOffset"));
            Assert.That(lightingSource, Does.Contain("float weight = TransmittanceHomogeneousMedium(extinction, max(t - t0, 0.0)) * rcpPdf"));
            Assert.That(lightingSource, Does.Contain("lighting * integratedTransmittance + punctualLightingIntegral"));
            Assert.That(lightingSource, Does.Not.Contain("EvaluateClusteredPunctualLighting"));
            Assert.That(lightingSource, Does.Contain("#define VBUFFER_FILTER_GAUSSIAN_SIGMA 1.0"));
            Assert.That(lightingSource, Does.Contain("#define VBUFFER_FILTER_SIZE_1D (VBUFFER_FILTER_GROUP_SIZE_XY + 2)"));
            Assert.That(lightingSource, Does.Contain("groupshared float4 gs_VBufferFilterCache"));
            Assert.That(lightingSource, Does.Contain("GroupMemoryBarrierWithGroupSync"));
            Assert.That(lightingSource, Does.Contain("Gaussian(length(float2(idx, idx2)), VBUFFER_FILTER_GAUSSIAN_SIGMA)"));
            Assert.That(lightingSource, Does.Not.Contain("for (int z = -1; z <= 1; z++)"));
            Assert.That(lightingSource, Does.Contain("if (hasMaxZ && t0 * 0.99 > maxOpaqueGeometryDistance)"));
            Assert.That(lightingSource, Does.Contain("break;"));
            Assert.That(compositeSource, Does.Contain("Hidden/VividRP/VolumetricFogComposite"));
            Assert.That(compositeSource, Does.Contain("_VBufferLighting"));
            Assert.That(compositeSource, Does.Contain("GetVBufferLinearDistanceFromDeviceDepth"));
            Assert.That(densityPassSource, Does.Contain("UnsafePass, IAllowGlobalStateModificationPass"));
            Assert.That(densityPassSource, Does.Contain("FogVolumeVoxelize"));
            Assert.That(densityPassSource, Does.Contain("VolumetricFogVFX"));
            Assert.That(densityPassSource, Does.Contain("VolumetricFogVFXOverdrawDebug"));
            Assert.That(densityPassSource, Does.Contain("RecordFogVolumeAndVFXVoxelization"));
            Assert.That(densityPassSource, Does.Contain("DrawRendererList"));
            Assert.That(densityPassSource, Does.Contain("SetComputeBufferParam(m_VolumetricMaterialShader, m_ComputeMaterialKernel, VolumeBoundsId"));
            Assert.That(densityPassSource, Does.Contain("ViewCountId = Shader.PropertyToID(\"_ViewCount\")"));
            Assert.That(densityPassSource, Does.Contain("SetComputeIntParam(m_VolumetricMaterialShader, ViewCountId, m_ViewCount)"));
            Assert.That(densityPassSource, Does.Contain("CoreUtils.DivRoundUp(m_MaterialFogCount * m_ViewCount, ComputeMaterialThreadGroupSizeX)"));
            Assert.That(densityPassSource, Does.Contain("SetRenderTarget(m_VBufferDensity)"));
            Assert.That(densityPassSource, Does.Not.Contain("LocalVolumetricFogsId"));
            Assert.That(localFogSource, Does.Contain("Graphics.RenderPrimitivesIndexedIndirect"));
            Assert.That(localFogSource, Does.Contain("ResolveVoxelizationMaterial"));
            Assert.That(localFogSource, Does.Contain("ConfigureTextureMaskProperties"));
            Assert.That(localFogManagerSource, Does.Contain("DefaultVoxelizationShaderName = \"Hidden/VividRP/LocalVolumetricFogVoxelize\""));
            Assert.That(localFogManagerSource, Does.Contain("SetupFogVolumeBlendMode"));
            Assert.That(localFogManagerSource, Does.Contain("PrepareVolumetricMaterialDrawCalls(materialCount)"));
            Assert.That(localFogManagerSource, Does.Not.Contain("LocalVolumetricFogs\""));
            Assert.That(lightingPassSource, Does.Contain("cmd.DispatchCompute(m_Shader, m_LightingKernel, m_DispatchX, m_DispatchY, 1)"));
            Assert.That(lightingPassSource, Does.Contain("VBufferMaxZId = Shader.PropertyToID(\"_VBufferMaxZ\")"));
            Assert.That(lightingPassSource, Does.Contain("VBufferMaxZEnabledId = Shader.PropertyToID(\"_VBufferMaxZEnabled\")"));
            Assert.That(lightingPassSource, Does.Contain("ReferenceEquals(m_VBufferMaxZ, m_LocalVBufferMaxZ)"));
            Assert.That(lightingPassSource, Does.Contain("VolumetricMaxZPass.MaxZTileSize"));
            Assert.That(lightingPassSource, Does.Contain("BindVBufferMaxZ(context, cmd, kernel)"));
            Assert.That(lightingPassSource, Does.Contain("FilterKernelName = \"FilterVolumetricLighting\""));
            Assert.That(lightingPassSource, Does.Contain("m_FilterDispatchZ = Mathf.Max(m_Settings.VBufferParameters.SliceCount, 1)"));
            Assert.That(lightingPassSource, Does.Contain("cmd.DispatchCompute(m_Shader, m_FilterKernel, m_DispatchX, m_DispatchY, m_FilterDispatchZ)"));
            Assert.That(lightingPassSource, Does.Contain("PunctualLightCountId = Shader.PropertyToID(\"_PunctualLightCount\")"));
            Assert.That(lightingPassSource, Does.Contain("AreaLightCountId = Shader.PropertyToID(\"_AreaLightCount\")"));
            Assert.That(lightingPassSource, Does.Contain("BigTileLightListId = Shader.PropertyToID(\"g_vBigTileLightList\")"));
            Assert.That(lightingPassSource, Does.Contain("VolumetricUseBigTileLightListId = Shader.PropertyToID(\"_VolumetricUseBigTileLightList\")"));
            Assert.That(lightingPassSource, Does.Contain("NumTileBigTileXId = Shader.PropertyToID(\"_NumTileBigTileX\")"));
            Assert.That(lightingPassSource, Does.Contain("Name = \"BigTileLightList\", Access = AccessFlags.Read"));
            Assert.That(lightingPassSource, Does.Contain("SetLightLoopBuffer(cmd, kernel, BigTileLightListId, m_BigTileLightListBuffer)"));
            Assert.That(lightingPassSource, Does.Contain("cmd.SetComputeIntParam(m_Shader, PunctualLightCountId, m_PunctualLightCount)"));
            Assert.That(lightingPassSource, Does.Contain("cmd.SetComputeIntParam(m_Shader, AreaLightCountId, m_AreaLightCount)"));
            Assert.That(lightingPassSource, Does.Contain("cmd.SetComputeIntParam(m_Shader, VolumetricUseBigTileLightListId"));
            Assert.That(lightingPassSource, Does.Contain("m_PunctualLightCount = HasBoundPunctualLightBuffer()"));
            Assert.That(lightingPassSource, Does.Contain("m_AreaLightCount = HasBoundAreaLightBuffer()"));
            Assert.That(lightingPassSource, Does.Contain("m_SupportsVolumetricBigTileLightList = supportsClusteredFiniteLights"));
            Assert.That(lightGridPassSource, Does.Contain("Name = \"BigTileLightList\""));
            Assert.That(lightGridPassSource, Does.Contain("clusteredLightingData.bigTileLightList = m_BigTileLightListBuffer"));
            Assert.That(lightGridPassSource, Does.Contain("clusteredLightingData.bigTileCountX = m_ClusterBigTileCountX"));
            Assert.That(clusteredLightingDataSource, Does.Contain("public RenderGraphBuffer bigTileLightList"));
            Assert.That(clusteredLightingDataSource, Does.Contain("public int bigTileCountX"));

            var vBufferSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VBuffer.hlsl"));
            Assert.That(vBufferSource, Does.Contain("DecodeLogarithmicDepthGeneralized"));
            Assert.That(vBufferSource, Does.Contain("_VBufferCoordToViewDirWS"));
            Assert.That(vBufferSource, Does.Contain("_VBufferDepthDecodingParams"));
            Assert.That(vBufferSource, Does.Contain("_VBufferIsOrthographic"));
            Assert.That(vBufferSource, Does.Contain("IsVBufferFarDepth"));

            var volumetricVariablesSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "ShaderVariablesVolumetric.hlsl"));
            Assert.That(volumetricVariablesSource, Does.Contain("_VBufferMaxZDilationRadius"));
        }

        [Test]
        public void VividLocalVolumetricFogEditor_UsesBoundProxySceneHandles()
        {
            var editorSource = File.ReadAllText(GetPackageFilePath("Editor", "ComponentEditor", "VividLocalVolumetricFogEditor.cs"));

            Assert.That(editorSource, Does.Contain("BoundProxyEditorUtility.DrawSceneHandles"));
            Assert.That(editorSource, Does.Contain("allowCenterHandle: true"));
            Assert.That(editorSource, Does.Contain("DrawGizmo"));
        }

        [Test]
        public void VividRPCoreResources_DefinesVolumetricResourcePaths()
        {
            Assert.That(GetResourcePath(nameof(VividRPCoreResources.VolumetricDensityCompute)), Is.EqualTo(
                "Shaders/Core/Private/Volumetric/VolumetricDensity.compute"));
            Assert.That(GetResourcePath(nameof(VividRPCoreResources.VolumetricMaxZCompute)), Is.EqualTo(
                "Shaders/Core/Private/Volumetric/VolumetricMaxZ.compute"));
            Assert.That(GetResourcePath(nameof(VividRPCoreResources.VolumetricMaterialCompute)), Is.EqualTo(
                "Shaders/Core/Private/Volumetric/VolumetricMaterial.compute"));
            Assert.That(GetResourcePath(nameof(VividRPCoreResources.LocalVolumetricFogVoxelizeShader)), Is.EqualTo(
                "Shaders/Core/Private/Volumetric/LocalVolumetricFogVoxelize"));
            Assert.That(GetResourcePath(nameof(VividRPCoreResources.VolumetricLightingCompute)), Is.EqualTo(
                "Shaders/Core/Private/Volumetric/VolumetricLighting.compute"));
            Assert.That(GetResourcePath(nameof(VividRPCoreResources.VolumetricFogCompositeShader)), Is.EqualTo(
                "Shaders/Core/Private/Volumetric/VolumetricFogComposite"));
        }

        private static string GetResourcePath(string fieldName)
        {
            return typeof(VividRPCoreResources)
                .GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)
                ?.GetCustomAttribute<ResourcePathAttribute>()
                ?.Path;
        }

        private static string GetPackageFilePath(params string[] path)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp"),
                Path.Combine(projectRoot, "Packages", "Custom_URP")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(path));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(path));
        }

        private static void SetLocalFogBoundProxy(VividLocalVolumetricFog fog, BoundProxyShape shape)
        {
            typeof(VividLocalVolumetricFog)
                .GetField("m_BoundProxy", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(fog, shape);
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected, float tolerance = 0.0001f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
        }
    }
}
