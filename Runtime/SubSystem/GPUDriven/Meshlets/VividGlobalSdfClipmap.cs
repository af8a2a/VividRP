using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    internal static class VividGlobalSdfClipmap
    {
        // Keep the resolution and level count in sync with GlobalSdf.hlsl.
        internal const int Resolution = 128;
        internal const int LevelCount = 3;
        internal const int RegionsPerLevel = 3;
        internal const float DefaultVoxelSize = 0.125f;

        internal struct Region
        {
            public int3 Min;
            public int3 Size;
        }

        internal static int GetUpdateRegions(int3 previous, int3 next, bool reset, Region[] regions, int offset)
        {
            int3 end = next + Resolution;
            int3 overlapMin = math.max(previous, next);
            int3 overlapMax = math.min(previous + Resolution, end);
            if (reset || math.any(overlapMin >= overlapMax))
            {
                regions[offset] = new Region { Min = next, Size = new int3(Resolution) };
                return 1;
            }

            // Restrict each subsequent slab to the overlap on axes already processed.
            int count = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                if (next[axis] == previous[axis])
                    continue;
                int3 slabMin = next;
                int3 slabMax = end;
                if (next[axis] > previous[axis])
                    slabMin[axis] = overlapMax[axis];
                else
                    slabMax[axis] = overlapMin[axis];
                regions[offset + count++] = new Region { Min = slabMin, Size = slabMax - slabMin };
                next[axis] = overlapMin[axis];
                end[axis] = overlapMax[axis];
            }
            return count;
        }
    }

    internal sealed class VividGlobalSdfCameraState : CameraRelativeState
    {
        internal readonly RTHandle[] Textures = new RTHandle[VividGlobalSdfClipmap.LevelCount];
        internal readonly int3[] Origins = new int3[VividGlobalSdfClipmap.LevelCount];
        internal bool Valid;
        internal uint SceneRevision;
        internal int LayerMask;
        internal float BaseVoxelSize;

        internal void EnsureTextures(GraphicsFormat format)
        {
            const int size = VividGlobalSdfClipmap.Resolution;
            for (int i = 0; i < Textures.Length; i++)
            {
                var texture = Textures[i];
                if (texture?.rt != null && texture.rt.IsCreated())
                    continue;

                Valid = false;
                texture?.Release();
                Textures[i] = RTHandles.Alloc(size, size, slices: size, dimension: TextureDimension.Tex3D,
                    colorFormat: format, filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Repeat,
                    enableRandomWrite: true, name: "Global SDF Level " + i);
            }
        }

        public override void Dispose()
        {
            for (int i = 0; i < Textures.Length; i++)
            {
                Textures[i]?.Release();
                Textures[i] = null;
            }
            Valid = false;
        }
    }

    internal sealed class VividGlobalSdfFrameData : ContextItem
    {
        internal readonly Vector4[] Levels = new Vector4[VividGlobalSdfClipmap.LevelCount];
        internal readonly Vector4[] Scroll = new Vector4[VividGlobalSdfClipmap.LevelCount];
        internal readonly int3[] Origins = new int3[VividGlobalSdfClipmap.LevelCount];
        internal VividGlobalSdfCameraState State;
        internal RenderGraphTexture[] Outputs;
        internal uint SceneRevision;
        internal int LayerMask;
        internal float BaseVoxelSize;
        internal bool Recorded;

        internal void Commit()
        {
            if (State == null || !Recorded)
                return;
            System.Array.Copy(Origins, State.Origins, Origins.Length);
            State.SceneRevision = SceneRevision;
            State.LayerMask = LayerMask;
            State.BaseVoxelSize = BaseVoxelSize;
            State.Valid = true;
        }

        public override void Reset()
        {
            State = null;
            Outputs = null;
            Recorded = false;
        }
    }
}
