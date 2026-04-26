using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    //todo:
    //candidate async compute
    public sealed class GTAOPass : ComputePass
    {
        private const int DepthMipCount = 5;
        private const int PrefilterTileSize = 16;
        private const int MainKernelThreadGroupSize = 8;
        private const int DenoiseThreadGroupSizeX = 16;
        private const int DenoiseThreadGroupSizeY = 8;
        private const float DefaultRadiusMultiplier = 1.457f;
        private const float DefaultSampleDistributionPower = 2.0f;
        private const float DefaultThinOccluderCompensation = 0.0f;
        private const float DefaultDepthMipSamplingOffset = 3.30f;
        private const float DefaultDenoiseBlurBeta = 1.2f;
        private const float DisabledDenoiseBlurBeta = 10000.0f;
        private const float PlaneEpsilon = 1e-6f;

        private static readonly int GTAOConstantBufferId = Shader.PropertyToID("GTAOConstantBuffer");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int WorkingDepthId = Shader.PropertyToID("_WorkingDepth");
        private static readonly int WorkingAOTermId = Shader.PropertyToID("_WorkingAOTerm");
        private static readonly int WorkingEdgesId = Shader.PropertyToID("_WorkingEdges");
        private static readonly int SourceAOTermId = Shader.PropertyToID("_SourceAOTerm");
        private static readonly int SourceEdgesId = Shader.PropertyToID("_SourceEdges");
        private static readonly int DenoiseAOTermId = Shader.PropertyToID("_DenoiseAOTerm");
        private static readonly int GTAOTextureId = Shader.PropertyToID("_GTAOTexture");
        private static readonly int[] s_WorkingDepthMipIds =
        {
            Shader.PropertyToID("_WorkingDepthMIP0"),
            Shader.PropertyToID("_WorkingDepthMIP1"),
            Shader.PropertyToID("_WorkingDepthMIP2"),
            Shader.PropertyToID("_WorkingDepthMIP3"),
            Shader.PropertyToID("_WorkingDepthMIP4")
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct GTAOConstantBufferData
        {
            public int ViewportSizeX;
            public int ViewportSizeY;
            public Vector2 ViewportPixelSize;

            public Vector2 DepthUnpackConsts;
            public Vector2 CameraTanHalfFOV;

            public Vector2 NDCToViewMul;
            public Vector2 NDCToViewAdd;

            public Vector2 NDCToViewMulXPixelSize;
            public float EffectRadius;
            public float EffectFalloffRange;

            public float RadiusMultiplier;
            public float Padding0;
            public float FinalValuePower;
            public float DenoiseBlurBeta;

            public float SampleDistributionPower;
            public float ThinOccluderCompensation;
            public float DepthMIPSamplingOffset;
            public int NoiseIndex;
        }

        private readonly struct ProjectionData
        {
            public ProjectionData(bool isLeftHanded, Vector4 frustum)
            {
                IsLeftHanded = isLeftHanded;
                Frustum = frustum;
            }

            public bool IsLeftHanded { get; }

            public Vector4 Frustum { get; }
        }

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "GTAOTexture", Access = AccessFlags.Write)]
        private RenderGraphTexture m_GTAOTexture;

        [RenderGraphResource(
            Name = "GTAOWorkingDepth",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_WorkingDepthTexture;

        [RenderGraphResource(
            Name = "GTAOWorkingAOTerm",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_WorkingAOTermTexture;

        [RenderGraphResource(
            Name = "GTAOWorkingAOTermPong",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_WorkingAOTermPongTexture;

        [RenderGraphResource(
            Name = "GTAOWorkingEdges",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_WorkingEdgesTexture;

        private ComputeShader m_GTAOCompute;
        private int m_PrefilterKernel = -1;
        private int m_GTAOLowKernel = -1;
        private int m_GTAOMediumKernel = -1;
        private int m_GTAOHighKernel = -1;
        private int m_GTAOUltraKernel = -1;
        private int m_DenoiseKernel = -1;
        private int m_DenoiseLastKernel = -1;
        private int m_Width = 1;
        private int m_Height = 1;
        private GTAOSettingsData m_Settings;
        private GTAOConstantBufferData m_ConstantBuffer;

        public GTAOPass()
        {
            profilingSampler = new ProfilingSampler(nameof(GTAOPass));

            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_GBuffer1 = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_GTAOTexture = CreateTexture("GTAOTexture", GraphicsFormat.R8_UNorm, clearBuffer: true, clearColor: Color.white);
            m_WorkingDepthTexture = CreateTexture("GTAOWorkingDepth", GraphicsFormat.R16_SFloat, useMipMap: true, mipCount: DepthMipCount);
            m_WorkingAOTermTexture = CreateTexture("GTAOWorkingAOTerm", GraphicsFormat.R8_UInt);
            m_WorkingAOTermPongTexture = CreateTexture("GTAOWorkingAOTermPong", GraphicsFormat.R8_UInt);
            m_WorkingEdgesTexture = CreateTexture("GTAOWorkingEdges", GraphicsFormat.R8_UNorm);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_GTAOCompute = resources?.GTAOCompute;
            if (m_GTAOCompute == null)
                return;

            m_PrefilterKernel = m_GTAOCompute.FindKernel("CSPrefilterDepths16x16");
            m_GTAOLowKernel = m_GTAOCompute.FindKernel("CSGTAOLow");
            m_GTAOMediumKernel = m_GTAOCompute.FindKernel("CSGTAOMedium");
            m_GTAOHighKernel = m_GTAOCompute.FindKernel("CSGTAOHigh");
            m_GTAOUltraKernel = m_GTAOCompute.FindKernel("CSGTAOUltra");
            m_DenoiseKernel = m_GTAOCompute.FindKernel("CSDenoisePass");
            m_DenoiseLastKernel = m_GTAOCompute.FindKernel("CSDenoiseLastPass");
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var camera = cameraData?.camera;
            var postProcessingAllowed = camera != null && CoreUtils.ArePostProcessesEnabled(camera);

            m_Width = CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualWidth ?? 0, cameraData?.pixelWidth ?? 0, Screen.width);
            m_Height = CameraDimensionUtility.ResolveCameraDimension(cameraData?.actualHeight ?? 0, cameraData?.pixelHeight ?? 0, Screen.height);
            m_Settings = postProcessingAllowed ? GTAOSettingsResolver.Resolve() : GTAOSettingsData.CreateDefault();

            ResizeTexture(m_DepthTexture, m_Width, m_Height);
            ResizeTexture(m_GBuffer1, m_Width, m_Height);
            ResizeOutputTexture(m_GTAOTexture, m_Width, m_Height, GraphicsFormat.R8_UNorm, Color.white);
            ResizeWorkingDepthTexture(m_WorkingDepthTexture, m_Width, m_Height);
            ResizeOutputTexture(m_WorkingAOTermTexture, m_Width, m_Height, GraphicsFormat.R8_UInt, Color.clear, clearBuffer: false);
            ResizeOutputTexture(m_WorkingAOTermPongTexture, m_Width, m_Height, GraphicsFormat.R8_UInt, Color.clear, clearBuffer: false);
            ResizeOutputTexture(m_WorkingEdgesTexture, m_Width, m_Height, GraphicsFormat.R8_UNorm, Color.clear, clearBuffer: false);

            m_ConstantBuffer = BuildConstantBuffer(cameraData, m_Width, m_Height, m_Settings);
        }


        public override void Record(ComputePassContext context)
        {
            var cmd = context.cmd;
            using (new ProfilingScope(cmd, profilingSampler))
            {

                if (!CanExecute() || !m_Settings.enabled)
                    return;

                ConstantBuffer.Push(cmd, m_ConstantBuffer, m_GTAOCompute, GTAOConstantBufferId);

                cmd.SetComputeTextureParam(m_GTAOCompute, m_PrefilterKernel, DepthTextureId, m_DepthTexture.innerHandle);
                BindWorkingDepthMips(cmd);
                cmd.DispatchCompute(
                    m_GTAOCompute,
                    m_PrefilterKernel,
                    CoreUtils.DivRoundUp(m_Width, PrefilterTileSize),
                    CoreUtils.DivRoundUp(m_Height, PrefilterTileSize),
                    1);

                int mainKernel = ResolveQualityKernel(m_Settings.qualityLevel);
                cmd.SetComputeTextureParam(m_GTAOCompute, mainKernel, WorkingDepthId, m_WorkingDepthTexture.innerHandle);
                cmd.SetComputeTextureParam(m_GTAOCompute, mainKernel, GBuffer1Id, m_GBuffer1.innerHandle);
                cmd.SetComputeTextureParam(m_GTAOCompute, mainKernel, WorkingAOTermId, m_WorkingAOTermTexture.innerHandle);
                cmd.SetComputeTextureParam(m_GTAOCompute, mainKernel, WorkingEdgesId, m_WorkingEdgesTexture.innerHandle);
                cmd.DispatchCompute(
                    m_GTAOCompute,
                    mainKernel,
                    CoreUtils.DivRoundUp(m_Width, MainKernelThreadGroupSize),
                    CoreUtils.DivRoundUp(m_Height, MainKernelThreadGroupSize),
                    1);

                var sourceAo = m_WorkingAOTermTexture;
                var destinationAo = m_WorkingAOTermPongTexture;
                int totalResolvePasses = Mathf.Max(1, m_Settings.denoisePasses);

                for (int passIndex = 0; passIndex < totalResolvePasses; passIndex++)
                {
                    bool isLastPass = passIndex == totalResolvePasses - 1;
                    int kernel = isLastPass ? m_DenoiseLastKernel : m_DenoiseKernel;

                    cmd.SetComputeTextureParam(m_GTAOCompute, kernel, SourceAOTermId, sourceAo.innerHandle);
                    cmd.SetComputeTextureParam(m_GTAOCompute, kernel, SourceEdgesId, m_WorkingEdgesTexture.innerHandle);

                    if (isLastPass)
                    {
                        cmd.SetComputeTextureParam(m_GTAOCompute, kernel, GTAOTextureId, m_GTAOTexture.innerHandle);
                    }
                    else
                    {
                        cmd.SetComputeTextureParam(m_GTAOCompute, kernel, DenoiseAOTermId, destinationAo.innerHandle);
                    }

                    cmd.DispatchCompute(
                        m_GTAOCompute,
                        kernel,
                        CoreUtils.DivRoundUp(m_Width, DenoiseThreadGroupSizeX),
                        CoreUtils.DivRoundUp(m_Height, DenoiseThreadGroupSizeY),
                        1);

                    if (!isLastPass)
                        (sourceAo, destinationAo) = (destinationAo, sourceAo);
                }
            }
        }

        public override void Dispose()
        {
            m_GTAOCompute = null;
            m_PrefilterKernel = -1;
            m_GTAOLowKernel = -1;
            m_GTAOMediumKernel = -1;
            m_GTAOHighKernel = -1;
            m_GTAOUltraKernel = -1;
            m_DenoiseKernel = -1;
            m_DenoiseLastKernel = -1;
            m_Width = 1;
            m_Height = 1;
            m_Settings = GTAOSettingsData.CreateDefault();
            m_ConstantBuffer = default;
        }

        private bool CanExecute()
        {
            return m_GTAOCompute != null
                && m_PrefilterKernel >= 0
                && ResolveQualityKernel(m_Settings.qualityLevel) >= 0
                && m_DenoiseKernel >= 0
                && m_DenoiseLastKernel >= 0
                && m_DepthTexture?.innerHandle.IsValid() == true
                && m_GBuffer1?.innerHandle.IsValid() == true
                && m_GTAOTexture?.innerHandle.IsValid() == true
                && m_WorkingDepthTexture?.innerHandle.IsValid() == true
                && m_WorkingAOTermTexture?.innerHandle.IsValid() == true
                && m_WorkingAOTermPongTexture?.innerHandle.IsValid() == true
                && m_WorkingEdgesTexture?.innerHandle.IsValid() == true;
        }

        private void BindWorkingDepthMips(ComputeCommandBuffer cmd)
        {
            for (int mipIndex = 0; mipIndex < DepthMipCount; mipIndex++)
                cmd.SetComputeTextureParam(m_GTAOCompute, m_PrefilterKernel, s_WorkingDepthMipIds[mipIndex], m_WorkingDepthTexture.innerHandle, mipIndex);
        }

        private int ResolveQualityKernel(int qualityLevel)
        {
            return qualityLevel switch
            {
                0 => m_GTAOLowKernel,
                1 => m_GTAOMediumKernel,
                3 => m_GTAOUltraKernel,
                _ => m_GTAOHighKernel
            };
        }

        private static GTAOConstantBufferData BuildConstantBuffer(
            VividCameraData cameraData,
            int width,
            int height,
            GTAOSettingsData settings)
        {
            var projection = cameraData != null
                ? cameraData.GetGPUProjectionMatrix(renderIntoTexture: true)
                : Matrix4x4.Perspective(60.0f, Mathf.Max(width / (float)Mathf.Max(height, 1), 0.0001f), 0.1f, 1000.0f);
            bool isOrthographic = cameraData?.camera != null && cameraData.camera.orthographic;
            var projectionData = DecomposeProjection(projection, isOrthographic);

            if (!projectionData.IsLeftHanded)
            {
                projection = ConvertProjectionToLeftHanded(projection);
                projectionData = DecomposeProjection(projection, isOrthographic);
            }

            Vector2 viewportPixelSize = new(1.0f / Mathf.Max(width, 1), 1.0f / Mathf.Max(height, 1));
            Vector2 ndcToViewAdd = new(projectionData.Frustum.x, projectionData.Frustum.y);
            Vector2 ndcToViewMul = new(projectionData.Frustum.z, projectionData.Frustum.w);
            Vector2 cameraTanHalfFov = new(Mathf.Abs(ndcToViewMul.x) * 0.5f, Mathf.Abs(ndcToViewMul.y) * 0.5f);
            var depthUnpackConsts = new Vector2(-projection[3, 2], projection[2, 2]);
            if (depthUnpackConsts.x * depthUnpackConsts.y < 0.0f)
                depthUnpackConsts.y = -depthUnpackConsts.y;

            return new GTAOConstantBufferData
            {
                ViewportSizeX = width,
                ViewportSizeY = height,
                ViewportPixelSize = viewportPixelSize,
                DepthUnpackConsts = depthUnpackConsts,
                CameraTanHalfFOV = cameraTanHalfFov,
                NDCToViewMul = ndcToViewMul,
                NDCToViewAdd = ndcToViewAdd,
                NDCToViewMulXPixelSize = Vector2.Scale(ndcToViewMul, viewportPixelSize),
                EffectRadius = settings.radius,
                EffectFalloffRange = settings.falloffRange,
                RadiusMultiplier = DefaultRadiusMultiplier,
                Padding0 = 0.0f,
                FinalValuePower = settings.finalValuePower,
                DenoiseBlurBeta = settings.denoisePasses == 0 ? DisabledDenoiseBlurBeta : DefaultDenoiseBlurBeta,
                SampleDistributionPower = DefaultSampleDistributionPower,
                ThinOccluderCompensation = DefaultThinOccluderCompensation,
                DepthMIPSamplingOffset = DefaultDepthMipSamplingOffset,
                NoiseIndex = settings.denoisePasses > 0 ? Time.frameCount % 64 : 0
            };
        }

        private static ProjectionData DecomposeProjection(Matrix4x4 projection, bool isOrthographicHint)
        {
            bool isReversedZ = MvpToPlanes(
                projection,
                out var leftPlane,
                out var rightPlane,
                out var bottomPlane,
                out var topPlane,
                out var nearPlane,
                out var farPlane);
            bool isOrthographic = isOrthographicHint || Mathf.Abs(projection[3, 3] - 1.0f) <= 1e-5f;

            float x0;
            float x1;
            float y0;
            float y1;

            if (isOrthographic)
            {
                x0 = -leftPlane.w;
                x1 = rightPlane.w;
                y0 = -bottomPlane.w;
                y1 = topPlane.w;

                if (projection[1, 1] < 0.0f)
                    Swap(ref y0, ref y1);
            }
            else
            {
                x0 = leftPlane.z / leftPlane.x;
                x1 = rightPlane.z / rightPlane.x;
                y0 = bottomPlane.z / bottomPlane.y;
                y1 = topPlane.z / topPlane.y;
            }

            float nearZ = -nearPlane.w;
            Vector4 clip = projection * new Vector4(0.0f, 0.0f, nearZ, 1.0f);
            Vector3 column2 = isOrthographic
                ? GetColumn(projection, 2) * (isReversedZ ? -1.0f : 1.0f)
                : new Vector3(0.0f, 0.0f, clip.w > 0.0f ? 1.0f : -1.0f);

            bool compare = Vector3.Dot(Vector3.Cross(GetColumn(projection, 0), GetColumn(projection, 1)), column2) > 0.0f;
            bool isLeftHanded = projection[1, 1] > 0.0f ? compare : !compare;
            Vector4 frustum = new(-x0, -y1, x0 - x1, y1 - y0);
            return new ProjectionData(isLeftHanded, frustum);
        }

        private static Matrix4x4 ConvertProjectionToLeftHanded(Matrix4x4 projection)
        {
            for (int row = 0; row < 4; row++)
                projection[row, 2] = -projection[row, 2];

            return projection;
        }

        private static bool MvpToPlanes(
            Matrix4x4 matrix,
            out Vector4 left,
            out Vector4 right,
            out Vector4 bottom,
            out Vector4 top,
            out Vector4 near,
            out Vector4 far)
        {
            left = NormalizePlane(matrix.GetRow(3) + matrix.GetRow(0));
            right = NormalizePlane(matrix.GetRow(3) - matrix.GetRow(0));
            bottom = NormalizePlane(matrix.GetRow(3) + matrix.GetRow(1));
            top = NormalizePlane(matrix.GetRow(3) - matrix.GetRow(1));
            far = NormalizePlane(matrix.GetRow(3) - matrix.GetRow(2));
            near = NormalizePlane(matrix.GetRow(2));

            bool isReversedZ = Mathf.Abs(near.w) > Mathf.Abs(far.w);
            if (isReversedZ)
                Swap(ref near, ref far);

            if (GetLengthSquared(far) < PlaneEpsilon * PlaneEpsilon)
                far = new Vector4(-near.x, -near.y, -near.z, far.w);

            return isReversedZ;
        }

        private static Vector4 NormalizePlane(Vector4 plane)
        {
            float length = Mathf.Sqrt(GetLengthSquared(plane));
            return length > PlaneEpsilon ? plane / length : plane;
        }

        private static float GetLengthSquared(Vector4 value)
        {
            return value.x * value.x + value.y * value.y + value.z * value.z;
        }

        private static Vector3 GetColumn(Matrix4x4 matrix, int index)
        {
            Vector4 column = matrix.GetColumn(index);
            return new Vector3(column.x, column.y, column.z);
        }

        private static void ResizeTexture(RenderGraphTexture texture, int width, int height)
        {
            texture?.Resize(width, height);
        }

        private static void ResizeOutputTexture(
            RenderGraphTexture texture,
            int width,
            int height,
            GraphicsFormat format,
            Color clearColor,
            bool clearBuffer = true)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = width;
            texture.desc.Height = height;
            texture.desc.ColorFormat = format;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.EnableRandomWrite = true;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.ClearBuffer = clearBuffer;
            texture.desc.ClearColor = clearColor;
        }

        private static void ResizeWorkingDepthTexture(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = width;
            texture.desc.Height = height;
            texture.desc.ColorFormat = GraphicsFormat.R16_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.EnableRandomWrite = true;
            texture.desc.UseMipMap = true;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = DepthMipCount;
            texture.desc.ClearBuffer = false;
        }

        private static RenderGraphTexture CreateTexture(
            string name,
            GraphicsFormat format,
            bool clearBuffer = false,
            Color? clearColor = null,
            bool useMipMap = false,
            int mipCount = 1)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, format)
            };

            texture.desc.Name = name;
            texture.desc.EnableRandomWrite = true;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = useMipMap;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = mipCount;
            texture.desc.ClearBuffer = clearBuffer;
            texture.desc.ClearColor = clearColor ?? Color.clear;
            texture.desc.MsaaSamples = MSAASamples.None;
            return texture;
        }

        private static void ClearTexture(CommandBuffer cmd, RenderGraphTexture texture, Color clearColor)
        {
            if (cmd == null || texture?.innerHandle.IsValid() != true)
                return;

            cmd.SetRenderTarget(texture);
            cmd.ClearRenderTarget(false, true, clearColor);
        }

        private static void Swap(ref Vector4 left, ref Vector4 right)
        {
            (left, right) = (right, left);
        }

        private static void Swap(ref float left, ref float right)
        {
            (left, right) = (right, left);
        }
    }
}
