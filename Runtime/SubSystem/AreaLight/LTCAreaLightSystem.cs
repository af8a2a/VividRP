
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


    public sealed class LTCAreaLightSystem : VividSubsystem<LTCAreaLightSystem>
    {
        const int k_LtcLUTResolution = 64;

        Texture2DArray m_LtcData;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void AutoInitialize()
        {
            Initialize();
        }

        protected override void OnInitialize()
        {
            BuildLTCData();
        }

        protected override void OnDeinitialize()
        {
            CoreUtils.Destroy(m_LtcData);
            m_LtcData = null;
        }

        public new static void Deinitialize()
        {
            VividSubsystem<LTCAreaLightSystem>.Deinitialize();

#if UNITY_EDITOR
            // Keep the FrameContext callback wired in editor so the next preview render lazily
            // rebuilds the LUT; OnDeinitialize already released the previous GPU texture.
            EnsurePreRenderSubscribed();
#endif
        }

        void BuildLTCData()
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


        protected override void OnUpdate(ContextContainer frameData, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemLTCAreaLightMarker.Auto())
            {
                UpdateCore(frameData, cmd);
            }
        }

        private void UpdateCore(ContextContainer frameData, CommandBuffer cmd)
        {
            if (m_LtcData == null)
                BuildLTCData();

            cmd.SetGlobalTexture("_LtcData", m_LtcData);
        }
    }
}
