# Streaming Virtual Texture Roadmap

## Context

当前 VT 系统已经完成了一个可运行的单空间 SVT MVP：`VirtualTextureDemoController` 可以注册一个 2D address space，`VirtualTextureDemoPass` 绑定 page table、physical cache 和 feedback buffer，demo shader 能按 UV 反馈缺页并采样物理页缓存。最新迭代已经让 `Assets/vt/UVTest.jpg` 可以作为 source texture 被 `VTTexture2DPageProducer` 按需切 tile，并在 shader 侧加入上下 mip 的三线性混合，降低 mip 切换硬边。

这份 roadmap 用 Unreal Engine 的 VT 架构作为长期参照，但不直接移植 UE 的复杂度。目标是把当前 demo 能力逐步演进成可用于正式材质和大纹理资产的 Streaming Virtual Texture 系统。

## Unreal Reference Map

参考路径位于 `E:\UnrealEngine`。关键点按职责拆分：

- `Engine/Source/Runtime/RenderCore/Public/VirtualTexturing.h`
    - `FVTProducerDescription` 描述 tile size、border、texture layer、physical group、fallback 等 producer 元数据。
    - `IVirtualTexture` 把 page 请求拆成 `RequestPageData(...)` 与 `ProducePageData(...)`。
    - `IVirtualTextureFinalizer` 把 page 生成和物理纹理写入放到明确的 render finalize 阶段。
    - `IAllocatedVirtualTexture` 管理 page table texture、physical texture、shader uniform packing。
- `Engine/Source/Runtime/Renderer/Private/VT/VirtualTextureSystem.*`
    - 统一负责 producer 注册、VT 分配、feedback 收集、请求去重、排序、提交、finalize。
- `Engine/Source/Runtime/Renderer/Private/VT/TexturePagePool.*`
    - 物理页池管理 allocation、lock、evict、remap，并维护 page table mapping。
- `Engine/Source/Runtime/Engine/Private/VT/VirtualTextureBuiltData.h`
    - 离线构建后的 tile/chunk/mip/codec 元数据。
- `Engine/Source/Runtime/Engine/Private/VT/UploadingVirtualTexture.*`
    - 从构建数据和 chunk 中读取 tile，处理 streamed page 请求。
- `Engine/Source/Runtime/Engine/Private/VT/VirtualTextureUploadCache.*`
    - 管理 staging upload buffer，并作为 finalizer 写入 physical texture。
- `Engine/Shaders/Private/VirtualTextureCommon.ush`
    - shader 侧统一处理 mip 计算、feedback、page table lookup、manual trilinear、physical UV、fallback。

对 VividRP 来说，最值得借鉴的是边界：producer、built data、request queue、physical pool、page table、upload finalizer、shader sampling 应分层演进。

## Current Baseline

当前实现的核心文件：

- `Runtime/SubSystem/VirtualTexture/VirtualTextureSystem.cs`
    - 管理全局 address space 注册、feedback readback 聚合、按 space 分发请求、统计输出。
- `Runtime/SubSystem/VirtualTexture/VTAddressSpace.cs`
    - 持有 descriptor、residency manager、page table updater、runtime producer、upload scheduler。
- `Runtime/SubSystem/VirtualTexture/VTResidencyManager.cs`
    - 管理 page resident/pending/locked 状态，维护 physical page LRU 和 eviction。
- `Runtime/SubSystem/VirtualTexture/VTPageTableUpdater.cs`
    - 重建完整 page table，未 resident 的 page 会 fallback 到可用 ancestor mip。
- `Runtime/SubSystem/VirtualTexture/VTUploadScheduler.cs`
    - 使用双 staging `Texture2DArray` 和 graphics fence 提交 copy upload。
- `Runtime/SubSystem/VirtualTexture/VTProducer.cs`
    - 当前包含 procedural、checker、Texture2D source producer。
- `Shaders/Core/Public/VirtualTexture/VirtualTexture.hlsl`
    - page table decode、mip 请求、feedback 写入、physical cache sampling。
- `Shaders/Material/VirtualTextureDemo.shader`
    - 当前 demo material 的 SVT 验证入口。

当前限制：

