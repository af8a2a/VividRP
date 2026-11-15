# ThinShell 壳内起点的黑色覆盖修复

修复 `SdfScreenProbeTracing.compute` 把壳内起点的出口误当成无效几何背面的问题。保留有效黑命中后，Cornell 顶部光片附近的探针不再反复丢失覆盖率和 scalar anchor 历史。**这是一个局部覆盖语义修复；没有完成整幅画面、移动质量或 Lumen 精度验收。**

证据根目录：`E:/VividRP_GI_Evidence/ScreenProbeResidualMap-20261115-104052-278383`。实际 D3D12 / RTX 5070 Ti，Unity 7000.0.0a16；原生 Screen Probe Gather，16 px / 每探针 8 条射线，已保存的 age512 / FP32 anchor 与 4-pass À-trous。Surface Cache 固定为 8 阶、每批 64 样本、32 批，输入 SHA-256 为 `40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。

## 原因和边界

天花板 Y=3，单面发光光片 Y=2.965，默认 4 cm bias 将接收起点移到 Y=2.96，落入光片的 ThinShell。SDF 出口梯度与出射方向同向，但材质背面吸收是有效黑色遮挡；原代码在恢复卡片材质法线前直接丢弃该样本。

仅当几何背面命中满足下列条件时，继续执行原来的卡片查找与材质单面判定：有效材质、`signMode == 2`、原始射线起点在该实例有效网格内、该起点的有符号 Mesh SDF 不大于零。其他背面保持拒绝。网格外 clamp 值不参与壳内判定；不改变全局 bias、SDF 厚度、发光增益、采样预算或历史拒绝。

先前宽泛接受所有 ThinShell 出口的候选未保留；它还接受了少量从壳外开始的退出命中。窄化候选只处理实测的壳内起点。

## 首命中和能量

`hit-diagnostics-v2` / `inside-shell-hit` 各读取 40 个探针、32 帧、10,240 条光线；每组载荷 765,952 字节。比较结果在 `inside-shell-hit-comparison.json`：

| 指标 | 原路径 | 修复后 |
|---|---:|---:|
| 壳内 ThinShell 几何背面被丢弃 | 6,731 | 0 |
| 零覆盖探针帧 / 1,280 | 529 | 0 |
| 接受的 ThinShell 材质背面返回正辐射度 | 0 | 0 |
| anchor age 中位数 | 1 | 512 |

首命中两组没有强制相同随机序列；计数是各自真实 GPU 观测，不是逐射线配对。`reference` / `inside-shell-reference` 则采用相同的两条独立均匀序列：每条 1,024 帧、每帧 8 根光线，共 16,384 条参考射线 / 探针。32 个 64 帧块以 CPU 双精度保存和与平方和，前 64 帧与末帧保留原始读回；每组载荷约 37.70 MiB。前两个块的聚合通过独立 Python 重算。

比较条件 RGB × coverage 的消耗能量：左墙、右墙、背墙和地面的平均 Y 变化为 0，天花板为 **+0.000869%**。前 128 个保存的参考帧中，99.99885% 的消耗 RGB 完全相同，Y 差 RMS 为 `1.34e-6`、最大 `8.75e-4`；剩余差异来自条件颜色与覆盖率的半精度表示。原始平均能量没有通过额外增益校正。参考共享 Surface Cache 与追踪实现，不能评估缓存来源误差或绝对 Unreal 精度。

## 静态质量与移动限制

最终质量组为 `trace-input-before` / `trace-input-after`，每组两次静止、两次固定移动；每个 arm 预热 1,024 帧，测量 128 帧。固定轨迹周期 128，横移 0.28 m、前移 0.06 m、偏航 −7°。最终报告是 **`trace-input-quality-analysis-v3.json`**。

静态两次的实际深度、GBuffer0/1、自适应布点、逆 VP、派生探针几何、像素几何和冻结光源完全匹配。以真实几何选出光片附近 11,098 个天花板像素：

| 指标 | 第一次 | 第二次 |
|---|---:|---:|
| Resolve 时域方差下降 | 85.44% | 85.36% |
| 最终像素时域方差下降 | 99.99055% | 99.99003% |
| 最终平均 Y 变化 | −13.92% | −13.82% |

变暗集中在此前覆盖率失效的位置。在 28 个实际探针中心，当前帧高采样目标平均 Y 为 0.25265；最终像素由约 1.93 / 1.97 降至 0.8741 / 0.8745，参考 RMS 误差由 2.160 / 2.186 降至 0.639 左右。**仍有约 +0.622 的重建正偏差**，不能把局部方差收敛等同于能量完全正确。数值和固定色阶图见 `thin-shell-probe-center-reference.json`、`thin-shell-static.png`；参考点位于全分辨率插值前，故这也是有限的重建诊断。

普通天花板、地面和背墙 ROI 的方差收益没有在两次重复中一致成立，不能推广为整幅画面降噪。

移动两次各检查 3,894 个新露出像素样本：旧历史年龄与接受计数均为 0，时域输出减当前 Resolve 的 P99 / 最大差均为 0。最终输出减 Resolve 的 P99 仍升高，且该差包含空间滤波，不能解释为纯拖影。更严格的全屏输入见证发现移动组 6 帧有 8 条 GBuffer 哈希记录不同；深度、布点、逆矩阵和派生几何相同。故报告为 `staticQualified=true`、全组 `qualified=false`，没有宣称移动质量不回退，也未执行可归因的 GPU 性能 A/B。

早期几何输出不匹配的报告保留为未通过；新增输入见证不放宽原来的几何逐位相同门槛。`trace-input-quality-analysis.json` 的初版验收逻辑有误，允许派生几何一致掩盖输入失败，**该报告作废**；最终 v3 要求实际输入与原有几何门槛同时通过。4 项 Python 回归覆盖输入差异、矩阵差异以及极小派生位置差异的拒绝。

## 验证与环境恢复

16 个相关 DX12 kernel 变体编译、GPU 区域读回和参考聚合通过。各次捕获有独立源码 / 执行脚本快照，最终质量组各校验 2,787 个文件。没有启动活动 Editor 内的 Unity Test Framework。

实验期间一次 Editor 原生崩溃来自曝光 Inspector 的异步读回：代码重载时，通过已释放的 Camera 包装对象查字典触发 `UnityEngine.Object.Equals`。提交 `e3af495c` 让回调持有自身 SnapshotState，并在重载、退出播放、退出 Editor 和清除过期状态时退休旧状态。无 Editor 时执行的两项定向 Unity 测试通过，包括稳定调用零分配；恢复后额外验证 3 路真实 GPU 读回隔离，并成功切换播放。

原始崩溃栈和失败尝试保留。PIX 预检确认恢复 Editor 使用 DX12 和匹配的早期注入运行库。最初启动 watchdog 未附着成功；之后对预检确认的 PID 监控了 900 秒，无内存 / 句柄超限，不宣称获得了完整启动阶段监控证据。

分析入口：

```powershell
python -B Tools~/Diagnostics/test_screen_probe_thin_shell_quality.py
python -B Tools~/Diagnostics/AnalyzeScreenProbeResidualReference.py E:/VividRP_GI_Evidence/ScreenProbeResidualMap-20261115-104052-278383
python -B Tools~/Diagnostics/AnalyzeScreenProbeThinShellQuality.py E:/VividRP_GI_Evidence/ScreenProbeResidualMap-20261115-104052-278383 --baseline trace-input-before --candidate trace-input-after --reference inside-shell-reference --output trace-input-quality-reanalysis.json
```

生成新质量脚本使用 `BuildScreenProbeThinShellQuality.py NEW_OUTPUT --motion-history --trace-inputs`。不覆盖旧输出目录；先固定实际源码与执行脚本，运行后验证快照。完整像素只保存固定 ROI、一个末帧和必要输入端点；逐帧全屏 GBuffer / 深度仅保存哈希。最高质量组设 512 MiB 载荷上限，紧凑参考设 48 MiB 上限，证据根目录加入保留清单。后续优先定位剩余的空间重建正偏差，再建立移动组可靠输入与独立 GPU 计时证据。

结束时实际观察到正常非诊断帧恢复 8 阶 / 32 批、16 px / 8 rays、age512 / FP32、4-pass À-trous、bias=0.04；回调清空，强制帧与各项覆盖参数关闭，两场景和 Profile 干净，播放已暂停。`post-experiment-storage.json` 只统计本次新根目录、不跟随链接：新增约 3.059 GiB，结合既有清单估算证据总量约 113.501 GiB，E 盘剩余约 123.215 GiB，未触及 125 / 64 GiB 审查线。没有删除受保护的参考、失败记录或崩溃证据。
