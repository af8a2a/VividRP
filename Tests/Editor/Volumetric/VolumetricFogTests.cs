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
        public void ComputeVBufferParameters_ClampsDimensionsAndEncodesDepth()
        {
            var parameters = VividVolumetricUtility.ComputeVBufferParameters(
                1920,
                1080,
                50.0f,
                64,
                100.0f,
                0.5f);

            Assert.That(parameters.ViewportWidth, Is.EqualTo(960));
            Assert.That(parameters.ViewportHeight, Is.EqualTo(540));
            Assert.That(parameters.SliceCount, Is.EqualTo(64));
            Assert.That(parameters.DepthDistributionPower, Is.EqualTo(1.5f).Within(0.0001f));
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
                parameters.positiveFade = new Vector3(1.0f, 2.0f, 4.0f);
                parameters.negativeFade = new Vector3(2.0f, 4.0f, 8.0f);
                fog.parameters = parameters;

                var data = fog.ConvertToEngineData(null);

                Assert.That(data.scatteringExtinction.w, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(data.scatteringExtinction.x, Is.EqualTo(0.05f).Within(0.0001f));
                Assert.That(data.positiveFade.x, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(data.negativeFade.z, Is.EqualTo(0.2f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
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
        public void VolumetricDensityPass_InitializesStableResources()
        {
            IRenderPass renderPass = new VolumetricDensityPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "CameraDepth",
                "VBufferDensity"
            }));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "LocalVolumetricFogs"
            }));
            Assert.That(resources.Textures.Single(entry => entry.Name == "CameraDepth").Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.Textures.Single(entry => entry.Name == "VBufferDensity").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(resources.Buffers.Single(entry => entry.Name == "LocalVolumetricFogs").Access, Is.EqualTo(AccessFlags.Read));
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
                "VBufferDensity",
                "VBufferLighting",
                "VBufferLightingFiltered"
            }));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Is.EquivalentTo(new[]
            {
                "AreaLights",
                "DirectionalLights",
                "LayeredLightList",
                "LayeredOffset",
                "LogBaseBuffer",
                "PunctualLights"
            }));
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
            Assert.That(vBuffer.desc.Width, Is.EqualTo(640));
            Assert.That(vBuffer.desc.Height, Is.EqualTo(360));
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
                typeof(VolumetricLightingPass),
                typeof(VolumetricFogCompositePass)
            });

            var nodeNames = registrations.Select(registration => registration.NodeClassName).ToArray();

            Assert.That(nodeNames, Does.Contain(nameof(VolumetricDensityPass)));
            Assert.That(nodeNames, Does.Contain(nameof(VolumetricLightingPass)));
            Assert.That(nodeNames, Does.Contain(nameof(VolumetricFogCompositePass)));
        }

        [Test]
        public void VolumetricShaderSources_DefineExpectedKernelsAndResources()
        {
            var densitySource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VolumetricDensity.compute"));
            var lightingSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VolumetricLighting.compute"));
            var compositeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Volumetric", "VolumetricFogComposite.shader"));

            Assert.That(densitySource, Does.Contain("#pragma kernel ClearVBufferDensity"));
            Assert.That(densitySource, Does.Contain("#pragma kernel VoxelizeVBufferDensity"));
            Assert.That(densitySource, Does.Contain("StructuredBuffer<VividLocalVolumetricFogEngineData> _LocalVolumetricFogs"));
            Assert.That(densitySource, Does.Contain("_LocalVolumetricFogMask0"));
            Assert.That(lightingSource, Does.Contain("#pragma kernel VolumetricLighting"));
            Assert.That(lightingSource, Does.Contain("#pragma kernel GaussianFilterVBufferLighting"));
            Assert.That(lightingSource, Does.Contain("LightingLoop.hlsl"));
            Assert.That(compositeSource, Does.Contain("Hidden/VividRP/VolumetricFogComposite"));
            Assert.That(compositeSource, Does.Contain("_VBufferLighting"));
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
