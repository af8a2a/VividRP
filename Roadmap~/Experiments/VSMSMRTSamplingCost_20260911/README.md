# SMRT 跨层采样成本与粗层误差门限 · 2026-09-11

保留一个最小的等价优化：DDA 在同一虚拟页内复用已验证的物理地址偏移，跨页时重新解析映射。50 m Resolve GPU 标记中位数由本轮基线约 **4.58 ms 降至 2.86 ms（降低 37.7%）**；10 m 由 **3.83 ms 降至 1.83 ms（降低 52.2%）**。

光程、跨层结束距离、射线数、实际深度采样单元、slab 厚度和 gap 规则均保持原样。粗层细几何误差在本轮 448 组几何基准中没有新增；不把这解释为消除了既有的细杆漏遮挡。

## 保留的实现

`Shaders/Core/Private/VSMSMRT.hlsl` 的单段 DDA 保存当前虚拟页的 `[pageLow, pageHigh)` 和 `physicalOffset`。

1. 初始缓存为空，第一步调用完整的 `TryResolveVSMPhysicalTexel`。
2. 同页后续单元直接使用 `cell + physicalOffset`，仍逐单元读取静态/动态深度并执行原求交规则。
3. 正向、反向或对角跨页均重新验证新页。未映射、脏页、元数据不匹配仍返回不可用。
4. 每条射线、每个 clipmap 段重新初始化缓存。映射只在当前 resolve dispatch 内复用，依赖既有分配/光栅化/resolve 的资源同步，不跨帧缓存。

运行时仅改动该 HLSL，未新增托管热路径或配置参数。新增 SamplingTests 覆盖乱序物理页正反向跨越，以及下一页三类失效状态；测试代码属于冷路径。

## 性能对照与被否决的候选

RTX 5070 Ti，D3D12，Unity 6000.7.0a6；同一 Sponza 相机，1920×1080，4096 虚拟分辨率，1024 页，SMRT 4×8，7.1° 光源。每阶段预热 4 秒、记录 4 秒；各版本按 10/50/50/10 m 顺序执行，计时没有截图读回或并发 shader 编译。

| 版本 | 10 m 两轮中位数 | 50 m 两轮中位数 | 决定 |
|---|---:|---:|---|
| 本轮基线，完整跨层光程 | 3.859 / 3.799 ms | 4.607 / 4.556 ms | 对照 |
| **仅物理页复用** | **1.843 / 1.817 ms** | **2.841 / 2.871 ms** | **保留** |
| 页复用 + 省略重复元数据检查 | 2.411 / 2.411 ms | 2.864 / 2.763 ms | 否决 |

第三个候选利用完整 footprint 已检查驻留状态这一条件，减少每条射线的重复元数据读取。它通过了 559 万条 GPU 对照，但 10 m 比页复用单独方案变慢约 32%，50 m 改善很小，因此已完整撤回。归档源码为 `VSMSMRT.hlsl.rejected-prevalidation.txt`，不参与 Unity 编译。

性能取自既有 Unity GPU 标记 harness，**不是经认证归属到单相机的整帧时间**。各 run 保留 `completed_with_warnings` / `comparable=false` 及全部原始样本，未据此推导 FPS、总 GPU 时间或声称硬件瓶颈已被定位。见 [timing.csv](timing.csv) 和 [timing](timing/)。Nsight CLI 环境预检查通过，本轮收益数据来自上述 Unity 标记，没有使用 Nsight Trace 数据。

深度读取单元数没有下降；收益来自减少寻址和重复校验。当前结果仍明显高于此前会截短光程的实现，不能将后者的耗时作为等质量基线。

## 几何门限

沿用 [上一轮独立几何基准](../VSMSMRTContinuation_20260911/README.md)：14 个解析场景，多个分辨率、光源角度、4/8 预算、滚动和深度尺度，包含受限终端层。

- 最终 GPU 对照 **448 组 / 5,591,040 条射线**，可见性不一致 **0**，深度读取次数不一致 **0**，驻留支持集异常 **0**。
- 本轮 GPU 探针将每层拆成 2×2 页，并反转全部物理槽位。虚拟相邻单元不能依赖物理相邻性，覆盖正反方向和跨层的页复用。
- 生产门限保持逐射线等价，比只比较平均阴影误差更严格。因此该基准中各场景的误遮挡/漏遮挡计数与原跨层实现相同。
- 例如原常规 50 m 组合中，细杆漏遮挡仍约 **1.018%**；该表示误差没有被解决或隐藏。完整数据见 [metrics.csv](metrics.csv)。

