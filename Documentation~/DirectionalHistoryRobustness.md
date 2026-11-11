# Cornell Box：逐方向时域历史鲁棒性重构

2026-11-11，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti。原生 Screen Probe Gather 保持 **16 px 网格、每探针 8 条射线**。最终静止结果：有效 Age 均值 **1.547 → 4.821**，校准前 SH 法线方向时间方差下降 **64.88%**，达到本轮两个目标。

最终天花板像素 RMS 仍约 1.635；方向重构改善没有直接变成同等幅度的最终平面降噪，scalar anchor 校准仍限制该项结果。以下区分方向场、校准和最终像素。

## 与本机 Unreal 的对照

检查本机 UE **5.7.4** 的 `Engine/Shaders/Private/Lumen/LumenScreenProbeFiltering.usf`：

- `ScreenProbeTemporallyAccumulateTraceRadianceCS`（约 201 行）在八面体 texel 上重投影历史，以接收面的相对平面距离验证邻探针；此版本代码仍有将空间角度权重用于时域的 TODO。
- `GatherNeighborRadiance`（约 299 行）实现命中角度空间权重，并夹住邻居命中距离，保护近距离接触。

因此本轮对齐“方向场历史、接收面验证、角度 / 距离加权”的机制；下面的分级规则是针对 VividRP 的 4×4 方向 bin 与稀疏射线实现，不能描述为 UE 源码的逐行移植。

## 实现

### 分级的命中置信度

`ProbeTraceHitWeight` 的现有空间遮挡权重保留。新增时域专用 `ProbeTemporalHitWeight`：

- 无效命中、天空 / 表面类别变化继续拒绝；明确追踪过但未解析的 bin 不继承历史。
- 近距离按角度与端点范围差连续衰减，距离项使用指数权重，保留接触保护。
- 两端距离都接近或超过 5 m 时逐渐转向相对范围容差，保留远景方向能量。
- 大平面不只按距离判断：验证命中实例一致、法线一致以及双向平面误差。这样小房间内的地板也能跨射线抖动复用。共面容差含有有限的量化 / SDF 误差余量；小于约 0.5 m 的近接触不使用此层放宽。
- 对场景 / 光照历史仍有效、接收点位移几乎为零的情况，把同一个粗 bin 中的不同射线视为静止锥积分的样本。该层在毫米级位移上快速衰减，1–2 m 的命中范围内逐渐启用，置信度上限 0.90；空间借用不使用此静止历史层。

最后一项是必要的区别：固定场景内，射线在同一粗方向 bin 中交替命中不同平面，不一定表示真实的新露出区域。实测仅保留“同一命中实例”时该组平均 Age 已为 4.28，跨实例组约 1.92；继续把所有此类变化视为遮挡变化会压低静止锥积分的有效历史。

### 命中面元数据与精度

原生 trace / composite / temporal atlas 从两层改为三层：radiance、命中距离 / 方向 / 历史元数据、命中法线 / 实例身份。C# `DirectionalTracePlanes=3` 与 HLSL 的 metadata offset 同步，CameraHistory 描述符变化会使旧布局失效。

第三层使用 RGBA16F：XY 为八面体法线；ZW 通过两个有限 half payload 精确保存 26 位实例索引，避免直接以 half 数值存索引时在 2048 之后合并身份。未知身份或索引 ≥2²⁶ 时不启用身份共面层，保留保守范围层；场景 revision 变化沿用现有历史失效规则。

合成 bin 时检查多条当前光线的面一致性，混合了不同实例 / 不同平面的 bin 不声明共面证据。历史选择命中信息时保留对应身份，不对身份进行加权插值。GPU 回读覆盖 0、2047、8191、8192、65535、4194304、2²⁶−1 和越界等 10 组，精确匹配预期。

### 分数有效 Age 与 Stale

历史置信度实际参与累积：`acceptedAge = oldAge × confidence`，历史 blend 为 `acceptedAge / (1 + acceptedAge)`。有效 Age 以 1/32 的精度存储，Stale 独立打包。这样低于 1 的连续权重不会在每一帧被整数 Age 的 round 提前截断。

