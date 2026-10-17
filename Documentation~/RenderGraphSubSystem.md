# RenderGraph SubSystem（局部子图）

返回索引：[Documentation~ 索引](README.md) · 相关文档：[RenderGraph 编辑器](RenderGraphEditor.md) | [可序列化资源描述符](RenderGraphResourceDescriptors.md)

## 目标

`SubSystem` 是基于 Graph Toolkit `Local Subgraph` 的一层**编辑器期抽象**，用来将一组 `RenderPass` 与其私有资源折叠为一个高层节点，显著降低主 `RenderGraph` 的视觉复杂度。

> [!IMPORTANT]
> `SubSystem` 仅存在于 Authoring / Import 阶段。`.vrdg` 导入后仍然编译为扁平的运行时数据 `RenderGraphData`，不会引入额外的运行时层级执行开销。

![RenderGraph SubSystem 示例](Image/SubGraph.png)

## 适用场景与取舍

| 推荐使用 `SubSystem` | 建议直接保留在主图 |
| --- | --- |
| 内部资源与中间 Pass 很多，但对外输入/输出接口很少 | 只有一两个 Pass，结构本身已足够简单 |
| 把一段固定的后处理、天空或光照链路折叠为单个高层节点 | 需要跨边界暴露大量资源，导致子图端口与主图一样复杂 |
| 将局部使用的中间 `Texture / Buffer / RenderList / RTAS` 隐藏为私有实现细节 | 需要跨边界传递大量标量参数，或需要多层次嵌套复用 |

---

## 边界契约与 V1 约束

### 1. 允许跨边界传递的 I/O 变量类型

| 允许作为 `SubSystem` 输入/输出 | **不允许**作为 `SubSystem` 边界传递 |
| --- | --- |
| `RenderGraphTexture`<br>`RenderGraphBuffer`<br>`RenderGraphRenderList`<br>`RenderGraphAccelerationStructure` | • `float` / `int` / `bool` / `enum` 等标量参数（继续保留在内部 `RenderPass` 节点选项上）<br>• 任意普通 C# 对象引用<br>• 控制流语义或专用 `History Prev/Curr` 边界语义 |

### 2. 核心约束

- **仅支持 `Local Subgraph`**：不支持外部独立资产形式的 Asset Subgraph。
- **单层结构**：仅允许从主图创建一层 `SubSystem`，不支持嵌套子系统。
- **显式接口**：对外端口完全由子图内的 `Input / Output Variable` 显式声明，不做基于连线的自动推导。
- **单一输出源**：每个 `SubSystem` 输出变量应保持单一内部生产者来源，避免编译歧义。

---

## Authoring 工作流

### 1. 在主图中创建并进入 SubSystem

1. 打开一个 `.vrdg` 主图资产。
2. 使用 Graph Toolkit 的 `Local Subgraph` 创建入口，选择 `RenderGraphSubSystemGraph`。
3. 主图中会生成一个内建 `Subgraph Node`（节点标题即子系统图名称）。
4. 双击该 `SubSystem` 节点即可进入子图编辑（内部同样支持 `RenderPassNodeData`、专用 Pass 节点及四类资源节点与 History 节点）。

### 2. 定义显式输入与输出

| 方向 | 配置步骤 |
| --- | --- |
| **输入 (`Input variable`)** | 1. 在子图 Blackboard 中新建 `Input variable`，类型设为允许的四种资源类型之一<br>2. 将其拖入子图创建 `Variable Node`<br>3. 将该变量节点的输出连接到子图内部 `Pass` 的输入端口 |
| **输出 (`Output variable`)** | 1. 在子图 Blackboard 中新建 `Output variable`，类型设为允许的四种资源类型之一<br>2. 将其拖入子图创建 `Variable Node`<br>3. 将内部 `Pass` 产出的资源连接到该输出变量节点 |

定义完成后，回到主图即可看到 `SubSystem Node` 自动暴露出对应的输入/输出端口。

---

## 编译期展开与连接语义

```mermaid
flowchart LR
    U["主图上游 Pass"] -->|InputColor| S_In["SubSystem Input Variable"]
    subgraph SubSystem ["SubSystem (编译期展开)"]
        S_In --> P1["Internal Pass 1"]
        P1 --> T["Internal Private Texture"]
        T --> P2["Internal Pass 2"]
        P2 --> S_Out["SubSystem Output Variable"]
    end
    S_Out -->|OutputColor| D["主图下游 Pass"]
```

- **零拷贝边界重写**：
  - **输入**：主图连接到 `SubSystem` 输入端口的资源，在编译时直接重写为子图内部 `Input variable` 的真实上游来源，不会产生额外资源复制。
  - **输出**：子图连接到 `Output variable` 的内部资源，在编译时直接作为主图下游 `Pass` 的真实生产者。
- **私有资源隔离**：子图内部的资源节点默认作为私有实现细节参与全局描述符编译，主图无法直接访问或预览它们。
- **运行时透明**：`RenderGraphImporter` 先将主图与所有 `SubSystem` 展开为一份语义扁平图，再生成 `RenderGraphPassDefinition` 并写入 `RenderGraphData`，`PassRecorder` 的运行时执行逻辑完全无需感知 `SubSystem`。

---

## 校验规则

- **子系统内部**：沿用主图相同的 Pass、资源描述符与 History 校验规则。
- **主图边界**：额外校验 `SubSystem` I/O 变量类型是否合法、是否存在嵌套 `SubSystem` 以及子图整体是否通过验证。若子图内部存在错误，主图中的 `SubSystem` 节点会显示汇总报错提示，双击进入子图即可定位具体节点错误。
