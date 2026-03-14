using UnityEngine;
using UnityEngine.Assertions;
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

        private void BuildLTCData(Texture texture)
        {
            var ltcData = texture as Texture2DArray;
            Assert.IsTrue(ltcData);
            ltcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_GGX, 0, (int)LTCLightingModel.GGX);
            ltcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_Disney, 0, (int)LTCLightingModel.DisneyDiffuse);
            ltcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_Charlie, 0, (int)LTCLightingModel.Charlie);
            ltcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_KajiyaKaySpecular, 0, (int)LTCLightingModel.KajiyaKaySpecular);
            ltcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_CookTorrance, 0, (int)LTCLightingModel.CookTorrance);
            ltcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_Ward, 0, (int)LTCLightingModel.Ward);
            ltcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_OrenNayar, 0, (int)LTCLightingModel.OrenNayar);
            ltcData.Apply();
        }

        public override void Record(ComputeGraphContext context)
        {
            BuildLTCData(LTCData);
        }
    }
}