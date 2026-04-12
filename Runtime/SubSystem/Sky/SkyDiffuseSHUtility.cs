using UnityEngine;
using UnityEngine.Rendering;

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

    internal static class SkyCubemapBakingUtility
    {
        private static readonly int PixelCoordToViewDirWSId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private const float CubemapVerticalFieldOfViewRadians = 0.5f * Mathf.PI;

        internal static Matrix4x4 GetCubemapFacePixelCoordToViewDirWSMatrix(CubemapFace cubemapFace, int resolution)
        {
            var screenSize = new Vector4(resolution, resolution, 1.0f / resolution, 1.0f / resolution);
            var lookAt = Matrix4x4.LookAt(Vector3.zero, GetFaceForward(cubemapFace), GetFaceUp(cubemapFace));
            var worldToView = lookAt * Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));

            return CoreUtils.ComputePixelCoordToWorldSpaceViewDirectionMatrix(
                CubemapVerticalFieldOfViewRadians,
                Vector2.zero,
                screenSize,
                worldToView,
                true);
        }

        internal static void RenderSkyToCubemap(
            CommandBuffer cmd,
            RenderTexture targetCubemap,
            Material material,
            MaterialPropertyBlock propertyBlock,
            int passIndex)
        {
            if (cmd == null
                || targetCubemap == null
                || material == null
                || propertyBlock == null
                || passIndex < 0)
            {
                return;
            }

            for (var faceIndex = 0; faceIndex < SkyDiffuseSHUtility.ValidCubemapFaces.Length; faceIndex++)
            {
                var cubemapFace = SkyDiffuseSHUtility.ValidCubemapFaces[faceIndex];
                propertyBlock.SetMatrix(
                    PixelCoordToViewDirWSId,
                    GetCubemapFacePixelCoordToViewDirWSMatrix(cubemapFace, targetCubemap.width));

                cmd.SetRenderTarget(targetCubemap, 0, cubemapFace);
                cmd.SetViewport(new Rect(0.0f, 0.0f, targetCubemap.width, targetCubemap.height));
                CoreUtils.DrawFullScreen(cmd, material, propertyBlock, passIndex);
            }

            cmd.GenerateMips(targetCubemap);
        }

        private static Vector3 GetFaceForward(CubemapFace cubemapFace)
        {
            return cubemapFace switch
            {
                CubemapFace.PositiveX => Vector3.right,
                CubemapFace.NegativeX => Vector3.left,
                CubemapFace.PositiveY => Vector3.up,
                CubemapFace.NegativeY => Vector3.down,
                CubemapFace.PositiveZ => Vector3.forward,
                CubemapFace.NegativeZ => Vector3.back,
                _ => Vector3.forward,
            };
        }

        private static Vector3 GetFaceUp(CubemapFace cubemapFace)
        {
            return cubemapFace switch
            {
                CubemapFace.PositiveY => Vector3.back,
                CubemapFace.NegativeY => Vector3.forward,
                _ => Vector3.up,
            };
        }
    }
}
