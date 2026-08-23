# VirtualTexture Core Architecture

## 目标

VirtualTexture Core 是 VividRP 中负责虚拟纹理运行时调度的通用设施。它当前的职责不是绑定某一种具体 VT 资产格式，而是提供一组可复用的底层能力：

- 注册 producer，并把 producer 生命周期从 page-table address space 中解耦。
- 分配可被材质采样的 `VTAllocatedVirtualTexture`。
- 维护 page-table space、resident page 状态和 shared physical pool。
- 从 GPU feedback 中聚合 page fault 请求，驱动 residency 和 upload。
- 为渲染 pass 生成每帧 binding table，统一绑定 page table、physical cache、feedback buffer 和 shader 参数。
- 支持多 layer stack、physical group、per-layer format，以及多个 physical texture array。

当前 Core 仍然偏向 2D page table 和最多 4 个 layer/group 的实现，后续具体 VT 系统应优先复用这些基础设施，再在 producer、asset build、材质选择和高级策略上扩展。

## 核心模块

### `VirtualTextureSystem`

路径：`Runtime/SubSystem/VirtualTexture/Core/VirtualTextureSystem.cs`

`VirtualTextureSystem` 是全局 VT runtime 的协调者。它持有以下核心表：

- `s_ProducerRegistry`：producer handle 到 producer 实例和 runtime page producer 的映射。
- `s_Allocations` / `s_AllocationIdsByName` / `s_AllocationIdBySpaceId`：材质可采样 VT 对象的分配表。
- `s_PageTableSpaces` / `s_SpaceIdsByName`：page-table space 表。
- `s_PhysicalPools`：按 `VTPhysicalPoolDesc` 共享的 physical pool。
- `s_FeedbackCameraSystem`：每 camera、每 space 的 feedback/readback buffer 状态。
- `s_UploadScheduler`：跨 space 的 upload batch 调度器。

它的每帧更新流程由 `UpdateCore(...)` 驱动，负责收集 feedback、处理 residency、调度 upload、刷新 page table、准备本帧 binding table，并向 debug stats registry 上报统计信息。

### Producer Registry

路径：`Runtime/SubSystem/VirtualTexture/Core/VTProducerRegistry.cs`、`Runtime/SubSystem/VirtualTexture/Core/VTProducer.cs`

`VTProducerHandle` 是 VT Core 对 producer 的稳定引用。注册 producer 后，`VTProducerRegistry` 会：

- 用 producer 名称和 `VirtualTextureSpaceDesc` 生成 `VTProducerDesc`。
- 复用描述符和 producer 身份一致的已有 producer entry。
- 将外部 `VTProducer` 适配为内部 `IVTPageProducer`。
- 用引用计数管理 producer 释放。

这样 allocation、page-table space 和 producer 不需要互相持有强语义关系。未来不同 VT 资产系统可以注册自己的 producer，并共享同一套 allocation 和 residency 管线。

### Allocation

路径：`Runtime/SubSystem/VirtualTexture/Core/VTAllocation.cs`

`VTAllocationDesc` 描述一个可被材质采样的 VT 对象：

- `Name`
- `VirtualTextureSpaceDesc`
- `VTProducerHandle`
- `PrivateSpace`
- `ShareDuplicateLayers`

`VTAllocatedVirtualTexture` 是分配结果，包含：

- `AllocationId`
- `SpaceId`
- `ProducerHandle`
- `SpaceDesc`

目前 `RegisterAddressSpace(...)` 仍保留为兼容入口，内部会注册 producer 并创建默认 allocation。新的系统应优先走：

```text
RegisterProducer -> AllocateVirtualTexture -> frame binding lookup
```

### Page-Table Space

路径：`Runtime/SubSystem/VirtualTexture/Core/VTAddressSpace.cs`

文件名仍为 `VTAddressSpace.cs`，核心类型已经收窄为 `VTPageTableSpace`。它现在位于 `Core/`，代表一个 page-table space，而不是某个具体 asset。

主要职责：

- 持有 `VirtualTextureSpaceDesc`、space id、producer handle。
- 构建 mip offset 表和 page-table buffer。
- 拥有一个 `VTResidencyManager`。
- 引用一个共享 `VTPhysicalPool`。
- 初始化并锁定最低 mip，保证 fallback path 可用。
- 将 residency 状态转换为 `VirtualTexturePageTableEntry`。
- 生成 `VirtualTextureSpaceBinding` 供本帧渲染使用。

