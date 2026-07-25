using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    /// <summary>
    /// Stable identifier for a camera-relative history allocation.
    /// </summary>
    public readonly struct CameraHistoryId : IEquatable<CameraHistoryId>
    {
        private CameraHistoryId(int value, string name)
        {
            Value = value;
            Name = name;
        }

        public int Value { get; }

        public string Name { get; }

        public static CameraHistoryId Create(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Camera history names cannot be empty.", nameof(name));

            return new CameraHistoryId(Shader.PropertyToID(name), name);
        }

        public bool Equals(CameraHistoryId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is CameraHistoryId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public static bool operator ==(CameraHistoryId left, CameraHistoryId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(CameraHistoryId left, CameraHistoryId right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Built-in camera history usages. Custom subsystems can create their own stable IDs.
    /// </summary>
    public static class CameraHistoryIds
    {
        public static readonly CameraHistoryId AntialiasingTaa =
            CameraHistoryId.Create("AntialiasingTAAHistoryColor");

        public static readonly CameraHistoryId VolumetricLighting =
            CameraHistoryId.Create("VBufferLighting");

        public static readonly CameraHistoryId ColorPyramid =
            CameraHistoryId.Create("ColorPyramid");
    }

    /// <summary>
    /// RenderGraph-independent descriptor used to allocate persistent history textures.
    /// </summary>
    public readonly struct CameraHistoryTextureDescriptor : IEquatable<CameraHistoryTextureDescriptor>
    {
        public CameraHistoryTextureDescriptor(
            int width,
            int height,
            GraphicsFormat colorFormat,
            int slices = 1,
            TextureDimension dimension = TextureDimension.Tex2D,
            DepthBits depthBufferBits = DepthBits.None,
            MSAASamples msaaSamples = MSAASamples.None,
            FilterMode filterMode = FilterMode.Bilinear,
            TextureWrapMode wrapMode = TextureWrapMode.Clamp,
            int anisoLevel = 1,
            float mipMapBias = 0.0f,
            bool enableRandomWrite = false,
            bool useMipMap = false,
            bool autoGenerateMips = false,
            bool isShadowMap = false,
            bool bindTextureMS = false,
            bool useDynamicScale = false,
            bool useDynamicScaleExplicit = false)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            Slices = Mathf.Max(1, slices);
            Dimension = dimension;
            ColorFormat = colorFormat;
            DepthBufferBits = depthBufferBits;
            MsaaSamples = msaaSamples;
            FilterMode = filterMode;
            WrapMode = wrapMode;
            AnisoLevel = Mathf.Max(1, anisoLevel);
            MipMapBias = mipMapBias;
            EnableRandomWrite = enableRandomWrite;
            UseMipMap = useMipMap;
            AutoGenerateMips = autoGenerateMips;
            IsShadowMap = isShadowMap;
            BindTextureMS = bindTextureMS;
            UseDynamicScale = useDynamicScale;
            UseDynamicScaleExplicit = useDynamicScaleExplicit;
        }

        public int Width { get; }
        public int Height { get; }
        public int Slices { get; }
        public TextureDimension Dimension { get; }
        public GraphicsFormat ColorFormat { get; }
        public DepthBits DepthBufferBits { get; }
        public MSAASamples MsaaSamples { get; }
        public FilterMode FilterMode { get; }
        public TextureWrapMode WrapMode { get; }
        public int AnisoLevel { get; }
        public float MipMapBias { get; }
        public bool EnableRandomWrite { get; }
        public bool UseMipMap { get; }
        public bool AutoGenerateMips { get; }
        public bool IsShadowMap { get; }
        public bool BindTextureMS { get; }
        public bool UseDynamicScale { get; }
        public bool UseDynamicScaleExplicit { get; }

        public bool Equals(CameraHistoryTextureDescriptor other)
        {
            return Width == other.Width
                && Height == other.Height
                && Slices == other.Slices
                && Dimension == other.Dimension
                && ColorFormat == other.ColorFormat
                && DepthBufferBits == other.DepthBufferBits
                && MsaaSamples == other.MsaaSamples
                && FilterMode == other.FilterMode
                && WrapMode == other.WrapMode
                && AnisoLevel == other.AnisoLevel
                && MipMapBias.Equals(other.MipMapBias)
                && EnableRandomWrite == other.EnableRandomWrite
                && UseMipMap == other.UseMipMap
                && AutoGenerateMips == other.AutoGenerateMips
                && IsShadowMap == other.IsShadowMap
                && BindTextureMS == other.BindTextureMS
                && UseDynamicScale == other.UseDynamicScale
                && UseDynamicScaleExplicit == other.UseDynamicScaleExplicit;
        }

        public override bool Equals(object obj)
        {
            return obj is CameraHistoryTextureDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Width);
            hash.Add(Height);
            hash.Add(Slices);
            hash.Add(Dimension);
            hash.Add(ColorFormat);
            hash.Add(DepthBufferBits);
            hash.Add(MsaaSamples);
            hash.Add(FilterMode);
            hash.Add(WrapMode);
            hash.Add(AnisoLevel);
            hash.Add(MipMapBias);
            hash.Add(EnableRandomWrite);
            hash.Add(UseMipMap);
            hash.Add(AutoGenerateMips);
            hash.Add(IsShadowMap);
            hash.Add(BindTextureMS);
            hash.Add(UseDynamicScale);
            hash.Add(UseDynamicScaleExplicit);
            return hash.ToHashCode();
        }

        public static bool operator ==(
            CameraHistoryTextureDescriptor left,
            CameraHistoryTextureDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            CameraHistoryTextureDescriptor left,
            CameraHistoryTextureDescriptor right)
        {
            return !left.Equals(right);
        }
    }

    public delegate RTHandle CameraHistoryTextureAllocator(
        RTHandleSystem system,
        in CameraHistoryTextureDescriptor descriptor,
        string resourceName,
        int resourceIndex);

    /// <summary>
    /// A camera-relative persistent texture ring. Frame age 0 is the current write target,
    /// age 1 is the previous completed frame, and so on.
    /// </summary>
    public sealed class CameraHistoryTexture
    {
        private const int BufferId = 0;

        private readonly CameraHistory m_Owner;
        private readonly CameraHistoryId m_Id;
        private readonly int m_FrameCount;
        private readonly CameraHistoryTextureDescriptor m_Descriptor;
        private readonly CameraHistoryTextureAllocator m_Allocator;
        private readonly BufferedRTHandleSystem m_Storage = new();
        private int m_ValidHistoryCount;
        private long m_LastCommittedSequence = -1;
        private bool m_PendingWrite;
        private bool m_Disposed;

        internal CameraHistoryTexture(
            CameraHistory owner,
            CameraHistoryId id,
            int frameCount,
            in CameraHistoryTextureDescriptor descriptor,
            CameraHistoryTextureAllocator allocator)
        {
            m_Owner = owner;
            m_Id = id;
            m_FrameCount = Mathf.Max(1, frameCount);
            m_Descriptor = descriptor;
            m_Allocator = allocator;
            var resolvedAllocator = allocator ?? AllocateDefault;
            var allocationDescriptor = descriptor;
            m_Storage.AllocBuffer(
                BufferId,
                (system, resourceIndex) => resolvedAllocator(
                    system,
                    allocationDescriptor,
                    BuildResourceName(resourceIndex),
                    resourceIndex),
                m_FrameCount);
        }

        public CameraHistoryId Id => m_Id;

        public int FrameCount => m_FrameCount;

        public CameraHistoryTextureDescriptor Descriptor => m_Descriptor;

        public RTHandle GetCurrent()
        {
            return GetFrame(0);
        }

        public RTHandle GetPrevious()
        {
            return GetFrame(1);
        }

        public RTHandle GetFrame(int frameAge)
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(CameraHistoryTexture));
            if (frameAge < 0 || frameAge >= m_FrameCount)
                throw new ArgumentOutOfRangeException(nameof(frameAge));

            return m_Storage.GetFrameRT(BufferId, frameAge);
        }

        public bool IsValid(int frameAge = 1)
        {
            if (m_Disposed || frameAge < 0 || frameAge >= m_FrameCount)
                return false;
            if (m_LastCommittedSequence != m_Owner.CurrentSequence - 1)
                return false;

            return frameAge == 0
                ? m_FrameCount == 1 && m_ValidHistoryCount > 0
                : frameAge <= m_ValidHistoryCount;
        }

        /// <summary>
        /// Marks the current texture as written. It is promoted to history only when the
        /// owning camera frame is committed successfully.
        /// </summary>
        public void MarkWritten()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(CameraHistoryTexture));
            if (!m_Owner.IsFrameActive)
                throw new InvalidOperationException("Camera history writes must occur inside an active camera frame.");

            m_PendingWrite = true;
        }

        internal bool Matches(
            int frameCount,
            in CameraHistoryTextureDescriptor descriptor,
            CameraHistoryTextureAllocator allocator)
        {
            return m_FrameCount == Mathf.Max(1, frameCount)
                && m_Descriptor.Equals(descriptor)
                && Equals(m_Allocator, allocator);
        }

        internal void BeginFrame()
        {
            m_PendingWrite = false;
        }

        internal void CommitFrame(int referenceWidth, int referenceHeight, long sequence)
        {
            if (!m_PendingWrite)
                return;

            m_Storage.SwapAndSetReferenceSize(
                Mathf.Max(1, referenceWidth),
                Mathf.Max(1, referenceHeight));
            m_ValidHistoryCount = Mathf.Min(m_ValidHistoryCount + 1, Mathf.Max(1, m_FrameCount - 1));
            if (m_FrameCount == 1)
                m_ValidHistoryCount = 1;
            m_LastCommittedSequence = sequence;
            m_PendingWrite = false;
        }

        internal void AbortFrame()
        {
            m_PendingWrite = false;
        }

        internal void Dispose()
        {
            if (m_Disposed)
                return;

            m_Storage.Dispose();
            m_Disposed = true;
        }

        private string BuildResourceName(int resourceIndex)
        {
            var usageName = string.IsNullOrEmpty(m_Id.Name) ? $"History{m_Id.Value}" : m_Id.Name;
            return $"{usageName}_{m_Owner.CameraName}_{resourceIndex}";
        }

        private static RTHandle AllocateDefault(
            RTHandleSystem system,
            in CameraHistoryTextureDescriptor descriptor,
            string resourceName,
            int resourceIndex)
        {
            return system.Alloc(
                width: descriptor.Width,
                height: descriptor.Height,
                slices: descriptor.Slices,
                depthBufferBits: descriptor.DepthBufferBits,
                colorFormat: descriptor.ColorFormat,
                filterMode: descriptor.FilterMode,
                wrapMode: descriptor.WrapMode,
                dimension: descriptor.Dimension,
                enableRandomWrite: descriptor.EnableRandomWrite,
                useMipMap: descriptor.UseMipMap,
                autoGenerateMips: descriptor.AutoGenerateMips,
                isShadowMap: descriptor.IsShadowMap,
                anisoLevel: descriptor.AnisoLevel,
                mipMapBias: descriptor.MipMapBias,
                msaaSamples: descriptor.MsaaSamples,
                bindTextureMS: descriptor.BindTextureMS,
                useDynamicScale: descriptor.UseDynamicScale,
                useDynamicScaleExplicit: descriptor.UseDynamicScaleExplicit,
                name: resourceName);
        }
    }

    /// <summary>
    /// Camera-relative owner for persistent history resources.
    /// </summary>
    public sealed class CameraHistory : CameraRelativeState
    {
        private readonly Dictionary<CameraHistoryId, CameraHistoryTexture> m_Textures = new();
        private readonly Dictionary<CameraHistoryId, CameraHistoryBuffer> m_Buffers = new();
        private Camera m_Camera;
        private int m_ReferenceWidth = 1;
        private int m_ReferenceHeight = 1;
        private long m_CurrentSequence;
        private bool m_IsFrameActive;

        internal long CurrentSequence => m_CurrentSequence;

        internal bool IsFrameActive => m_IsFrameActive;

        internal string CameraName => m_Camera != null && !string.IsNullOrEmpty(m_Camera.name)
            ? m_Camera.name
            : "Camera";

        internal void SetCamera(Camera camera)
        {
            m_Camera = camera;
        }

        public void BeginFrame(int referenceWidth, int referenceHeight)
        {
            if (m_IsFrameActive)
                AbortFrame();

            m_ReferenceWidth = Mathf.Max(1, referenceWidth);
            m_ReferenceHeight = Mathf.Max(1, referenceHeight);
            m_CurrentSequence++;
            m_IsFrameActive = true;

            foreach (var texture in m_Textures.Values)
                texture.BeginFrame();
            foreach (var buffer in m_Buffers.Values)
                buffer.BeginFrame();
        }

        public CameraHistoryTexture GetOrCreateTexture(
            CameraHistoryId id,
            int frameCount,
            in CameraHistoryTextureDescriptor descriptor,
            CameraHistoryTextureAllocator allocator = null)
        {
            if (!m_IsFrameActive)
                throw new InvalidOperationException("BeginFrame must be called before allocating camera history.");

            if (m_Textures.TryGetValue(id, out var texture))
            {
                if (texture.Matches(frameCount, descriptor, allocator))
                    return texture;

                texture.Dispose();
                m_Textures.Remove(id);
            }

            texture = new CameraHistoryTexture(this, id, frameCount, descriptor, allocator);
            m_Textures.Add(id, texture);
            return texture;
        }

        public bool TryGetTexture(CameraHistoryId id, out CameraHistoryTexture texture)
        {
            return m_Textures.TryGetValue(id, out texture);
        }

        public CameraHistoryBuffer GetOrCreateBuffer(
            CameraHistoryId id,
            int frameCount,
            in CameraHistoryBufferDescriptor descriptor,
            CameraHistoryBufferAllocator allocator = null)
        {
            if (!m_IsFrameActive)
                throw new InvalidOperationException("BeginFrame must be called before allocating camera history.");

            var normalizedDescriptor = new CameraHistoryBufferDescriptor(
                descriptor.Count,
                descriptor.Stride,
                descriptor.Target,
                descriptor.UsageFlags);
            if (m_Buffers.TryGetValue(id, out var buffer))
            {
                if (buffer.Matches(frameCount, normalizedDescriptor, allocator))
                    return buffer;

                buffer.Dispose();
                m_Buffers.Remove(id);
            }

            buffer = new CameraHistoryBuffer(this, id, frameCount, normalizedDescriptor, allocator);
            m_Buffers.Add(id, buffer);
            return buffer;
        }

        public bool TryGetBuffer(CameraHistoryId id, out CameraHistoryBuffer buffer)
        {
            return m_Buffers.TryGetValue(id, out buffer);
        }

        public void CommitFrame()
        {
            if (!m_IsFrameActive)
                return;

            foreach (var texture in m_Textures.Values)
                texture.CommitFrame(m_ReferenceWidth, m_ReferenceHeight, m_CurrentSequence);
            foreach (var buffer in m_Buffers.Values)
                buffer.CommitFrame(m_CurrentSequence);

            m_IsFrameActive = false;
        }

        public void AbortFrame()
        {
            if (!m_IsFrameActive)
                return;

            foreach (var texture in m_Textures.Values)
                texture.AbortFrame();
            foreach (var buffer in m_Buffers.Values)
                buffer.AbortFrame();

            m_IsFrameActive = false;
        }

        public override void Dispose()
        {
            foreach (var texture in m_Textures.Values)
                texture.Dispose();
            foreach (var buffer in m_Buffers.Values)
                buffer.Dispose();

            m_Textures.Clear();
            m_Buffers.Clear();
            m_Camera = null;
            m_IsFrameActive = false;
        }
    }

    internal static class CameraHistorySystem
    {
        private static CameraRelativeSystem<CameraHistory> s_Histories = new();

        internal static CameraHistory GetOrCreate(Camera camera)
        {
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            s_Histories.PurgeDestroyedCameras();
            var history = s_Histories.GetOrCreateBase(camera);
            history.SetCamera(camera);
            return history;
        }

        internal static void Dispose()
        {
            s_Histories.Dispose();
            s_Histories = new CameraRelativeSystem<CameraHistory>();
        }
    }
}
