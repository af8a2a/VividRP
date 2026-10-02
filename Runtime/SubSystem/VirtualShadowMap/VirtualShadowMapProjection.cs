using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;

namespace VividRP.Runtime.VirtualShadowMap
{
    // GPU ABI: two matrices followed by two float4s. No fixed CSM array in consumers.
    [StructLayout(LayoutKind.Sequential)]
    internal struct VirtualShadowMapProjection
    {
        internal Matrix4x4 WorldToClip;
        internal Matrix4x4 WorldToShadow;
        internal Vector4 SelectionSphere;
        // World units per virtual texel, receiver normal bias, GPU performance LOD bias, max distance.
        internal Vector4 Parameters;
    }

    internal sealed class VirtualShadowMapProjectionSet : IDisposable
    {
        internal static readonly int BufferId = Shader.PropertyToID("_VSMProjections");
        internal static readonly int CountId = Shader.PropertyToID("_VSMProjectionCount");
        internal static readonly int RemapId = Shader.PropertyToID("_VSMProjectionRemap");
        internal const int MaxVirtualResolution = 16384;
        internal const int UnityRasterTileSize = 2048;
        private VirtualShadowMapProjection[] m_Upload;
        private int4[] m_Remap;
        private int2[] m_PageOffsets;
        internal static readonly int PageOffsetsId = Shader.PropertyToID("_VSMClipmapPageOffsets");
        internal GraphicsBuffer PageOffsetsBuffer { get; private set; }
        private readonly long[] m_RecordedOriginX = new long[VirtualShadowMapClipmapLayout.MaxLevels];
        private readonly long[] m_RecordedOriginY = new long[VirtualShadowMapClipmapLayout.MaxLevels];
        private VirtualShadowMapClipmapLayout m_Layout;
        private bool m_HasRecordedLayout;
        private ulong m_RecordedCamera, m_RecordedLight, m_RecordedGeneration;
        private Quaternion m_RecordedRotation;
        private int m_RecordedResolution, m_RecordedFirstLevel, m_RecordedCount;
        private readonly float[] m_RecordedDepthMin = new float[VirtualShadowMapClipmapLayout.MaxLevels];
        private readonly float[] m_RecordedDepthMax = new float[VirtualShadowMapClipmapLayout.MaxLevels];
        internal GraphicsBuffer Buffer { get; private set; }
        internal GraphicsBuffer RemapBuffer { get; private set; }
        internal int Count { get; private set; }
        internal ulong Generation { get; private set; }
        internal bool RequiresRemap { get; private set; }
        internal bool RequiresFeedbackReset { get; private set; }
        internal bool LayoutRecorded { get; private set; }

