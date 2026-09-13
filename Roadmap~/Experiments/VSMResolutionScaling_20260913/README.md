# VSM 分辨率成本与 Unreal 的实现差距

2026-09-13。分辨率升高导致的性能退化已通过当前编辑器实测复现。主因集中在 **ResolveTrace 的高分辨率分层深度求交**；完整虚拟页表扫描、单线程分配及页范围剔除也会随分辨率增长。没有修改生产 shader、运行时代码、场景资产或已有 Volume Profile。

## 测量条件与恢复

Unity 6000.7.0a6，RTX 5070 Ti，DX12，1920×1080，AA=None，Sponza 当前机位，光源角直径 7.1°，光程 10，First Level=0，最大阴影距离 150，10 个 clipmap，16 个深度层。空间及四帧短历史开启，每段预算 8。用户当前已经切换到 **4096／1024 页**；上一轮截图为 2048／256 页，不能把两项一起变化归因于分辨率。

在用户现有 Play Mode 内使用隐藏临时 Volume，仅覆盖分辨率、页预算；补充固定四射线实验时另关闭自适应。每个窗口预热 64 个主相机帧，记录 128 次 GPU 观察。2048、4096 的关键组合均包含返回方向窗口，8192／1024 与 16384／256 各一个窗口。没有在测量窗口做图像读回或逐帧文件输出。页计数和元数据在窗口结束后同步读取，下个窗口重新预热。

GPU Recorder 返回最后已完成的 GPU profiling frame；CSV 中的主相机帧号是观察时间标记，不是 GPU 帧身份。Resolve 与 Allocate 等主要 marker 每次观察的 sample count 均为 1，未混入多个相机的同名执行。结果是阶段成本，不是 GPU 整帧，嵌套 marker 不能相加。此轮与旧截图不同采集窗口，不能拼成同一帧总耗时。

计时、固定四射线实验都完整恢复序列化 Volume 值与 Override 标志、相机及变换；前后两个 Sponza 场景的 dirty 均为 false。诊断临时对象已移除，Play Mode 保持 true，runInBackground 恢复 false，实际设置回到 4096／1024、自适应 true。本轮没有启动 Unity Test Framework，也没有保存场景。

## 实测：物理预算充足时，Resolve 明显变贵

单位 ms；区间是两个窗口各自的中位数范围，不是 P95。精确 P50／P95、每次观察和 sample count 均在 results.json 与 CSV。

| 分辨率 | 页预算 | Resolve，自适应 2/4 | Resolve，固定 4 | Allocate，自适应 | 窗口末请求／溢出 |
|---|---:|---:|---:|---:|---:|
|2048|1024|6.49–6.54|6.98–7.02|0.271|266／0|
|4096|1024|14.04–14.18|18.02|0.569–0.595|641／0|
|8192|1024|27.44|34.83|1.638|1431／407|
|2048|256|6.25–6.28|未测|0.224–0.225|266／10|
|4096|256|5.35–5.64|未测|0.529–0.532|641／385|
|8192|256|1.41|未测|1.392–1.514|1431／1175|
|16384|256|1.82|未测|3.386|2825／2569|

1024 页的 2048→4096 对照，两端都无预算溢出，自适应 Resolve 约增加至 2.17 倍，固定四射线约增加至 2.57 倍。说明自适应不是退化根因：它对高分辨率的减负反而更明显。高分辨率下的细层驻留带来实际质量机会，也暴露更昂贵的求交工作。

相应 ResolveTrace 自适应约为 6.26–6.32、13.80–13.95、27.21 ms；两个滤波阶段合计仍约 0.22 ms。稳态 StaticRaster 约 0.007 ms，窗口末新增页均为 0。当前静态机位的分辨率退化不能主要归因于反复光栅重绘或降噪。

![分辨率与阶段耗时](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMResolutionScaling_20260913/resolution-scaling.png)

## 低页预算的快来自严重粗层回退

另用已有 VSMReceiverDebug 内核进行七组全屏诊断，均在采样相位 0、预热至少 64 帧后读取，计时完全独立。每个主/过渡估计固定四射线，不使用生产自适应；过渡混合或重试可使每像素总射线数超过四。各 mode 同帧执行。当前机位全部 2,073,600 个像素均为有效接收者。

| 配置 | 更粗层回退像素 | 每像素平均单元查询 | 每像素平均逻辑深度层读取 |
|---|---:|---:|---:|
|2048／1024|0.02%|61.52|396.76|
|4096／1024|0.01%|70.82|480.49|
|8192／1024|24.48%|63.87|429.70|
|2048／256|5.60%|59.07|381.24|
|4096／256|76.02%|47.82|316.83|
|8192／256|100%|11.85|44.34|
|16384／256|100%|18.07|75.50|