- 只支持单 layer、单 format、单 physical cache。
- page table 使用 structured buffer 和 CPU 全量 rebuild，尚未做局部更新。
- producer 当前偏同步，`Texture2D` source producer 仍是 runtime readback/copy 风格，不是磁盘 streamed asset。
- 缺少离线 SVT asset、chunk、codec、异步 IO、转码缓存。
- demo shader 独立于正式材质系统，尚未集成到 StandardLit 或 ShaderGraph 等材质路径。
- feedback 仍是直写 request buffer，缺少 UE 风格的低频采样、抖动、pending mip debug 和 request replay。

## Design Principles

- 先稳定单纹理 SVT，再扩展到 multi-layer stack。
- producer 只描述和生产 tile，不直接决定 physical pool 策略。
- residency 只管理虚拟页到物理页的生命周期，不承担资产读取或解码。
- upload/finalize 必须和 page table commit 分离，避免 page table 指向未完成上传的物理页。
- shader sampling API 要尽早稳定，后续材质接入只依赖公共 HLSL helper。
- 所有长期能力都要能被 demo scene 和 EditMode tests 验证，避免只靠人工看图。

## Milestone 0: Demo Stabilization

### Goal

把当前 `UVTest.jpg` demo 固化成 SVT 回归基准，保证后续重构不破坏最小可见功能。

### Scope

- 保持 `VirtualTextureDemoController` 默认使用 `Assets/vt/UVTest.jpg`。
- 保持 `VirtualTextureDemo.shader` 的 UV transform、feedback、fallback、manual trilinear。
- 增加明确的 debug acceptance：
    - normal view 能看到 UVTest 纹理。
    - Residency 模式能区分 resident、fallback、pending、invalid。
    - MipBias 模式能显示 requested mip 和 resolved mip 差异。
    - 小 cache 下能稳定 eviction，不出现随机闪烁。
- 扩展现有 shader contract tests，覆盖 trilinear path、dual mip feedback、source texture property。

### Acceptance

- `VirtualTextureProducerTests` 覆盖 Texture2D producer 的 gutter 与 edge clamp。
- `VirtualTextureShaderContractTests` 覆盖 `VTComputeRequestedMipRange` 和 `VTSamplePhysicalCacheTrilinear`。
- 手动验证：相机快速移动时，不出现大块 magenta 或整面抖动；mip 切换接缝明显弱于单 mip 采样。

## Milestone 1: Producer Contract Refactor

### Goal

把同步 `WritePage(...)` producer 拆成可扩展的 request/produce/finalize 模型，为异步 IO 和运行时生成做接口准备。

### Proposed Interfaces

- `VTProducerDesc`
    - name、tile size、border、virtual page count、mip count。
    - layer count、format、sRGB、fallback color。
    - producer priority、continuous update、persistent lowest mip。
- `IVTPageProducer`
    - `RequestPageData(...)` 返回 `Invalid`、`Saturated`、`Pending`、`Available`。
    - `ProducePageData(...)` 返回 `IVTPageFinalizer` 或 upload payload。
    - `GatherTasks(...)` 用于等待异步解码或 IO。
    - `CancelRequest(...)` 用于 eviction 或过期请求。
- `IVTPageFinalizer`
    - render readable phase，可选。
    - upload/write phase，必须。

### Implementation Notes

- 现有 `IVTRuntimePageProducer.WritePage(...)` 保留为 adapter，避免一次性重写全部 tests。
- `VTTexture2DPageProducer` 先实现 adapter path，后续替换成 asset producer。
- `VTUploadScheduler` 应只处理 upload payload，不直接调用 producer 同步写像素。

### Acceptance

- 现有 procedural/checker/Texture2D producer 都能通过 adapter 正常工作。
- request 状态在 tests 中可模拟 pending 和 saturated。
- page table 只在 upload commit 后切换到 resident。

## Milestone 2: SVT Asset Build Data

### Goal

新增离线构建后的 SVT 资产格式，摆脱运行时读取完整 `Texture2D` 的限制。

### Data Model

- `VividVirtualTextureAsset`
    - source texture GUID/path。
    - tile size、border size、mip count。
    - layer descriptors：format、sRGB、fallback color。
    - chunk descriptors：mip range、byte size、codec、data offset。
    - tile index table：mip、x、y 到 chunk offset。
- `VividVirtualTextureBuiltData`
    - runtime-only immutable data。
    - 可被 build/importer 生成并序列化。

### Build Pipeline

