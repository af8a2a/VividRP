using UnityEngine;
namespace VividRP.Runtime
{
    internal static class SkyDiffuseSHUtility
    {
        internal static readonly CubemapFace[] ValidCubemapFaces =
        {
            CubemapFace.PositiveX,
            CubemapFace.NegativeX,
            CubemapFace.PositiveY,
            CubemapFace.NegativeY,
            CubemapFace.PositiveZ,
            CubemapFace.NegativeZ
        };

        internal static Vector3 GetDirectionForCubemapFace(CubemapFace face, int x, int y, int size)
        {
            var u = ((x + 0.5f) / size) * 2.0f - 1.0f;
            var v = ((y + 0.5f) / size) * 2.0f - 1.0f;

            Vector3 direction = face switch
            {
                CubemapFace.PositiveX => new Vector3(1.0f, -v, -u),
                CubemapFace.NegativeX => new Vector3(-1.0f, -v, u),
                CubemapFace.PositiveY => new Vector3(u, 1.0f, v),
                CubemapFace.NegativeY => new Vector3(u, -1.0f, -v),
                CubemapFace.PositiveZ => new Vector3(u, -v, 1.0f),
                CubemapFace.NegativeZ => new Vector3(-u, -v, -1.0f),
                _ => Vector3.forward,
            };

            return direction.normalized;
        }
    }
}
