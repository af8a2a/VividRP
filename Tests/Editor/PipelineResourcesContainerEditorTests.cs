using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class PipelineResourcesContainerEditorTests
    {
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
                        entry.TypeName == typeof(VividRPCoreResources).FullName
                        && entry.FieldName == nameof(VividRPCoreResources.CoreBlitShader)
                        && entry.Asset != null),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(container);
            }
        }
    }
}
