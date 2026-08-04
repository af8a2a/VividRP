using System.IO;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class VividGPUDrivenOcclusionCullingTests
    {
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 2)]
        [TestCase(1080, 11)]
        [TestCase(1920, 11)]
        public void CalculateMipCount_MatchesHardwareMipChain(int dimension, int expectedMipCount)
        {
            Assert.That(
                VividGPUDrivenOcclusionHistorySystem.CalculateMipCount(dimension, dimension),
                Is.EqualTo(expectedMipCount));
        }

        [TestCase(1)]
        [TestCase(1080)]
        [TestCase(1920)]
        public void CalculateTextureDimension_DoesNotPowerOfTwoPadHistory(int dimension)
        {
            Assert.That(
                VividGPUDrivenOcclusionHistorySystem.CalculateTextureDimension(dimension),
                Is.EqualTo(dimension));
        }

        [Test]
        public void MeshletCullingShader_ImplementsConservativeTwoPassOcclusionContract()
        {
            string source = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "GPUDriven",
                "GPUMeshletCulling.compute"));

            Assert.That(source, Does.Contain("#pragma kernel CSOcclusionTestAll"));
            Assert.That(source, Does.Contain("#pragma kernel CSOcclusionTestCulled"));
            Assert.That(source, Does.Contain("#pragma kernel CSOcclusionRetest"));
            Assert.That(source, Does.Contain("#pragma kernel CSCopyOccluderDepth"));
            Assert.That(source, Does.Contain("#pragma kernel CSDownsampleOccluderDepth"));
            Assert.That(source, Does.Contain("FarthestDepth4"));
            Assert.That(source, Does.Contain("expandedMinPixel = minUv * pyramidSize - 2.0f"));
            Assert.That(source, Does.Contain("if (any(maxCoord >= mipSize))"));
            Assert.That(source, Does.Contain("return true;"));
            Assert.That(source, Does.Contain("_OccludedMeshletRenderRequests[occludedIndex] = renderRequest"));
            Assert.That(source, Does.Contain("_RecoveredMeshletRenderRequests[startInstance + localWriteOffset] = renderRequest"));
        }

        [Test]
        public void GPUDrivenSystem_DisablesOcclusionBuffersForShadowDispatchers()
        {
            string source = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "SubSystem",
                "GPUDriven",
                "VividGPUDrivenSystem.cs"));

            Assert.That(
                source,
                Does.Contain("new VividGPUDrivenCullingDispatcher(supportsOcclusion: false)"));
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
                string fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