- 初始版本只支持单 layer RGBA32 或 R8G8B8A8_UNorm。
- 先按 mip 分 chunk，高分辨率 mip 可一 mip 一个 chunk，低分辨率 mip 可合并到 mip tail。
- border 在 build time bake，runtime 不再为 source texture 重新补 gutter。
- 后续再支持 BCn/ASTC/raw GPU format。

### Acceptance

- 通过 editor importer 从 `UVTest.jpg` 构建一个 SVT asset。
- 删除 source texture readable 需求后，demo 仍能按需加载同样内容。
- tests 能验证 tile offset、mip count、fallback color、chunk bounds。

## Milestone 3: Streaming Asset Producer

### Goal

实现真正的磁盘 streamed page producer。

### Scope

- page request 去重：同一 producer/layer/mip/address 只发起一次 IO。
- request priority：active game camera 优先，SceneView 次之，低 hit count 请求排后。
- async read：从 chunk 读取所需 tile payload。
- decode/transcode：先支持 raw RGBA32，后续加入压缩格式。
- task cache：同一 tile 正在解码时，后续请求复用 task。
- request retirement：过期、evicted、producer flush 时可取消或丢弃。

### Acceptance

- 大图 SVT asset 不需要常驻内存。
- cache 较小时，相机移动触发渐进加载，fallback 到 coarse mip。
- 连续相机移动时 request queue 不无限增长。

## Milestone 4: Upload Cache And Finalizer

### Goal

把 upload 从当前 address space 的即时调度中抽离，形成全局 upload cache 和 render finalize 阶段。

### Scope

- 按 format/tile size 管理 staging upload pool。
- 支持多 tile batch upload。
- 支持 per-frame upload memory budget。
- finalizer 在 RenderGraph 中统一执行 copy/update。
- upload 完成后再 commit residency，并触发 page table dirty。

### Acceptance

- upload budget 降低时不会破坏 page table，一切未完成请求保持 pending/fallback。
- 同一 frame 内 producer 可提交多个 finalizer。
- tests 能模拟 fence passed/not passed，验证 page table commit 时机。

## Milestone 5: Physical Pool And Page Table Upgrade

### Goal

从每个 address space 一个 physical cache 过渡到可共享、可统计、可局部更新的 physical pool。

### Scope

- `VTPhysicalPoolDesc`
    - format、tile size、border、page count、layer group。
- `VTPhysicalPool`
    - allocation、free、lock、evict、touch。
    - 按 producer 和 page identity 查询已有 physical page。
    - 支持 region flush 和 producer flush。
- page table partial update
    - 当前全量 rebuild 先保留。
    - 新增 dirty page table update list。
    - 后续改成 compute/upload buffer 局部写入。

### Acceptance

- 两个相同 format/tile size 的 VT 可以共享 physical pool。
- flush producer 后只清理相关 page。
- debug panel 能看到 pool resident/free/locked/evicted 统计。

## Milestone 6: Multi-Layer Stack

### Goal

支持正式材质需要的多 layer stack，而不是只有单 RGBA layer。

### Target Layers

- BaseColor。
- Normal。
- ORM 或 Metallic/Roughness/AO。
- Height 或 Mask，作为可选 layer。

### Scope

- 扩展 `VTStackDesc` 为 layer 数组。
- 每个 layer 有 format、sRGB、fallback、physical group。
- producer 一次请求可产出多个 layer。
- page table lookup 共享，physical cache sampling 按 layer/physical group 取纹理。
- shader helper 提供统一采样 API：
    - `VTSampleBaseColor(...)`
    - `VTSampleNormal(...)`
    - `VTSampleMask(...)`

### Acceptance

- 一个 material 可以从 SVT stack 采样 base color 和 normal。
- base color/normal 分别使用正确 sRGB/linear 处理。
- fallback 对每个 layer 独立生效。

## Milestone 7: Feedback Quality And Prefetch

### Goal

减少 page pop、闪烁和 request thrash，提高视角移动时的稳定性。

### Scope

- feedback downsample：不是每像素都写 request，降低 UAV 压力。
- feedback jitter：跨帧轮转采样位置。
- pending mip debug：统计 requested mip 和 resolved mip 差距。
- dual mip request：manual trilinear 时同时请求 lower/upper mip。
- neighbor prefetch：请求当前 page 时可预取四邻域。
- velocity/camera prefetch：根据相机运动方向提高前方 page 优先级。

### Acceptance

