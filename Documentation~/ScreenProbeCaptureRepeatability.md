# Screen Probe 同代码重复性与光片候选复测

> 2026-11-17 状态更新：按用户要求，五个生产 shader 的精度改动已按原始备份逐字节撤销，相关 Quadrature 生成器兼容改动也已撤销。下文记录的是此前精度实验及移植时的结果，不代表当前生产源码的 A/A 已通过。诊断原型与原始采集证据保留；撤销后的分析见 [实现对齐与固定斑块](ScreenProbeImplementationAlignment.md)。

2026-11-16。证据根目录：`E:/VividRP_GI_Evidence/ScreenProbeRepeatabilityFix-20261116`。

任务先解决同代码重复采集的首次分歧，再复测光片边缘候选；最终均值图的空间误差与能量误差仍是画质门槛。A/A 一致本身不是降噪收益。

## 固定条件与判定

- Cornell Emissive Ceiling，1920×1080，序列 1733，冻结源 hash `40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。
- 16 px / 8 rays，4 cm bias，192 SDF steps，anchor 512 / pixel 32 / 四轮 wavelet；源 8 orders / 64 samples / 32 batches，seed 0。
- 长序列重置历史后预热 1024 帧，再测 128 帧。原生与私有 Resolve、时域历史、moments、geometry、final 必须逐字节一致；跨运行也必须一致。
- 用 `RGB × coverage` 的线性消费量统计最终图。区域平均能量、均值图 MSE、非均匀残差分别统计，不能用低时间方差覆盖空间误差。
- 比较前检查实际脚本、输入文件、2783 份生产/夹具源码快照、已保存载荷 SHA、格式、帧覆盖、采样配置。失败原始采集保留，不修改协议或放宽阈值使其通过。

## 首次分歧的证据

原版 `full-a/b` 的 17461 对记录中有 14062 对不同，最早**观察**到的是 frame 2。该序列在 3…1022 不读回主要纹理，因此后续稀疏检查点不是实际分歧发生时刻。

冻结 Resolve 重放先修正了诊断工具的纹理绑定、GPU 解码法线和实时 repair mode；`frozen-resolve-i` 连续 32 次与原生结果逐字节相同。这些是重放工具修复，不是生产 GI 修复。

`witness-0/1` 在 frame 2 的已捕获几何、当前方向、上一帧 anchor/geometry/statistics 和 48 个实际 GPU 常量字都相同，但 PreparedAnchor 不同。GPU 常量 witness 只占用经检查为无效几何的 12 个槽位，不能写入有效接收面。坐标与 anchor 精度约束消除了这组观测差异；Resolve、pixel temporal 的后续分歧需要继续约束相应运算。

逐帧 CPU 读回的 `dense1024-a/b` 有 21801 对记录完全相同，但同代码原稀疏节奏仍失败。因此增加读回不能当作修复。

新增 GPU 整数指纹账本，每帧记录分阶段结果，末尾一次读回，避免逐帧 CPU 读回的同步影响。指纹只用于定位，不能替代完整字节比较：

- `ledger-a/b` 在 frame 90 首次出现几何与后续结果差异。
- 补充输入的短序列出现 frame 16 ScreenRays 差异；补充屏幕历史后的短序列又通过。
- `ledger-screen-long-a/b` 在 frame 15 首次出现几何差异。发生时已记录的深度、GBuffer、motion、屏幕历史、各级 HZB 指纹及逐帧 CPU 矩阵相同。没有其他相机插入。

这支持继续检查世界坐标重建的浮点不变性，但**没有证明某条驱动指令或驱动缺陷**。CPU 矩阵不是实际 GPU 常量的完整替代；上述 GPU witness 也只覆盖它声明的 48 个字。

## 精度修复与长序列控制

在内存中依次约束历史坐标、anchor、Resolve、pixel temporal、filter，最后在 Trace 的 `LoadReceiver` 中明确 clip-space / inverse VP / homogeneous divide 的精度。单独的早期精度原型没有通过长序列，不作为最终修复。

完整原型的控制结果：

| 控制 | 检查点 | 跨运行结果 | 最终 128 帧均值 |
| --- | ---: | --- | --- |
| GPU 逐帧指纹，`receiver-long-a-v2/b` | 17461 对完整记录，另有 1152 帧指纹 | 全部相同 | 四个 ROI 的 RMS、平均能量差均为 0 |
| 原稀疏读回，`receiver-sparse-a/b` | 17461 对 | 全部相同 | 四个 ROI 的 RMS、平均能量差均为 0 |

两个协议的最终区域均值也相同。短序列成功、密集读回成功、单帧重放成功均没有被单独当作这个结论。

原版 A/A 自身的有限窗口均值图仍有明显空间差异：ceiling / floor / back / edge 的 RMS 分别为 0.422% / 1.578% / 1.076% / 1.169%；平均能量差分别为 +0.124% / −0.579% / −0.071% / +0.366%。它们是**同代码运行之间的差异**，不是相对物理参考的能量误差。`aa-final-mean-map.png` 展示这种差异的空间形状。

生产移植仅包含精度限定与 Trace 世界坐标重建，未改变公式的采样权重、历史门槛、卡片查询或资源布局。Common 的额外 filter 精度限定由该 compute 的宏控制，避免改变其他调用者的 Common 运算。持久化资产直接执行的验证结果见下方交接记录。

## 光片候选的可信复测

`card-candidate-a/b` 使用相同精度设置与原采样预算。基线及候选各 17461 对 A/A 全部相同；跨 A/B 的 640 对几何、深度、GBuffer 检查也全部通过。

独立参考沿用 `ScreenProbeCoveredWavelet-20261116-144800/reference-ceiling-128k`：2336 个匹配接收点，天花板 128k、其余 16k 的 Lambert 参考，包含参考协方差。它不是 OpenPBR 真值，也不是 Lumen 对等比较。

| ROI | 平均能量误差：基线 → 候选 | 均值图 MSE 变化 | 非均匀误差 RMS：基线 → 候选 |
| --- | ---: | ---: | ---: |
| ceiling | −0.579% → −0.579% | 0 | 1.428% → 1.428% |
| floor | +2.440% → +2.440% | 0 | 4.193% → 4.193% |
| back | −0.918% → −1.074% | −3.290% | 7.061% → 6.920% |
| edge | +0.569% → +0.644% | +1.599% | 5.437% → 5.473% |

边缘的时间方差下降 2.476%，但均值图 MSE 变差，超过原先增加不超过 1% 的门槛；保留参考不确定性的完整 RGB 判定同样拒绝。背墙时间方差增加 20.576%。按物体拆分的能量也保留，避免混合 ROI 相互抵消。

**光片边缘候选不合入生产。** 天花板和地面的最终样本完全不变，说明该窄范围覆盖补救没有解决这两个区域的云状固定斑块。它只修复一部分原来被拒绝的薄片命中，仍未处理此前已经接受的膨胀轮廓假命中；详见 [卡片覆盖诊断](ScreenProbeCardCoverage.md)。

报告：`card-quality-qualified.json/png`。`qualified=true` 表示输入和重复性满足比较条件；`spatialAndEnergyPassed=false`、`productionQualified=false` 表示候选未通过推广。

## 工具与验证边界

- `BuildScreenProbeColdReplay.py`：冻结输入、实际常量 witness、逐项精度原型与采集协议；只在内存替换 shader，finally 恢复。
- `BuildScreenProbeGpuFingerprint.py` / `ScreenProbeGpuFingerprint.compute.txt`：有界整数 GPU 账本；完整 payload 留给指定探针帧。早期版本跳过前两帧 previous 纹理，不能把账本表述成这些输入在该区间的证明。
- `AnalyzeScreenProbeColdReplay.py`：保留精确 A/A 门槛，同时输出最终均值图差异；不从 A/A 推断画质通过。
- `AnalyzeScreenProbeCardQuality.py`：严格模式现在必须提供基线和候选的 A/A 重复，才允许进入空间/能量验收。
- `BuildScreenProbeCardQuality.py --production`：直接使用持久化生产 Trace/Gather/Filter/Temporal，重放仍独立检查；取消内存原型替换。
- `ScreenProbeFrozenResolve.cs.txt` / `ScreenProbeFrozenAnchor.cs.txt`：离线输入的 live GPU 重放。失败的绑定/格式尝试均保留且未参与验收。

所有结果限于该固定 Cornell 场景、序列与 128 帧窗口，不证明无限收敛偏差已消除。未执行活动 Editor 中的 Unity Test Framework；未进行移动场景或 GPU 性能验收。Nsight 附加候选列表为空，没有用未知的捕获状态解释浮点差异。

## 持久化生产路径与恢复

`production-a-v2/b` 直接执行持久化生产 shader，未安装 Trace/Gather/Filter/Temporal 原型。每次实际执行 1024+128 帧，132 对 Resolve 与 3300 对独立私有历史检查全部通过；跨运行 **17461 对记录零差异**，四个 ROI 的最终均值图 RMS 与平均能量差均为 **0**。

正式路径与已接受的原型也在全部 17461 对共同记录上完全一致，见 `production-equivalence.json`。因此上面的光片候选复测使用的数值基线与当前正式代码输出一致。编译记录 `production-programs.json` 包含四个 compute 的 **34 个 D3D12 kernel**，全部成功且没有 shader messages。这个结果不等于 GPU 性能验收。

最终 diff 的逻辑检查确认 Gather/Temporal/Filtering 只添加精度限定（Filtering 另加 Common 的精度开关）；Trace 另启用明确的 receiver 重建。没有 C# 热路径、GPU 数据布局、采样数或时域长度改动。诊断生成器同步支持带 `precise` 的源码，`git diff --check` 与 7 个相关 Python 文件语法检查通过。

正式代码变更后还生成了兼容性 smoke，实际编译 Trace、anchor、Resolve、temporal、GPU fingerprint 的五个诊断入口，全部通过。该 smoke 只验证生成/编译，不被计作新的场景 A/A；没有重新进入 PlayMode。记录为 `compile-diagnostic-smoke-v2-cli.json`。

已恢复开始时的 **EditMode / 未暂停**，两个原有场景均干净；四个回调为空，frame / atrous override 为 −1，diagnostics flags 为 0。证据为 `restoration-state.json`。未运行 Unity Test Framework（活动 Editor 会话期间禁止主动启动），没有清空用户 Console。

本轮证据约 11.98 GiB，审查时 E 盘约 89.73 GiB 可用；没有删除既有证据，没有创建目录链接。一次扩展输入的长采集因预计超过 384 MiB 单次上限主动取消，改为指纹长序列；失败/取消记录均保留，未参与通过判定。工具所用的 shader/runtime 原型与正式改动前源码备份都保留在证据目录。
