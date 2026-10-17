# VividRP Wizard 配置向导

返回索引：[Documentation~ 索引](README.md) · 相关文档：[Bindless 原生插件设置](Bindless.md)

打开 **Window > Rendering > VividRP Wizard** 即可检查并一键配置 VividRP 在 Windows 平台下的基础图形环境要求。

## 向导功能

1. **配置 Direct3D 12 图形 API**：为 Windows 64-bit Standalone 关闭自动图形 API 选择（Auto Graphics API），并将 **Direct3D12** 置于首位。
2. **配置 DXC Shader 编译器**：为当前激活的 Build Profile 启用 Direct3D12 下的 **DXC** 编译器后端。

> [!IMPORTANT]
> - DXC 编译器配置按 **Build Profile** 独立存储；修复该项前请先创建或激活一个 Windows Build Profile。
> - 若配置完 Direct3D12 后当前 Editor 仍运行在其他图形 API（如 D3D11 或 Vulkan）下，请**重启 Unity Editor** 使图形设备切换生效。