        internal void EnsureCapacity(int count)
        {
            count = Mathf.Max(1, count);
            if (Buffer != null && Buffer.IsValid() && Buffer.count >= count)
                return;
            Buffer?.Dispose();
            RemapBuffer?.Dispose();
            PageOffsetsBuffer?.Dispose();
            m_Upload = new VirtualShadowMapProjection[count];
            m_Remap = new int4[count];
            m_PageOffsets = new int2[count * count];
            PageOffsetsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count * count, 8)
            { name = "VSMClipmapPageOffsets" };
            Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 160);
            Buffer.name = "VSMProjections";
            RemapBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
            RemapBuffer.name = "VSMProjectionRemap";
        }

        internal void Upload(CommandBuffer cmd)
        {
            if (Count > 0)
            {
                cmd.SetBufferData(Buffer, m_Upload, 0, 0, Count);
                if (m_Layout != null)
                {
                    cmd.SetBufferData(RemapBuffer, m_Remap, 0, 0, Count);
                    cmd.SetBufferData(PageOffsetsBuffer, m_PageOffsets, 0, 0, Count * Count);
                }
            }
        }

        internal void PrepareClipmaps(VirtualShadowMapClipmapLayout layout)
        {
            EnsureCapacity(layout.Count);
            m_Layout = layout;
            Count = layout.Count;
            bool sameBasis = m_HasRecordedLayout && m_RecordedCamera == layout.CameraId
                && m_RecordedLight == layout.LightId && m_RecordedRotation.Equals(layout.Rotation)
                && m_RecordedResolution == layout.Resolution && m_RecordedCount == layout.Count
                && m_RecordedFirstLevel == layout.FirstLevel;
            Generation = sameBasis ? m_RecordedGeneration : m_RecordedGeneration + 1;
            RequiresRemap = !sameBasis;
            RequiresFeedbackReset = !sameBasis;
            LayoutRecorded = false;
            int pages = layout.Resolution / VirtualShadowMapPrototypeRuntime.PageSize;
            for (int i = 0; i < Count; i++)
            {
                bool sameDepth = sameBasis && m_RecordedDepthMin[i] == layout.DepthMins[i]
                    && m_RecordedDepthMax[i] == layout.DepthMaxs[i];
                int dx = sameDepth ? ClampPageDelta(layout.OriginX[i] - m_RecordedOriginX[i], pages) : 0;
                int dy = sameDepth ? ClampPageDelta(layout.OriginY[i] - m_RecordedOriginY[i], pages) : 0;
                m_Remap[i] = new int4(dx, dy, sameDepth ? 0 : 1, 0);
                // Integer relative origins avoid cancellation of large world-space floats.
                // Both axes of WorldToShadow use the layout's positive light-space axes.
                for (int target = i; target < Count; target++)
                {
                    long scale = 1L << (target - i);
                    m_PageOffsets[i * Count + target] = new int2(
                        checked((int)(layout.OriginX[i] - layout.OriginX[target] * scale)),
                        checked((int)(layout.OriginY[i] - layout.OriginY[target] * scale)));
                }
                RequiresRemap |= !sameDepth || dx != 0 || dy != 0;
                RequiresFeedbackReset |= Math.Abs(dx) >= pages || Math.Abs(dy) >= pages;
                Vector3 center = layout.CameraPosition;
                m_Upload[i] = new VirtualShadowMapProjection
                {
                    WorldToClip = GL.GetGPUProjectionMatrix(layout.Projections[i], true) * layout.Views[i],
                    WorldToShadow = VividShadowData.BuildWorldToShadowMatrix(layout.Projections[i], layout.Views[i]),
                    SelectionSphere = new Vector4(center.x, center.y, center.z, -layout.Radii[i]),
                    Parameters = new Vector4(2 * layout.Radii[i] / layout.Resolution,
                        layout.NormalBias, 0, layout.MaxDistance)
                };
            }
        }

        internal static int ClampPageDelta(long delta, int pages)
            => (int)Math.Max(-pages, Math.Min(pages, delta));

        // Called only after the commands moving both residency and feedback have
        // been recorded. Merely preparing a camera must not advance this snapshot.
        internal void CommitRecordedLayout()
        {
            if (m_Layout == null)
                return;
            m_RecordedCamera = m_Layout.CameraId;
            m_RecordedLight = m_Layout.LightId;
            m_RecordedRotation = m_Layout.Rotation;
            m_RecordedResolution = m_Layout.Resolution;
            m_RecordedFirstLevel = m_Layout.FirstLevel;
            m_RecordedCount = m_Layout.Count;
            Array.Copy(m_Layout.DepthMins, m_RecordedDepthMin, Count);
            Array.Copy(m_Layout.DepthMaxs, m_RecordedDepthMax, Count);
            m_RecordedGeneration = Generation;
            Array.Copy(m_Layout.OriginX, m_RecordedOriginX, Count);
            Array.Copy(m_Layout.OriginY, m_RecordedOriginY, Count);
            m_HasRecordedLayout = true;
            LayoutRecorded = true;
        }

        internal void InvalidateLayout() => m_HasRecordedLayout = false;

        internal Matrix4x4 GetRasterMatrix(int projectionIndex, int resolution,
            int tileSize, int originX, int originY, bool uvStartsAtTop)
        {
            return RasterTransform(resolution, tileSize, originX, originY, uvStartsAtTop)
                * m_Upload[projectionIndex].WorldToClip;
        }

        internal static Matrix4x4 RasterTransform(int resolution, int tileSize,
            int originX, int originY, bool uvStartsAtTop)
        {
            Matrix4x4 transform = Matrix4x4.identity;
            transform.m00 = transform.m11 = (float)resolution / tileSize;
            transform.m03 = (float)(resolution - 2 * originX - tileSize) / tileSize;
            transform.m13 = (float)(resolution - 2 * originY - tileSize) / tileSize
                * (uvStartsAtTop ? -1.0f : 1.0f);
            return transform;
        }

        internal static int ResolveResolution(int requested, int csmResolution)
        {
            int resolution = Mathf.Clamp(requested > 0 ? requested : csmResolution,
                VirtualShadowMapClipmapLayout.MinResolution, MaxVirtualResolution);
            return Mathf.NextPowerOfTwo(resolution);
        }

        internal void Reset()
        {
            Count = 0;
            m_Layout = null;
            LayoutRecorded = false;
        }

        public void Dispose()
        {
            Buffer?.Dispose();
            Buffer = null;
            RemapBuffer?.Dispose();
            PageOffsetsBuffer?.Dispose();
            RemapBuffer = null;
            PageOffsetsBuffer = null;
            m_PageOffsets = null;
            m_Upload = null;
            m_Remap = null;
            m_HasRecordedLayout = false;
            m_Layout = null;
            LayoutRecorded = false;
            Count = 0;
        }
    }
}
