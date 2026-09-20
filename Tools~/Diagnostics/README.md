# VividRP Editor / CLI 诊断工具

将重复使用的状态检查、GPU 阶段计时和 VSM 原始缓冲读回收敛为一个常驻 Editor API。运行时没有新增埋点或算法改动；未启动诊断时没有逐帧订阅。工具不依赖实验日期目录、固定机位、编辑器 PID 或桌面鼠标键盘。

## 入口

- Editor 菜单：`Tools > VividRP > Diagnostics` 下的 `Save State Snapshot`、`Profile Current Camera`、`Capture VSM Buffers`、`Cancel Current Diagnostic`。
- C# / 已有 MCP eval：`VividRP.Editor.VividDiagnostics.Execute(requestJson)`，返回 JSON 字符串。
- Unity CLI：可选的 `com.unity.pipeline >= 0.7.0-exp.1` 注册 `vivid_diagnostics` 命令。独立适配程序集避免让核心 Editor 工具依赖 Pipeline；没有该包时仍可使用菜单/API，不会自动安装包。
- Python CLI：本目录 `vivid_diagnostics.py`，仅依赖 Python 标准库及已安装的 Unity CLI。所有进程调用传入参数数组，不经过 shell 拼接。

从 VividRP 包根目录执行；其他目录用脚本绝对路径并传 `--project-path`。下面的 ID 和结果目录用实际返回值替换。

```powershell
python Tools~/Diagnostics/vivid_diagnostics.py doctor
python Tools~/Diagnostics/vivid_diagnostics.py cameras
python Tools~/Diagnostics/vivid_diagnostics.py snapshot --camera-id CAMERA_COMPONENT_ID
python Tools~/Diagnostics/vivid_diagnostics.py profile --camera-id CAMERA_COMPONENT_ID --warmup 64 --frames 128 --wait
python Tools~/Diagnostics/vivid_diagnostics.py status --job-id JOB_ID
python Tools~/Diagnostics/vivid_diagnostics.py cancel --job-id JOB_ID
python Tools~/Diagnostics/vivid_diagnostics.py summarize OUTPUT_DIRECTORY
python Tools~/Diagnostics/vivid_diagnostics.py capture --camera-id CAMERA_COMPONENT_ID --warmup 16 --wait
python Tools~/Diagnostics/vivid_diagnostics.py verify-capture OUTPUT_DIRECTORY
```

`cameras` 返回 **Camera 组件** ID，不是 GameObject ID；Unity 6.5+ 的 EntityId 以十进制字符串传递，避免 32 位截断。未指定相机时选择 `Camera.main`；没有启用的 MainCamera 就明确失败，不会移动、启用或创建相机。

跨项目指定：`python <脚本路径> --project-path <Unity项目目录> profile ...`。
`--output-directory` 是输出父目录，每次创建唯一子目录；相对路径相对于 Unity 项目根目录。默认 `Temp/VividRPDiagnostics/`。工具不覆盖既有捕获。

## 请求、任务与失败语义

```json
{
  "action": "profile",
  "cameraId": "1099511669000",
  "warmupFrames": 64,
  "frames": 128,
  "timeoutSeconds": 120,
  "outputDirectory": "Temp/VividRPDiagnostics",
  "markers": ["VSM.Resolve", "VSM.ResolveTrace"]
}
```

`action` 支持 `cameras / snapshot / profile / capture / status / cancel`。`profile` 不传 markers 时使用18个 VSM GPU marker，也可传任意 Render 分类 GPU marker，因此其他渲染流程可直接复用。

profile/capture 返回 `jobId`，状态为 `warming → sampling → readback（仅捕获）→ complete`，或 `failed/cancelled`。查询不启动新任务；取消必须携带匹配的 jobId。编辑器同时只运行一个此类任务，并与旧 Baseline Recorder、Quality Reproduction 双向互斥。快照导出也不会插入正在运行的计时窗口。

`--wait` 每秒查询一次；Ctrl+C 只结束等待，不假设远端已取消，输出 jobId 供显式取消。任务自身有总超时。程序集重载、退出或 Play Mode 切换会注销回调并保留终态；重载后可读最近一个任务的状态。取消后的回调不再写数据。深度转存纹理必须活到读回完成：正常完成不等待，提前退出且尚有读回时调用 `WaitAllRequests` 再释放；该冷路径也可能等待编辑器中其他原生读回，不能放入计时窗口。

相机、光源、Volume、Play Mode、场景资产均不修改；采集前后保存状态快照。默认只请求 Editor 渲染 tick，不开窗口、不切换视图、不调用 Camera.Render。若现有 Game view 不产生所选相机帧，任务按超时失败。需要复现旧实验的主动重绘方式时显式加 `--repaint`（API 的 `repaintViews=true`），只请求现有视图重绘，不改变窗口焦点；该条件保存在 request.json，比较窗口必须一致。

