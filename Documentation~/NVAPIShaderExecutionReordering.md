# NVIDIA Shader Execution Reordering (SER)

返回索引：[Documentation~ 索引](README.md) · 相关文档：[RTX Texture Filtering](RTXTextureFiltering.md)

参考路径追踪（Reference Path Tracing）Pass 在 Windows x86_64 + Direct3D 12 环境下提供可选的 **NVIDIA Shader Execution Reordering (SER)** 变体，用于在硬件层面重排发散的光线着色线程。

## 启用方式

1. 在支持 SER 的 NVIDIA 显卡及驱动上使用 Windows + Direct3D 12 运行。
2. 在当前激活的 Volume Profile 中打开 `VividRP/Path Tracing/Reference Path Tracing`。
3. 勾选 **Shader Execution Reordering**。

> [!NOTE]
> Pass 运行时会同时查询驱动 Opcode 与 GPU 线程重排能力。若原生插件、图形 API、驱动、GPU 或 Shader 变体任一条件不满足，VividRP 只会打印一次警告并自动回退至标准 `TraceRay` 变体。

## 实现细节

- **重排作用范围**：SER 仅对重材质负载的表面求交光线使用 `NvTraceRayHitObject`、`NvReorderThread` 与 `NvInvokeHitObject`；可见性阴影射线（Visibility Rays）因跳过 Closest-Hit 着色、无需材质一致性重排，继续走标准路径。
- **NVAPI 扩展 UAV 绑定**：SER 变体在 `u31` 声明 NVAPI 的 `g_NvidiaExt` 指令 UAV。VividRP 在 Dispatch 前会绑定一个单元素（256 字节）的计数器缓冲区，以满足 Unity 光线追踪资源校验对属性初始化的要求；支持 SER 的 NVIDIA 驱动会直接消费该扩展操作。
- **累积状态保持**：切换 SER 开关**不会**重置参考路径追踪的历史累积，因为它仅改变线程调度顺序，不改变积分估计器或随机采样序列。

## 源码与插件位置

| 模块 | 路径 |
| --- | --- |
| C# 托管绑定与预编译二进制插件 | `Runtime/SubSystem/Plugin/NVAPI/` |
| NVAPI 着色器头文件 | `Shaders/Core/Private/NVAPI/` |
| C++ 插件工程与 NVAPI SDK 快照 | `PluginSource~/NVAPINative~/`（含固定版本与 Release 构建命令说明 [`PluginSource~/NVAPINative~/README.md`](../PluginSource~/NVAPINative~/README.md)） |
