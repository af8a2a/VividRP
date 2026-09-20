# 按帧 GC 分配查询

`agentic_gc` / `GcProfilerBridge.Execute` 只读取 CPU Profiler 已有数据。
不启动/停止录制，不开启 Deep Profile，不切换场景、Play Mode 或 Profiler 窗口，
不使用 Frame Debugger，也不选择或观察 Raster / Native RenderPass。

## 四个操作

```powershell
$project = 'E:/VividRP_Reborn'

# 数据范围、录制目标和当前录制状态
unity command agentic_gc --project-path $project --action status --format json

# 最近 30 个可用帧的总量；--frame 指定起始 Profiler 帧索引
unity command agentic_gc --project-path $project --action frames --count 30 --format json

# 指定帧：各线程统计及第一页分配记录
unity command agentic_gc --project-path $project --action frame --frame 256 --limit 20 --format json

# 使用上一响应中实际的 frameToken、threadIndex、sampleIndex
unity command agentic_gc --project-path $project --action allocation --frame 256 --thread 0 --sample 123 --frame_token TOKEN --format json
```

Editor 主线程也可直接调用：

```csharp
var status = VividRP.AgenticDebugger.GcProfilerBridge.Execute("status");
var frame = VividRP.AgenticDebugger.GcProfilerBridge.Execute("frame", frameIndex: 256);
```

CLI 结果位于 `data.result`，同时检查外层 `success` 和结果自身的 `success`。
非法参数、过期分配定位等返回 `success=false` / `code` / `message`；
查询到缺失或不完整的帧仍返回结构化帧结果，不能只检查 success 判定零 GC。

## 零分配与数据缺失

| 字段 | 含义 |
| --- | --- |
| `dataState=complete` | 所有枚举线程都有有效采样与 GC 标记信息，已扫描全部采样，分配大小均可读，帧指纹未变化 |
| `dataState=partial` | 有可读数据，但某线程、元数据或扫描范围不完整 |
| `dataState=missing` | 帧不存在/未就绪、无可读 GC 线程数据，或帧在读取期间被替换 |
| `allocationState=zero` | 完整扫描且未观察到分配事件 |
| `allocationState=nonzero` | 至少观察到一条分配事件；其大小仍可能未知 |
| `allocationState=unknown` | 未观察到分配且证据不完整，不能断言为零 |
| `totalBytes` | 仅在 complete 时为数字，否则为 **null** |
| `knownBytes` | 已成功读取大小的分配之和，是不完整数据的已知部分，不代表总量 |
| `allocationCount` | 所有线程采样覆盖完整时的事件数；线程/扫描缺失时为 null |
| `observedAllocations` / `missingSizeCount` | 已发现的事件数 / 其中大小未知的事件数 |
| `issues` | 缺失、读取失败、预算耗尽等具体原因；frame 另有逐线程 issues |

大小未知的 GC.Alloc 仍计入已观察事件，并保留在分配记录中，`bytes=null`、`sizeState=missing`。
负的字节元数据也按未知处理。不把 marker 缺失、空线程采样或过期帧当作 0。
有效的零字节分配事件与“没有分配事件”也不同，前者仍计入 allocationCount。

“完整/零分配”仅针对这份 CPU Profiler 记录及其可见线程，不证明未被采集的执行路径、
未注册线程或其他采集目标也没有分配。GC.Alloc 是托管分配，不是对象存活量或回收暂停耗时。
当前录制设置不能反推历史帧的采集设置；status 的设置字段描述的是当前状态。

## 定位、分页与成本

- `--frame` 是零基 CPU Profiler 索引。响应同时给出一基 `displayFrame`；二者都不直接等于
  `Time.frameCount`、相机帧或 GPU 帧号。省略 frame 时 frame/allocation 取最近可用帧，frames 取最近 count 帧。
- `frame.allocations` 按线程索引、采样索引排列，`--offset` 是 GC 事件偏移，`--limit` 为页大小。
  `nextAllocationOffset=-1` 表示已扫描记录没有下一页；若 dataState 不完整，它不代表整帧已穷尽。
- `--max_samples` 是本次请求所有帧共享的采样扫描预算，默认 1,000,000，上限 10,000,000。
  耗尽时返回不完整结果，不会把未扫描部分填成零。它是采样数预算，不是毫秒时限。
  frames 最多 120 帧，分配页最多 2000 条。分页为控制输出大小，仍需重新扫描以给出完整总量。
- 分配定位使用 `(frameToken, profilerFrameIndex, threadIndex, sampleIndex)`。
  threadId 以字符串返回，避免 JSON 消费端丢失 64 位精度；线程槽位不是跨帧身份。
- frameToken 是当前读取器域、连接、帧时间/采样数量等形成的失效检测指纹，
  用于拒绝常见的缓冲滚动、加载替换和域重载后的旧请求；不是 Unity 官方捕获 UUID，
  不能证明完全相同元数据的两份记录一定相同。不跨录制数据源复用 token。
- `frames` / `frame` 不解析调用栈。`allocation` 才按需解析，返回 `callstack.state`：
  `unavailable`（没有记录栈地址）、`resolved`（各地址都有方法名）、`partial_symbols`（部分无法解析）。
  即使方法名可解析，文件名/行号仍可能为空/0；不会补造位置。当前版本不推断父标记作为方法调用栈。
- 建议查询已停止录制的数据，以免环形缓冲变化和查询器自身的分配混入后续录制帧。
  工具不会替用户停止录制，所有原生数据视图都会及时 Dispose，无常驻 Update/渲染回调。

## 验证（2026-09-20，Unity 6000.7.0b1）

- Unity 导入及核心/CLI 编译通过，`agentic_gc` 已注册。
- 独立 .NET 数据完整性检查 31 项通过：有效零、无帧、空线程、缺失标记、未知/负大小、
  部分线程缺失、读取异常、预算限制、分页、过期帧、线程同名不同 ID、301 线程、缺失栈等。
- 已有停止录制的数据上，17 项只读集成断言通过；254/255 帧为完整零，
  256 帧扫描 135 线程、68,575 事件、52,336,305 字节，单条分配栈状态为 partial_symbols。
  这是历史数据上的工具验证，不是本次 VividRP 零 GC 或性能基线结论。
- 帧总量与线程总量/分页记录相符，越界帧和扫描预算不足均返回 null 总量。
  查询前后录制、profileEditor、连接及可用帧范围未改变。
- 未运行 Unity Test Framework。集成检查不生成场景、不自动录制或读取 Frame Debugger。

```powershell
# 不连接 Editor 的纯数据逻辑检查；需要 .NET 10 SDK
dotnet run --project Editor/AgenticDebugger/Tests~/GcChecks/GcChecks.csproj

# 只读验证已有且已停止录制的数据；缺少数据时失败退出，不自动采集
python Editor/AgenticDebugger/Tests~/gc_smoke.py --project E:/VividRP_Reborn
```
