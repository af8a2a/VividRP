using System.IO;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureShaderContractTests
    {
        [Test]
        public void VirtualTextureHlsl_DeclaresExpectedPublicSymbolsAndHelpers()
        {
            string source = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Public",
                "VirtualTexture",
                "VirtualTexture.hlsl"));

            Assert.That(source, Does.Contain("StructuredBuffer<uint> _VTPageTable;"));
            Assert.That(source, Does.Contain("TEXTURE2D(_VTPhysicalCache);"));
            Assert.That(source, Does.Contain("TEXTURE2D(_VTPhysicalCache1);"));
            Assert.That(source, Does.Contain("TEXTURE2D(_VTPhysicalCache2);"));
            Assert.That(source, Does.Contain("TEXTURE2D(_VTPhysicalCache3);"));
            Assert.That(source, Does.Not.Contain("TEXTURE2D_ARRAY(_VTPhysicalCache"));
            Assert.That(source, Does.Contain("_VTFeedbackRequests"));
            Assert.That(source, Does.Contain("_VTFeedbackCounter"));
            Assert.That(source, Does.Contain("register(u1)"));
            Assert.That(source, Does.Contain("register(u2)"));
            Assert.That(source, Does.Contain("float _VTSpaceParams[32];"));
            Assert.That(source, Does.Contain("float4 _VTLayerFallbacks[4];"));
            Assert.That(source, Does.Contain("float _VTMipOffsets[VIVID_VT_MAX_MIPS];"));
            Assert.That(source, Does.Contain("int _VTDebugMode;"));
            Assert.That(source, Does.Contain("int _VTFeedbackEnabled;"));
            Assert.That(source, Does.Contain("int _VTFeedbackFrameIndex;"));
            Assert.That(source, Does.Contain("int _VTFeedbackSampleRate;"));
            Assert.That(source, Does.Contain("float4 _VTFeedbackViewParams;"));
            Assert.That(source, Does.Contain("#define VT_PAGE_TABLE_PHYSICAL_PAGE_ID_BITS 20u"));
            Assert.That(source, Does.Contain("#define VT_PAGE_TABLE_RESOLVED_MIP_BITS 6u"));
            Assert.That(source, Does.Contain("#define VT_PAGE_TABLE_LOCKED_BIT 29u"));
            Assert.That(source, Does.Contain("packedEntry & VT_PAGE_TABLE_PHYSICAL_PAGE_ID_MASK"));
            Assert.That(source, Does.Contain("struct VTMipRange"));
            Assert.That(source, Does.Contain("float VTComputeRequestedMipLevel"));
            Assert.That(source, Does.Contain("uint VTComputeRequestedMip"));
            Assert.That(source, Does.Contain("VTMipRange VTComputeRequestedMipRange"));
            Assert.That(source, Does.Contain("float VTComputeRequestedMipLevelGrad"));
            Assert.That(source, Does.Contain("VTMipRange VTComputeRequestedMipRangeGrad"));
            Assert.That(source, Does.Contain("VTResolvedAddress VTResolveAddress"));
            Assert.That(source, Does.Contain("float3 VTComputePhysicalUVW"));
            Assert.That(source, Does.Contain("float3 VTComputePhysicalUVWLayer"));
            Assert.That(source, Does.Contain("uint VTGetPhysicalGroupLayerCount"));
            Assert.That(source, Does.Contain("uint VTGetLayerPhysicalGroup"));
            Assert.That(source, Does.Contain("uint VTGetLayerPhysicalLayer"));
            Assert.That(source, Does.Contain("float2 VTComputePhysicalAtlasUv"));
            Assert.That(source, Does.Contain("float4 VTSamplePhysicalCacheGroup"));
            Assert.That(source, Does.Contain("float4 VTSamplePhysicalCache"));
            Assert.That(source, Does.Contain("_VTPhysicalCache3.GetDimensions(width, height)"));
            Assert.That(source, Does.Contain("SAMPLE_TEXTURE2D_LOD(_VTPhysicalCache3, sampler_VTPhysicalCache, atlasUv, 0.0)"));
            Assert.That(source, Does.Contain("float4 VTSamplePhysicalCacheTrilinear"));
            Assert.That(source, Does.Contain("float4 VTSampleBaseColor"));
            Assert.That(source, Does.Contain("float3 VTSampleNormal"));
            Assert.That(source, Does.Contain("float4 VTSampleMask"));
            Assert.That(source, Does.Contain("float3 VTSRGBToLinear"));
            Assert.That(source, Does.Contain("bool VTShouldWriteFeedback"));
            Assert.That(source, Does.Contain("VTFeedbackHash"));
            Assert.That(source, Does.Contain("void VTWriteFeedback"));
            Assert.That(source, Does.Contain("void VTWriteFallbackSample(float2 virtualUv, uint requestedMip, VTResolvedAddress resolved)"));
        }

        [Test]
        public void VirtualTextureShaderIds_MatchExpectedPropertyNames()
        {
            Assert.That(VirtualTextureShaderIDs._VTPageTable, Is.EqualTo(Shader.PropertyToID("_VTPageTable")));
            Assert.That(VirtualTextureShaderIDs._VTPhysicalCache, Is.EqualTo(Shader.PropertyToID("_VTPhysicalCache")));
            Assert.That(VirtualTextureShaderIDs._VTPhysicalCache1, Is.EqualTo(Shader.PropertyToID("_VTPhysicalCache1")));
            Assert.That(VirtualTextureShaderIDs._VTPhysicalCache2, Is.EqualTo(Shader.PropertyToID("_VTPhysicalCache2")));
            Assert.That(VirtualTextureShaderIDs._VTPhysicalCache3, Is.EqualTo(Shader.PropertyToID("_VTPhysicalCache3")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackRequests, Is.EqualTo(Shader.PropertyToID("_VTFeedbackRequests")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackCounter, Is.EqualTo(Shader.PropertyToID("_VTFeedbackCounter")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackEnabled, Is.EqualTo(Shader.PropertyToID("_VTFeedbackEnabled")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackViewParams, Is.EqualTo(Shader.PropertyToID("_VTFeedbackViewParams")));
            Assert.That(VirtualTextureShaderIDs._VTSpaceParams, Is.EqualTo(Shader.PropertyToID("_VTSpaceParams")));
            Assert.That(VirtualTextureShaderIDs._VTMipOffsets, Is.EqualTo(Shader.PropertyToID("_VTMipOffsets")));
            Assert.That(VirtualTextureShaderIDs._VTLayerFallbacks, Is.EqualTo(Shader.PropertyToID("_VTLayerFallbacks")));
            Assert.That(VirtualTextureShaderIDs._VTDebugMode, Is.EqualTo(Shader.PropertyToID("_VTDebugMode")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackFrameIndex, Is.EqualTo(Shader.PropertyToID("_VTFeedbackFrameIndex")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackSampleRate, Is.EqualTo(Shader.PropertyToID("_VTFeedbackSampleRate")));
        }

        [Test]
        public void VirtualTextureDemoShader_DeclaresDedicatedPassAndFeedbackPath()
        {
            string source = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Material",
                "VirtualTextureDemo",
                "VirtualTextureDemo.shader"));

            Assert.That(source, Does.Contain("Shader \"VividRP/Material/VirtualTextureDemo\""));
            Assert.That(source, Does.Contain("[MainTexture] [Tex(SurfaceInputs, _BaseTint)] _BaseMap"));
            Assert.That(source, Does.Contain("[HideInInspector] _MainTex"));
            Assert.That(source, Does.Contain("Tags { \"LightMode\" = \"VividVT\" }"));
            Assert.That(source, Does.Contain("#define VIVID_VT_ENABLE_FEEDBACK_RW 1"));
            Assert.That(source, Does.Contain("float4 _BaseMap_ST;"));
            Assert.That(source, Does.Contain("output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;"));
            Assert.That(source, Does.Contain("VTComputeRequestedMipRange"));
            Assert.That(source, Does.Contain("VTSampleBaseColor"));
            Assert.That(source, Does.Contain("VTWriteFeedback"));
            Assert.That(source, Does.Contain("VTWriteFallbackSample(input.uv, requestedMips.lowerMip, lowerResolved, input.positionCS)"));
        }

        private static string GetPackageFilePath(params string[] parts)
        {
            string customPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "Custom_URP"));
            if (Directory.Exists(customPath))
                return Path.Combine(customPath, Path.Combine(parts));

            string vividPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "VividRP"));
            if (Directory.Exists(vividPath))
                return Path.Combine(vividPath, Path.Combine(parts));

            string legacyPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "com.af8a2a.vividrp"));
            return Path.Combine(legacyPath, Path.Combine(parts));
        }
    }
}
