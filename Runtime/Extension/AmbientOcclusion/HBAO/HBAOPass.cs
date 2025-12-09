using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;


namespace UnityEngine.Rendering.Universal
{
    public class HBAOPass : ScriptableRenderPass
    {
        private ComputeShader DeinterleaveDepthShader;
        private int DeinterleaveDepthKernel;

        private ComputeShader DeinterleaveNormalShader;
        private int DeinterleaveNormalKernel;

        private ComputeShader DeinterleaveAOShader;
        private int DeinterleaveAOKernel;

        private ComputeShader ReinterleaveAOShader;
        private int ReinterleaveAOKernel;

        private ComputeShader BlurAOShader;
        private int BlurAOKernel;

        public HBAOPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
        }


        enum ProfileID
        {
            DeinterleaveDepth,
            DeinterleaveNormal,
            Clear,
            SSAOCalc,
            Reinterleave,
            SSAOBlur
        }

        class PassData
        {
            internal ComputeShader DeinterleaveDepthShader;
            internal int DeinterleaveDepthKernel;

            internal ComputeShader DeinterleaveNormalShader;
            internal int DeinterleaveNormalKernel;

            internal ComputeShader HBAOCalcShader;
            internal int HBAOCalcKernelID;

            internal ComputeShader HBAOReinterleaveShader;
            internal int HBAOReinterleaveKernelID;

            internal ComputeShader HBAOBlurShader;
            internal int HBAOBlurKernelID;


            internal AmbientOcclusion setting;
            internal UniversalCameraData cameraData;
            internal float2 Dimension;

            internal TextureHandle depthTexture;
            internal TextureHandle normalTexture;

            internal TextureHandle deinterleaveNormalTexture;
            internal TextureHandle deinterleaveDepthTexture;
            internal TextureHandle deinterleaveSSAOTexture;
            internal TextureHandle reinterleaveSSAOTexture;

            internal TextureHandle SSAOPing;
            internal TextureHandle SSAOPong;
        }


        static Vector4[] GetJitter()
        {
            Vector4[] jitter = new Vector4[16];
            var rand = new Unity.Mathematics.Random();
            rand.InitState();
            float numDir = 8; // keep in sync to shader

            for (int i = 0; i < 16; i++)
            {
                float Rand1 = rand.NextFloat();
                float Rand2 = rand.NextFloat();
                float Angle = 2 * math.PI * Rand1 / numDir;
                jitter[i].x = math.cos(Angle);
                jitter[i].y = math.sin(Angle);
                jitter[i].z = Rand2;
                jitter[i].w = 0;
            }


            return jitter;
        }


        static void ExecutePass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;


            float fovRad = data.cameraData.camera.fieldOfView * Mathf.Deg2Rad;
            float invHalfTanFov = 1 / Mathf.Tan(fovRad * 0.5f);
            Vector2 focalLen = new Vector2(invHalfTanFov * data.cameraData.camera.aspect, invHalfTanFov);
            Vector2 invFocalLen = new Vector2(1 / focalLen.x, 1 / focalLen.y);

            float4 uvToView = new Vector4(2 * invFocalLen.x, 2 * invFocalLen.y, -1 * invFocalLen.x, -1 * invFocalLen.y);

            float R = data.setting.radius.value;
            float R2 = R * R;
            float NegInvR2 = -1.0f / R2;
            float RadiusToScreen = R * 0.5f * data.cameraData.pixelHeight / (2.0f / invHalfTanFov);
            float PowExponent = math.max(data.setting.intensity.value, 0.0f);
            float NDotVBias = math.min(math.max(0.0f, data.setting.bias.value), 1.0f);
            float AOMultiplier = 1.0f / (1.0f - NDotVBias);
            int halfWidth = ((data.cameraData.camera.pixelWidth + 1) / 2);
            int halfHeight = ((data.cameraData.camera.pixelHeight + 1) / 2);

            float2 HalfResolution = new float2(halfWidth, halfHeight);
            float2 InvHalfResolution = new float2(1.0f / halfWidth, 1.0f / halfHeight);
            float2 InvFullResolution = new float2(1.0f / (data.cameraData.pixelWidth), 1.0f / data.cameraData.pixelHeight);


