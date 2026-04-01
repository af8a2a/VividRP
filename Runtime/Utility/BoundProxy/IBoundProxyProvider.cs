namespace VividRP.Runtime
{
    public interface IBoundProxyProvider
    {
        BoundProxyFeature BoundProxyFeature { get; }

        bool IsBoundProxyActive { get; }

        UnityEngine.Transform BoundProxyTransform { get; }

        BoundProxyShape BoundProxyShape { get; }
    }
}
