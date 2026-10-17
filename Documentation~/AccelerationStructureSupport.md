# RenderGraph 光线追踪加速结构 (RTAS) 支持

返回索引：[Documentation~ 索引](README.md) · 相关文档：[RenderGraph 编辑器](RenderGraphEditor.md) | [可序列化资源描述符](RenderGraphResourceDescriptors.md)

## 概述

VividRP 的 RenderGraph 将光线追踪加速结构（Ray Tracing Acceleration Structure, RTAS）作为一等公民纳入可序列化描述符系统。RTAS 的构建与消费通过显式的图连线表达，替代隐藏的全局状态（`SetGlobalRayTracingAccelerationStructure`），从而保障依赖顺序与 Async Compute 调度的正确性。

---

## 核心组件

| 组件 | 路径 | 职责 |
| --- | --- | --- |
| `RenderGraphAccelerationStructureDesc` | `Runtime/RenderGraph/Resource/RenderGraphAccelerationStructureDesc.cs` | 可序列化的 RTAS 描述符，对应 Unity `RayTracingAccelerationStructureDesc`，提供 `ToAccelerationStructureDesc()`、`FromAccelerationStructureDesc(desc)` 与 `Create(name)` |
| `RenderGraphAccelerationStructure` | `Runtime/RenderGraph/Resource/RenderGraphAccelerationStructureDesc.cs` | RenderGraph 资源包装器，可根据序列化描述符按需创建原生 `RayTracingAccelerationStructure` 或包装外部管理实例 |
| `AccelerationStructureResourceNodeData` | `Editor/RenderGraph/Nodes/AccelerationStructureResourceNodeData.cs` | 图编辑器中的 RTAS 资源节点，用于显式声明自定义 RTAS 描述符 |
| `RTASBuildPass` | `Runtime/RenderPass/Core/RTASBuildPass.cs` | 负责将场景几何构建为 `RenderGraphAccelerationStructure` 资源并向下游光追 Pass 输出 |

---

## 使用模式

### 1. 在图中构建与传递 RTAS

- **默认自持模式**：直接将 `RTASBuildPass` 节点的 `SceneRTAS` 输出端口连接到下游光线追踪 Pass（如方向光 DXR 阴影、延迟光照、Reference Path Tracing 或 Raytracing GBuffer）。
- **外部覆盖模式**：若需自定义 RTAS 描述符，可在图中创建 `AccelerationStructureResourceNodeData` 节点并接入 `RTASBuildPass`。

### 2. C# Pass 声明示例

```csharp
// 生产者 Pass (如 RTASBuildPass)
[RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Write)]
private RenderGraphAccelerationStructure m_SceneAccelerationStructure = new();

// 消费者 Pass (下游光追 Pass)
[RenderGraphResource(Name = "SceneRTAS", Access = AccessFlags.Read)]
private RenderGraphAccelerationStructure m_SceneAccelerationStructure = new();
```

在录制阶段，消费者 Pass 直接从 `m_SceneAccelerationStructure` 获取底层原生对象，并通过 `SetRayTracingAccelerationStructure(...)` 等 CommandBuffer API 绑定到 Compute 或 Ray Tracing Shader。

### 3. HLSL Inline Ray Tracing (`RayQuery`) 示例

```hlsl
#pragma kernel RayTraceMain

RWTexture2D<float4> _OutputTexture;
RaytracingAccelerationStructure _AccelStruct;

[numthreads(8, 8, 1)]
void RayTraceMain(uint3 id : SV_DispatchThreadID)
{
    RayDesc ray;
    ray.Origin = float3(0.0, 0.0, 0.0);
    ray.Direction = normalize(float3(id.xy, 1.0));
    ray.TMin = 0.001;
    ray.TMax = 1000.0;

    RayQuery<RAY_FLAG_NONE> query;
    query.TraceRayInline(_AccelStruct, RAY_FLAG_NONE, 0xFF, ray);
    query.Proceed();

    float4 color = query.CommittedStatus() == COMMITTED_TRIANGLE_HIT
        ? float4(1.0, 1.0, 1.0, 1.0)
        : float4(0.0, 0.0, 0.0, 1.0);
    _OutputTexture[id.xy] = color;
}
```

---

## 硬件与平台要求

- **图形 API**：Windows DirectX 12（具备 DXR 支持）；或支持 Ray Tracing 扩展的 Vulkan / Metal。
- **GPU 硬件**：具备硬件光追能力的显卡（NVIDIA RTX、AMD RDNA 2+、Intel Arc、Apple Silicon）。
