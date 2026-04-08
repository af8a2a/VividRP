using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class DDGIProbePreviewTests
    {
        [Test]
        public void DDGIProbePreviewRenderer_UsesInstancedDrawForSelectedVolumes()
        {
            string source = File.ReadAllText(GetPackageFilePath("Editor", "VolumeEditor", "DDGIProbePreviewRenderer.cs"));

            Assert.That(source, Does.Contain("[InitializeOnLoad]"));
            Assert.That(source, Does.Contain("Selection.gameObjects"));
            Assert.That(source, Does.Contain("Graphics.DrawMeshInstanced("));
            Assert.That(source, Does.Contain("RenderPipelineManager.beginCameraRendering"));
        }

        [Test]
        public void DDGIProbePreviewShader_UsesStandardInstancing()
        {
            string source = File.ReadAllText(GetPackageFilePath("Editor", "Shader", "DDGIProbePreview.shader"));

            Assert.That(source, Does.Contain("Shader \"Hidden/VividRP/Editor/DDGIProbePreview\""));
            Assert.That(source, Does.Contain("#pragma multi_compile_instancing"));
            Assert.That(source, Does.Contain("UNITY_SETUP_INSTANCE_ID(input)"));
            Assert.That(source, Does.Contain("TransformObjectToWorld(input.positionOS)"));
        }

        [Test]
        public void DDGIVolumeEditor_ExplainsSceneViewProbePreview()
        {
            string source = File.ReadAllText(GetPackageFilePath("Editor", "VolumeEditor", "DDGIVolumeEditor.cs"));

            Assert.That(source, Does.Contain("Select this DDGI volume in Scene view to preview probe placement with indirect-drawn spheres."));
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