            using (new ProfilingScope(cmd, ProfilingSampler.Get(ProfileID.DeinterleaveDepth)))
            {
                cmd.SetComputeTextureParam(data.DeinterleaveDepthShader, data.DeinterleaveDepthKernel, "LinearDepthInput", data.depthTexture);
                cmd.SetComputeTextureParam(data.DeinterleaveDepthShader, data.DeinterleaveDepthKernel, "DeinterleaveDepthOutput",
                    data.deinterleaveDepthTexture);
                var threadX = RenderingUtilsExt.DivRoundUp((int)data.Dimension.x, 8 * 2);
                var threadY = RenderingUtilsExt.DivRoundUp((int)data.Dimension.y, 8 * 2);
                cmd.DispatchCompute(data.DeinterleaveDepthShader, data.DeinterleaveDepthKernel, threadX, threadY, 1);
            }

            using (new ProfilingScope(cmd, ProfilingSampler.Get(ProfileID.DeinterleaveNormal)))
            {
                cmd.SetComputeTextureParam(data.DeinterleaveNormalShader, data.DeinterleaveNormalKernel, "NormalInput", data.normalTexture);
                cmd.SetComputeTextureParam(data.DeinterleaveNormalShader, data.DeinterleaveNormalKernel, "DeinterleaveNormalOutput",
                    data.deinterleaveNormalTexture);

                var threadX = RenderingUtilsExt.DivRoundUp((int)data.Dimension.x, 8 * 2);
                var threadY = RenderingUtilsExt.DivRoundUp((int)data.Dimension.y, 8 * 2);
                cmd.DispatchCompute(data.DeinterleaveNormalShader, data.DeinterleaveNormalKernel, threadX, threadY, 1);
            }

            using (new ProfilingScope(cmd, ProfilingSampler.Get(ProfileID.SSAOCalc)))
            {
                cmd.SetComputeTextureParam(data.HBAOCalcShader, data.HBAOCalcKernelID, "LinearDepthInput", data.deinterleaveDepthTexture);
                cmd.SetComputeTextureParam(data.HBAOCalcShader, data.HBAOCalcKernelID, "NormalViewInput", data.deinterleaveNormalTexture);
                cmd.SetComputeTextureParam(data.HBAOCalcShader, data.HBAOCalcKernelID, "AOOutput", data.deinterleaveSSAOTexture);


                cmd.SetComputeFloatParam(data.HBAOCalcShader, "RadiusToScreen", RadiusToScreen);
                cmd.SetComputeFloatParam(data.HBAOCalcShader, "R2", R2);
                cmd.SetComputeFloatParam(data.HBAOCalcShader, "NegInvR2", NegInvR2);
                cmd.SetComputeFloatParam(data.HBAOCalcShader, "NDotVBias", NDotVBias);
                cmd.SetComputeFloatParam(data.HBAOCalcShader, "AOMultiplier", AOMultiplier);
                cmd.SetComputeFloatParam(data.HBAOCalcShader, "PowExponent", PowExponent);

                cmd.SetComputeVectorParam(data.HBAOCalcShader, "InvFullResolution", InvFullResolution.xyxx);
                cmd.SetComputeVectorParam(data.HBAOCalcShader, "InvHalfResolution", InvHalfResolution.xyxx);
                cmd.SetComputeVectorParam(data.HBAOCalcShader, "_SSAO_UVToView", uvToView);

                cmd.SetComputeVectorArrayParam(data.HBAOCalcShader, "jitters", GetJitter());

                var threadX = RenderingUtilsExt.DivRoundUp((int)HalfResolution.x, 8);
                var threadY = RenderingUtilsExt.DivRoundUp((int)HalfResolution.y, 8);
                cmd.DispatchCompute(data.HBAOCalcShader, data.HBAOCalcKernelID, threadX, threadY, 4);
            }

