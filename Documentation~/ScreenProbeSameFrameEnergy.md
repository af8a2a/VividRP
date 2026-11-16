# 同帧能量参考与黑色环境边界

本轮建立了使用**未修改原生追踪程序**的同帧参考，确认上一轮稳定像素不裁剪候选尚不能推广，并找到了更应优先处理的覆盖语义：纯自发光 Cornell 关闭天空后，大量已离开 SDF 场景的完整 miss 仍被记为未知覆盖。**尚未修改生产渲染，也未证明修正该分类后最终噪声会下降。**

后续分类实验证明消费能量基本保留，但默认引导的边缘与长窗口方差未通过，候选已撤回；见[黑色环境分类与长窗口实验](ScreenProbeBlackEnvironment.md)。本页保留当时的参考与失败结论。

起点 `28f33196`，生产渲染仍为 `7b9d8836`。证据根目录 `E:/VividRP_GI_Evidence/ScreenProbeSameFrameEnergy-20261116-071500`。保持 Unity7000.0.0a16 / D3D12、原 Cornell 机位、16 px / 每探针8 rays、原生 Gather、age512/FP32 anchor、像素历史上限32、4轮 wavelet。Radiosity 为8阶/64样本/32批，源 hash 保持 `40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。

## 参考如何固定实际输入

继续使用两套独立的颜色、矩和几何历史，共享原生 Resolve。新增参考在原生记录完成后执行：

1. 使用当前持久化的 `SdfScreenProbeTracing.compute`，将所有追踪 UAV 和屏幕光线输出转向私有资源；不重新编译或替换该 shader。
2. 用原方向、帧号和资源重放屏幕追踪及 SDF Gather，要求全图屏幕光线、几何、raw命中光照逐字一致。
3. 只对私有参考关闭采样引导，每帧生成16组均匀方向；每组都重新计算屏幕光线，随后执行 SDF Gather。场景、HZB、屏幕历史、材质、实际接收几何和源缓存保持同帧输入。
4. FP32 纹理累加 `raw.rgb × raw.a` 和 coverage，读回实际探针中心所对应的最终像素。每次结束恢复原帧号、引导、诊断开关、全部 UAV 和屏幕输入绑定。

四个实际运行的捕获共通过 **960对原生/重放全图追踪 A/A**，以及90,000对私有历史基线 A/A。`high-sample` 每点有16,384条参考射线。它估计实际接收位置的消费后 E/π，不是所有像素的高样本渲染，也不检验 Surface Cache/SDF 自身的物理误差或 Unreal 最终画面一致性。旧跨运行 Hybrid 联合参考冲突仍保留为未通过。

最初两份脚本因局部变量声明/闭包确定赋值顺序编译失败，未执行实验逻辑；失败 CLI 和原脚本保留。`qualification-v3` 修正后通过，后续没有放宽 A/A。

## 不裁剪候选没有通过推广

`high-sample` 保持1024帧预热、128帧测量。四个最终 ROI 平均能量变化仍很小，但本次天花板方差没有达到5%门槛。以同帧 raw 消费参考计算，`floor` 与 `back` 目标条带的平均场残差 MSE 分别增加 **5.97% / 5.52%**，超过事前1%上限；`edge` 降低20.52%。天花板28个中心的最终输出在两路径间完全相同。

此外，参考自身有大量未解析方向，未通过逐点平均coverage≥0.99的完整照明参考门槛。因此上述 MSE 是当前消费信号的诊断，不能当作物理真值误差。候选没有被安装，未继续对其进行 GPU 推广计时，没有删除失败区域或改判旧参考。有限块数下的参考标准误也保存在报告中。

这里沿用的 ROI 名称不能直接当作材质平面：GPU几何表明 `floor` 条带包含地面与短盒子正面，`back` 全部位于高盒子正面，`edge` 混合盒顶、背墙和高盒子；天花板条带是同一向下平面。新分析器保存实际位置范围与法线，避免将条带指标误称为某个纯平面的结果。

## 未解析方向来自哪里

`reference-reasons` 在原程序的私有参考输出上启用逐射线状态记录；诊断纹理由工具独立分配。GPU 累加 accepted、完整无屏幕阻挡的 miss、屏幕拒绝、追踪未完成、卡片/表面缺失、背面拒绝和其他原因。每个探针每帧必须恰有 `16×8` 条记录，accepted比例必须与原raw coverage一致。

| 目标条带 | 实测accepted | 完整且无屏幕阻挡的miss | 其他未解析原因合计 |
| --- | ---: | ---: | ---: |
| 天花板 | 99.9908% | 0 | 0.0092% |
| 地面/短盒面 `floor` | 72.9574% | 27.0030% | 0.0397% |
| 高盒面 `back` | 56.7627% | 43.2297% | 0.0076% |
| 混合边缘 `edge` | 80.0989% | 19.8633% | 0.0378% |

大部分缺失不是卡片覆盖或追踪步数耗尽。`TraceProbe` 仅在 `HasSkyTexture()` 时调用环境 miss 判定；天空关闭后，这些方向保持 `RayMiss`，不增加 `validWeight`，方向历史也保存为未解析方向。

## GPU 边界证明

`black-boundary-proof` 保存每个测量帧实际 SDF GPU根节点的48字节，核对32位坐标字与元数据，同时记录当前追踪范围、bias及没有天空纹理。按实际接收位置到根 AABB 最远角点的距离，再加整个bias长度，构造与方向无关的保守半径。

该捕获逐帧记录的是未绑定天空纹理；`SkyType.None` 来自受检夹具预设，结束后的三个正常渲染帧另行核对实际None/RealtimeOnly。补验不替代捕获期间逐帧的天空类型记录。生成器随后增加了实际 `VividSkyData.activeSkyType == None` 前置条件供后续实验使用；不能把“未绑定GI天空纹理”泛化成任意场景的无天空边界。

全部58个接收点、64帧均被当前追踪范围完整包住，最小余量 **5.43米**。因而这些**完整、无屏幕拒绝**的miss已经越过已知SDF场景，在本夹具的无天空边界条件下可归为已知黑色环境。这个证明不适用于超出范围的大场景、未完成追踪、屏幕命中被拒绝或缺失卡片。

若仅改变这些方向的有效性而保留零辐亮度，条带的平均 coverage 可分别达到99.9908%、99.9603%、99.9924%、99.9622%；最差单点平均也超过99.87%。这是**反事实分类统计**：没有写回渲染器coverage、修改参考RGB、放宽门槛或宣称最终画面已改善。`RGB×coverage` 的原始能量在这种分类下应保留，后续仍须实测 FP16 舍入、方向累计、SH 与像素重建。

两次64帧参考的消费RGB/coverage累加、接收几何及原因计数共 **1,024份读回逐字一致**，见 `uniform-reference-repeatability.json`。这只证明本次均匀参考的重复性；原生引导历史和最终输出是不同运行，不声称也相同。

下一步应对原生路径增加有限边界内的已知黑色环境分类，只接纳确证离开场景且无未完成/屏幕阻挡的miss；保持其他失败仍未解析。然后比较 raw消费能量、方向有效年龄与覆盖率、校准SH、最终平面噪声，复验新露出和光照变化，通过质量检查后再测 GPU 开销。

## 验证与恢复

109项Python诊断检查通过，包括完整射线计数、coverage等式、平均能量与空间误差分离、原生绑定恢复、边界半径和bias检查。内存 DX12 accumulator 编译、原生追踪 GPU重放、读回、捕获后源码/脚本指纹核验通过。正在使用的 Editor 内未运行 Unity Test Framework。

所有实际脚本和 shader 输入按捕获目录保留；每轮完成时核验后才继续修改诊断模板。完成后运行1026个原生产帧，核对原 Hybrid、16/8、8阶/32批、4-pass、原 Temporal/wavelet 程序，清空回调，恢复干净场景/Profile、播放暂停、Frame Debugger关闭和PIX idle。没有修改Runtime、生产shader、资产或meta。

已归档被完整参考取代的资格运行和逐字重复的原因读回中间帧，保留源码、hash、报告、聚合和首末帧；被归档序列不可再做完整原始帧重分析。完整高样本与GPU边界证明数据保留。存储审查与删除日志见根目录 `post-experiment-storage.json` 和 `*-archive-apply.json`。

本轮释放91,436,608字节（约87.2 MiB），新目录约298.7 MiB。继承旧清单后，证据总量估计124.97 GiB，E盘剩余约111.69 GiB；较早目录未重新全量扫描，当前目录不跟随链接。

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeSameFrameEnergy.py NEW_REFERENCE --sets 16 --measured 128
python -B Tools~/Diagnostics/BuildScreenProbeSameFrameEnergy.py NEW_BOUNDARY --sets 16 --measured 64 --record-reference-reasons
python -B Tools~/Diagnostics/AnalyzeScreenProbeSameFrameEnergy.py COMPLETE_CAPTURE
python -B -m unittest discover -s Tools~/Diagnostics -p 'test_*.py'
```

执行前固定实际脚本/协议/输入，完成后立即核验。每次捕获二进制上限256 MiB，工具拒绝覆盖已有目录。`qualified`表示受控数据通过检查；推广还必须满足独立的样本数、coverage、质量与运行验证门槛。
