using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class VividPreIntegratedFGD
    {
        internal const int TextureResolution = 64;
        internal const string GGXDisneyDiffuseShaderName = "Hidden/VividRP/PreIntegratedFGD_GGXDisneyDiffuse";
        internal const string CharlieFabricLambertShaderName = "Hidden/VividRP/PreIntegratedFGD_CharlieFabricLambert";

        internal static readonly int GGXDisneyDiffuseTextureId = Shader.PropertyToID("_PreIntegratedFGD_GGXDisneyDiffuse");
        internal static readonly int CharlieAndFabricTextureId = Shader.PropertyToID("_PreIntegratedFGD_CharlieAndFabric");

        internal static Material CreateGGXDisneyDiffuseMaterial(VividRPCoreResources resources)
        {
            return CreateMaterial(resources?.PreIntegratedFGDGGXDisneyDiffuseShader, GGXDisneyDiffuseShaderName);
        }

        internal static Material CreateCharlieFabricLambertMaterial(VividRPCoreResources resources)
        {
            return CreateMaterial(resources?.PreIntegratedFGDCharlieFabricLambertShader, CharlieFabricLambertShaderName);
        }

        internal static RenderGraphTexture CreateTexture(string name)
        {
            return new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = TextureResolution,
                    Height = TextureResolution,
                    Dimension = TextureDimension.Tex2D,
                    ColorFormat = GraphicsFormat.A2B10G10R10_UNormPack32,
                    DepthBufferBits = DepthBits.None,
                    FilterMode = FilterMode.Bilinear,
                    WrapMode = TextureWrapMode.Clamp,
                    UseMipMap = false,
                    AutoGenerateMips = false,
                    MipCount = 1,
                    ClearBuffer = false,
                    EnableRandomWrite = false,
                    Name = name
                }
            };
        }

        internal static RTHandle CreatePersistentTexture(string name)
        {
            return RTHandles.Alloc(
                TextureResolution,
                TextureResolution,
                slices: 1,
                depthBufferBits: DepthBits.None,
                colorFormat: GraphicsFormat.A2B10G10R10_UNormPack32,
                filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp,
                dimension: TextureDimension.Tex2D,
                enableRandomWrite: false,
                useMipMap: false,
                autoGenerateMips: false,
                isShadowMap: false,
                anisoLevel: 1,
                mipMapBias: 0f,
                msaaSamples: MSAASamples.None,
                bindTextureMS: false,
                useDynamicScale: false,
                useDynamicScaleExplicit: false,
                name: name);
        }

        internal static void BuildTexture(CommandBuffer cmd, Material material, RTHandle target)
        {
            if (cmd == null || target == null)
                return;

            CoreUtils.SetRenderTarget(cmd, target, ClearFlag.Color, Color.clear);

            if (material == null || GL.wireframe)
                return;

            CoreUtils.DrawFullScreen(cmd, material);
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
    }

    internal sealed class VividPreIntegratedFGDTextures
    {
        private Material m_GgxDisneyDiffuseMaterial;
        private Material m_CharlieFabricLambertMaterial;
        private RTHandle m_GgxDisneyDiffuseTexture;
        private RTHandle m_CharlieFabricLambertTexture;
        private bool m_IsBuilt;

        internal RTHandle GGXDisneyDiffuseTexture => m_GgxDisneyDiffuseTexture;

        internal RTHandle CharlieAndFabricTexture => m_CharlieFabricLambertTexture;

        internal void Create(VividRPCoreResources resources)
        {
            if (m_IsBuilt)
                return;

            m_GgxDisneyDiffuseMaterial ??= VividPreIntegratedFGD.CreateGGXDisneyDiffuseMaterial(resources);
            m_CharlieFabricLambertMaterial ??= VividPreIntegratedFGD.CreateCharlieFabricLambertMaterial(resources);
            m_GgxDisneyDiffuseTexture ??= VividPreIntegratedFGD.CreatePersistentTexture("PreIntegratedFGD_GGXDisneyDiffuse");
            m_CharlieFabricLambertTexture ??= VividPreIntegratedFGD.CreatePersistentTexture("PreIntegratedFGD_CharlieAndFabric");

            var cmd = CommandBufferPool.Get(nameof(VividPreIntegratedFGDTextures));
            VividPreIntegratedFGD.BuildTexture(cmd, m_GgxDisneyDiffuseMaterial, m_GgxDisneyDiffuseTexture);
            VividPreIntegratedFGD.BuildTexture(cmd, m_CharlieFabricLambertMaterial, m_CharlieFabricLambertTexture);
            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            m_IsBuilt = true;
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

            m_GgxDisneyDiffuseTexture?.Release();
            m_GgxDisneyDiffuseTexture = null;

            m_CharlieFabricLambertTexture?.Release();
            m_CharlieFabricLambertTexture = null;

            m_IsBuilt = false;
        }
    }
}