            using (new ProfilingScope(cmd, ProfilingSampler.Get(ProfileID.Reinterleave)))
            {
                cmd.SetComputeTextureParam(data.HBAOReinterleaveShader, data.HBAOReinterleaveKernelID, "DeinterleaveAOInput", data.deinterleaveSSAOTexture);
                cmd.SetComputeTextureParam(data.HBAOReinterleaveShader, data.HBAOReinterleaveKernelID, "ReinterleaveOutput", data.reinterleaveSSAOTexture);

                var threadX = RenderingUtilsExt.DivRoundUp((int)HalfResolution.x, 8);
                var threadY = RenderingUtilsExt.DivRoundUp((int)HalfResolution.y, 8);

                cmd.DispatchCompute(data.HBAOReinterleaveShader, data.HBAOReinterleaveKernelID, threadX, threadY, 1);
            }

            using (new ProfilingScope(cmd, ProfilingSampler.Get(ProfileID.SSAOBlur)))
            {
                cmd.SetComputeTextureParam(data.HBAOBlurShader, data.HBAOBlurKernelID, "AOInput", data.reinterleaveSSAOTexture);
                cmd.SetComputeTextureParam(data.HBAOBlurShader, data.HBAOBlurKernelID, "AOBlurOutput", data.SSAOPing);

                CoreUtils.SetKeyword(cmd, "PRESENT", false);

                cmd.SetComputeFloatParam(data.HBAOBlurShader, "Sharpness", data.setting.sharpness.value * 100);
                cmd.SetComputeVectorParam(data.HBAOBlurShader, "InvResolutionDirection", new Vector4(1.0f / data.Dimension.x, 0, 0, 0));

                var threadX = RenderingUtilsExt.DivRoundUp((int)data.Dimension.x, 8);
                var threadY = RenderingUtilsExt.DivRoundUp((int)data.Dimension.y, 8);
                cmd.DispatchCompute(data.HBAOBlurShader, data.HBAOBlurKernelID, threadX, threadY, 1);


                CoreUtils.SetKeyword(cmd, "PRESENT", true);
                cmd.SetComputeVectorParam(data.HBAOBlurShader, "InvResolutionDirection", new Vector4(0, 1.0f / data.Dimension.y, 0, 0));

                cmd.SetComputeTextureParam(data.HBAOBlurShader, data.HBAOBlurKernelID, "AOInput", data.SSAOPing);
                cmd.SetComputeTextureParam(data.HBAOBlurShader, data.HBAOBlurKernelID, "AOBlurOutput", data.SSAOPong);


                cmd.DispatchCompute(data.HBAOBlurShader, data.HBAOBlurKernelID, threadX, threadY, 1);
            }

