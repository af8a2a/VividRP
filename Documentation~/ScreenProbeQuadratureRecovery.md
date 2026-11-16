# 四分层插值的停车恢复与计时资格

停车后候选能恢复启用，重新启用附近的最终亮度连续性门槛通过。**生产默认仍未改变，GPU开销仍未获可信归因**。接下来应核对串行执行区间、事件时刻常量与回放输出，再进行原生整链候选验证。

后续已核验同一捕帧的实际PSO与事件常量；原生整链控制仍未通过重复性门槛，候选继续不启用，见[原生整链检查](ScreenProbeNativeQuadrature.md)。

证据根目录：`E:/VividRP_GI_Evidence/ScreenProbeQuadratureRecovery-20261116-113000`；生产源码基于`6c4cfccd`。Unity7000.0.0a16、CoreRP17.7、D3D12、RTX5070Ti。本次没有改动Runtime、shader资产、场景、Profile、Graphics Jobs设置或重启Editor。

## 停车恢复

沿用[完整像素历史实验](ScreenProbeQuadratureHistory.md)的Cornell纯自发光顶部面片、原机位和16px/8rays；Radiosity固定8阶/64样本/32批，source SHA仍为`40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。两条私有路径共享原生探针输入，分别维护颜色/矩/几何历史，执行原时域与4轮小波。候选负alpha诊断标记先转为正coverage，未写原生历史。

冻结source后只失效Gather元数据，暖机1024帧；随后执行原128帧正弦轨迹，再停在原机位128帧，共测量256帧。两块ROI为ceiling和混合edge。首帧GPU统计age=1、其他统计为0，coverage一致，anchor消费能量在既有FP16界限内；没有继承前一试跑的有效历史。260对完整Resolve A/A、3900组private/native时域、历史、矩、几何、最终输出比较全部字节一致。2824份源文件/夹具/依赖及7份实际脚本核验不变。

实际矩阵确认第128个测量帧停车；第185帧首次重新启用，即停车57帧后。天花板最终启用率100%，edge为71.02%。

| 指标 | 天花板 | 混合edge | 事前门槛 |
| --- | ---: | ---: | ---: |
| 重新启用附近最大区域均值单帧变化 | 0.004634% | 0.001306% | ≤0.25% |
| 最大像素变化P99 | 0.200044% | 0.115761% | ≤2% |
| 末64帧Y均值相对基线 | +0.05546% | −0.00189% | 绝对值≤1% |
| 末64帧RGB均值最大绝对变化 | 0.06342% | 0.00224% | 每通道≤1% |

单帧变化使用`Δ(candidate−baseline)`，去除两条路径共有的源收敛变化；百分比除以基线ROI均值，不是每个暗像素的局部亮度。对所有停车后启用率增加的帧及邻近历史恢复窗口检查，未只选择首次少量像素。

末64帧天花板方差点估计下降2.53%，edge增加0.208%；该窗口仍含anchor恢复收敛，不能替代此前静止长窗口44%～49%的降噪结果，也不能称作物理/Unreal参考误差。本组只支持停车恢复的连续性与均值门槛。

![停车恢复启用率与最终均值差](E:/VividRP_GI_Evidence/ScreenProbeQuadratureRecovery-20261116-113000/recovery-curves.png)

作图数据保存在`recovery-curves-input.json`。完整恢复分析为`recovery-analysis-final.json`，124,439,264 B二进制低于160MiB上限。只保存两块ROI的完整最终序列和关键历史帧，其余时域/历史保持全帧A/A哈希，避免再积累整屏逐帧图像。

## GPU Profiler与PIX

首先使用正常7plane SH、相同owned atlas和相同帧原生探针输入，交替AB/BA/BA/AB顺序重放两个Resolve。计时标记只包Dispatch，排除atlas复制；保留区间内不发起图像读回。5对完整图像A/A、source hash及实际候选启用均通过，但两个GPU Recorder均返回0个样本。`timing-v2-analysis-final.json`明确`available=false`，未采用CPU时间替代。

首次计时生成器遗漏原`Log`函数而在编译阶段失败，修正后用新目录重跑，原记录保留。首次PIX工作流的preflight又被长eval占用Unity CLI队列而超时，尚未开始捕帧；持有脚本正常结束后，改为Editor主循环拥有的异步任务，CLI立即返回。`hold-terminal.json`是任务终止结果，`scheduled=true`仅表示调度成功。任务仍使用原捕获/恢复函数和取消文件；`finally`释放本次资源与程序集锁。

实际捕获使用仓库[vivid-pix-workflow](../.agents/skills/vivid-pix-workflow/SKILL.md)与标准`vivid_pix_agent.py diagnose`，复用已注入的PIX2606.18-preview。目标相机为`Emissive Cornell Camera`，精确marker为`VividRP.RenderPass.Record/12:SdfDiffuseGatherPass`。两份canonical捕帧均通过真实非零GPU工作、四逻辑队列边界与Console游标检查。

单次负载捕帧`pix-pair-v2/capture-1.wpix`，SHA256为`702db6c7c649b36aa3060b6ccc825c55ffeeeb5358d61ce8d8c433b14b2aaf57`。从该捕帧自身事件树定位，queue1/event912与929分别位于精确Baseline/Stratified标记下，均为240×135×1 Dispatch、queueType0。静态视图14项，SDK访问查询返回11项；共享840×136 SH atlas和几何/接收面资源，输出分离。CBV偏移不同是观测事实，尚未逐字段证明事件时刻常量仅差算法flag。Shader entry/hash在SDK中缺失，未补写为已知。

四轮交替回放的叶事件EOP duration分别为320～352ns、192ns。为检查其能否代表完整工作，又创建每个标记重复8次相同Resolve的独立负载；未增加探针射线、未改变最后输出，原生/复制基线A/A和实时候选输出检查仍通过。`pix-pair-eight/capture-1.wpix`的SHA256为`44822cb065b33082e64203d647cdc22fd3308cb24c23609cd32e77e15fdbc8ec`。

| 完整作用域EOP duration | 单次负载捕帧 | 8次重复负载捕帧的4轮回放 |
| --- | ---: | ---: |
| 私有Baseline | 1,120ns | 7,537,472～7,945,504ns |
| 私有Stratified | 960ns | 5,632～5,792ns |
| 原生Resolve（两组均1次） | 1,111,040ns | 1,088,672～1,098,560ns |

这些是API原始时序，**缺少可比的单次工作归因**。Baseline的1→8作用域比例约6966，不按8解释；也不将Stratified数值除8后称作算法成本。尚未定位串行依赖、时序边界或回放状态的具体影响。`pix-timing-qualification.json`明确`performanceQualified=false`；不据此宣称加速/减速、整帧开销或瓶颈。保留原始查询、身份/hash及缩放记录，下一步先校验常量/输出和可归因区间，不重复依赖空的Profiler样本。

## 重现与恢复

生成恢复脚本：`python -B Tools~/Diagnostics/BuildScreenProbeQuadratureRecovery.py <new-output>`；在已授权、暂停的干净Cornell PlayMode运行生成脚本，再运行`AnalyzeScreenProbeQuadratureRecovery.py <output> --output <new-report>`。

PIX持有脚本由`BuildScreenProbeQuadraturePixHold.py`生成，可用`--dispatch-repeats 8`创建校准负载。CLI执行后等待`ready-for-pix.txt`，用维护工作流的当前preflight marker进行捕帧；工作流终止后写`done.txt`，等待`hold-terminal.json`成功并核对`capture.json`。`cancel.txt`用于本次所有者取消。`ReplayScreenProbeQuadraturePix.py`仅自动定位单Dispatch对照并输出原始API结果，始终不授予性能资格；8次作用域查询的完整请求与响应单独保留。

全部临时shader已释放，原持久化Gather/Hybrid追踪、默认参数与程序hash恢复；1025正常帧预热后再次核验，Editor仍在PlayMode并暂停，场景干净、诊断0、hooks为空、Frame Debugger关闭、PIX idle。未在运行中的Editor启动Unity Test Framework。

仅删除两份闭合的PIX live副本，与canonical逐份SHA核对一致，共释放438,947,652 B。两份canonical、所有恢复ROI序列、源码、失败/拒绝记录和查询保留。容量复核见`post-experiment-storage.json`；125GiB为软审查线，本轮保留新证据并要求下次大捕帧前继续复核，未删除旧的已接受高样本参考。没有创建临时链接。
