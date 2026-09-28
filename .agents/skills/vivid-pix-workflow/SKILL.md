---
name: vivid-pix-workflow
description: 在 VividRP 中执行 agentic PIX 工作流：检查 D3D12/PIX 注入、捕获指定相机与 pass、调用外部 vivid-pix-analyzer，并根据结构化证据诊断 GPU 事件、资源绑定、时序与计数器。用于 PIX 空 capture 排查、VividRP 捕获到分析的端到端验证，以及已有 .wpix 的定向分析；不用于通用 Unity 编辑或 Nsight 工作流。
---

# VividRP PIX workflow

复用仓库维护的脚本与契约，交付可追溯的诊断和原始证据。不要复制一套捕获实现，也不要把历史验收中的路径、PID、pass 序号或事件编号当作当前环境配置。

## 定位与预检

1. 本文件所在目录的 `../../..` 是 VividRP package 根目录；以下命令均从根目录执行。读取根目录 `AGENTS.md`、`package.json`，定位包含该 package 的 Unity 项目并读取其 `ProjectSettings/ProjectVersion.txt`。嵌入包通常位于项目的 `Packages` 下，但外部包不能仅凭目录层级推断项目。
2. 阅读 [工作流命令和边界](../../../Tools~/PIX/README.md)。确认 Python 3.10+、Unity CLI、匹配项目版本的 Editor、PIX 安装，以及 `Tools~/PIX/bin/vivid-pix-analyzer.exe`。需要操控场景时遵循可用的 `unity-cli` skill，通过已发现的命令操作，不手改序列化资产。
3. 将已确认的项目路径、PIX 安装路径分别保存为 PowerShell `$project`、`$pix`；需要启动时还需 `$editor`。先检查已有 Editor：

```powershell
unity command agentic_gpu_debugger --project-path $project --backend pix --action preflight --format json
```

检查响应中的 D3D12、VividRP、native 可用性、已注入 PIX 安装、会话占用、当前 graph、相机及 pass markers。选择正在正常渲染的目标相机，以及预期包含非零 draw/dispatch 的**精确 marker**，分别保存为 `$camera` 和 `$marker`。歧义或目标缺失必须解决，不能自动换一个 pass 来通过验收。

## 按当前状态选择入口

| 状态 | 行动 |
| --- | --- |
| 当前 Editor 已正确注入，工具匹配 | 复用会话，直接运行 `diagnose`。 |
| 原生工具缺失或需更新 | 按 [原生构建指南](../../../PluginSource~/PixCaptureNative~/README.md) 构建部署可选工具；读取实际构建参数。已加载且内容改变的 DLL 需要正常关闭 Editor 后部署。 |
| 项目尚未打开 | 使用下方 `launch`，在 D3D12 device 创建前注入 capturer。 |
| Editor 已打开但注入缺失或版本不匹配 | 晚加载 DLL 无法修复已有 device。说明需要正常关闭并重新启动；沿用用户已有授权，未明确授权时先解决活动会话与未保存内容，不强杀或自动重启。 |
| 已有目标 `.wpix`，只需进一步分析 | 阅读 [M2 分析接口](../../../Editor/AgenticDebugger/PIX.md)，直接使用外部 analyzer。`diagnose` 会生成新 capture，不是读取旧 capture 的命令。 |

启动命令仅用于项目已关闭的情况。工作目录位于 package 根目录，输出目录必须尚不存在：

```powershell
$launchOutput = Join-Path (Get-Location) ('Temp~/PIX-runs/launch-' + [guid]::NewGuid().ToString('N'))
python -X utf8 ./Tools~/PIX/vivid_pix_agent.py launch --project $project --pix $pix --editor $editor --output $launchOutput
```

该路径使用官方 `pixtool launch` 和 programmatic-capture supervisor。遇到 `UnityLockfile` 不删除锁文件绕过检查。启动超时时先读保留的 PID、日志和当前进程状态，Editor 可能仍在启动，不重复拉起实例。

## 捕获与定向分析

先明确用户问题，再选择可选查询。基础命令已经包含捕获、内容验证、事件、pipeline 和资源绑定分析：

```powershell
$diagnoseOutput = Join-Path (Get-Location) ('Temp~/PIX-runs/diagnose-' + [guid]::NewGuid().ToString('N'))
$question = '检查目标 pass 的实际 GPU 工作及资源绑定'
python -X utf8 ./Tools~/PIX/vivid_pix_agent.py diagnose --project $project --pix $pix --output $diagnoseOutput --camera $camera --marker $marker --question $question
```

