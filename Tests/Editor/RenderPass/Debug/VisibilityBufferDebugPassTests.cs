using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class VisibilityBufferDebugPassTests
    {
        [Test]
        public void Initialize_RegistersVisibilityInputAndColorOutput()
        {
            IRenderPass renderPass = new VisibilityBufferDebugPass();

            var resources = renderPass.Initialize();
            var visibilityEntry = resources.Textures.Single(entry => entry.Name == "VisibilityBuffer");
            var outputEntry = resources.Textures.Single(entry => entry.Name == "OutputTexture");

            Assert.That(resources.Textures, Has.Length.EqualTo(2));
            Assert.That(visibilityEntry.Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(visibilityEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32_UInt));
            Assert.That(outputEntry.Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(outputEntry.AttachmentIndex, Is.EqualTo(0));
            Assert.That(outputEntry.Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
        }

        [Test]
        public void ApplyEnumParameters_UpdatesVisualizationMode()
        {
            var pass = new VisibilityBufferDebugPass();

            RenderGraphPassEnumParameterUtility.ApplyEnumParameters(
                pass,
                typeof(VisibilityBufferDebugPass),
                new List<RenderGraphPassEnumParameter>
                {
                    new()
                    {
                        FieldName = "m_VisualizationMode",
                        Value = (int)VisibilityBufferDebugVisualizationMode.ClusterLOD,
                    }
                });

            Assert.That(pass.VisualizationMode, Is.EqualTo(VisibilityBufferDebugVisualizationMode.ClusterLOD));
        }

        [Test]
        public void Prepare_UsesVisibilityTextureSize_WhenConfigured()
        {
            var pass = new VisibilityBufferDebugPass();
            var visibilityTexture = GetTextureField(pass, "m_VisibilityBuffer");
            var outputTexture = GetTextureField(pass, "m_OutputTexture");

            visibilityTexture.desc.Width = 1600;
            visibilityTexture.desc.Height = 900;

            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 1920;
            cameraData.actualHeight = 1080;

            pass.Prepare(frameData);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(1600));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(900));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(outputTexture.desc.FilterMode, Is.EqualTo(FilterMode.Point));
        }

        [Test]
        public void ResolveSettings_UsesRenderingDebuggerValues()
        {
            var data = new VividRenderingDebugSettingsData
            {
                visibilityBufferDebugMode = VisibilityBufferDebugVisualizationMode.ClusterLOD,
                visibilityBufferDebugExposure = 2.5f,
            };

            var settings = VisibilityBufferDebugPass.ResolveSettings(
                data,
                VisibilityBufferDebugVisualizationMode.Cluster,
                0f);

            Assert.That(settings.visualizationMode, Is.EqualTo(VisibilityBufferDebugVisualizationMode.ClusterLOD));
            Assert.That(settings.exposure, Is.EqualTo(2.5f));
        }

        [Test]
        public void ResolveSettings_ClampsExposure()
        {
            var settings = VisibilityBufferDebugPass.ResolveSettings(
                new VividRenderingDebugSettingsData
                {
                    visibilityBufferDebugExposure = 32f,
                },
                VisibilityBufferDebugVisualizationMode.Cluster,
                0f);

            Assert.That(settings.exposure, Is.EqualTo(16f));
        }

        [Test]
        public void ResolveSettings_UsesPassDefaults_WhenDebuggerDataIsMissing()
        {
            var settings = VisibilityBufferDebugPass.ResolveSettings(
                null,
                VisibilityBufferDebugVisualizationMode.Triangle,
                1.5f);

            Assert.That(settings.visualizationMode, Is.EqualTo(VisibilityBufferDebugVisualizationMode.Triangle));
            Assert.That(settings.exposure, Is.EqualTo(1.5f));
        }

        [Test]
        public void Prepare_UsesRenderingDebuggerModeAndExposure()
        {
            try
            {
                VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugMode =
                    VisibilityBufferDebugVisualizationMode.ClusterLOD;
                VividRenderingDebugDisplaySettings.Data.visibilityBufferDebugExposure = 4f;

                var pass = new VisibilityBufferDebugPass
                {
                    VisualizationMode = VisibilityBufferDebugVisualizationMode.Instance,
                    Exposure = -2f,
                };

                var frameData = new ContextContainer();
                frameData.GetOrCreate<VividCameraData>().actualWidth = 64;
                frameData.GetOrCreate<VividCameraData>().actualHeight = 64;

                pass.Prepare(frameData);

                Assert.That(
                    GetFieldValue<VisibilityBufferDebugVisualizationMode>(pass, "m_ResolvedVisualizationMode"),
                    Is.EqualTo(VisibilityBufferDebugVisualizationMode.ClusterLOD));
                Assert.That(GetFieldValue<float>(pass, "m_ResolvedExposure"), Is.EqualTo(4f));
            }
            finally
            {
                VividRenderingDebugDisplaySettings.Data.Reset();
            }
        }

        [Test]
        public void VisibilityBufferDebugShader_SupportsClusterAndClusterLodModes()
        {
            var shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl\""));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl\""));
            Assert.That(shaderSource, Does.Contain("VIVID_VISIBILITY_BUFFER_DEBUG_INSTANCE"));
            Assert.That(shaderSource, Does.Contain("VIVID_VISIBILITY_BUFFER_DEBUG_CLUSTER"));
            Assert.That(shaderSource, Does.Contain("VIVID_VISIBILITY_BUFFER_DEBUG_CLUSTER_LOD"));
            Assert.That(shaderSource, Does.Contain("VIVID_VISIBILITY_BUFFER_DEBUG_TRIANGLE"));
            Assert.That(shaderSource, Does.Contain("ResolveClusterLODLevel"));
            Assert.That(shaderSource, Does.Contain("PullInstanceData"));
            Assert.That(shaderSource, Does.Contain("PullMeshLODNode"));
            Assert.That(shaderSource, Does.Contain("_MeshLODNodeCount"));
            Assert.That(shaderSource, Does.Contain("UnpackVisibilityBufferValue("));
            Assert.That(shaderSource, Does.Contain("IsPackedVisibilityBufferValueValid("));
        }

        [Test]
        public void VividRPCoreResources_DeclaresVisibilityBufferDebugShader()
        {
            var field = typeof(VividRPCoreResources).GetField(
                nameof(VividRPCoreResources.VisibilityBufferDebugShader),
                BindingFlags.Instance | BindingFlags.Public);

            Assert.That(field, Is.Not.Null);
            var resourcePath = field.GetCustomAttribute<VividResourcePathAttribute>();
            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/Debug/VisibilityBufferDebug"));
        }

        private static RenderGraphTexture GetTextureField(VisibilityBufferDebugPass pass, string fieldName)
        {
            var field = typeof(VisibilityBufferDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (RenderGraphTexture)field.GetValue(pass);
        }

        private static T GetFieldValue<T>(VisibilityBufferDebugPass pass, string fieldName)
        {
            var field = typeof(VisibilityBufferDebugPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(pass);
        }

        private static string GetShaderSourcePath()
        {
            var shaderPath = GetPackageFilePath("Shaders", "Core", "Private", "Debug", "VisibilityBufferDebug.shader");

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected shader source at '{shaderPath}'.");
            return shaderPath;
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "Custom_URP"),
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
    }
}
