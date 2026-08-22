# VividPrimitiveScene v0 原型

## 状态与目的

`VividPrimitiveScene` 是现有 GPUDriven 场景旁边的一份内部镜像。v0 的目的不是改变最终绘制结果，而是先验证以下基础能力：

- Renderer 粒度的稳定 Primitive 身份；
- Geometry、Material 的稳定逻辑句柄和引用生命周期；
- legacy instance 到 Primitive/DrawSection 的可追踪桥接；
- 与变化量相关的 CPU 更新和 GPU 增量上传；
- 为后续 Visibility Buffer、GPUScene、场景 SDF 和材质系统重构建立可演进的 ABI。

现阶段 `VividGPUDrivenSceneData` 仍然是最终图像的权威数据源。PrimitiveScene 的七张 GPU buffer 会被上传并绑定，但没有 shader 消费者读取它们。因此启用本原型不应改变主视图、阴影、Visibility Buffer、RendererList、RTAS 或 SDF 行为。

## v0 范围

- 一个 `MeshletRenderer` 或 `VividTerrain` 对应一个 Primitive。
- Renderer 的 submesh、Terrain 的 chunk 表达为同一 Primitive 下的多个 DrawSection。
- Primitive 只保存一份 bounds 和 transform；DrawSection 只描述 Geometry/Material 引用和来源 section。
- Geometry、Material 使用稳定逻辑句柄，GPU payload 暂时桥接现有 `VividGPUDrivenSceneData` 的 dense index。
- 所有类型和入口保持 `internal`，不增加 authoring 组件、序列化字段、Pipeline Asset 开关或公开注册 API。
- 数据源限定为 `VividMeshletRendererDatabase`。数据库之外的 Unity `Renderer` 不会自动进入 PrimitiveScene。

明确不在 v0 范围内：

- 不替换 `VividGPUDrivenSceneData`、现有 instance ID、Visibility Buffer ID 或 DrawProceduralIndirect 路径；
- 不接管 RendererList、ShaderLab RenderState、阴影或 RTAS/SBT 构建；
- 不迁移 Meshlet vertex/index residency、材质参数、Closure/MaterialProgram ABI、SDF payload 或 PerObjectBuffer；
- 不移除 `MeshletRenderer.LateUpdate`，也不引入集中式 transform authoring 收集；
- 不修改 `PipelineResources.asset`、RenderGraph 生成节点或其他同步生成资产。

## CPU 数据模型

### Primitive 与 DrawSection

Primitive 的权威 CPU 身份是来源 `EntityId`。`RegisterOrUpdate` 以该 ID 查找或创建稳定 slot；`Remove` 也以该 ID 定位对象。

每个 Primitive 记录：

- world bounds；
- current object-to-world 与 world-to-object；
- previous object-to-world；
- rendering layer、pass mask 和当前可映射 flags；
- 一个连续 DrawSection 区间。

Primitive slot 与 Transform slot 在 v0 中一一对应，但 GPU ABI 保留显式 `TransformIndex`，避免未来拆分存储时修改 Primitive ABI。

DrawSection 使用 best-fit 连续区间分配器。区间释放后会与相邻空闲区间合并；高水位只增长。section 数量不变的资源更新保留原区间，数量变化时先取得新区间、切换 Primitive 的 range，再释放旧区间，从而不暴露半更新状态。

### 稳定句柄

CPU 内部定义三类只读句柄：

```text
VividPrimitiveHandle         { Index, Generation }
VividPrimitiveGeometryHandle { Index, Generation }
VividPrimitiveMaterialHandle { Index, Generation }
```

共同不变量如下：

- `Index < 0` 或 `Generation == 0` 表示无效句柄；
- 新 slot 的 generation 从 `1` 开始；
- 删除后 slot 进入 free-list，generation 递增；从 `uint.MaxValue` 回绕时直接回到 `1`，永不产生零；
- 句柄有效性同时要求 slot 已分配且当前 generation 完全相等；
- free-list 复用 Index 后，旧句柄立即失效，不能解析到新对象；
- GPU 使用稳定的 `uint Index` 作为资源 ID，同时在记录或引用中携带 generation；
- Primitive 注册、删除以及 section 结构/身份变化会递增 `SceneRevision`。未来不能携带 generation 的时序数据必须在 revision 不匹配时整体失效；
- 删除 GPU 记录时写入携带新 generation 的无效 tombstone 并标脏，不能让旧的 `Valid` 位或 payload 留在 GPU 上。