**Age 上限仍为 8，Stale 失效仍为 8 个无新证据帧**；没有增加上限来满足指标。两个字段编码为有限且可精确表示的 half payload；10 组 GPU 编码 / 回读测试通过。对旧格式或非法编码范围按无历史处理。

### 未采样方向的各向异性借用

仅当前帧 draw mass 为 0 时检查 8 个相邻探针的同一世界方向：

- donor 必须来自**当前 composite 的新鲜解析光线**，不使用其他历史或借用结果刷新 Stale。
- 接收面深度 / 法线、命中类别、范围 / 平面一致性和相对射线的横向偏移共同加权。
- 至少两个 donor、足够总权重才接纳；已有历史仅混入受限的新鲜证据并刷新 Stale，不增加其时间样本 Age。
- 没有中心证据时，只允许受限、相互一致的平面 / 远景支持，coverage 上限为 0.25，初始 Age 为 1。

本轮最终方向抽样记录中，**46,884** 个明确未解析的 bin 全部没有继承历史。

## 实验条件与阶段对照

Cornell Bright 原机位，1920×1080、定向光 3600 lux、固定 EV100=8。Radiosity 固定 **8 阶、4096 射线 / 卡片 texel、seed 0**，采样累积实验选项关闭；测量阶段卡片更新数为 0。各组有效卡片首尾及组间逐位一致。

启用空间 anchor、Adaptive 5×5、两轮 À-trous，像素历史上限 16，anchor blend 0.85。关闭采样引导和 Far Radiance Cache 以统一工作负载。每组静止 512 帧；168 个天花板探针和既有 96×48 天花板像素区域。

隔离过程的数据均保留，没有用不达标组代替最终结果：

| 阶段 | Age 均值（完整借用组） | 校准前 SH 时间方差下降 |
|---|---:|---:|
| 仅近 / 远范围连续衰减 + 借用 | 1.711 | 12.72% |
| 增加命中法线 / 共面验证 | 3.211 | 42.39% |
| 增加精确实例身份 | 3.407 | 对应记录见 identity-tier |
| 保存分数有效 Age | 约 3.42 | 对应记录见 fractional |
| 增加受限静止锥积分复用 | **4.821** | **64.88%** |

首轮发现当前区域绝大多数有效命中在 5 m 内，单独放宽远景不能解决小房间大平面历史。分数 Age 修复了累积表示，但单独带来的平均 Age 改善很小；主要收益来自区分静止锥内的采样变化和真实接收点变化。

## 最终三组结果

旧路径通过 Editor-only bit 1024 恢复；bit 2048 禁用新鲜邻居借用。正常渲染 flags=0 使用完整重构。两组新路径都维持 8 次有效样本上限。

| 指标 | 旧路径 | 连续时域、不借用 | 完整重构 |
|---|---:|---:|---:|
| 有效 Age 均值 | 1.5466 | 4.8173 | **4.8215** |
| 有效方向支持率 | 67.56% | 69.64% | **73.29%** |
| 未做空间滤波的 SH 方差 | 258.5257 | 约 97.16 | **74.0160** |
| 空间滤波后、校准前 SH 方差 | 144.8639 | 59.4492 | **50.8730** |
| 校准前 26 方向场方差 | 51.4373 | 21.1937 | **18.2527** |
| 校准后 26 方向场方差 | 78.9084 | 37.0585 | **32.5180** |
| 校准后法线方向平均 Y | 55.45995 | 55.46212 | **55.46009** |
| 最终像素时间 RMS | 1.63470 | 1.63449 | **1.63476** |

完整组 Age 的 P5 / P50 / P95 为 **1 / 5.09375 / 8**，当前有 draw 的方向平均 5.010，无 draw 的方向平均 4.671。

SH 方差统计先沿时间减去各探针均值，再平均方差，使用相同 scalar trace coverage；26 方向指标在固定球面方向上计算未截断 SH 场，避免只看一个接收法线。校准前法线方差下降 **64.88%**，配对 32 帧块 bootstrap 的条件 95% 区间为 **64.25–65.52%**；这描述该固定序列的块离散程度，不是跨场景或跨 seed 保证。校准前 26 方向场下降 **64.51%**，校准后 26 方向场也下降约 **58.79%**。

