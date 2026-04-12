using System;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal interface ISkyRenderer : IDisposable
    {
        SkyType Type { get; }

        bool IsActive();

        int GetSkyHash(in SkyRendererContext context);

        void Build(VividRPCoreResources resources);

        void Update(in SkyRendererContext context, VividSkyData skyData, CommandBuffer cmd, int skyHash, bool forceRebuild);
    }
}
