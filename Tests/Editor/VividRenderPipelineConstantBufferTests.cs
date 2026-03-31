using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Tests
{
    public class VividRenderPipelineConstantBufferTests
    {
        [SetUp]
        public void SetUp()
        {
            VividRenderPipeline.ReleaseConstantBuffersForShutdown();
        }

        [TearDown]
        public void TearDown()
        {
            VividRenderPipeline.ReleaseConstantBuffersForShutdown();
        }

        [Test]
        public void ReleaseConstantBuffersForShutdown_ReleasesAndRecreatesSingleton_WhenLightListConstantBufferWasAllocated()
        {
            Assert.That(GetRegisteredConstantBufferCount(), Is.Zero);

            ConstantBuffer.UpdateData(default(ShaderVariablesLightList));

            Assert.That(GetRegisteredConstantBufferCount(), Is.EqualTo(1));

            VividRenderPipeline.ReleaseConstantBuffersForShutdown();

            Assert.That(GetRegisteredConstantBufferCount(), Is.Zero);

            ConstantBuffer.UpdateData(default(ShaderVariablesLightList));

            Assert.That(GetRegisteredConstantBufferCount(), Is.EqualTo(1));
        }

        [Test]
        public void HammersleyInitialize_RegistersSamplingConstantBuffers()
        {
            Assert.That(GetRegisteredConstantBufferCount(), Is.Zero);

            Hammersley.Initialize();

            Assert.That(GetRegisteredConstantBufferCount(), Is.EqualTo(4));
        }

        [Test]
        public void Constructor_InitializesHammersleyConstants_BeforeSkySystemsUseAmbientConvolution()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPipeline", "VividRenderPipeline.cs"));

            Assert.That(source, Does.Contain("Hammersley.Initialize();"));
        }

        private static int GetRegisteredConstantBufferCount()
        {
            var field = typeof(ConstantBuffer).GetField(
                "m_RegisteredConstantBuffers",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var registeredBuffers = field.GetValue(null) as ICollection;
            Assert.That(registeredBuffers, Is.Not.Null);
            return registeredBuffers.Count;
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