另外独立评估了将粗层 slab 厚度限制为起始层厚度的 1×/2×/4×。固定 7.1°、50 m、常规层数，共 112 组 / 1,397,760 条射线/候选。门限要求每组的误遮挡与漏遮挡计数都不增加，不能用总体平均改善掩盖个别细几何场景退步。

| 厚度上限 | 误遮挡总数，基线 → 候选 | 漏遮挡总数，基线 → 候选 | 退步组数 |
|---|---:|---:|---:|
| 1× 起始层厚度 | 9438 → 9189 | 10071 → 10354 | 17 |
| 2× 起始层厚度 | 9438 → 9315 | 10071 → 10112 | 5 |
| 4× 起始层厚度 | 9438 → 9354 | 10071 → 10080 | 3 |

三个候选均未采用。见 [thickness-candidates.json](thickness-candidates.json)、[thickness-candidates.csv](thickness-candidates.csv) 与可复现的 `quality_gate.py`。下一步要降低粗层细几何误差，需要针对信息丢失建立更保守的表示或受控细层验证，仅缩小厚度没有通过本轮门限。

## 实际场景质量

最终版本重新捕获静止 32 帧、平移 96 帧、转向 128 帧；动态场景每四帧及末帧采集 GPU 快照，共 **90 份**。对照为上一轮最终完整光程捕获 `20260911_141314_738`；仅采样 HLSL 变化，其他 shader 源哈希一致。相机姿态、投影、抖动和 256 帧随机序列相位逐帧一致。

- 90 份页审计无页表/元数据/物理归属错误、预算溢出、最终不可用或整条 PCF 回退。
- 90 份配对快照的选择层级和混合权重完全一致。
- 阴影原始读回为 **700×520 ROI**，不是整个 1920×1080 输出；合计 **32,760,000 个像素观测**。
- ROI 仅 1 次差异超过 0.001，最大 0.25，发生于静止序列；平移与转向最大差异均为 0.00048828125。三组平均绝对误差分别为 2.33e-8、1.33e-9、1.01e-9。

真实场景没有做到逐位一致。单次 0.25 差异的原因未单独定位，不能直接当作几何精度改善；前一轮已记录生产输出与诊断重放存在少量单射线差异。本轮保留该限制，独立解析几何上的 0 差异是另一项验证。参见 [shadow-comparison.json](shadow-comparison.json)、[scene-summary.json](scene-summary.json)、`scene-after/page-audit.json`。

## 编译、恢复与复现

最终 DXC **41/41 内核通过**，Runtime、Editor、Editor.Tests **三个 Roslyn 编译通过**。Editor 保持打开，按仓库规则没有启动 Unity Test Framework；请手动运行 `VirtualShadowMapSamplingTests`，尤其新增的 `SMRT_PageReuse*` 用例。

相机、灯光、Volume、播放和运行状态与本轮开始时一致。临时诊断以 `.txt` 归档，核对归档后移除源码及自动生成的元数据；未改写 `.meta` 内容。见 `validation/`。

在包根目录中复现：

```powershell
# 生成独立几何输入；会清除 Temp 下上一次 GPU 输出
python Roadmap~/Experiments/VSMSMRTContinuation_20260911/path_geometry.py prepare
# 安装本轮的乱序物理页探针，随后 Unity Refresh
python Roadmap~/Experiments/VSMSMRTSamplingCost_20260911/probe.py install
# 在 Edit Mode 运行菜单 Tools/VividRP/Diagnostics/Run SMRT Path Reference
python Roadmap~/Experiments/VSMSMRTContinuation_20260911/path_geometry.py verify
# 评估粗层厚度候选，不改变生产 shader
python Roadmap~/Experiments/VSMSMRTSamplingCost_20260911/quality_gate.py
```

`scene_summary.py` 针对归档标识的两次捕获分析；原始二进制在 `Temp~/vsm-general-captures` 和 `Temp~/smrt-path`。计时 harness 的配置来源与全部原始计时样本已保存，本次未修改实际场景资产或默认渲染参数。
