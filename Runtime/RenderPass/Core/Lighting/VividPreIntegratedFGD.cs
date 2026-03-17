using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VividPreIntegratedFGD
    {
        internal const int TextureResolution = 64;
        internal const string GGXDisneyDiffuseShaderName = "Hidden/VividRP/PreIntegratedFGD_GGXDisneyDiffuse";
        internal const string CharlieFabricLambertShaderName = "Hidden/VividRP/PreIntegratedFGD_CharlieFabricLambert";

        private static readonly int PreIntegratedFGDGGXDisneyDiffuseId = Shader.PropertyToID("_PreIntegratedFGD_GGXDisneyDiffuse");
        private static readonly int PreIntegratedFGDCharlieAndFabricId = Shader.PropertyToID("_PreIntegratedFGD_CharlieAndFabric");

        private Material m_GgxDisneyDiffuseMaterial;
        private Material m_CharlieFabricLambertMaterial;
        private RenderTexture m_GgxDisneyDiffuseTexture;
        private RenderTexture m_CharlieFabricLambertTexture;
        private bool m_IsGgxDisneyDiffuseInitialized;
        private bool m_IsCharlieFabricLambertInitialized;

        internal void Create(VividRPCoreResources resources)
        {
            m_GgxDisneyDiffuseMaterial = CreateMaterial(resources?.PreIntegratedFGDGGXDisneyDiffuseShader, GGXDisneyDiffuseShaderName);
            m_CharlieFabricLambertMaterial = CreateMaterial(resources?.PreIntegratedFGDCharlieFabricLambertShader, CharlieFabricLambertShaderName);
            m_GgxDisneyDiffuseTexture = CreateTexture("PreIntegratedFGD_GGXDisneyDiffuse");
            m_CharlieFabricLambertTexture = CreateTexture("PreIntegratedFGD_CharlieAndFabric");
        }

        internal void Bind(CommandBuffer cmd)
        {
            if (cmd == null)
                return;

            EnsureInitialized(cmd, m_GgxDisneyDiffuseMaterial, m_GgxDisneyDiffuseTexture, ref m_IsGgxDisneyDiffuseInitialized);
            EnsureInitialized(cmd, m_CharlieFabricLambertMaterial, m_CharlieFabricLambertTexture, ref m_IsCharlieFabricLambertInitialized);

            cmd.SetGlobalTexture(
                PreIntegratedFGDGGXDisneyDiffuseId,
                m_GgxDisneyDiffuseTexture != null ? m_GgxDisneyDiffuseTexture : Texture2D.blackTexture);
            cmd.SetGlobalTexture(
                PreIntegratedFGDCharlieAndFabricId,
                m_CharlieFabricLambertTexture != null ? m_CharlieFabricLambertTexture : Texture2D.blackTexture);
        }

        internal void Dispose()
        {
            if (m_GgxDisneyDiffuseMaterial != null)
            {
                CoreUtils.Destroy(m_GgxDisneyDiffuseMaterial);
                m_GgxDisneyDiffuseMaterial = null;
            }

            if (m_CharlieFabricLambertMaterial != null)
            {
                CoreUtils.Destroy(m_CharlieFabricLambertMaterial);
                m_CharlieFabricLambertMaterial = null;
            }

            if (m_GgxDisneyDiffuseTexture != null)
            {
                CoreUtils.Destroy(m_GgxDisneyDiffuseTexture);
                m_GgxDisneyDiffuseTexture = null;
            }

            if (m_CharlieFabricLambertTexture != null)
            {
                CoreUtils.Destroy(m_CharlieFabricLambertTexture);
                m_CharlieFabricLambertTexture = null;
            }

            m_IsGgxDisneyDiffuseInitialized = false;
            m_IsCharlieFabricLambertInitialized = false;
        }

        private static Material CreateMaterial(Shader shader, string fallbackShaderName)
        {
            shader ??= Shader.Find(fallbackShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{fallbackShaderName}' for pre-integrated FGD generation.");
                return null;
            }

            return CoreUtils.CreateEngineMaterial(shader);
        }

        private static RenderTexture CreateTexture(string name)
        {
            return new RenderTexture(TextureResolution, TextureResolution, 0)
            {
                graphicsFormat = GraphicsFormat.A2B10G10R10_UNormPack32,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                name = name
            };
        }

        private static void EnsureInitialized(
            CommandBuffer cmd,
            Material material,
            RenderTexture texture,
            ref bool isInitialized)
        {
            if (cmd == null || material == null || texture == null)
                return;

            if (isInitialized && texture.IsCreated())
                return;

            if (!texture.IsCreated())
                texture.Create();

            if (GL.wireframe)
                return;

            CoreUtils.DrawFullScreen(cmd, material, new RenderTargetIdentifier(texture));
            isInitialized = true;
        }
    }
}
