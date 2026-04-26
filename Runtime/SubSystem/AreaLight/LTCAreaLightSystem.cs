
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

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


    public static class LTCAreaLightSystem
    {
        static Texture2DArray m_LtcData;

        const int k_LtcLUTResolution = 64;
        static bool s_Initialized;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        internal static void Initialize()
        {
            if (s_Initialized)
                return;

            BuildLTCData();
            FrameContextSystem.SubsystemPreRender -= Update;
            FrameContextSystem.SubsystemPreRender += Update;
            s_Initialized = true;
        }

        static void BuildLTCData()
        {
            CoreUtils.Destroy(m_LtcData);
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
            Debug.Assert(m_LtcData != null);
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


        internal static void Update(ContextContainer frameData, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemLTCAreaLightMarker.Auto())
            {
                UpdateCore(frameData, cmd);
            }
        }

        private static void UpdateCore(ContextContainer frameData, CommandBuffer cmd)
        {
            if (!s_Initialized)
                Initialize();

            cmd.SetGlobalTexture("_LtcData", m_LtcData);
        }


        internal static void Deinitialize()
        {
#if !UNITY_EDITOR
            FrameContextSystem.SubsystemPreRender -= Update;
#endif
            CoreUtils.Destroy(m_LtcData);
            m_LtcData = null;
            s_Initialized = false;
        }
    }
}
