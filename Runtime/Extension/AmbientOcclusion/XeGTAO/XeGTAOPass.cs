using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class XeGTAOPass : ScriptableRenderPass
    {
        private ComputeShader _denoiseCS;
        private ComputeShader _mainPassCS;
        private ComputeShader _prefilterDepthsCS;


        public XeGTAOPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
        }


        public class PassData
        {
            public ComputeShader DenoiseCS;
            public ComputeShader MainPassCS;
            public ComputeShader PrefilterDepthsCS;
            public TextureHandle AOTerm;
            public TextureHandle AOTermPong;
            public TextureHandle Edges;
            public TextureHandle NormalTexture;

            public TextureHandle FinalAOTerm;
            public TextureHandle RawAOTerm;

            public TextureHandle SrcRawDepth;
            public TextureHandle WorkingDepths;
            public XeGTAO.GTAOConstantsCS GTAOConstants;
            public bool OutputBentNormals;
            public int2 Resolution;
            public bool IsDeferred;
            public float4 ResolutionScale;
            public XeGTAO.GTAOSettings Settings;


            //URP Spec property
            public float DirectLightingStrength;
        }


        private static class ShaderIDs
        {
            public static class Global
            {
                public static readonly int _GTAOTerm = Shader.PropertyToID(nameof(_GTAOTerm));
                public static readonly int _GTAOResolutionScale = Shader.PropertyToID(nameof(_GTAOResolutionScale));
            }

            public static class PrefilterDepths
            {
                public static readonly int g_srcRawDepth = Shader.PropertyToID(nameof(g_srcRawDepth));
                public static readonly int g_outWorkingDepthMIP0 = Shader.PropertyToID(nameof(g_outWorkingDepthMIP0));
                public static readonly int g_outWorkingDepthMIP1 = Shader.PropertyToID(nameof(g_outWorkingDepthMIP1));
                public static readonly int g_outWorkingDepthMIP2 = Shader.PropertyToID(nameof(g_outWorkingDepthMIP2));
                public static readonly int g_outWorkingDepthMIP3 = Shader.PropertyToID(nameof(g_outWorkingDepthMIP3));
                public static readonly int g_outWorkingDepthMIP4 = Shader.PropertyToID(nameof(g_outWorkingDepthMIP4));
            }

            public static class MainPass
            {
                public static readonly int g_srcWorkingDepth = Shader.PropertyToID(nameof(g_srcWorkingDepth));
                public static readonly int g_outWorkingAOTerm = Shader.PropertyToID(nameof(g_outWorkingAOTerm));
                public static readonly int g_outWorkingEdges = Shader.PropertyToID(nameof(g_outWorkingEdges));
                public static readonly int g_CameraNormalTexture = Shader.PropertyToID(nameof(g_CameraNormalTexture));
            }

            public static class Denoise
            {
                public static readonly int g_srcWorkingAOTerm = Shader.PropertyToID(nameof(g_srcWorkingAOTerm));
                public static readonly int g_srcWorkingEdges = Shader.PropertyToID(nameof(g_srcWorkingEdges));
                public static readonly int g_outFinalAOTerm = Shader.PropertyToID(nameof(g_outFinalAOTerm));
                public static readonly int g_RawAOTerm = Shader.PropertyToID(nameof(g_RawAOTerm));
            }
        }

        private static class Keywords
        {
            public static readonly string XE_GTAO_COMPUTE_BENT_NORMALS = nameof(XE_GTAO_COMPUTE_BENT_NORMALS);
            public static readonly string DEFERRED = nameof(DEFERRED);
        }

        private static class Profiling
        {
            public static ProfilingSampler PrefilterDepths = new(nameof(PrefilterDepths));
            public static ProfilingSampler MainPass = new(nameof(MainPass));
            public static ProfilingSampler Denoise = new(nameof(Denoise));
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using var builder = renderGraph.AddComputePass("XeGTAO", out PassData passData);
            var setting = VolumeManager.instance.stack.GetComponent<AmbientOcclusion>();

            if (!setting.IsActive() || setting.ambientOcclusionModeParameter.value is not AmbientOcclusionMode.GroundTruthAmbientOcclusion)
            {
                return;
            }

            if (!_prefilterDepthsCS)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<AmbientOcclusionRuntimeShader>();

                _prefilterDepthsCS = runtimeShader.XeGTAOPrefilter;
                _mainPassCS = runtimeShader.XeGTAOMainPass;
                _denoiseCS = runtimeShader.XeGTAODenoise;
            }


            var resourcesData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();


            passData.IsDeferred = frameData.Get<UniversalRenderingData>().renderingMode is RenderingMode.Deferred or RenderingMode.DeferredPlus;
            passData.Settings = XeGTAO.GTAOSettings.Default;
            passData.Settings.QualityLevel = (int)setting.QualityLevel.value;
            passData.Settings.DenoisePasses = (int)setting.DenoisingLevel.value;
            passData.Settings.FinalValuePower *= setting.FinalValuePower.value;
            passData.Settings.FalloffRange *= setting.FalloffRange.value;
            passData.OutputBentNormals = setting.BentNormals.value;
            passData.Resolution = new int2(cameraData.actualWidth, cameraData.actualHeight);
            passData.DirectLightingStrength = setting.directLightingStrength.value;
            const bool rowMajor = false;
            const uint frameCounter = 0;
            var viewCorrectionMatrix = Matrix4x4.Scale(new Vector3(1, -1, -1));
            XeGTAO.GTAOSettings.GTAOUpdateConstants(ref passData.GTAOConstants, passData.Resolution.x, passData.Resolution.y, passData.Settings,
                cameraData.GetGPUProjectionMatrix(true) * viewCorrectionMatrix, rowMajor, frameCounter
            );

            passData.PrefilterDepthsCS = _prefilterDepthsCS;
            passData.MainPassCS = _mainPassCS;
            passData.DenoiseCS = _denoiseCS;
            passData.NormalTexture = resourcesData.cameraNormalsTexture;
            passData.SrcRawDepth = resourcesData.cameraDepthTexture;
            passData.WorkingDepths = builder.CreateTransientTexture(new TextureDesc(cameraData.actualWidth, cameraData.actualHeight)
            {
                enableRandomWrite = true,
                format = GraphicsFormat.R32_SFloat,
                name = "XeGTAO_CameraDepth",
                clearBuffer = false,
                useMipMap = true,
                autoGenerateMips = false,
            });

            passData.AOTerm = builder.CreateTransientTexture(new TextureDesc(cameraData.actualWidth, cameraData.actualHeight)
            {
                enableRandomWrite = true,
                format = passData.OutputBentNormals ? GraphicsFormat.R32_UInt : GraphicsFormat.R8_UInt,
                name = "AOTerm"
            });

            passData.AOTermPong = builder.CreateTransientTexture(new TextureDesc(cameraData.actualWidth, cameraData.actualHeight)
            {
                enableRandomWrite = true,
                format = passData.OutputBentNormals ? GraphicsFormat.R32_UInt : GraphicsFormat.R8_UInt,
                name = "AOTermPong"
            });


            passData.Edges = builder.CreateTransientTexture(new TextureDesc(cameraData.actualWidth, cameraData.actualHeight)
            {
                enableRandomWrite = true,
                format = GraphicsFormat.R8_UNorm,
                name = "Edges",
            });

            passData.FinalAOTerm = renderGraph.CreateTexture(new TextureDesc(cameraData.actualWidth, cameraData.actualHeight)
            {
                enableRandomWrite = true,
                format = passData.OutputBentNormals ? GraphicsFormat.R32_UInt : GraphicsFormat.R8_UInt,
                name = "FinalAOTerm"
            }); //visibility & BentNormal

            passData.RawAOTerm = renderGraph.CreateTexture(new TextureDesc(cameraData.actualWidth, cameraData.actualHeight)
            {
                enableRandomWrite = true,
                format = GraphicsFormat.R8_UNorm,
                name = "URP AOTerm"
            });

            builder.UseTexture(passData.SrcRawDepth);
            builder.UseTexture(passData.NormalTexture);

            builder.UseTexture(passData.FinalAOTerm, AccessFlags.Write);
            builder.UseTexture(passData.RawAOTerm, AccessFlags.Write);

            builder.AllowGlobalStateModification(true);
            builder.SetGlobalTextureAfterPass(passData.RawAOTerm, Shader.PropertyToID("_ScreenSpaceOcclusionTexture"));

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<PassData>((data, context) =>
            {
                CoreUtils.SetKeyword(context.cmd, "DEFERRED", passData.IsDeferred);

                using (new ProfilingScope(context.cmd, Profiling.PrefilterDepths))
                {
                    const int kernelIndex = 0;
                    const int threadGroupSizeDim = 16;
                    int threadGroupsX = RenderingUtilsExt.DivRoundUp(data.Resolution.x, threadGroupSizeDim);
                    int threadGroupsY = RenderingUtilsExt.DivRoundUp(data.Resolution.y, threadGroupSizeDim);
                    context.cmd.SetComputeTextureParam(data.PrefilterDepthsCS, kernelIndex, ShaderIDs.PrefilterDepths.g_srcRawDepth, data.SrcRawDepth);
                    context.cmd.SetComputeTextureParam(data.PrefilterDepthsCS, kernelIndex, ShaderIDs.PrefilterDepths.g_outWorkingDepthMIP0, data.WorkingDepths,
                        0);
                    context.cmd.SetComputeTextureParam(data.PrefilterDepthsCS, kernelIndex, ShaderIDs.PrefilterDepths.g_outWorkingDepthMIP1, data.WorkingDepths,
                        1);
                    context.cmd.SetComputeTextureParam(data.PrefilterDepthsCS, kernelIndex, ShaderIDs.PrefilterDepths.g_outWorkingDepthMIP2, data.WorkingDepths,
                        2);
                    context.cmd.SetComputeTextureParam(data.PrefilterDepthsCS, kernelIndex, ShaderIDs.PrefilterDepths.g_outWorkingDepthMIP3, data.WorkingDepths,
                        3);
                    context.cmd.SetComputeTextureParam(data.PrefilterDepthsCS, kernelIndex, ShaderIDs.PrefilterDepths.g_outWorkingDepthMIP4, data.WorkingDepths,
                        4);
                    ConstantBuffer.Push(context.cmd, data.GTAOConstants, data.PrefilterDepthsCS, Shader.PropertyToID(nameof(XeGTAO.GTAOConstantsCS)));

                    context.cmd.DispatchCompute(data.PrefilterDepthsCS, kernelIndex, threadGroupsX, threadGroupsY, 1);
                }

                using (new ProfilingScope(context.cmd, Profiling.MainPass))
                {
                    CoreUtils.SetKeyword(data.MainPassCS, Keywords.XE_GTAO_COMPUTE_BENT_NORMALS, data.OutputBentNormals);

                    int kernelIndex = data.Settings.QualityLevel;
                    int threadGroupsX = RenderingUtilsExt.DivRoundUp(data.Resolution.x, XeGTAO.XE_GTAO_NUMTHREADS_X);
                    int threadGroupsY = RenderingUtilsExt.DivRoundUp(data.Resolution.y, XeGTAO.XE_GTAO_NUMTHREADS_Y);

                    context.cmd.SetComputeTextureParam(data.MainPassCS, kernelIndex, ShaderIDs.MainPass.g_srcWorkingDepth, data.WorkingDepths, 0);
                    context.cmd.SetComputeTextureParam(data.MainPassCS, kernelIndex, ShaderIDs.MainPass.g_srcWorkingDepth, data.WorkingDepths, 0);
                    context.cmd.SetComputeTextureParam(data.MainPassCS, kernelIndex, ShaderIDs.MainPass.g_CameraNormalTexture, data.NormalTexture, 0);
                    context.cmd.SetComputeTextureParam(data.MainPassCS, kernelIndex, ShaderIDs.MainPass.g_outWorkingAOTerm, data.AOTerm);
                    context.cmd.SetComputeTextureParam(data.MainPassCS, kernelIndex, ShaderIDs.MainPass.g_outWorkingEdges, data.Edges);
                    ConstantBuffer.Push(context.cmd, data.GTAOConstants, data.MainPassCS, Shader.PropertyToID(nameof(XeGTAO.GTAOConstantsCS)));
                    context.cmd.DispatchCompute(data.MainPassCS, kernelIndex, threadGroupsX, threadGroupsY, 1);
                }

                using (new ProfilingScope(context.cmd, Profiling.Denoise))
                {
                    CoreUtils.SetKeyword(data.DenoiseCS, Keywords.XE_GTAO_COMPUTE_BENT_NORMALS, data.OutputBentNormals);

                    int passCount = math.max(1, data.Settings.DenoisePasses);
                    for (int passIndex = 0; passIndex < passCount; passIndex++)
                    {
                        bool isLastPass = passIndex == passCount - 1;
                        int kernelIndex = isLastPass ? 1 : 0;

                        int threadGroupsX = RenderingUtilsExt.DivRoundUp(data.Resolution.x, XeGTAO.XE_GTAO_NUMTHREADS_X);
                        int threadGroupsY = RenderingUtilsExt.DivRoundUp(data.Resolution.y, XeGTAO.XE_GTAO_NUMTHREADS_Y);

                        context.cmd.SetComputeTextureParam(data.DenoiseCS, kernelIndex, ShaderIDs.Denoise.g_srcWorkingAOTerm, data.AOTerm);
                        context.cmd.SetComputeTextureParam(data.DenoiseCS, kernelIndex, ShaderIDs.Denoise.g_srcWorkingEdges, data.Edges);
                        context.cmd.SetComputeTextureParam(data.DenoiseCS, kernelIndex, ShaderIDs.Denoise.g_outFinalAOTerm,
                            isLastPass ? data.FinalAOTerm : data.AOTermPong
                        );
                        context.cmd.SetComputeTextureParam(data.DenoiseCS, kernelIndex, ShaderIDs.Denoise.g_RawAOTerm, data.RawAOTerm);

                        // ConstantBuffer.PushGlobal(context.cmd, data.GTAOConstants, Shader.PropertyToID(nameof(XeGTAO.GTAOConstantsCS)));

                        ConstantBuffer.Push(context.cmd, data.GTAOConstants, data.DenoiseCS, Shader.PropertyToID(nameof(XeGTAO.GTAOConstantsCS)));
                        context.cmd.DispatchCompute(data.DenoiseCS, kernelIndex, threadGroupsX, threadGroupsY, 1);
                        (data.AOTerm, data.AOTermPong) = (data.AOTermPong, data.AOTerm);
                    }
                }

                context.cmd.SetGlobalVector("_AmbientOcclusionParam",
                    new Vector4(1f, 0f, 0f, data.DirectLightingStrength));
                CoreUtils.SetKeyword(context.cmd, ShaderKeywordStrings.ScreenSpaceOcclusion, true);
            });

            resourcesData.ssaoTexture = passData.RawAOTerm;
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.ScreenSpaceOcclusion, false);
            CoreUtils.SetKeyword(cmd, "DEFERRED", false);
        }
    }
}