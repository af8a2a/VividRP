using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class ShadowClassifyPass : ComputePass
    {
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const string KernelName = "ShadowClassify";

        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int ShadowClassifyMaskId = Shader.PropertyToID("_ShadowClassifyMask");
        private static readonly int LightDirectionWSId = Shader.PropertyToID("_LightDirectionWS");
        private static readonly int NormalFacingThresholdId = Shader.PropertyToID("_NormalFacingThreshold");

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "ShadowClassifyMask", Access = AccessFlags.Write)]
        private RenderGraphTexture m_ShadowClassifyMask;

        private ComputeShader m_ClassifyCompute;
        private int m_Kernel = -1;
        private int m_DispatchGroupCountX = 1;
        private int m_DispatchGroupCountY = 1;
        private Vector4 m_LightDirectionWS = new Vector4(0f, 1f, 0f, 0f);

        private const float DefaultNormalFacingThreshold = 0.0f;

        public ShadowClassifyPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ShadowClassifyPass));
            m_DepthTexture = CreateInputTexture("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_GBuffer1 = CreateInputTexture("GBuffer1", GraphicsFormat.R16G16_SFloat);
            m_ShadowClassifyMask = CreateMaskTexture("ShadowClassifyMask");
        }

        public override void Create()
        {
            m_ClassifyCompute =
                PipelineResourceManager.Get<VividRPCoreResources>()?.ShadowClassifyCompute;

            if (m_ClassifyCompute == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find compute shader resource for {nameof(ShadowClassifyPass)}.");
                return;
            }

            m_Kernel = m_ClassifyCompute.FindKernel(KernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            ConfigureMaskTexture(cameraData.actualWidth, cameraData.actualHeight);

            m_DispatchGroupCountX = CoreUtils.DivRoundUp(cameraData.actualWidth, ThreadGroupSizeX);
            m_DispatchGroupCountY = CoreUtils.DivRoundUp(cameraData.actualHeight, ThreadGroupSizeY);

            var lightData = frameData.GetOrCreate<VividLightData>();
            if (lightData != null && lightData.hasMainDirectionalLight)
            {
                var dir = lightData.mainDirectionalLight.directionWS;
                m_LightDirectionWS = new Vector4(dir.x, dir.y, dir.z, 0f);
            }
            else
            {
                m_LightDirectionWS = new Vector4(0f, 1f, 0f, 0f);
            }
        }

        public override void Record(ComputeGraphContext context)
        {
            if (m_ClassifyCompute == null || m_Kernel < 0)
                return;

            if (!m_DepthTexture.innerHandle.IsValid()
                || !m_GBuffer1.innerHandle.IsValid()
                || !m_ShadowClassifyMask.innerHandle.IsValid())
                return;

            var cmd = context.cmd;

            cmd.SetComputeTextureParam(m_ClassifyCompute, m_Kernel,
                DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_ClassifyCompute, m_Kernel,
                GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_ClassifyCompute, m_Kernel,
                ShadowClassifyMaskId, m_ShadowClassifyMask.innerHandle);

            cmd.SetComputeVectorParam(m_ClassifyCompute, LightDirectionWSId, m_LightDirectionWS);
            cmd.SetComputeFloatParam(m_ClassifyCompute, NormalFacingThresholdId, DefaultNormalFacingThreshold);

            cmd.DispatchCompute(m_ClassifyCompute, m_Kernel,
                m_DispatchGroupCountX, m_DispatchGroupCountY, 1);
        }

        public override void Dispose()
        {
            m_ClassifyCompute = null;
            m_Kernel = -1;
            m_DispatchGroupCountX = 1;
            m_DispatchGroupCountY = 1;
            m_LightDirectionWS = new Vector4(0f, 1f, 0f, 0f);
        }

        private void ConfigureMaskTexture(int width, int height)
        {
            if (m_ShadowClassifyMask?.desc == null)
                return;

            m_ShadowClassifyMask.desc.Width = width;
            m_ShadowClassifyMask.desc.Height = height;
            m_ShadowClassifyMask.desc.EnableRandomWrite = true;
        }

        private static RenderGraphTexture CreateInputTexture(string name, GraphicsFormat format, DepthBits depthBits = DepthBits.None)
        {
            var texture = new RenderGraphTexture
            {
                desc = format == GraphicsFormat.None
                    ? RenderGraphTextureDesc.CreateDepthTarget(1, 1, depthBits)
                    : RenderGraphTextureDesc.CreateColorTarget(1, 1, format)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            texture.desc.EnableRandomWrite = false;
            return texture;
        }

        private static RenderGraphTexture CreateMaskTexture(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R8_UNorm)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.clear;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.EnableRandomWrite = true;
            return texture;
        }
    }
}
