using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime
{
    public static class HairGroomBinaryContract
    {
        // "VDHR" in little-endian ASCII.
        public const uint Magic = 0x52484456u;
        public const uint Version = 1u;
        public const int HeaderSize = 64;

        // Matches the 32-bit Tiled Visibility Atlas payload:
        // [31:20] 12-bit local linear depth, [19:0] 20-bit segment index.
        public const uint MaxSegmentsPerModel = 0xFFFFFu;
        public const uint MaxStrandsPerModel = 0xFFFFFu;
        public const uint MaxLocalSegmentIndex = 0xFFFu;
        public const uint VisibilityDepthBits = 12u;
        public const uint VisibilitySegmentBits = 20u;
    }

    [Flags]
    public enum HairGroomFlags : uint
    {
        None = 0u,
        StrandsRandomized = 1u << 0,
        HasBakedL1SH = 1u << 1,
        HasClumpData = 1u << 2,
    }

    /// <summary>
    /// Per-strand metadata uploaded directly to GPU buffers.
    /// Must remain bit-identical with VividHairGroomTypes.hlsl.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HairGroomStrandDesc
    {
        public const int Stride = 16;

        public uint FirstVertex;
        public uint VertexCount;
        public uint RootUVPacked;
        public uint ClumpAndSeed;

        public HairGroomStrandDesc(
            uint firstVertex,
            uint vertexCount,
            Vector2 rootUV,
            ushort clumpId,
            float randomSeed)
        {
            FirstVertex = firstVertex;
            VertexCount = vertexCount;
            RootUVPacked = HairGroomPacking.PackHalf2(rootUV);
            ClumpAndSeed = HairGroomPacking.PackClumpAndSeed(clumpId, randomSeed);
        }

        public Vector2 RootUV => HairGroomPacking.UnpackHalf2(RootUVPacked);
        public ushort ClumpId => (ushort)(ClumpAndSeed & 0xFFFFu);
        public float RandomSeed => HairGroomPacking.UnpackUnorm16((ushort)(ClumpAndSeed >> 16));
    }

    /// <summary>
    /// Per-vertex rest-pose strand data uploaded directly to GPU buffers.
    /// Must remain bit-identical with VividHairGroomTypes.hlsl.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HairGroomRestVertex
    {
        public const int Stride = 32;

        public Vector3 PositionOS;
        public float Radius;
        public Vector3 TangentOS;
        public uint PackedAttrs;

        public HairGroomRestVertex(
            Vector3 positionOS,
            float radius,
            Vector3 tangentOS,
            float strandU,
            float strandRandom)
        {
            PositionOS = positionOS;
            Radius = radius;
            TangentOS = tangentOS;
            PackedAttrs = HairGroomPacking.PackUnorm16Pair(strandU, strandRandom);
        }

        public float StrandU => HairGroomPacking.UnpackUnorm16((ushort)(PackedAttrs & 0xFFFFu));
        public float StrandRandom => HairGroomPacking.UnpackUnorm16((ushort)(PackedAttrs >> 16));
    }

    /// <summary>
    /// Per-segment index record linking a segment to its contiguous vertex pair
    /// (VertexIndex0, VertexIndex0 + 1) and owning strand.
    /// Must remain bit-identical with VividHairGroomTypes.hlsl.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HairGroomSegmentIndex
    {
        public const int Stride = 8;

        public uint VertexIndex0;
        public uint StrandAndLocalIndex;

        public HairGroomSegmentIndex(
            uint vertexIndex0,
            uint strandIndex,
            uint localSegmentIndex)
        {
            VertexIndex0 = vertexIndex0;
            StrandAndLocalIndex = HairGroomPacking.PackSegmentStrandLocal(
                strandIndex,
                localSegmentIndex);
        }

        public uint StrandIndex => HairGroomPacking.UnpackSegmentStrandIndex(StrandAndLocalIndex);
        public uint LocalSegmentIndex => HairGroomPacking.UnpackSegmentLocalIndex(StrandAndLocalIndex);
    }

    /// <summary>
    /// Per-vertex precomputed Biased L1 SH self-shadowing coefficients
    /// (IJGC SIGGRAPH 2025: average scattering event count with negative miss bias).
    /// Must remain bit-identical with VividHairGroomTypes.hlsl.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HairGroomVisibilitySH
    {
        public const int Stride = 16;

        public Vector4 Coefficients;

        public HairGroomVisibilitySH(Vector4 coefficients)
        {
            Coefficients = coefficients;
        }
    }

    /// <summary>
    /// CPU/GPU-matched IJGC SIGGRAPH 2025 Biased L1 Spherical Harmonics math helpers.
    /// Orthonormal basis ordered (Y00, Y1x, Y1y, Y1z) matching VividStrandHairCommon.hlsl.
    /// </summary>
    public static class HairGroomL1SH
    {
        public const float MaxScatterEvents = 5.0f;
        public const float ShL0 = 0.2820947918f; // 1 / (2 * sqrt(pi))
        public const float ShL1 = 0.4886025119f; // sqrt(3 / (4 * pi))

        public static Vector4 EvaluateBasis(Vector3 dir)
        {
            return new Vector4(ShL0, ShL1 * dir.x, ShL1 * dir.y, ShL1 * dir.z);
        }

        public static float EvaluateScatterEvents(Vector4 shCoeffsOS, Vector3 lightDirOS)
        {
            Vector4 basis = EvaluateBasis(lightDirOS);
            float n = Vector4.Dot(shCoeffsOS, basis);
            return Mathf.Clamp(n, 0.0f, MaxScatterEvents);
        }

        public static float EvaluateTransmittance(Vector4 shCoeffsOS, Vector3 lightDirOS, float extinctionPerEvent = 0.65f)
        {
            float n = EvaluateScatterEvents(shCoeffsOS, lightDirOS);
            return Mathf.Exp(-Mathf.Max(0.0f, extinctionPerEvent) * n);
        }
    }

    /// <summary>
    /// Fixed 64-byte header for .vividhair binary files.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HairGroomBinaryHeader
    {
        public const int Stride = HairGroomBinaryContract.HeaderSize;

        public uint Magic;
        public uint Version;
        public uint Flags;
        public uint StrandCount;
        public uint VertexCount;
        public uint SegmentCount;
        public Vector3 BoundsMin;
        public Vector3 BoundsMax;
        public float DefaultRadius;
        public uint Reserved0;
        public uint Reserved1;
        public uint Reserved2;

        public Bounds ToUnityBounds()
        {
            var center = (BoundsMin + BoundsMax) * 0.5f;
            var size = Vector3.Max(BoundsMax - BoundsMin, Vector3.one * 1e-4f);
            return new Bounds(center, size);
        }

        public void Validate()
        {
            if (Magic != HairGroomBinaryContract.Magic)
            {
                throw new InvalidDataException(
                    $"Invalid .vividhair magic 0x{Magic:X8}; expected 0x{HairGroomBinaryContract.Magic:X8}.");
            }

            if (Version != HairGroomBinaryContract.Version)
            {
                throw new InvalidDataException(
                    $"Unsupported .vividhair version {Version}; expected {HairGroomBinaryContract.Version}.");
            }

            if (StrandCount == 0 || StrandCount > HairGroomBinaryContract.MaxStrandsPerModel)
            {
                throw new InvalidDataException(
                    $"Invalid strand count {StrandCount} (max {HairGroomBinaryContract.MaxStrandsPerModel}).");
            }

            if (VertexCount < StrandCount * 2u)
            {
                throw new InvalidDataException(
                    $"Vertex count {VertexCount} is too small for {StrandCount} strands.");
            }

            if (SegmentCount == 0 || SegmentCount > HairGroomBinaryContract.MaxSegmentsPerModel)
            {
                throw new InvalidDataException(
                    $"Invalid segment count {SegmentCount} (max {HairGroomBinaryContract.MaxSegmentsPerModel}).");
            }

            if (!IsFinite(BoundsMin) || !IsFinite(BoundsMax)
                || BoundsMin.x > BoundsMax.x
                || BoundsMin.y > BoundsMax.y
                || BoundsMin.z > BoundsMax.z)
            {
                throw new InvalidDataException("Hair groom bounds are non-finite or inverted.");
            }

            if (float.IsNaN(DefaultRadius) || float.IsInfinity(DefaultRadius) || DefaultRadius <= 0.0f)
            {
                throw new InvalidDataException($"Invalid default strand radius {DefaultRadius}.");
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                   && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                   && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }

    public static class HairGroomPacking
    {
        public static ushort PackUnorm16(float value)
        {
            float clamped = Mathf.Clamp01(value);
            return (ushort)Mathf.RoundToInt(clamped * 65535.0f);
        }

        public static float UnpackUnorm16(ushort value)
        {
            return value * (1.0f / 65535.0f);
        }

        public static uint PackUnorm16Pair(float low, float high)
        {
            return (uint)PackUnorm16(low) | ((uint)PackUnorm16(high) << 16);
        }

        public static uint PackClumpAndSeed(ushort clumpId, float randomSeed)
        {
            return (uint)clumpId | ((uint)PackUnorm16(randomSeed) << 16);
        }

        public static uint PackHalf2(Vector2 uv)
        {
            uint x = math.f32tof16(uv.x) & 0xFFFFu;
            uint y = math.f32tof16(uv.y) & 0xFFFFu;
            return x | (y << 16);
        }

        public static Vector2 UnpackHalf2(uint packed)
        {
            float x = math.f16tof32(packed & 0xFFFFu);
            float y = math.f16tof32((packed >> 16) & 0xFFFFu);
            return new Vector2(x, y);
        }

        public static uint PackSegmentStrandLocal(uint strandIndex, uint localSegmentIndex)
        {
            if (strandIndex > HairGroomBinaryContract.MaxStrandsPerModel)
                throw new ArgumentOutOfRangeException(nameof(strandIndex));
            if (localSegmentIndex > HairGroomBinaryContract.MaxLocalSegmentIndex)
                throw new ArgumentOutOfRangeException(nameof(localSegmentIndex));

            return (strandIndex & HairGroomBinaryContract.MaxStrandsPerModel)
                   | ((localSegmentIndex & HairGroomBinaryContract.MaxLocalSegmentIndex) << 20);
        }

        public static uint UnpackSegmentStrandIndex(uint packed)
        {
            return packed & HairGroomBinaryContract.MaxStrandsPerModel;
        }

        public static uint UnpackSegmentLocalIndex(uint packed)
        {
            return (packed >> 20) & HairGroomBinaryContract.MaxLocalSegmentIndex;
        }

        public static uint PackVisibilityPayload(float normalizedLinearDepth, uint segmentId)
        {
            uint depth12 = (uint)Mathf.RoundToInt(Mathf.Clamp01(normalizedLinearDepth) * 4095.0f) & 0xFFFu;
            return (depth12 << 20) | (segmentId & HairGroomBinaryContract.MaxSegmentsPerModel);
        }

        public static float UnpackVisibilityDepth12(uint payload)
        {
            return ((payload >> 20) & 0xFFFu) * (1.0f / 4095.0f);
        }

        public static uint UnpackVisibilitySegmentId20(uint payload)
        {
            return payload & HairGroomBinaryContract.MaxSegmentsPerModel;
        }
    }

    public sealed class HairGroomParsedData
    {
        public HairGroomBinaryHeader Header;
        public HairGroomStrandDesc[] Strands;
        public HairGroomRestVertex[] Vertices;
        public HairGroomSegmentIndex[] Segments;
        public HairGroomVisibilitySH[] VisibilitySH;
    }

    public static class HairGroomBinaryCodec
    {
        public static byte[] WriteToBytes(
            HairGroomStrandDesc[] strands,
            HairGroomRestVertex[] vertices,
            HairGroomSegmentIndex[] segments,
            Bounds bounds,
            float defaultRadius,
            HairGroomFlags flags,
            HairGroomVisibilitySH[] visibilitySH = null)
        {
            if (strands == null || strands.Length == 0)
                throw new ArgumentException("Strands array must be non-empty.", nameof(strands));
            if (vertices == null || vertices.Length < strands.Length * 2)
                throw new ArgumentException("Vertices array is invalid.", nameof(vertices));
            if (segments == null || segments.Length == 0)
                throw new ArgumentException("Segments array must be non-empty.", nameof(segments));

            bool hasSH = visibilitySH != null && visibilitySH.Length == vertices.Length;
            if (hasSH)
                flags |= HairGroomFlags.HasBakedL1SH;
            else
                flags &= ~HairGroomFlags.HasBakedL1SH;

            var header = new HairGroomBinaryHeader
            {
                Magic = HairGroomBinaryContract.Magic,
                Version = HairGroomBinaryContract.Version,
                Flags = (uint)flags,
                StrandCount = (uint)strands.Length,
                VertexCount = (uint)vertices.Length,
                SegmentCount = (uint)segments.Length,
                BoundsMin = bounds.min,
                BoundsMax = bounds.max,
                DefaultRadius = defaultRadius,
                Reserved0 = 0u,
                Reserved1 = 0u,
                Reserved2 = 0u,
            };
            header.Validate();

            int totalSize = checked(
                HairGroomBinaryHeader.Stride
                + strands.Length * HairGroomStrandDesc.Stride
                + vertices.Length * HairGroomRestVertex.Stride
                + segments.Length * HairGroomSegmentIndex.Stride
                + (hasSH ? vertices.Length * HairGroomVisibilitySH.Stride : 0));

            using var stream = new MemoryStream(totalSize);
            using var writer = new BinaryWriter(stream);

            WriteHeader(writer, in header);
            for (int i = 0; i < strands.Length; i++)
            {
                ref readonly var s = ref strands[i];
                writer.Write(s.FirstVertex);
                writer.Write(s.VertexCount);
                writer.Write(s.RootUVPacked);
                writer.Write(s.ClumpAndSeed);
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                ref readonly var v = ref vertices[i];
                writer.Write(v.PositionOS.x);
                writer.Write(v.PositionOS.y);
                writer.Write(v.PositionOS.z);
                writer.Write(v.Radius);
                writer.Write(v.TangentOS.x);
                writer.Write(v.TangentOS.y);
                writer.Write(v.TangentOS.z);
                writer.Write(v.PackedAttrs);
            }

            for (int i = 0; i < segments.Length; i++)
            {
                ref readonly var seg = ref segments[i];
                writer.Write(seg.VertexIndex0);
                writer.Write(seg.StrandAndLocalIndex);
            }

            if (hasSH)
            {
                for (int i = 0; i < visibilitySH.Length; i++)
                {
                    ref readonly var sh = ref visibilitySH[i];
                    writer.Write(sh.Coefficients.x);
                    writer.Write(sh.Coefficients.y);
                    writer.Write(sh.Coefficients.z);
                    writer.Write(sh.Coefficients.w);
                }
            }

            return stream.ToArray();
        }

        public static HairGroomBinaryHeader ReadHeader(byte[] data)
        {
            if (data == null || data.Length < HairGroomBinaryHeader.Stride)
                throw new InvalidDataException("Binary buffer is too small to contain a .vividhair header.");

            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream);
            var header = ReadHeader(reader);
            header.Validate();
            return header;
        }

        public static HairGroomParsedData Read(byte[] data)
        {
            if (data == null || data.Length < HairGroomBinaryHeader.Stride)
                throw new InvalidDataException("Binary buffer is too small to contain a .vividhair file.");

            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream);
            var header = ReadHeader(reader);
            header.Validate();

            bool hasSH = ((HairGroomFlags)header.Flags & HairGroomFlags.HasBakedL1SH) != 0;
            long expectedBytes =
                HairGroomBinaryHeader.Stride
                + (long)header.StrandCount * HairGroomStrandDesc.Stride
                + (long)header.VertexCount * HairGroomRestVertex.Stride
                + (long)header.SegmentCount * HairGroomSegmentIndex.Stride
                + (hasSH ? (long)header.VertexCount * HairGroomVisibilitySH.Stride : 0L);

            if (data.Length < expectedBytes)
            {
                throw new InvalidDataException(
                    $"Truncated .vividhair payload: got {data.Length} bytes, expected at least {expectedBytes} bytes.");
            }

            var strands = new HairGroomStrandDesc[header.StrandCount];
            for (int i = 0; i < strands.Length; i++)
            {
                strands[i] = new HairGroomStrandDesc
                {
                    FirstVertex = reader.ReadUInt32(),
                    VertexCount = reader.ReadUInt32(),
                    RootUVPacked = reader.ReadUInt32(),
                    ClumpAndSeed = reader.ReadUInt32(),
                };
            }

            var vertices = new HairGroomRestVertex[header.VertexCount];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new HairGroomRestVertex
                {
                    PositionOS = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    Radius = reader.ReadSingle(),
                    TangentOS = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    PackedAttrs = reader.ReadUInt32(),
                };
            }

            var segments = new HairGroomSegmentIndex[header.SegmentCount];
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = new HairGroomSegmentIndex
                {
                    VertexIndex0 = reader.ReadUInt32(),
                    StrandAndLocalIndex = reader.ReadUInt32(),
                };
            }

            HairGroomVisibilitySH[] visibilitySH = null;
            if (hasSH)
            {
                visibilitySH = new HairGroomVisibilitySH[header.VertexCount];
                for (int i = 0; i < visibilitySH.Length; i++)
                {
                    visibilitySH[i] = new HairGroomVisibilitySH
                    {
                        Coefficients = new Vector4(
                            reader.ReadSingle(),
                            reader.ReadSingle(),
                            reader.ReadSingle(),
                            reader.ReadSingle()),
                    };
                }
            }

            ValidateTopology(in header, strands, segments);

            return new HairGroomParsedData
            {
                Header = header,
                Strands = strands,
                Vertices = vertices,
                Segments = segments,
                VisibilitySH = visibilitySH,
            };
        }

        private static void WriteHeader(BinaryWriter writer, in HairGroomBinaryHeader header)
        {
            writer.Write(header.Magic);
            writer.Write(header.Version);
            writer.Write(header.Flags);
            writer.Write(header.StrandCount);
            writer.Write(header.VertexCount);
            writer.Write(header.SegmentCount);
            writer.Write(header.BoundsMin.x);
            writer.Write(header.BoundsMin.y);
            writer.Write(header.BoundsMin.z);
            writer.Write(header.BoundsMax.x);
            writer.Write(header.BoundsMax.y);
            writer.Write(header.BoundsMax.z);
            writer.Write(header.DefaultRadius);
            writer.Write(header.Reserved0);
            writer.Write(header.Reserved1);
            writer.Write(header.Reserved2);
        }

        private static HairGroomBinaryHeader ReadHeader(BinaryReader reader)
        {
            return new HairGroomBinaryHeader
            {
                Magic = reader.ReadUInt32(),
                Version = reader.ReadUInt32(),
                Flags = reader.ReadUInt32(),
                StrandCount = reader.ReadUInt32(),
                VertexCount = reader.ReadUInt32(),
                SegmentCount = reader.ReadUInt32(),
                BoundsMin = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                BoundsMax = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                DefaultRadius = reader.ReadSingle(),
                Reserved0 = reader.ReadUInt32(),
                Reserved1 = reader.ReadUInt32(),
                Reserved2 = reader.ReadUInt32(),
            };
        }

        private static void ValidateTopology(
            in HairGroomBinaryHeader header,
            HairGroomStrandDesc[] strands,
            HairGroomSegmentIndex[] segments)
        {
            for (int i = 0; i < strands.Length; i++)
            {
                ref readonly var s = ref strands[i];
                if (s.VertexCount < 2u || (ulong)s.FirstVertex + s.VertexCount > header.VertexCount)
                {
                    throw new InvalidDataException(
                        $"Strand {i} has out-of-range vertex span [{s.FirstVertex}, +{s.VertexCount}) vs {header.VertexCount}.");
                }
            }

            for (int i = 0; i < segments.Length; i++)
            {
                ref readonly var seg = ref segments[i];
                if (seg.VertexIndex0 + 1u >= header.VertexCount)
                {
                    throw new InvalidDataException(
                        $"Segment {i} vertex index {seg.VertexIndex0} exceeds vertex count {header.VertexCount}.");
                }

                if (seg.StrandIndex >= header.StrandCount)
                {
                    throw new InvalidDataException(
                        $"Segment {i} strand index {seg.StrandIndex} exceeds strand count {header.StrandCount}.");
                }
            }
        }
    }
}
