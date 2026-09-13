# VSM 阴影噪声稳定性：Unreal 对照与保守裁剪改进

2026-09-13。已保留一项生产改动：**根据当前帧与历史的差异，有限放宽短历史的邻域裁剪**。完整生产路径的 256 帧相位对照中，半影平均时间标准差降低 **7.7%**，地面半影降低 **9.1%**；相邻帧差异分别降低 **8.7% / 10.6%**。GPU Resolve 成本基本持平。这是渐进改善，尚未消除用户 GIF 中的全部颗粒变化。

最终只修改 [CSMShadowResolve.compute](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute)。4 帧历史上限、自适应 2/4 射线、采样序列、求交规则、页预算、16 层双深度池和上一轮空池跳过全部保留。没有新增生产资源或 C# 渲染循环操作。工作起点为 `56d0c7b2`；修改前后指纹见 `source-before.json`、`source-after.json`。

## 对比 Unreal

Epic 的 [VSM 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/virtual-shadow-maps-in-unreal-engine)明确指出，半影噪声与射线数量有关，Epic 阴影档默认使用 8 条射线；光源角度和自适应射线数也会影响投影成本。当前场景配置为 4 条半影射线，因此不能把它与 Unreal 的高档成片直接视为相同预算。

Epic 的 [TSR 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/temporal-super-resolution-in-unreal-engine)还说明了历史积累、运动矢量重投影、着色拒绝与闪烁分析之间的配合；闪烁抑制在运动和较大视差处需要限制，以免把真实变化积累成拖影。我们的基线 AA=None，阴影依靠自己的深度／法线重投影及短历史，没有这一整套 TSR 稳定机制。

本次参考的是“区分随机波动与真实变化”的思路。没有 Unreal 同场景实测，没有访问或复刻 Unreal 私有 SMRT；本文的数值全部来自 VividRP。8SPP 资源也不等同于 STBN 或 Unreal 的实际采样实现。

## 定位与最终规则

用户 GIF 为 906×566、22 帧、约 1.77 秒。其规则点阵可能包含调色板抖动，不能作为原始阴影误差度量。量化使用 GPU 浮点阴影输出。

同帧重放验证了原短历史路径：静止半影的平均历史年龄约 3.99，年龄重置不是主要原因。只延长历史到 8 帧收益有限；放宽裁剪则明显改变噪声。这支持“噪声造成的邻域范围波动限制了积累”的判断，不能据此断言所有残余噪声都来自裁剪。

旧规则把历史限制在当帧 3×3 邻域的最小／最大值之间。低射线数下这个范围自身会波动，持续把已经积累的历史拉回当帧噪声。最终规则为：

```hlsl
float margin = min((high - low) * 0.5,
    max(0, 0.05 - abs(history.x - current)));
float old = clamp(history.x, max(0, low - margin), min(1, high + margin));
```

变化达到 0.05 时余量归零，恢复原裁剪；邻域为常量时余量也为零。原有深度／法线拒绝、0.2 变化重置和四帧上限继续生效。0.05 是本轮经过运动约束验证的保守阈值，不宣称是所有内容的最优值。

## 最终生产测量

Unity 6000.7.0a6、DX12、RTX 5070 Ti、1920×1080、AA=None。VSM 2048、256 页、128 页尺寸、16 层；SMRT 自适应 2/4 射线、8 步、光程 10、光源角直径 7.1°。沿用原相机和光照，每侧至少预热 64 帧，采集完整相位 0–255。相机矩阵、尺寸、位置与采样相位逐帧一致。阴影历史包含实际生产的自适应反馈。

分析区域为 GPU 坐标起点 (482,162)、步距 4、240×180 个点；半影按基线时间均值在 0.02–0.98 之间选取。地面子集另要求解码法线 y>0.95。此处“平均时间标准差”是每个点跨帧标准差的平均值，不能替代所有 RMS 或视觉指标。