校准前平均 Y 从 58.9013 变为 55.2957（−6.12%），不能只用未归一化方差声称全部改善均与增益无关。将两组均值归一到同一增益后，校准前方差仍下降 **60.15%**。校准后的平均能量几乎一致，最终像素均值差 −0.0055%。

**最终平面噪声未明显下降。** 校准把 probe 法线的能量重新约束到原有 scalar anchor，所以该方向方差仍约 20.99，而非跟随方向场一同下降。后续应单独推进 scalar anchor 的时空收敛与反应速度，并继续以高样本均值限制能量漂移；本轮没有同时改动该校准目标。

## 固定轨迹、板边与新露出区域

沿用 128 帧固定轨迹：水平位移 0.28 m、前后位移 0.06 m、yaw −7°，按正弦往返。两组 VP 与接收几何逐位相同。移动实验临时固定太阳为相同恒定输入（关闭 interactsWithSky），避免大气透射的微小变化重置源缓存；结束后恢复。

- 12,570 个明确新露出像素、涉及 114 帧：旧 / 新都没有 age>1 的旧像素历史继承，Temporal 相对当前 Resolve 的误差 P99 / max 均为 0。
- 在深度 / 法线断裂附近取 3 px 范围，共 625,416 个有效样本。完整组相对旧路径的亮度差绝对 P99 为 **0.1291%**，合计亮度变化 **+0.000166%**。
- 归一 RGB 色度差 P99 为 **0.000389**；对照图在相同曝光和显示变换下没有显示明显新增板边光晕。

这些是同轨迹 A/B 差异和**像素层历史拒绝**验证，不是高样本动态 GI 真值误差，也不能证明所有探针层拖影或遮挡偏差已消失。未验证 Sponza、天空边界、动态几何或采样引导开启时的质量。

## 存储、检查与恢复

三层 metadata 增加存储。1920×1080、Adaptive 开启时，ray / composite / 双缓冲 temporal 的描述符预算合计增加约 **6.97 MiB**；这不是驱动实测显存，也不包含额外诊断输出。未增加 SDF 光线数。GPU 毫秒开销尚未测量。

- 31 个相关入口的 DXIL / SPIR-V 编译通过；实际 D3D12 静止和移动捕获通过，错误记录为空。
- Unity shader 检视无 Error；有 D3D11 的临时寄存器推荐值、可能未初始化变量和局部变量命名遮蔽 Warning，保留在 editor-verification.json。未据此声称性能无回退。
- 三个新布局纹理的稳定描述符更新预热后重复 4096 次，0 B 托管分配；不是全帧分配测量。
- 原 Profile、相机、灯光、诊断回调及 flags 已恢复，没有编辑场景 / Profile 资产。上一轮 Radiosity 累积实验默认仍关闭。
- 未运行 Unity Test Framework：Editor 正在交互运行。

## 复现与产物

本机证据：`E:/VividRP_Reborn/Logs/DirectionalHistory-20261111/`。

- `final/`：最终三组、每组 512 帧。`initial/`、`plane/`、`identity-tier/`、`fractional/` 保存各阶段对照；相应 `*-source/` 保存中间实现。
- `analysis/summary.json`、`metrics.csv`、`directional-history-quality.png`：完整指标、方差区间及校准前后曲线。
- `motion/motion-summary.json`、`boundary-summary.json`、`panel-edge-comparison.png`：固定轨迹历史拒绝与板边差异。
- `identity/`、`age-packing/`：GPU half 精确编码 / 解码控制。
- `final-compile/`、`editor-verification.json`、`scripts/`、`source/`：编译、恢复与实际脚本 / 源码快照。

```powershell
unity command script eval --project-path E:/VividRP_Reborn `
  --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/DirectionalHistoryCapture.cs `
  --timeout 1200 --format json
python Tools~/Diagnostics/DirectionalHistoryExperiment.py `
  E:/VividRP_Reborn/Logs/DirectionalHistory-20261111 `
  E:/VividRP_Reborn/Logs/DirectionalHistory-20261111/analysis
python Tools~/Diagnostics/GatherHistoryMotion.py `
  E:/VividRP_Reborn/Logs/DirectionalHistory-20261111/motion
python Tools~/Diagnostics/DirectionalHistoryMotion.py `
  E:/VividRP_Reborn/Logs/DirectionalHistory-20261111/motion
```
