using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    public class AreaLightSystem
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


        private static AreaLightSystem _instance = new AreaLightSystem();

        public static AreaLightSystem instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new AreaLightSystem();
                }

                return _instance;
            }
        }

        internal const int k_LtcLUTResolution = 64;


        Texture2DArray m_LtcData;
        
        internal void Build()
        {
            m_LtcData = new Texture2DArray(k_LtcLUTResolution, k_LtcLUTResolution, (int)LTCLightingModel.Count, GraphicsFormat.R16G16B16A16_SFloat,
                TextureCreationFlags.None)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = CoreUtils.GetTextureAutoName(k_LtcLUTResolution, k_LtcLUTResolution, GraphicsFormat.R16G16B16A16_SFloat,
                    depth: (int)LTCLightingModel.Count, dim: TextureDimension.Tex2DArray, name: "LTC_LUT")
            };

            m_LtcData.SetPixelData(LTCAreaLight.s_LtcMatrixData_BRDF_GGX, 0, (int)LTCLightingModel.GGX);
            m_LtcData.SetPixelData(LTCAreaLight.s_LtcMatrixData_BRDF_Disney, 0, (int)LTCLightingModel.DisneyDiffuse);
            m_LtcData.SetPixelData(LTCAreaLight.s_LtcMatrixData_BRDF_Charlie, 0, (int)LTCLightingModel.Charlie);
            m_LtcData.SetPixelData(LTCAreaLight.s_LtcMatrixData_BRDF_KajiyaKaySpecular, 0, (int)LTCLightingModel.KajiyaKaySpecular);
            m_LtcData.SetPixelData(LTCAreaLight.s_LtcMatrixData_BRDF_CookTorrance, 0, (int)LTCLightingModel.CookTorrance);
            m_LtcData.SetPixelData(LTCAreaLight.s_LtcMatrixData_BRDF_Ward, 0, (int)LTCLightingModel.Ward);
            m_LtcData.SetPixelData(LTCAreaLight.s_LtcMatrixData_BRDF_OrenNayar, 0, (int)LTCLightingModel.OrenNayar);

            m_LtcData.Apply();
        }

        internal void Cleanup()
        {
            CoreUtils.Destroy(m_LtcData);
        }

        internal void Bind(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture("_LtcData", m_LtcData);
        }
    }
}