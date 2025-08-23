using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class CMAA2Pass : ScriptableRenderPass
    {
        private ComputeShader cmaa2Shader;
        private int EdgesColorKernelID;
        private int ComputeDispatchArgsCSKernelID;
        private int ProcessCandidatesCSKernelID;
        private int DeferredColorApply2x2CSKernelID;



        bool firstRun = true;



        internal class PassData
        {
            internal ComputeShader cmaa2Shader;
            internal int EdgesColorKernelID;
            internal int ComputeDispatchArgsCSKernelID;
            internal int ProcessCandidatesCSKernelID;
            internal int DeferredColorApply2x2CSKernelID;



            internal TextureHandle InputTexture;
            internal TextureHandle OutputTexture;

            internal TextureHandle WorkingEdges;
            internal TextureHandle WorkingDeferredBlendItemListHeads;

            internal BufferHandle WorkingShapeCandidatesBuffer;
            internal BufferHandle WorkingDeferredBlendItemListBuffer;
            internal BufferHandle WorkingDeferredBlendLocationListBuffer;
            internal BufferHandle WorkingControlBuffer;
            internal BufferHandle WorkingExecuteIndirectBuffer;

            internal bool firstRun;

            internal int2 resolution;
            // internal int2 textureResolution;
        }


        static void ExecutePass(PassData data, ComputeGraphContext ctx)
        {
            var cmd = ctx.cmd;
            

            if (data.firstRun)
            {
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, "g_workingShapeCandidates", data.WorkingShapeCandidatesBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, "g_workingControlBuffer", data.WorkingControlBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, "g_workingDeferredBlendLocationList",
                    data.WorkingDeferredBlendLocationListBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, "g_workingExecuteIndirectBuffer",
                    data.WorkingExecuteIndirectBuffer);

                cmd.DispatchCompute(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, 2, 1, 1);
            }

            // first pass edge detect
            {
                int csOutputKernelSizeX = 16 - 2;
                int csOutputKernelSizeY = 16 - 2;
                int threadGroupCountX = (data.resolution.x + csOutputKernelSizeX * 2 - 1) / (csOutputKernelSizeX * 2);
                int threadGroupCountY = (data.resolution.y + csOutputKernelSizeY * 2 - 1) / (csOutputKernelSizeY * 2);

                cmd.SetComputeTextureParam(data.cmaa2Shader, data.EdgesColorKernelID, "g_inoutColorReadonly", data.InputTexture);
                cmd.SetComputeTextureParam(data.cmaa2Shader, data.EdgesColorKernelID, "g_workingEdges", data.WorkingEdges);
                cmd.SetComputeTextureParam(data.cmaa2Shader, data.EdgesColorKernelID, "g_workingDeferredBlendItemListHeads",
                    data.WorkingDeferredBlendItemListHeads);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.EdgesColorKernelID, "g_workingControlBuffer", data.WorkingControlBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.EdgesColorKernelID, "g_workingShapeCandidates", data.WorkingShapeCandidatesBuffer);

                cmd.DispatchCompute(data.cmaa2Shader, data.EdgesColorKernelID, threadGroupCountX, threadGroupCountY, 1);
            }

            // Set up for the first DispatchIndirect

            {
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, "g_workingShapeCandidates", data.WorkingShapeCandidatesBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, "g_workingControlBuffer", data.WorkingControlBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, "g_workingDeferredBlendLocationList",
                    data.WorkingDeferredBlendLocationListBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, "g_workingExecuteIndirectBuffer",
                    data.WorkingExecuteIndirectBuffer);

                cmd.DispatchCompute(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, 2, 1, 1);
            }

            // Process shape candidates DispatchIndirect
            {
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ProcessCandidatesCSKernelID, "g_workingShapeCandidates", data.WorkingShapeCandidatesBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ProcessCandidatesCSKernelID, "g_workingControlBuffer", data.WorkingControlBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ProcessCandidatesCSKernelID, "g_workingDeferredBlendLocationList",
                    data.WorkingDeferredBlendLocationListBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ProcessCandidatesCSKernelID, "g_workingDeferredBlendItemList",
                    data.WorkingDeferredBlendItemListBuffer);


                cmd.SetComputeTextureParam(data.cmaa2Shader, data.ProcessCandidatesCSKernelID, "g_workingDeferredBlendItemListHeads",
                    data.WorkingDeferredBlendItemListHeads);
                cmd.SetComputeTextureParam(data.cmaa2Shader, data.ProcessCandidatesCSKernelID, "g_workingEdges", data.WorkingEdges);
                cmd.SetComputeTextureParam(data.cmaa2Shader, data.ProcessCandidatesCSKernelID, "g_inoutColorReadonly", data.InputTexture);
                cmd.DispatchCompute(data.cmaa2Shader, data.ProcessCandidatesCSKernelID, data.WorkingExecuteIndirectBuffer, 0);
            }


            //Set up for the second DispatchIndirect            
            {
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, "g_workingShapeCandidates", data.WorkingShapeCandidatesBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, "g_workingControlBuffer", data.WorkingControlBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, "g_workingDeferredBlendLocationList",
                    data.WorkingDeferredBlendLocationListBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, "g_workingExecuteIndirectBuffer",
                    data.WorkingExecuteIndirectBuffer);

                cmd.DispatchCompute(data.cmaa2Shader, data.ComputeDispatchArgsCSKernelID, 1, 2, 1);
            }

            // Writing the final outputs using the UAV; in case of MSAA path, the D3D12_RESOURCE_STATE_UNORDERED_ACCESS is already set so only do it in the non-MSAA case.
            {
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.DeferredColorApply2x2CSKernelID, "g_workingShapeCandidates",
                    data.WorkingShapeCandidatesBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.DeferredColorApply2x2CSKernelID, "g_workingControlBuffer", data.WorkingControlBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.DeferredColorApply2x2CSKernelID, "g_workingDeferredBlendLocationList",
                    data.WorkingDeferredBlendLocationListBuffer);
                cmd.SetComputeBufferParam(data.cmaa2Shader, data.DeferredColorApply2x2CSKernelID, "g_workingDeferredBlendItemList",
                    data.WorkingDeferredBlendItemListBuffer);


                cmd.SetComputeTextureParam(data.cmaa2Shader, data.DeferredColorApply2x2CSKernelID, "g_workingDeferredBlendItemListHeads",
                    data.WorkingDeferredBlendItemListHeads);
                cmd.SetComputeTextureParam(data.cmaa2Shader, data.DeferredColorApply2x2CSKernelID, "g_workingEdges", data.WorkingEdges);

                // cmd.SetComputeTextureParam(data.cmaa2Shader, data.DeferredColorApply2x2CSKernelID, "g_inoutColorReadonly", data.InputTexture);
                cmd.SetComputeTextureParam(data.cmaa2Shader, data.DeferredColorApply2x2CSKernelID, "g_inoutColorWriteonly", data.OutputTexture);

                cmd.DispatchCompute(data.cmaa2Shader, data.DeferredColorApply2x2CSKernelID, data.WorkingExecuteIndirectBuffer, 0);
            }
        }

        //in VividRP, ColorAttachment default set enableRandomWrite
        public TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData, in TextureHandle source)
        {
            var cameraData = frameData.Get<UniversalCameraData>();

            var setting = VolumeManager.instance.stack.GetComponent<CMAA2>();
            if (!setting.enabled.value)
            {
                return source;
            }

            using (var builder = renderGraph.AddComputePass<PassData>("CMAA2", out var passData))
            {
                passData.InputTexture = source;

                var desc = renderGraph.GetTextureDesc(passData.InputTexture);

                var textureSampleCount = (int)desc.msaaSamples;

                passData.resolution = new int2(cameraData.scaledWidth, cameraData.scaledHeight);

                if (!cmaa2Shader)
                {
                    var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<CMAA2RuntimeShader>();
                    cmaa2Shader = runtimeShader.cmaa2Shader;
                    EdgesColorKernelID = cmaa2Shader.FindKernel("EdgesColor2x2CS");
                    ComputeDispatchArgsCSKernelID = cmaa2Shader.FindKernel("ComputeDispatchArgsCS");
                    ProcessCandidatesCSKernelID = cmaa2Shader.FindKernel("ProcessCandidatesCS");
                    DeferredColorApply2x2CSKernelID = cmaa2Shader.FindKernel("DeferredColorApply2x2CS");

                    passData.firstRun = true;
                }
                else
                {
                    passData.firstRun = false;
                }


                if (firstRun)
                {
                    firstRun = false;
                }


                var requiredCandidatePixels = cameraData.pixelWidth * cameraData.pixelHeight / 4 * textureSampleCount;
                int requiredDeferredColorApplyBuffer = cameraData.pixelWidth * cameraData.pixelHeight / 2 * textureSampleCount;
                int requiredListHeadsPixels = (cameraData.pixelWidth * cameraData.pixelHeight + 3) / 6;

                passData.cmaa2Shader = cmaa2Shader;
                passData.EdgesColorKernelID = EdgesColorKernelID;
                passData.ComputeDispatchArgsCSKernelID = ComputeDispatchArgsCSKernelID;
                passData.ProcessCandidatesCSKernelID = ProcessCandidatesCSKernelID;
                passData.DeferredColorApply2x2CSKernelID = DeferredColorApply2x2CSKernelID;

                
                passData.WorkingShapeCandidatesBuffer =
                    builder.CreateTransientBuffer(new BufferDesc(requiredCandidatePixels, sizeof(int), GraphicsBuffer.Target.Structured));
                passData.WorkingDeferredBlendItemListBuffer =
                    builder.CreateTransientBuffer(new BufferDesc(requiredDeferredColorApplyBuffer, sizeof(int) * 2, GraphicsBuffer.Target.Structured));
                passData.WorkingDeferredBlendLocationListBuffer = builder.CreateTransientBuffer(new BufferDesc(requiredListHeadsPixels, sizeof(int)
                    , GraphicsBuffer.Target.Structured));
                passData.WorkingControlBuffer = builder.CreateTransientBuffer(new BufferDesc(16, sizeof(int), GraphicsBuffer.Target.Raw));
                passData.WorkingExecuteIndirectBuffer = builder.CreateTransientBuffer(new BufferDesc(4, sizeof(int), GraphicsBuffer.Target.IndirectArguments));

                passData.WorkingEdges = builder.CreateTransientTexture(new TextureDesc(new Vector2(0.5f, 1))
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R8_UInt,
                });
                passData.WorkingDeferredBlendItemListHeads = builder.CreateTransientTexture(new TextureDesc(Vector2.one * 0.5f)
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R32_UInt,
                });
                desc.enableRandomWrite = true;
                passData.OutputTexture = source;


                builder.UseTexture(passData.InputTexture);
                builder.UseTexture(passData.WorkingEdges, AccessFlags.ReadWrite);
                builder.UseTexture(passData.WorkingDeferredBlendItemListHeads, AccessFlags.ReadWrite);
                builder.UseTexture(passData.OutputTexture, AccessFlags.ReadWrite);

                builder.UseBuffer(passData.WorkingShapeCandidatesBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.WorkingDeferredBlendItemListBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.WorkingDeferredBlendLocationListBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.WorkingControlBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.WorkingExecuteIndirectBuffer, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData data, ComputeGraphContext ctx) => ExecutePass(data, ctx));
                return passData.OutputTexture;
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();

            using (var builder = renderGraph.AddComputePass<PassData>("CMAA2", out var passData))
            {
                passData.InputTexture = resourceData.activeColorTexture;

                var desc = renderGraph.GetTextureDesc(passData.InputTexture);

                var textureSampleCount = (int)desc.msaaSamples;

                passData.firstRun = firstRun;
                passData.resolution = new int2(cameraData.pixelWidth, cameraData.pixelHeight);


                if (firstRun)
                {
                    firstRun = false;
                }


                var requiredCandidatePixels = cameraData.pixelWidth * cameraData.pixelHeight / 4 * textureSampleCount;
                int requiredDeferredColorApplyBuffer = cameraData.pixelWidth * cameraData.pixelHeight / 2 * textureSampleCount;
                int requiredListHeadsPixels = (cameraData.pixelWidth * cameraData.pixelHeight + 3) / 6;

                passData.cmaa2Shader = cmaa2Shader;
                passData.EdgesColorKernelID = EdgesColorKernelID;
                passData.ComputeDispatchArgsCSKernelID = ComputeDispatchArgsCSKernelID;
                passData.ProcessCandidatesCSKernelID = ProcessCandidatesCSKernelID;
                passData.DeferredColorApply2x2CSKernelID = DeferredColorApply2x2CSKernelID;



                passData.WorkingShapeCandidatesBuffer =
                    builder.CreateTransientBuffer(new BufferDesc(requiredCandidatePixels, sizeof(int), GraphicsBuffer.Target.Structured));
                passData.WorkingDeferredBlendItemListBuffer =
                    builder.CreateTransientBuffer(new BufferDesc(requiredDeferredColorApplyBuffer, sizeof(int) * 2, GraphicsBuffer.Target.Structured));
                passData.WorkingDeferredBlendLocationListBuffer = builder.CreateTransientBuffer(new BufferDesc(requiredListHeadsPixels, sizeof(int)
                    , GraphicsBuffer.Target.Structured));
                passData.WorkingControlBuffer = builder.CreateTransientBuffer(new BufferDesc(16, sizeof(int), GraphicsBuffer.Target.Raw));
                passData.WorkingExecuteIndirectBuffer = builder.CreateTransientBuffer(new BufferDesc(4, sizeof(int), GraphicsBuffer.Target.IndirectArguments));

                passData.WorkingEdges = builder.CreateTransientTexture(new TextureDesc(new Vector2(0.5f, 1))
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R8_UInt,
                });
                passData.WorkingDeferredBlendItemListHeads = builder.CreateTransientTexture(new TextureDesc(Vector2.one * 0.5f)
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R32_UInt,
                });
                desc.enableRandomWrite = true;
                passData.OutputTexture = renderGraph.CreateTexture(desc);


                builder.UseTexture(passData.InputTexture);
                builder.UseTexture(passData.WorkingEdges, AccessFlags.ReadWrite);
                builder.UseTexture(passData.WorkingDeferredBlendItemListHeads, AccessFlags.ReadWrite);
                builder.UseTexture(passData.OutputTexture, AccessFlags.ReadWrite);

                builder.UseBuffer(passData.WorkingShapeCandidatesBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.WorkingDeferredBlendItemListBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.WorkingDeferredBlendLocationListBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.WorkingControlBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.WorkingExecuteIndirectBuffer, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData data, ComputeGraphContext ctx) => ExecutePass(data, ctx));
                resourceData.cameraColor = passData.OutputTexture;
            }
        }
    }
}