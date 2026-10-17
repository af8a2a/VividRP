# RenderGraph 可序列化资源描述符与 PassBypass

返回索引：[Documentation~ 索引](README.md) · 相关文档：[RenderGraph 编辑器](RenderGraphEditor.md) | [SubSystem 局部子图](RenderGraphSubSystem.md) | [RTAS 加速结构支持](AccelerationStructureSupport.md)

## 概述

VividRP 的 RenderGraph 提供了与 Unity CoreRP `TextureDesc`、`BufferDesc` 和 `RayTracingAccelerationStructureDesc` 一一对应的**可序列化资源描述符**。资源属性可以直接保存在 `.vrdg` 与 `RenderGraphData` 资产中，支持在 Graph Toolkit 编辑器和自定义 Property Drawer 中声明式配置。

---

## 核心描述符类型

| 描述符类型 | 源码路径 | 对应运行时包装类型 | 主要配置项与工厂方法 |
| --- | --- | --- | --- |
| `RenderGraphTextureDesc` | `Runtime/RenderGraph/Resource/RenderGraphTextureDesc.cs` | `RenderGraphTexture` | • **配置项**：尺寸/切片/维度、颜色格式与深度位数、MSAA/过滤/寻址/各向异性/MipBias、MipMap 生成、Clear 设置、`EnableRandomWrite`、动态缩放、名称与 Fallback 纹理<br>• **转换与工厂**：`ToTextureDesc()`、`FromTextureDesc(...)`、`CreateColorTarget(w, h, format)`、`CreateDepthTarget(w, h, depthBits)` |
| `RenderGraphBufferDesc` | `Runtime/RenderGraph/Resource/RenderGraphBufferDesc.cs` | `RenderGraphBuffer` | • **配置项**：`Count`、`Stride`、`GraphicsBuffer.Target`（`Structured`、`Append`、`IndirectArguments`、`Raw` 等）、调试名称<br>• **转换与工厂**：`ToBufferDesc()`、`FromBufferDesc(...)`、`CreateStructured(count, stride)`、`CreateAppend(count, stride)`、`CreateIndirectArguments(count)` |
| `RenderGraphAccelerationStructureDesc` | `Runtime/RenderGraph/Resource/RenderGraphAccelerationStructureDesc.cs` | `RenderGraphAccelerationStructure` | • **配置项**：用于调试和 Profiling 的 `Name` 元数据<br>• **转换与工厂**：`ToAccelerationStructureDesc()`、`FromAccelerationStructureDesc(...)`、`Create(name)` |

---

## 节点与端口集成

### 1. 资源节点与相机历史

- `TextureResourceNodeData`：持有 `RenderGraphTextureDesc`（默认通过 `CreateColorTarget(1920, 1080)` 初始化），输出 `RenderGraphTexture`。
- `BufferResourceNodeData`：持有 `RenderGraphBufferDesc`（默认通过 `CreateStructured(1, 4)` 初始化），输出 `RenderGraphBuffer`。
- `AccelerationStructureResourceNodeData`：持有 `RenderGraphAccelerationStructureDesc`，输出 `RenderGraphAccelerationStructure`。
- `CameraHistoryTexture` / `CameraHistoryBuffer`：跨帧持久化资源按相机在运行时管理，由 `CameraHistoryRenderGraphBridge` 转换描述符并将请求的历史帧导入 RenderGraph。

### 2. 端口描述符存储 (`RenderGraphPortData`)

路径：`Runtime/RenderGraph/Data/RenderGraphPortData.cs`

编译后的端口数据可按资源类型保存描述符：
- 字段：`TextureDesc`、`BufferDesc`、`AccelerationStructureDesc`
- 访问方法：`GetDescriptor()`（根据端口类型返回对应描述符）、`SetDescriptor(object)`（设置对应类型并清空其余字段）

### 3. 代码创建示例

```csharp
// 1. 创建自定义纹理描述符
var textureDesc = RenderGraphTextureDesc.CreateColorTarget(2048, 2048, GraphicsFormat.R16G16B16A16_SFloat);
textureDesc.UseMipMap = true;
textureDesc.AutoGenerateMips = true;

// 2. 创建 Append Structured Buffer 描述符
var bufferDesc = RenderGraphBufferDesc.CreateStructured(1024, 16);
bufferDesc.Target = GraphicsBuffer.Target.Append;

// 3. 创建光线追踪加速结构描述符
var rtasDesc = RenderGraphAccelerationStructureDesc.Create("SceneRTAS");
```

---

## Pass 旁路 (`[PassBypass]`)

对于“读取输入资源并产出替换纹理或 Buffer”的 Pass，可以使用 `[PassBypass]` 声明禁用状态下的**零 GPU 开销旁路**，无需在 Pass 未激活时执行冗余拷贝：

```csharp
[RenderGraphResource(Access = AccessFlags.Read)]
private RenderGraphTexture m_Source = new();

[RenderGraphResource(Access = AccessFlags.Write)]
[PassBypass(nameof(m_Source))]
private RenderGraphTexture m_Output = new();

public override bool IsActive(ContextContainer frameData)
{
    return MySettings.Resolve(frameData).enabled;
}
```

### 旁路规则与约束

1. **零开销句柄转发**：当 `IsActive` 返回 `false` 时，`PassRecorder` 会直接跳过该 Pass 的 RenderGraph builder 录制，并将输出字段句柄直接转发到源字段句柄。
2. **参数契约**：`[PassBypass(...)]` 的参数必须是 **C# 字段名**（推荐使用 `nameof(m_Source)`），而不是 `RenderGraphResource.Name` 的显示名称。
3. **类型与访问权限**：当前支持 `RenderGraphTexture` 和 `RenderGraphBuffer` 字段；源字段与输出字段必须类型相同，且源字段必须可读（`Read`）、输出字段必须可写（`Write`）。
4. **典型案例（`StopNaNPass`）**：`StopNaNPass` 不再由 `PassRecorder` 硬编码注入，而是显式放置在 `.vrdg` 图中并串联下游；当相机关闭 Stop NaNs 选项时，其输出直接旁路转发至输入，不产生任何 GPU 调度。
5. **`ReadWrite` 原地修改无需声明**：对于 `AccessFlags.ReadWrite` 的同一个资源字段，跳过未激活 Pass 时原有句柄天然保持有效并继续供下游读取，无需标记 `[PassBypass]`。
