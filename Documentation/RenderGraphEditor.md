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

## 使用方式（最小闭环）

1. 菜单创建：`Assets/Create/VividRP/Render Graph`
2. 双击打开 `.vrdg`，添加资源节点与 Pass 节点
3. 将资源节点输出连接到 Pass 节点对应的资源端口
4. 在 `VividRenderPipelineAsset` 的 `RenderGraphAsset` 字段中引用该 `.vrdg` 资产（其主对象为 `RenderGraphData`）

## 运行时执行

`PassRecorder` 会在第一次渲染或检测到 `ImportVersion` 变化时：

- 从 `RenderGraphData` 读取 Pass 列表与资源绑定
- 通过反射实例化 Pass，并把绑定的资源（运行时副本）写入对应字段
- 调用 `Create()` / `Initialize()` 生成 `PassResource`
- 每帧调用 `Prepare()`，并把 Pass 记录到 Unity RenderGraph
- 使用每帧缓存避免同一个 `RenderGraphTexture/Buffer` 被重复 `CreateTexture/CreateBuffer`

## 后续扩展方向

- 支持更多资源类型（Acceleration Structure 等）
- 支持 Import/Export（相机颜色/深度、外部纹理/缓冲）
- 更强的图验证（循环、未绑定资源、资源生命周期/读写冲突提示）
- Pass 参数化（在图中配置 Pass 的可序列化参数）
