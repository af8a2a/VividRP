# AgenticDebugger — Frame Debugger

PIX 的显式捕获、M1 管线边界/异步队列同步、M2 外部分析器及异步查询，以及统一入口 `agentic_gpu_debugger --backend pix|frame_debugger` 见 [PIX.md](PIX.md)。

CPU Profiler 的 `status / frames / frame / allocation` GC 分配查询见 [GCProfiler.md](GCProfiler.md)。
该接口独立于下面的 Frame Debugger，不读取其事件或 RasterPass 详情。

通过 Editor API / Unity CLI 读取 Unity Frame Debugger，不需要操作桌面窗口。
核心程序集仅在 Editor 加载；`Pipeline/` 是可选的 `com.unity.pipeline` CLI 适配。
当前项目版本：Unity **7000.0.0a16**（见项目 `ProjectSettings/ProjectVersion.txt`）。私有 API 被隔离在 `FrameDebuggerApi.cs`；
缺少成员时返回 `unsupported_unity_api`，升级 Editor 后应重新运行 smoke 检查。

**Frame Debugger 观察 Raster / Native RenderPass 时的崩溃问题已在当前版本修复（2026-11-15，用户确认）。**
此前为规避崩溃添加的事件白名单和详情字段过滤已移除，支持 Raster / Native RenderPass、Compute 和 Ray Tracing 事件查看，
并恢复当前选中事件的渲染目标导出。会话归属、事件索引、过期 hash 和详情匹配检查仍然保留。

## 调用

```powershell
$project = 'E:/VividRP_Reborn'

# 状态，包括版本、支持情况、事件数、当前选中事件和活动会话
unity command agentic_frame_debugger --project-path $project --action status --format json

# 开始本地捕获；从 data.result.sessionId 获取会话 ID
unity command agentic_frame_debugger --project-path $project --action capture --timeout_seconds 120 --format json

# 上一行的替代方式，不要同时启动两个捕获：
# Game view 隐藏/不重绘时，显式选择相机重渲染捕获（会推进渲染历史）
unity command agentic_frame_debugger --project-path $project --action capture --source camera --timeout_seconds 120 --format json

# 轮询 status，等待 state=ready；读取事件列表（事件编号从 0 开始）
unity command agentic_frame_debugger --project-path $project --action events --count 100 --format json

# 使用实际返回的 sessionId / eventsHash / eventIndex 替换下面的值
unity command agentic_frame_debugger --project-path $project --action select --session_id SESSION --event_index 42 --events_hash HASH --format json

# select 后需等 Unity 重绘；遇到 code=pending 时稍后重试
unity command agentic_frame_debugger --project-path $project --action event --event_index 42 --include_shader_properties true --format json

# 导出同一选中事件的渲染目标；文件必须不存在，HDR 可使用 .exr
unity command agentic_frame_debugger --project-path $project --action export --event_index 42 --events_hash HASH --path Temp/AgenticDebugger/target.png --format json

# 完成后必须释放；无人释放时也会在租约到期后自动恢复
unity command agentic_frame_debugger --project-path $project --action release --session_id SESSION --format json
```

Editor C#（主线程）使用同一个 API：

```csharp
var result = VividRP.AgenticDebugger.FrameDebuggerBridge.Execute("status");
```

CLI 响应需要同时检查外层 `success` 和 `data.result.success`。
`pending` 是可重试结果，不是一次成功的数据读取。命令只提交捕获/选择请求，
不会在主线程阻塞等待渲染完成。

## 返回的数据

- `events`：返回所有原生事件类型，包含零基 `eventIndex`、Profiler 标记名称、事件类型和关联对象。
  编号与原生索引一致；`total` / `eventCount` 是原生事件总数。
  `offset` 是原生索引，`count` 是最多返回的事件数，用 `nextOffset` 翻页（-1 表示结束）。单页最多 2000 条。
- `event`：当前选中事件的完整公开 DTO 字段，包括 Shader、绘制参数、RenderTarget、混合/深度/模板/光栅状态，
  以及 Compute Kernel、线程组和光追分派参数。
  `details` 保留当前 Unity DTO 的字段名，并非跨版本固定字段表；不适用于该事件的字段可能为零/空。
  `include_shader_properties=true` 额外输出关键字、常量、纹理/缓冲绑定名称等。
  Unity Object 只输出身份与纹理描述，不遍历其属性或复制缓冲内容。
- `events` / `event` 可通过 `path=...json` 将本次响应写入文件。
- 所有输出路径相对于 Unity 项目根目录，也可传绝对路径；已有文件不会被覆盖。
- `export`：将当前选中事件的 `m_RenderTargetRenderTexture` 导出至必填的 `path`，支持 `.png`（RGBA8，超出范围的值被截断）
  和 `.exr`（RGBA 浮点，保留 HDR 范围）。返回 `eventIndex`、`path`、`width`、`height`、`format`。
  与 `event` 一样，需先选择事件并等待重绘；`event_index=-1` 使用当前选择，`pending` 时可稍后重试。
  导出读取 Unity 提供的当前 2D 渲染目标预览，不切换 MRT、深度、mip 或数组切片，也不保证与窗口的通道、色阶和翻转显示一致。
  无目标时返回 `no_render_target`，非 2D 目标返回 `unsupported_render_target`。导出会同步读取 GPU 数据，不应计入性能基线；
  完成或失败时均恢复活动渲染目标和 sRGB 写入状态，并释放工具创建的临时纹理。