按问题在命令末尾添加选项：

- `--timing`：所选事件的 replay 时序。
- `--accessed`：实际访问资源查询；静态绑定不能代替此证据。
- `--counter 'Exact Name'`：最多四个精确计数器名称，由 agent 根据问题选择。`--question` 只记录问题，不会自动选指标。
- `--occupancy`：仅在需要时请求，并接受 SDK 明确返回不可用。

默认选取精确 marker 下第一个 `gpuWork=true` 的真实非零 GPU 事件，不代表最慢事件。`queueId` 与 `eventIndex` 共同标识同一份 capture 内的事件；不要将历史 capture 的编号传入新一轮 `diagnose`。指定某个既有事件时，用该 capture 自己的 JSON 查询结果和 M2 analyzer 接口继续分析。

已有 capture 的每次查询都保留原始 capture 路径、session、expected pass、boundary mode 和 SHA-256；生成新的 request ID 和输出文件，校验返回身份。查询应分页并限制数量，不向上下文倾倒整个事件或计数器目录。标准脚本默认每页 128、最多 512 个 marker 事件、单次 120 秒、总体 600 秒；更多边界和恢复命令以维护文档及脚本 `--help` 为准。

## 证据门槛与失败处理

- Capture 必须到达经过内容验证的 `ready`：目标 pass 内有真实 GPU 工作，session 边界成对且顺序正确。`boundaryMode=all` 要求四个逻辑 queue scope 通过验证。文件存在、文件大小、Begin/End HRESULT 或 fence 完成均不能单独证明成功。
- 同时检查捕获区间 Console 游标：新增渲染错误、游标重置或证据丢失均不能当作干净验收。保留原始错误和查询结果，追踪首个真实失败。
- 使用 analyzer schema 3 身份校验和 capture hash 固定证据；退出码与 JSON 必须一致。最终 `diagnosis.json` 为 schema 1。`success=true` / `state=complete` 只说明必需流程完成，仍需读取 `limitations`、截断标志及可选查询状态。
- 仅 capture 阶段的瞬态 `editor_busy` 允许脚本内有限重试；preflight 失败立即返回。缺失 pass、空捕获、渲染错误及超时保留证据并诊断，不通过重复捕获、关闭校验、改动场景或切换目标掩盖失败。
- 失败或取消后仅释放本次持有的 session。清理未完成时保留 token 并按状态恢复，不释放他人的会话。每次使用新输出目录，不覆盖旧 capture 或 JSON。

## 解读与交付

优先读取 `diagnosis.json`，再按问题读取其链接的有界原始 JSON，不把 `.wpix` 二进制输入上下文。报告问题、capture 路径/hash、marker、队列与事件身份、相关资源/着色器/指标证据、限制和下一步。

- `queueType` 的 SDK 值为 0 graphics、1 compute、2 copy；graph 的 `asyncCompute` 选项不证明实际运行在 compute queue。
- 区分静态绑定与实际访问资源。保留 64 位数字字符串的精度和时序单位；未知编码的 counter 只能报告 raw/format，不能臆测单位、解码值或把 unavailable/null 写成 0。
- 单次事件 replay 时序不能证明整帧耗时或瓶颈；捕获用的 queue joins 会改变调度。性能结论需要可比较的重复测量和额外证据。
- 本仓库既有 PIX 2606.18-preview 验收曾遇到 occupancy `E_NOTIMPL`、counter format 7 未解码、Unity DXBC shader hash 缺失。以当前 SDK 实际返回为准，不将这些历史限制泛化为永久不支持。
- [M3 实机验收记录](../../../Documentation~/AgenticPixM3Acceptance.md) 仅供复现参考，不能替代当前运行；尤其不能把 graphics queue 上成功的 dispatch 称为真实 async-only 验收。

交付时链接诊断与原始证据，分别说明通过、失败、未验证项，以及清理结果和 Editor 是否仍运行。无实际 GPU 执行就明确写明静态或离线验证。

若修改工作流脚本，运行 `python -X utf8 ./Tools~/PIX/test_workflow.py`；原生/C# 改动按仓库指南做针对性检查。Editor 正在运行时不得主动启动 Unity Test Framework 测试。仅整理 skill 或文档时，验证格式、引用和 diff 即可，不重新启动 Editor 或捕获。
