# Screen Probe 追踪重复性定位

2026-11-15，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti。承接 [播放模式计时与输入重复性](ScreenProbePlayModeTiming.md)。本轮仍未取得合格的 FP16/128 与 FP32/512 GPU A/B；没有性能或新增 GI 降噪结论。精度与共享 receiver 原型已撤回，实际 Editor 重新导入原 shader，保存的默认值保持 16 px、8 rays、FP16/128、guarded anchor 预滤波、四轮 À-trous。

## 已排除的输入差异

用户确认可以独占 Editor 实测后，`ScreenProbeControlledTraceAA-20261115-010602-290128` 再次在第二组第 2 帧出现追踪分歧：没有其他相机、没有待完成读回、源状态已准备 264 帧、没有超时，源码/实际脚本指纹未变。播放、暂停和 Frame Debugger 状态检查没有触发。该运行尚含下述精度原型，因此随后恢复原 shader，再独立验证输入。

`ScreenProbeSdfInputWitness-20261115-011654-903687` 使用原 shader、两个 FP16/128 冷启动、64 帧预热的短定位协议，不是完整计时。源保持 8 阶、64 samples、32 batches、seed0，等待八个稳定源帧后，只重置 Gather 并从相同采样序号开始。

首次三帧中，以下九种输入逐份 SHA 完全一致，共 27 对：

- Mesh SDF voxel、field、instance、BVH node、leaf instance；
- 漫反射 surface、Radiosity work、receiver entity 映射；
- 完整 1920×1080、每像素两个 uint 的 visibility。

visibility 使用临时内存 ComputeShader 复制到 owned structured buffer 后读回，保留全部整数位。没有创建 shader 资产、`.meta` 或 include 别名，等待 GPU 请求完成后销毁临时 shader/buffer。列表型 buffer 只读取实际使用的元素区间；voxel 读取完整容量，并记录范围。原生 UInt 纹理直接读回失败，不能作为有效证据。

同一阶段的深度副本、GBuffer、HZB 0～4、motion、当前几何、引导分布、有效的上一帧历史也一致。第 0 帧已失效的 previous directional 存储不参与历史比较。两组前几帧均选择 cached receiver kernel4，sun cache 不可用，receiver8、node3，没有 kernel 切换。

第 2 帧 screen-rays 与 SDF rays 首先不一致；随后 composite、directional temporal/filter 不同，第 3 帧 raw 与 proposal 开始分歧。这里的“首先”是记录到的阶段顺序，不代表已经证明 GPU 故障原因。输出的屏幕命中载荷包含像素编码，不能将该值误读为命中距离。

这些数据排除了本次分歧由已观察到的 SDF 内容、三角形 visibility ID、CPU 矩阵或 kernel 选择变化直接引起的解释。未读出的 dispatch 常量、执行时同步与编译行为仍需定位；不能据此宣称 GPU race、驱动或浮点融合是根因。

## 已撤回的实验

| 目录（均位于 `E:/VividRP_GI_Evidence`） | 实际结果与边界 |
|---|---|
| `ScreenProbeReceiverPrepPilot-20261115-001355-482964` | 统一准备 receiver、供 guide/screen/SDF 使用的原型仍未通过短 A/A，撤回。 |
| `ScreenProbeGpuInputWitness-20261115-001847-188050` | 上述原型增加 GPU 常量见证后，1,266 对短序列哈希相同。仪表改变了结果，不能当作原型修复或根因证明。126 份 GPU 常量见证已逐位压缩保留。 |
| `ScreenProbePreciseReceiverPilot-20261115-002500-578347` | 两处 world position 输出增加 `precise`，1,203 对短序列哈希相同。 |
| `ScreenProbePreciseGpuTiming-20261115-003323-857461` | 同一精度候选的完整 1,024 帧预热协议在第二组第 1,727 帧 raw 失配，候选成本未测，撤回。短通过不足以推广。 |
| `ScreenProbePreciseRayFrame-20261115-004254-225175` | 再约束两条追踪路径的 origin/direction；只完成第一组。捕帧期间七份 Editor 源码并发修改，播放状态改变，整轮归因无效。保留失败记录，不认定为精度改善或超时。 |

