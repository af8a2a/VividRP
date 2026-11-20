using System;
using UnityEngine;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    // On disk the mesh and field bounds are UE mesh-local centimetres, and mip 0 is the finest.
    public sealed class VividSparseMeshDistanceField : ScriptableObject
    {
        [Serializable]
        internal struct Mip
        {
            public Vector3Int Dimensions;
            public Vector3 VolumeToVirtualUVScale, VolumeToVirtualUVAdd;
            public Vector2 DistanceFieldToVolumeScaleBias;
            public uint[] Indirection;
            public byte[] Bricks;
        }

        [SerializeField] internal string SourceObjectPath;
        [SerializeField] internal Bounds GeometryBoundsCm, FieldBoundsCm;
        [SerializeField] internal bool MostlyTwoSided;
        [SerializeField] internal Mip[] Mips;
        internal uint Revision { get; private set; }
        private void OnEnable() => ++Revision;
        private void OnValidate() => ++Revision;
    }
}
