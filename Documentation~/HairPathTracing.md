# Chiang 毛发路径追踪与 DOTS GPU 流

返回索引：[Documentation~ 索引](README.md) · 相关文档：[毛发路径追踪路线图](../Roadmap~/HairPathTracingRoadmap.md)

VividRP 毛发系统采用 RTXCR 的 **Chiang 近场 BCSDF**（Near-Field BCSDF）材质模型，并搭配 VividRP 自有的 **DOTS（Degree-2 Orthogonal Triangle Strips）** 几何后端。静态发丝构成 V1 基线，同一套网格契约同时支持携带上一帧中心线，为变形发丝提供准确的动态运动向量。

## 支持路径与概览

| 模块 | 当前支持规格 |
| --- | --- |
| **Shader** | `VividRP/Material/Hair`（仅包含 DXR 光追 Pass，不含光栅预览 Pass） |
| **几何表示** | DOTS 网格：每条线段（Segment）展开为 4 个正交三角形（12 个独立顶点），支持可选的动态帧历史 |
| **材质与 GBuffer Pass** | `ReferencedPathtracingDXR`（参考路径追踪）与 `RaytracingGBufferDXR`（光追 GBuffer 引导） |
| **光照传输** | 统一解析光源与环境光 NEE、BSDF 采样、多反弹传输与多重重要性采样（MIS） |
| **能量分类與介质栈** | V1 中毛发能量归类为 Specular；Chiang TT/TRT 散射瓣可穿越径向法线，但**不进入**封闭实体材质的四层介质栈 |
| **降噪支持** | 接入现有 NRD REBLUR 与 DLSS Ray Reconstruction 引导信号 |

## 生成验证资产

在 Unity Editor 菜单中选择 **`VividRP > Hair > Create Chiang Validation Assets`**，将自动生成：

- `Assets/VividRPValidation/Hair/ChiangHairValidationDots.asset`
- `Assets/VividRPValidation/Hair/ChiangHairValidation.mat`

在使用 Reference Path Tracing 图的场景中，将上述网格和材质分别赋给 `MeshFilter` 与 `MeshRenderer` 即可验证。该资产为一束弯曲且渐变收细（Tapered）的发束，用于验证几何求交、散射瓣、自相交跳过与降噪引导，不代表完整生产级 Groom 的性能负载。

---

## DOTS 网格顶点契约 (`HairDotsMeshBuilder`)

`HairDotsMeshBuilder` 接收带有端点位置、半径和 UV 的独立线段。每条线段写入 **12 个独立顶点**，属性布局如下：

| 语义 | 类型 | 内容与约定 |
| --- | --- | --- |
| `POSITION` | `float3` | 展开后的正交条带表面顶点位置 |
| `NORMAL` | `float3` | 带符号的中心线偏移轴（Signed Centerline Offset Axis） |
| `TANGENT.xyz` | `float3` | 中心线切向（Centerline Tangent） |
| `TEXCOORD0.xy` | `float2` | 发丝 UV（Strand UV） |
| `TEXCOORD1.x` | `float` | 体积补偿后的半径（Volume-Compensated Radius） |
| `TEXCOORD1.y` | `float` | 线段内端点参数坐标（起点为 `0`，终点为 `1`） |
| `TEXCOORD2.xyz` | `float3` | 上一帧模型空间（Object Space）中的端点中心线位置 |
| `TEXCOORD2.w` | `float` | 上一帧体积补偿半径幅值；**负值作为单帧历史重置标记（History-Reset Marker）** |

> [!IMPORTANT]
> - **半径缩放同步**：半径缩放在构建网格时直接烘焙进展开顶点与存储半径中；仅修改材质属性无法正确缩放 DOTS 几何体。
> - **解析锥体修正与防自交**：光追 Closest-Hit 着色器在三角形命中点附近重建渐变线段中心线，求交解析锥柱表面并输出修正后的位置、径向法线、切线和半径；NEE 与后续反弹射线起点利用该半径在穿过发丝时直接跳过第二片正交条带。
> - **静态与 CPU 动态更新**：静态构建会将当前中心线与半径同时写入 `TEXCOORD2`，无需额外资产即可保留相机与物体变换运动。CPU 变形接入可在模拟后、`RTASBuildPass` 前调用 `HairDotsMeshBuilder.BuildDynamic(current, previous, target)`（历史重置、瞬移或切换 Groom 时传入相同的 `current` 数据）。

---

## 持久 GPU 发丝流式更新 (`HairDotsGpuStream`)

对于拓扑固定的生产级动态 Groom，应使用 `HairDotsGpuStream`：它一次性分配网格顶点/索引缓冲并绑定给 Renderer，直接通过 Compute 将 GPU 模拟结果展开到原生顶点缓冲，并在 GPU 历史缓冲中维护上一帧中心线——**每帧零 `Mesh.Clear`、零索引重建、零 GPU→CPU 回读**。