8192／256 和 16384／256 的实际采样层全部为 **最末 level 9**，对应 0.125 和 0.0625 世界单位／纹素。没有触发最终 PCF 回退；它们仍执行最粗层 SMRT。其低 Resolve 耗时不能代表保持细节后的性能。页表也证实这些配置的较细层请求大量未驻留；8192／256 甚至有中间续接层完全未驻留，完整 footprint 检查会进一步推向 terminal。

逻辑深度读取计数统计分层查询调用，不等同于硬件纹理事务、字节流量或两个池各自的实际 Load。已有空池标志仍可以省掉调用内部的物理读取。一次固定相位诊断也不替代跨帧几何参考；本文不以这些指标宣称阴影误差改善。

## 代码揭示的结构差距

### 1. 当前 SMRT 的实际工作量没有被每像素采样预算直接限定

[VSMSMRT.hlsl](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl:84) 逐个 DDA 纹素单元推进，每个单元可能进一步检查最多 16 层静态／动态深度；跨 clipmap 后重新获得段预算。最后一层还可以扩展预算以完成明确的世界光程。已有空池跳过减少深度访问，但没有跨多个纹素单元的保守区间跳过。

段末时间约为 `4.242 × 世界纹素尺寸 / tan(角半径)`（8 格预算），见 [VSMSMRTRayLength](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:1175)。同一个覆盖级的虚拟分辨率翻倍会使纹素减半，细层可推进的世界距离也减半，随后需要粗层续接。分辨率、几何精度、层次遍历因此互相耦合。

不过，**不能简单声称分辨率翻倍就必定让 DDA 读取翻倍**。本次 2048→4096／1024 的固定相位逻辑层读取只增加约 21%，固定四射线 GPU 耗时却增加约 157%；8192 的逻辑读取还低于 4096，但 GPU 更慢。这说明仅按循环次数解释性能是不完整的。更多驻留页、空间/层间访问局部性、线程执行分歧和缓存停顿是待验证因素。尚无 Nsight 的 L2 命中率、显存事务、寄存器占用或 stall 数据，不能把其中任意一项写成已证实的主因。

