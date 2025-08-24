using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UniversalCameraData = UnityEngine.Rendering.Universal.UniversalCameraData;

namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature]
    public class GSR2Feature : ScriptableRendererFeature
    {

        internal class GSR2Pass : ScriptableRenderPass
        {
            private Vector2[] HaltonSequence = new Vector2[32];
            private Material material;
            private int jitterIndex = 0;
            private RTHandle motionDepthClipRT;
            private RTHandle[] outputRT;
            private int frameCount = 0;


            private float Halton(int index, int baseN)
            {
                float result = 0f;
                float invBase = 1f / baseN;
                float fraction = invBase;

                while (index > 0)
                {
                    result += (index % baseN) * fraction;
                    index /= baseN;
                    fraction *= invBase;
                }

                return result;
            }

            public GSR2Pass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                for (int i = 0; i < HaltonSequence.Length; i++) // 使用32帧序列
                {
                    HaltonSequence[i] = new Vector2(
                        Halton(i + 1, 2) - 0.5f,
                        Halton(i + 1, 3) - 0.5f
                    );
                }

                outputRT = new RTHandle[2];
                material = new Material(Shader.Find("PostProcessing/GSR2"));
                requiresIntermediateTexture = true;
            }

            private Vector2 GetJitter()
            {
                return HaltonSequence[jitterIndex % HaltonSequence.Length];
            }


            class PassData
            {
                public Material material;

                public TextureHandle cameraTexture;
                public TextureHandle historyTexture;
                public TextureHandle outputTexture;
                public TextureHandle motionDepthClipTexture;
            }


            void Setup(ref UniversalCameraData cameraData,GSR2 setting)
            {
                ConfigureInput(ScriptableRenderPassInput.Depth|ScriptableRenderPassInput.Motion);

                var upscaledRatio = setting.upscaledRatio.value;
                var minLerpContribution = setting.minLerpContribution.value;
                
                RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
                descriptor.msaaSamples = 1;
                descriptor.depthBufferBits = 0;
                Vector2 renderSize = new Vector2(descriptor.width, descriptor.height);

                var outputSize = new Vector2(descriptor.width, descriptor.height);
                descriptor.width = Mathf.RoundToInt(descriptor.width / upscaledRatio);
                descriptor.height = Mathf.RoundToInt(descriptor.height / upscaledRatio);
                RenderingUtils.ReAllocateHandleIfNeeded(ref outputRT[0], descriptor, name: "outputRT_0");
                RenderingUtils.ReAllocateHandleIfNeeded(ref outputRT[1], descriptor, name: "outputRT_1");

                descriptor.graphicsFormat = GraphicsFormat.R8_UNorm;
                RenderingUtils.ReAllocateHandleIfNeeded(ref motionDepthClipRT, descriptor, name: "motionDepthClipRT");


                var camera = cameraData.camera;
                if (!camera.orthographic)
                {
                    camera.ResetProjectionMatrix();
                    var nextJitter = GetJitter();
                    var jitProj = camera.projectionMatrix;
                    camera.nonJitteredProjectionMatrix = jitProj;
                    jitProj.m02 += nextJitter.x / camera.pixelWidth;
                    jitProj.m12 += nextJitter.y / camera.pixelHeight;
                    camera.projectionMatrix = jitProj;
                }

                Vector4 jitter = GetJitter();
                material.SetVector("_RenderSize", renderSize);
                material.SetVector("_RenderSizeRcp", new Vector2(1f / renderSize.x, 1f / renderSize.y));
                material.SetVector("_OutputSize", outputSize);
                material.SetVector("_OutputSizeRcp", new Vector2(1f / Screen.width, 1f / Screen.height));
                material.SetVector("_JitterOffset", jitter);
                material.SetVector("_ScaleRatio",
                    new Vector2(upscaledRatio,
                        Mathf.Min(20f,
                            Mathf.Pow((outputSize.x * outputSize.y) / (renderSize.x * renderSize.y), 3f))));
                material.SetFloat("_CameraFovAngleHor",
                    Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * renderSize.x / renderSize.y);
                material.SetFloat("_MinLerpContribution", minLerpContribution);

                material.SetFloat("_Reset", frameCount == 0 ? 1f : 0f);
            }


            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                
                var setting = VolumeManager.instance.stack.GetComponent<GSR2>();
                if (!setting || !setting.IsActive())
                {
                    return;
                }

                Setup(ref cameraData,setting);
                var resourceData = frameData.Get<UniversalResourceData>();


                var output = outputRT[frameCount % 2];
                var history = outputRT[(frameCount + 1) % 2];
                var motionDepthClip = renderGraph.ImportTexture(motionDepthClipRT);
                var outputHandle = renderGraph.ImportTexture(output);
                var historyHandle = renderGraph.ImportTexture(history);
                using (var builder = renderGraph.AddUnsafePass<PassData>("GSR2", out var passData))
                {
                    passData.motionDepthClipTexture = motionDepthClip;
                    passData.outputTexture = outputHandle;
                    passData.historyTexture = historyHandle;
                    passData.cameraTexture = resourceData.activeColorTexture;
                    passData.material = material;

                    builder.UseTexture(passData.motionDepthClipTexture, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.outputTexture, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.historyTexture, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.cameraTexture, AccessFlags.ReadWrite);

                    builder.SetRenderFunc((PassData data, UnsafeGraphContext rgContext) =>
                    {
                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(rgContext.cmd);
                        var material = data.material;

                        Blitter.BlitCameraTexture(cmd, data.cameraTexture,
                            data.motionDepthClipTexture,
                            material, 0);

                        material.SetTexture("_PrevHistory", history);
                        material.SetTexture("MotionDepthClipAlphaBuffer", data.motionDepthClipTexture);

                        Blitter.BlitCameraTexture(cmd, data.cameraTexture, data.outputTexture,
                            material, 1);
                        // Blitter.BlitCameraTexture(cmd, data.outputTexture, data.cameraTexture);
                    });
                }

                resourceData.cameraColor = outputHandle;
                frameCount++;
                jitterIndex++;
            }

        }

        GSR2Pass mGsr2Pass;


        public override void Create()
        {
            mGsr2Pass = new GSR2Pass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(mGsr2Pass);
        }
    }
}