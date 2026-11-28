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

    public enum VividSlabDefaultTextureKind : byte
    {
        InheritFallback = 0,
        White = 1,
        Black = 2,
        Bump = 3,
        Gray = 4,
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
        public bool IsColor;

        [NonSerialized]
        private int m_CachedPropertyId;

        [NonSerialized]
        private int m_CachedBaseTexturePropertyId;

        [NonSerialized]
        private bool m_BaseTexturePropertyIdResolved;

        public int PropertyId
        {
            get
            {
                if (m_CachedPropertyId == 0 && !string.IsNullOrEmpty(PropertyName))
                    m_CachedPropertyId = Shader.PropertyToID(PropertyName);
                return m_CachedPropertyId;
            }
        }

        internal int BaseTexturePropertyId
        {
            get
            {
                if (!m_BaseTexturePropertyIdResolved)
                {
                    m_BaseTexturePropertyIdResolved = true;
                    if (!string.IsNullOrEmpty(PropertyName)
                        && PropertyName.Length > 3
                        && PropertyName.EndsWith("_ST", StringComparison.Ordinal))
                    {
                        m_CachedBaseTexturePropertyId = Shader.PropertyToID(
                            PropertyName.Substring(0, PropertyName.Length - 3));
                    }
                }
                return m_CachedBaseTexturePropertyId;
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
        public VividSlabDefaultTextureKind DefaultTexture;

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
        public const int MaxSupportedShellLayerCount = 16;

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
        private float m_WorldPositionOffsetPadding;

        [SerializeField]
        private string[] m_WorldPositionOffsetPaddingProperties = Array.Empty<string>();

        [SerializeField]
        private int m_ShellLayerCount = 1;

        [SerializeField]
        private int m_ShadowShellLayerCount = 1;

        [SerializeField]
        private uint m_SourceContentHash;

        [SerializeField]
        private bool m_HasImportErrors;

        [SerializeField]
        private string[] m_Diagnostics = Array.Empty<string>();

        [NonSerialized]
        private uint[] m_ScratchWords = Array.Empty<uint>();

        [NonSerialized]
        private int[] m_WpoPaddingPropertyIds;

        private static readonly int s_MaxWorldPositionOffsetPropertyId =
            Shader.PropertyToID("_MaxWorldPositionOffset");

        private static readonly int s_WpoMaxDisplacementPropertyId =
            Shader.PropertyToID("_WPOMaxDisplacement");

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

        public float WorldPositionOffsetPadding => Mathf.Max(0.0f, m_WorldPositionOffsetPadding);

        public IReadOnlyList<string> WorldPositionOffsetPaddingProperties =>
            m_WorldPositionOffsetPaddingProperties ?? Array.Empty<string>();

        public int ShellLayerCount => Mathf.Clamp(m_ShellLayerCount, 1, MaxSupportedShellLayerCount);

        public int ShadowShellLayerCount => Mathf.Clamp(
            m_ShadowShellLayerCount > 0 ? m_ShadowShellLayerCount : m_ShellLayerCount,
            1,
            ShellLayerCount);

        public bool RequiresProgrammableVisibilityRaster =>
            m_HasCustomCoverage || m_HasVertexOffset || m_ShellLayerCount > 1;

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
            bool hasVertexOffset = false,
            float worldPositionOffsetPadding = 0.0f,
            string[] worldPositionOffsetPaddingProperties = null,
            int shellLayerCount = 1,
            int shadowShellLayerCount = 0)
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
            m_WorldPositionOffsetPadding = float.IsFinite(worldPositionOffsetPadding)
                ? Mathf.Max(0.0f, worldPositionOffsetPadding)
                : 0.0f;
            m_WorldPositionOffsetPaddingProperties = worldPositionOffsetPaddingProperties ?? Array.Empty<string>();
            m_ShellLayerCount = Mathf.Clamp(shellLayerCount, 1, MaxSupportedShellLayerCount);
            m_ShadowShellLayerCount = shadowShellLayerCount > 0
                ? Mathf.Clamp(shadowShellLayerCount, 1, m_ShellLayerCount)
                : m_ShellLayerCount;
            m_WpoPaddingPropertyIds = null;
            m_SourceContentHash = sourceContentHash;
            m_HasImportErrors = hasImportErrors;
            m_Diagnostics = diagnostics ?? Array.Empty<string>();

            if (m_RasterShader != null)
                VividSlabProgramRegistry.Register(m_RasterShader, this);
        }

        /// <summary>
        /// Evaluates the conservative world-space WPO bounds padding (in meters) for a material
        /// using this Slab program. Allocates zero managed memory after first call.
        /// </summary>
        public float ResolveWorldPositionOffsetPadding(Material sourceMaterial)
        {
            float padding = float.IsFinite(m_WorldPositionOffsetPadding)
                ? Mathf.Max(0.0f, m_WorldPositionOffsetPadding)
                : 0.0f;

            string[] props = m_WorldPositionOffsetPaddingProperties;
            if (props != null && props.Length > 0)
            {
                if (m_WpoPaddingPropertyIds == null || m_WpoPaddingPropertyIds.Length != props.Length)
                {
                    m_WpoPaddingPropertyIds = new int[props.Length];
                    for (int i = 0; i < props.Length; i++)
                    {
                        m_WpoPaddingPropertyIds[i] = !string.IsNullOrEmpty(props[i])
                            ? Shader.PropertyToID(props[i])
                            : 0;
                    }
                }

                for (int i = 0; i < m_WpoPaddingPropertyIds.Length; i++)
                {
                    int propId = m_WpoPaddingPropertyIds[i];
                    if (propId == 0)
                        continue;

                    float value = 0.0f;
                    if (sourceMaterial != null && sourceMaterial.HasProperty(propId))
                    {
                        value = sourceMaterial.GetFloat(propId);
                    }
                    else
                    {
                        value = FindDefaultParameterScalar(propId);
                    }

                    if (float.IsFinite(value))
                        padding += Mathf.Abs(value);
                }
            }

            if (sourceMaterial != null)
            {
                if (sourceMaterial.HasProperty(s_MaxWorldPositionOffsetPropertyId))
                {
                    float overridePad = sourceMaterial.GetFloat(s_MaxWorldPositionOffsetPropertyId);
                    if (float.IsFinite(overridePad) && overridePad > padding)
                        padding = overridePad;
                }
                if (sourceMaterial.HasProperty(s_WpoMaxDisplacementPropertyId))
                {
                    float overridePad = sourceMaterial.GetFloat(s_WpoMaxDisplacementPropertyId);
                    if (float.IsFinite(overridePad) && overridePad > padding)
                        padding = overridePad;
                }
            }

            return float.IsFinite(padding) ? Mathf.Max(0.0f, padding) : 0.0f;
        }

        public static float ResolveMaterialWorldPositionOffsetPadding(Material sourceMaterial)
        {
            if (sourceMaterial == null)
                return 0.0f;

            if (sourceMaterial.shader != null
                && VividSlabProgramRegistry.TryGetDescriptor(
                    sourceMaterial.shader,
                    out VividSlabProgramDescriptor descriptor)
                && descriptor != null)
            {
                return descriptor.ResolveWorldPositionOffsetPadding(sourceMaterial);
            }

            float padding = 0.0f;
            if (sourceMaterial.HasProperty(s_MaxWorldPositionOffsetPropertyId))
            {
                float v = sourceMaterial.GetFloat(s_MaxWorldPositionOffsetPropertyId);
                if (float.IsFinite(v) && v > padding)
                    padding = v;
            }
            if (sourceMaterial.HasProperty(s_WpoMaxDisplacementPropertyId))
            {
                float v = sourceMaterial.GetFloat(s_WpoMaxDisplacementPropertyId);
                if (float.IsFinite(v) && v > padding)
                    padding = v;
            }
            return padding;
        }

        private float FindDefaultParameterScalar(int propId)
        {
            if (m_Parameters == null)
                return 0.0f;

            for (int i = 0; i < m_Parameters.Length; i++)
            {
                if (m_Parameters[i].PropertyId == propId)
                    return m_Parameters[i].DefaultValue.x;
            }
            return 0.0f;
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

            bool convertColorToLinear = slot.IsColor && QualitySettings.activeColorSpace == ColorSpace.Linear;

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
                            if (convertColorToLinear)
                            {
                                Color lin = sourceMaterial.GetColor(propId).linear;
                                return new float4(lin.r, lin.g, lin.b, lin.a);
                            }

                            Vector4 vec = sourceMaterial.GetVector(propId);
                            return new float4(vec.x, vec.y, vec.z, vec.w);
                        }
                    }
                }
                else if (slot.Kind == VividSlabParameterKind.Float4)
                {
                    int baseTexId = slot.BaseTexturePropertyId;
                    if (baseTexId != 0 && sourceMaterial.HasProperty(baseTexId))
                    {
                        Vector2 scale = sourceMaterial.GetTextureScale(baseTexId);
                        Vector2 offset = sourceMaterial.GetTextureOffset(baseTexId);
                        return new float4(scale.x, scale.y, offset.x, offset.y);
                    }
                }
            }

            if (convertColorToLinear)
            {
                Color linDefault = new Color(
                    slot.DefaultValue.x,
                    slot.DefaultValue.y,
                    slot.DefaultValue.z,
                    slot.DefaultValue.w).linear;
                return new float4(linDefault.r, linDefault.g, linDefault.b, linDefault.a);
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
