using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class PreIntegratedFGDPreparePassTests
    {
        [Test]
        public void Initialize_RegistersTwoPreIntegratedFgdOutputs_WhenPassIsCreated()
        {
            IRenderPass renderPass = new PreIntegratedFGDPreparePass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "PreIntegratedFGD_CharlieAndFabric",
                "PreIntegratedFGD_GGXDisneyDiffuse"
            }));
            Assert.That(textureEntries.Select(entry => entry.Access).Distinct(), Is.EqualTo(new[] { AccessFlags.Write }));
        }

        [Test]
        public void PreIntegratedFGDPreparePass_InheritsFromUnsafePass()
        {
            Assert.That(typeof(UnsafePass).IsAssignableFrom(typeof(PreIntegratedFGDPreparePass)), Is.True);
        }

        [Test]
        public void PreIntegratedFGDPreparePass_ImportsPersistentLutTextures_WithoutGlobalBindings()
        {
            var source = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "Lighting",
                "PreIntegratedFGDPreparePass.cs"));

            Assert.That(source, Does.Contain("VividPreIntegratedFGDTextures"));
            Assert.That(source, Does.Contain("PassRecorder.ImportTexture"));
            Assert.That(source, Does.Not.Contain("SetGlobalTexture("));
            Assert.That(source, Does.Not.Contain("SetGlobalColor("));
            Assert.That(source, Does.Not.Contain("SetGlobalVector("));
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