`VTPageTableSpace` 不直接表达 asset 身份。asset 或材质层面的身份由 `VTAllocatedVirtualTexture` 和 producer 系统承载。

### Stack 与 Layer

路径：`Runtime/SubSystem/VirtualTexture/Core/VTStackDesc.cs`

`VTStackDesc` 描述一个 VT stack 的 page 和 layer 结构：

- `PageSize`
- `BorderSize`
- `CachePageCount`
- `LayerCount`
- `MaxUploadsPerFrame`
- `FeedbackCapacity`
- `NeighborPrefetchCount`

`VTLayerDesc` 描述一个逻辑 layer：

- `Semantic`：例如 `BaseColor`、`Normal`、`Mask`
- `GraphicsFormat`
- `SRGB`
- `FallbackColor`
- `PhysicalGroup`

当前约束：

- 最大 layer 数为 `VTStackDesc.MaxLayerCount`，当前值为 4。
- physical group index 必须小于最大 layer 数。
- physical group 需要紧凑地从 0 开始。
- 同一个 physical group 内的 layer storage format 必须一致。

### Physical Pool

路径：`Runtime/SubSystem/VirtualTexture/Core/VTPhysicalPool.cs`

`VTPhysicalPool` 是 physical page 的共享缓存。多个 page-table space 可以共享同一个 physical pool，只要它们的 `VTPhysicalPoolDesc` 完全匹配。

`VTPhysicalPoolDesc` 的 key 包含：

- page size
- border size
- page count
- layer count
- physical group count
- per-layer semantic
- per-layer physical group
- per-layer graphics/storage format
- per-layer sRGB flag

当前实现已经拆成多个 physical texture array：

```text
VTPhysicalPool
    group 0 -> Texture2DArray
    group 1 -> Texture2DArray
    group 2 -> Texture2DArray
    group 3 -> Texture2DArray
```

每个 group 的 texture array depth 为：

```text
cachePageCount * groupLayerCount
```

逻辑 layer 到 physical slice 的映射由 pool/desc 维护：

```text
physicalGroup = GetLayerPhysicalGroup(layerIndex)
localLayer    = GetLayerPhysicalLayerIndex(layerIndex)
slice         = physicalPageId * groupLayerCount + localLayer
```

物理页状态、LRU、锁定、共享绑定、producer/page identity 也由 `VTPhysicalPool` 管理。`VTResidencyManager` 只记录本 space 的 virtual page 到 physical page 映射，并通过 pool 完成共享、分配、复用和淘汰。

### Residency

路径：`Runtime/SubSystem/VirtualTexture/Core/VTResidencyManager.cs`

`VTResidencyManager` 是单个 page-table space 的 residency 状态机。

它维护：

- 每个 virtual page 的 `PhysicalPageId`
- generation
- resident / pending upload / locked 状态
- pending request 列表
- dirty page-table update 标记

核心行为：

1. 对 feedback 聚合结果调用 `ProcessRequests(...)`。
2. 已 resident 的 page 只更新 physical pool LRU。
3. pending upload 的 page 不重复请求。
4. 可共享时通过 `VTPhysicalPool.TryAttachResidentPage(...)` 复用已有 physical page。
5. 需要新页时通过 `VTPhysicalPool.TryAllocatePage(...)` 分配或淘汰。
6. 请求提交后通过 `TryCommitUpload(...)` 将 page 标记为 resident。

最低 mip 会在 `VTPageTableSpace` 初始化时被 bootstrap，并以 locked page 的形式保留，用于所有 miss 的 fallback。

### Page Table

路径：`Runtime/SubSystem/VirtualTexture/Core/VTPageTableUpdater.cs`、`Runtime/SubSystem/VirtualTexture/Core/VirtualTextureTypes.cs`

`VTPageTableUpdater` 将 residency 状态构建为 GPU 可读的 structured buffer：

```text
StructuredBuffer<uint> _VTPageTable
```

每个 `VirtualTexturePageTableEntry` 打包为一个 `uint`，包含：

- physical page id
- resolved mip
- resident
- fallback
- pending upload
- locked

rebuild 时从最低 mip 向最高精度 mip 传播最佳可用 parent mapping。未 resident 的高精度页可以 fallback 到已 resident 的祖先页，从而避免无效采样。

