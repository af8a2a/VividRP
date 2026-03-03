using System.Reflection;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class PassResource
    {
        public RenderGraphTextureDesc[] ReadTextureDesc;
        public RenderGraphTextureDesc[] WriteTextureDesc;
        public RenderGraphTextureDesc[] AttachmentTextureDesc; //Raster Specific

        public RenderGraphBufferDesc[] ReadBufferDesc;
        public RenderGraphBufferDesc[] WriteBufferDesc;
        public RenderGraphBufferDesc[] ReadWriteBufferDesc;
    }

    public interface IRenderPass
    {

        ///Prepare runtime resource(e.g:dynamic count buffer)  
        ///after Prepare,the RenderGraph resource will automatic use by IBaseRenderGraphBuilder and derived type
        /// using (var builder = renderGraph.AddComputePass<PassData>("Example", out var passData))
        /// {
        /// Prepare(FrameContext)
        /// builder.
        /// }
        void Prepare(ContextContainer frameData);


        ///builder.SetRenderFunc(static (PassData data, ComputeGraphContext context) =>
        ///{
        /// data.pass.Record(context)
        /// }
        void Record();

        //RenderGraph use to bake the pass resource info 
        PassResource Initialize()
        {
            var type = GetType();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            PassResource passResource=new PassResource();
            foreach (var field in fields)
            {
                var attr = field.GetCustomAttribute<RenderGraphResource>();
                if (attr != null)
                {
                    
                    if (attr.Flags is AccessFlags.Read)
                    {
                    }
                }
            }

            return passResource;
        }
    }


    public interface IComputePass : IRenderPass
    {
        class PassData
        {
        }

        // void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        // {
        // }
    }


    public interface IRasterPass : IRenderPass
    {
    }


    public interface IUnsafePass : IRenderPass
    {
    }
}