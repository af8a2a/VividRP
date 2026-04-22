# RenderGraph SubSystem

## 目标

`SubSystem` 是基于 Graph Toolkit `Local Subgraph` 的一层编辑器期抽象，用来把一组 `RenderPass` 与其私有资源折叠成一个高层节点，降低主 `RenderGraph` 的复杂度。

`SubSystem` 只存在于 Authoring / Import 阶段。`.vrdg` 导入后仍然只生成当前的扁平运行时数据 `RenderGraphData`，不会引入新的 runtime 层级执行模型。

## 适用场景

- 把一段固定的后处理链路折叠成单个节点
- 把一组只在局部使用的中间 `Texture / Buffer / RenderList / RTAS` 收进局部系统
- 在主图里只保留少量明确的输入/输出资源，隐藏内部实现细节

## v1 约束

- 仅支持 `Local Subgraph`
- 仅支持一层 `SubSystem`，不允许嵌套
- 仅支持显式 I/O，不做自动接口推导
- 运行时仍然展开为扁平 `RenderGraphData`
- 支持在 `SubSystem` 内放置 `Pass`、私有资源节点与 `History` 节点

## 边界类型

`SubSystem` 输入/输出变量只允许以下资源类型：

- `RenderGraphTexture`
- `RenderGraphBuffer`
- `RenderGraphRenderList`
- `RenderGraphAccelerationStructure`

以下内容 **不** 作为 `SubSystem` 边界传递：

- `float / int / bool / enum`
- 任何普通对象引用
- 控制流语义
- 专门的 `History Prev/Curr` 边界语义

> 标量参数继续保留在内部 `RenderPass` 的参数或选项上，不通过 `SubSystem` 端口传递。

## Authoring 工作流

### 1. 在主图中创建 SubSystem

1. 创建或打开一个 `.vrdg`
2. 在主图中使用 Graph Toolkit 提供的 `Local Subgraph` 创建入口
3. 选择 `RenderGraphSubSystemGraph`
4. 主图中会出现一个内建 `Subgraph Node`，节点标题等于子系统图名称

> `SubSystem` 只允许从主图创建。进入 `SubSystem` 后不会继续提供创建下一层子系统的能力。

### 2. 进入 SubSystem 编辑

双击主图中的 `SubSystem` 节点，进入子图编辑。

在 `SubSystem` 内部可以继续使用现有节点生态：

- `RenderPassNodeData`
- `TextureResourceNodeData`
- `BufferResourceNodeData`
- `RenderListResourceNodeData`
- `AccelerationStructureResourceNodeData`
- `HistoryResourceNodeData`

### 3. 定义显式输入/输出

`SubSystem` 对外接口完全由子图中的 `Input / Output Variable` 决定。

推荐做法：

1. 在 `SubSystem` 内新建一个 `Input variable`
2. 类型设置为允许的资源类型之一
3. 为它创建 `Variable Node`
4. 把该变量节点输出连接到内部 `Pass` 的输入端口

输出同理：

1. 在 `SubSystem` 内新建一个 `Output variable`
2. 类型设置为允许的资源类型之一
3. 为它创建 `Variable Node`
4. 把内部产出的资源连接到这个输出变量节点

创建完成后，主图中的 `SubSystem Node` 会自动暴露对应输入/输出端口。

## 连接规则

### 输入

- 主图中接到 `SubSystem` 输入端口的资源，会在编译时重写为子图内部 `Input variable` 的真实来源
- 内部 `Pass` 读取的是外层真实来源，而不是额外复制的一份资源

### 输出

- 子图中连接到 `Output variable` 的资源，会在编译时作为 `SubSystem` 输出端口的真实来源
- 主图下游 `Pass` 读取的是该内部真实生产者

### 私有资源

- `SubSystem` 内部资源节点默认是私有实现细节
- 它们会参与最终编译并进入全局描述符列表
- 主图不能直接访问这些私有资源

## 推荐的构建方式

一个典型结构如下：

1. 主图上游 `Pass` 产生资源
2. 资源进入 `SubSystem`
3. `SubSystem` 内部完成若干步处理
4. 最终结果通过 `Output variable` 暴露回主图
5. 主图下游 `Pass` 继续消费

例如：

```text
Main Pass A
    -> SubSystem(InputColor)
        -> Internal Pass 1
        -> Internal Pass 2
        -> Internal Private Texture
        -> OutputColor
    -> Main Pass B
```

## History 资源说明

- `HistoryResourceNodeData` 可以作为 `SubSystem` 内部私有实现存在
- 跨 `SubSystem` 边界时，不提供专门的 `Prev / Curr` 端口语义
- 如果把 history 相关结果暴露到外部，它只按普通 `RenderGraphTexture` 处理

## 导入与运行时行为

`.vrdg` 导入时：

1. Importer 先把主图与所有 `SubSystem` 展开成一份语义扁平图
2. 再沿用现有编译逻辑生成 `RenderGraphPassDefinition`
3. 最终写入同一份扁平 `RenderGraphData`

这意味着：

- runtime 不知道 `SubSystem` 的存在
- `PassRecorder` 仍按现有扁平 Pass 列表执行
- 不需要修改运行时资源绑定模型

## 校验规则

### 子系统内部

进入子图后，仍然沿用现有的 pass / resource / history 校验逻辑。

### 主图层面

主图会额外校验：

- `SubSystem` I/O variable 是否为允许的资源类型
- 是否出现嵌套 `SubSystem`
- 子图整体是否合法

如果子图整体不合法，主图中的 `SubSystem Node` 会收到一个汇总错误；具体错误需要进入子图查看。

## 限制与注意事项

- 仅支持本地子图，不支持 Asset Subgraph
- 不支持嵌套 `SubSystem`
- 不支持通过当前连线自动推导 `SubSystem` 接口
- 不支持标量参数穿越 `SubSystem` 边界
- 不支持主图直接访问或预览内部私有资源
- `SubSystem` 输出变量应保持单一内部来源，避免歧义

## 最小示例

### 目标

把一段“输入颜色 -> 局部处理 -> 输出颜色”的处理链折叠为一个 `SubSystem`。

### 步骤

1. 在主图创建一个 `SubSystem`
2. 进入子图
3. 新建 `Input variable`：`InputColor : RenderGraphTexture`
4. 新建 `Output variable`：`OutputColor : RenderGraphTexture`
5. 放置一个内部 `RenderPass`
6. 将 `InputColor` 连接到该 `Pass` 输入
7. 将该 `Pass` 输出连接到 `OutputColor`
8. 回到主图，把上游资源连到 `InputColor`
9. 把 `OutputColor` 连到下游 `Pass`

最终主图中只保留一个高层节点，但运行时仍会展开成真实的 Pass 依赖链。

## 何时不建议使用

以下情况更适合继续留在主图直接表达：

- 只有一两个 Pass，结构本身已经足够简单
- 需要大量跨边界暴露资源，导致 `SubSystem` 端口和主图一样复杂
- 需要跨边界传递大量标量参数
- 需要多层次递归复用

当一个局部流程“内部资源很多，但外部接口很少”时，`SubSystem` 的收益最高。
