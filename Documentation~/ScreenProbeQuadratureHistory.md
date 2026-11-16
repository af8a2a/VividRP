# 四分层插值的完整像素历史验证

四分层Resolve经过独立的像素时域历史和原4轮À-trous后，天花板方差在两组独立序列中分别下降44.19%与48.71%。平均能量门槛通过。**生产默认仍未改变**：当前只验证了共享原生探针输入下的重建，移动时保守条件几乎全程关闭候选，未测量GPU开销、停车后重新启用及高样本物理参考误差。

证据目录：`E:/VividRP_GI_Evidence/ScreenProbeQuadratureHistory-20261116-105600`。生产源码基于`0c0775ec`；本次未改动`Runtime/`、`Shaders/`、场景或Profile资产。

## 实验边界与一致性

Cornell纯自发光顶部面片，原机位`(0,1.5,-3.8)`、VFOV42、1920×1080；原生Screen Probe Gather，16px网格、8rays/probe、FP32 anchor age512、pixel age32。Radiosity固定8阶/64样本/32批，source SHA256为`40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。天空None、太阳0。先冻结source，再只失效Gather历史元数据，暖机1024帧、测量128帧。所有质量量均为材质反射率相乘前的线性`RGB×coverage`，不是Unreal/物理真值。

`BuildScreenProbeQuadratureHistory.py`复用[raw原型](ScreenProbeResolveComponents.md)，将候选诊断负alpha通过owned纹理转回正coverage。原生、私有基线和私有候选共享当前探针与几何；两条私有路径分别持有颜色、矩、几何ping-pong历史，执行原时域shader和原4轮小波。候选不写原生历史、不反馈到探针采样引导。基线使用内存Gather原型的flag关闭分支，不能称作未改动的生产PSO。

每组在全部128测量帧和暖机0/1/2/1023帧检查完整图像A/A。两组静态分别通过132对Resolve、3300组时域输出/颜色历史/矩/几何/最终输出的native/private字节一致性；移动组通过132对Resolve、1320组private比较。ROI最终输出还逐帧等于实际原生最终输出，排除了遗漏后置近场AO的误报。每组2812份源文件/输入和7份实际脚本核验不变。6个Gather kernel、原时域/小波和覆盖率恢复kernel的DX12编译通过。

## 修复诊断重放的历史兼容条件

首个`static-a`试跑在第1帧被A/A门槛拦截，未用于质量结论。冷epoch后屏幕追踪第0帧不可用，第1帧恢复；原生`Record`还要求`HasScreenRays`、`ScreenCardLighting`等上一帧配置兼容，因此第1帧也拒绝历史。旧私有重放只检查通用`m_HasHistory`与历史纹理，过早接受了第0帧。

`ScreenProbeColorHistoryReplay.cs.txt`现在镜像这些局部兼容条件，包括接收器修正、OpenPBR与Global Clipmap状态；缺少完整receiver kernel时明确拒绝重放。修正后实际历史有效性为`false/false/true`，`static-a-v2`、`static-b`和`motion`均通过严格A/A。没有放宽误差阈值或修改生产失效行为。

## 最终像素静态结果

序列1733与9481各自对比同帧原生控制；事前门槛为天花板方差至少下降5%、其他ROI方差最多增加2%、各ROI的Y与每个RGB通道均值变化绝对值≤1%、逐帧差RMS最多增加2%。两组全部通过。

| ROI | 方差下降：1733 / 9481 | Y均值变化：1733 / 9481 | RGB均值变化最大绝对值：1733 / 9481 |
| --- | ---: | ---: | ---: |
| 天花板 | 44.19% / 48.71% | −0.0035% / +0.0544% | 0.0427% / 0.1428% |
| floor | 15.55% / 6.45% | +0.0290% / +0.0481% | 0.0348% / 0.0556% |
| back | 3.81% / 21.07% | +0.0415% / +0.0920% | 0.0480% / 0.0997% |
| 混合边缘 | 2.61% / 1.82% | +0.0424% / +0.0736% | 0.0502% / 0.0777% |

天花板逐帧差RMS分别下降41.33%/40.95%；最大回退为第一组back的+0.259%，低于2%门槛。天花板相对时间RMS为0.1039%→0.0776%、0.1002%→0.0717%。这是两个短窗口，仍存在静态偏差/低频空间误差；128帧内4个时间块的bootstrap仅描述本窗口，不能据此声称全面无噪点或达到Lumen质量。

## 固定轨迹的实际覆盖范围

保持既有128帧正弦轨迹：侧移0.28m、前移0.06m、yaw−7°。73帧包含3894个明确新露出像素样本；两路径的旧age比例与历史接受率均为0，temporal减当前Resolve的P99/最大误差均为0。最终减当前Resolve的P99/最大值均为2.270/4.691，含空间滤波效应，不能当作历史残留或物理参考误差。

**候选只在第一个测量帧启用，后127帧因保守条件回退原插值。** 天花板启用像素帧比例0.78125%，edge约0.55485%；新露出样本中启用数为0。这验证了关闭候选时的历史过渡与遮挡边缘回退，不能声称验证了移动中四分层降噪的收益。还需补停车后长窗口恢复、重新启用时的亮度连续性、生产整链以及GPU开销。

后续已完成停车恢复：57帧后重新启用且亮度连续性门槛通过；GPU计时仍未通过可比性校验，见[恢复与计时资格实验](ScreenProbeQuadratureRecovery.md)。

## 重现与保留

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeQuadratureHistory.py <new-output> --sequence 1733
python -B Tools~/Diagnostics/BuildScreenProbeQuadratureHistory.py <new-output> --sequence 9481
python -B Tools~/Diagnostics/BuildScreenProbeQuadratureHistory.py <new-output> --sequence 1733 --motion
```

在已授权、暂停的干净Cornell PlayMode中通过Unity CLI执行生成的`capture-script.cs`；捕帧前后运行`VerifyGiSourceSnapshot.py`。静态用`AnalyzeScreenProbeQuadratureHistory.py`、移动用`AnalyzeScreenProbeQuadratureMotion.py`，两者均要求`--output`。运行Editor期间不执行Unity Test Framework。

两组静态payload各274,996,960 B，低于320MiB；移动组上限384MiB。完整三组合格序列保留。旧raw-only quadrature内部帧已归档156,033,752 B，端点/每16帧/源码/hash/报告/聚合量保留；首次无效试跑的完整失败图像按审计清理，区域差异、全部原始hash和失败原因保留。容量与恢复核验见本轮`post-experiment-storage.json`、`restoration.json`，未创建临时链接。