### Geometry 与 Material 资源键

Geometry、Material slot 按逻辑资源键共享并引用计数。只有最后一个 DrawSection 释放引用后，资源 slot 才进入 free-list。

逻辑键由以下部分构成：

```text
ResourceDomain + ObjectId + OwnerId + SourceSectionIndex
```

当前资源域包括 `MeshletGeometry`、`TerrainGeometry`、`MaterialProxy`、`UnityMaterial`、`TerrainMaterial` 和 `MissingMaterial`。

- 有效 Unity 对象以“资源域 + 对象 EntityId”为键，`OwnerId` 为空且 section 为 `-1`，因此不同 Primitive 可以共享同一个 Geometry/Material 句柄；
- 缺失材质以“MissingMaterial + Primitive EntityId + source section index”为键，避免所有 `EntityId.None` 被错误合并；
- Terrain material 以 Terrain Primitive 自身的 `EntityId` 为对象键；同一 Terrain 的多个 chunk 共享一个 material handle，同时避免共享 TerrainData 的多个 Terrain 在启用 per-terrain RVT 时互相覆盖 dense payload；
- legacy dense geometry/material index 改变只更新稳定 slot 的 payload，不改变逻辑句柄。

### Legacy source identity

`VividGPUDrivenSceneDataBuilder` 在追加每个 `VividInstanceData` 时，同时追加一条仅 CPU 使用的平行 identity：

```text
Primitive EntityId
Geometry EntityId
Material EntityId
source submesh/chunk index
resource-domain flags
```

该 identity 用于在 legacy scene 重建后恢复稳定资源键，并生成 legacy instance 到 Primitive/DrawSection 的桥接。它不改变原有 GPUDriven instance ABI。

## GPU 表 ABI

所有 ABI 结构采用精确顺序布局，并以 Unity `GenerateHLSL(PackingRules.Exact)` 生成 HLSL。C# 结构是唯一源文件；生成的 HLSL 不应手工编辑。

| Buffer / C# 结构 | stride | 字段顺序与语义 |
|---|---:|---|
| `_VividPrimitiveData` / `VividPrimitiveData` | 64 B | `float4 WorldBoundsMin`；`float4 WorldBoundsMax`；`uint TransformIndex`；`uint DrawSectionOffset`；`uint DrawSectionCount`；`uint RenderingLayerMask`；`uint PassMask`；`uint Flags`；`uint Generation`；`uint CustomDataAddress` |
| `_VividPrimitiveTransformData` / `VividPrimitiveTransformData` | 128 B | `float4x4 ObjectToWorldMatrix`；`float4x4 WorldToObjectMatrix` |
| `_VividPrimitivePreviousTransformData` / `VividPrimitivePreviousTransformData` | 64 B | `float4x4 PreviousObjectToWorldMatrix` |
| `_VividPrimitiveDrawSectionData` / `VividPrimitiveDrawSectionData` | 32 B | `uint GeometryIndex`；`uint GeometryGeneration`；`uint MaterialIndex`；`uint MaterialGeneration`；`uint SourceSectionIndex`；`uint Flags`；两个 `uint` padding |
| `_VividPrimitiveGeometryData` / `VividPrimitiveGeometryData` | 16 B | `uint Generation`；`uint LegacyTopMeshLODStartIndex`；`uint LegacyTotalMeshLODCount`；`uint LegacyMeshLODLevelCount` |
| `_VividPrimitiveMaterialData` / `VividPrimitiveMaterialData` | 16 B | `uint Generation`；`uint LegacyMaterialIndex`；`VividRendererListID RendererListID`；`VividMaterialFlags MaterialFlags` |
| `_VividLegacyInstanceMappingData` / `VividLegacyInstanceMappingData` | 16 B | `uint PrimitiveIndex`；`uint PrimitiveGeneration`；`uint DrawSectionIndex`；`uint Flags`，bit 0 表示 mapping 有效 |

对应的全局计数为 `_VividPrimitiveCount`、`_VividPrimitiveDrawSectionCount`、`_VividPrimitiveGeometryCount`、`_VividPrimitiveMaterialCount` 和 `_VividLegacyInstanceMappingCount`；`_VividPrimitiveSceneRevision` 绑定当前结构 revision。Transform 数量在 v0 中隐含等于 Primitive slot 数。

`uint.MaxValue` 是无效 GPU index/address。无效 Geometry/Material payload 仍保留当前 generation，以便消费者区分 tombstone 与陈旧引用。

