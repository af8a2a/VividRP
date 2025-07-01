using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class MipGenerator
    {
        static readonly int _RectOffset = Shader.PropertyToID("_RectOffset");
        static readonly int _Result1 = Shader.PropertyToID("_Result1");
        static readonly int _Source4 = Shader.PropertyToID("_Source4");
        static int[] _IntParams = new int[2];


        void SampleCopyChannel(
            ComputeCommandBuffer cmd,
            RectInt rect,
            int _source,
            TextureHandle source,
            int _target,
            TextureHandle target,
            int slices,
            int kernel8,
            int kernel1)
        {
            RectInt main, topRow, rightCol, topRight;
            unsafe
            {
                RectInt* dispatch1Rects = stackalloc RectInt[3];
                int dispatch1RectCount = 0;
                RectInt dispatch8Rect = new RectInt(0, 0, 0, 0);

                if (TileLayoutUtils.TryLayoutByTiles(
                        rect,
                        8,
                        out main,
                        out topRow,
                        out rightCol,
                        out topRight))
                {
                    if (topRow.width > 0 && topRow.height > 0)
                    {
                        dispatch1Rects[dispatch1RectCount] = topRow;
                        ++dispatch1RectCount;
                    }

                    if (rightCol.width > 0 && rightCol.height > 0)
                    {
                        dispatch1Rects[dispatch1RectCount] = rightCol;
                        ++dispatch1RectCount;
                    }

                    if (topRight.width > 0 && topRight.height > 0)
                    {
                        dispatch1Rects[dispatch1RectCount] = topRight;
                        ++dispatch1RectCount;
                    }

                    dispatch8Rect = main;
                }
                else if (rect.width > 0 && rect.height > 0)
                {
                    dispatch1Rects[dispatch1RectCount] = rect;
                    ++dispatch1RectCount;
                }

                cmd.SetComputeTextureParam(m_HDRPGPUCopyShader, kernel8, _source, source);
                cmd.SetComputeTextureParam(m_HDRPGPUCopyShader, kernel1, _source, source);
                cmd.SetComputeTextureParam(m_HDRPGPUCopyShader, kernel8, _target, target);
                cmd.SetComputeTextureParam(m_HDRPGPUCopyShader, kernel1, _target, target);

                if (dispatch8Rect.width > 0 && dispatch8Rect.height > 0)
                {
                    var r = dispatch8Rect;
                    // Use intermediate array to avoid garbage
                    _IntParams[0] = r.x;
                    _IntParams[1] = r.y;
                    cmd.SetComputeIntParams(m_HDRPGPUCopyShader, _RectOffset, _IntParams);
                    cmd.DispatchCompute(m_HDRPGPUCopyShader, kernel8, (int)Mathf.Max(r.width / 8, 1), (int)Mathf.Max(r.height / 8, 1), slices);
                }

                for (int i = 0, c = dispatch1RectCount; i < c; ++i)
                {
                    var r = dispatch1Rects[i];
                    // Use intermediate array to avoid garbage
                    _IntParams[0] = r.x;
                    _IntParams[1] = r.y;
                    cmd.SetComputeIntParams(m_HDRPGPUCopyShader, _RectOffset, _IntParams);
                    cmd.DispatchCompute(m_HDRPGPUCopyShader, kernel1, (int)Mathf.Max(r.width, 1), (int)Mathf.Max(r.height, 1), slices);
                }
            }
        }

        public void SampleCopyChannel_xyzw2x(ComputeCommandBuffer cmd, TextureHandle source, TextureHandle target, RectInt rect)
        {
            bool AssumeSlice(RTHandle lhs, RTHandle rhs) => lhs.rt.volumeDepth == rhs.rt.volumeDepth;
            int FetchSlice(RTHandle rtHandle) => rtHandle.rt.volumeDepth;

            Debug.Assert(AssumeSlice(source, target));
            SampleCopyChannel(cmd, rect, _Source4, source, _Result1, target, FetchSlice(source), k_SampleKernel_xyzw2x_8, k_SampleKernel_xyzw2x_1);
        }
    }
}