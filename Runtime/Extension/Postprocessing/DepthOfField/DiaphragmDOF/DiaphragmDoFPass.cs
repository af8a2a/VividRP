using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class DiaphragmDoFPass : ScriptableRenderPass
    {
        private PhysicallyDepthOfField physicallyDepthOfField;


        public ComputeShader dofCoCCS;
        public int dofCoCKernel;

        ComputeShader pbDoFCoCMinMaxCS;
        int pbDoFMinMaxKernel;

        ComputeShader pbDoFDilateCS;
        int pbDoFDilateKernel;

        ComputeShader dofComputeApertureShapeCS;
        int dofComputeApertureShapeKernel;

        ComputeShader dofComputeSlowTilesCS;
        int dofComputeSlowTilesKernel;

        ComputeShader dofMipCS;
        int dofMipColorKernel;
        int dofMipCoCKernel;
        ComputeShader dofMipSafeCS;
        int dofMipSafeKernel;
        TextureHandle sourcePyramid;


        ComputeShader pbDoFGatherCS;
        int pbDoFGatherKernel;

        ComputeShader pbDoFCombineCS;
        int pbDoFCombineKernel;


        static int k_DepthOfFieldApertureShapeBufferSize = 256;

        internal class PassData
        {
            //Common
            public DepthOfFieldResolution resolution;
            public float FocalLength;
            public float focusDistance;
            public float farMaxBlur;
            public float nearMaxBlur;
            public Vector2 cocLimit;
            public Vector2Int viewportSize;
            public Vector2 physicalCameraCurvature;
            public float physicalCameraAperture;
            public float physicalCameraAnamorphism;
            public float physicalCameraBarrelClipping;
            public int physicalCameraBladeCount;


            public TextureHandle source;
            public TextureHandle destination;


            public ComputeShader dofCircleOfConfusionCS;
            public int dofCircleOfConfusionKernel;
            public UniversalCameraData cameraData;
            public TextureHandle depthBuffer;
            public TextureHandle fullresCoC;


            public ComputeShader pbDoFCoCMinMaxCS;
            public ComputeShader pbDoFDilateCS;
            public int pbDoFDilateKernel;
            public int pbDoFMinMaxKernel;
            public int minMaxCoCTileSize;


            public TextureHandle minMaxCoCPing;
            public TextureHandle minMaxCoCPong;
            public TextureHandle sourcePyramid;
            public TextureHandle scaledDof;


            public ComputeShader dofComputeApertureShapeCS;
            public int dofComputeApertureShapeKernel;
            public BufferHandle shapeTable;


            public ComputeShader dofComputeSlowTilesCS;
            public int dofComputeSlowTilesKernel;


            public ComputeShader dofMipCS;
            public int dofMipColorKernel;
            public int dofMipCoCKernel;
            public ComputeShader dofMipSafeCS;
            public int dofMipSafeKernel;


            public ComputeShader pbDoFGatherCS;
            public int pbDoFGatherKernel;
            public int farSampleCount;
            public int nearSampleCount;
            public Vector2 adaptiveSamplingWeights;

            public ComputeShader pbDoFCombineCS;
            public int pbDoFCombineKernel;
        }


        internal enum ProfileID
        {
            DepthOfFieldCoC,
            DepthOfFieldDilate,
            DepthOfFieldApertureShape,
            DepthOfFieldComputeSlowTiles,
            DepthOfFieldPyramid,
            DepthOfFieldGatherNear,
            DepthOfFieldCombine
        }


        void InitResource(ref PassData passData, RenderGraph renderGraph, UniversalCameraData cameraData,
            UniversalResourceData resourceData)
        {
            passData.dofCircleOfConfusionCS = dofCoCCS;
            passData.dofCircleOfConfusionKernel = dofCoCKernel;
            passData.FocalLength = (36 / 2.0f) / Mathf.Tan(cameraData.camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            passData.viewportSize = new Vector2Int(cameraData.camera.pixelWidth, cameraData.camera.pixelHeight);
            passData.focusDistance = physicallyDepthOfField.FocusDistance;
            passData.physicalCameraAperture = physicallyDepthOfField.Aperture;
            passData.farMaxBlur = physicallyDepthOfField.farMaxBlur;
            passData.nearMaxBlur = physicallyDepthOfField.nearMaxBlur;
            passData.adaptiveSamplingWeights
                = new Vector2(
                    physicallyDepthOfField.adaptiveSamplingWeight <= 1.0f
                        ? physicallyDepthOfField.adaptiveSamplingWeight
                        : 1.0f,
                    physicallyDepthOfField.adaptiveSamplingWeight > 1.0f
                        ? physicallyDepthOfField.adaptiveSamplingWeight
                        : 1.0f
                );
            passData.cameraData = cameraData;
            passData.resolution = physicallyDepthOfField.resolution;
            passData.physicalCameraAnamorphism = cameraData.camera.anamorphism;
            passData.physicalCameraBarrelClipping = cameraData.camera.barrelClipping;
            passData.physicalCameraBladeCount = cameraData.camera.bladeCount;
            passData.physicalCameraCurvature = cameraData.camera.curvature;
            passData.pbDoFCoCMinMaxCS = pbDoFCoCMinMaxCS;
            passData.pbDoFMinMaxKernel = pbDoFMinMaxKernel;
            passData.pbDoFDilateCS = pbDoFDilateCS;
            passData.pbDoFDilateKernel = pbDoFDilateKernel;
            passData.minMaxCoCTileSize = 8;


            passData.dofComputeApertureShapeCS = dofComputeApertureShapeCS;
            passData.dofComputeApertureShapeKernel = dofComputeApertureShapeKernel;


            passData.dofComputeSlowTilesCS = dofComputeSlowTilesCS;
            passData.dofComputeSlowTilesKernel = dofComputeSlowTilesKernel;

            passData.dofMipCS = dofMipCS;

            passData.dofMipColorKernel = dofMipColorKernel;
            passData.dofMipCoCKernel = dofMipCoCKernel;
            passData.dofMipSafeCS = dofMipSafeCS;
            passData.dofMipSafeKernel = dofMipSafeKernel;

            passData.pbDoFGatherCS = pbDoFGatherCS;
            passData.pbDoFGatherKernel = pbDoFGatherKernel;

            passData.pbDoFCombineCS = pbDoFCombineCS;
            passData.pbDoFCombineKernel = pbDoFCombineKernel;


            if (passData.resolution != DepthOfFieldResolution.Quarter)
            {
                passData.pbDoFGatherCS.EnableKeyword("FORCE_POINT_SAMPLING");
                passData.pbDoFCombineCS.EnableKeyword("FORCE_POINT_SAMPLING");
            }

            passData.nearSampleCount = physicallyDepthOfField.nearSampleCount;
            passData.farSampleCount = physicallyDepthOfField.farSampleCount;
        }


        public void Setup(PhysicallyDepthOfField depthOfField)
        {
            physicallyDepthOfField = depthOfField;

            dofCoCCS = Resources.Load<ComputeShader>("DoFCircleOfConfusion");
            dofCoCKernel = dofCoCCS.FindKernel("KMainCoCPhysical");

            pbDoFCoCMinMaxCS = Resources.Load<ComputeShader>("DoFCoCMinMax");
            pbDoFMinMaxKernel = pbDoFCoCMinMaxCS.FindKernel("KMainCoCMinMax");

            pbDoFDilateCS = Resources.Load<ComputeShader>("DoFMinMaxDilate");
            pbDoFDilateKernel = pbDoFDilateCS.FindKernel("KMain");

            dofComputeApertureShapeCS = Resources.Load<ComputeShader>("DoFApertureShape");
            dofComputeApertureShapeKernel = dofComputeApertureShapeCS.FindKernel("ComputeShapeBuffer");

            dofComputeSlowTilesCS = Resources.Load<ComputeShader>("DoFComputeSlowTiles");
            dofComputeSlowTilesKernel = dofComputeSlowTilesCS.FindKernel("ComputeSlowTiles");


            dofMipCS = Resources.Load<ComputeShader>("DepthOfFieldMip");
            dofMipColorKernel = dofMipCS.FindKernel("KMainColor");
            dofMipCoCKernel = dofMipCS.FindKernel("KMainCoC");
            dofMipSafeCS = Resources.Load<ComputeShader>("DepthOfFieldMipSafe");
            dofMipSafeKernel = dofMipSafeCS.FindKernel("KMain");


            pbDoFGatherCS = Resources.Load<ComputeShader>("DoFGather");
            pbDoFGatherKernel = pbDoFGatherCS.FindKernel("KMain");


            pbDoFCombineCS = Resources.Load<ComputeShader>("DoFCombine");
            pbDoFCombineKernel = pbDoFCombineCS.FindKernel("UpsampleFastTiles");
        }


        static void ExecutePass(PassData passData, ComputeGraphContext computeGraphContext)
        {
            var cmd = computeGraphContext.cmd;


            float F = passData.FocalLength / 1000f;
            float A = passData.FocalLength / passData.physicalCameraAperture;
            float P = passData.focusDistance;


            var scale = passData.viewportSize / new Vector2(1920f, 1080f);
            float resolutionScale = Mathf.Min(scale.x, scale.y) * 2f;

            float farMaxBlur = resolutionScale * passData.farMaxBlur;
            float nearMaxBlur = resolutionScale * passData.nearMaxBlur;
            float radiusMultiplier = 4.0f;

            Vector2 cocLimit = new Vector2(
                Mathf.Max(radiusMultiplier * farMaxBlur, 0.01f),
                Mathf.Max(radiusMultiplier * nearMaxBlur, 0.01f));
            float maxCoc = Mathf.Max(cocLimit.x, cocLimit.y);

            float sensorScale = (0.5f / 36) * (float)passData.viewportSize.y;

            float maxFarCoC = sensorScale * (A * F) / Mathf.Max((P - F), 1e-6f);


            float cocBias = maxFarCoC * (1f - P / passData.cameraData.camera.farClipPlane);
            float cocScale = maxFarCoC * P *
                             (passData.cameraData.camera.farClipPlane - passData.cameraData.camera.nearClipPlane) /
                             (passData.cameraData.camera.farClipPlane * passData.cameraData.camera.nearClipPlane);

            using (new ProfilingScope(cmd, ProfilingSampler.Get(ProfileID.DepthOfFieldCoC)))
            {
                cmd.SetComputeVectorParam(passData.dofCircleOfConfusionCS, "_Params",
                    new Vector4(cocLimit.x, cocLimit.y, cocScale, cocBias));


                cmd.SetComputeTextureParam(passData.dofCircleOfConfusionCS, passData.dofCircleOfConfusionKernel,
                    "_CameraDepthTexture", passData.depthBuffer);
                cmd.SetComputeTextureParam(passData.dofCircleOfConfusionCS, passData.dofCircleOfConfusionKernel,
                    "_OutputTexture", passData.fullresCoC);

                cmd.DispatchCompute(passData.dofCircleOfConfusionCS, passData.dofCircleOfConfusionKernel,
                    (passData.viewportSize.x + 7) / 8, (passData.viewportSize.y + 7) / 8,
                    1);
            }

            using (new ProfilingScope(cmd, ProfilingSampler.Get(ProfileID.DepthOfFieldDilate)))
            {
                int tileSize = passData.minMaxCoCTileSize;
                int tileCountX = Mathf.CeilToInt(passData.viewportSize.x / (float)tileSize);
                int tileCountY = Mathf.CeilToInt(passData.viewportSize.y / (float)tileSize);
                int tx = RenderingUtilsExt.DivRoundUp(tileCountX, 8);
                int ty = RenderingUtilsExt.DivRoundUp(tileCountY, 8);
                // Min Max CoC tiles
                {
                    var cs = passData.pbDoFCoCMinMaxCS;
                    var kernel = passData.pbDoFMinMaxKernel;

                    cmd.SetComputeTextureParam(cs, kernel, "_InputTexture", passData.fullresCoC, 0);
                    cmd.SetComputeVectorParam(cs, "_OutputResolution", new Vector2(tileCountX, tileCountY));
                    cmd.SetComputeTextureParam(cs, kernel, "_OutputTexture", passData.minMaxCoCPing, 0);
                    cmd.DispatchCompute(cs, kernel, tx, ty, 1);
                }

                //  Min Max CoC tile dilation
                {
                    var cs = passData.pbDoFDilateCS;
                    var kernel = passData.pbDoFDilateKernel;

                    int iterations = (int)Mathf.Max(Mathf.Ceil(cocLimit.y / passData.minMaxCoCTileSize), 1.0f);
                    for (int pass = 0; pass < iterations; ++pass)
                    {
                        cmd.SetComputeTextureParam(cs, kernel, "_InputTexture", passData.minMaxCoCPing, 0);
                        cmd.SetComputeTextureParam(cs, kernel, "_OutputTexture", passData.minMaxCoCPong, 0);
                        cmd.DispatchCompute(cs, kernel, tx, ty, 1);
                        CoreUtils.Swap(ref passData.minMaxCoCPing, ref passData.minMaxCoCPong);
                    }
                }
            }

            // Compute the shape of the aperture into a buffer, sampling this buffer in the loop of the DoF
            // is faster than computing sin/cos of each angle for the sampling and it let us handle the shape
            // of the aperture with the blade count.
            using (new ProfilingScope(cmd, ProfilingSampler.Get(ProfileID.DepthOfFieldApertureShape)))
            {
                var cs = passData.dofComputeApertureShapeCS;
                var kernel = passData.dofComputeApertureShapeKernel;
                float rotation = (passData.physicalCameraAperture - Camera.kMinAperture) /
                                 (Camera.kMaxAperture - Camera.kMinAperture);
                rotation *= (360f / passData.physicalCameraBladeCount) *
                            Mathf.Deg2Rad; // TODO: Crude approximation, make it correct

                float ngonFactor = 1f;
                if (passData.physicalCameraCurvature.y - passData.physicalCameraCurvature.x > 0f)
                    ngonFactor = (passData.physicalCameraAperture - passData.physicalCameraCurvature.x) /
                                 (passData.physicalCameraCurvature.y - passData.physicalCameraCurvature.x);

                ngonFactor = Mathf.Clamp01(ngonFactor);
                ngonFactor = Mathf.Lerp(ngonFactor, 0f, Mathf.Abs(passData.physicalCameraAnamorphism));

                cmd.SetComputeVectorParam(cs, "_Params",
                    new Vector4(passData.physicalCameraBladeCount, ngonFactor, rotation,
                        passData.physicalCameraAnamorphism / 4f));
                cmd.SetComputeIntParam(cs, "_ApertureShapeTableCount", k_DepthOfFieldApertureShapeBufferSize);
                cmd.SetComputeBufferParam(cs, kernel, "_ApertureShapeTable", passData.shapeTable);
                cmd.DispatchCompute(cs, kernel, k_DepthOfFieldApertureShapeBufferSize / 64, 1, 1);
            }


            // Slow tiles refer to a tile that contain both in focus and defocus pixels which requires to gather the CoC
            // per pixel

            // Compute the slow path tiles into the output buffer.
            // The output of this pass is used as input for the color pyramid below, this is to avoid some
            // leaking artifacts on the border of the tiles. Blurring the slow tiles allows for the bilinear
            // interpolation in the final upsample pass to get more correct data instead of sampling non-blurred tiles.
            using (new ProfilingScope(cmd, ProfilingSampler.Get(ProfileID.DepthOfFieldComputeSlowTiles)))
            {
                var cs = passData.dofComputeSlowTilesCS;
                var kernel = passData.dofComputeSlowTilesKernel;
                float sampleCount = Mathf.Max(passData.nearSampleCount, passData.farSampleCount);
                float anamorphism = passData.physicalCameraAnamorphism / 4f;

                float mipLevel = 1 + Mathf.Ceil(Mathf.Log(maxCoc, 2));
                cmd.SetComputeVectorParam(cs, "_Params",
                    new Vector4(sampleCount, maxCoc, anamorphism, 0.0f));
                cmd.SetComputeVectorParam(cs, "_Params2",
                    new Vector4(1, 1, (float)passData.resolution, 1.0f / (float)passData.resolution));

                cmd.SetComputeTextureParam(cs, kernel, "_InputTexture", passData.source);
                cmd.SetComputeTextureParam(cs, kernel, "_InputCoCTexture", passData.fullresCoC);
                cmd.SetComputeTextureParam(cs, kernel, "_TileList", passData.minMaxCoCPing, 0);
                cmd.SetComputeTextureParam(cs, kernel, "_OutputTexture", passData.destination);
                cmd.SetComputeBufferParam(cs, kernel, "_ApertureShapeTable", passData.shapeTable);
                cmd.SetComputeIntParam(cs, "_ApertureShapeTableCount", k_DepthOfFieldApertureShapeBufferSize);

                cmd.DispatchCompute(cs, kernel, (passData.viewportSize.x + 7) / 8,
                    (passData.viewportSize.y + 7) / 8, 1);
            }


            // When the DoF is at full resolution, we consider that this is the highest quality level so we remove
            // the sampling from the pyramid which causes artifacts on the border of tiles in certain scenarios.
            if (passData.resolution != DepthOfFieldResolution.Full)
            {
                // DoF color pyramid with the slow tiles inside
                using (new ProfilingScope(cmd, ProfilingSampler.Get(ProfileID.DepthOfFieldPyramid)))
                {
                    var cs = passData.dofMipCS;
                    var kernel = passData.dofMipColorKernel;

                    cmd.SetComputeTextureParam(cs, kernel, "_InputTexture", passData.destination, 0);
                    cmd.SetComputeTextureParam(cs, kernel, "_OutputTexture", passData.sourcePyramid, 0);
                    cmd.SetComputeTextureParam(cs, kernel, "_OutputMip1", passData.sourcePyramid, 1);
                    cmd.SetComputeTextureParam(cs, kernel, "_OutputMip2", passData.sourcePyramid, 2);
                    cmd.SetComputeTextureParam(cs, kernel, "_OutputMip3", passData.sourcePyramid, 3);
                    cmd.SetComputeTextureParam(cs, kernel, "_OutputMip4", passData.sourcePyramid, 4);

                    int tx = ((passData.viewportSize.x >> 1) + 7) / 8;
                    int ty = ((passData.viewportSize.y >> 1) + 7) / 8;
                    cmd.DispatchCompute(cs, kernel, tx, ty, 1);
                }
            }

            // Blur far and near tiles with a "fast" blur
            using (new ProfilingScope(cmd, ProfilingSampler.Get(ProfileID.DepthOfFieldGatherNear)))
            {
                var cs = passData.pbDoFGatherCS;
                var kernel = passData.pbDoFGatherKernel;
                float sampleCount = Mathf.Max(passData.nearSampleCount, passData.farSampleCount);
                float anamorphism = passData.physicalCameraAnamorphism / 4f;

                float mipLevel = 1 + Mathf.Ceil(Mathf.Log(maxCoc, 2));
                cmd.SetComputeVectorParam(cs, "_Params", new Vector4(sampleCount, maxCoc, anamorphism, 0.0f));
                cmd.SetComputeVectorParam(cs, "_Params2",
                    new Vector4(mipLevel, 3, 1.0f / (float)passData.resolution, (float)passData.resolution));
                cmd.SetComputeVectorParam(cs, "_Params3",
                    new Vector4(passData.adaptiveSamplingWeights.x, passData.adaptiveSamplingWeights.y, 0.0f,
                        0.0f));
                cmd.SetComputeTextureParam(cs, kernel, "_InputTexture",
                    passData.resolution != DepthOfFieldResolution.Full ? passData.sourcePyramid : passData.source);
                cmd.SetComputeTextureParam(cs, kernel, "_InputCoCTexture", passData.fullresCoC);
                cmd.SetComputeTextureParam(cs, kernel, "_OutputTexture", passData.scaledDof);
                cmd.SetComputeTextureParam(cs, kernel, "_TileList", passData.minMaxCoCPing, 0);
                // BlueNoiseSystem.BindDitheredTextureSet(cmd, passData.ditheredTextureSet);
                int scaledWidth = (passData.viewportSize.x / (int)passData.resolution + 7) / 8;
                int scaledHeight = (passData.viewportSize.y / (int)passData.resolution + 7) / 8;
                cmd.SetComputeBufferParam(cs, kernel, "_ApertureShapeTable", passData.shapeTable);
                cmd.SetComputeIntParam(cs, "_ApertureShapeTableCount", k_DepthOfFieldApertureShapeBufferSize);

                cmd.DispatchCompute(cs, kernel, scaledWidth, scaledHeight, 1);
            }


            // Upscale near/far defocus tiles with a bilinear filter. The bilinear filtering leaking is reduced
            // because the neighbouring tiles have already been blurred by the first slow tile pass.
            using (new ProfilingScope(cmd, ProfilingSampler.Get(ProfileID.DepthOfFieldCombine)))
            {
                var cs = passData.pbDoFCombineCS;
                var kernel = passData.pbDoFCombineKernel;
                float sampleCount = Mathf.Max(passData.nearSampleCount, passData.farSampleCount);
                float anamorphism = passData.physicalCameraAnamorphism / 4f;

                float mipLevel = 1 + Mathf.Ceil(Mathf.Log(maxCoc, 2));
                cmd.SetComputeVectorParam(cs, "_Params", new Vector4(sampleCount, maxCoc, anamorphism, 0.0f));
                cmd.SetComputeVectorParam(cs, "_Params2",
                    new Vector4(passData.adaptiveSamplingWeights.x, passData.adaptiveSamplingWeights.y,
                        (float)passData.resolution, 1.0f / (float)passData.resolution));
                cmd.SetComputeTextureParam(cs, kernel, "_InputTexture", passData.source);
                cmd.SetComputeTextureParam(cs, kernel, "_InputCoCTexture", passData.fullresCoC);
                cmd.SetComputeTextureParam(cs, kernel, "_InputNearTexture", passData.scaledDof);
                cmd.SetComputeTextureParam(cs, kernel, "_TileList", passData.minMaxCoCPing, 0);
                cmd.SetComputeTextureParam(cs, kernel, "_OutputTexture", passData.destination);
                cmd.SetComputeIntParam(cs, "_DebugTileClassification", 0);

                cmd.DispatchCompute(cs, kernel, (passData.viewportSize.x + 7) / 8,
                    (passData.viewportSize.y + 7) / 8, 1);
            }
        }


        static void GetDoFResolutionScale(in PassData passData, out float scale, out float resolutionScale)
        {
            scale = 1f / (float)passData.resolution;
            resolutionScale = (passData.viewportSize.y / 1080f) * 2f;
            // Note: The DoF sampling is performed in normalized space in the shader, so we don't need any scaling for half/quarter resoltion.
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddComputePass<PassData>("DiaphragmDoF", out var data))
            {
                var resourcesData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (!cameraData.isGameCamera)
                {
                    return;
                }

                InitResource(ref data, renderGraph, cameraData, resourcesData);
                GetDoFResolutionScale(data, out float scale, out float resolutionScale);
                var screenScale = new Vector2(scale, scale);


                data.fullresCoC = builder.CreateTransientTexture(new TextureDesc(Vector2.one)
                {
                    name = "Full res CoC",
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    useMipMap = false,
                    enableRandomWrite = true
                });

                ScaleFunc scaler = size => new Vector2Int((size.x + 7) / 8, (size.y + 7) / 8);

                data.scaledDof = builder.CreateTransientTexture(new TextureDesc(screenScale)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite = true, name = "Scaled DoF"
                });


                // if (data.resolution != DepthOfFieldResolution.Full)
                data.sourcePyramid = builder.CreateTransientTexture(
                    renderGraph.CreateTexture(new TextureDesc(Vector2.one)
                    {
                        name = "DoF Source Pyramid",
                        format = GraphicsFormat.R16G16B16A16_SFloat,
                        useMipMap = true,
                        enableRandomWrite = true
                    }));

                data.minMaxCoCPing = builder.CreateTransientTexture(new TextureDesc(scaler)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat, useMipMap = false, enableRandomWrite = true,
                    name = "CoC Min Max Tiles"
                });

                data.minMaxCoCPong = builder.CreateTransientTexture(new TextureDesc(scaler)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat, useMipMap = false, enableRandomWrite = true,
                    name = "CoC Min Max Tiles"
                });


                data.depthBuffer = resourcesData.cameraDepth;

                data.shapeTable = builder.CreateTransientBuffer(new BufferDesc(k_DepthOfFieldApertureShapeBufferSize,
                    sizeof(float) * 2));
                data.source = resourcesData.activeColorTexture;
                data.destination = renderGraph.CreateTexture(new TextureDesc(Vector2.one)
                {
                    name = "DoF Destination",
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    useMipMap = false,
                    enableRandomWrite = true
                });

                builder.UseTexture(data.depthBuffer);
                builder.UseTexture(data.source);

                builder.UseTexture(data.fullresCoC, AccessFlags.ReadWrite);
                builder.UseTexture(data.minMaxCoCPing, AccessFlags.ReadWrite);
                builder.UseTexture(data.minMaxCoCPong, AccessFlags.ReadWrite);
                builder.UseTexture(data.sourcePyramid, AccessFlags.ReadWrite);
                builder.UseTexture(data.scaledDof, AccessFlags.ReadWrite);
                builder.UseBuffer(data.shapeTable, AccessFlags.ReadWrite);
                builder.UseTexture(data.destination, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData passData, ComputeGraphContext computeGraphContext) =>
                {
                    ExecutePass(passData, computeGraphContext);
                });
                resourcesData.cameraColor = data.destination;
            }
        }
    }
}