### flags

Primitive flags 只编码当前能从数据库无损映射的语义：

- `Valid`
- `Disabled`
- `FlipWindingOrder`
- `Static`
- `Skinned`
- `Terrain`
- `ReceiveShadows`

DrawSection flags 当前只有 `Valid` 和 `Terrain`。MaterialProgram、closure 类型、材质参数和 SDF 标记不在此 ABI 中。

## 每帧同步时序

单帧顺序固定为：

```text
VividGPUDrivenSceneDataBuilder.Build
    -> PrimitiveScene.BeginFrame
    -> 消费 VividMeshletRendererDatabase change journal
    -> 注册/更新/删除 Renderer 粒度 Primitive
    -> 必要时 RebuildLegacyBridge
    -> 更新稳定 Geometry/Material 的 legacy dense payload
    -> 上传 PrimitiveScene dirty ranges
    -> 现有 GPUDriven BufferSet 正常上传并继续绘制
    -> 绑定七张 PrimitiveScene buffer 和计数
```

数据库 journal 按 `EntityId` 合并一帧内的重复事件，事件类别为 `Added`、`Removed`、`Transform`、`RenderState` 和 `Resources`。删除事件覆盖同一实体之前的更新。首次创建、数据库清空或上一次同步抛出异常时执行 full reconciliation；同步异常会保留下一帧 full-resync 请求。

以下变化会要求重建 legacy bridge：

- full reconciliation；
- renderer 增删；
- Geometry/Material 或 section 结构变化；
- 可能改变 legacy instance 结构的 render-state 更新；
- legacy static/material dense 数据重建。

普通 transform 更新只定位 journal 中对应 Primitive，不扫描所有 Renderer，也不重建 legacy bridge。资源、render-state 或结构变化允许遍历当前 legacy instances，因为旧路径在这种情况下已经发生相应场景数据重建。

Profiler 中使用以下 marker 分离成本：

- `VividRP.PrimitiveScene.Sync`
- `VividRP.PrimitiveScene.RebuildLegacyBridge`
- `VividRP.PrimitiveScene.Upload`

## Previous Transform 规则

Previous Transform 通过“本帧移动集合”维护，不扫描全部 Primitive：

1. 新 Primitive 的 previous 等于 current，因此首次出现没有虚假速度；
2. Primitive 本帧第一次变化时，previous 写入更新前的 current，再写入新 current；
3. 在下一次 `BeginFrame`，上帧移动过的 Primitive 将 previous 追平已提交的 current；
4. 若对象本帧继续移动，随后再次把 previous 保持为更新前 current；
5. 若对象停止，追平只产生一帧 previous 上传，之后稳定帧不再上传。

因此 transform bookkeeping 的 CPU 成本与本帧和上帧移动过的 Primitive 数量相关，而不是场景总 Primitive 数量。

## Dirty page 与 GPU buffer 策略

每张 CPU GPU 表使用 blittable 连续存储，并以约 4 KiB 为目标划分记录页：

- 每页记录数为 `max(1, floor(4096 / stride))`；
- 写入只标记覆盖到的页；值未变化的 `SetIfChanged` 不标脏；
- 上传前对 dirty 页排序，只合并相邻页，彼此分离的页保持独立 range；
- buffer 首次创建、容量不足而扩容或显式整表 resync 时上传整个有效表；其余帧只上传 dirty ranges；
- `GraphicsBuffer` 容量是不小于有效记录数的 2 次幂，最少为一个元素，运行期间不会因为删除而自动缩容；
- 空场景仍创建七个可安全绑定的单元素占位 buffer，逻辑 count 保持零；
- 扩容只改变物理 capacity，不改变稳定逻辑 Index；
- 上传完成后清空 dirty 页。完全稳定的一帧应当具有零上传 range、零上传字节和零 `SetData` 调用。

Primitive、Geometry、Material 删除会写 tombstone 并产生 dirty page。DrawSection 释放会清零记录；其区间可以被 best-fit allocator 后续复用。GPU buffer 的物理高水位不会因这些删除回退。

需要特别区分两类成本：PrimitiveScene 的 dirty-page 增量上传，以及 v0 中仍然存在的 legacy `VividGPUDrivenSceneData` 全量上传。100k/10k 压测报告应分别记录两者，不能把旧路径成本归因到 PrimitiveScene。

## 诊断

