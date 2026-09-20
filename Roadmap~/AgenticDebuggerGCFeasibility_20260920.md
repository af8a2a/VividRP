# AgenticDebugger 按帧 GC 分配检查可行性

日期：2026-09-20。结论：可行，优先实现已录制 CPU Profiler 帧的只读查询。
本次是源码调研与 API 编译验证，没有启用 Profiler 录制、调用 Frame Debugger，或修改场景。

## 核对版本与证据

- 项目：Unity 6000.7.0b1，revision `6f112f2bea37`。
- 已安装包：`com.ericjrico.gcalloc-analyzer` 0.2.3，Git 锁定
  `f2ba99189774334fa478d84139e79c9b533c3679`，MIT，声明 Unity 6000.0+。
- 以下源码位置均相对于该包：

| 位置 | 已有机制及接入意义 |
| --- | --- |
| `Editor/GCAllocAnalyzerWindow.cs:1855`，`OnPullData` | 从 Profiler 可用帧范围逐帧、逐线程扫描 `GC.Alloc`，计算每帧分配字节数 |
| `Editor/GCAllocAnalyzerWindow.cs:1981`，`RunAnalysis` | 线程身份、原始事件、调用栈解析与缓存、多帧聚合；方法为窗口私有实例方法，伴随 UI 操作 |
| `Editor/GCAllocBreakdownModule.cs:250`，`ExtractFrame` | 单帧实现，但线程扫描硬编码上限 256，遇到 invalid 即结束，不能直接用作完整性保证 |
| `Editor/GCAllocAnalyzerData.cs:50`，`RawAllocation` | 字节数、Profiler 帧/采样索引、线程 ID/索引/组名/名称、父标记、层级路径、符号化调用栈 |
| `Editor/GCAllocAnalyzerData.cs:15`，`AnalysisSnapshot` | 总量、次数、逐帧数据、分组与调用栈存在标志；`HasData` 依赖分配数大于零，不能表示有效的零分配帧 |
| `Editor/GCAllocExporter.cs:79` | 原始分配 CSV 导出调用文件对话框；不能直接作为无窗口 CLI 接口 |
| `Editor/SnapshotSerializer.cs:10` | 内部的版本化二进制快照（当前 v3），不是公开服务接口 |

## 技术路径

```text
Profiler 已录制帧
  → ProfilerFrameDataIterator.GetThreadCount(frameIndex)
  → ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex)
  → GetMarkerId("GC.Alloc") / GetSampleMarkerId
  → GetSampleMetadataCount / GetSampleMetadataAsLong(sampleIndex, 0)
  → 按需 GetSampleCallstack / ResolveMethodInfo
  → 自有 DTO、线程/调用点/帧范围汇总 → JSON
```

这是 CPU Profiler 数据路径，与 Frame Debugger 的事件选择、Native RenderPass 详情及
渲染目标预览无关。没有理由为 GC 分析解除现有 RasterPass 禁令。
它仍然使用 Unity 的原生 Profiler 数据访问接口；编译通过不等于已验证运行稳定性。

| 需求 | 判断 |
| --- | --- |
| 指定已录制帧的分配字节数、事件数 | 可实现；只对有效且具有相应采集数据的帧作结论 |
| 主线程、渲染线程、工作线程分别检查 | 可实现；每帧重新枚举线程，保存 threadId + group/name，不把线程索引当永久身份 |
| 按分配调用点排序、最大单笔、连续帧趋势 | 可实现；包已有事件数据和聚合示例 |
| 方法、源码文件与行号 | 有条件可实现；采集时须存在调用栈，且符号信息可解析；缺失时不能补造 |
| 未开启调用栈时的来源定位 | 降级为 CPU Profiler 标记层级/父标记，明确它不是完整 C# 调用栈 |
| 检查正在执行、尚未完成的“当前帧” | 不适合作为精确接口；读最近已完成并进入 Profiler 缓冲的帧 |
| GC 回收暂停/增量回收耗时 | 另做 CPU 回收标记分析；不能由 GC.Alloc 字节数推出 |
| 对象存活、泄漏、引用链、精确对象类型 | 该包提供的事件数据不足，需另用内存快照等机制 |

