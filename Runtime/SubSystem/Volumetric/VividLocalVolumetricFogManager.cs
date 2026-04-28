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
        public const int MaxVolumetricMaterialViewCount = 2;
        private const int IndirectDrawIndexedArgsUIntCount = 5;
        private const int IndirectDrawIndexedArgsStride = IndirectDrawIndexedArgsUIntCount * 4;

        private static readonly List<VividLocalVolumetricFog> s_RegisteredFogs = new();
        private static readonly Comparison<VividLocalVolumetricFog> s_PriorityComparison =
            (left, right) => right.priority.CompareTo(left.priority);
        private static VividLocalVolumetricFogEngineData[] s_UploadData =
            new VividLocalVolumetricFogEngineData[MaxVisibleLocalVolumetricFogCount];
        private static readonly VividVolumetricMaterialBounds[] s_VolumeBoundsData =
            new VividVolumetricMaterialBounds[MaxVisibleLocalVolumetricFogCount];
        private static readonly uint[] s_VisibleGlobalIndicesData =
            new uint[MaxVisibleLocalVolumetricFogCount];
        private static readonly VividLocalVolumetricFog[] s_VisibleMaterialFogs =
            new VividLocalVolumetricFog[MaxVisibleLocalVolumetricFogCount];
        private static readonly uint[] s_VolumetricMaterialIndexData =
        {
            0, 1, 2,
            0, 2, 3,
            0, 3, 4,
            0, 4, 5
        };
        private static readonly Texture3D[] s_VisibleMaskTextures =
            new Texture3D[MaxVisibleLocalVolumetricFogMaskCount];
        private static readonly int[] s_VisibleMaskTextureIds = BuildVisibleMaskTextureIds();
        private static readonly RenderGraphBuffer s_LocalFogBuffer =
            RenderGraphBuffer.CreateStructured(
                "LocalVolumetricFogs",
                MaxVisibleLocalVolumetricFogCount,
                VividLocalVolumetricFogEngineData.Stride);
        private static readonly RenderGraphBuffer s_VolumeBoundsBuffer =
            RenderGraphBuffer.CreateStructured(
                "VolumeBounds",
                MaxVisibleLocalVolumetricFogCount,
                VividVolumetricMaterialBounds.Stride);
        private static readonly RenderGraphBuffer s_VisibleGlobalIndicesBuffer =
            RenderGraphBuffer.CreateStructured(
                "VolumetricVisibleGlobalIndices",
                MaxVisibleLocalVolumetricFogCount,
                4,
                GraphicsBuffer.Target.Raw);
        private static readonly RenderGraphBuffer s_GlobalIndirectArgsBuffer =
            RenderGraphBuffer.CreateStructured(
                "VolumetricGlobalIndirectArgs",
                MaxVisibleLocalVolumetricFogCount,
                IndirectDrawIndexedArgsStride,
                GraphicsBuffer.Target.IndirectArguments);
        private static readonly RenderGraphBuffer s_GlobalIndirectionBuffer =
            RenderGraphBuffer.CreateStructured(
                "VolumetricGlobalIndirection",
                MaxVisibleLocalVolumetricFogCount,
                4,
                GraphicsBuffer.Target.Raw);
        private static readonly RenderGraphBuffer s_VolumetricMaterialDataBuffer =
            RenderGraphBuffer.CreateStructured(
                "VolumetricMaterialData",
                MaxVisibleLocalVolumetricFogCount * MaxVolumetricMaterialViewCount,
                VividVolumetricMaterialRenderingData.Stride);
        private static Texture3D s_DefaultMaskTexture;
        private static GraphicsBuffer s_VolumetricMaterialIndexBuffer;
        private static int s_MaterialFogCount;

        public static int registeredFogCount => s_RegisteredFogs.Count;

        internal static RenderGraphBuffer buffer => s_LocalFogBuffer;
        internal static RenderGraphBuffer volumeBoundsBuffer => s_VolumeBoundsBuffer;
        internal static RenderGraphBuffer visibleGlobalIndicesBuffer => s_VisibleGlobalIndicesBuffer;
        internal static RenderGraphBuffer globalIndirectArgsBuffer => s_GlobalIndirectArgsBuffer;
        internal static RenderGraphBuffer globalIndirectionBuffer => s_GlobalIndirectionBuffer;
        internal static RenderGraphBuffer volumetricMaterialDataBuffer => s_VolumetricMaterialDataBuffer;
        internal static GraphicsBuffer volumeBoundsGraphicsBuffer => s_VolumeBoundsBuffer.ImportedGraphicsBuffer;
        internal static GraphicsBuffer visibleGlobalIndicesGraphicsBuffer => s_VisibleGlobalIndicesBuffer.ImportedGraphicsBuffer;
        internal static GraphicsBuffer globalIndirectArgsGraphicsBuffer => s_GlobalIndirectArgsBuffer.ImportedGraphicsBuffer;
        internal static GraphicsBuffer globalIndirectionGraphicsBuffer => s_GlobalIndirectionBuffer.ImportedGraphicsBuffer;
        internal static GraphicsBuffer volumetricMaterialDataGraphicsBuffer => s_VolumetricMaterialDataBuffer.ImportedGraphicsBuffer;
        internal static GraphicsBuffer volumetricMaterialIndexBuffer => s_VolumetricMaterialIndexBuffer;
        internal static int materialFogCount => s_MaterialFogCount;

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
            var materialCount = 0;
            var planes = camera != null ? GeometryUtility.CalculateFrustumPlanes(camera) : null;

            for (int index = 0; index < s_RegisteredFogs.Count; index++)
            {
                var fog = s_RegisteredFogs[index];
                if (fog == null || !fog.IsActive())
                    continue;

                if (planes != null && !GeometryUtility.TestPlanesAABB(planes, fog.GetBounds()))
                    continue;

                if (fog.UsesVolumetricMaterialVoxelization())
                {
                    if (materialCount < MaxVisibleLocalVolumetricFogCount)
                    {
                        s_VolumeBoundsData[materialCount] = fog.ConvertToVolumeBounds();
                        s_VisibleGlobalIndicesData[materialCount] = (uint)materialCount;
                        s_VisibleMaterialFogs[materialCount] = fog;
                        materialCount++;
                    }
                    continue;
                }

                if (count >= MaxVisibleLocalVolumetricFogCount)
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

            s_MaterialFogCount = materialCount;
            BindVisibleMaskTextures();
            EnsureBufferDescriptor();
            EnsureVolumetricMaterialBufferDescriptors();
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

            UploadVolumetricMaterialBuffers(materialCount);
            PrepareVolumetricMaterialDrawCalls(materialCount);

            return count;
        }

        internal static void Dispose()
        {
            s_LocalFogBuffer.ClearImportedBuffer();
            s_VolumeBoundsBuffer.ClearImportedBuffer();
            s_VisibleGlobalIndicesBuffer.ClearImportedBuffer();
            s_GlobalIndirectArgsBuffer.ClearImportedBuffer();
            s_GlobalIndirectionBuffer.ClearImportedBuffer();
            s_VolumetricMaterialDataBuffer.ClearImportedBuffer();
            Array.Clear(s_UploadData, 0, s_UploadData.Length);
            Array.Clear(s_VolumeBoundsData, 0, s_VolumeBoundsData.Length);
            Array.Clear(s_VisibleGlobalIndicesData, 0, s_VisibleGlobalIndicesData.Length);
            Array.Clear(s_VisibleMaterialFogs, 0, s_VisibleMaterialFogs.Length);
            Array.Clear(s_VisibleMaskTextures, 0, s_VisibleMaskTextures.Length);
            s_MaterialFogCount = 0;
            for (int index = 0; index < s_VisibleMaskTextureIds.Length; index++)
                Shader.SetGlobalTexture(s_VisibleMaskTextureIds[index], null);

            s_VolumetricMaterialIndexBuffer?.Dispose();
            s_VolumetricMaterialIndexBuffer = null;

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

        private static void EnsureVolumetricMaterialBufferDescriptors()
        {
            ConfigureBufferDescriptor(
                s_VolumeBoundsBuffer,
                MaxVisibleLocalVolumetricFogCount,
                VividVolumetricMaterialBounds.Stride,
                GraphicsBuffer.Target.Structured,
                "VolumeBounds");
            ConfigureBufferDescriptor(
                s_VisibleGlobalIndicesBuffer,
                MaxVisibleLocalVolumetricFogCount,
                4,
                GraphicsBuffer.Target.Raw,
                "VolumetricVisibleGlobalIndices");
            ConfigureBufferDescriptor(
                s_GlobalIndirectArgsBuffer,
                MaxVisibleLocalVolumetricFogCount,
                IndirectDrawIndexedArgsStride,
                GraphicsBuffer.Target.IndirectArguments,
                "VolumetricGlobalIndirectArgs");
            ConfigureBufferDescriptor(
                s_GlobalIndirectionBuffer,
                MaxVisibleLocalVolumetricFogCount,
                4,
                GraphicsBuffer.Target.Raw,
                "VolumetricGlobalIndirection");
            ConfigureBufferDescriptor(
                s_VolumetricMaterialDataBuffer,
                MaxVisibleLocalVolumetricFogCount * MaxVolumetricMaterialViewCount,
                VividVolumetricMaterialRenderingData.Stride,
                GraphicsBuffer.Target.Structured,
                "VolumetricMaterialData");

            EnsureVolumetricMaterialIndexBuffer();
        }

        private static void ConfigureBufferDescriptor(
            RenderGraphBuffer buffer,
            int count,
            int stride,
            GraphicsBuffer.Target target,
            string name)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = count;
            buffer.desc.Stride = stride;
            buffer.desc.Target = target;
            buffer.desc.Name = name;
        }

        private static void UploadVolumetricMaterialBuffers(int materialCount)
        {
            var boundsBuffer = s_VolumeBoundsBuffer.EnsureImportedBuffer();
            var visibleIndicesBuffer = s_VisibleGlobalIndicesBuffer.EnsureImportedBuffer();
            s_GlobalIndirectArgsBuffer.EnsureImportedBuffer();
            s_GlobalIndirectionBuffer.EnsureImportedBuffer();
            s_VolumetricMaterialDataBuffer.EnsureImportedBuffer();

            if (materialCount > 0)
            {
                boundsBuffer?.SetData(s_VolumeBoundsData, 0, 0, materialCount);
                visibleIndicesBuffer?.SetData(s_VisibleGlobalIndicesData, 0, 0, materialCount);
            }
            else
            {
                s_VolumeBoundsData[0] = default;
                s_VisibleGlobalIndicesData[0] = 0;
                boundsBuffer?.SetData(s_VolumeBoundsData, 0, 0, 1);
                visibleIndicesBuffer?.SetData(s_VisibleGlobalIndicesData, 0, 0, 1);
            }
        }

        private static void PrepareVolumetricMaterialDrawCalls(int materialCount)
        {
            EnsureVolumetricMaterialIndexBuffer();
            var materialDataBuffer = s_VolumetricMaterialDataBuffer.ImportedGraphicsBuffer;
            var indirectArgsBuffer = s_GlobalIndirectArgsBuffer.ImportedGraphicsBuffer;
            if (materialDataBuffer == null || indirectArgsBuffer == null || s_VolumetricMaterialIndexBuffer == null)
                return;

            for (int index = 0; index < materialCount; index++)
                s_VisibleMaterialFogs[index]?.PrepareVolumetricMaterialDrawCall(
                    index,
                    materialDataBuffer,
                    s_VolumetricMaterialIndexBuffer,
                    indirectArgsBuffer);
        }

        private static void EnsureVolumetricMaterialIndexBuffer()
        {
            if (s_VolumetricMaterialIndexBuffer != null && s_VolumetricMaterialIndexBuffer.IsValid())
                return;

            s_VolumetricMaterialIndexBuffer?.Dispose();
            s_VolumetricMaterialIndexBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Index,
                s_VolumetricMaterialIndexData.Length,
                sizeof(uint));
            s_VolumetricMaterialIndexBuffer.SetData(s_VolumetricMaterialIndexData);
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
