using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    [GenerateHLSL]
    internal enum LTCLightingModel
    {
        // Lit, Stack-Lit and Fabric/Silk
        GGX,
        DisneyDiffuse,

        // Fabric/CottonWool shader
        Charlie,
        // FabricLambert, (Isotropic)

        // Hair
        KajiyaKaySpecular,

        // KajiyaKayDiffuse, (Isotropic)
        Marschner, // TODO

        // Other
        CookTorrance,
        Ward,
        OrenNayar,
        Count
    }


    public class LTCAreaLightPreparePass : ComputePass
    {
        [RenderGraphResource(Access = AccessFlags.Write, Name = "LTCAreaLightData")]
        private RenderGraphTexture LTCData = new RenderGraphTexture();

        internal const int k_LtcLUTResolution = 64;

        public override void Create()
        {
        }

        public override void Dispose()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            LTCData.desc.Slices = (int)LTCLightingModel.Count;
            
        }

        public override void Record(ComputeGraphContext context)
        {
            throw new System.NotImplementedException();
        }
    }
}