|指标|原版|最终候选|变化|
|---|---:|---:|---:|
|半影平均时间标准差，12,398 点|0.0126724|0.0116908|−7.7%|
|半影 RMS 时间标准差|0.0173453|0.0167345|−3.5%|
|半影平均相邻帧差异|0.00851845|0.00777628|−8.7%|
|地面半影平均时间标准差，3,274 点|0.0113317|0.0102955|−9.1%|
|地面半影平均相邻帧差异|0.00734164|0.00656013|−10.6%|

半影各点时间均值的平均绝对变化为 0.000714，P95 为 0.002323；整体平均可见度从 0.539022 变为 0.538945。本轮没有重新建立高采样几何真值，因此这些是相对基线的变化，不能直接称为参考误差改善。

![最终均值与噪声对照](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNoiseStability_20260913/final-guarded-production.png)

完整指标见 [final-guarded-production.json](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNoiseStability_20260913/final-guarded-production.json)。图像按 GPU 坐标翻转为正向显示，使用相同色标；原始最终画面保存在 `raw-final/capture.tar.xz` 内。

### 暗部与运动约束

原箭头点 (1070,474) 在最终双方 256 帧中最大可见度均为 0。基线始终严格为零的 20,582 个点上，候选最大值为 **0.0000311136**；不把它写成“所有暗部逐位不变”。宽松的暗部集合（均值小于 0.0001）的平均均值变化约 2.32e−8。见 [dark-region-check.json](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNoiseStability_20260913/dark-region-check.json)。

真实 GPU 内核测试覆盖水平／垂直、0.25／1／4 像素每帧、单边缘／两像素细条带，共 12 类、每类 48 帧。以空间滤波结果作为即时响应基准：

- 单边缘最大等效延迟均为 **0.5966 像素**。
- 原五像素支持范围外的残留在双方全部用例中均为 **0**。
- 单边缘的最大延迟和绝对误差面积保持一致；最慢水平移动在空间极值点的局部误差增加约 0.00113。最慢水平细条带的最大绝对误差面积从 0.59507 变为 0.59620，增加约 0.00113（约 0.19%）。这些差异原样保留。
- 这里的带符号面积只适合单边缘延迟；细条带同时检查绝对误差面积，避免前后缘误差抵消。`atSpatialExtrema` 不是“支持范围外”：空间滤波刚到 0/1 的点仍可能落在时间裁剪的邻域支持内。

见 `motion-final-baseline.txt`、`motion-final-candidate.txt` 及对应 GPU 脚本。另有 8 类真实 GPU 历史失效测试通过，包括新亮、新暗、常量、全局失效、深度／法线失效、越界和天空，见 `temporal-final.txt`。最终方案没有做新的真实骨骼变形场景覆盖；不能据这些合成用例宣称所有动态阴影均无拖影。

### GPU 成本

单独执行 A/B/B/A，每窗口预热 64 次、记录 128 次；计时观测不执行纹理回读或逐帧 JSON 写入。使用 `VSM.Resolve` GPU sampler，包含接收者 resolve 和滤波，不是整帧时间。

|窗口|原版 P50 ms|候选 P50 ms|原版 P95 ms|候选 P95 ms|
|---|---:|---:|---:|---:|
|前序|6.0705|6.0762|6.5759|6.6469|
|反序|6.0820|6.0742|6.6997|6.6052|

成本变化处于本次编辑器波动范围内，不宣称加速。计时使用同一个带诊断开关的 shader 构建，分别启用原裁剪／候选余量；最终生产文件移除了该开关。GPU Recorder 对应最近完成的 GPU profiling frame，观测相机帧号不代表精确 GPU 帧身份。原始观测见 `timing/`，汇总见 `timing.json`。

## 未采用的候选

