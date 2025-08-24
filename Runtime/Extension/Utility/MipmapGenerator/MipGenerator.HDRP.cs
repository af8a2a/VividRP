using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class MipGenerator
    {
        private static int _DepthMipChain = Shader.PropertyToID("_DepthMipChain");
        private static int _DepthPyramidConstants = Shader.PropertyToID("DepthPyramidConstants");
        private static int _DepthPyramidMipLevelOffsets = Shader.PropertyToID("_DepthPyramidMipLevelOffsets");
        internal ref PackedMipChainInfo depthBufferMipChainInfo => ref m_DepthBufferMipChainInfo;

        internal Vector2Int depthMipChainSize => m_DepthBufferMipChainInfo.textureSize;

        internal struct PackedMipChainInfo
        {
            public Vector2Int textureSize;
            public int mipLevelCount; // mips contain min (closest) depth
            public int mipLevelCountCheckerboard;
            public Vector2Int[] mipLevelSizes;
            public Vector2Int[] mipLevelOffsets; // mips contain min (closest) depth
            public Vector2Int[] mipLevelOffsetsCheckerboard;

            private Vector2 cachedTextureScale;
            private Vector2Int cachedHardwareTextureSize;
            private int cachedCheckerboardMipCount;

            private bool m_OffsetBufferWillNeedUpdate;

            public void Allocate()
            {
                mipLevelOffsets = new Vector2Int[15];
                mipLevelOffsetsCheckerboard = new Vector2Int[15];
                mipLevelSizes = new Vector2Int[15];
                m_OffsetBufferWillNeedUpdate = true;
            }

            enum PackDirection
            {
                Right,
                Down,
            }

            static Vector2Int NextMipBegin(Vector2Int prevMipBegin, Vector2Int prevMipSize, PackDirection dir)
            {
                Vector2Int mipBegin = prevMipBegin;
                if (dir == PackDirection.Right)
                    mipBegin.x += prevMipSize.x;
                else
                    mipBegin.y += prevMipSize.y;
                return mipBegin;
            }

            // We pack all MIP levels into the top MIP level to avoid the Pow2 MIP chain restriction.
            // We compute the required size iteratively.
            // This function is NOT fast, but it is illustrative, and can be optimized later.
            public void ComputePackedMipChainInfo(Vector2Int viewportSize, int checkerboardMipCount)
            {
                // only support up to 2 mips of checkerboard data being created
                checkerboardMipCount = Mathf.Clamp(checkerboardMipCount, 0, 2);

                bool isHardwareDrsOn = DynamicResolutionHandler.instance.HardwareDynamicResIsEnabled();
                Vector2Int hardwareTextureSize = isHardwareDrsOn ? DynamicResolutionHandler.instance.ApplyScalesOnSize(viewportSize) : viewportSize;
                Vector2 textureScale = isHardwareDrsOn
                    ? new Vector2((float)viewportSize.x / (float)hardwareTextureSize.x, (float)viewportSize.y / (float)hardwareTextureSize.y)
                    : new Vector2(1.0f, 1.0f);

                // We need to mark the buffer dirty in case another camera has a different viewport size
                m_OffsetBufferWillNeedUpdate = true;

                // No work needed.
                if (cachedHardwareTextureSize == hardwareTextureSize && cachedTextureScale == textureScale &&
                    cachedCheckerboardMipCount == checkerboardMipCount)
                    return;

                cachedHardwareTextureSize = hardwareTextureSize;
                cachedTextureScale = textureScale;
                cachedCheckerboardMipCount = checkerboardMipCount;

                mipLevelSizes[0] = hardwareTextureSize;
                mipLevelOffsets[0] = Vector2Int.zero;
                mipLevelOffsetsCheckerboard[0] = mipLevelOffsets[0];

                int mipLevel = 0;
                Vector2Int mipSize = hardwareTextureSize;
                bool hasCheckerboard = (checkerboardMipCount != 0);
                int maxCheckboardLevelCount = hasCheckerboard ? (1 + checkerboardMipCount) : 0;
                do
                {
                    mipLevel++;

                    // Round up.
                    mipSize.x = Math.Max(1, (mipSize.x + 1) >> 1);
                    mipSize.y = Math.Max(1, (mipSize.y + 1) >> 1);

                    mipLevelSizes[mipLevel] = mipSize;

                    Vector2Int prevMipSize = mipLevelSizes[mipLevel - 1];
                    Vector2Int prevMipBegin = mipLevelOffsets[mipLevel - 1];
                    Vector2Int prevMipBeginCheckerboard = mipLevelOffsetsCheckerboard[mipLevel - 1];

                    Vector2Int mipBegin = prevMipBegin;
                    Vector2Int mipBeginCheckerboard = prevMipBeginCheckerboard;
                    if (mipLevel == 1)
                    {
                        // first mip always below full resolution
                        mipBegin = NextMipBegin(prevMipBegin, prevMipSize, PackDirection.Down);

                        // pack checkerboard next to it if present
                        if (hasCheckerboard)
                            mipBeginCheckerboard = NextMipBegin(mipBegin, mipSize, PackDirection.Right);
                        else
                            mipBeginCheckerboard = mipBegin;
                    }
                    else
                    {
                        // alternate directions, mip 2 starts with down if checkerboard, right if not
                        bool isOdd = ((mipLevel & 1) != 0);
                        PackDirection dir = (isOdd ^ hasCheckerboard) ? PackDirection.Down : PackDirection.Right;

                        mipBegin = NextMipBegin(prevMipBegin, prevMipSize, dir);
                        mipBeginCheckerboard = NextMipBegin(prevMipBeginCheckerboard, prevMipSize, dir);
                    }

                    mipLevelOffsets[mipLevel] = mipBegin;
                    mipLevelOffsetsCheckerboard[mipLevel] = mipBeginCheckerboard;

                    hardwareTextureSize.x = Math.Max(hardwareTextureSize.x, mipBegin.x + mipSize.x);
                    hardwareTextureSize.y = Math.Max(hardwareTextureSize.y, mipBegin.y + mipSize.y);
                    hardwareTextureSize.x = Math.Max(hardwareTextureSize.x, mipBeginCheckerboard.x + mipSize.x);
                    hardwareTextureSize.y = Math.Max(hardwareTextureSize.y, mipBeginCheckerboard.y + mipSize.y);
                } while ((mipSize.x > 1) || (mipSize.y > 1));

                textureSize = new Vector2Int(
                    (int)Mathf.Ceil((float)hardwareTextureSize.x * textureScale.x), (int)Mathf.Ceil((float)hardwareTextureSize.y * textureScale.y));

                mipLevelCount = mipLevel + 1;
                mipLevelCountCheckerboard = hasCheckerboard ? (1 + checkerboardMipCount) : 0;
            }

            public ComputeBuffer GetOffsetBufferData(ComputeBuffer mipLevelOffsetsBuffer)
            {
                if (m_OffsetBufferWillNeedUpdate)
                {
                    mipLevelOffsetsBuffer.SetData(mipLevelOffsets);
                    m_OffsetBufferWillNeedUpdate = false;
                }

                return mipLevelOffsetsBuffer;
            }
        }


        public static TextureHandle RenderMinDepthPyramidHDRP(
            RenderGraph renderGraph,
            ContextContainer frameData
        )
        {
            var instance = MipGenerator.instance;
            return instance.RenderMinDepthPyramid(renderGraph, frameData);
        }


        // Generates an in-place depth pyramid
        // HDRP Version
        // TODO: Mip-mapping depth is problematic for precision at lower mips, generate a packed atlas instead
        public TextureHandle RenderMinDepthPyramid(
            RenderGraph renderGraph,
            ContextContainer frameData
        )
        {
            using (var builder = renderGraph.AddComputePass<DepthPyramidPassData>("HDRP DepthPyramid", out var passData))
            {
                passData.DepthPyramidShader = m_DepthPyramidCS;
                passData.DepthPyramidKernelID = m_DepthDownsampleKernel;
                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();
                passData.width = cameraData.scaledWidth;
                passData.height = cameraData.scaledHeight;


                depthBufferMipChainInfo.ComputePackedMipChainInfo(new Vector2Int(cameraData.actualWidth,cameraData.actualHeight), 0 /*not support yet*/);

                passData.depthBufferMipChainInfo = depthBufferMipChainInfo;
                passData.depthPyramidTexture = renderGraph.CreateTexture(
                    new TextureDesc(depthBufferMipChainInfo.textureSize.x, depthBufferMipChainInfo.textureSize.y)
                    {
                        enableRandomWrite = true,
                        name = "HDRP DepthPyramid",
                        format = GraphicsFormat.R32_SFloat,
                        depthBufferBits = 0,
                        msaaSamples = MSAASamples.None
                    });
                passData.depthTexture = resourceData.cameraDepthTexture;
                builder.UseTexture(passData.depthPyramidTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.depthTexture);

                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc<DepthPyramidPassData>((data, context) =>
                {
                    var cmd = context.cmd;
                    var cs = data.DepthPyramidShader;
                    int kernel = data.DepthPyramidKernelID;
                    var info = passData.depthBufferMipChainInfo;
                    cmd.SetComputeTextureParam(cs, kernel, _DepthMipChain, data.depthPyramidTexture);
                    cmd.SetGlobalBuffer(_DepthPyramidMipLevelOffsets, data.depthBufferMipChainInfo.GetOffsetBufferData(m_DepthPyramidMipLevelOffsetsBuffer));


                    SampleCopyChannel_xyzw2x(cmd, data.depthTexture, data.depthPyramidTexture, new RectInt(0, 0, data.width, data.height));

                    // Note: Gather() doesn't take a LOD parameter and we cannot bind an SRV of a MIP level,
                    // and we don't support Min samplers either. So we are forced to perform 4x loads.
                    for (int dstIndex0 = 1; dstIndex0 < info.mipLevelCount;)
                    {
                        int minCount = Mathf.Min(info.mipLevelCount - dstIndex0, 4);
                        int cbCount = 0;
                        if (dstIndex0 < info.mipLevelCountCheckerboard)
                        {
                            cbCount = info.mipLevelCountCheckerboard - dstIndex0;
                            Debug.Assert(dstIndex0 == 1, "expected to make checkerboard mips on the first pass");
                            Debug.Assert(cbCount <= minCount, "expected fewer checkerboard mips than min mips");
                            Debug.Assert(cbCount <= 2, "expected 2 or fewer checkerboard mips for now");
                        }

                        Vector2Int srcOffset = info.mipLevelOffsets[dstIndex0 - 1];
                        Vector2Int srcSize = info.mipLevelSizes[dstIndex0 - 1];
                        int dstIndex1 = Mathf.Min(dstIndex0 + 1, info.mipLevelCount - 1);
                        int dstIndex2 = Mathf.Min(dstIndex0 + 2, info.mipLevelCount - 1);
                        int dstIndex3 = Mathf.Min(dstIndex0 + 3, info.mipLevelCount - 1);

                        DepthPyramidConstants cb = new DepthPyramidConstants
                        {
                            _MinDstCount = (uint)minCount,
                            _CbDstCount = (uint)cbCount,
                            _SrcOffset = srcOffset,
                            _SrcLimit = srcSize - Vector2Int.one,
                            _DstSize0 = info.mipLevelSizes[dstIndex0],
                            _DstSize1 = info.mipLevelSizes[dstIndex1],
                            _DstSize2 = info.mipLevelSizes[dstIndex2],
                            _DstSize3 = info.mipLevelSizes[dstIndex3],
                            _MinDstOffset0 = info.mipLevelOffsets[dstIndex0],
                            _MinDstOffset1 = info.mipLevelOffsets[dstIndex1],
                            _MinDstOffset2 = info.mipLevelOffsets[dstIndex2],
                            _MinDstOffset3 = info.mipLevelOffsets[dstIndex3],
                            _CbDstOffset0 = info.mipLevelOffsetsCheckerboard[dstIndex0],
                            _CbDstOffset1 = info.mipLevelOffsetsCheckerboard[dstIndex1],
                        };
                        ConstantBuffer.Push(cmd, cb, cs, _DepthPyramidConstants);

                        CoreUtils.SetKeyword(cmd, cs, "ENABLE_CHECKERBOARD", cbCount != 0);

                        Vector2Int dstSize = info.mipLevelSizes[dstIndex0];
                        cmd.DispatchCompute(cs, kernel, RenderingUtilsExt.DivRoundUp(dstSize.x, 8), RenderingUtilsExt.DivRoundUp(dstSize.y, 8),
                            1);

                        dstIndex0 += minCount;
                    }
                });
                return passData.depthPyramidTexture;
            }
        }
    }
}