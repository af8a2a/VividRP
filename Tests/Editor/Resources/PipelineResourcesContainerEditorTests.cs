using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class PipelineResourcesContainerEditorTests
    {
        [Test]
        public void CreateInspectorGUI_BuildsRecollectButtonAndEntriesInspector()
        {
            var container = ScriptableObject.CreateInstance<PipelineResourcesContainer>();
            var editor = UnityEditor.Editor.CreateEditor(container, typeof(VividRP.Editor.PipelineResourcesContainerEditor));

            try
            {
                var root = editor.CreateInspectorGUI();

                Assert.That(root, Is.Not.Null);
                Assert.That(root.Q<HelpBox>("vivid-pipeline-resources-help"), Is.Not.Null);
                Assert.That(root.Q<Label>("vivid-pipeline-resources-entry-count"), Is.Not.Null);
                Assert.That(root.Q<Button>("vivid-pipeline-resources-recollect-button"), Is.Not.Null);
                Assert.That(root.Q<IMGUIContainer>("vivid-pipeline-resources-entries"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(container);
            }
        }

        [Test]
        public void UpdateContainerResources_PopulatesEntriesForKnownPipelineResources()
        {
            var container = ScriptableObject.CreateInstance<PipelineResourcesContainer>();

            try
            {
                var entryCount = PipelineResourceUpdater.UpdateContainerResources(container);

                Assert.That(entryCount, Is.GreaterThan(0));
                Assert.That(container.Entries.Count, Is.EqualTo(entryCount));
                Assert.That(
                    container.Entries.Any(entry =>
                        entry.ResourceName == "Shaders/Core/Private/CoreBlit"
                        && entry.ResourceObject != null),
                    Is.True);
                Assert.That(
                    container.Entries.Any(entry =>
                        entry.ResourceName == "Shaders/Core/Private/ScreenSpaceReflection/ScreenSpaceReflectionHybrid.raytrace"
                        && entry.ResourceObject is RayTracingShader),
                    Is.True);
                Assert.That(
                    container.Entries.Any(entry =>
                        entry.ResourceName == "Shaders/Core/Private/Lighting/ReGIRGridBuild.compute"
                        && entry.ResourceObject is ComputeShader),
                    Is.True);
                Assert.That(
                    container.Entries.Any(entry =>
                        entry.ResourceName == "Shaders/Core/Private/Debug/ReGIRDebug"
                        && entry.ResourceObject is Shader),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(container);
            }
        }

        [Test]
        public void UpdateContainerResources_InvalidatesPipelineResourceManagerCache()
        {
            PipelineResourceManager.Cleanup();
            var cachedResources = PipelineResourceManager.Get<VividRPCoreResources>();
            var container = ScriptableObject.CreateInstance<PipelineResourcesContainer>();

            try
            {
                PipelineResourceUpdater.UpdateContainerResources(container);

                var refreshedResources = PipelineResourceManager.Get<VividRPCoreResources>();

                Assert.That(refreshedResources, Is.Not.SameAs(cachedResources));
            }
            finally
            {
                Object.DestroyImmediate(container);
                PipelineResourceManager.Cleanup();
            }
        }

        [Test]
        public void PackagePathUtility_IncludesCurrentAndLegacyPackageRoots()
        {
            var packageRoots = VividPackagePathUtility.GetCandidatePackageRoots();

            Assert.That(packageRoots, Does.Contain("Packages/com.vivid.render-pipelines"));
            Assert.That(packageRoots, Does.Contain("Packages/com.af8a2a.vividrp"));
            Assert.That(packageRoots, Does.Contain("Packages/VividRP"));
            Assert.That(packageRoots, Does.Contain("Packages/Custom_URP"));
            Assert.That(packageRoots.Distinct().Count(), Is.EqualTo(packageRoots.Length));
        }
    }
}