CLI 输出统一 JSON，错误返回非零退出码。业务失败同时检查外层 Unity CLI 与内层诊断结果；不能仅凭有 `data` 或文件存在判断完成。`result.json` 在开始和结束写入，采集中进度以 status 为准。独立文件仍可能来自被取消的部分捕获，分析器只接受 complete 目录。

## 产物与测量边界

| 工作流 | 输出 | 可用于 |
|---|---|---|
| snapshot | snapshot.json | 场景 dirty 状态、相机矩阵、灯光、有效阴影设置、VSM 布局及环境快照 |
| profile | request.json、before/after.json、result.json、stages.csv | 无 GPU 回读的阶段成本窗口；采样存储预分配，结束后写 CSV |
| capture | 上述元数据、frame.json、capture.json、各通道 bin.gz | 同一 resolve 后的 depth/normal/shadow、页表/页元数据/所有权/分配计数/投影数据 |

capture 默认只取一帧；`frames` 仅适用于 profile。加 `--include-depth-pools` 才读取完整静态/动态16层池，256页时原始池数据约512 MiB。请先使用小型默认捕获。

depth/normal/shadow 转换为 RGBAFloat 保存，格式不是原纹理的逐位复制；深度池保存源 uint 格式。原生深度先由诊断 compute 核转存，深度值在 x 通道，其余通道为0。normal 是 GBuffer1 的转换值，仍需按当前 GBuffer 布局解码，不是已经展开的世界法线。shadow 是 resolve 后的可见度，可能包含当前滤波与接触阴影。`capture.json` 记录源格式、存储格式、尺寸、层数、buffer count/stride、解压字节数和 SHA256；数组纹理逐层连续写入。`verify-capture` 检查解压长度、哈希和受支持的描述符长度，不把文件完整性当作阴影正确性。

GPU recorder 是**最近完成的 profiling frame**，不是 observation 的 GPU frame ID；marker 可包含其他相机的执行。`available=0` 与 `gpu_sample_count=0` 分别表示无样本和无执行，均不当作零毫秒。`summarize` 保留这两类计数，输出条件 P50/P95/P99，不把父子阶段或它们的分位数相加。需要完整 VSM 墙钟或硬件带宽/原子指标时，继续使用 Nsight Graphics 原生 CLI 捕获；本工具不伪造这些指标。

每次所选相机的 endCameraRendering 回调算一个 observation。Edit Mode 的 `Time.frameCount` 可能不增长，CSV 保留为 `unity_frame_observed` 时间标记，不能据此丢弃相机回调或认定 GPU 样本重复。

profile 使用当前场景设置，不施加固定射线/页预算 preset。做 A/B 时由上层流程记录、设置并恢复参数，复用同一相机、预热、窗口长度及正反顺序；读回与计时分开运行。

## 实验专用配方

[16层容量、成本与动态基线](../../Temp~/VSM/Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/README.md) 中的 RTAS 几何参考、36个插入夹具、动态 meshlet 序列和256/1024射线配对属于实验配方，保留原始来源及分析脚本，本轮不复制成另一套常驻实现。

新的普通场景成本检查用 `profile`；页表、阴影和池检查用 `capture`。需要独立几何正确性时，再按原报告导入相应配方，先检查法线有效、参考命中、同相位配对与状态恢复。不要把新的原始缓冲文件直接传给要求旧实验特定 schema 的分析器。

其他 Editor 检查优先使用 Unity CLI 已有的 `editor_status`、`console`、`find_gameobjects`、`screenshot` 等命令；先通过 `unity command --query ... --format json` 获取当前参数。没有另建常驻 HTTP 服务、任意代码执行端点或自动安装/重启回退。

## 验证

```powershell
python -m unittest discover -s Tools~/Diagnostics -v
```

Editor 定向测试为 `VividRP.Editor.Tests.VividDiagnosticsTests`，覆盖错误请求、任务不启动、Edit Mode 重复 Unity 帧编号及预热后 Observe 零托管分配。仅在 Editor 关闭时通过 `unity test ... --filter VividRP.Editor.Tests.VividDiagnosticsTests` 运行；编辑器打开时手动运行，不从 CLI/UI 自动触发。更换后端后还应进行实际 profile/capture 验证，缺失 marker 的零分配测试不代替 GPU 有效性验证。

2026-09-12 在 Unity 6000.7.0a6 / DX12 实际验证了命令注册、快照、32次GPU计时观测、默认8通道与含完整16层池的10通道捕获、任务互斥和取消；详见 [验证记录](validation.json)。Editor / CLI适配 / Tests 三程序集独立编译及诊断 shader DXC 通过。Unity CLI 检测到已打开编辑器后拒绝启动测试，因此上述 Editor NUnit 测试只编译，未执行，也未宣称新采样器已实测零分配。
