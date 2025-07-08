using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// PreIntegratedFGD
    /// </summary>
    sealed class PreIntegratedFGD
    {
        [GenerateHLSL]
        public enum FGDTexture
        {
            Resolution = 64
        }

        static PreIntegratedFGD s_Instance;

        public static PreIntegratedFGD instance
        {
            get
            {
                if (s_Instance == null)
                    s_Instance = new PreIntegratedFGD();

                return s_Instance;
            }
        }

        public enum FGDIndex
        {
            FGD_GGXAndDisneyDiffuse = 0,
            FGD_CharlieAndFabricLambert = 1,
            FGD_Marschner = 2,
            Count = 2
        }

        bool[] m_isInit = new bool[(int)FGDIndex.Count];
        int[] m_refCounting = new int[(int)FGDIndex.Count];

        Material[] m_PreIntegratedFGDMaterial = new Material[(int)FGDIndex.Count];
        RenderTexture[] m_PreIntegratedFGD = new RenderTexture[(int)FGDIndex.Count];
        RTHandle[] m_PreIntegratedFGDHandles = new RTHandle[(int)FGDIndex.Count];

        PreIntegratedFGD()
        {
            for (int i = 0; i < (int)FGDIndex.Count; ++i)
            {
                m_isInit[i] = false;
                m_refCounting[i] = 0;
            }
        }

        private Shader GetShaderForIndex(FGDIndex index)
        {
            var shaders = GraphicsSettings.GetRenderPipelineSettings<PreIntegratedRuntimeShader>();

            if (shaders == null)
            {
                Debug.LogError($"Invalid resources to get shader for {index}.");
                return null;
            }

            switch (index)
            {
                case FGDIndex.FGD_GGXAndDisneyDiffuse: return shaders.preIntegratedFGD_GGXDisneyDiffusePS;
                case FGDIndex.FGD_CharlieAndFabricLambert: return shaders.preIntegratedFGD_CharlieFabricLambertPS;
                case FGDIndex.FGD_Marschner: return shaders.preIntegratedFGD_MarschnerPS;
                default: Debug.LogError($"Unable to get shader for index: {index}."); break;
            }

            return null;
        }

        /// <summary>
        /// Build in pipeline constructor.
        /// </summary>
        /// <param name="index"></param>
        public void Build(FGDIndex index)
        {
            Debug.Assert(index != FGDIndex.Count);
            Debug.Assert(m_refCounting[(int)index] >= 0);

            if (m_refCounting[(int)index] == 0)
            {
                Shader pixelShader = GetShaderForIndex(index);
                int res = (int)FGDTexture.Resolution;
                m_PreIntegratedFGDMaterial[(int)index] = CoreUtils.CreateEngineMaterial(pixelShader);
                m_PreIntegratedFGD[(int)index] = new RenderTexture(res, res, 0, GraphicsFormat.A2B10G10R10_UNormPack32)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = CoreUtils.GetRenderTargetAutoName(res, res, 1, GraphicsFormat.A2B10G10R10_UNormPack32, $"preIntegrated{index}")
                };
                m_PreIntegratedFGD[(int)index].Create();
                m_PreIntegratedFGDHandles[(int)index] = RTHandles.Alloc(m_PreIntegratedFGD[(int)index]);
                m_isInit[(int)index] = false;
            }

            m_refCounting[(int)index]++;
        }

        /// <summary>
        /// InitializeGlobalResources, recreated if texture is internally destoryed.
        /// </summary>
        /// <param name="index"></param>
        /// <param name="cmd"></param>
        public void RenderInit(FGDIndex index, CommandBuffer cmd)
        {
            // Here we have to test IsCreated because in some circumstances (like loading RenderDoc), the texture is internally destroyed but we don't know from C# side.
            // In this case IsCreated will return false, allowing us to re-render the texture (setting the texture as current RT during DrawFullScreen will automatically re-create it internally)
            if (m_isInit[(int)index] && m_PreIntegratedFGD[(int)index].IsCreated())
                return;

            // If we are in wireframe mode, the drawfullscreen will not work as expected, but we don't need the LUT anyway
            // So create the texture to avoid errors, it will be initialized by the first render without wireframe
            if (GL.wireframe)
            {
                m_PreIntegratedFGD[(int)index].Create();
                return;
            }

            CoreUtils.DrawFullScreen(cmd, m_PreIntegratedFGDMaterial[(int)index], new RenderTargetIdentifier(m_PreIntegratedFGD[(int)index]));
            m_isInit[(int)index] = true;
        }

        /// <summary>
        /// Dispose cleanup resources.
        /// </summary>
        /// <param name="index"></param>
        public void Cleanup(FGDIndex index)
        {
            m_refCounting[(int)index]--;

            if (m_refCounting[(int)index] == 0)
            {
                CoreUtils.Destroy(m_PreIntegratedFGDMaterial[(int)index]);
                CoreUtils.Destroy(m_PreIntegratedFGD[(int)index]);

                m_isInit[(int)index] = false;
            }

            Debug.Assert(m_refCounting[(int)index] >= 0);
        }

        /// <summary>
        /// Bind material textures, global parameters is not recommended. But we have to do this.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="index"></param>
        public void Bind(CommandBuffer cmd, FGDIndex index)
        {
            switch (index)
            {
                case FGDIndex.FGD_GGXAndDisneyDiffuse:
                    cmd.SetGlobalTexture(ShaderConstants._PreIntegratedFGD_GGXDisneyDiffuse, m_PreIntegratedFGD[(int)index]);
                    break;

                case FGDIndex.FGD_CharlieAndFabricLambert:
                    cmd.SetGlobalTexture(ShaderConstants._PreIntegratedFGD_CharlieAndFabric, m_PreIntegratedFGD[(int)index]);
                    break;

                case FGDIndex.FGD_Marschner:
                    cmd.SetGlobalTexture(ShaderConstants._PreIntegratedFGD_CharlieAndFabric, m_PreIntegratedFGD[(int)index]);
                    break;
                default:
                    break;
            }
        }
        /// <summary>
        /// Bind material textures, global parameters is not recommended. But we have to do this.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="index"></param>
        public void Bind(RasterCommandBuffer cmd, FGDIndex index, TextureHandle texture)
        {
            switch (index)
            {
                case FGDIndex.FGD_GGXAndDisneyDiffuse:
                    cmd.SetGlobalTexture(ShaderConstants._PreIntegratedFGD_GGXDisneyDiffuse, texture);
                    break;

                case FGDIndex.FGD_CharlieAndFabricLambert:
                    cmd.SetGlobalTexture(ShaderConstants._PreIntegratedFGD_CharlieAndFabric, texture);
                    break;

                case FGDIndex.FGD_Marschner:
                    cmd.SetGlobalTexture(ShaderConstants._PreIntegratedFGD_CharlieAndFabric, texture);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Bind material textures.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="index"></param>
        public void Bind(ComputeCommandBuffer cmd, FGDIndex index, TextureHandle texture)
        {
            switch (index)
            {
                case FGDIndex.FGD_GGXAndDisneyDiffuse:
                    cmd.SetGlobalTexture(ShaderConstants._PreIntegratedFGD_GGXDisneyDiffuse, texture);
                    break;

                case FGDIndex.FGD_CharlieAndFabricLambert:
                    cmd.SetGlobalTexture(ShaderConstants._PreIntegratedFGD_CharlieAndFabric, texture);
                    break;
                case FGDIndex.FGD_Marschner:
                    cmd.SetGlobalTexture(ShaderConstants._PreIntegratedFGD_CharlieAndFabric, texture);
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// RenderGraph Path, Import texture and add it to your passData.
        /// </summary>
        /// <param name="renderGraph"></param>
        /// <param name="index"></param>
        public TextureHandle ImportToRenderGraph(RenderGraph renderGraph, FGDIndex index)
        {
            switch (index)
            {
                case FGDIndex.FGD_GGXAndDisneyDiffuse:
                    return renderGraph.ImportTexture(m_PreIntegratedFGDHandles[(int)index]);
                case FGDIndex.FGD_CharlieAndFabricLambert:
                    return renderGraph.ImportTexture(m_PreIntegratedFGDHandles[(int)index]);
                default:
                    return TextureHandle.nullHandle;
            }
        }

        internal static class ShaderConstants
        {
            public static readonly int _PreIntegratedFGD_GGXDisneyDiffuse = Shader.PropertyToID("_PreIntegratedFGD_GGXDisneyDiffuse");
            public static readonly int _PreIntegratedFGD_CharlieAndFabric = Shader.PropertyToID("_PreIntegratedFGD_CharlieAndFabric");

        }
    }
}