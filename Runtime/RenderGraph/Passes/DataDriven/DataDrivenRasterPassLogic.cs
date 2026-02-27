using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderGraph.Passes.DataDriven
{
    public readonly struct DataDrivenRasterPassContext
    {
        public RendererListHandle RendererList { get; }
        public bool HasRendererList { get; }

        public DataDrivenRasterPassContext(RendererListHandle rendererList, bool hasRendererList)
        {
            RendererList = rendererList;
            HasRendererList = hasRendererList;
        }
    }

    public abstract class DataDrivenRasterPassLogic
    {
        public virtual void Execute(RasterGraphContext context, in DataDrivenRasterPassContext passContext)
        {
            if (passContext.HasRendererList)
                context.cmd.DrawRendererList(passContext.RendererList);
        }
    }

    public sealed class DefaultRasterPassLogic : DataDrivenRasterPassLogic
    {
        [PassReadWrite] public TextureHandle Color;
        [PassDepth, PassReadWrite] public TextureHandle Depth;
        [PassRead] public RendererListHandle RendererList;
    }
}
