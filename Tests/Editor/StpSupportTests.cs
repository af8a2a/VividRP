using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class StpSupportTests
    {
        [Test]
        public void PipelineAsset_ImplementsStpEnabledInterface()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                var stpAsset = asset as ISTPEnabledRenderPipeline;

                Assert.That(stpAsset, Is.Not.Null);
                Assert.That(stpAsset.isStpUsed, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
