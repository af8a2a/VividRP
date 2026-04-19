# VividRP VirtualTextureSystem v1 方案

**Summary**
- 在 `Runtime/SubSystem/VirtualTexture` 增加一个仅做“核心 + 管线接入层”的 `VirtualTextureSystem` 模块，面向未来 `Decal / Terrain / Shadow` 复用，但 v1 不实现任何具体业务生产者。
- 核心模型固定为：`多 Virtual Texture Space` 共用一套系统编排，`每个 Space 独占一个 Texture2DArray 物理缓存`，`Page Feedback 走 GPU 写入 + CPU 异步读回调度`。
- 生命周期对齐现有子系统：`Initialize/Deinitialize + FrameContextSystem.SubsystemPreRender`，并在 `VividRenderPipeline.Dispose` 中显式释放。

**Key Changes**
- 定义不可变注册描述 `VirtualTextureSpaceDesc`，字段固定包含：`SpaceName`、`PageSize`、`BorderSize`、`VirtualPageCountX/Y`、`MipCount`、`CachePageCount`、`GraphicsFormat`、`MaxUploadsPerFrame`、`FeedbackCapacity`。Space 在初始化时注册，运行中不允许热改描述。
- `VirtualTextureSystem` 负责全局编排，不负责业务内容生成。它维护 `VirtualTextureSpaceState`，其中包含：CPU 页表镜像、`Texture2DArray` 物理缓存、空闲列表、LRU 链表、驻留字典、待上传队列、脏页表区段、统计信息。
- 页表 GPU 形态固定为 `GraphicsBuffer`，按 mip 展平存储。每条 `VirtualTexturePageTableEntry` 用一个 `uint` 打包：`bits 0-19 = physicalPageId`，`20-25 = resolvedMip`，`26 = resident`，`27 = fallback`，`28 = pendingUpload`，`29 = locked`，`30-31` 保留。
- CPU 侧页表不做 shader 递归回退。任何缺失子页在调度阶段都会被写成“当前最佳祖先页”的映射，shader 只做一次页表查询，不向上追父节点。
- 物理缓存固定为“一个 page 对应 `Texture2DArray` 的一个 slice”。每个 slice 尺寸为 `PageSize + 2 * BorderSize`；v1 不做 atlas-in-slice，也不做多 space 共 cache。
- 分配策略固定为：优先空闲列表，其次淘汰“最久未使用且未锁定”的 resident page；`pending upload`、`locked`、本帧刚分配的页不可淘汰。LRU touch 时机固定为“反馈命中去重后”和“上传成功提交后”。
- 待上传项固定为 `VirtualTextureUploadRequest`，至少包含：`SpaceId`、`VirtualPageCoord(X,Y,Mip)`、`PhysicalPageId`、`Generation`、`Priority`、`RequestFrame`。`Generation` 用于丢弃晚到的上传结果，避免被复用的物理页被旧请求覆盖。
- Feedback 使用相机维度的双缓冲状态，建议复用 `CameraRelativeSystem` 模式实现 `VirtualTextureFeedbackCameraState`。每个 `Camera + Space` 持有一对 `request buffer + counter buffer`，当前帧写 A、异步读回 B，下一帧交换，避免 stall 和覆盖未完成读回的数据。
- Feedback entry 固定为 64-bit packed key，编码 `SpaceId + X + Y + Mip`；CPU 读回后按 key 去重并累加命中次数，再按 `Mip 升序、命中次数降序、Game Camera 优先于 SceneView` 排序，最后按 `MaxUploadsPerFrame` 截断。
- `Runtime/RenderGraph/FrameContext` 新增 `VividVirtualTextureFrameData : ContextItem`，暴露每个 active space 的 `VirtualTextureSpaceBinding`。binding 固定包含：`GraphicsBuffer PageTableBuffer`、`Texture2DArray PhysicalCache`、`GraphicsBuffer FeedbackRequests`、`GraphicsBuffer FeedbackCounter`、`VirtualTextureSpaceShaderParams`。
- RenderGraph 接入方式固定为：`page table / feedback buffer` 通过现有 `RenderGraphBuffer.SetImportedBuffer(...)` 导入；`Texture2DArray` 物理缓存保持为持久外部纹理，pass 直接绑定，不把它做成 transient RenderGraph texture。
- `VirtualTextureSystem.Update(frameData, cmd)` 的执行顺序固定为：处理完成的 readback、合并/排序 fault、执行分配与淘汰、生成 upload queue、刷新脏页表 buffer、填充 `VividVirtualTextureFrameData`、上报 stats。这个更新点挂到 `FrameContextSystem.SubsystemPreRender`。
- Shader 公共契约放到 `Shaders/Core/Public/VirtualTexture/VirtualTexture.hlsl`。v1 只支持“单 shader 一次绑定一个 space”，固定暴露 `_VTPageTable`、`_VTPhysicalCache`、`_VTFeedbackRequests`、`_VTFeedbackCounter`、`_VTSpaceParams`、`_VTMipOffsets`，并提供 `VTResolveAddress`、`VTComputePhysicalUVW`、`VTWriteFeedback` 三个最小 helper。
- 新增 `VirtualTextureStats` / `VirtualTextureStatsRegistry`，字段至少覆盖：active space 数、resident/free page 数、pending upload 数、eviction 数、fault 数、dedup 后请求数、最近一次 readback frame、最近一次状态消息。模式对齐现有 `VividGPUDrivenStatsRegistry`。

**Test Plan**
- `Tests/Editor` 新增纯 C# 单测，覆盖 `VirtualTexturePageTableEntry` 打包/解包、mip 展平索引、祖先 fallback 物化逻辑。
- 为 cache allocator 增加单测，验证空闲分配、LRU 淘汰、锁页保护、generation 失效保护，以及“本帧新页不可被立刻回收”。
- 为 feedback processor 增加单测，验证双缓冲交换、重复 fault 去重、优先级排序、`MaxUploadsPerFrame` 截断和多 camera 合并规则。
- 为 `VividVirtualTextureFrameData` 和 `VirtualTextureSystem.Update` 增加单测，验证只对 `Game/SceneView` 创建反馈状态、deinitialize 后释放所有 GPU 资源与 camera state。
- 增加源码契约测试，确认 `VirtualTexture.hlsl` 暴露的固定符号名与 C# 侧 property ID 一致，避免未来接入业务 shader 时接口漂移。

**Assumptions**
- v1 只做 runtime core、FrameContext/RenderGraph 接入、最小 HLSL 契约和 stats，不做 editor authoring、debug UI、具体业务 pass。
- v1 的“物理缓存”职责包含页面槽位管理和 upload queue 生成，但不包含具体内容生产；`Decal / Terrain / Shadow` 后续各自消费 `VirtualTextureUploadRequest` 并完成真正的数据填充。
- v1 只保证 `Game` 和 `SceneView` 相机的 feedback/统计路径；`Preview / Reflection` 默认跳过。
- v1 不新建独立 asmdef，全部落在现有 `VividRP.Runtime` 里，避免额外程序集边界和 RenderGraph/FrameContext 引用扩散。
- v1 不支持单个 shader 同时采样多个 VT space；如未来需要，再在不改页表编码的前提下扩展多绑定接口。
