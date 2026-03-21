using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class DirectionalRayTracedShadowComputeTests
    {
        [Test]
        public void DirectionalRayTracedShadowCompute_UsesInlineRayQueryWithDepthNormalAndShadowBindings()
        {
            var source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("#include \"UnityRayQuery.cginc\""));
            Assert.That(source, Does.Contain("#pragma require inlineraytracing"));
            Assert.That(source, Does.Contain("RaytracingAccelerationStructure _AccelerationStructure;"));
            Assert.That(source, Does.Contain("Texture2D<float> _DepthTexture;"));
            Assert.That(source, Does.Contain("Texture2D<float4> _GBuffer1;"));
            Assert.That(source, Does.Contain("RWTexture2D<float> _DirectionalShadowTexture;"));
            Assert.That(source, Does.Contain("DecodeVividNormalOct"));
            Assert.That(source, Does.Contain("ComputeWorldSpacePosition"));
            Assert.That(source, Does.Contain("query.TraceRayInline("));
            Assert.That(source, Does.Contain("while (query.Proceed())"));
            Assert.That(source, Does.Contain("query.CommittedStatus() == COMMITTED_TRIANGLE_HIT ? 0.0 : 1.0;"));
        }

        [Test]
        public void DirectionalRayTracedShadowCompute_UsesSharedRayTracingConstantBuffer()
        {
            var computeSource = File.ReadAllText(GetComputeShaderSourcePath());
            var commonSource = File.ReadAllText(GetRayTracingCommonSourcePath());

            Assert.That(computeSource, Does.Contain("EvaluateRayTracingBias(positionWS)"));
            Assert.That(computeSource, Does.Not.Contain("float _RayBias;"));
            Assert.That(computeSource, Does.Not.Contain("float _DistantRayBias;"));
            Assert.That(commonSource, Does.Contain("CBUFFER_START(ShaderVariablesRayTracing)"));
            Assert.That(commonSource, Does.Contain("float _RayTracingRayBias;"));
            Assert.That(commonSource, Does.Contain("float _RayTracingDistantRayBias;"));
            Assert.That(commonSource, Does.Contain("float _RayTracingMinSolidAngle;"));
        }

        [Test]
        public void VividRPCoreResources_DeclaresDirectionalRayTracedShadowCompute()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.DirectionalRayTracedShadowCompute));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/DirectionalRayTracedShadow"));
        }

        private static string GetComputeShaderSourcePath()
        {
            var shaderPath = GetPackageFilePath("Shaders", "Core", "Private", "DirectionalRayTracedShadow.compute");

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected compute shader source at '{shaderPath}'.");
            return shaderPath;
        }

        private static string GetRayTracingCommonSourcePath()
        {
            var shaderPath = GetPackageFilePath("Shaders", "Core", "Public", "RaytracingCommon.hlsl");

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected ray tracing common source at '{shaderPath}'.");
            return shaderPath;
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
    }
}