|候选|测得结果|决定|
|---|---|---|
|仅延长历史到 8 帧|同帧静止半影标准差约降低 7.2%|收益不足以优先增加历史长度|
|8 帧加连续创新量权重|标准差反而增加约 2.1%|不采用|
|2/4 射线等角度排列|解析半平面有收益，实际半影仅约降低 1.2%；更高射线预算并不普遍更好|不采用|
|8SPP 纹理、完整 256 帧序列|只替换接收者相位基本无变化；替换全部相位反而增加约 3.3% 噪声|不采用|
|无变化量保护的裁剪余量，4 帧|完整生产半影标准差降低约 22.2%，但移动细条带绝对误差面积明显增大|不采用|
|无变化量保护的裁剪余量，8 帧|同帧静止标准差降低约 39.8%，运动延迟更大|不采用|

探索性同帧重放覆盖静止、相机平移、光照阶跃各 128 帧；两个采样试验分别覆盖两侧／三侧各 256 帧。运动场景中的时间标准差包含真实阴影变化，未把其降低直接解释为降噪。中间结果保留为 `temporal-replay.json`、`sampling-comparison.json`、`bnd-comparison.json`、`clipping-replay.json`、`unconditional-production.json` 和 `rejected-trial-fields.npz`，**不要把其中 22%／40% 的数字当作最终采用版本的收益**。

## 编译、恢复与复现

最终 DXC：生产 28 个入口、SamplingTests 19 个入口，**47/47 通过**。独立 Roslyn 编译 Runtime、Editor、Editor.Tests 全通过，编译期间源树和 Bee response 稳定。当前生产 compute 的 Unity shader 错误为 0；这不代表整个 Console 没有此前其他模块的错误。交互 Editor 一直开启，未运行 Unity Test Framework；相关阴影 Unity 测试需由用户手动运行。

所有临时 Editor 脚本、shader 及其 Unity 生成的 meta 已通过 AssetDatabase 移除。相机、相机 Transform、光源 Transform 序列化值与开始时相同，阴影设置的有效值相同；Sponza 和 SponzaLightingDay 开始、结束均为 dirty=false，未保存场景、未清除 dirty 标志，Play=false、runInBackground=false。解析后的 Volume 里，TemporalDenoise 和 AdaptiveRays 的 override 标志从 false 变为 true，但有效值始终为 true；没有修改配置资产去强制还原这两个计算状态。详细差异见 [state-restoration.json](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMNoiseStability_20260913/state-restoration.json)。

最终 512 帧原始 GPU 历史、元数据和画面已无损归档为 `raw-final/capture.tar.xz`，约 25.19 MB；哈希覆盖解压后的浮点字节。已经实际恢复全部 1,031 个文件并重算指标，除捕获根路径外结果完全一致。拒绝候选的逐帧数据保留在本机 `Temp~/vsm-noise-stability/`，报告只保存其统计及均值／标准差场，以减少重复数据。

从包目录复现最终数据分析：

```powershell
python Roadmap~/Experiments/VSMNoiseStability_20260913/archive-final.py restore Roadmap~/Experiments/VSMNoiseStability_20260913/raw-final Temp~/vsm-noise-stability/repro
python Roadmap~/Experiments/VSMNoiseStability_20260913/analyze-sampling.py Temp~/vsm-noise-stability/repro --variants baseline4 clip4 --output Temp~/vsm-noise-stability/repro-analysis.json
```

`comparison-build.compute.txt` 保存最终 A/B 时的完整生产 shader，含临时 `_VSMNoiseStrictHistoryClip` 开关；基线为 1、候选为 0。若重跑 A/B 工具，需恢复这个诊断构建，不能在已移除开关的最终 shader 上设置同名整数并假定获得旧算法。`VSMNoiseSamplingProbe.cs.txt` 为最终静态采集／计时器；`.cs.txt` 均为供恢复的归档，导入时由 Unity 生成 meta。`VSMNoiseStabilityProbe.cs.txt` 是较早同帧重放工具，依赖已移除的临时水平滤波源接口，不是可直接在最终生产代码上运行的插件。

更强的抑制仍需要能区分采样方差与真实遮挡变化的可靠信息。本轮测量支持先保留这一受约束的小改动，继续以原漏光位置、细几何和运动响应作为后续质量边界。
