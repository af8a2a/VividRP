using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public sealed class VividPreIntegratedFGDData : ContextItem
    {
        internal RTHandle ggxDisneyDiffuseTexture;
        internal RTHandle charlieAndFabricTexture;
        internal bool hasValidTextures;

        public override void Reset()
        {
            ggxDisneyDiffuseTexture = null;
            charlieAndFabricTexture = null;
            hasValidTextures = false;
        }

        internal void SetTextures(RTHandle ggxDisneyDiffuse, RTHandle charlieAndFabric)
        {
            ggxDisneyDiffuseTexture = ggxDisneyDiffuse;
            charlieAndFabricTexture = charlieAndFabric;
            hasValidTextures = ggxDisneyDiffuseTexture != null && charlieAndFabricTexture != null;
        }
    }
}
