using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

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
            Assert.That(source, Does.Contain("float4x4 _InvViewProjectionMatrix;"));
            Assert.That(source, Does.Contain("int _OutputWidth;"));
            Assert.That(source, Does.Contain("int _OutputHeight;"));
            Assert.That(source, Does.Contain("DecodeVividNormalOct"));
            Assert.That(source, Does.Contain("ComputeWorldSpacePosition(uv, deviceDepth, _InvViewProjectionMatrix);"));
            Assert.That(source, Does.Contain("dispatchThreadID.x >= (uint)_OutputWidth"));
            Assert.That(source, Does.Contain("dispatchThreadID.y >= (uint)_OutputHeight"));
            Assert.That(source, Does.Contain("float2(_OutputWidth, _OutputHeight)"));
            Assert.That(source, Does.Not.Contain("UNITY_MATRIX_I_VP"));
            Assert.That(source, Does.Not.Contain("_ScreenSize"));
            Assert.That(source, Does.Contain("query.TraceRayInline("));
            Assert.That(source, Does.Contain("query.Proceed();"));
            Assert.That(source, Does.Contain("PackPenumbra(shadowHitDist, _TanSunAngularRadius)"));
        }

        [Test]
        public void ShadowClassifyCompute_UsesExplicitOutputDimensions()
        {
            var source = File.ReadAllText(GetShadowClassifyComputeShaderSourcePath());

            Assert.That(source, Does.Contain("int _OutputWidth;"));
            Assert.That(source, Does.Contain("int _OutputHeight;"));
            Assert.That(source, Does.Contain("dispatchThreadID.x >= (uint)_OutputWidth"));
            Assert.That(source, Does.Contain("dispatchThreadID.y >= (uint)_OutputHeight"));
            Assert.That(source, Does.Contain("min(pixelCoord.x + 1, (uint)_OutputWidth - 1)"));
            Assert.That(source, Does.Contain("min(pixelCoord.y + 1, (uint)_OutputHeight - 1)"));
            Assert.That(source, Does.Not.Contain("_ScreenSize"));
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

        private static string GetShadowClassifyComputeShaderSourcePath()
        {
            var shaderPath = GetPackageFilePath("Shaders", "Core", "Private", "ShadowClassify.compute");

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
