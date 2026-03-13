using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    /// <summary>
    /// Example pass demonstrating how to import external RTHandles in Prepare()
    /// and use them as TextureHandle member variables in Record().
    /// </summary>
    public class ImportTextureExamplePass : RasterPass
    {
        // Declare TextureHandle member variables (not [RenderGraphResource])
        // These will be assigned in Prepare() via Import()
        private TextureHandle m_ImportedColorTexture;
        private TextureHandle m_ImportedDepthTexture;

        // Output texture (still uses [RenderGraphResource] for graph-managed resources)
        [RenderGraphResource(Access = AccessFlags.Write, AttachmentIndex = 0)]
        public RenderGraphTexture outputTexture;

        private Material m_Material;

        public override void Create()
        {
            // Load shader and create material
            var shader = Shader.Find("Hidden/VividRP/Blit");
            if (shader != null)
                m_Material = new Material(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();

            // Update output texture dimensions
            outputTexture.desc.Width = cameraData.actualWidth;
            outputTexture.desc.Height = cameraData.actualHeight;

            // Example: Import external RTHandles
            // In a real scenario, you would get these from somewhere else
            // (e.g., from a previous render pass, camera target, or external system)

            // Example 1: Import camera color target (if available)
            var camera = cameraData.camera;
            if (camera.targetTexture != null)
            {
                // Create RTHandle from RenderTexture (in real code, you'd get this from elsewhere)
                // var externalColorRT = RTHandles.Alloc(camera.targetTexture);
                // m_ImportedColorTexture = Import(externalColorRT);
            }

            // Example 2: Import a depth texture
            // var externalDepthRT = GetDepthTextureFromSomewhere();
            // m_ImportedDepthTexture = Import(externalDepthRT);

            // Note: The Import() method is provided by the RasterPass base class
            // It returns a TextureHandle that can be used directly in Record()
        }

        public override void Record(RasterGraphContext context)
        {
            // Now you can use the imported TextureHandle member variables directly
            // They are valid TextureHandles that can be passed to RenderGraph APIs

            if (m_ImportedColorTexture.IsValid())
            {
                // Use the imported texture
                // context.cmd.Blit(m_ImportedColorTexture, outputTexture);
            }

            if (m_ImportedDepthTexture.IsValid())
            {
                // Use the imported depth texture
                // context.cmd.SetGlobalTexture("_ImportedDepth", m_ImportedDepthTexture);
            }

            // Example: Simple clear operation
            context.cmd.ClearRenderTarget(true, true, Color.black);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                Object.DestroyImmediate(m_Material);
                m_Material = null;
            }
        }
    }
}
