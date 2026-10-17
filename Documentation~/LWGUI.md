# LWGUI 集成与 DXC Shader 性能分析后端

返回索引：[Documentation~ 索引](README.md)

VividRP 将 [LWGUI](https://github.com/JasonMa0012/LWGUI) 源码内嵌于 `ThirdParty/LWGUI` 目录下，以便直接在包内定制 Shader GUI 与着色器性能分析工具。

## 上游基线版本

- **分支**：`1.x`
- **提交**：`0acba799ec7d582ebb667ea6db0aefd32fd8c2ef`
- **版本**：`1.41.0`
- **保留目录**：`Editor`、`Runtime`、`UnityEditorExtension`、`LICENSE`、`README.md`、`README_CN.md`（未拷贝测试、独立包元数据、文档资源与原始 `.meta` 文件）。

## VividRP 定制适配

1. **相对路径图标加载**：上游 LWGUI 通过固定 Asset GUID 加载多处图标，内嵌后因不含原始 `.meta` 会失效。VividRP 通过 `ThirdParty/LWGUI/Editor/Helper/LwguiAssetPathUtility.cs` 将其替换为包内相对路径加载。
2. **移除内嵌 FXC**：自 `1.41.0` 起不再内嵌 `fxc.exe`，其 FXC 性能监控器直接从系统已安装的 Windows SDK 查找编译器。
3. **新增 DXC Shader Model 6 分析后端**：
   - 路径：`ThirdParty/LWGUI/Editor/PerformanceMonitor/ShaderCompiler/Dxc/`
   - 行为：将选中的 Unity D3D12 Shader 变体预处理为独立 HLSL，编译为 Shader Model 6 DXIL，并统计 IR 级 ALU 开销、纹理操作数、指令总数与 SSA 临时变量数量（注：SSA 计数仅作为寄存器压力参考指标，实际物理寄存器分配仍取决于驱动与 GPU 架构）。

## 上游升级核对清单

从上游更新 LWGUI 源码前，请务必审查并保留以下文件中的 VividRP 定制改动：

- `ThirdParty/LWGUI/Editor/Helper/LwguiAssetPathUtility.cs`
- `ThirdParty/LWGUI/Editor/Helper/Helper.cs`
- `ThirdParty/LWGUI/Editor/Helper/RevertableHelper.cs`
- `ThirdParty/LWGUI/Editor/Helper/ToolbarHelper.cs`
- `ThirdParty/LWGUI/Editor/Helper/IOHelper.cs`
- `ThirdParty/LWGUI/Editor/PerformanceMonitor/ShaderCompiler/Dxc/ShaderCompilerDxc.cs`
