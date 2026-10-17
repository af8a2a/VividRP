# Bindless 原生插件设置 (`UnityBindless.dll`)

返回索引：[Documentation~ 索引](README.md) · 相关文档：[VividRP Wizard](VividRPWizard.md)

## 背景：为什么需要独立安装脚本

VividRP 的 Bindless 路径依赖 `UnityBindless.dll` 在 D3D12 描述符堆创建阶段尽早挂载 Hook，以预留由插件管理的 Bindless 描述符区间。

在当前 Unity Editor 版本中，从 `Packages/...` 加载的原生插件时机可能晚于 D3D12 设备的 `CreateDescriptorHeap` 调用（对应 Unity 已知问题 `UUM-134389`）。一旦错过该时机，插件虽然仍能通过 `SetDescriptorHeaps` 观察到当前激活的 Shader-visible CBV/SRV/UAV 堆，但无法获知插件专属的描述符区间，表现为以下典型症状：

- `GetSRVDescriptorHeapCount()` 返回非零堆大小；
- `GetBindlessDescriptorCount()` 始终为 `0`；
- 无法可靠地创建 Bindless 描述符。

## 设计机制：`BindlessNative~` 与 `Assets/Plugins`

| 位置 | 角色与行为 |
| --- | --- |
| `Packages/VividRP/BindlessNative~` | **包内源码载荷（Source of Truth）**：利用 `~` 后缀使 Unity 在包导入阶段忽略该目录，避免从 `Packages/` 晚加载 |
| `Assets/Plugins/VividRP/x86_64/` | **项目级早加载副本**：参与主工程插件发现路径并标记为 Preloaded，确保在 D3D12 设备与描述符堆初始化完成前加载 |

## 安装步骤

在 Unity **项目根目录**下执行以下 PowerShell 脚本，完成后**重启 Unity Editor**：

```powershell
powershell -ExecutionPolicy Bypass -File .\Packages\VividRP\Setup-Bindless.ps1
```

该脚本会自动完成以下操作：

1. 创建目录 `Assets/Plugins/VividRP/x86_64`（若不存在）；
2. 复制 `UnityBindless.dll`（以及存在时的 `UnityBindless.pdb`）；
3. 首次安装时生成开启了 Preload 的 `UnityBindless.dll.meta`；
4. 清理旧位置的残留副本。

## 生成文件清单

脚本安装后的项目本地运行时文件如下（这些文件是有意生成的，而 `BindlessNative~` 仅作为源载荷保留在包内）：

- `Assets/Plugins/VividRP/x86_64/UnityBindless.dll`
- `Assets/Plugins/VividRP/x86_64/UnityBindless.pdb`
- `Assets/Plugins/VividRP/x86_64/UnityBindless.dll.meta`
