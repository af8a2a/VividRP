using System;
using NUnit.Framework;
using UnityEditor;
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
        [InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        internal static void Initialize()
        {
            BuildLTCData();
            FrameContextSystem.SubsystemPreRender += Update;
        }

        static void BuildLTCData()
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
#if UNITY_EDITOR
            Assert.IsTrue(m_LtcData);
#endif
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

            s_Initialized = true;
        }


        internal static void Update(ContextContainer frameData, CommandBuffer cmd)
        {
            if (!s_Initialized)
                Initialize();

            cmd.SetGlobalTexture("_LtcData", m_LtcData);
        }


        internal static void Deinitialize()
        {
            s_Initialized = false;
           CoreUtils.Destroy(m_LtcData);
           FrameContextSystem.SubsystemPreRender -= Update;
        }
    }
}