## 状态与边界

- `capture` 只管理本地 Editor，不创建/激活窗口，不切换 Play Mode。
  默认 `source=game_view` 请求现有视图重绘，需要已经打开且实际参与渲染的 Game view；
  隐藏或被节流时可能没有事件，不会自动切换捕获方式。
  显式 `source=camera` 在 Unity 捕获作用域内调用 `Camera.Render()`，默认使用 MainCamera，
  可用 `camera` 指定唯一的启用相机名称。它会重新渲染、推进 VSM/TAA 等历史，
  不代表用户刚才看到的帧；捕获及事件选择各最多渲染 4 次以等待数据稳定。
- Play Mode 正在运行时捕获会暂停；释放时只恢复本工具改变的暂停状态。
  关闭、程序集重载、Play Mode 转换、租约到期都清理本工具的会话。
  租约是从 capture 开始的绝对时限（1..300 秒），读取/选择不会自动延期。
- 已启用的 Frame Debugger 可直接 `events` / `event` / `export`，不会被本工具接管。
  `select` / `release` 只接受本工具发出的会话 ID；用户已有捕获由用户在原窗口选择事件。
- `eventsHash` 用于检测事件列表变化，不代表 GPU 帧号，也不能证明两个图像来自同一帧。
  `event` 校验 Unity 返回成功及其 `m_FrameEventIndex`，避免返回上次选中的旧事件。
- 空闲时没有 Update 或渲染回调；只在活动捕获期间订阅 Editor Update。
  Frame Debugger 会改变渲染执行方式，捕获不应计入性能基线。

## 验证

既有验证记录（最初验证目标为 Unity **6000.7.0a6**）：核心/CLI 两个程序集已通过 Roslyn 离线编译。
当时的 `Tests~/PolicyChecks` 通过了 10 项白名单和字段过滤检查；该历史结果不代表当前恢复后的功能验收。

2026-11-15：移除限制后，核心/CLI 两个程序集使用当前 Unity **7000.0.0a16** 的实际编译响应文件和引用通过 Roslyn 离线编译。
更新后的 `Tests~/PolicyChecks` 在独立 .NET 进程中检查实际编译程序集的事件类型序列化、Raster/Compute/Ray Tracing 详情、
Shader 属性开关、PNG/EXR 输出路径及禁止覆盖行为，16 项检查通过，不构造原生适配器、不调用任何 Unity 原生函数。
需要 .NET 10 SDK，参数为编译后的核心 DLL、UnityEngine DLL 目录、Newtonsoft.Json DLL：

```powershell
dotnet run --project Editor/AgenticDebugger/Tests~/PolicyChecks -- <AgenticDebugger.dll> <UnityEngine目录> <Newtonsoft.Json.dll>
```

早期实时验证未完成：默认 Game view 捕获未收到事件；显式相机探测曾返回 126 条事件元数据，
但随后编辑器发生崩溃（用户确认，具体触发点未确定）。探测没有调用事件详情 API。
收到 RasterPass 崩溃限制后已停止原生捕获，添加白名单并只做离线验证。
截至 2026-11-15，用户已确认当前 Unity **7000.0.0a16** 中上述崩溃问题已修复。
**恢复后的 Raster / Native RenderPass、Compute / Ray Tracing 实时详情、PNG/EXR 导出和最终会话生命周期仍待在当前版本验证。**
此次未重新运行实时捕获；离线编译和检查不等同于整个自动化捕获流程已验收通过。

`Tests~/smoke.py` 是 CLI 集成检查，不调用 Unity Test Framework：
它会调用原生捕获/选择/详情和 GPU 读回 API，可用于当前版本的集成复验；本次未运行此脚本。

```powershell
python Editor/AgenticDebugger/Tests~/smoke.py --project E:/VividRP_Reborn
# 无需 Game view 重绘；显式允许相机重新渲染：
python Editor/AgenticDebugger/Tests~/smoke.py --project E:/VividRP_Reborn --source camera
```

它临时启用捕获，检查完整事件分页、Raster 选择及详情、PNG/EXR 文件签名、禁止覆盖、错误会话、过期 hash 和释放，
并用 `finally` 恢复工具创建的会话。图片输出到系统临时目录并自动清理。需要连接中的 Editor 和包含可导出 Raster 目标的帧；
Game view 模式需要正在重绘的视图，camera 模式需要可渲染相机。遇到用户已有捕获会拒绝启动。
