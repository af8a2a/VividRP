using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public static class VividLocalVolumetricFogManager
    {
        public const int MaxVisibleLocalVolumetricFogCount = 64;
        public const int MaxVisibleLocalVolumetricFogMaskCount = 8;

        private static readonly List<VividLocalVolumetricFog> s_RegisteredFogs = new();
        private static readonly Comparison<VividLocalVolumetricFog> s_PriorityComparison =
            (left, right) => right.priority.CompareTo(left.priority);
        private static VividLocalVolumetricFogEngineData[] s_UploadData =
            new VividLocalVolumetricFogEngineData[MaxVisibleLocalVolumetricFogCount];
        private static readonly Texture3D[] s_VisibleMaskTextures =
            new Texture3D[MaxVisibleLocalVolumetricFogMaskCount];
        private static readonly int[] s_VisibleMaskTextureIds = BuildVisibleMaskTextureIds();
        private static readonly RenderGraphBuffer s_LocalFogBuffer =
            RenderGraphBuffer.CreateStructured(
                "LocalVolumetricFogs",
                MaxVisibleLocalVolumetricFogCount,
                VividLocalVolumetricFogEngineData.Stride);
        private static Texture3D s_DefaultMaskTexture;

        public static int registeredFogCount => s_RegisteredFogs.Count;

        internal static RenderGraphBuffer buffer => s_LocalFogBuffer;

        public static void Register(VividLocalVolumetricFog fog)
        {
            if (fog == null || s_RegisteredFogs.Contains(fog))
                return;

            s_RegisteredFogs.Add(fog);
        }

        public static void Unregister(VividLocalVolumetricFog fog)
        {
            if (fog == null)
                return;

            s_RegisteredFogs.Remove(fog);
        }

        internal static int PrepareVisibleFogs(Camera camera)
        {
            RemoveDestroyedFogs();
            s_RegisteredFogs.Sort(s_PriorityComparison);
            Array.Clear(s_VisibleMaskTextures, 0, s_VisibleMaskTextures.Length);

            var count = 0;
            var maskCount = 0;
            var planes = camera != null ? GeometryUtility.CalculateFrustumPlanes(camera) : null;

            for (int index = 0; index < s_RegisteredFogs.Count && count < MaxVisibleLocalVolumetricFogCount; index++)
            {
                var fog = s_RegisteredFogs[index];
                if (fog == null || !fog.IsActive())
                    continue;

                if (planes != null && !GeometryUtility.TestPlanesAABB(planes, fog.GetBounds()))
                    continue;

                var data = fog.ConvertToEngineData(camera);
                if (fog.TryGetVolumeMask(out var volumeMask, out var alphaOnly)
                    && maskCount < MaxVisibleLocalVolumetricFogMaskCount)
                {
                    s_VisibleMaskTextures[maskCount] = volumeMask;
                    data.parameters.w = alphaOnly ? 2.0f : 1.0f;
                    data.textureScaleOffset0.w = maskCount + 1.0f;
                    maskCount++;
                }

                s_UploadData[count++] = data;
            }

            BindVisibleMaskTextures();
            EnsureBufferDescriptor();
            var graphicsBuffer = s_LocalFogBuffer.EnsureImportedBuffer();
            if (graphicsBuffer != null)
            {
                if (count > 0)
                {
                    s_LocalFogBuffer.SetData(s_UploadData, 0, 0, count);
                }
                else
                {
                    s_UploadData[0] = default;
                    s_LocalFogBuffer.SetData(s_UploadData, 0, 0, 1);
                }
            }

            return count;
        }

        internal static void Dispose()
        {
            s_LocalFogBuffer.ClearImportedBuffer();
            Array.Clear(s_UploadData, 0, s_UploadData.Length);
            Array.Clear(s_VisibleMaskTextures, 0, s_VisibleMaskTextures.Length);
            for (int index = 0; index < s_VisibleMaskTextureIds.Length; index++)
                Shader.SetGlobalTexture(s_VisibleMaskTextureIds[index], null);

            if (s_DefaultMaskTexture != null)
            {
                CoreUtils.Destroy(s_DefaultMaskTexture);
                s_DefaultMaskTexture = null;
            }
        }

        private static int[] BuildVisibleMaskTextureIds()
        {
            var ids = new int[MaxVisibleLocalVolumetricFogMaskCount];
            for (int index = 0; index < ids.Length; index++)
                ids[index] = Shader.PropertyToID($"_LocalVolumetricFogMask{index}");

            return ids;
        }

        private static void BindVisibleMaskTextures()
        {
            EnsureDefaultMaskTexture();
            for (int index = 0; index < s_VisibleMaskTextureIds.Length; index++)
                Shader.SetGlobalTexture(s_VisibleMaskTextureIds[index], s_VisibleMaskTextures[index] ?? s_DefaultMaskTexture);
        }

        private static void EnsureDefaultMaskTexture()
        {
            if (s_DefaultMaskTexture != null)
                return;

            s_DefaultMaskTexture = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false)
            {
                name = "VividDefaultLocalVolumetricFogMask",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            s_DefaultMaskTexture.SetPixels(new[] { Color.white });
            s_DefaultMaskTexture.Apply(false, true);
        }

        private static void EnsureBufferDescriptor()
        {
            if (s_LocalFogBuffer.desc == null)
                return;

            s_LocalFogBuffer.desc.Count = MaxVisibleLocalVolumetricFogCount;
            s_LocalFogBuffer.desc.Stride = VividLocalVolumetricFogEngineData.Stride;
            s_LocalFogBuffer.desc.Target = UnityEngine.GraphicsBuffer.Target.Structured;
            s_LocalFogBuffer.desc.Name = "LocalVolumetricFogs";
        }

        private static void RemoveDestroyedFogs()
        {
            for (int index = s_RegisteredFogs.Count - 1; index >= 0; index--)
            {
                if (s_RegisteredFogs[index] == null)
                    s_RegisteredFogs.RemoveAt(index);
            }
        }
    }
}
