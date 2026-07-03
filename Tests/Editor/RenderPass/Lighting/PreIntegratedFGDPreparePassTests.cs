using System.Linq;
using NUnit.Framework;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class PreIntegratedFGDFrameContextTests
    {
        [Test]
        public void DeferredLightingPass_DoesNotRegisterPreIntegratedFgdInputs_WhenInitialized()
        {
            IRenderPass renderPass = new DeferredLightingPass();

            var resources = renderPass.Initialize();

            Assert.That(resources.Textures.Any(entry => entry.Name.StartsWith("PreIntegratedFGD_")), Is.False);
        }

        [Test]
        public void VividPreIntegratedFGDData_SetTexturesMarksValidAndResetClearsHandles()
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

                data.Reset();

                Assert.That(data.hasValidTextures, Is.False);
                Assert.That(data.ggxDisneyDiffuseTexture, Is.Null);
                Assert.That(data.charlieAndFabricTexture, Is.Null);
            }
            finally
            {
                ggxDisneyDiffuse?.Release();
                charlieAndFabric?.Release();
            }
        }

        [Test]
        public void VividPreIntegratedFGDSystem_InheritsFromFrameContextSubsystem()
        {
            Assert.That(
                typeof(VividSubsystem<VividPreIntegratedFGDSystem>).IsAssignableFrom(typeof(VividPreIntegratedFGDSystem)),
                Is.True);
        }
    }
}
