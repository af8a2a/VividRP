using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public static class VividLocalVolumetricFogManager
    {
        public const int MaxVisibleLocalVolumetricFogCount = 64;
        public const int MaxVolumetricMaterialViewCount = 2;
        private const int IndirectDrawIndexedArgsUIntCount = 5;
        private const int IndirectDrawIndexedArgsStride = IndirectDrawIndexedArgsUIntCount * 4;
        private const string DefaultVoxelizationShaderName = "Hidden/VividRP/LocalVolumetricFogVoxelize";

        private static readonly List<VividLocalVolumetricFog> s_RegisteredFogs = new();
        private static readonly Comparison<VividLocalVolumetricFog> s_PriorityComparison =
            (left, right) => right.priority.CompareTo(left.priority);
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
        private static readonly Material[] s_DefaultVoxelizationMaterials =
            new Material[Enum.GetValues(typeof(VividLocalVolumetricFogBlendingMode)).Length];
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
        private static readonly int FogVolumeBlendModeId = Shader.PropertyToID("_FogVolumeBlendMode");
        private static readonly int FogVolumeSrcColorBlendId = Shader.PropertyToID("_FogVolumeSrcColorBlend");
        private static readonly int FogVolumeDstColorBlendId = Shader.PropertyToID("_FogVolumeDstColorBlend");
        private static readonly int FogVolumeSrcAlphaBlendId = Shader.PropertyToID("_FogVolumeSrcAlphaBlend");
        private static readonly int FogVolumeDstAlphaBlendId = Shader.PropertyToID("_FogVolumeDstAlphaBlend");
        private static readonly int FogVolumeColorBlendOpId = Shader.PropertyToID("_FogVolumeColorBlendOp");
        private static readonly int FogVolumeAlphaBlendOpId = Shader.PropertyToID("_FogVolumeAlphaBlendOp");

        public static int registeredFogCount => s_RegisteredFogs.Count;

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
        internal static Texture3D defaultMaskTexture
        {
            get
            {
                EnsureDefaultMaskTexture();
                return s_DefaultMaskTexture;
            }
        }

        public static void Register(VividLocalVolumetricFog fog)
        {
            if (fog == null || s_RegisteredFogs.Contains(fog))
                return;

            s_RegisteredFogs.Add(fog);
        }

        public static bool Contains(VividLocalVolumetricFog fog)
        {
            return fog != null && s_RegisteredFogs.Contains(fog);
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
            Array.Clear(s_VisibleMaterialFogs, 0, s_VisibleMaterialFogs.Length);

            var materialCount = 0;
            var planes = camera != null ? GeometryUtility.CalculateFrustumPlanes(camera) : null;

            for (int index = 0; index < s_RegisteredFogs.Count; index++)
            {
                var fog = s_RegisteredFogs[index];
                if (fog == null || !fog.IsActive())
                    continue;

                if (planes != null && !GeometryUtility.TestPlanesAABB(planes, fog.GetBounds()))
                    continue;

                if (materialCount >= MaxVisibleLocalVolumetricFogCount)
                    continue;

                s_VolumeBoundsData[materialCount] = fog.ConvertToVolumeBounds();
                s_VisibleGlobalIndicesData[materialCount] = (uint)materialCount;
                s_VisibleMaterialFogs[materialCount] = fog;
                materialCount++;
            }

            s_MaterialFogCount = materialCount;
            EnsureVolumetricMaterialBufferDescriptors();
            UploadVolumetricMaterialBuffers(materialCount);
            PrepareVolumetricMaterialDrawCalls(materialCount);

            return materialCount;
        }

        internal static void Dispose()
        {
            s_VolumeBoundsBuffer.ClearImportedBuffer();
            s_VisibleGlobalIndicesBuffer.ClearImportedBuffer();
            s_GlobalIndirectArgsBuffer.ClearImportedBuffer();
            s_GlobalIndirectionBuffer.ClearImportedBuffer();
            s_VolumetricMaterialDataBuffer.ClearImportedBuffer();
            Array.Clear(s_VolumeBoundsData, 0, s_VolumeBoundsData.Length);
            Array.Clear(s_VisibleGlobalIndicesData, 0, s_VisibleGlobalIndicesData.Length);
            Array.Clear(s_VisibleMaterialFogs, 0, s_VisibleMaterialFogs.Length);
            s_MaterialFogCount = 0;

            s_VolumetricMaterialIndexBuffer?.Dispose();
            s_VolumetricMaterialIndexBuffer = null;

            for (int index = 0; index < s_DefaultVoxelizationMaterials.Length; index++)
            {
                if (s_DefaultVoxelizationMaterials[index] == null)
                    continue;

                CoreUtils.Destroy(s_DefaultVoxelizationMaterials[index]);
                s_DefaultVoxelizationMaterials[index] = null;
            }

            if (s_DefaultMaskTexture != null)
            {
                CoreUtils.Destroy(s_DefaultMaskTexture);
                s_DefaultMaskTexture = null;
            }
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
            {
                var fog = s_VisibleMaterialFogs[index];
                var defaultMaterial = fog != null ? GetDefaultVoxelizationMaterial(fog.blendingMode) : null;
                s_VisibleMaterialFogs[index]?.PrepareVolumetricMaterialDrawCall(
                    index,
                    materialDataBuffer,
                    s_VolumetricMaterialIndexBuffer,
                    indirectArgsBuffer,
                    defaultMaterial,
                    defaultMaskTexture);
            }
        }

        private static Material GetDefaultVoxelizationMaterial(VividLocalVolumetricFogBlendingMode blendingMode)
        {
            int index = Mathf.Clamp((int)blendingMode, 0, s_DefaultVoxelizationMaterials.Length - 1);
            if (s_DefaultVoxelizationMaterials[index] != null)
                return s_DefaultVoxelizationMaterials[index];

            var shader = ResolveDefaultVoxelizationShader();
            if (shader == null)
                return null;

            var material = CoreUtils.CreateEngineMaterial(shader);
            material.name = $"Vivid Local Volumetric Fog Voxelize ({blendingMode})";
            material.hideFlags = HideFlags.HideAndDontSave;
            SetupFogVolumeBlendMode(material, blendingMode);
            s_DefaultVoxelizationMaterials[index] = material;
            return material;
        }

        private static Shader ResolveDefaultVoxelizationShader()
        {
            var shader = PipelineResourceManager.Get<VividRPCoreResources>()?.LocalVolumetricFogVoxelizeShader;
            return shader != null ? shader : Shader.Find(DefaultVoxelizationShaderName);
        }

        internal static void SetupFogVolumeBlendMode(Material material, VividLocalVolumetricFogBlendingMode mode)
        {
            if (material == null)
                return;

            ComputeBlendParameters(
                mode,
                out var srcColorBlend,
                out var srcAlphaBlend,
                out var dstColorBlend,
                out var dstAlphaBlend,
                out var colorBlendOp,
                out var alphaBlendOp);

            material.SetFloat(FogVolumeSrcColorBlendId, (float)srcColorBlend);
            material.SetFloat(FogVolumeDstColorBlendId, (float)dstColorBlend);
            material.SetFloat(FogVolumeSrcAlphaBlendId, (float)srcAlphaBlend);
            material.SetFloat(FogVolumeDstAlphaBlendId, (float)dstAlphaBlend);
            material.SetFloat(FogVolumeColorBlendOpId, (float)colorBlendOp);
            material.SetFloat(FogVolumeAlphaBlendOpId, (float)alphaBlendOp);
            material.SetFloat(FogVolumeBlendModeId, (float)mode);
        }

        private static void ComputeBlendParameters(
            VividLocalVolumetricFogBlendingMode mode,
            out BlendMode srcColorBlend,
            out BlendMode srcAlphaBlend,
            out BlendMode dstColorBlend,
            out BlendMode dstAlphaBlend,
            out BlendOp colorBlendOp,
            out BlendOp alphaBlendOp)
        {
            colorBlendOp = BlendOp.Add;
            alphaBlendOp = BlendOp.Add;

            switch (mode)
            {
                default:
                case VividLocalVolumetricFogBlendingMode.Additive:
                    srcColorBlend = BlendMode.One;
                    dstColorBlend = BlendMode.One;
                    srcAlphaBlend = BlendMode.One;
                    dstAlphaBlend = BlendMode.One;
                    break;
                case VividLocalVolumetricFogBlendingMode.Multiply:
                    srcColorBlend = BlendMode.DstColor;
                    dstColorBlend = BlendMode.Zero;
                    srcAlphaBlend = BlendMode.DstAlpha;
                    dstAlphaBlend = BlendMode.Zero;
                    break;
                case VividLocalVolumetricFogBlendingMode.Overwrite:
                    srcColorBlend = BlendMode.One;
                    dstColorBlend = BlendMode.Zero;
                    srcAlphaBlend = BlendMode.One;
                    dstAlphaBlend = BlendMode.Zero;
                    break;
                case VividLocalVolumetricFogBlendingMode.Max:
                    srcColorBlend = BlendMode.One;
                    dstColorBlend = BlendMode.One;
                    srcAlphaBlend = BlendMode.One;
                    dstAlphaBlend = BlendMode.One;
                    alphaBlendOp = BlendOp.Max;
                    colorBlendOp = BlendOp.Max;
                    break;
                case VividLocalVolumetricFogBlendingMode.Min:
                    srcColorBlend = BlendMode.One;
                    dstColorBlend = BlendMode.One;
                    srcAlphaBlend = BlendMode.One;
                    dstAlphaBlend = BlendMode.One;
                    alphaBlendOp = BlendOp.Min;
                    colorBlendOp = BlendOp.Min;
                    break;
            }
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