### Feedback

路径：`Runtime/SubSystem/VirtualTexture/Core/VirtualTextureFeedback.cs`、`Runtime/SubSystem/VirtualTexture/Core/VirtualTextureFeedbackBindingUtility.cs`

shader 通过 `_VTFeedbackRequests` 和 `_VTFeedbackCounter` 写入反馈。CPU 侧按 camera/space 管理双缓冲 readback 状态，并形成 `VirtualTextureFeedbackBatch`。

`VirtualTextureFeedbackProcessor` 负责：

- 编码/解码 feedback key。
- 聚合重复请求。
- 记录 view id、camera priority、active view 信息。
- 输出按 space 分组的 `VirtualTextureAggregatedFeedbackRequest`。

每帧 `VirtualTextureSystem.UpdateCore(...)` 会收集已完成 readback、聚合请求、计算 prefetch bias，再交给对应的 `VTPageTableSpace.ProcessRequests(...)`。

### Upload Scheduler

路径：`Runtime/SubSystem/VirtualTexture/Core/VTUploadScheduler.cs`

`VTUploadScheduler` 负责把 pending page request 转换为 GPU texture copy：

```text
producer finalizer -> staging Texture2DArray -> physical group Texture2DArray
```

关键点：

- upload batch 按 page size、storage format、layer count 分组。
- 每个 upload pool 维护双 batch，batch in-flight 时通过 graphics fence 延迟 commit。
- `IVTPageFinalizer` / `IVTMultiLayerPageFinalizer` 负责把 producer 结果写入 staging texture。
- copy 到 physical cache 时按 physical group 和 local layer 计算目标 slice。
- fence 通过后，scheduler 调用 `IVTUploadRequestCommitter.TryCommitUpload(...)`，将 page 标记为 resident 并更新 page table。

当前 staging texture 使用 `TextureFormat.RGBA32`。这适合当前 Color32 finalizer 管线；如果未来要上传高精度格式或压缩格式，应扩展 staging/upload path，使 staging format 与目标 physical group storage format 显式匹配。

### Frame Binding Table

路径：`Runtime/RenderGraph/FrameContext/VividVirtualTextureFrameData.cs`、`Runtime/SubSystem/VirtualTexture/Core/VirtualTextureTypes.cs`

`VividVirtualTextureFrameData` 是 RenderGraph frame context 中的 VT binding table。每帧由 `VirtualTextureSystem` 重建。

每个 `VirtualTextureSpaceBinding` 包含：

- `BindingIndex`
- `AllocationId`
- `SpaceId`
- `ProducerHandle`
- `PageTableBuffer`
- `PhysicalCaches`
- group0 兼容入口 `PhysicalCache`
- feedback request/counter buffer
- `VirtualTextureSpaceShaderParams`
- mip offsets
- layer fallback colors

渲染 pass 可以：

- 用 `TryGetDefaultBinding(...)` 获取默认 binding。
- 用 `TryGetBinding(index, ...)` 获取指定 binding。
- 用 `TryGetBindingForAllocation(allocationId, ...)` 绑定某个材质/asset 对应 VT。

目前已有 pass 仍主要使用 default binding。后续材质级多 VT 支持应转向 allocation id 或显式 binding index。

### Shader Binding

路径：`Shaders/Core/Public/VirtualTexture/VirtualTexture.hlsl`

VT shader contract 当前包含：

```hlsl
StructuredBuffer<uint> _VTPageTable;
TEXTURE2D_ARRAY(_VTPhysicalCache);
TEXTURE2D_ARRAY(_VTPhysicalCache1);
TEXTURE2D_ARRAY(_VTPhysicalCache2);
TEXTURE2D_ARRAY(_VTPhysicalCache3);
float _VTSpaceParams[32];
float _VTMipOffsets[VIVID_VT_MAX_MIPS];
float4 _VTLayerFallbacks[4];
```

`VirtualTextureSpaceShaderParams` 将 CPU 侧结构投影到 `_VTSpaceParams[32]`。其中 0-19 是基础参数和 layer semantic/sRGB 信息，20-31 是 physical group 信息：

```text
20..23: group0..3 layer count
24..27: layer0..3 physical group
28..31: layer0..3 physical layer index inside group
```

采样流程：

