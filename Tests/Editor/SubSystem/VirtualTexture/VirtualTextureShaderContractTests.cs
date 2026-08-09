using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualTextureShaderContractTests
    {

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
            Assert.That(VirtualTextureShaderIDs._VTFeedbackResidentHash, Is.EqualTo(Shader.PropertyToID("_VTFeedbackResidentHash")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackResidentHashCapacity, Is.EqualTo(Shader.PropertyToID("_VTFeedbackResidentHashCapacity")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackEnabled, Is.EqualTo(Shader.PropertyToID("_VTFeedbackEnabled")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackViewParams, Is.EqualTo(Shader.PropertyToID("_VTFeedbackViewParams")));
            Assert.That(VirtualTextureShaderIDs._VTSpaceParams, Is.EqualTo(Shader.PropertyToID("_VTSpaceParams")));
            Assert.That(VirtualTextureShaderIDs._VTMipOffsets, Is.EqualTo(Shader.PropertyToID("_VTMipOffsets")));
            Assert.That(VirtualTextureShaderIDs._VTLayerFallbacks, Is.EqualTo(Shader.PropertyToID("_VTLayerFallbacks")));
            Assert.That(VirtualTextureShaderIDs._VTDebugMode, Is.EqualTo(Shader.PropertyToID("_VTDebugMode")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackFrameIndex, Is.EqualTo(Shader.PropertyToID("_VTFeedbackFrameIndex")));
            Assert.That(VirtualTextureShaderIDs._VTFeedbackSampleRate, Is.EqualTo(Shader.PropertyToID("_VTFeedbackSampleRate")));
            Assert.That(VirtualTextureShaderIDs._VTAdaptiveMipBias, Is.EqualTo(Shader.PropertyToID("_VTAdaptiveMipBias")));
        }
    }
}