### 1. 模拟缓冲区 ABI (`HairGpuStrandSegment`, 48 字节)

每个线段对应一个 48 字节结构体（3 个连续 `float4`）：

| 偏移 | 类型 | 字段含义 |
| --- | --- | --- |
| `0` | `float4` | `xyz`：起点模型空间位置；`w`：起点半径 |
| `16` | `float4` | `xyz`：终点模型空间位置；`w`：终点半径 |
| `32` | `float4` | `xy`：起点 UV；`zw`：终点 UV |

### 2. 调用示例

使用 `HairDotsGpuStream.CreateSimulationBuffer`（或手动创建步长为 `HairGpuStrandSegment.Stride` 的 `Structured` `GraphicsBuffer`），在模拟 Kernel 写入当前线段后、`RTASBuildPass` 执行前录制展开：

```csharp
var stream = new HairDotsGpuStream(segmentCount);
meshFilter.sharedMesh = stream.Mesh;

stream.RecordGpuUpdate(
    commandBuffer,
    simulationSegments,
    segmentCount,
    conservativeObjectSpaceBounds,
    Time.frameCount,
    topologyVersion);
```

> [!NOTE]
> - **包围盒与动态 RTAS**：调用方必须提供保守覆盖所有模拟顶点的模型空间包围盒（V1 不含 GPU 包围盒归约），且 Renderer / RTAS 实例必须标记为动态几何。销毁 Groom 时需释放 `stream` 与模拟缓冲区。
> - **自动与显式历史重置**：首次更新、线段数或 `topologyVersion` 变化、GPU 存储重建及帧号不连续时会自动触发历史重置（将当前中心线同时写入当前/历史槽，并将上一帧半径标记为负值一帧，使 Hit Shader 旁路过期的上一帧物体矩阵）。若发生未体现在 `topologyVersion` 中的瞬移，可调用 `RequestHistoryReset()` 或传入 `forceHistoryReset: true`。

---

## 材质参数与 Inspector (`VividRP/Material/Hair`)

专用 Hair 材质 Inspector 将属性分为四组（保持底层序列化属性名稳定，旧材质无需迁移）：

- **Absorption（吸收）**：支持 `Color`（直接指定吸收颜色）、`Physical Melanin` 或 `Normalized Melanin`（通过黑色素浓度 `Melanin Concentration` 与红度 `Melanin Redness` 控制）。
- **Scattering（散射）**：纵向粗糙度 $\beta_m$（Longitudinal Roughness）与方位角粗糙度 $\beta_n$（Azimuthal Roughness）。
- **Fiber Interface（纤维界面）**：毛鳞片倾角（Cuticle Angle，角度制）、纤维折射率（Fiber IOR）以及解析 / Schlick 菲涅尔模式选择。
- **Emission（自发光）**：可选 HDR 自发光。

`HairInput.hlsl` 将材质属性加载并净化为统一的 `VividHairMaterialData` 结构体，由 Chiang 适配器、Reference Path Tracing Closest-Hit 与 Raytracing GBuffer Closest-Hit 共同消费。

---

## DLSS Ray Reconstruction 引导与运动向量

- **GBuffer 引导输出**：Hair GBuffer Closest-Hit 写入修正后的纤维位置、径向法线、纵向粗糙度、基础色与介电 `F0`。
- **统一运动向量重建**：Hit Shader 在当前 `segmentU` 处重建上一帧中心线，将径向表面坐标传输到上一帧发丝局部标架并应用上一帧半径，再乘上 `UNITY_PREV_MATRIX_M`。相机运动、物体变换、中心线变形/旋转及半径变化共用同一条运动向量链路。

## 当前限制

- 持久 GPU DOTS 更新需由调用方提供保守包围盒并在 `RTASBuildPass` 前完成调度（包内暂不包含内置 Groom 物理模拟调度器）；
- 暂不包含 glTF / Groom 资产导入器；
- 暂不包含光栅化毛发着色或光栅化阴影 Pass；
- 暂不包含远场（Far-Field）毛发 BCSDF、程序化 AABB 或原生 LSS 后端；
- NRD 发丝材质 ID / 粗细特化尚未启用。
- **验证基准**：未降噪的 Reference PT 累积结果是唯一正确性基线；调试引导信号时应将 DLSS-RR / REBLUR 预览与原始累积结果对照。

## 第三方源码声明

内嵌的 RTXCR 材质文件保留各自原始头文件与 MIT 许可声明，上游提交记录见 [`Shaders/Material/Hair/Vendor/RTXCR/NOTICE.md`](../Shaders/Material/Hair/Vendor/RTXCR/NOTICE.md)（未内嵌 RTXCR 几何或示例胶水代码）。
