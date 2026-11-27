using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven
{
    public enum VividSlabParameterKind : byte
    {
        Bool = 0,
        Float = 1,
        Float2 = 2,
        Float3 = 3,
        Float4 = 4,
    }

    public enum VividSlabTextureSampleKind : byte
    {
        Color = 0,
        Normal = 1,
        Mask = 2,
        Raw = 3,
    }

    [Serializable]
    public struct VividSlabParameterSlot
    {
        public string Symbol;
        public string PropertyName;
        public VividSlabParameterKind Kind;
        public int WordOffset;
        public int WordCount;
        public Vector4 DefaultValue;

        [NonSerialized]
        private int m_CachedPropertyId;

        public int PropertyId
        {
            get
            {
                if (m_CachedPropertyId == 0 && !string.IsNullOrEmpty(PropertyName))
                    m_CachedPropertyId = Shader.PropertyToID(PropertyName);
                return m_CachedPropertyId;
            }
        }
    }

    [Serializable]
    public struct VividSlabResourceSlot
    {
        public string Symbol;
        public string PropertyName;
        public VividSlabTextureSampleKind SampleKind;
        public int SlotIndex;

        [NonSerialized]
        private int m_CachedPropertyId;

        public int PropertyId
        {
            get
            {
                if (m_CachedPropertyId == 0 && !string.IsNullOrEmpty(PropertyName))
                    m_CachedPropertyId = Shader.PropertyToID(PropertyName);
                return m_CachedPropertyId;
            }
        }
    }

    /// <summary>
    /// Runtime metadata SubAsset produced by <c>VividSlabShaderImporter</c> alongside a
    /// <c>*.slab.shader</c>'s main <see cref="Shader"/> and standalone GPUDriven <see cref="ComputeShader"/>.
    /// </summary>
    public sealed class VividSlabProgramDescriptor : ScriptableObject
    {
        public const string DefaultResolveKernelName = "ResolveVisibilityBufferSlab";

        [SerializeField]
        private Shader m_RasterShader;

        [SerializeField]
        private ComputeShader m_ResolveComputeShader;

        [SerializeField]
        private string m_ResolveKernelName = DefaultResolveKernelName;

        [SerializeField]
        private string m_ShaderName = string.Empty;

        [SerializeField]
        private string m_BaseTemplateAssetPath = string.Empty;

        [SerializeField]
        private string m_LinkedGraphAssetPath = string.Empty;

        [SerializeField]
        private int m_EstimatedSubstrateBytesPerPixel =
            VividSubstrateContract.OpenPBROpaqueSingleTotalSlices * VividSubstrateContract.BytesPerSlice;

        [SerializeField]
        private int m_ParameterStrideInWords;

        [SerializeField]
        private VividSlabParameterSlot[] m_Parameters = Array.Empty<VividSlabParameterSlot>();

        [SerializeField]
        private VividSlabResourceSlot[] m_Resources = Array.Empty<VividSlabResourceSlot>();

        [SerializeField]
        private bool m_HasLinkedGraphSurface;

        [SerializeField]
        private bool m_HasCustomSurfaceHook;

        [SerializeField]
        private bool m_IsMultiClosure;

        [SerializeField]
        private int m_ClosureCount = 1;

        [SerializeField]
        private bool m_HasCustomCoverage;

        [SerializeField]
        private bool m_HasVertexOffset;

        [SerializeField]
        private uint m_SourceContentHash;

        [SerializeField]
        private bool m_HasImportErrors;

        [SerializeField]
        private string[] m_Diagnostics = Array.Empty<string>();

        [NonSerialized]
        private uint[] m_ScratchWords = Array.Empty<uint>();

        public Shader RasterShader => m_RasterShader;

        public ComputeShader ResolveComputeShader => m_ResolveComputeShader;

        public string ResolveKernelName => string.IsNullOrEmpty(m_ResolveKernelName)
            ? DefaultResolveKernelName
            : m_ResolveKernelName;

        public string ShaderName => m_ShaderName;

        public string BaseTemplateAssetPath => m_BaseTemplateAssetPath;

        public string LinkedGraphAssetPath => m_LinkedGraphAssetPath;

        public int EstimatedSubstrateBytesPerPixel => m_EstimatedSubstrateBytesPerPixel;

        public int ParameterStrideInWords => m_ParameterStrideInWords;

        public int ParameterLaneCount => (m_ParameterStrideInWords + 3) / 4;

        public IReadOnlyList<VividSlabParameterSlot> Parameters =>
            m_Parameters ?? Array.Empty<VividSlabParameterSlot>();

        public IReadOnlyList<VividSlabResourceSlot> Resources =>
            m_Resources ?? Array.Empty<VividSlabResourceSlot>();

        public int ResourceCount => m_Resources != null ? m_Resources.Length : 0;

        public bool HasLinkedGraphSurface => m_HasLinkedGraphSurface;

        public bool HasCustomSurfaceHook => m_HasCustomSurfaceHook;

        public bool IsMultiClosure => m_IsMultiClosure;

        public int ClosureCount => Mathf.Max(1, m_ClosureCount);

        public bool HasCustomCoverage => m_HasCustomCoverage;

        public bool HasVertexOffset => m_HasVertexOffset;

        public bool RequiresProgrammableVisibilityRaster =>
            m_HasCustomCoverage || m_HasVertexOffset;

        public uint SourceContentHash => m_SourceContentHash;

        public bool HasImportErrors => m_HasImportErrors;

        public string[] Diagnostics => m_Diagnostics ?? Array.Empty<string>();

        public bool IsGPUDrivenResolveReady =>
            !m_HasImportErrors && m_ResolveComputeShader != null;

        private void OnEnable()
        {
            if (m_RasterShader != null)
                VividSlabProgramRegistry.Register(m_RasterShader, this);
        }

        internal void Apply(
            Shader rasterShader,
            ComputeShader resolveComputeShader,
            string resolveKernelName,
            string shaderName,
            string baseTemplateAssetPath,
            string linkedGraphAssetPath,
            int estimatedSubstrateBytesPerPixel,
            int parameterStrideInWords,
            VividSlabParameterSlot[] parameters,
            VividSlabResourceSlot[] resources,
            bool hasLinkedGraphSurface,
            bool hasCustomSurfaceHook,
            uint sourceContentHash,
            bool hasImportErrors,
            string[] diagnostics,
            bool isMultiClosure = false,
            int closureCount = 1,
            bool hasCustomCoverage = false,
            bool hasVertexOffset = false)
        {
            m_RasterShader = rasterShader;
            m_ResolveComputeShader = resolveComputeShader;
            m_ResolveKernelName = string.IsNullOrEmpty(resolveKernelName)
                ? DefaultResolveKernelName
                : resolveKernelName;
            m_ShaderName = shaderName ?? string.Empty;
            m_BaseTemplateAssetPath = baseTemplateAssetPath ?? string.Empty;
            m_LinkedGraphAssetPath = linkedGraphAssetPath ?? string.Empty;
            m_EstimatedSubstrateBytesPerPixel = Mathf.Max(
                VividSubstrateContract.OpenPBROpaqueSingleTotalSlices * VividSubstrateContract.BytesPerSlice,
                estimatedSubstrateBytesPerPixel);
            m_ParameterStrideInWords = Mathf.Max(0, ((parameterStrideInWords + 3) / 4) * 4);
            m_Parameters = parameters ?? Array.Empty<VividSlabParameterSlot>();
            m_Resources = resources ?? Array.Empty<VividSlabResourceSlot>();
            m_HasLinkedGraphSurface = hasLinkedGraphSurface;
            m_HasCustomSurfaceHook = hasCustomSurfaceHook;
            m_IsMultiClosure = isMultiClosure;
            m_ClosureCount = Mathf.Clamp(closureCount, 1, VividSubstrateContract.MaxClosuresPerPixel);
            m_HasCustomCoverage = hasCustomCoverage;
            m_HasVertexOffset = hasVertexOffset;
            m_SourceContentHash = sourceContentHash;
            m_HasImportErrors = hasImportErrors;
            m_Diagnostics = diagnostics ?? Array.Empty<string>();

            if (m_RasterShader != null)
                VividSlabProgramRegistry.Register(m_RasterShader, this);
        }

        /// <summary>
        /// Zero-allocation steady-state packing of a Unity <see cref="Material"/> (and optional
        /// <see cref="GPUDrivenMaterialProxy"/> overrides) into GPU <c>_MaterialParameterData</c> lanes.
        /// </summary>
        internal void PackParameterLanes(
            Material sourceMaterial,
            GPUDrivenMaterialProxy overrideProxy,
            List<uint4> outputLanes)
        {
            if (outputLanes == null)
                throw new ArgumentNullException(nameof(outputLanes));

            int laneCount = ParameterLaneCount;
            if (laneCount == 0)
                return;

            int wordCapacity = laneCount * 4;
            if (m_ScratchWords == null || m_ScratchWords.Length < wordCapacity)
                m_ScratchWords = new uint[wordCapacity];
            Array.Clear(m_ScratchWords, 0, wordCapacity);

            if (m_Parameters != null)
            {
                for (int i = 0; i < m_Parameters.Length; i++)
                {
                    VividSlabParameterSlot slot = m_Parameters[i];
                    float4 val = ResolveSlotValue(ref m_Parameters[i], sourceMaterial, overrideProxy);
                    uint4 bits = math.asuint(val);
                    int count = Mathf.Min(slot.WordCount, 4);
                    for (int w = 0; w < count; w++)
                    {
                        int idx = slot.WordOffset + w;
                        if ((uint)idx < (uint)wordCapacity)
                            m_ScratchWords[idx] = bits[w];
                    }
                }
            }

            for (int lane = 0; lane < laneCount; lane++)
            {
                int o = lane * 4;
                outputLanes.Add(new uint4(
                    m_ScratchWords[o],
                    m_ScratchWords[o + 1],
                    m_ScratchWords[o + 2],
                    m_ScratchWords[o + 3]));
            }
        }

        private static float4 ResolveSlotValue(
            ref VividSlabParameterSlot slot,
            Material sourceMaterial,
            GPUDrivenMaterialProxy overrideProxy)
        {
            if (overrideProxy != null && !string.IsNullOrEmpty(slot.Symbol))
            {
                MaterialValueType valueType = ToMaterialValueType(slot.Kind);
                var decl = new MaterialParameterDeclaration(slot.Symbol, valueType);
                if (overrideProxy.TryGetParameterOverride(in decl, out Vector4 overrideVal))
                    return new float4(overrideVal.x, overrideVal.y, overrideVal.z, overrideVal.w);
            }

            if (sourceMaterial != null)
            {
                int propId = slot.PropertyId;
                if (propId != 0 && sourceMaterial.HasProperty(propId))
                {
                    switch (slot.Kind)
                    {
                        case VividSlabParameterKind.Bool:
                        case VividSlabParameterKind.Float:
                            return new float4(sourceMaterial.GetFloat(propId), 0.0f, 0.0f, 0.0f);
                        case VividSlabParameterKind.Float2:
                        case VividSlabParameterKind.Float3:
                        case VividSlabParameterKind.Float4:
                        {
                            Vector4 vec = sourceMaterial.GetVector(propId);
                            return new float4(vec.x, vec.y, vec.z, vec.w);
                        }
                    }
                }
            }

            return new float4(
                slot.DefaultValue.x,
                slot.DefaultValue.y,
                slot.DefaultValue.z,
                slot.DefaultValue.w);
        }

        internal static MaterialValueType ToMaterialValueType(VividSlabParameterKind kind)
        {
            return kind switch
            {
                VividSlabParameterKind.Bool => MaterialValueType.Bool,
                VividSlabParameterKind.Float => MaterialValueType.Float,
                VividSlabParameterKind.Float2 => MaterialValueType.Float2,
                VividSlabParameterKind.Float3 => MaterialValueType.Float3,
                _ => MaterialValueType.Float4,
            };
        }

        internal static VividSlabParameterKind FromMaterialValueType(MaterialValueType type)
        {
            return type switch
            {
                MaterialValueType.Bool => VividSlabParameterKind.Bool,
                MaterialValueType.Float => VividSlabParameterKind.Float,
                MaterialValueType.Float2 => VividSlabParameterKind.Float2,
                MaterialValueType.Float3 => VividSlabParameterKind.Float3,
                _ => VividSlabParameterKind.Float4,
            };
        }

        internal static VividSlabTextureSampleKind FromSampleClass(MaterialTextureSampleClass sampleClass)
        {
            return sampleClass switch
            {
                MaterialTextureSampleClass.Normal => VividSlabTextureSampleKind.Normal,
                MaterialTextureSampleClass.Mask => VividSlabTextureSampleKind.Mask,
                MaterialTextureSampleClass.Raw => VividSlabTextureSampleKind.Raw,
                _ => VividSlabTextureSampleKind.Color,
            };
        }
    }

    /// <summary>
    /// Fast zero-allocation lookup table mapping a <c>*.slab.shader</c>'s <see cref="Shader"/>
    /// to its <see cref="VividSlabProgramDescriptor"/> SubAsset.
    /// </summary>
    public static class VividSlabProgramRegistry
    {
        private static readonly Dictionary<Shader, VividSlabProgramDescriptor> s_ByShader = new();
        private static Func<Shader, VividSlabProgramDescriptor> s_EditorResolver;

        public static void Register(Shader shader, VividSlabProgramDescriptor descriptor)
        {
            if (shader == null)
                return;
            if (descriptor == null)
                s_ByShader.Remove(shader);
            else
                s_ByShader[shader] = descriptor;
        }

        public static void Unregister(Shader shader)
        {
            if (shader != null)
                s_ByShader.Remove(shader);
        }

        internal static void SetEditorResolver(Func<Shader, VividSlabProgramDescriptor> resolver)
        {
            s_EditorResolver = resolver;
        }

        public static bool TryGetDescriptor(Shader shader, out VividSlabProgramDescriptor descriptor)
        {
            descriptor = null;
            if (shader == null)
                return false;

            if (s_ByShader.TryGetValue(shader, out descriptor))
            {
                if (descriptor != null)
                    return true;
                s_ByShader.Remove(shader);
            }

            if (s_EditorResolver != null)
            {
                descriptor = s_EditorResolver(shader);
                if (descriptor != null)
                {
                    s_ByShader[shader] = descriptor;
                    return true;
                }
            }

            return false;
        }

        internal static void ClearForTests()
        {
            s_ByShader.Clear();
        }
    }
}
