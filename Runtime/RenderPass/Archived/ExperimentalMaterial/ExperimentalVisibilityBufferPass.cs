using System;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Runtime.RenderPass.Experimental.Material
{
    [Obsolete("Use VisibilityBufferPass. Experimental materials now render through the shared visibility buffer.")]
    public sealed class ExperimentalVisibilityBufferPass : VisibilityBufferPass
    {
    }
}
