using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class DeferredDirectionalLightingPass : UnsafePass, IAllowGlobalStateModificationPass
    {
        private const int ClearThreadGroupSizeX = 8;
        private const int ClearThreadGroupSizeY = 8;
        private const int MaterialTileSize = 8;
        private const string ClearDeferredLitKernelName = "ClearDeferredLit";
        private const string DeferredLitKernelName = "DeferredLit";

        private static readonly int GBuffer0Id = Shader.PropertyToID("_GBuffer0");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int GBuffer2Id = Shader.PropertyToID("_GBuffer2");
        private static readonly int GBuffer3Id = Shader.PropertyToID("_GBuffer3");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int LightingTextureId = Shader.PropertyToID("_LightingTexture");
        private static readonly int LightingWidthId = Shader.PropertyToID("_LightingWidth");
        private static readonly int LightingHeightId = Shader.PropertyToID("_LightingHeight");
        private static readonly int MaterialPixelIndicesId = Shader.PropertyToID("_MaterialPixelIndices");
        private static readonly int MaterialDispatchArgsId = Shader.PropertyToID("_MaterialDispatchArgs");
        private static readonly int SkyIBLCubemapId = Shader.PropertyToID("_VividSkyIBLCubemap");
        private static readonly int SkyIBLTintId = Shader.PropertyToID("_VividSkyIBLTint");
        private static readonly int SkyIBLParamsId = Shader.PropertyToID("_VividSkyIBLParams");

        [RenderGraphResource(Name = "GBuffer0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "GBuffer2", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer2;

        [RenderGraphResource(Name = "GBuffer3", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer3;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "StandardMaterialIndices", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_StandardMaterialIndices;

        [RenderGraphResource(Name = "FabricMaterialIndices", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_FabricMaterialIndices;

        [RenderGraphResource(Name = "ClearCoatMaterialIndices", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ClearCoatMaterialIndices;

        [RenderGraphResource(Name = "StandardIndirectArgs", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_StandardIndirectArgs;

        [RenderGraphResource(Name = "FabricIndirectArgs", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_FabricIndirectArgs;

        [RenderGraphResource(Name = "ClearCoatIndirectArgs", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ClearCoatIndirectArgs;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Write, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTexture;

        private ComputeShader m_DeferredLitCompute;
        private int m_ClearDeferredLitKernel = -1;
        private int m_DeferredLitKernel = -1;
        private int m_LightingWidth = 1;
        private int m_LightingHeight = 1;
        private int m_ClearDispatchGroupCountX = 1;
        private int m_ClearDispatchGroupCountY = 1;
        private int m_MaterialDispatchGroupCountX = 1;
        private VividPreIntegratedFGD m_PreIntegratedFGD;

        public DeferredDirectionalLightingPass()
        {
            profilingSampler = new ProfilingSampler(nameof(DeferredDirectionalLightingPass));

            m_GBuffer0 = CreateInputTexture("GBuffer0", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer1 = CreateInputTexture("GBuffer1", GraphicsFormat.R16G16_SFloat);
            m_GBuffer2 = CreateInputTexture("GBuffer2", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer3 = CreateInputTexture("GBuffer3", GraphicsFormat.B10G11R11_UFloatPack32);
            m_DepthTexture = CreateDepthTexture("Depth");
            m_StandardMaterialIndices = CreateStructuredBuffer("StandardMaterialIndices");
            m_FabricMaterialIndices = CreateStructuredBuffer("FabricMaterialIndices");
            m_ClearCoatMaterialIndices = CreateStructuredBuffer("ClearCoatMaterialIndices");
            m_StandardIndirectArgs = CreateIndirectArgsBuffer("StandardIndirectArgs");
            m_FabricIndirectArgs = CreateIndirectArgsBuffer("FabricIndirectArgs");
            m_ClearCoatIndirectArgs = CreateIndirectArgsBuffer("ClearCoatIndirectArgs");
            m_ColorTexture = CreateOutputTexture("Color", GraphicsFormat.R16G16B16A16_SFloat);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_PreIntegratedFGD = new VividPreIntegratedFGD();
            m_PreIntegratedFGD.Create(resources);
            m_DeferredLitCompute = resources?.DeferredLitCompute;

            if (m_DeferredLitCompute == null)
            {
                Debug.LogWarning($"[VividRP] Could not find compute shader resource 'Shaders/Material/DeferredLit' for {nameof(DeferredDirectionalLightingPass)}.");
                return;
            }

            m_ClearDeferredLitKernel = m_DeferredLitCompute.FindKernel(ClearDeferredLitKernelName);
            m_DeferredLitKernel = m_DeferredLitCompute.FindKernel(DeferredLitKernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            var height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0)
                width = Mathf.Max(1, Screen.width);

            if (height <= 0)
                height = Mathf.Max(1, Screen.height);

            m_LightingWidth = width;
            m_LightingHeight = height;
            m_ClearDispatchGroupCountX = Mathf.Max(1, (width + ClearThreadGroupSizeX - 1) / ClearThreadGroupSizeX);
            m_ClearDispatchGroupCountY = Mathf.Max(1, (height + ClearThreadGroupSizeY - 1) / ClearThreadGroupSizeY);
            var tileCountX = Mathf.Max(1, (width + MaterialTileSize - 1) / MaterialTileSize);
            var tileCountY = Mathf.Max(1, (height + MaterialTileSize - 1) / MaterialTileSize);
            m_MaterialDispatchGroupCountX = Mathf.Max(1, tileCountX * tileCountY);

            ResizeTexture(m_GBuffer0, width, height);
            ResizeTexture(m_GBuffer1, width, height);
            ResizeTexture(m_GBuffer2, width, height);
            ResizeTexture(m_GBuffer3, width, height);
            ResizeTexture(m_DepthTexture, width, height);
            ResizeTexture(m_ColorTexture, width, height);
        }

        public override void Record(UnsafeGraphContext context)
        {
            if (m_DeferredLitCompute == null
                || m_ClearDeferredLitKernel < 0
                || m_DeferredLitKernel < 0)
            {
                return;
            }

            var cmd = context.cmd;
            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);

            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                m_PreIntegratedFGD?.Bind(nativeCmd);
                BindSkyIblGlobals(nativeCmd);

                BindSharedParameters(cmd, m_ClearDeferredLitKernel);
                cmd.DispatchCompute(m_DeferredLitCompute, m_ClearDeferredLitKernel, m_ClearDispatchGroupCountX, m_ClearDispatchGroupCountY, 1);

                BindSharedParameters(cmd, m_DeferredLitKernel);
                DispatchMaterialClass(cmd, m_StandardMaterialIndices, m_StandardIndirectArgs);
                DispatchMaterialClass(cmd, m_FabricMaterialIndices, m_FabricIndirectArgs);
                DispatchMaterialClass(cmd, m_ClearCoatMaterialIndices, m_ClearCoatIndirectArgs);
            }
        }

        public override void Dispose()
        {
            if (m_PreIntegratedFGD != null)
            {
                m_PreIntegratedFGD.Dispose();
                m_PreIntegratedFGD = null;
            }

            m_DeferredLitCompute = null;
            m_ClearDeferredLitKernel = -1;
            m_DeferredLitKernel = -1;
        }

        internal static Vector4 BuildSkyIblParams(Cubemap skyCubemap, float exposure, float rotation)
        {
            var maxMip = skyCubemap != null ? Mathf.Max(0, skyCubemap.mipmapCount - 1) : 0f;
            var enabled = skyCubemap != null ? 1f : 0f;
            return new Vector4(Mathf.Max(0f, exposure), -rotation, maxMip, enabled);
        }

        private void BindSharedParameters(UnsafeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GBuffer0Id, m_GBuffer0.innerHandle);
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GBuffer2Id, m_GBuffer2.innerHandle);
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, GBuffer3Id, m_GBuffer3.innerHandle);
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, DepthTextureId, m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(m_DeferredLitCompute, kernel, LightingTextureId, m_ColorTexture.innerHandle);
            cmd.SetComputeIntParam(m_DeferredLitCompute, LightingWidthId, m_LightingWidth);
            cmd.SetComputeIntParam(m_DeferredLitCompute, LightingHeightId, m_LightingHeight);
        }

        private void DispatchMaterialClass(UnsafeCommandBuffer cmd, RenderGraphBuffer materialIndices, RenderGraphBuffer materialDispatchArgs)
        {
            cmd.SetComputeBufferParam(m_DeferredLitCompute, m_DeferredLitKernel, MaterialPixelIndicesId, materialIndices.innerHandle);
            cmd.SetComputeBufferParam(m_DeferredLitCompute, m_DeferredLitKernel, MaterialDispatchArgsId, materialDispatchArgs.innerHandle);
            cmd.DispatchCompute(m_DeferredLitCompute, m_DeferredLitKernel, m_MaterialDispatchGroupCountX, 1, 1);
        }

        private static RenderGraphTexture CreateInputTexture(string name, GraphicsFormat format)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, format)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            return texture;
        }

        private static RenderGraphTexture CreateDepthTexture(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, DepthBits.Depth32)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            return texture;
        }

        private static RenderGraphTexture CreateOutputTexture(string name, GraphicsFormat format)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, format)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.clear;
            texture.desc.EnableRandomWrite = true;
            return texture;
        }

        private static RenderGraphBuffer CreateStructuredBuffer(string name)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = 1,
                    Stride = sizeof(uint),
                    Target = GraphicsBuffer.Target.Structured,
                    Name = name
                }
            };
        }

        private static RenderGraphBuffer CreateIndirectArgsBuffer(string name)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = 4,
                    Stride = sizeof(uint),
                    Target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                    Name = name
                }
            };
        }

        private static void ResizeTexture(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = width;
            texture.desc.Height = height;
        }

        private static void BindSkyIblGlobals(CommandBuffer cmd)
        {
            var skySettings = VolumeManager.instance.stack?.GetComponent<HDRISkyVolume>();
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var skyCubemap = skySettings?.GetSkyCubemapOrDefault() ?? resources?.DefaultHDRISkyCubemap;
            var skyTint = skySettings?.tint.value ?? Color.white;
            var skyExposure = skySettings?.exposure.value ?? 1f;
            var skyRotation = skySettings?.rotation.value ?? 0f;

            if (skyCubemap != null)
                cmd.SetGlobalTexture(SkyIBLCubemapId, skyCubemap);

            cmd.SetGlobalColor(SkyIBLTintId, skyTint);
            cmd.SetGlobalVector(SkyIBLParamsId, BuildSkyIblParams(skyCubemap, skyExposure, skyRotation));
        }
    }
}