Unity 官方说明：调用栈需在采集时启用，地址通过 ResolveMethodInfo 解析：
[GetSampleCallstack](https://docs.unity.cn/6000.7/Documentation/ScriptReference/Profiling.RawFrameDataView.GetSampleCallstack.html)。
分配可能出现在多个线程，GC.Alloc 度量的是托管分配：
[Tracking garbage collection allocations](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/performance-memory/managed-memory/track-garbage-collection)。

## 不直接复用窗口作为后端

包中的 AnalysisSnapshot / RawAllocation / Exporter 均为 internal；分析代码与窗口字段、
进度条、过滤器、图表及状态恢复耦合。通过反射创建窗口再调用 RunAnalysis，会把这些副作用
和版本依赖带进 AgenticDebugger。直接改 Library/PackageCache 也不能形成可靠交付。

建议参考它的提取算法，在 AgenticDebugger 中建立小型、无 UI 的 CPU Profiler 读取器，
直接使用以上可编译 API，输出自有稳定 DTO。这样无需强依赖第三方窗口程序集。
将来若要复用其完整聚合算法，应在受版本控制的包 fork 中提取 public 分析服务，
而不是反射窗口；复制 MIT 源码时保留其版权及许可证声明。

## 完整性和成本要求

1. API 明确区分 `profilerFrameIndex`（零基）和 `displayFrame`（一基）。二者均不直接等于
   `Time.frameCount` 或 GPU 帧号；跨录制会话、重新加载数据后须使缓存失效。
2. 无效帧、不可用线程、没有字节元数据的 GC.Alloc 均单独计数。未知大小的事件不能消失，
   更不能被解释为“零分配”。返回完整性状态与未解析调用栈数量。
3. 不复用模块的 256 线程上限。每帧检查线程数和 raw.valid，不假定线程槽位跨帧不变。
   窗口已有按 threadId 去重及候选槽位缓存，复制时也要验证线程迁移/新出现线程的完整性。
4. 启用录制后缓冲可能滚动覆盖；已录制区间分析优先停止录制后进行。若仍在录制，
   查询边界和读取失败必须体现在结果中，不能沿用旧数据。
5. 符号解析与 JSON 生成会产生分配和耗时。不要在目标渲染热路径内扫描/序列化；
   采集和分析分离，默认只算总量，按需解析少量异常帧的栈。
   原生帧视图使用 using 及时释放，不跨异步等待持有；后台聚合只处理已复制的普通数据。
6. 明确采集目标（Editor/Play Mode/Player）和 EditorLoop/PlayerLoop 等标记范围，避免
   把 Editor UI、CLI 和分析器自己的分配归因给 VividRP。全局及过滤后的统计应同时保留。
7. VividRP 归因优先使用 CPU 标记层级与解析后的完整调用栈。不能只看最顶端 allocator，
   也不能把 GPU Pass 耗时标记当成分配线程/调用者证据。
8. 缓存应按数据会话管理；完整栈键要检查地址序列相等，不能仅凭栈哈希认定相同。

## 建议推进顺序（尚未实现的接口）

第一阶段：只读 `status / frames / frame / allocation`。
status 返回录制目标、可用帧区间和数据能力；frames 返回逐帧总量与缺失状态；
frame 返回指定帧的线程统计和分配记录；allocation 延迟解析某个采样的调用栈。
通过可选 Unity CLI 子程序集暴露为 `agentic_gc`，不改变现有 FrameDebuggerBridge。

第二阶段：`compare / check`。支持预热后帧区间、线程/标记/方法过滤、Top N、
分配次数/大小和预算检查；结果区分通过、超限、证据不足。

第三阶段：显式有界录制控制。保存并恢复 Profiler 原设置，只开启所需 CPU 采集和
GC.Alloc 调用栈，不默认启用 Deep Profile。先验证主线程、工作线程、无调用栈、
元数据缺失、环形缓冲过期、零分配和分析器自污染等情形，再用于质量门禁。

## 本次验证与未验证项

- 已阅读本地锁定版本源码；未操作 package、场景或 Editor 设置。
- 编译原型：`Temp~/gc-feasibility/ApiCompileProbe.cs`，只覆盖 API 可调用性，非成品扫描器。
- 使用 6000.7.0b1 的程序集与 Roslyn 10.0.303 编译，退出码 0：线程枚举、
  GetRawFrameDataView、marker/元数据读取、threadId、GetSampleCallstack、ResolveMethodInfo 均通过。
- 未运行原型，未读取实际 Profiler 帧，未测量扫描速度/内存，也未验证 Editor/Player
  调用栈的实际覆盖率。因此可确认实现路径存在，不能声称当前场景零 GC 或工具已完成验收。