并发的 Frame Debugger 改动保留，未纳入本轮 GI 提交。用户确认测试窗口后仍可复现分歧，因此没有把之前的并发操作当作全部异常的解释。

## 原生 dispatch 绑定检查

当前用户更新的 Frame Debugger 支持 Compute 事件详情。首次捕帧包括 `Camera.CustomRender/` 与普通视图的两组追踪；脚本拒绝把四个 dispatch 当作同一对。后续只选择指定相机的 `Camera.CustomRender/` scope、准确 marker 和 `SdfScreenProbeTracing` 对象，使用 owner token、events hash 与 lease，退出时释放自己的会话。

`FrameDebuggerGICustomInputs-20261115-013203-900038` 成功读取两个 kernel，但逐个选择事件时返回的 `_SdfDiffuseFrame` 分别为 295/303。这些查询未固定采样序号，不能当作同一次执行内两条路径的帧号不一致。

`FrameDebuggerGIFixedFrame-20261115-013807-646763` 用已有 Editor hook 临时固定采样序号：

| 原生事件 | kernel | 线程组 | 实际 `_SdfDiffuseFrame` |
|---|---|---|---|
| 45，Screen Trace | `TraceReceiverDiffuseScreenRays` | 15×17×1，每组 8×8×1 | 1733 |
| 46，Gather and SDF Fallback | `TraceSdfReceiverCachedProbes` | 15×17×1，每组 8×8×1 | 1733 |

该检查两场景仍加载，receiver14/node7，与隔离播放实验的 receiver8/node3 条件不同。它证明固定值到达两个原生事件，并提供纹理/缓冲绑定名称；不能替代失败 A/A 当时的完整状态快照。纹理池复用时物理资源名也不能单独证明逻辑接线错误。

返回的 `m_Matrices` 为空，`ShaderVariablesGlobal` 仅有名字，内部成员数组为空。应记录为**未暴露矩阵/全局常量**，不能记为零矩阵、常量相同或绑定已验证。源码和实际脚本指纹核验 2,781 份文件未变，两个 native capture 都正常释放，Frame Debugger 关闭。

本机当前 Editor 没有早期注入 PIX runtime，native preflight 不可用。没有将 native 事件 JSON 当成 PIX capture，也没有为此重启用户 Editor。

## 诊断工具与恢复

`ScreenProbeAnchorGpuTimingCapture.cs` 更新为 v12：开始前拒绝启用中的 Frame Debugger，采集时检查播放/暂停/debugger 状态；记录最多八个意外相机的名称、类型、场景；失败时保留 arm、frame、phase、pending、active、相机计数、源帧数和 deadline 状态。保持原有严格 A/A、完整 ABBA 和 1 GiB 二进制预算，没有放宽通过标准。

v12 的实际脚本已在 live eval 编译运行，受控失配时给出了明确的 frame2 输入错误和完整退出状态。没有运行 Unity Test Framework。现有 Python 资格检查未修改，也未把失败或缩短协议输出成候选耗时。

恢复检查确认所有诊断回调为空、override=-1、flags0、16 px/8 rays/age128/四轮/bias0.04/源8阶32批，两个场景干净，Edit Mode，自动刷新保持0。未保存 Profile、Scene 或修改同步资源资产。

下一步需使用可复现的 GPU 常量见证或捕帧，在同一个失败运行中核对 screen/SDF dispatch 的实际矩阵和 ray 构造，再决定修复位置。通过长 A/A 后才恢复 FP32/512 成本比较；继续增加历史权重或模糊半径无法解释当前输入重复性问题。

## 旁路见证、精度重建与观测控制组

