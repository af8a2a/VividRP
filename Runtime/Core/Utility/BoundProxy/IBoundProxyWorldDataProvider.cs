namespace VividRP.Runtime
{
    internal interface IBoundProxyWorldDataProvider
    {
        bool TryCreateBoundProxyWorldData(out BoundProxyWorldData worldData);
    }
}
