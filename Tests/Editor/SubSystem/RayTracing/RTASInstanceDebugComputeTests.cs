using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class RTASInstanceDebugComputeTests
    {
        [Test]
        public void RTASInstanceDebugCompute_UsesRayQueryToVisualizeCommittedInstancesAndPrimitives()
        {
            var source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("#include \"UnityRayQuery.cginc\""));
            Assert.That(source, Does.Contain("#pragma require inlineraytracing"));
            Assert.That(source, Does.Contain("RaytracingAccelerationStructure _AccelerationStructure;"));
            Assert.That(source, Does.Contain("RWTexture2D<float4> _OutputTexture;"));
            Assert.That(source, Does.Contain("query.TraceRayInline("));
            Assert.That(source, Does.Contain("while (query.Proceed())"));
            Assert.That(source, Does.Contain("query.CommittedStatus() == COMMITTED_TRIANGLE_HIT"));
            Assert.That(source, Does.Contain("_VisualizationMode"));
            Assert.That(source, Does.Contain("query.CommittedInstanceID()"));
            Assert.That(source, Does.Contain("query.CommittedInstanceIndex()"));
            Assert.That(source, Does.Contain("query.CommittedPrimitiveIndex()"));
            Assert.That(source, Does.Contain("_OutputTexture[dispatchThreadID.xy] = float4(debugColor, 1.0);"));
        }

        [Test]
        public void VividRPCoreResources_DeclaresRTASInstanceDebugCompute()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.RTASInstanceDebugCompute));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/RTASInstanceDebug"));
        }

        private static string GetComputeShaderSourcePath()
        {
            var shaderPath = GetPackageFilePath("Shaders", "Core", "Private", "RTASInstanceDebug.compute");

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected compute shader source at '{shaderPath}'.");
            return shaderPath;
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
    }
}
