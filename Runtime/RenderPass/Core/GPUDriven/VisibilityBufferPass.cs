using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VisibilityBufferPass : RasterPass
    {
        internal const string VisibilityBufferShaderName = "Hidden/VividRP/GPUDriven/VisibilityBufferPass";

        private const int IndirectDrawArgsByteStride = sizeof(uint) * 4;

        private static readonly int s_CullId = Shader.PropertyToID("_Cull");
        private static readonly int s_VisibleMeshletRenderRequestsId = Shader.PropertyToID("_VisibleMeshletRenderRequests");
        private static readonly string s_AlphaTestKeyword = "_ALPHATEST_ON";

        [RenderGraphResource(Name = "VisibleMeshletRenderRequests", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_VisibleMeshletRenderRequests;

        [RenderGraphResource(Name = "VisibleMeshletIndirectArgs", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_VisibleMeshletIndirectArgs;

        [RenderGraphResource(
            Name = "VisibilityBuffer",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_VisibilityBuffer;

        [RenderGraphResource(
            Name = "Depth",
            Access = AccessFlags.ReadWrite,
            IsDepthAttachment = true,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_Depth;

        private readonly RenderGraphTexture m_DefaultVisibilityBuffer;
        private readonly RenderGraphTexture m_DefaultDepth;
        private readonly Material[] m_Materials = new Material[(int)VividRendererListID.Count];

        public VisibilityBufferPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VisibilityBufferPass));

            m_VisibleMeshletRenderRequests = CreateStructuredBuffer(
                "VisibleMeshletRenderRequests",
                1,
                sizeof(uint) * 2,
                GraphicsBuffer.Target.Structured
            );
            m_VisibleMeshletIndirectArgs = CreateStructuredBuffer(
                "VisibleMeshletIndirectArgs",
                4,
                sizeof(uint),
                GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments
            );

            m_VisibilityBuffer = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R32G32_UInt)
            };
            m_VisibilityBuffer.desc.Name = "VisibilityBuffer";
            m_VisibilityBuffer.desc.FilterMode = FilterMode.Point;
            m_VisibilityBuffer.desc.WrapMode = TextureWrapMode.Clamp;
            m_VisibilityBuffer.desc.ClearBuffer = true;
            m_VisibilityBuffer.desc.ClearColor = Color.clear;
            m_VisibilityBuffer.desc.UseMipMap = false;
            m_VisibilityBuffer.desc.AutoGenerateMips = false;
            m_VisibilityBuffer.desc.MipCount = 1;

            m_Depth = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, DepthBits.Depth32)
            };
            m_Depth.desc.Name = "Depth";

            m_DefaultVisibilityBuffer = m_VisibilityBuffer;
            m_DefaultDepth = m_Depth;
        }

        public override void Create()
        {
            Shader shader = Shader.Find(VisibilityBufferShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{VisibilityBufferShaderName}' for {nameof(VisibilityBufferPass)}.");
                return;
            }

            for (int rendererListIndex = 0; rendererListIndex < m_Materials.Length; rendererListIndex++)
            {
                Material material = CoreUtils.CreateEngineMaterial(shader);
                material.name = $"{nameof(VisibilityBufferPass)}_{(VividRendererListID)rendererListIndex}";
                ConfigureMaterial(material, (VividRendererListID)rendererListIndex);
                m_Materials[rendererListIndex] = material;
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            int width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            int height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0)
                width = Mathf.Max(1, Screen.width);

            if (height <= 0)
                height = Mathf.Max(1, Screen.height);

            ResizePassOwnedTexture(m_VisibilityBuffer, m_DefaultVisibilityBuffer, width, height);
            ResizePassOwnedTexture(m_Depth, m_DefaultDepth, width, height);

            var gpuDrivenFrameData = frameData.GetOrCreate<VividGPUDrivenFrameData>();
            GraphicsBuffer visibleMeshletRenderRequestsBuffer = gpuDrivenFrameData.visibleMeshletRenderRequestsBuffer;
            GraphicsBuffer visibleMeshletIndirectDrawArgsBuffer = gpuDrivenFrameData.visibleMeshletIndirectDrawArgsBuffer;

            if ((visibleMeshletRenderRequestsBuffer == null || visibleMeshletIndirectDrawArgsBuffer == null) &&
                VividGPUDrivenSystem.TryGetCurrentVisibleMeshletBuffers(
                    out GraphicsBuffer fallbackVisibleMeshletRenderRequestsBuffer,
                    out GraphicsBuffer fallbackVisibleMeshletIndirectDrawArgsBuffer))
            {
                visibleMeshletRenderRequestsBuffer ??= fallbackVisibleMeshletRenderRequestsBuffer;
                visibleMeshletIndirectDrawArgsBuffer ??= fallbackVisibleMeshletIndirectDrawArgsBuffer;
            }

            UpdateImportedBuffer(
                m_VisibleMeshletRenderRequests,
                visibleMeshletRenderRequestsBuffer,
                GraphicsBuffer.Target.Structured,
                "VisibleMeshletRenderRequests"
            );
            UpdateImportedBuffer(
                m_VisibleMeshletIndirectArgs,
                visibleMeshletIndirectDrawArgsBuffer,
                GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments,
                "VisibleMeshletIndirectArgs"
            );
        }

        public override void Record(RasterPassContext context)
        {
            if (m_Materials[0] == null)
                return;

            GraphicsBuffer visibleMeshletRenderRequestsBuffer = m_VisibleMeshletRenderRequests?.ImportedGraphicsBuffer;
            GraphicsBuffer visibleMeshletIndirectArgsBuffer = m_VisibleMeshletIndirectArgs?.ImportedGraphicsBuffer;
            if (visibleMeshletRenderRequestsBuffer == null || visibleMeshletIndirectArgsBuffer == null)
                return;

            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                for (int rendererListIndex = 0; rendererListIndex < m_Materials.Length; rendererListIndex++)
                {
                    Material material = m_Materials[rendererListIndex];
                    if (material == null)
                        continue;

                    material.SetBuffer(s_VisibleMeshletRenderRequestsId, visibleMeshletRenderRequestsBuffer);
                    context.cmd.DrawProceduralIndirect(
                        Matrix4x4.identity,
                        material,
                        0,
                        MeshTopology.Triangles,
                        visibleMeshletIndirectArgsBuffer,
                        rendererListIndex * IndirectDrawArgsByteStride
                    );
                }
            }
        }

        public override void Dispose()
        {
            for (int materialIndex = 0; materialIndex < m_Materials.Length; materialIndex++)
            {
                if (m_Materials[materialIndex] == null)
                    continue;

                CoreUtils.Destroy(m_Materials[materialIndex]);
                m_Materials[materialIndex] = null;
            }
        }

        private static void ConfigureMaterial(Material material, VividRendererListID rendererListID)
        {
            if (material == null)
                return;

            material.SetFloat(s_CullId, (float)GetCullMode(rendererListID));
            CoreUtils.SetKeyword(material, s_AlphaTestKeyword, (rendererListID & VividRendererListID.AlphaTest) != 0);
        }

        private static CullMode GetCullMode(VividRendererListID rendererListID)
        {
            if ((rendererListID & VividRendererListID.CullFront) != 0)
                return CullMode.Front;

            if ((rendererListID & VividRendererListID.CullOff) != 0)
                return CullMode.Off;

            return CullMode.Back;
        }

        private static void ResizePassOwnedTexture(
            RenderGraphTexture texture,
            RenderGraphTexture defaultTexture,
            int width,
            int height)
        {
            if (!ReferenceEquals(texture, defaultTexture) || texture?.desc == null)
                return;

            texture.desc.Width = width;
            texture.desc.Height = height;
        }

        private static void UpdateImportedBuffer(
            RenderGraphBuffer renderGraphBuffer,
            GraphicsBuffer graphicsBuffer,
            GraphicsBuffer.Target fallbackTarget,
            string name)
        {
            if (renderGraphBuffer == null)
                return;

            renderGraphBuffer.desc.Name = name;

            if (graphicsBuffer == null)
            {
                renderGraphBuffer.desc.Target = fallbackTarget;
                renderGraphBuffer.ClearImportedBuffer();
                return;
            }

            renderGraphBuffer.desc.Count = Mathf.Max(1, graphicsBuffer.count);
            renderGraphBuffer.desc.Stride = Mathf.Max(1, graphicsBuffer.stride);
            renderGraphBuffer.desc.Target = graphicsBuffer.target;
            renderGraphBuffer.SetImportedBuffer(graphicsBuffer);
        }

        private static RenderGraphBuffer CreateStructuredBuffer(
            string name,
            int count,
            int stride,
            GraphicsBuffer.Target target)
        {
            return new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = count,
                    Stride = stride,
                    Target = target,
                    Name = name,
                }
            };
        }
    }
}
