using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class HdrpDeferredLitLightingTests
    {

        [Test]
        public void PreIntegratedFGDFrameData_StoresPreparedLutHandlesForDeferredLighting()
        {
            RTHandles.Initialize(1, 1);
            var ggxDisneyDiffuse = VividPreIntegratedFGD.CreatePersistentTexture("TestPreIntegratedFGD_GGXDisneyDiffuse");
            var charlieAndFabric = VividPreIntegratedFGD.CreatePersistentTexture("TestPreIntegratedFGD_CharlieAndFabric");
            var data = new VividPreIntegratedFGDData();

            try
            {
                data.SetTextures(ggxDisneyDiffuse, charlieAndFabric);

                Assert.That(data.hasValidTextures, Is.True);
                Assert.That(data.ggxDisneyDiffuseTexture, Is.SameAs(ggxDisneyDiffuse));
                Assert.That(data.charlieAndFabricTexture, Is.SameAs(charlieAndFabric));
            }
            finally
            {
                ggxDisneyDiffuse?.Release();
                charlieAndFabric?.Release();
            }
        }
    }
}
