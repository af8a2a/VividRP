using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class SinglePassBlurPass : ScriptableRenderPass
    {
        private ComputeShader _computeShader;
        private int _ffxBlurTileSizeX = 8;
        private int _ffxBlurTileSizeY = 8;
        private int kernelID;

        public void Setup(ComputeShader computeShader)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            _computeShader = computeShader;
            kernelID = computeShader.FindKernel("SinglePassBlur");
        }

        internal class PassData
        {
            internal ComputeShader ComputeShader;
            internal int KernelID;
            internal Vector2 ImageSize;
            internal Vector2 BlurTileSize;
            internal bool Fp16Packed = false;
            internal bool WaveFrontOperation = false;
            internal TextureHandle InputTexture;
            internal TextureHandle OutputTexture;
        }


        static void ExecutePass(PassData passData, ComputeGraphContext computeGraphContext)
        {
            var cmd = computeGraphContext.cmd;


            cmd.SetKeyword(passData.ComputeShader, new LocalKeyword(passData.ComputeShader, "FFX_WAVE"),
                passData.WaveFrontOperation);
            cmd.SetKeyword(passData.ComputeShader, new LocalKeyword(passData.ComputeShader, "FFX_HALF"),
                passData.Fp16Packed);

            cmd.SetComputeVectorParam(passData.ComputeShader, "imageSize", passData.ImageSize);

            cmd.SetComputeTextureParam(passData.ComputeShader, passData.KernelID, "r_input_src", passData.InputTexture);
            cmd.SetComputeTextureParam(passData.ComputeShader, passData.KernelID, "rw_output", passData.OutputTexture);
            cmd.DispatchCompute(passData.ComputeShader, passData.KernelID,
                (int)(passData.ImageSize.x / passData.BlurTileSize.x), (int)(passData.ImageSize.y /
                                                                             passData.BlurTileSize.y),
                1);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();

            var desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
            desc.enableRandomWrite = true;
            desc.name = "SinglePassBlur";
            var output = renderGraph.CreateTexture(desc);

            var setting = VolumeManager.instance.stack.GetComponent<SinglePassBlurSetting>();
            if (setting == null || !setting.IsActive())
            {
                return;
            }
            using (var builder = renderGraph.AddComputePass<PassData>("SinglePassBlurPass", out var passData))
            {
                builder.AllowGlobalStateModification(true);
                passData.InputTexture = resourceData.activeColorTexture;
                passData.OutputTexture = output;
                passData.BlurTileSize = new Vector2(_ffxBlurTileSizeX, _ffxBlurTileSizeY);
                passData.ImageSize = new Vector2(desc.width, desc.height);
                passData.ComputeShader = _computeShader;
                passData.KernelID = kernelID;
                if (setting != null)
                {
                    passData.Fp16Packed = setting.fp16Packed.value;
                    passData.WaveFrontOperation = setting.wavefrontOperation.value;
                }

                builder.UseTexture(passData.InputTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.OutputTexture, AccessFlags.ReadWrite);

                builder.SetRenderFunc((PassData data, ComputeGraphContext cgContext) => ExecutePass(data, cgContext));
            }
        }
    }
}