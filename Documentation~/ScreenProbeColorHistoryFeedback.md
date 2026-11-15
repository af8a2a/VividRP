# Hybrid 输入见证与颜色历史反馈试验

本轮验证了两类限制：此前Hybrid参考的单块冲突未在指定窗口复现；普通空间滤波回写虽然降噪，却造成能量下降。对称守恒交换能保持全图能量，仍未通过天花板方差和区域均值的联合门槛。**三个候选均未启用到默认路径，生产Runtime、shader、场景和Profile未修改。**

起点 `a783341e`，生产渲染仍为 `7b9d8836`。Unity 7000.0.0a16 / D3D12 / RTX5070Ti。保持Cornell固定机位、16 px / 每探针8条射线、原生Gather、4轮À-trous、FP32 anchor age512和像素历史上限32。Radiosity固定8阶 / 64样本 / 32批，hash仍为 `40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。

## Hybrid 指定窗口

证据：`E:/VividRP_GI_Evidence/ScreenProbeHybridInputWitness-20261116-052500`。

`BuildScreenProbeHybridInputWitness.py`在同一次受控捕获中分别固定之前的两种Gather几何，各预热832帧、测量64帧，使用25387序列，对应旧冲突的uniform-b帧832–895。关闭采样引导，显式Hybrid；不固定屏幕追踪输入。保存28个目标探针对应的屏幕光线、逐射线状态/辐亮度及首命中；完整HZB、Motion、屏幕几何/辐亮度历史、GBuffer0/1逐帧计算hash，记录CPU矩阵位模式。前一帧逆矩阵明确标记为重新计算值，不能代替GPU常量缓冲见证。

结果：128帧的256个当前原生位置/相同位置强制重放对通过。六类屏幕输入及CPU矩阵跨组全部一致，11个共同几何目标的光线和raw输出均逐字一致，块均值完全相同，28目标块均值均为0.2612614264。旧单块冲突没有复现；旧联合能量参考仍保持未通过，未因本次结果放宽或删除失败。

## 颜色历史回写

证据：`E:/VividRP_GI_Evidence/ScreenProbeColorHistoryFeedback-20261116-054500`。

当前实现把空间滤波前的颜色保存到像素历史，À-trous只影响当前输出。新增内存shader和Editor捕获hook，检验回写是否增加有效空间样本；没有修改生产shader或资源绑定。每个候选都有两次基线与一次反馈运行，各1024帧预热、256帧测量，同1733序列。保留默认重要性采样引导，记录最终天花板、地面、背墙和板边ROI。

事前门槛：相对两个基线，天花板最终像素时域方差至少下降5%；四个ROI平均能量变化均不超过1%。只有通过后才运行独立序列、移动/新露出区域、边缘色溢、高采样能量参考与GPU开销验收。本轮没有扩大射线数、提高曝光、补偿增益或放宽门槛。

| 候选 | 天花板方差相对两基线 | 天花板均值变化 | 结果 |
| --- | ---: | ---: | --- |
| 第2轮À-trous直接回写 | 下降36.7%–40.7% | −3.12%至−3.00% | 能量失败 |
| 对称守恒交换，矩不变 | 上升33.5%–53.1% | −0.54%至−0.51% | 噪声失败，地面均值也略超1% |
| 守恒交换，同步矩均值 | 下降18.1%–23.8% | −1.81%至−1.79% | 能量失败 |

普通回写对天花板历史每次平均注入约−0.1339%的能量变化，见`pilot/history-injection-summary.json`。重复回写会累积这种偏差；不能把更平滑但变暗的结果作为质量提升。首个原型的无效历史判定不充分，虽保持零alpha且未重新激活无效历史，也已在后续工具中改为显式检查有效alpha。其原始shader、脚本及失败结果完整保留。

守恒候选使用两个pass。第一个计算邻域权重和，第二个用`w_ij / max(sum_i, sum_j)`交换覆盖后的RGB能量；配对深度平面、法线和亮度权重对称，保留对角自权重。每次交换的跨像素贡献理论上成对抵消。实际全分辨率RGB总量读回通过0.01%门槛，最大单通道变化低于0.0002%，有效像素数不变。全图守恒不等于局部ROI能量或重建质量通过。

颜色历史改变后，原生时域矩均值仍描述回写前的信号。第三个原型给矩均值增加新旧条件辐亮度的log亮度差，在本帧最后一轮空间滤波完成后才发布。读回确认观测方差、矩年龄、coverage逐字不变；所有候选的颜色历史alpha（覆盖率/年龄/状态）也保持逐字一致。这个一致性调整改善了本次天花板方差，仍没有达到区域能量要求。

## 对比的实际限制与下一步

三组试验的几何在对应帧逐字一致，但独立运行的raw探针光照在256帧中全部不逐字一致。基线A/B本身也出现约6%–13%的天花板方差差异。固定相机和帧号没有固定所有引导/缓存状态；bootstrap只描述这些有限窗口，不能解释为独立物理样本的置信范围。

因此本轮只把这些结果用于拒绝推广和收窄下一步，不据此宣称候选达到Lumen质量，也不能完全归因所有均值变化。各次`analysis.json`与`causal-review.json`共同保存raw目标均值、输入差异、局部与全图能量、历史元数据见证及未验证项。

下一步应复用`ScreenProbeVarianceHistoryReplay.cs.txt`与`BuildScreenProbeVarianceHistory.py`已有的同帧重投影绑定，为基线和候选建立独立的完整颜色历史与矩历史：

1. 共享本帧raw Resolve、几何、Motion和矩阵，候选不写回渲染器历史、不影响下一帧采样引导。
2. 先让私有基线逐帧复现原生Temporal及4轮À-trous，验证输出、覆盖率/年龄/状态和历史初始化A/A。
3. 再改变私有候选的颜色历史反馈，保留同样的方差/能量门槛及旧Hybrid参考失败；通过后才扩展独立序列、移动与GPU验证。

这样能区分上游采样波动和重建递推的实际影响。尚未实现这个完整颜色历史重放，不能把目前只维护独立矩的重放当作已经具备它。

## 验证、恢复与清理

四组实际捕获均完成DX12内存shader编译、GPU执行、读回及源码/脚本/输入指纹核验。94项Python诊断检查通过；活动Editor内未运行Unity Test Framework。读回和额外dispatch改变工作量，没有GPU性能结论。

试验结束后明确丢弃相机的候选历史，并用默认生产路径重新运行1026个正常帧。最终检查确认原参数、默认Hybrid、原生Gather、原shader、干净场景/Profile和清空的诊断回调；Editor播放暂停，Frame Debugger关闭，PIX空闲。本轮没有Editor重启、Unity资产改动、手写meta或目录链接。工具的finally也增加了候选历史清除，避免只恢复参数却留下被拒绝的缓存。

按明确清理策略归档普通回写与未同步矩的守恒候选的中间读回，释放475,779,072字节（约453.7 MiB），保留首末帧、每16帧历史见证、全部聚合、源码、协议和报告。它们不再可做全序列重分析。同步矩候选完整数据与Hybrid见证保留。两个新目录约24.4 / 334.8 MiB，证据总量估计124.53 GiB，E盘剩余112.13 GiB。精确空间、逐文件删除hash及当前工具归档见两目录的`post-experiment-storage.json`、`tool-archive/manifest.json`和`*-archive-apply.json`；既有生产基线与高采样参考未删除。

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeHybridInputWitness.py NEW_WITNESS --geometry-0 GEOMETRY_0.bin --geometry-1 GEOMETRY_1.bin
python -B Tools~/Diagnostics/BuildScreenProbeHistoryFeedback.py NEW_PILOT --method conservative-moments
python -B Tools~/Diagnostics/AnalyzeScreenProbeHybridInputWitness.py COMPLETE_WITNESS
python -B Tools~/Diagnostics/AnalyzeScreenProbeHistoryFeedback.py COMPLETE_PILOT
python -B -m unittest discover -s Tools~/Diagnostics -p 'test_*.py'
```

构建后先固定源码与实际脚本，再通过活动Unity CLI运行，结束立即核验。分析器拒绝把已归档的残缺原始序列当作完整捕获。`--method`必须明确指定；这三个选项均为诊断原型，不代表推荐默认画面设置。