`VividGPUDrivenStats` 内部携带 `VividPrimitiveSceneStats`，至少记录：

- active Primitive、DrawSection、Geometry、Material 数量；
- Primitive、Geometry、Material 的 slot/free slot 数量，以及 DrawSection 高水位；
- `SceneRevision`；
- 本帧变化 Primitive 数；
- full-resync 累计次数；
- dirty page 数；
- 最近一次上传 range 数和字节数。

buffer count/capacity、高水位和各表的 dirty/upload 分布应在性能验收时一并采集。诊断数据只用于观察原型，不构成公开 API。

## 验收清单

### Editor tests

- 句柄注册、更新、删除、slot 复用和 generation 从最大值回绕；旧句柄始终无效；
- 三 submesh Renderer 只产生一个 Primitive、三个 DrawSection 和一份 transform；
- Terrain 只产生一个 Primitive，chunk 映射到多个 section，并共享 Terrain material handle；
- 多个 Primitive 共享 Geometry/Material 时复用逻辑句柄，最后引用释放后才回收；
- legacy instance 顺序或 dense index 变化只更新 bridge/payload，不改变逻辑句柄；
- 多个缺失材质不会因为 `EntityId.None` 被错误共享；
- transform-only 更新只使 Primitive bounds/current/previous transform 对应页面变脏，Geometry、Material、DrawSection 不上传；
- 移动物体停止后一帧 previous 追平，随后稳定帧上传字节和 `SetData` 次数均为零；
- buffer 按 2 次幂扩容且删除后不缩容；用 `GraphicsBuffer.GetData` 验证 dirty 目标区和未修改区域；
- 空场景七张 buffer 均可安全绑定；
- C# 与生成 HLSL 的 stride 固定为 `64/128/64/32/16/16/16` 字节；
- 100k Primitive、随机更新其中 10k 时，PrimitiveScene 上传量只由触及的 dirty 页决定。

### 手动运行验收

在代表性 MeshletRenderer 与 Terrain 场景中对比改动前后：

- 主视图像素结果一致；
- Visibility Buffer ID 和可见性结果一致；
- 主光/附加光阴影一致；
- RTAS instance 数、mask、hit group/SBT 行为一致；
- RendererList 与 ShaderLab RenderState 行为一致；
- Frame Debugger 中旧绘制调用没有切换到 PrimitiveScene；
- Profiler 能看到 Sync、RebuildLegacyBridge、Upload 三段，并且 transform-only 帧不出现 bridge 全量重建；
- 删除、禁用、重启 Play Mode、场景切换和数据库清空后没有陈旧 Primitive 或无效 GPU 引用。

Unity Editor 正在运行时，不应另起 batchmode 测试进程。此时先完成静态 C#、ABI 和 shader 生成检查，再由当前 Editor 会话手动执行相关 Editor tests 与场景回归。

## 进入 Visibility 阶段的门槛

只有以下条件全部满足，才应让 Visibility/绘制消费者读取 PrimitiveScene：

1. 生命周期测试覆盖增删、复用、generation 回绕、场景切换和异常后的 full reconciliation，且不存在 stale handle；
2. MeshletRenderer 与 Terrain 的 Primitive/DrawSection 映射在实际内容中稳定，legacy bridge 能逐 instance 反查并验证；
3. ABI stride、字段顺序、无效值、flags 和 HLSL 生成产物已经冻结，并有自动测试防止漂移；
4. transform-only、停止后一帧和稀疏资源更新符合增量上传预期，100k/10k 压测没有退化为场景规模的 CPU 扫描或整表上传；
5. v0 图像、阴影、Visibility、RendererList 和 RTAS 回归完全等价，证明旁路镜像本身不引入行为变化；
6. 确定首个小范围消费者及回退路径。建议先让调试/验证 compute pass 读取表，再切换 PrimitiveID 生产，最后才迁移间接绘制；
7. 在切换绘制前单独完成材质程序/RenderState 分组设计。稳定 Material handle 不等同于已经解决类 Substrate 的程序选择、参数 residency 或 PSO 管理；
8. RTAS/SBT 与场景 SDF 仍作为独立里程碑，明确 ownership、更新频率和历史失效规则后再接入，不与首个 Visibility 切换捆绑。

首个消费者接入应当支持逐帧对照 legacy 结果，并保留快速回退到现有 GPUDriven 路径的能力。在对照期内，`SceneRevision`、generation 和 legacy mapping 必须全部参与一致性检查。
