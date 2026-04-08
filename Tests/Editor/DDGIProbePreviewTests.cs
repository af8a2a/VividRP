using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class DDGIProbePreviewTests
    {
        [Test]
        public void DDGIProbePreviewRenderer_UsesSceneHandlesForSelectedVolumes()
        {
            string source = File.ReadAllText(GetPackageFilePath("Editor", "VolumeEditor", "DDGIProbePreviewRenderer.cs"));

            Assert.That(source, Does.Contain("DrawSceneViewPreview"));
            Assert.That(source, Does.Contain("Handles.SphereHandleCap("));
            Assert.That(source, Does.Contain("Handles.DrawWireDisc("));
            Assert.That(source, Does.Contain("EventType.Repaint"));
            Assert.That(source, Does.Contain("CompareFunction.Always"));
        }

        [Test]
        public void DDGIProbePreviewShader_RemainsAvailableForFutureShaderBasedPreview()
        {
            string source = File.ReadAllText(GetPackageFilePath("Editor", "Shader", "DDGIProbePreview.shader"));

            Assert.That(source, Does.Contain("Shader \"Hidden/VividRP/Editor/DDGIProbePreview\""));
            Assert.That(source, Does.Contain("#pragma multi_compile_instancing"));
        }

        [Test]
        public void DDGIVolumeEditor_HooksSceneViewProbePreview()
        {
            string source = File.ReadAllText(GetPackageFilePath("Editor", "VolumeEditor", "DDGIVolumeEditor.cs"));

            Assert.That(source, Does.Contain("Select the GameObject that owns this DDGI volume in Scene view to preview probe placement with sphere handles."));
            Assert.That(source, Does.Contain("DDGIProbePreviewRenderer.DrawSceneViewPreview(ddgiVolume);"));
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
