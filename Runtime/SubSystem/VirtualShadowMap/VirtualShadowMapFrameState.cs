namespace VividRP.Runtime.VirtualShadowMap
{
    internal enum VirtualShadowMapPrototypeFrameState
    {
        Disabled = 0,
        Prepared = 1,
        Refreshing = 2,
        Cached = 3,
        Active = 4,
        Fallback = 5,
    }

    internal enum VirtualShadowMapPrototypeFallbackReason
    {
        None = 0,
        InvalidCamera = 1,
        UnityCasterIncompatible = 2,
        UnsupportedPlatform = 3,
        ResourceUnavailable = 4,
        InvalidCacheKey = 5,
        GPUDrivenUnavailable = 6,
        MeshletShaderResourceUnavailable = 7,
        MeshletDrawSetUnavailable = 8,
        VirtualTextureUnavailable = 9,
        RecordPreparationFailed = 10,
        PhysicalResourceUnavailable = 11,
        ReceiverFeedbackUnavailable = 12,
        PageManagementUnavailable = 13,
    }
}
