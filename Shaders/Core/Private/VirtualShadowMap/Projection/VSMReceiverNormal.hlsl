float3 ReconstructVSMReceiverNormal(uint2 pixelCoord, float deviceDepth,
    float3 positionWS, float3 fallbackNormal)
{
    // A normal map describes shading, not the plane stored in the shadow pool.
    // Select the one-sided derivative whose two neighbors extrapolate back to
    // this depth. A different face can be closer in depth than the true slope.
    // Eight depth loads, but only two additional world reconstructions.
    int2 pixel = int2(pixelCoord);
    int2 limit = int2(_CSMOutputWidth, _CSMOutputHeight) - 1;
    int2 left = max(pixel - int2(1, 0), 0);
    int2 right = min(pixel + int2(1, 0), limit);
    int2 down = max(pixel - int2(0, 1), 0);
    int2 up = min(pixel + int2(0, 1), limit);
    float4 depths = float4(_DepthTexture.Load(int3(left, 0)), _DepthTexture.Load(int3(right, 0)),
        _DepthTexture.Load(int3(down, 0)), _DepthTexture.Load(int3(up, 0)));
    float4 error = abs(depths - deviceDepth);
    error.x = left.x == pixel.x || IsSkyPixel(depths.x) ? 1e20 : error.x;
    error.y = right.x == pixel.x || IsSkyPixel(depths.y) ? 1e20 : error.y;
    error.z = down.y == pixel.y || IsSkyPixel(depths.z) ? 1e20 : error.z;
    error.w = up.y == pixel.y || IsSkyPixel(depths.w) ? 1e20 : error.w;
    if (min(error.x, error.y) >= 1e20 || min(error.z, error.w) >= 1e20)
        return fallbackNormal;
    int2 left2 = max(pixel - int2(2, 0), 0);
    int2 right2 = min(pixel + int2(2, 0), limit);
    int2 down2 = max(pixel - int2(0, 2), 0);
    int2 up2 = min(pixel + int2(0, 2), limit);
    float4 secondDepths = float4(_DepthTexture.Load(int3(left2, 0)), _DepthTexture.Load(int3(right2, 0)),
        _DepthTexture.Load(int3(down2, 0)), _DepthTexture.Load(int3(up2, 0)));
    // Device depth is affine across a projected triangle, including perspective.
    float4 planeError = abs(2.0 * depths - secondDepths - deviceDepth);
    planeError.x = error.x >= 1e20 || left2.x == left.x || IsSkyPixel(secondDepths.x) ? 1e20 : planeError.x;
    planeError.y = error.y >= 1e20 || right2.x == right.x || IsSkyPixel(secondDepths.y) ? 1e20 : planeError.y;
    planeError.z = error.z >= 1e20 || down2.y == down.y || IsSkyPixel(secondDepths.z) ? 1e20 : planeError.z;
    planeError.w = error.w >= 1e20 || up2.y == up.y || IsSkyPixel(secondDepths.w) ? 1e20 : planeError.w;
    // Compare plane errors only with complete stencils on both sides. A missing
    // second neighbor must not force selection across a different face.
    if (max(planeError.x, planeError.y) < 1e20) error.xy = planeError.xy;
    if (max(planeError.z, planeError.w) < 1e20) error.zw = planeError.zw;
    float3 dx = error.x <= error.y
        ? positionWS - ReconstructWorldPosition(uint2(left), depths.x)
        : ReconstructWorldPosition(uint2(right), depths.y) - positionWS;
    float3 dy = error.z <= error.w
        ? positionWS - ReconstructWorldPosition(uint2(down), depths.z)
        : ReconstructWorldPosition(uint2(up), depths.w) - positionWS;
    float3 normal = cross(dx, dy);
    float lengthSquared = dot(normal, normal);
    if (lengthSquared < 1e-20)
        return fallbackNormal;
    normal *= rsqrt(lengthSquared);
    // Only the hemisphere comes from the shading normal; its normal-map slope
    // must not enter the normal offset or receiver-plane depth correction.
    return dot(normal, fallbackNormal) < 0.0 ? -normal : normal;
}