            cmd.SetGlobalVector("_AmbientOcclusionParam",
                new Vector4(1f, 0f, 0f, data.setting.directLightingStrength.value));
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.ScreenSpaceOcclusion, true);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddComputePass<PassData>("HBAO", out var data))
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();

                var depthTexture = resourceData.cameraDepthTexture;
                var normalTexture = resourceData.cameraNormalsTexture;

                var setting = VolumeManager.instance.stack.GetComponent<AmbientOcclusion>();
                if (!setting.IsActive() || setting.ambientOcclusionModeParameter.value is not AmbientOcclusionMode.HorizonBasedAmbientOcclusion)
                {
                    return;
                }



                if (!DeinterleaveDepthShader)
                {
                    var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<AmbientOcclusionRuntimeShader>();
                    DeinterleaveDepthShader = runtimeShader.HBAODeinterleaveDepth;
                    DeinterleaveDepthKernel = DeinterleaveDepthShader.FindKernel("KMain");

                    DeinterleaveNormalShader = runtimeShader.HBAODeinterleaveNormal;
                    DeinterleaveNormalKernel = DeinterleaveNormalShader.FindKernel("KMain");

                    DeinterleaveAOShader = runtimeShader.HBAOCalc;
                    DeinterleaveAOKernel = DeinterleaveAOShader.FindKernel("KMain");

                    ReinterleaveAOShader = runtimeShader.HBAOReinterleave;
                    ReinterleaveAOKernel = ReinterleaveAOShader.FindKernel("KMain");

                    BlurAOShader = runtimeShader.HBAOBlur;
                    BlurAOKernel = BlurAOShader.FindKernel("KMain");
                }


                var deinterleaveDepthTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.actualWidth / 2, cameraData.actualHeight / 2)
                {
                    enableRandomWrite = true,
                    dimension = TextureDimension.Tex2DArray,
                    depthBufferBits = 0,
                    slices = 4,
                    colorFormat = GraphicsFormat.R32_SFloat,
                    name = "deinterleaveDepthTexture"
                });

                var deinterleaveNormalTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.actualWidth / 2, cameraData.actualHeight / 2)
                {
                    enableRandomWrite = true,
                    dimension = TextureDimension.Tex2DArray,
                    depthBufferBits = 0,
                    slices = 4,
                    colorFormat = GraphicsFormat.R8G8B8A8_SNorm,
                    name = "deinterleaveNormalTexture"
                });

                var deinterleaveSSAOTexture = renderGraph.CreateTexture(
                    new TextureDesc(new TextureDesc(cameraData.actualWidth / 2, cameraData.actualHeight / 2))
                    {
                        enableRandomWrite = true,
                        dimension = TextureDimension.Tex2DArray,
                        slices = 4,
                        colorFormat = GraphicsFormat.R16G16_SFloat,
                        name = "deinterleaveSSAOTexture",
                        depthBufferBits = 0,
                    });

                var reinterleaveSSAOTexture = renderGraph.CreateTexture(new TextureDesc(new TextureDesc(cameraData.actualWidth, cameraData.actualHeight))
                {
                    enableRandomWrite = true,
                    colorFormat = GraphicsFormat.R16G16_SFloat,
                    depthBufferBits = 0,
                    name = "reinterleaveSSAOTexture"
                });
                var SSAOPing = renderGraph.CreateTexture(new TextureDesc(new TextureDesc(cameraData.actualWidth, cameraData.actualHeight))
                {
                    enableRandomWrite = true,
                    colorFormat = GraphicsFormat.R16G16_SFloat,
                    depthBufferBits = 0,
                    name = "SSAOPing"
                });
                var SSAOPong = renderGraph.CreateTexture(new TextureDesc(new TextureDesc(cameraData.actualWidth, cameraData.actualHeight))
                {
                    enableRandomWrite = true,
                    colorFormat = GraphicsFormat.R16_SFloat,
                    depthBufferBits = 0,
                    name = "SSAOPong"
                });


                data.setting = setting;

                data.Dimension = new float2(cameraData.actualWidth, cameraData.actualHeight);

                data.cameraData = cameraData;

                data.depthTexture = depthTexture;
                data.normalTexture = normalTexture;
                data.deinterleaveDepthTexture = deinterleaveDepthTexture;
                data.deinterleaveNormalTexture = deinterleaveNormalTexture;
                data.deinterleaveSSAOTexture = deinterleaveSSAOTexture;
                data.reinterleaveSSAOTexture = reinterleaveSSAOTexture;
                data.SSAOPing = SSAOPing;
                data.SSAOPong = SSAOPong;


                data.DeinterleaveDepthShader = DeinterleaveDepthShader;
                data.DeinterleaveDepthKernel = DeinterleaveDepthKernel;

                data.DeinterleaveNormalShader = DeinterleaveNormalShader;
                data.DeinterleaveNormalKernel = DeinterleaveNormalKernel;

                data.HBAOCalcShader = DeinterleaveAOShader;
                data.HBAOCalcKernelID = DeinterleaveAOKernel;


                data.HBAOReinterleaveShader = ReinterleaveAOShader;
                data.HBAOReinterleaveKernelID = ReinterleaveAOKernel;


                data.HBAOBlurShader = BlurAOShader;
                data.HBAOBlurKernelID = BlurAOKernel;

                builder.AllowGlobalStateModification(true);

                builder.UseTexture(data.depthTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.normalTexture);
                builder.UseTexture(data.deinterleaveDepthTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.deinterleaveNormalTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.deinterleaveSSAOTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.reinterleaveSSAOTexture, AccessFlags.ReadWrite);
                builder.UseTexture(data.SSAOPing, AccessFlags.ReadWrite);
                builder.UseTexture(data.SSAOPong, AccessFlags.ReadWrite);
                builder.SetRenderFunc((PassData passdata, ComputeGraphContext context) => ExecutePass(passdata, context));
                resourceData.ssaoTexture = SSAOPong;
            }
        }

        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.ScreenSpaceOcclusion, false);
        }
    }
}