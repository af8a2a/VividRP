using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class DeferredDirectionalLightingPass : RasterPass, IAllowGlobalStateModificationPass
    {
        internal const string DeferredDirectionalLightingIndirectShaderName = "Hidden/VividRP/DeferredDirectionalLightingIndirect";

        private static readonly int GBuffer0Id = Shader.PropertyToID("_GBuffer0");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int GBuffer2Id = Shader.PropertyToID("_GBuffer2");
        private static readonly int GBuffer3Id = Shader.PropertyToID("_GBuffer3");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int LightingWidthId = Shader.PropertyToID("_LightingWidth");
        private static readonly int LightingHeightId = Shader.PropertyToID("_LightingHeight");
        private static readonly int MaterialPixelIndicesId = Shader.PropertyToID("_MaterialPixelIndices");

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

        private Material m_DeferredDirectionalLightingMaterial;
        private int m_LightingWidth = 1;
        private int m_LightingHeight = 1;

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
            var shader = resources?.DeferredDirectionalLightingIndirectShader;
            shader ??= Shader.Find(DeferredDirectionalLightingIndirectShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{DeferredDirectionalLightingIndirectShaderName}' for {nameof(DeferredDirectionalLightingPass)}.");
                return;
            }

            m_DeferredDirectionalLightingMaterial = CoreUtils.CreateEngineMaterial(shader);
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

            ResizeTexture(m_GBuffer0, width, height);
            ResizeTexture(m_GBuffer1, width, height);
            ResizeTexture(m_GBuffer2, width, height);
            ResizeTexture(m_GBuffer3, width, height);
            ResizeTexture(m_DepthTexture, width, height);
            ResizeTexture(m_ColorTexture, width, height);
        }

        public override void Record(RasterGraphContext context)
        {
            if (m_DeferredDirectionalLightingMaterial == null)
                return;

            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                context.cmd.SetGlobalTexture(GBuffer0Id, m_GBuffer0.innerHandle);
                context.cmd.SetGlobalTexture(GBuffer1Id, m_GBuffer1.innerHandle);
                context.cmd.SetGlobalTexture(GBuffer2Id, m_GBuffer2.innerHandle);
                context.cmd.SetGlobalTexture(GBuffer3Id, m_GBuffer3.innerHandle);
                context.cmd.SetGlobalTexture(DepthTextureId, m_DepthTexture.innerHandle);
                context.cmd.SetGlobalInt(LightingWidthId, m_LightingWidth);
                context.cmd.SetGlobalInt(LightingHeightId, m_LightingHeight);

                DrawMaterialClass(context.cmd, m_StandardMaterialIndices, m_StandardIndirectArgs);
                DrawMaterialClass(context.cmd, m_FabricMaterialIndices, m_FabricIndirectArgs);
                DrawMaterialClass(context.cmd, m_ClearCoatMaterialIndices, m_ClearCoatIndirectArgs);
            }
        }

        public override void Dispose()
        {
            if (m_DeferredDirectionalLightingMaterial != null)
            {
                CoreUtils.Destroy(m_DeferredDirectionalLightingMaterial);
                m_DeferredDirectionalLightingMaterial = null;
            }
        }

        private void DrawMaterialClass(RasterCommandBuffer cmd, RenderGraphBuffer materialIndices, RenderGraphBuffer indirectArgs)
        {
            if (materialIndices?.ImportedGraphicsBuffer == null || indirectArgs?.ImportedGraphicsBuffer == null)
                return;

            cmd.SetGlobalBuffer(MaterialPixelIndicesId, materialIndices.ImportedGraphicsBuffer);
            cmd.DrawProceduralIndirect(
                Matrix4x4.identity,
                m_DeferredDirectionalLightingMaterial,
                0,
                MeshTopology.Points,
                indirectArgs.ImportedGraphicsBuffer);
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
            texture.desc.EnableRandomWrite = false;
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
    }
}
