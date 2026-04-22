using UnityEngine;

namespace VividRP.Runtime
{
    internal static class CameraDimensionUtility
    {
        internal static int ResolveCameraDimension(
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension)
        {
            if (actualCameraDimension > 0)
                return actualCameraDimension;

            if (cameraDimension > 0)
                return cameraDimension;

            return Mathf.Max(1, screenDimension);
        }
    }
}
