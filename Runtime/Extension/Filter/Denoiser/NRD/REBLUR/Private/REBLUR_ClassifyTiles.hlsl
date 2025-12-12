/*
Copyright (c) 2022, NVIDIA CORPORATION. All rights reserved.

NVIDIA CORPORATION and its licensors retain all intellectual property
and proprietary rights in and to this software, related documentation
and any modifications thereto. Any use, reproduction, disclosure or
distribution of this software and related documentation without an express
license agreement from NVIDIA CORPORATION is strictly prohibited.
*/



[numthreads( 8, 4, 1 )]
NRD_EXPORT void NRD_CS_MAIN(uint2 threadPos : SV_GroupThreadId, uint2 tilePos : SV_GroupId, uint threadIndex : SV_GroupIndex)
{
    uint2 pixelPos = tilePos * 16 + threadPos * uint2(2, 4);
    int sum = 0;

    [unroll]
    for (uint i = 0; i < 2; i++)
    {
        [unroll]
        for (uint j = 0; j < 4; j++)
        {
            uint2 pos = pixelPos + uint2( i, j );
            float viewZ = UnpackViewZ( gIn_ViewZ[ WithRectOrigin( pos ) ] );

            sum += viewZ > gDenoisingRange ? 1 : 0;
        }
    }

    int s_Sum = WaveActiveSum(sum);


    if (WaveIsFirstLane())
    {
        float isSky = s_Sum == 256 ? 1.0 : 0.0;

        gOut_Tiles[tilePos] = isSky;
    }
}