同日后续实验仍使用 Cornell 固定机位、16 px、8 rays、源8阶/64 samples/32 batches。保存的 shader、Runtime 与 Profile 均已恢复，没有新增渲染质量或性能推广。

`ScreenProbeDispatchInputWitness-20261115-014752-081900/long-repeat` 在两个原追踪 dispatch 前分别运行旁路 kernel，保存64×2个float4：四组矩阵、相机/屏幕/光照常量，以及 probe(95,4) 的 receiver、basis 和八条射线。原追踪函数体未改。两组第0～6帧的七对 GPU 见证逐位一致；第3帧原几何输出仍有3,478个texel不同，最大位置差7.152557e-7 m，随后raw/proposal出现分歧。旁路 kernel 读的是自身 shader 状态，不能据此证明原 kernel 的实际常量缓冲也完全一致。

该目录另保留两个工具自身的失败：首次在没有有效receiver的SceneView上触发RenderGraph绑定校验，未进入采样；加入receiver-ready前置条件后，首组1728帧完成，但末帧重复登记geometry/raw导致记账拒绝。分别修正后另建目录重跑，没有覆盖失败或放宽校验。

`ScreenProbeOrderedReconstruction-20261115-020229-565866` 临时将精度约束放入世界坐标重建内部，显式展开矩阵乘加并约束UV运算；同时撤回旁路kernel与对应C#代码。实际DX12 shader编译诊断为空。无深层阶段读回的1728帧A/A在第二组第12帧失败，几何哈希一致，raw/proposal/rays/screen-rays不同，不能推广此方案。

| 控制组 | 结果 |
|---|---|
| 精度原型，`stages`，64帧 | 966对哈希一致，包含composite、directional temporal/filter、prepared/reconstructed anchor和previous histories。 |
| 精度原型，`stage-long`，1728帧 | 25,926对哈希一致；仅保存832,512 B二进制。 |
| 原shader，`ScreenProbeOriginalStageControl-20261115-021737-918072`，同一深层1728帧协议 | 同样25,926对哈希一致，二进制832,512 B；源码核验2782份文件未变。 |

上述比较排除第0帧已经失效的previous存储。大尺寸方向纹理只保留哈希，失配仍立即拒绝；未提供GPU计时。GPU读回本身会改变观测条件，不能将精度原型的深层对照通过归因于精度修改，也不能由此认定存在缺失barrier。原型已全部撤回，补丁和准确执行脚本保存在证据目录。

当前需要直接核对原dispatch的常量缓冲、实际资源访问和命令顺序。PIX 2606.18-preview、匹配的analyzer与Unity7000a16路径已核验；原生桥的`ERROR_MOD_NOT_FOUND`来自缺少已注入的`WinPixGpuCapturer.dll`。现有D3D12 device不能通过该桥晚加载补全捕帧条件。

原shader控制目录的`pix-relaunch-plan.json`记录了正常退出、官方PIX launch、相机/精确marker和监控阈值；`WatchPixStartup.ps1`只会附加到新launcher的Editor后代，并拒绝旧PID。监控预算30 GiB/24000 handles参考当前正常空闲Editor约22.9 GiB/11443 handles，超限仅作为终止诊断的边界，不能单独判定泄漏。脚本静态语法校验通过，尚未启动验证；重启计划另获用户明确授权，见`pix-restart-approval.json`。

`normal-state.json`再次确认回调为空、override=-1、flags0、默认age128/四轮/16 px/8 rays/bias0.04，两个场景干净。所有捕帧已结束，Editor处于Edit Mode，现有并发Frame Debugger源文件修改保留。

正常重启前的持久资产检查发现`Assets/VividRPDefaultVolumeProfile.asset`尚未保存。未修改其值；先将原磁盘文件以及内存中的Profile/29个组件JSON存入`default-profile-before-restart/`，再通过`SaveAssetIfDirty`仅保存该资产，保留重启前的状态。该项目资产不在本轮package提交中。
