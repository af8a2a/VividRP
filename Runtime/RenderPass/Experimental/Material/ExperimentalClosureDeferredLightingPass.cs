using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Experimental.Material
{
    public sealed class ExperimentalClosureDeferredLightingPass : ComputePass
    {
        private static readonly string[] KernelNames =
        {
            "ExperimentalClosureLit_Fast",
            "ExperimentalClosureLit_Single",
            "ExperimentalClosureLit_Complex"
        };

        private static readonly int[] ClosureBufferIds =
        {
            Shader.PropertyToID("_ExperimentalClosureBuffer0"),
            Shader.PropertyToID("_ExperimentalClosureBuffer1"),
            Shader.PropertyToID("_ExperimentalClosureBuffer2"),
            Shader.PropertyToID("_ExperimentalClosureBuffer3"),
            Shader.PropertyToID("_ExperimentalClosureBuffer4"),
            Shader.PropertyToID("_ExperimentalClosureBuffer5")
        };

        private static readonly int DepthTextureId =
            Shader.PropertyToID("_DepthTexture");
        private static readonly int TileListId =
            Shader.PropertyToID("_ExperimentalClosureTileList");
        private static readonly int TileListOffsetId =
            Shader.PropertyToID("_ExperimentalClosureTileListOffset");
        private static readonly int LightingTextureId =
            Shader.PropertyToID("_ExperimentalClosureLightingTexture");
        private static readonly int DebugTextureId =
            Shader.PropertyToID("_ExperimentalClosureDebugTexture");
        private static readonly int DirectionalLightsId =
            Shader.PropertyToID("_DirectionalLights");
        private static readonly int DirectionalLightCountId =
            Shader.PropertyToID("_DirectionalLightCount");

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer0",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClosureBuffer0;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer1",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClosureBuffer1;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer2",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClosureBuffer2;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer3",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClosureBuffer3;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer4",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClosureBuffer4;

        [RenderGraphResource(
            Name = "ExperimentalClosureBuffer5",
            Access = AccessFlags.Read)]
        private RenderGraphTexture m_ClosureBuffer5;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(
            Name = "ExperimentalClosureTileList",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_TileList;

        [RenderGraphResource(
            Name = "ExperimentalClosureIndirectArgs",
            Access = AccessFlags.Read)]
        private RenderGraphBuffer m_IndirectArgs;

        [RenderGraphResource(Name = "DirectionalLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_DirectionalLights;

        [RenderGraphResource(
            Name = "ExperimentalClosureLighting",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_LightingTexture;

        [RenderGraphResource(
            Name = "ExperimentalClosureDebug",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DebugTexture;

        private readonly RenderGraphBuffer m_LocalDirectionalLights;
        private readonly int[] m_Kernels = { -1, -1, -1 };
        private ComputeShader m_Compute;
        private int m_Width = 1;
        private int m_Height = 1;
        private int m_TileCount = 1;
        private int m_DirectionalLightCount;

        public ExperimentalClosureDeferredLightingPass()
        {
            m_ClosureBuffer0 = CreateClosureInput(
                "ExperimentalClosureBuffer0",
                GraphicsFormat.R8G8B8A8_UNorm);
            m_ClosureBuffer1 = CreateClosureInput(
                "ExperimentalClosureBuffer1",
                GraphicsFormat.A2B10G10R10_UNormPack32);
            m_ClosureBuffer2 = CreateClosureInput(
                "ExperimentalClosureBuffer2",
                GraphicsFormat.R8G8B8A8_UNorm);
            m_ClosureBuffer3 = CreateClosureInput(
                "ExperimentalClosureBuffer3",
                GraphicsFormat.R8G8B8A8_UNorm);
            m_ClosureBuffer4 = CreateClosureInput(
                "ExperimentalClosureBuffer4",
                GraphicsFormat.B10G11R11_UFloatPack32);
            m_ClosureBuffer5 = CreateClosureInput(
                "ExperimentalClosureBuffer5",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_DepthTexture = RenderGraphTexture.CreateInput(
                "Depth",
                GraphicsFormat.None,
                DepthBits.Depth32);
            m_TileList = RenderGraphBuffer.CreateStructured(
                "ExperimentalClosureTileList",
                sizeof(uint));
            m_IndirectArgs = RenderGraphBuffer.CreateStructured(
                "ExperimentalClosureIndirectArgs",
                ExperimentalClosureClassificationPass.VariantCount
                    * ExperimentalClosureClassificationPass.IndirectArgsElementCount,
                sizeof(uint),
                GraphicsBuffer.Target.Structured
                    | GraphicsBuffer.Target.IndirectArguments);
            m_LocalDirectionalLights = RenderGraphBuffer.CreateStructured(
                "DirectionalLights",
                VividLightData.DirectionalLightData.Stride);
            m_DirectionalLights = m_LocalDirectionalLights;
            m_LightingTexture = CreateOutput("ExperimentalClosureLighting");
            m_DebugTexture = CreateOutput("ExperimentalClosureDebug");
        }

        public override void Create()
        {
            m_Compute = PipelineResourceManager.Get<VividRPCoreResources>()
                ?.ExperimentalClosureDeferredLitCompute;
            if (m_Compute == null)
                return;

            for (var i = 0; i < KernelNames.Length; i++)
                m_Kernels[i] = m_Compute.FindKernel(KernelNames[i]);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_Width = cameraData.actualWidth > 0
                ? cameraData.actualWidth
                : cameraData.pixelWidth;
            m_Height = cameraData.actualHeight > 0
                ? cameraData.actualHeight
                : cameraData.pixelHeight;

            if (m_Width <= 0)
                m_Width = Mathf.Max(1, Screen.width);
            if (m_Height <= 0)
                m_Height = Mathf.Max(1, Screen.height);

            for (var i = 0; i < ClosureBufferIds.Length; i++)
                GetClosureBuffer(i).Resize(m_Width, m_Height);
            m_DepthTexture.Resize(m_Width, m_Height);
            m_LightingTexture.Resize(m_Width, m_Height);
            m_DebugTexture.Resize(m_Width, m_Height);

            var tileCountX = Mathf.Max(
                1,
                (m_Width + ExperimentalClosureClassificationPass.TileSize - 1)
                    / ExperimentalClosureClassificationPass.TileSize);
            var tileCountY = Mathf.Max(
                1,
                (m_Height + ExperimentalClosureClassificationPass.TileSize - 1)
                    / ExperimentalClosureClassificationPass.TileSize);
            m_TileCount = Mathf.Max(1, tileCountX * tileCountY);

            var clusteredLightingData =
                frameData.GetOrCreate<VividClusteredLightingData>();
            m_DirectionalLightCount =
                !ReferenceEquals(m_DirectionalLights, m_LocalDirectionalLights)
                    ? Mathf.Max(0, clusteredLightingData.directionalLightCount)
                    : 0;
        }

        public override void Record(ComputePassContext context)
        {
            if (m_Compute == null)
                return;

            var cmd = context.cmd;
            for (var variant = 0; variant < m_Kernels.Length; variant++)
            {
                var kernel = m_Kernels[variant];
                if (kernel < 0)
                    continue;

                BindKernel(cmd, kernel, variant);
                var indirectArgsOffset = (uint)(
                    variant
                    * ExperimentalClosureClassificationPass.IndirectArgsElementCount
                    * sizeof(uint));
                cmd.DispatchCompute(
                    m_Compute,
                    kernel,
                    m_IndirectArgs,
                    indirectArgsOffset);
            }
        }

        public override void Dispose()
        {
            m_Compute = null;
            for (var i = 0; i < m_Kernels.Length; i++)
                m_Kernels[i] = -1;
        }

        private void BindKernel(
            ComputeCommandBuffer cmd,
            int kernel,
            int variant)
        {
            for (var i = 0; i < ClosureBufferIds.Length; i++)
            {
                cmd.SetComputeTextureParam(
                    m_Compute,
                    kernel,
                    ClosureBufferIds[i],
                    GetClosureBuffer(i).innerHandle);
            }

            cmd.SetComputeTextureParam(
                m_Compute,
                kernel,
                DepthTextureId,
                m_DepthTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_Compute,
                kernel,
                LightingTextureId,
                m_LightingTexture.innerHandle);
            cmd.SetComputeTextureParam(
                m_Compute,
                kernel,
                DebugTextureId,
                m_DebugTexture.innerHandle);
            cmd.SetComputeBufferParam(
                m_Compute,
                kernel,
                TileListId,
                m_TileList.innerHandle);
            cmd.SetComputeBufferParam(
                m_Compute,
                kernel,
                DirectionalLightsId,
                m_DirectionalLights.innerHandle);
            cmd.SetComputeIntParam(
                m_Compute,
                DirectionalLightCountId,
                m_DirectionalLightCount);
            cmd.SetComputeIntParam(
                m_Compute,
                TileListOffsetId,
                variant * m_TileCount);
        }

        private static RenderGraphTexture CreateClosureInput(
            string name,
            GraphicsFormat format)
        {
            return RenderGraphTexture.CreateInput(name, format);
        }

        private RenderGraphTexture GetClosureBuffer(int index)
        {
            return index switch
            {
                0 => m_ClosureBuffer0,
                1 => m_ClosureBuffer1,
                2 => m_ClosureBuffer2,
                3 => m_ClosureBuffer3,
                4 => m_ClosureBuffer4,
                5 => m_ClosureBuffer5,
                _ => null,
            };
        }

        private static RenderGraphTexture CreateOutput(string name)
        {
            var texture = RenderGraphTexture.CreateOutput(
                name,
                GraphicsFormat.R16G16B16A16_SFloat);
            texture.desc.EnableRandomWrite = true;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.clear;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            return texture;
        }
    }
}
