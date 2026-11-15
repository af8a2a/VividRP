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
