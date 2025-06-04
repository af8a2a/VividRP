using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Features.Shadow.UberScreenSpaceShadow
{
    public class ScreenSpaceShadowsPass : ScriptableRenderPass
    {
        // Serialized Fields
        [SerializeField, HideInInspector] private Shader m_Shader = null;

        // Private Variables
        private Material m_Material;
        private ScreenSpaceShadowsSettings m_CurrentSettings;
        private int m_ScreenSpaceShadowmapTextureID;
        private PassData m_PassData;

        // Constants
        private const string k_ShaderName = "Hidden/CustomScreenSpaceShadows";


        internal ScreenSpaceShadowsPass()
        {
            LoadMaterial();

            profilingSampler = new ProfilingSampler("Blit Screen Space Shadows");
            m_CurrentSettings = new ScreenSpaceShadowsSettings();
            m_ScreenSpaceShadowmapTextureID = Shader.PropertyToID("_ScreenSpaceShadowmapTexture");
            m_PassData = new PassData();
        }

        private bool LoadMaterial()
        {
            if (m_Material != null)
            {
                return true;
            }

            if (m_Shader == null)
            {
                m_Shader = Shader.Find(k_ShaderName);
                if (m_Shader == null)
                {
                    return false;
                }
            }

            m_Material = CoreUtils.CreateEngineMaterial(m_Shader);


            return m_Material != null;
        }

        internal bool Setup(ScreenSpaceShadowsSettings featureSettings)
        {
            m_CurrentSettings = featureSettings;
            ConfigureInput(ScriptableRenderPassInput.Depth);

            return m_Material != null;
        }


        private class PassData
        {
            internal TextureHandle target;
            internal Material material;
            internal int shadowmapID;
        }

        /// <summary>
        /// Initialize the shared pass data.
        /// </summary>
        /// <param name="passData"></param>
        private void InitPassData(ref PassData passData)
        {
            passData.material = m_Material;
            passData.shadowmapID = m_ScreenSpaceShadowmapTextureID;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Material == null)
            {
                Debug.LogErrorFormat(
                    "{0}.Execute(): Missing material. ScreenSpaceShadows pass will not execute. Check for missing reference in the renderer resources.",
                    GetType().Name);
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            var desc = cameraData.cameraTargetDescriptor;
            desc.depthStencilFormat = GraphicsFormat.None;
            desc.msaaSamples = 1;
            // UUM-41070: We require `Linear | Render` but with the deprecated FormatUsage this was checking `Blend`
            // For now, we keep checking for `Blend` until the performance hit of doing the correct checks is evaluated
            desc.graphicsFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Blend)
                ? GraphicsFormat.R8_UNorm
                : GraphicsFormat.B8G8R8A8_UNorm;
            TextureHandle color = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_ScreenSpaceShadowmapTexture", true);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
            {
                passData.target = color;
                builder.SetRenderAttachment(color, 0, AccessFlags.Write);

                InitPassData(ref passData);
                builder.AllowGlobalStateModification(true);

                if (color.IsValid())
                    builder.SetGlobalTextureAfterPass(color, m_ScreenSpaceShadowmapTextureID);

                builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => { ExecutePass(rgContext.cmd, data, data.target); });
            }
        }

        private static void ExecutePass(RasterCommandBuffer cmd, PassData data, RTHandle target)
        {
            Blitter.BlitTexture(cmd, target, Vector2.one, data.material, 0);
            cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadows, false);
            cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadowCascades, false);
            cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadowScreen, true);
        }
    }
}