1. `VTResolveAddress(...)` 从 page table 解析 physical page id 和 resolved mip。
2. `VTComputePhysicalUVWLayer(...)` 计算 page-local uv 和 physical slice。
3. `VTGetLayerPhysicalGroup(...)` 找到目标 physical cache group。
4. `VTSamplePhysicalCacheGroup(...)` 选择 `_VTPhysicalCache / 1 / 2 / 3`。
5. `VTSampleBaseColor`、`VTSampleNormal`、`VTSampleMask` 根据 semantic layer 采样并应用 fallback/sRGB 规则。

`VirtualTextureFeedbackBindingUtility.BindSpaceGlobals(...)` 和 debug visualization pass 会绑定最多 4 个 physical cache。不存在的 group 会回退绑定到 group0，避免 shader 访问空 texture。

## 每帧数据流

```text
GPU draw
    -> shader resolves page table
    -> shader samples physical cache
    -> shader writes feedback requests/counters

next frames
    -> VirtualTextureFeedbackBufferState readback completes
    -> VirtualTextureSystem collects feedback batches
    -> VirtualTextureFeedbackProcessor aggregates and groups by space
    -> VTPageTableSpace processes requests
    -> VTResidencyManager allocates or reuses physical pages
    -> VTUploadScheduler requests producer payloads
    -> producer finalizer writes staging texture
    -> scheduler copies slices into physical group textures
    -> graphics fence passes
    -> upload committer marks pages resident
    -> VTPageTableUpdater rebuilds and refreshes GPU page table
    -> VividVirtualTextureFrameData publishes binding table
```

## Render Pass 集成

当前 runtime pass 通过 `VividVirtualTextureFrameData` 获取 VT binding：

- `GBufferPass`：在 VT render list 绘制前绑定 VT globals 和 feedback UAV。
- `VirtualTextureFeedbackPass`：专门的 VT feedback/GBuffer 路径。
- `VisibilityBufferPass`：写入 GPUDriven visibility；alpha test 可采样 VT，但不写 feedback。
- `VisibilityBufferGBufferResolvePass`：按 GPUDriven private allocation 绑定资源，在重建 UV 导数后采样并写 feedback。
- `VirtualTextureVisualizationPass`：debug visualization path。

`VirtualTextureDemoController` 现在只验证 `MeshletRenderer`、`GPUDrivenMaterialProxy` 和兼容的
`GPUDrivenSurface` VT asset，实际渲染复用上述 VisibilityBuffer 流程。`VirtualTextureDemoPass`
仅作为旧 RenderGraph 资产的序列化兼容节点保留，不再注册 page-table 依赖、绑定 feedback UAV
或执行绘制；旧 `VirtualTextureDemo.shader` 同样不再写 feedback。

`GPUDrivenMaterialProxy` 的纹理 payload 按后端互斥。Bindless 模式保存
`BaseMap / BumpMap / MaskMap`；Virtual Texture 模式只保存 `StreamedVirtualTexture`，不再重复
持有这三张源贴图。BaseColor、UV tiling/offset、normal strength、mask interpretation、材质因子、
alpha clip、cull mode 和 lighting flags 仍属于两种后端共享的必要材质数据。Editor 首次把
Bindless Proxy 转为 VT 时优先烘焙 Proxy 当前 raw maps；进入 VT 模式后的刷新则从源 Material，
或从已有 `.vividvt` importer 的 source references，临时解析源贴图。构建完成后 Proxy 继续保持
SVT-only。旧的 dual-payload Proxy 在显式同步前仍可读取，VT runtime
只提交 SVT，Bindless runtime 只提交普通贴图。`SourceMaterial` 目前仍作为 Editor provenance 和
RTAS fallback 保留，不属于提交给 VT 后端的 surface texture payload。

`MeshletRenderer` 转换工具不再把生成资产直接散落在源资产旁，而是在源 Mesh 所在目录下
建立以下结构：

```text
GPUDrivenGenerated/
├─ MaterialProxy/
├─ MeshletAsset/
└─ SVT/
   └─ Bin/
```

