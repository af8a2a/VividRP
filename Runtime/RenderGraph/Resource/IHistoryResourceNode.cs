namespace VividRP.Runtime.RenderGraph.Resource
{
    public interface IHistoryResourceNode
    {
        ResourceSlot CreateHistorySlot(ResourceCreationContext context);
        string HistoryPortId { get; }
    }
}
