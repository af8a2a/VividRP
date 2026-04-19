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
            Assert.That(source, Does.Contain("TEXTURE2D_ARRAY(_VTPhysicalCache);"));
            Assert.That(source, Does.Contain("_VTFeedbackRequests"));
            Assert.That(source, Does.Contain("_VTFeedbackCounter"));
            Assert.That(source, Does.Contain("register(u1)"));
            Assert.That(source, Does.Contain("register(u2)"));
            Assert.That(source, Does.Contain("float _VTSpaceParams[12];"));
            Assert.That(source, Does.Contain("float _VTMipOffsets[VIVID_VT_MAX_MIPS];"));
            Assert.That(source, Does.Contain("int _VTDebugMode;"));
            Assert.That(source, Does.Contain("int _VTFeedbackEnabled;"));
            Assert.That(source, Does.Contain("float VTComputeRequestedMipLevel"));
            Assert.That(source, Does.Contain("uint VTComputeRequestedMip"));
            Assert.That(source, Does.Contain("VTResolvedAddress VTResolveAddress"));
            Assert.That(source, Does.Contain("float3 VTComputePhysicalUVW"));
            Assert.That(source, Does.Contain("float4 VTSamplePhysicalCache"));
            Assert.That(source, Does.Contain("void VTWriteFeedback"));
        }

        [Test]
        public void VirtualTextureShaderIds_MatchExpectedPropertyNames()
        {
            Assert.That(VirtualTextureShaderIDs._VTPageTable, Is.EqualTo(Shader.PropertyToID("_VTPageTable")));
            Assert.That(VirtualTextureShaderIDs._VTPhysicalCache, Is.EqualTo(Shader.PropertyToID("_VTPhysicalCache")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackRequests, Is.EqualTo(Shader.PropertyToID("_VTFeedbackRequests")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackCounter, Is.EqualTo(Shader.PropertyToID("_VTFeedbackCounter")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackEnabled, Is.EqualTo(Shader.PropertyToID("_VTFeedbackEnabled")));
            Assert.That(VirtualTextureShaderIDs._VTSpaceParams, Is.EqualTo(Shader.PropertyToID("_VTSpaceParams")));
            Assert.That(VirtualTextureShaderIDs._VTMipOffsets, Is.EqualTo(Shader.PropertyToID("_VTMipOffsets")));
            Assert.That(VirtualTextureShaderIDs._VTDebugMode, Is.EqualTo(Shader.PropertyToID("_VTDebugMode")));
        }

        [Test]
        public void VirtualTextureDemoShader_DeclaresDedicatedPassAndFeedbackPath()
        {
            string source = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Material",
                "VirtualTextureDemo.shader"));

            Assert.That(source, Does.Contain("Shader \"VividRP/Material/VirtualTextureDemo\""));
            Assert.That(source, Does.Contain("Tags { \"LightMode\" = \"VividVT\" }"));
            Assert.That(source, Does.Contain("#define VIVID_VT_ENABLE_FEEDBACK_RW 1"));
            Assert.That(source, Does.Contain("VTComputeRequestedMip"));
            Assert.That(source, Does.Contain("VTWriteFeedback"));
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
