# 原生 Screen Probe producer 长序列重复性

## 目的与固定条件

[anchor 精度 / 窗口实验](ScreenProbeAnchorPrecisionExperiment.md) 的长基线 A/A 在第 4,095 帧检查点已出现 raw 分歧，FP32 / 512 候选因此未执行。本轮先定位 producer 的重复性，保留现有默认渲染实现与已保存资产；它不是降噪收益、物理能量或 GPU 性能实验。

仍使用纯自发光 Cornell 固定机位、1920×1080、16 px / 8 rays、guarded anchor 预滤波、age128、四轮 À-trous、方向及分层引导。源为已收敛的 8 阶 / 64 样本 / 32 批 Radiosity；太阳、天空和近场 AO 为零。每组独立清理该相机历史，等待源连续八帧稳定，再使 Gather 签名失效以开始独立 epoch；采样帧号设为 `1733 + epochFrame`。

## 逐帧 producer 控制

`ScreenProbeProducerEpoch-20261114-163837-067569` 两组各运行 4,608 帧。每帧记录完整 CPU 历史 / 场景签名及几何、raw、引导 proposal 的 GPU SHA-256；首两帧、幂次帧、每 512 帧和最后一帧额外检查方向历史、SH、屏幕源、运动与 Radiosity。基线 raw 全量保留，其他载荷主要保留首尾与首次分歧，降低磁盘占用。

- **13,824 对逐帧主要输入 hash，零分歧**；加上辅助检查点，共 **14,087 对** GPU 检查点，全部一致。
- **9,216 条**逐帧 CPU 状态连续，无意外历史失效、源更新或所记录采样帧号偏离；两组 VP 与受检 CPU / 场景字段无差异。
- 几何、源 atlas、屏幕源、屏幕几何和运动纹理在受检位置各只有一个 hash；Radiosity 与统一参考 `8d1b4e59…` 完全相同。
- **2,781 份**源码 / 实际捕帧脚本及两份外部参考输入在捕帧前后未变。11 个 producer / 重建 kernel 的实际 D3D12 编译二进制已保存。
- 恢复查询确认两个场景未脏、回调为空、flags=0、frame / Atrous override=−1、age128、16 px / 8 rays、四轮滤波和 bias=0.04。

CPU 对比仅排除 `previous.Frame` 的准备阶段计数差异；真正配置给 shader 的采样帧号逐帧单独检查。GPU 主输入确实每帧读取，辅助来源仍是检查点，不能据此宣称所有辅助资源在帧间都未变化。

本次未复现旧分歧。密集读回改变了命令与 CPU / GPU 等待节奏；它的通过不能证明之前的失败已修复，也不能排除观察方式影响。源代码中的 `SdfDiffuseGatherPass` 和 `GlobalSdfClipmapPass` 没有实现 `IAsyncComputeSupportedPass`，正常录制器不会将它们作为异步计算 pass；本轮没有据此修改同步策略。

## 稀疏复查

同一捕帧工具的 `densePrimary=false` 配置运行 6,400 个 epoch 渲染帧，仅在 0、4,095、4,096 后每 9 帧及 6,399 读取 producer；CPU 签名仍逐帧记录。首次观测到主要输入分歧后，第二组最多再运行八帧。该模式只给出首次已观察分歧，不能把稀疏检查点当作真实发生时刻。

实际脚本、预算和初始快照位于 `ScreenProbeProducerSparse-20261114-170522-580442`。启动前确认 **2,779 份生产 / 夹具文件**与逐帧控制相同；最终结果另以该目录的 `analysis.json` 与源码核验为准。复查不运行精度候选，不改变正常 Profile。

实际两组各 **6,400 帧**完整结束，**3,884 对**稀疏 GPU 检查点零分歧，其中主要输入 777 对；**12,800 条**连续 CPU 状态无事件、无组间差异。受检 2,781 份源码 / 实际脚本及两份输入在捕帧前后未变，恢复状态检查通过。

两种读回节奏在共同帧上的 **354 对**主要输入 hash 全部相同，11 个编译 kernel 的实际二进制 SHA-256 也一致。与旧失败对照的 0 / 4,095 / 4,096 / 6,399 帧比较：第 0 帧两组均相同；后三个检查点上，新结果的 raw / anchor / 统计 / SH 与旧 **B** 组相同，与旧 A 组不同，几何仍一致。旧 A 组的分歧未复现，具体触发仍未知；本轮结果没有证明或修改某个生产同步错误。

这些控制允许恢复带输入有效性门槛的质量实验；后续捕帧继续逐帧记录历史、场景版本及源状态，在条件不合格时明确拒绝质量比较。完整交叉结果见稀疏目录 `cross-cadence-analysis.json`，不将通过控制等同于画面质量提升。

## 工具与判定

`ScreenProbeProducerEpochCapture.cs` 默认密集模式。复现时在独立实际脚本中选择模式与新的输出目录，并在运行前生成源码和输入 hash。捕帧结束、恢复回调后再核对源码；只有核验通过才运行分析：

```powershell
unity command script eval --project-path E:/VividRP_Reborn --file ACTUAL_CAPTURE_SCRIPT --timeout 7200 --format json
python -B Tools~/Diagnostics/ScreenProbeProducerEpochExperiment.py CAPTURE_ROOT
```

分析器检查 CPU / GPU 帧覆盖、实际配置和格式、相机、源 hash、采样帧、历史签名、首次主要分歧及其前后 CPU 状态。B 组补采上下文若没有 A 组对应载荷，只作为单边诊断，不当作成对证据。`qualifiedFullPrefix` 只表示所声明采样范围的 A/A 条件通过。未运行 Unity Test Framework，Editor 保持交互运行；没有 GPU 计时或本轮画面质量推广。

后续质量验收仍需把独立高样本参考下的固定误差、最终像素时间方差、平均能量与移动露出区域一起评估。已有 grid 参考是同一渲染器的探针标量估计，不能直接冒充独立物理真值或包含正确方向分布的全分辨率参考。