- 快速移动时 fallback sample count 和 pending count 可控。
- 与 M0 demo 对比，纹理清晰度恢复更平滑。
- feedback overflow 有可见统计和预算调参入口。

## Milestone 8: Material Integration

### Goal

把 SVT 从 demo pass 推进到正式材质路径。

### Scope

- 在 package shader library 中提供稳定 public HLSL。
- 为 VividRP material shader 加入 SVT sampling branch。
- 材质 inspector 暴露 virtual texture asset/stack binding。
- RenderGraph pass 自动绑定当前 frame 所需 VT spaces。
- 支持普通 texture 和 SVT texture feature switch。

### Acceptance

- `VirtualTextureDemo.shader` 不再是唯一 SVT 消费者。
- 至少一个正式 lit material 能使用 SVT base color。
- 非 SVT 材质不承担不必要的 UAV/feedback 绑定成本。

## Milestone 9: Adaptive VT And Sparse Blocks

### Goal

支持更大纹理、更稀疏的 tile 地址空间，以及局部高分辨率区域。

### Scope

- sparse block 或 UDIM block 描述。
- Morton address 编码。
- tile offset data 支持空洞区域。
- adaptive page table indirection。
- low mip persistent allocation。

### Acceptance

- 非方形、非完整填充的大纹理不会浪费大量 tile offset 数据。
- 只在需要的区域分配高分辨率 page table。
- shader 侧能通过 indirection 解析 adaptive region。

## Milestone 10: Tooling, Debugging, And Automation

### Goal

让 SVT 可维护、可定位、可回归。

### Scope

- Debug overlay：
    - residency heatmap。
    - requested/resolved mip。
    - physical page id。
    - feedback overflow。
    - upload queue。
    - pool usage。
- Editor tooling：
    - SVT asset builder。
    - source texture conversion wizard。
    - tile preview。
    - chunk/mip stats。
- Automation：
    - request record/replay。
    - cache pressure deterministic tests。
    - scene-based visual validation。
    - performance counters snapshots。

### Acceptance

- 一次回归能回答：缺页来自哪里、为什么没加载、是否被 eviction、是否 upload budget 不足。
- 关键 VT 场景能通过 batch test 或截图基准做自动验证。

## Suggested Delivery Order

1. M0 稳定 demo 与测试。
2. M1 抽象 producer/request/finalizer。
3. M2 构建 SVT asset data。
4. M3 实现 streaming asset producer。
5. M4 抽离 upload cache/finalizer。
6. M5 重构 physical pool 与 page table update。
7. M6 接入 multi-layer stack。
8. M7 优化 feedback 和 prefetch。
9. M8 集成正式材质。
10. M9/M10 做 adaptive、大世界和完整工具链。

M1 和 M2 是最关键的分水岭。只要 producer contract 和 asset build data 定稳，后续 upload cache、multi-layer、shared pool 都可以独立推进；如果继续在同步 `Texture2D` producer 上堆功能，后续会出现较大返工。

## Testing Strategy

- EditMode tests:
    - descriptor validation。
    - page table fallback。
    - producer request 状态。
    - upload finalizer commit 时机。
    - tile offset/chunk lookup。
    - multi-layer fallback。
- Shader contract tests:
    - public HLSL helper 名称与关键分支。
    - manual trilinear。
    - feedback encode/decode。
    - layer sampling API。
- Runtime or PlayMode tests:
    - camera movement feedback。
    - cache pressure。
    - async upload completion。
    - material integration。
- Manual acceptance:
    - `Assets/vt/UVTest.jpg` 平面 demo。
    - 大图 streamed demo。
    - 小 cache stress demo。
    - multi-layer lit material demo。

## Risks And Open Questions

- Unity `Texture2DArray` 和 `Graphics.CopyTexture` 在不同平台上的格式支持差异需要尽早验证。
- RenderGraph 中 UAV feedback 与普通材质路径的绑定成本需要控制，不能让非 SVT 材质付费。
- 压缩格式策略需要早定：先 raw GPU-ready，再引入 BC/ASTC。不要在 asset format 尚未稳定前投入复杂 codec。
- 多 camera、SceneView/GameView 并存时，request priority 和 cache affinity 需要保持确定性。
- Adaptive VT 不应过早实现。它依赖 asset format、physical pool、page table indirection 和 shader API 都稳定后再推进。