Unreal 官方把 Shadow Projection 的成本主要归于屏幕上采样的灯光数、每像素射线数和每射线样本数，并采用首层深度与遮挡后方的外推规则。我们的多层几何保留与逐格求交比该模型承担更多工作。它帮助修复过隐藏遮挡漏光；直接退回单层或截短光程会改变正确性保障。[Epic VSM 文档](https://dev.epicgames.com/documentation/unreal-engine/virtual-shadow-maps-in-unreal-engine)

### 2. 屏幕密度选层存在，但覆盖约束仍迫使很多像素选择较粗的覆盖级

[SelectVSMDensityLevel](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMReceiverQuality.hlsl:28) 已根据接收平面屏幕纹素大小计算 LOD。理想密度控制区，分辨率翻倍使 desired LOD 加一，世界纹素尺寸可以保持不变。因此“缺少屏幕密度 LOD”不是当前差距。

实际选择还受几何覆盖、bias 与 SMRT footprint 的硬约束。1024 页诊断中，2048 时约 **79.36%** 像素受到较粗覆盖级约束；4096 时仍有 **52.84%**。这部分像素不能简单靠 LOD 加一抵消分辨率提高，而会得到更细的世界纹素和不同的求交负担。以上百分比比较 minimum-covered level 与夹取后 desired LOD 的下整值，不是几何误差率。

UE 的 16K 首先是虚拟地址空间，按屏幕需要分配和绘制页面、选择 LOD。当前分辨率旋钮直接改变所有固定覆盖级的纹素和页面世界尺寸；需要把“地址容量”“实际请求密度”“SMRT 远段几何精度”做成成本清晰的独立约束，而不是把它视为普通纹理分辨率旋钮。[Epic VSM 说明](https://dev.epicgames.com/documentation/unreal-engine/virtual-shadow-maps-in-unreal-engine)

### 3. 页内容稀疏，页管理仍有密集扫描与串行分配

当前 10 级的虚拟页表条目数为 `10 × (R/128)²`：2048 为 2,560，4096 为 10,240，8192 为 40,960，16384 为 163,840。分辨率翻倍，完整虚拟表面积增加四倍，独立于物理页预算。

[VSMPrototypeAllocatePages](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:526) 只调度一个 64 线程组；先扫描整个虚拟表，再由线程 0 扫描位集、按四个优先级处理请求并查找物理槽。已有位集改善了无请求页的内层处理，但没有消除全表扫描和串行尾部。

固定 256 页从 2048→16384，Allocate **0.224→3.386 ms**，PageCull **0.049→1.040 ms**。后者的 [meshlet 页范围检查](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:373) 仍可能枚举较大的虚拟页矩形，以找到相关驻留页。这些是已测到的独立可扩展性问题，不应笼统归因于“分辨率使光栅像素变多”。

### 4. 预算溢出主要靠回退，没有压力驱动的质量目标

当前页优先级保护 terminal/parent，但屏幕纹素目标与 LOD bias 仍是固定输入。请求越来越多时，分配器丢掉部分需求，接收者随后在完整 footprint 检查中退层；较少量的续接层缺失也可能使可用主层无法完成软阴影估计。

UE 5.8 CVar 表列出 `DynamicRes.MaxPagePoolLoadFactor=0.85` 与 `DynamicRes.MaxResolutionLodBias=2`，用于在池接近容量时调整请求分辨率。平台或项目可覆盖这些值。本仓库下一步需要同时约束 primary/continuation 满足率与实际屏幕纹素误差，并让标页和 Resolve 使用同一生效目标。[Epic CVar 文档](https://dev.epicgames.com/documentation/unreal-engine/unreal-engine-console-variables-reference)

### 5. 切换分辨率还会触发不必要的物理池重建

[EnsureResources](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:2980) 将虚拟页表尺寸与物理池描述符一起比较；任意项改变后执行 ReleaseAllocatedResources，再创建两套物理池。固定 1024 页时，它们仍是相同的 2 GiB 存储，却随虚拟分辨率切换释放和重建。

这是切换卡顿和冷缓存的资源生命周期问题。本次稳态窗口已经排除预热，不能用它解释 14／27 ms 的长期 Trace 成本。可保留描述符相同的池，只更新页表资源、清理所有权并失效深度内容；保持分辨率切换后的正确失效仍然必要。

## 建议推进顺序

1. **先做 Trace 的硬件成本归因。** 固定 1024 页与四射线，比较 2048／4096 的相同机位，采集缓存、带宽、纹理事务、warp/wave 分歧、寄存器及 stall。两端均无溢出，可排除细层预算不足这个混淆因素。依据结果选择层数据布局、访问一致性或遍历结构，避免只优化 ALU。
2. **建立可保守跳过区间的层次求交对照。** 评估按页/块的占用或深度区间摘要，在保留隐藏遮挡的前提下跳过多个单元；近接触细几何继续精确检查。当前逐格读取与多层表示需有明确的每像素成本约束，不能靠缩短光程兑现性能。
3. **把页管理从完整虚拟空间工作转向活动需求工作。** 并行压紧请求、减少分配器单线程尾部、让大 meshlet 的页范围与活动页相交。单独记录 Allocate/PageCull，不与 Trace 收益混算。
4. **补池压力下的质量反馈，再扩展驻留预算。** 页预算从 256→1024 把双层池预分配从 512 MiB 提升到 2 GiB；缺页归零也会暴露昂贵的细层求交。质量目标应平滑变化，验收覆盖拱门漏光、细几何、运动和噪声。
5. **独立修复资源重建范围。** 用相同物理描述符重复切换分辨率验证池复用，另记冷态峰值；不影响稳态主瓶颈的优先级。

本轮是分析与诊断，没有采用新的求交、降分辨率或页预算策略。没有 Unreal 同场景同硬件计时，也没有审计 Unreal 私有 shader 实现。

## 复核材料

- `results.json`、`raw/*.csv`：自适应计时与页元数据；`fixed4/results.json`、`fixed4/raw/*.csv`：固定四射线计时。
- `diagnostics-results.json`：同相位的全屏诊断聚合；`diagnostics/*.txt`：相位与尺寸，`diagnostics/raw-manifest.json`：原始 gzip 的 SHA256。
- 大体积全屏诊断放在忽略目录 `E:/VividRP_Reborn/Packages/VividRP/Temp~/vsm-resolution-scaling-20260913/diagnostic-raw`，未加入生产代码或版本管理目录。若临时数据已清理，可用所附 CLI 脚本重新采集。
- `sweep.cs.txt`、`fixed4-sweep.cs.txt`、`diagnostics.cs.txt` 是 CLI eval_file 的语句体；执行前复制到临时 `.cs` 文件，不要导入 Unity 编译目录。它们会短暂改变内存中的生效配置，完成后恢复，不适合在操纵相机或切换 Play Mode 时运行。

离线复算：

```powershell
python Roadmap~/Experiments/VSMResolutionScaling_20260913/analyze.py
python Roadmap~/Experiments/VSMResolutionScaling_20260913/analyze.py Roadmap~/Experiments/VSMResolutionScaling_20260913/fixed4
python Roadmap~/Experiments/VSMResolutionScaling_20260913/analyze-diagnostics.py Temp~/vsm-resolution-scaling-20260913/diagnostic-raw
python Roadmap~/Experiments/VSMResolutionScaling_20260913/plot.py
```
