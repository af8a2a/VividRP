// PositionDebugUtils.hlsl

#ifndef POSITION_DEBUG_UTILS_INCLUDED
#define POSITION_DEBUG_UTILS_INCLUDED

// --- 基础重构函数 ---
// 辅助确认：输入深度应为硬件深度图采样值，uv 为 [0,1]
float3 ReconstructWorldPos(float2 uv, float depth, float4x4 invVP)
{
    #if UNITY_UV_STARTS_AT_TOP
    uv.y = 1.0 - uv.y;
    #endif
    
    // NDC 空间: xy [-1, 1], z [0, 1] (如果是 Reverse-Z, 1是近, 0是远)
    float4 ndc = float4(uv * 2.0 - 1.0, depth, 1.0);
    float4 worldPosW = mul(invVP, ndc);
    return worldPosW.xyz / worldPosW.w;
}

// --- 模式 1: 世界空间网格 ---
float3 Debug_WorldGrid(float3 worldPos, float gridSize = 1.0, float thickness = 0.05)
{
    float3 f = frac(worldPos / gridSize);
    float3 grid = step(1.0 - thickness, f) + step(f, thickness);
    float edge = max(max(grid.x, grid.y), grid.z);
    return edge.xxx;
}

// --- 模式 2: 轴向色彩映射 (RGB Axis) ---
float3 Debug_WorldAxis(float3 worldPos, float frequency = 1.0)
{
    return frac(worldPos * frequency);
}

// --- 模式 3: 距离等高线 (Distance Rings) ---
float3 Debug_DistanceRings(float3 worldPos, float3 center, float interval = 1.0)
{
    float d = distance(worldPos, center);
    float ring = smoothstep(0.1, 0.0, abs(frac(d / interval + 0.5) - 0.5));
    return ring.xxx;
}

// --- 模式 4: 数值哨兵 (NaN/Inf 检测) ---
float3 Debug_CheckInvalid(float3 worldPos)
{
    if (any(isnan(worldPos)) || any(isinf(worldPos)))
    {
        return float3(1.0, 0.0, 1.0); // 亮紫色报警
    }
    return float3(0.0, 0.2, 0.0); // 正常则暗绿
}

// --- 模式 5: 动态扫描线 ---
float3 Debug_Scanline(float3 worldPos, float time, float speed = 2.0, float density = 5.0)
{
    float lineY = sin(worldPos.y * density - time * speed);
    return step(0.9, lineY).xxx;
}

#endif