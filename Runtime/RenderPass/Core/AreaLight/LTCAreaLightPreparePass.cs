using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
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


    public class LTCAreaLightPreparePass : UnsafePass
    {
        Texture2DArray m_LtcData;

        internal const int k_LtcLUTResolution = 64;

        public override void Create()
        {
            BuildLTCData();
        }

        public override void Dispose()
        {
            CoreUtils.Destroy(m_LtcData);
        }
        
        

        public override void Prepare(ContextContainer frameData)
        {
        }

        private void BuildLTCData()
        {
            m_LtcData = new Texture2DArray(k_LtcLUTResolution, k_LtcLUTResolution, (int)LTCLightingModel.Count,
                GraphicsFormat.R16G16B16A16_SFloat,
                TextureCreationFlags.None)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = CoreUtils.GetTextureAutoName(k_LtcLUTResolution, k_LtcLUTResolution,
                    GraphicsFormat.R16G16B16A16_SFloat,
                    depth: (int)LTCLightingModel.Count, dim: TextureDimension.Tex2DArray, name: "LTC_LUT")
            };

            Assert.IsTrue(m_LtcData);
            m_LtcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_GGX, 0, (int)LTCLightingModel.GGX);
            m_LtcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_Disney, 0,
                (int)LTCLightingModel.DisneyDiffuse);
            m_LtcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_Charlie, 0, (int)LTCLightingModel.Charlie);
            m_LtcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_KajiyaKaySpecular, 0,
                (int)LTCLightingModel.KajiyaKaySpecular);
            m_LtcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_CookTorrance, 0,
                (int)LTCLightingModel.CookTorrance);
            m_LtcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_Ward, 0, (int)LTCLightingModel.Ward);
            m_LtcData.SetPixelData(LTCAreaLightData.s_LtcMatrixData_BRDF_OrenNayar, 0, (int)LTCLightingModel.OrenNayar);
            m_LtcData.Apply();
        }

        public override void Record(UnsafeGraphContext context)
        {
            context.cmd.SetGlobalTexture("_LtcData", m_LtcData);
        }
    }
}