using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class VolumetricLightPass : ScriptableRenderPass
    {
        #region ShaderID

        private static readonly int _InputTexture = Shader.PropertyToID("_InputTexture");
        private static readonly int _OutputTexture = Shader.PropertyToID("_OutputTexture");
        private static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int _SrcOffsetAndLimit = Shader.PropertyToID("_SrcOffsetAndLimit");
        private static readonly int _DilationWidth = Shader.PropertyToID("_DilationWidth");
        private static readonly int _VBufferDensity = Shader.PropertyToID("_VBufferDensity");
        private static readonly int _VolumetricLightingBuffer = Shader.PropertyToID("VolumetricLightingBuffer");
        private static readonly int _VolumeBounds = Shader.PropertyToID("_VolumeBounds");
        private static readonly int _GlobalIndices = Shader.PropertyToID("_GlobalIndices");
        private static readonly int _IndirectArgs = Shader.PropertyToID("_IndirectArgs");
        private static readonly int _Indirections = Shader.PropertyToID("_Indirections");
        private static readonly int _VolumetricFogRenderingData = Shader.PropertyToID("_VolumetricFogRenderingData");
        private static readonly int _VBufferLightingRW = Shader.PropertyToID("_VBufferLightingRW");
        private static readonly int _MaxZMaskTexture = Shader.PropertyToID("_MaxZMaskTexture");

        private static readonly int _VBufferLighting = Shader.PropertyToID("_VBufferLighting");
        private static readonly int R_VBufferLighting = Shader.PropertyToID("R_VBufferLighting");
        private static readonly int W_VBufferLighting = Shader.PropertyToID("W_VBufferLighting");
        private static readonly int RW_VBufferLighting = Shader.PropertyToID("RW_VBufferLighting");

        private static readonly int unity_MatrixInvVP = Shader.PropertyToID("unity_MatrixInvVP");
        private static readonly int unity_MatrixInvP = Shader.PropertyToID("unity_MatrixInvP");
        #endregion

        public VolumetricLightPass()
        {
            LocalVolumetricFogManager.manager.InitializeGraphicsBuffer();
            // LocalVolumetricFogManager.manager.textureFogMaterial = m_VolumetricTextureFogMat;

            int maxVolumeCountOnScreen = LocalVolumetricFogManager.manager.maxVolumeCountOnScreen;
            m_VisibleVolumeBoundsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxVolumeCountOnScreen, Marshal.SizeOf(typeof(OrientedBBox)));
            m_VisibleVolumeIndicesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, maxVolumeCountOnScreen, Marshal.SizeOf(typeof(uint)));
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Render(renderGraph, frameData);
        }

        private GraphicsBuffer m_VisibleVolumeBoundsBuffer;
        private GraphicsBuffer m_VisibleVolumeIndicesBuffer;

        private Vector4 m_GlobalFogDensity;
        private List<OrientedBBox> m_VisibleBounds = new List<OrientedBBox>();
        private List<int> m_VisibleIndices = new List<int>();
        private bool m_HasLocalFog = false;
        private VolumetricLightingBuffer m_ShaderConstantBuffer = new VolumetricLightingBuffer();
        int m_SliceCount;
        int m_VoxelSize;
        Matrix4x4 m_VBufferCoordToViewDirWS;
        float m_Near;
        float m_Far;
        float m_SliceDistributionUniformity;
        Vector2Int m_Resolution;
        private int m_VisibleCount;
        private Material m_FinalMaterial;

        private ShaderTagId m_ShaderTag = new ShaderTagId("VolumetricFogVoxelize");


        class PassData
        {
            public ComputeShader generateMaxZCS;
            public int maxZKernel;
            public int maxZDownsampleKernel;
            public int dilateMaxZKernel;

            public ComputeShader VolumetricFogInitializeCS;
            public int VolumetricFogInitializeKernel;

            public ComputeShader VolumetricFogIndirectCS;
            public int VolumetricFogIndirectKernel;

            public ComputeShader VolumetricLightingCS;
            public int VolumetricLightingKernel;

            public ComputeShader volumetricLightingFilteringCS;
            public int volumetricLightingFilteringKernel;

            public RendererListHandle VolumetricFogVoxelize;
            public Material finalMaterial;

            public VolumetricLightingBuffer shaderConstantBuffer;

            public Vector2Int intermediateMaskSize;
            public Vector2Int finalMaskSize;
            public Vector2Int minDepthMipOffset;
            public Vector2Int densityResolution;

            public float dilationWidth;

            public TextureHandle depthTexture;
            public TextureHandle maxZ8xBuffer;
            public TextureHandle maxZBuffer;
            public TextureHandle dilatedMaxZBuffer;
            public TextureHandle VBufferDensity;
            public TextureHandle VBufferLighting;
            public TextureHandle CameraColor;


            public BufferHandle VisibleVolumeBounds;
            public BufferHandle VisibleVolumeIndices;

            public BufferHandle globalIndirectArgBuffer;
            public BufferHandle globalIndirectionBuffer;
            public BufferHandle volumetricFogRenderingBuffer;
        }

        static Vector4 ComputeLogarithmicDepthEncodingParams(float n, float f, float c)
        {
            Vector4 encodeParams = new Vector4();
            encodeParams.y = 1.0f / Mathf.Log(c * (f - n) + 1, 2);
            encodeParams.x = Mathf.Log(c, 2) * encodeParams.y;
            encodeParams.z = n - 1.0f / c;
            encodeParams.w = 0.0f;
            return encodeParams;
        }

        static float EncodeLogarithmicDepthGeneralized(float z, Vector4 encodeParams)
        {
            return encodeParams.x + encodeParams.y * Mathf.Log(Mathf.Max(0, z - encodeParams.z), 2);
        }

        static Vector4 ComputeLogarithmicDepthDecodingParams(float n, float f, float c)
        {
            Vector4 decodeParams = new Vector4();
            decodeParams.x = 1.0f / c;
            decodeParams.y = Mathf.Log(c * (f - n) + 1, 2);
            decodeParams.z = n - 1.0f / c;
            decodeParams.w = 0.0f;
            return decodeParams;
        }

        static float DecodeLogarithmicDepthGeneralized(float d, Vector4 decodeParams)
        {
            return decodeParams.x * Mathf.Pow(2, d * decodeParams.y) + decodeParams.z;
        }

        unsafe void UpdateShaderVariables()
        {
            for (int i = 0; i < 16; ++i)
                m_ShaderConstantBuffer._VBufferCoordToViewDirWS[i] = m_VBufferCoordToViewDirWS[i];

            m_ShaderConstantBuffer._VBufferViewportSize = new Vector4(m_Resolution.x, m_Resolution.y, 1.0f / m_Resolution.x, 1.0f / m_Resolution.y);
            m_ShaderConstantBuffer._VBufferSliceCount = (uint)m_SliceCount;
            m_ShaderConstantBuffer._VBufferRcpSliceCount = 1.0f / m_SliceCount;
            m_ShaderConstantBuffer._VBufferVoxelSize = m_VoxelSize;
            m_ShaderConstantBuffer._VBufferDistanceEncodingParams = ComputeLogarithmicDepthEncodingParams(m_Near, m_Far, m_SliceDistributionUniformity);
            m_ShaderConstantBuffer._VBufferDistanceDecodingParams = ComputeLogarithmicDepthDecodingParams(m_Near, m_Far, m_SliceDistributionUniformity);
            // _VBufferLightingViewportScale and _VBufferLightingViewportLimit just set a default for now
            m_ShaderConstantBuffer._VBufferLightingViewportScale = Vector4.one;
            m_ShaderConstantBuffer._VBufferLightingViewportLimit = Vector4.one;
            m_ShaderConstantBuffer._GlobalFogDensity = m_GlobalFogDensity;
            m_ShaderConstantBuffer._VisibleCount = (uint)m_VisibleCount;
        }

        internal static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Matrix4x4 view, float verticalFov, Vector2Int resolution)
        {
            float tanHalfVertFov = Mathf.Tan(0.5f * verticalFov);
            var viewSpaceRasterTransform = new Matrix4x4(
                new Vector4(2.0f / resolution.y, 0.0f, 0.0f, -(float)resolution.x / resolution.y),
                new Vector4(0.0f, 2.0f / resolution.y, 0.0f, -1.0f),
                new Vector4(0.0f, 0.0f, -1.0f / tanHalfVertFov, 0.0f),
                new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

            // Remove the translation component.
            view.SetColumn(3, new Vector4(0, 0, 0, 1));
            //view = Matrix4x4.identity;

            return view * viewSpaceRasterTransform.transpose;
        }

        void Setup(UniversalCameraData cameraData)
        {
            var setting = VolumeManager.instance.stack.GetComponent<VolumetricLightingSetting>();
            var camera = cameraData.camera;

            float extinction = VolumetricUtils.ExtinctionFromMeanFreePath(setting.meanFreePath.value);
            m_GlobalFogDensity = new Vector4(
                setting.albedo.value.r,
                setting.albedo.value.g,
                setting.albedo.value.b,
                1) * extinction;
            const int voxelSize = 8;
            m_VoxelSize = voxelSize;
            m_Resolution.x = Mathf.RoundToInt((float)camera.scaledPixelWidth / m_VoxelSize);
            m_Resolution.y = Mathf.RoundToInt((float)camera.scaledPixelHeight / m_VoxelSize);
            m_SliceCount = setting.sliceCount.value;

            m_VBufferCoordToViewDirWS =
                ComputePixelCoordToWorldSpaceViewDirectionMatrix(camera.cameraToWorldMatrix, camera.fieldOfView * Mathf.Deg2Rad, m_Resolution);

            // TODO: make it configurable by volume component
            m_Near = camera.nearClipPlane;
            m_Far = setting.range.value;
            m_SliceDistributionUniformity = setting.sliceDistrubutionUniform.value;

            m_VisibleBounds.Clear();
            m_VisibleIndices.Clear();
            int visibleCount = 0;
            var fogManager = LocalVolumetricFogManager.manager;
            var volumes = fogManager.volumes;
            foreach (var volume in volumes)
            {
                var trans = volume.transform;
                Vector3 center = trans.position;

                var transform = volume.transform;
                var bounds = GeometryUtils.OBBToAABB(transform.right, transform.up, transform.forward, volume.parameters.size, center);
                if (GeometryUtility.TestPlanesAABB(cameraData.frustum.planes, bounds))
                {
                    if (visibleCount >= fogManager.maxVolumeCountOnScreen)
                    {
                        Debug.LogError($"The number of local volumetric fog in the view is above the limit: {fogManager.maxVolumeCountOnScreen}.");
                        break;
                    }

                    var obb = new OrientedBBox(Matrix4x4.TRS(trans.position, trans.rotation, volume.parameters.size));
                    m_VisibleBounds.Add(obb);
                    m_VisibleIndices.Add(volume.globalIndex);
                    visibleCount++;
                }
            }

            m_HasLocalFog = visibleCount > 0;
            m_VisibleCount = visibleCount;
            if (!m_HasLocalFog) return;
            m_VisibleVolumeBoundsBuffer.SetData(m_VisibleBounds);
            m_VisibleVolumeIndicesBuffer.SetData(m_VisibleIndices);
            m_VisibleBounds.Clear();
            m_VisibleIndices.Clear();


            UpdateShaderVariables();
        }


        // Sample utility method that showcases how to create a renderer list via the RenderGraph API
        private RendererListHandle InitRendererLists(RenderGraph renderGraph, ContextContainer frameData)
        {
            // Access the relevant frame data from the Universal Render Pipeline
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            var sortFlags = cameraData.defaultOpaqueSortFlags;
            RenderQueueRange renderQueueRange = RenderQueueRange.all;

            FilteringSettings filterSettings = new FilteringSettings(renderQueueRange);

            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTag, universalRenderingData, cameraData, lightData, sortFlags);

            var param = new RendererListParams(universalRenderingData.cullResults, drawSettings, filterSettings);

            return renderGraph.CreateRendererList(param);
        }


        void Render(RenderGraph renderGraph, ContextContainer frameData)

        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var renderingData = frameData.Get<UniversalRenderingData>();

            var depthTexture = resourceData.cameraDepthPyramidTexture;
            var depthMipInfo = MipGenerator.instance.depthBufferMipChainInfo;

            using (var builder = renderGraph.AddUnsafePass<PassData>("Volumetric Fog", out var passData))
            {
                Setup(cameraData);
                if (!m_HasLocalFog)
                {
                    return;
                }

                var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<VolumetricLightRuntimeResource>();
                var fogManager = LocalVolumetricFogManager.manager;
                if (!fogManager.textureFogMaterial)
                {
                    fogManager.textureFogMaterial = CoreUtils.CreateEngineMaterial(runtimeShaders.defaultFogVolumeShader);
                }

                if (!m_FinalMaterial)
                {
                    m_FinalMaterial = CoreUtils.CreateEngineMaterial(runtimeShaders.finalShader);
                }

                passData.finalMaterial = m_FinalMaterial;
                passData.CameraColor = resourceData.cameraColor;
                //TODO: move the entire vbuffer to hardware DRS mode. When Hardware DRS is enabled we will save performance
                // on these buffers, however the final vbuffer will be wasting resolution. This requires a bit of more work to optimize.
                passData.generateMaxZCS = runtimeShaders.maxZCS;
                passData.generateMaxZCS.shaderKeywords = null;

                passData.maxZKernel = passData.generateMaxZCS.FindKernel("ComputeMaxZ");
                passData.maxZDownsampleKernel = passData.generateMaxZCS.FindKernel("ComputeFinalMask");
                passData.dilateMaxZKernel = passData.generateMaxZCS.FindKernel("DilateMask");


                passData.VolumetricFogInitializeCS = runtimeShaders.volumeInitializeCS;
                passData.VolumetricFogInitializeKernel = passData.VolumetricFogInitializeCS.FindKernel("VolumetricFogInitialize");


                passData.VolumetricFogIndirectCS = runtimeShaders.volumetricFogIndirectCS;
                passData.VolumetricFogIndirectKernel = passData.VolumetricFogIndirectCS.FindKernel("ComputeVolumetricFogRenderingParameters");

                passData.VolumetricFogVoxelize = InitRendererLists(renderGraph, frameData);

                passData.VolumetricLightingCS = runtimeShaders.volumetricLightingCS;
                passData.VolumetricLightingKernel = passData.VolumetricLightingCS.FindKernel("VolumetricLighting");

                passData.volumetricLightingFilteringCS = runtimeShaders.volumetricLightingFilteringCS;
                passData.volumetricLightingFilteringKernel = passData.volumetricLightingFilteringCS.FindKernel("FilterVolumetricLighting");

                passData.shaderConstantBuffer = m_ShaderConstantBuffer;

                passData.intermediateMaskSize.x = RenderingUtilsExt.DivRoundUp(cameraData.actualWidth, 8);
                passData.intermediateMaskSize.y = RenderingUtilsExt.DivRoundUp(cameraData.actualHeight, 8);

                passData.finalMaskSize.x = passData.intermediateMaskSize.x / 2;
                passData.finalMaskSize.y = passData.intermediateMaskSize.y / 2;

                passData.minDepthMipOffset.x = depthMipInfo.mipLevelOffsets[4].x;
                passData.minDepthMipOffset.y = depthMipInfo.mipLevelOffsets[4].y;

                passData.densityResolution = m_Resolution;
                passData.dilationWidth = 1;


                passData.depthTexture = depthTexture;
                passData.maxZ8xBuffer = builder.CreateTransientTexture(new TextureDesc((int)(cameraData.scaledWidth / 8.0f), (int)
                        (cameraData.scaledHeight / 8.0f))
                    { format = GraphicsFormat.R32_SFloat, enableRandomWrite = true, name = "MaxZ mask 8x" }
                );
                passData.maxZBuffer = builder.CreateTransientTexture(new TextureDesc((int)(cameraData.scaledWidth / 8.0f), (int)
                        (cameraData.scaledHeight / 8.0f))
                    { format = GraphicsFormat.R32_SFloat, enableRandomWrite = true, name = "MaxZ mask" });
                passData.dilatedMaxZBuffer = builder.CreateTransientTexture(new TextureDesc((int)(cameraData.scaledWidth / 16.0f), (int)
                        (cameraData.scaledHeight / 16.0f))
                    { format = GraphicsFormat.R32_SFloat, enableRandomWrite = true, name = "Dilated MaxZ mask" });
                passData.VBufferDensity = builder.CreateTransientTexture(new TextureDesc(m_Resolution.x, m_Resolution.y)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    slices = m_SliceCount,
                    dimension = TextureDimension.Tex3D,
                    enableRandomWrite = true, name = "VBuffer Density"
                });
                passData.VBufferLighting = builder.CreateTransientTexture(new TextureDesc(m_Resolution.x, m_Resolution.y)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    slices = m_SliceCount,
                    dimension = TextureDimension.Tex3D,
                    enableRandomWrite = true, name = "VBuffer Lighting"
                });

                passData.VisibleVolumeBounds = renderGraph.ImportBuffer(m_VisibleVolumeBoundsBuffer);
                passData.VisibleVolumeIndices = renderGraph.ImportBuffer(m_VisibleVolumeIndicesBuffer);
                passData.globalIndirectArgBuffer = renderGraph.ImportBuffer(fogManager.globalIndirectArgBuffer);
                passData.globalIndirectionBuffer = renderGraph.ImportBuffer(fogManager.globalIndirectionBuffer);
                passData.volumetricFogRenderingBuffer = renderGraph.ImportBuffer(fogManager.volumetricFogRenderingBuffer);

                builder.UseTexture(passData.depthTexture);
                builder.UseRendererList(passData.VolumetricFogVoxelize);
                builder.UseTexture(passData.CameraColor);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc<PassData>((data, ctx) =>
                {
                    // Downsample 8x8 with max operator

                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    ConstantBuffer.PushGlobal(cmd, passData.shaderConstantBuffer, _VolumetricLightingBuffer);

                    #region MaxZ

                    var cs = data.generateMaxZCS;
                    var kernel = data.maxZKernel;
                    int maskW = data.intermediateMaskSize.x;
                    int maskH = data.intermediateMaskSize.y;

                    int dispatchX = maskW;
                    int dispatchY = maskH;

                    cmd.SetComputeTextureParam(cs, kernel, _OutputTexture, data.maxZ8xBuffer);
                    cmd.SetComputeTextureParam(cs, kernel, _CameraDepthTexture, data.depthTexture);

                    cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, 1);

                    // --------------------------------------------------------------
                    // Downsample to 16x16 and compute gradient if required

                    kernel = data.maxZDownsampleKernel;

                    cmd.SetComputeTextureParam(cs, kernel, _InputTexture, data.maxZ8xBuffer);
                    cmd.SetComputeTextureParam(cs, kernel, _OutputTexture, data.maxZBuffer);
                    cmd.SetComputeTextureParam(cs, kernel, _CameraDepthTexture, data.depthTexture);

                    Vector4 srcLimitAndDepthOffset = new Vector4(
                        maskW,
                        maskH,
                        data.minDepthMipOffset.x,
                        data.minDepthMipOffset.y
                    );
                    cmd.SetComputeVectorParam(cs, _SrcOffsetAndLimit, srcLimitAndDepthOffset);
                    cmd.SetComputeFloatParam(cs, _DilationWidth, data.dilationWidth);

                    int finalMaskW = Mathf.CeilToInt(maskW / 2.0f);
                    int finalMaskH = Mathf.CeilToInt(maskH / 2.0f);

                    dispatchX = RenderingUtilsExt.DivRoundUp(finalMaskW, 8);
                    dispatchY = RenderingUtilsExt.DivRoundUp(finalMaskH, 8);

                    ctx.cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, 1);

                    // --------------------------------------------------------------
                    // Dilate max Z and gradient.
                    kernel = data.dilateMaxZKernel;

                    cmd.SetComputeTextureParam(cs, kernel, _InputTexture, data.maxZBuffer);
                    cmd.SetComputeTextureParam(cs, kernel, _OutputTexture, data.dilatedMaxZBuffer);
                    cmd.SetComputeTextureParam(cs, kernel, _CameraDepthTexture, data.depthTexture);

                    srcLimitAndDepthOffset.x = finalMaskW;
                    srcLimitAndDepthOffset.y = finalMaskH;
                    cmd.SetComputeVectorParam(cs, _SrcOffsetAndLimit, srcLimitAndDepthOffset);
                    cmd.DispatchCompute(cs, kernel, dispatchX, dispatchY, 1);

                    #endregion


                    cs = passData.VolumetricFogInitializeCS;
                    kernel = passData.VolumetricFogInitializeKernel;
                    cmd.SetComputeTextureParam(cs, kernel, _VBufferDensity, passData.VBufferDensity);
                    cmd.DispatchCompute(cs, kernel, (passData.densityResolution.x + 7) / 8, (passData.densityResolution.y + 7) / 8, 1);

                    cs = passData.VolumetricFogIndirectCS;
                    kernel = passData.VolumetricFogIndirectKernel;


                    cmd.SetComputeBufferParam(cs, kernel, _VolumeBounds, passData.VisibleVolumeBounds);
                    cmd.SetComputeBufferParam(cs, kernel, _GlobalIndices, passData.VisibleVolumeIndices);
                    cmd.SetComputeBufferParam(cs, kernel, _IndirectArgs, passData.globalIndirectArgBuffer);
                    cmd.SetComputeBufferParam(cs, kernel, _Indirections, passData.globalIndirectionBuffer);
                    cmd.SetComputeBufferParam(cs, kernel, _VolumetricFogRenderingData, passData.volumetricFogRenderingBuffer);

                    cmd.DispatchCompute(cs, kernel, (m_VisibleCount + 31) / 32, 1, 1);

                    cmd.SetGlobalBuffer(_Indirections, fogManager.globalIndirectionBuffer);
                    cmd.SetGlobalBuffer(_VolumetricFogRenderingData, fogManager.volumetricFogRenderingBuffer);

                    CoreUtils.SetRenderTarget(cmd, passData.VBufferDensity);
                    cmd.DrawRendererList(passData.VolumetricFogVoxelize);


                    cs = passData.VolumetricLightingCS;
                    kernel = passData.VolumetricLightingKernel;

                    cmd.SetComputeTextureParam(cs, kernel, _VBufferDensity, passData.VBufferDensity);
                    cmd.SetComputeTextureParam(cs, kernel, _VBufferLightingRW, passData.VBufferLighting);
                    cmd.SetComputeTextureParam(cs, kernel, _MaxZMaskTexture, passData.dilatedMaxZBuffer);

                    cmd.DispatchCompute(cs, kernel, (passData.densityResolution.x + 7) / 8, (passData.densityResolution.y + 7) / 8, 1);


                    cs = passData.volumetricLightingFilteringCS;
                    kernel = passData.volumetricLightingFilteringKernel;

                    cmd.SetComputeTextureParam(cs, kernel, RW_VBufferLighting, passData.VBufferLighting);

                    cmd.DispatchCompute(cs, kernel, (passData.densityResolution.x + 7) / 8, (passData.densityResolution.y + 7) / 8, m_SliceCount);


                    CoreUtils.SetRenderTarget(cmd, passData.CameraColor);

                    passData.finalMaterial.SetTexture(_VBufferLighting, passData.VBufferLighting);
                    CoreUtils.DrawFullScreen(cmd, passData.finalMaterial);
                });

                // return passData.dilatedMaxZBuffer;
            }
        }
        
        public void Dispose()
        { 
            m_VisibleVolumeBoundsBuffer?.Release();
            m_VisibleVolumeIndicesBuffer?.Release();
        }

    }
}