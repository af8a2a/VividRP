namespace VividRP.Runtime
{
    internal static class RenderGraphTextureDescUtility
    {
        internal static bool HasExplicitSize(RenderGraphTextureDesc descriptor)
        {
            return descriptor != null
                && descriptor.Width > 0
                && descriptor.Height > 0
                && !(descriptor.Width == 1 && descriptor.Height == 1);
        }
    }
}
