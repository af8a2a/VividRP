using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using ResourcePathAttribute = VividRP.Runtime.ResourcePathAttribute;

namespace VividRP.Editor.Tests
{
    public sealed class DDGIShaderIntegrationTests
    {
        [Test]
        public void DDGIShaderConfig_UsesBalancedDefaultsAndOverrideFriendlyTexelSemantics()
        {
            var source = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "DDGI",
                "Internal",
                "DDGIShaderConfig.hlsl"));

            Assert.That(source, Does.Contain("#ifndef RTXGI_COORDINATE_SYSTEM"));
            Assert.That(source, Does.Contain("#ifndef RTXGI_DDGI_BLEND_RAYS_PER_PROBE"));
            Assert.That(source, Does.Contain("#define RTXGI_DDGI_BLEND_RAYS_PER_PROBE 144"));
            Assert.That(source, Does.Contain("#ifndef RTXGI_DDGI_PROBE_NUM_TEXELS"));
            Assert.That(source, Does.Contain("#define RTXGI_DDGI_PROBE_NUM_TEXELS 8"));
            Assert.That(source, Does.Contain("#ifndef RTXGI_DDGI_PROBE_NUM_INTERIOR_TEXELS"));
            Assert.That(source, Does.Contain("#define RTXGI_DDGI_PROBE_NUM_INTERIOR_TEXELS (RTXGI_DDGI_PROBE_NUM_TEXELS - 2)"));
            Assert.That(source, Does.Contain("#define RTXGI_DDGI_BINDLESS_RESOURCES 0"));
            Assert.That(source, Does.Contain("#define RTXGI_DDGI_RESOURCE_MANAGEMENT 0"));
        }

        [Test]
        public void DDGIShaders_DeclareTraceBlendAndQueryContracts()
        {
            var traceSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "DDGI",
                "Internal",
                "ProbeTrace.compute"));
            var distanceBlendSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GlobalIllumination",
                "DDGI",
                "Internal",
                "ProbeBlendingDistance.compute"));
            var querySource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "DDGI.hlsl"));

            Assert.That(traceSource, Does.Contain("#pragma require inlineraytracing"));
            Assert.That(traceSource, Does.Contain("TraceRayInline("));
            Assert.That(traceSource, Does.Contain("CommittedInstanceIndex()"));
            Assert.That(traceSource, Does.Contain("CommittedPrimitiveIndex()"));
            Assert.That(traceSource, Does.Contain("CommittedTriangleBarycentrics()"));
            Assert.That(traceSource, Does.Contain("NonUniformResourceIndex(material.BaseMapIndex)"));
            Assert.That(traceSource, Does.Contain("DDGIStoreProbeRayBackfaceHit"));
            Assert.That(traceSource, Does.Contain("max(emissive + directDiffuse, 0.0f)"));
            Assert.That(distanceBlendSource, Does.Contain("#define RTXGI_DDGI_BLEND_RADIANCE 0"));
            Assert.That(distanceBlendSource, Does.Contain("#define RTXGI_DDGI_PROBE_NUM_TEXELS 16"));
            Assert.That(distanceBlendSource, Does.Contain("#define RTXGI_DDGI_PROBE_NUM_INTERIOR_TEXELS 14"));
            Assert.That(querySource, Does.Contain("Texture2DArray<float4> _DDGIProbeIrradiance;"));
            Assert.That(querySource, Does.Contain("Texture2DArray<float4> _DDGIProbeDistance;"));
            Assert.That(querySource, Does.Contain("bool VividDDGIIsEnabled()"));
            Assert.That(querySource, Does.Contain("DDGIGetSurfaceBias"));
            Assert.That(querySource, Does.Contain("DDGIGetVolumeIrradiance("));
            Assert.That(querySource, Does.Contain("VividDDGIGetBlendWeight"));
        }

        [Test]
        public void DeferredLightingShaders_EnableDDGIAndApplyDeferredDiffuseOverride()
        {
            var hdrpLightingSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "HdrpLitLighting.hlsl"));
            var deferredComputeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredLit.compute"));
            var deferredIndirectSource = File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirectPass.hlsl"));

            Assert.That(hdrpLightingSource, Does.Contain("#if defined(VIVIDRP_DDGI_ENABLED)"));
            Assert.That(hdrpLightingSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/DDGI.hlsl\""));
            Assert.That(hdrpLightingSource, Does.Contain("float3 ApplyVividDDGIIndirectDiffuse("));
            Assert.That(hdrpLightingSource, Does.Contain("float3 ddgiIrradiance = VividDDGIGetIrradiance(positionWS, surfaceData.normalWS, viewDirectionWS);"));
            Assert.That(deferredComputeSource, Does.Contain("#define VIVIDRP_DDGI_ENABLED 1"));
            Assert.That(deferredComputeSource, Does.Contain("ApplyVividDDGIIndirectDiffuse("));
            Assert.That(deferredIndirectSource, Does.Contain("#define VIVIDRP_DDGI_ENABLED 1"));
            Assert.That(deferredIndirectSource, Does.Contain("ApplyVividDDGIIndirectDiffuse("));
        }

        [Test]
        public void VividRPCoreResources_DeclareDDGIShaderAssets()
        {
            AssertResourcePath(nameof(VividRPCoreResources.DDGIProbeTraceCompute), "Shaders/Core/Private/GlobalIllumination/DDGI/Internal/ProbeTrace.compute");
            AssertResourcePath(nameof(VividRPCoreResources.DDGIProbeBlendIrradianceCompute), "Shaders/Core/Private/GlobalIllumination/DDGI/Internal/ProbeBlending.compute");
            AssertResourcePath(nameof(VividRPCoreResources.DDGIProbeBlendDistanceCompute), "Shaders/Core/Private/GlobalIllumination/DDGI/Internal/ProbeBlendingDistance.compute");
            AssertResourcePath(nameof(VividRPCoreResources.DDGIProbeRelocationCompute), "Shaders/Core/Private/GlobalIllumination/DDGI/Internal/ProbeRelocation.compute");
            AssertResourcePath(nameof(VividRPCoreResources.DDGIProbeClassificationCompute), "Shaders/Core/Private/GlobalIllumination/DDGI/Internal/ProbeClassification.compute");
            AssertResourcePath(nameof(VividRPCoreResources.DDGIReductionCompute), "Shaders/Core/Private/GlobalIllumination/DDGI/Internal/Reduction.compute");
        }

        private static void AssertResourcePath(string fieldName, string expectedPath)
        {
            FieldInfo field = typeof(VividRPCoreResources).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null);

            ResourcePathAttribute resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();
            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo(expectedPath));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (string packageRoot in packageRoots)
            {
                string fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
