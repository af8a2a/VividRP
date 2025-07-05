using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    //reference from https://github.com/jiaozi158/UnitySSGIURP
    public class SceneViewMotionVectorPass : ScriptableRenderPass
    {
        /// Motion vectors may not render correctly in the scene view
        /// This pass is used to "fix" camera motion vectors to improve scene view denoising
        private const string m_ProfilerTag = "Prepare Screen Space Global Illumination";

        private readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);


        private Matrix4x4 camVPMatrix;
        private Matrix4x4 prevCamVPMatrix;

        // This pass is editor only
        const string _PrevViewProjMatrix = "_PrevViewProjMatrix";
        const string _NonJitteredViewProjMatrix = "_NonJitteredViewProjMatrix";
        const string motionColorHandleName = "m_Color";
        const string motionDepthHandleName = "m_Depth";

        public SceneViewMotionVectorPass()
        {
        }


        // This class stores the data needed by the pass, passed as parameter to the delegate function that executes the pass
        private class PassData
        {
            internal Matrix4x4 prevCamVPMatrix;
            internal Matrix4x4 camVPMatrix;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            // Fix scene view motion vectors
            cmd.SetGlobalMatrix(_PrevViewProjMatrix, data.prevCamVPMatrix);
            cmd.SetGlobalMatrix(_NonJitteredViewProjMatrix, data.camVPMatrix);
        }

        // This is where the renderGraph handle can be accessed.
        // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // add an unsafe render pass to the render graph, specifying the name and the data type that will be passed to the ExecutePass function
            using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData))
            {
                
                // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
                // The active color and depth textures are the main color and depth buffers that the camera renders into
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                var camera = cameraData.camera;
                if (!cameraData.isSceneViewCamera)
                {
                    return;
                }

                camVPMatrix = GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, true) * cameraData.GetViewMatrix();
                passData.camVPMatrix = camVPMatrix;
                passData.prevCamVPMatrix = prevCamVPMatrix == null ? camera.previousViewProjectionMatrix : prevCamVPMatrix;
                prevCamVPMatrix = camVPMatrix;

                // This pass is editor only
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }

        public void Dispose()
        {
        }
    }
}