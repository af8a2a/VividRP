using System;

namespace VividRP.Runtime
{
    internal interface ISkyRenderer : IDisposable
    {
        SkyType Type { get; }

        bool IsActive();

        int GetSkyHash(in SkyRendererContext context);

        void Build(VividRPCoreResources resources);

        void Update(in SkyRendererContext context, VividSkyData skyData);
    }
}
