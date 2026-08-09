# RenderGraphEditor (Graph Toolkit)

## 目标

使用 `com.unity.graphtoolkit` 在 Editor 中提供一个可视化 RenderGraph 资产（`.vrdg`）的编辑与验证，并通过 `ScriptedImporter` 编译成可在运行时使用的 `RenderGraphData`。

## 资产流转

- Authoring：`.vrdg`（Graph Toolkit 的 `RenderGraphEditorGraph` + 各类 NodeData）
- Import：`RenderGraphImporter` 监听 `.vrdg`，每次导入/变更会生成运行时数据模型 `RenderGraphData`
- Runtime：`VividRenderPipelineAsset.RenderGraphAsset` 引用 `RenderGraphData`，`PassRecorder` 在运行时实例化 Pass 并绑定资源

## 当前支持的节点

- `TextureResourceNodeData`：定义一个 `RenderGraphTextureDesc`，输出端口类型为 `RenderGraphTexture`
- `BufferResourceNodeData`：定义一个 `RenderGraphBufferDesc`，输出端口类型为 `RenderGraphBuffer`
- `RenderPassNodeData`：选择一个实现 `IRenderPass` 的脚本类型，并基于 Pass 上的 `[RenderGraphResource]` 字段反射生成输入端口

> 端口目前统一用“输入端口”表达“该 Pass 使用该资源”。是否读/写由 Pass 字段上的 `[RenderGraphResource(Access=...)]` 决定。

### Pass 自持资源

- `[RenderGraphResource]` 现在支持 `BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable`
- 该模式适合“Pass 在构造函数里创建资源实例，`Prepare()` 每帧修正 descriptor”的固定资源
- Editor 中该字段默认不显示输入端口，但如果字段可写，仍会保留输出端口给后续 Pass 使用
- Pass 节点会额外显示一个 `Override <ResourceName>` 选项；打开后重新暴露输入端口，允许用图上的资源节点覆盖
- 仅建议对已经具备完整默认 descriptor 初始化逻辑的字段启用该模式；如果字段必须依赖外部资源才有意义，继续使用默认的 `External`

`DrawObjectPass` 的 `RenderList` 使用该模式：默认在节点 Inspector 中直接配置 `Render List Descriptor`，图上不显示 RenderList 输入端口。打开 `Override RenderList` 后可以连接共享或动态的 `RenderGraphRenderList`；端口未连接时仍回退到节点内嵌 descriptor。

### Pass 名称

- RenderPass 节点标题会编译为运行时 Pass 名称，并用于 RenderGraph pass、Profiler 生命周期 marker 和编译预览
- 空白标题回退到 Pass 类型名；允许多个节点使用相同标题
- `IRenderGraphRecordingPass` 创建的内部子 pass 继续使用代码显式提供的名称，外层 authored pass marker 使用节点标题

### Pass 内部临时资源

- `[TransientResource]` 适合仅在单个 Pass 内部使用、也不希望暴露到 RenderGraph 图上的 `RenderGraphTexture` / `RenderGraphBuffer`
- 该标记不会生成输入/输出/debug 端口，也不会显示 override 选项
- 标记后的资源在录制 Pass 时通过 CoreRP builder 的 `CreateTransientTexture` / `CreateTransientBuffer` 创建，不能跨 Pass 或跨帧传递
- 跨 Pass 资源继续使用普通 `[RenderGraphResource]` 字段或资源节点；跨帧资源继续使用 history 流程
- 持久化纹理与 buffer 历史分别由 `CameraHistoryTexture` 和 `CameraHistoryBuffer` 管理，并通过 `CameraHistoryRenderGraphBridge` 导入 RenderGraph
- 纹理 history 运行时由 `BufferedRTHandleSystem` 做双缓冲物理存储，但对 Pass 暴露的仍是 `RenderGraphTexture`
- `PassRecorder` 会在录制阶段自动把 history 逻辑资源导入 RenderGraph，并在图执行成功后提交 history；不再依赖图尾的显式 copy 更新

## 使用方式（最小闭环）

1. 菜单创建：`Assets/Create/VividRP/Standard Render Graph` 会从内置标准模板生成一份可运行管线；`Assets/Create/VividRP/Render Graph` 仍用于创建空图
2. 双击打开 `.vrdg`，添加资源节点与 Pass 节点
3. 将资源节点输出连接到 Pass 节点对应的资源端口
4. 在 `VividRenderPipelineAsset` 的 `RenderGraphAsset` 字段中引用该 `.vrdg` 资产（其主对象为 `RenderGraphData`）

## 标准模板

- 内置模板位于 `Editor/RenderGraph/Templates/StandardRenderGraph.vrdg.txt`
- 模板内容基于项目示例 `Assets/Hybrid.vrdg`，包含预深度、GBuffer、阴影、天空、延迟光照、后处理、抗锯齿和最终 Blit 等基础链路
- 新项目建议优先从 `Assets/Create/VividRP/Standard Render Graph` 创建，再按项目需求删减或替换 Pass

## SubSystem

- 主图现在支持基于 Graph Toolkit `Local Subgraph` 的 `SubSystem`
- `SubSystem` 适合把一组 `RenderPass` 与私有资源折叠成高层节点
- 详细使用方式见 `Documentation/RenderGraphSubSystem.md`

## 运行时执行

`PassRecorder` 会在第一次渲染或检测到 `ImportVersion` 变化时：

- 从 `RenderGraphData` 读取 Pass 列表、节点名称、序列化参数与资源绑定
- 通过反射实例化 Pass，并把绑定的资源（运行时副本）写入对应字段
- 调用 `Create()` / `Initialize()` 生成 `PassResource`
- 每帧调用 `Prepare()`，并把 Pass 记录到 Unity RenderGraph
- 使用每帧缓存避免同一个 `RenderGraphTexture/Buffer` 被重复 `CreateTexture/CreateBuffer`

## 后续扩展方向

- 支持更多资源类型（Acceleration Structure 等）
- 支持 Import/Export（相机颜色/深度、外部纹理/缓冲）
- 更强的图验证（循环、未绑定资源、资源生命周期/读写冲突提示）
- Pass 参数化（在图中配置 Pass 的可序列化参数）
