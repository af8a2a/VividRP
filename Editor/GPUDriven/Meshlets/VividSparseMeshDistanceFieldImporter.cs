using System;
using System.IO;
using System.Text;
using UnityEditor.AssetImporters;
using UnityEngine;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.GPUDriven.Meshlets
{
    [ScriptedImporter(1, "vsdf")]
    internal sealed class VividSparseMeshDistanceFieldImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<VividSparseMeshDistanceField>();
            try
            {
                using var stream = File.OpenRead(ctx.assetPath);
                using var reader = new BinaryReader(stream);
                Read(reader, asset);
                asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
                ctx.AddObjectToAsset("SparseDistanceField", asset);
                ctx.SetMainObject(asset);
            }
            catch
            {
                DestroyImmediate(asset);
                throw;
            }
        }

        internal static void Read(BinaryReader reader, VividSparseMeshDistanceField asset)
        {
            if (reader.ReadUInt32() != 0x46445356 || reader.ReadUInt32() != 1 || reader.ReadUInt32() != 3)
                throw new InvalidDataException("Unsupported sparse distance field header.");
            uint flags = reader.ReadUInt32();
            int length = reader.ReadInt32();
            if (flags > 1 || length < 1 || length > 65535)
                throw new InvalidDataException("Invalid sparse distance field identity.");
            asset.MostlyTwoSided = flags != 0;
            asset.SourceObjectPath = new UTF8Encoding(false, true).GetString(ReadBytes(reader, length));
            asset.GeometryBoundsCm = ReadBounds(reader);
            asset.FieldBoundsCm = ReadBounds(reader);
            var extents = asset.FieldBoundsCm.extents;
            if (Mathf.Min(extents.x, extents.y, extents.z) <= 0f)
                throw new InvalidDataException("Empty distance field volume.");
            var mips = new VividSparseMeshDistanceField.Mip[3];
            for (int i = 0; i < mips.Length; ++i)
            {
                ref var mip = ref mips[i];
                var dims = new Vector3Int(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                int bricks = reader.ReadInt32();
                if (dims.x <= 0 || dims.y <= 0 || dims.z <= 0 || dims.x > 1023 || dims.y > 1023 || dims.z > 1023)
                    throw new InvalidDataException("Invalid indirection dimensions.");
                mip.Dimensions = dims;
                mip.VolumeToVirtualUVScale = ReadVector(reader);
                mip.VolumeToVirtualUVAdd = ReadVector(reader);
                mip.DistanceFieldToVolumeScaleBias = new Vector2(ReadFloat(reader), ReadFloat(reader));
                long cells = (long)dims.x * dims.y * dims.z;
                long size = reader.ReadUInt32();
                if (bricks < 0 || bricks > cells || size != cells * 4 + (long)bricks * 512
                    || size > int.MaxValue || size > reader.BaseStream.Length - reader.BaseStream.Position)
                    throw new InvalidDataException("Invalid sparse mip payload size.");
                mip.Indirection = new uint[(int)cells];
                for (int cell = 0; cell < mip.Indirection.Length; ++cell)
                {
                    uint index = reader.ReadUInt32();
                    if (index != uint.MaxValue && index >= bricks)
                        throw new InvalidDataException("Indirection addresses an absent brick.");
                    mip.Indirection[cell] = index;
                }
                mip.Bricks = ReadBytes(reader, bricks * 512);
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
                throw new InvalidDataException("Trailing bytes after final mip.");
            asset.Mips = mips;
        }

        private static byte[] ReadBytes(BinaryReader reader, int count)
        {
            var result = reader.ReadBytes(count);
            return result.Length == count ? result : throw new EndOfStreamException();
        }

        private static float ReadFloat(BinaryReader reader)
        {
            float value = reader.ReadSingle();
            return float.IsFinite(value) ? value : throw new InvalidDataException("Nonfinite distance field metadata.");
        }

        private static Vector3 ReadVector(BinaryReader reader) => new(ReadFloat(reader), ReadFloat(reader), ReadFloat(reader));

        private static Bounds ReadBounds(BinaryReader reader)
        {
            var min = ReadVector(reader);
            var max = ReadVector(reader);
            if (min.x > max.x || min.y > max.y || min.z > max.z)
                throw new InvalidDataException("Inverted distance field bounds.");
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }
    }
}