一次转换以源 Mesh 所在目录作为生成根，因此 Mesh 与 Material 分处不同目录时，三类产物仍位于
同一个 `GPUDrivenGenerated`。MeshletCollection 归入 `MeshletAsset`，MaterialProxy 归入
`MaterialProxy`，`.vividvt` 序列化对象位于 `SVT`，其 `.stream` 二进制位于 `SVT/Bin`。
Proxy 文件名携带稳定资源标识，避免同名 Material 或 Mesh 在统一目录中碰撞。该规则只用于转换
生成的资产；手工 VT、Terrain VT 和 SourceMaterial 匹配且已经绑定的旧邻近 Proxy 保持原路径与引用。

旧 Demo 场景迁移时：先通过 `MeshletRenderer` Inspector 的 takeover 流程捕获并移除源
`MeshRenderer`，再通过 GPUDriven Material Proxy Editor 构建并绑定 `GPUDrivenSurface` VT asset，
最后将这些已配置资源赋给 `VirtualTextureDemoController` 做一致性校验。RenderGraph 中旧
`VirtualTextureDemoPass` 节点可在图编辑器中删除；删除前它是可裁剪的空兼容节点。

通用绑定入口是：

```csharp
VirtualTextureFeedbackBindingUtility.BindSpaceGlobals(...)
VirtualTextureFeedbackBindingUtility.BindFeedbackTargets(...)
```

这保证 page table、physical caches、shader params、mip offsets、layer fallback、debug mode 和 feedback UAV 的绑定规则集中在一个地方。

## 扩展新 VT 系统的推荐路径

### 1. 定义 asset/build data

具体 VT 系统应将 asset authoring/build data 与 runtime Core 解耦。build data 至少需要能生成：

- `VirtualTextureSpaceDesc`
- layer 列表和 physical group 布局
- producer 可索引的 tile/page 数据

### 2. 实现 producer

实现 `VTProducer`，并通过 runtime adapter 或内部 producer 能力提供：

- page request 状态查询
- page finalizer 生成
- multi-layer upload
- request cancel/retire

Core 只要求 producer 能在给定 `VirtualTextureSpaceDesc` 和 `VTRequest` 时提供页面数据。

### 3. 注册 producer 并分配 VT 对象

推荐流程：

```text
VTProducerHandle handle = VirtualTextureSystem.RegisterProducer(desc, producer)
VTAllocatedVirtualTexture vt = VirtualTextureSystem.AllocateVirtualTexture(
    new VTAllocationDesc(name, desc, handle))
```

材质或实例系统应保存 `AllocationId`，渲染时用 binding table 找到对应 `VirtualTextureSpaceBinding`。

### 4. 接入材质/渲染 pass

短期可以使用 default binding。多 VT 或多材质实例应改为：

```text
allocation id -> binding table lookup -> bind space globals -> draw
```

如果同一 draw 中需要多个 VT 对象，当前 global binding 模式还不够，需要进一步引入 bindless/table-driven shader binding。

## 当前约束与后续演进点

- shader 最多支持 4 个 layer 和 4 个 physical group。
- page table 是 2D space，尚未抽象出 3D、UDIM 或 sparse mega texture 的 page addressing。
- `VirtualTextureSystem` 仍是静态全局 subsystem，多 world/多 pipeline 隔离还没有做。
- render pass 多数仍使用 default binding，材质级 allocation 选择尚未贯穿。
- streamed payload 保持线性 RawRGBA32 staging；提交前按 physical group 转换到真实 storage format。
  当前 cache 只接受非压缩 color format，BC/ASTC 仍需要独立 block codec/transcode 路径。
- physical pool sharing 以 descriptor 完全匹配为前提，未来可继续引入更细的 pool policy。
- page table entry 只存 physical page id/resolved mip/status，不存 group；group 由 layer shader params 决定。
- page table 首次构建全量上传，后续由 residency dirty list 驱动子树重算，并合并连续 entry 做局部 buffer upload。
- feedback 仍以 shader 全局 UAV 为主，未来多 VT 同 draw 或 bindless feedback 需要重新设计 feedback key 和绑定模型。

## 设计原则

- page-table space 只负责地址翻译和 residency，不直接代表 asset。
- producer 只负责提供页面内容，不拥有 page table 或 physical pool。
- allocation 是材质/asset 可采样对象的身份，不等同于 physical cache。
- physical pool 是可共享资源，按 descriptor 复用。
- frame binding table 是渲染侧唯一入口，避免 pass 直接读写 subsystem 内部状态。
- shader 通过稳定参数表理解 layer/group 布局，CPU 侧负责生成映射。
