using System.IO;
using System.Reflection;
using NUnit.Framework;
using VividRP.Runtime;
using ResourcePathAttribute = VividRP.Runtime.ResourcePathAttribute;

namespace VividRP.Editor.Tests
{
    public class SkyAmbientProbeConvolutionTests
    {
        [Test]
        public void TryPopulateProbeFromCoefficients_MapsReadbackLayoutIntoSphericalHarmonics()
        {
            var coefficients = new float[27];
            for (var index = 0; index < coefficients.Length; index++)
                coefficients[index] = index + 0.5f;

            var converted = SkyAmbientProbeConvolution.TryPopulateProbeFromCoefficients(coefficients, out var probe);

            Assert.That(converted, Is.True);
            Assert.That(probe[0, 0], Is.EqualTo(0.5f));
            Assert.That(probe[0, 8], Is.EqualTo(8.5f));
            Assert.That(probe[1, 0], Is.EqualTo(9.5f));
            Assert.That(probe[2, 8], Is.EqualTo(26.5f));
        }

        [Test]
        public void VividRPCoreResources_DeclaresSkyAmbientProbeConvolutionCompute()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.SkyAmbientProbeConvolutionCompute));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/Sky/AmbientProbeConvolution.compute"));
        }

        [Test]
        public void AmbientProbeConvolutionCompute_DeclaresKernelAndSkyParameters()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Sky", "AmbientProbeConvolution.compute"));

            Assert.That(source, Does.Contain("#pragma kernel AmbientProbeConvolution"));
            Assert.That(source, Does.Contain("TEXTURECUBE(_AmbientProbeInputCubemap);"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<float> _AmbientProbeOutputBuffer;"));
            Assert.That(source, Does.Contain("float4 _SkyConvolutionTint;"));
            Assert.That(source, Does.Contain("float4 _SkyConvolutionParams;"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
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
