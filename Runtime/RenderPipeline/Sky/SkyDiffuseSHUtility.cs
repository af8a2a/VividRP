using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class SkyDiffuseSHUtility
    {
        private const float Y00 = 0.2820947918f;
        private const float Y1 = 0.4886025119f;
        private const float Y2_2 = 1.0925484306f;
        private const float Y20 = 0.3153915653f;
        private const float Y22 = 0.5462742153f;
        private const int TargetFaceResolution = 16;
        internal static readonly CubemapFace[] ValidCubemapFaces =
        {
            CubemapFace.PositiveX,
            CubemapFace.NegativeX,
            CubemapFace.PositiveY,
            CubemapFace.NegativeY,
            CubemapFace.PositiveZ,
            CubemapFace.NegativeZ
        };

        internal static bool TryProjectCubemapToSH(
            Cubemap cubemap,
            Color tint,
            float exposure,
            float rotation,
            out SphericalHarmonicsL2 sh)
        {
            sh = default;

            if (cubemap == null)
                return false;

            try
            {
                var mipLevel = GetProjectionMip(cubemap);
                var faceSize = Mathf.Max(1, cubemap.width >> mipLevel);
                var intensity = Mathf.Max(exposure, 0.0f);
                var rotationQuaternion = Quaternion.Euler(0.0f, -rotation, 0.0f);

                foreach (var face in ValidCubemapFaces)
                {
                    var pixels = cubemap.GetPixels(face, mipLevel);
                    if (pixels == null || pixels.Length == 0)
                        continue;

                    for (var index = 0; index < pixels.Length; index++)
                    {
                        var x = index % faceSize;
                        var y = index / faceSize;
                        var texelDirection = GetDirectionForCubemapFace(face, x, y, faceSize);
                        var worldDirection = rotationQuaternion * texelDirection;
                        var weight = GetTexelSolidAngle(x, y, faceSize);
                        var color = pixels[index];
                        color.r *= tint.r * intensity;
                        color.g *= tint.g * intensity;
                        color.b *= tint.b * intensity;

                        Accumulate(ref sh, worldDirection, color, weight);
                    }
                }

                return true;
            }
            catch (UnityException exception)
            {
                Debug.LogWarning($"[VividRP] Failed to project sky cubemap '{cubemap.name}' to SH. Ensure Read/Write is enabled. {exception.Message}");
                return false;
            }
        }

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

        private static int GetProjectionMip(Cubemap cubemap)
        {
            if (cubemap == null || cubemap.mipmapCount <= 1)
                return 0;

            var mipLevel = 0;
            var faceSize = cubemap.width;
            while (mipLevel + 1 < cubemap.mipmapCount && faceSize > TargetFaceResolution)
            {
                mipLevel++;
                faceSize >>= 1;
            }

            return mipLevel;
        }

        private static void Accumulate(ref SphericalHarmonicsL2 sh, Vector3 direction, Color color, float weight)
        {
            var basis0 = Y00;
            var basis1 = Y1 * direction.y;
            var basis2 = Y1 * direction.z;
            var basis3 = Y1 * direction.x;
            var basis4 = Y2_2 * direction.x * direction.y;
            var basis5 = Y2_2 * direction.y * direction.z;
            var basis6 = Y20 * (3.0f * direction.z * direction.z - 1.0f);
            var basis7 = Y2_2 * direction.x * direction.z;
            var basis8 = Y22 * (direction.x * direction.x - direction.y * direction.y);

            AddCoefficient(ref sh, 0, color.r, weight, basis0, basis1, basis2, basis3, basis4, basis5, basis6, basis7, basis8);
            AddCoefficient(ref sh, 1, color.g, weight, basis0, basis1, basis2, basis3, basis4, basis5, basis6, basis7, basis8);
            AddCoefficient(ref sh, 2, color.b, weight, basis0, basis1, basis2, basis3, basis4, basis5, basis6, basis7, basis8);
        }

        private static void AddCoefficient(
            ref SphericalHarmonicsL2 sh,
            int channel,
            float value,
            float weight,
            float basis0,
            float basis1,
            float basis2,
            float basis3,
            float basis4,
            float basis5,
            float basis6,
            float basis7,
            float basis8)
        {
            var scaledValue = value * weight;
            sh[channel, 0] += scaledValue * basis0;
            sh[channel, 1] += scaledValue * basis1;
            sh[channel, 2] += scaledValue * basis2;
            sh[channel, 3] += scaledValue * basis3;
            sh[channel, 4] += scaledValue * basis4;
            sh[channel, 5] += scaledValue * basis5;
            sh[channel, 6] += scaledValue * basis6;
            sh[channel, 7] += scaledValue * basis7;
            sh[channel, 8] += scaledValue * basis8;
        }

        private static float GetTexelSolidAngle(int x, int y, int size)
        {
            var u = ((x + 0.5f) / size) * 2.0f - 1.0f;
            var v = ((y + 0.5f) / size) * 2.0f - 1.0f;
            var invResolution = 1.0f / size;
            var x0 = u - invResolution;
            var y0 = v - invResolution;
            var x1 = u + invResolution;
            var y1 = v + invResolution;

            return AreaElement(x0, y0) - AreaElement(x0, y1) - AreaElement(x1, y0) + AreaElement(x1, y1);
        }

        private static float AreaElement(float x, float y)
        {
            return Mathf.Atan2(x * y, Mathf.Sqrt(x * x + y * y + 1.0f));
        }
    }
}
