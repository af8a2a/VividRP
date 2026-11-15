# 原生 Gather 第 1 帧的成对 PIX 检查

2026-11-15，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti。沿用已授权重启后的 PIX Editor，原 shader、纯自发光 Cornell Box、固定机位、1920×1080、16 px / 8 rays。本轮未推广 FP32/512，没有新增 GI 降噪或性能改善结论。

## 未捕获对照仍有分歧

`E:/VividRP_GI_Evidence/ScreenProbeFreshPixTiming-20261115-032443-384775` 先验证 GPU recorder：7 个 marker 各 192 个正值、count=1 的样本，无环形覆盖、无其他相机；2,782 份源码及实际脚本指纹未变。这只证明计时可用。

完整 A/A→两轮 ABBA 在第二组原 FP16/128 的第 4 帧自动停止，未进入候选比较。首次分歧是第 1 帧 `directional-temporal` 的 545 个 texel，随后影响 `directional-filtered`，第 2 帧才影响 raw / proposal / rays。该帧已读回的 geometry、composite、previous geometry / directional / filtered directional 均逐位一致，记录的当前/上一帧矩阵与 light scale 相同。不能用这些晚期读回和 CPU 数值代替异常 dispatch 当时的绑定证明。

第一组完成 1,728 帧；两组源均等待 264 帧、8 阶 / 64 样本 / 32 批就绪。源码与实际执行脚本未变，`timing-qualification.json` 明确拒绝比较，不输出候选 GPU 成本。二进制载荷硬上限为 256 MiB。

## 精确捕获原 temporal dispatch

证据在 `E:/VividRP_GI_Evidence/ScreenProbeTemporalPixPair-20261115-034908-669385/controlled`。独立脚本执行两组冷启动，每组准备源后重置 Gather：标准 PIX preparation drain 渲染 epoch0，临时关闭目标相机直到 native armed，再渲染并捕获 epoch1（sampleFrame1734）。包装 observer 只转发原 `PixOperations`，核验所有权并在 `finally` 释放；没有替换 kernel 或修改生产捕帧代码。

两组均恰好提交两帧，其他相机零帧，标准内容校验均为 ready：2 个物理 queue、GI pass 内 17 个 GPU 工作、4 个逻辑 scope 有序配对。源码 / 协议 / 实际脚本校验通过。首个尝试因缩短协议重复登记末帧而失败，其原脚本、失败与释放记录保留；修正后使用新子目录，不覆盖原记录。

| arm | 完整捕帧 SHA256 | 原 temporal 工作 |
|---|---|---|
| 00-half128 | `4aa1caaeb18f5200570eb7b9b217bd2efabf01435d8d684c2455d10955517101` | queue1 / event1194，command-list1313 |
| 01-half128 | `b4c8b292f5704b65a8bd5c4e657df8433badf4383a2bb2ff302c51546d3f8610` | queue1 / event1194，command-list1315 |

两者实际均为 graphics queue 的 `Dispatch 120×136×1`。6 个静态视图和实际访问查询分别保留，当前/上一帧资源身份不同。第 1 帧全部受检纹理相同，且 temporal 输出均等于原未捕获实验第一组；没有重现原第二组异常。epoch0 的 screen-rays / rays 元数据仍有差异，但其 composite 与 temporal 相同，不能宣称整个 A/A 逐位通过。

## 实际常量、程序与顺序

官方 C++ 导出将 b0 解析为 scratch resource366 / 367 的偏移 2,237,696。生成代码中的 runtime modification3 / 4 在各自 GI command-list 前更新相关页，后续更新不覆盖 b0 所在页546。按完整 reader 调用顺序定位压缩块，包含本次额外的 query 回读数据；完整读流总长度分别等于 192,476,800 / 192,898,006 B 的 `resources.bin`，没有沿用旧捕帧的“末尾若干块”假设。

实际程序是 DXIL 容器，入口 `ScreenProbeTemporallyAccumulateTraceRadianceCS`、线程组16×1×1；两个 22,204 B 容器逐位相同，SHA256 为 `1527c6fb3bc97ebe3f2d8813a30cd504f732cb503923ce89e998247a217304f8`。官方导出的字节码使用本机 DXC dumpbin 检查，不将 SDK 空 shader hash 视作程序缺失。

DXIL 所有常量读取均为字面寄存器索引：30、31、32、33、39、40、43、50、53、56。这十个寄存器共 160 B 逐位相同；它们的拼接 SHA256 为 `ac0025f0d8fc43f4f32635b7efa898259236dc6be26b9061f0972f27861c1a7c`。提取的 1,024 B 窗口仅在912～1023字节不同，位于所有实际读取之外；窗口长度不是声明的 cbuffer 大小，不把未使用尾部当成失配。

composite 输入在 temporal 前有 UAV barrier 与 `UNORDERED_ACCESS → NON_PIXEL_SHADER_RESOURCE` 转换，当前 geometry 也有 UAV barrier。两个原程序、被读常量及捕获输出一致，但 PIX preparation、相机门控与 queue joins 改变了调度。因此这些正常捕帧不能证明先前异常帧的绑定正确，不能证明或排除所有生产同步问题，也不能归因于某个精度表达式。

`analysis.json`、两组 `temporal-analysis/`、`constant-bytes/` 和生成 C++ 保存具体证据。`ExtractTemporalConstants-v3.py` 记录提取过程；`VerifyRetainedTemporalConstants.py` 独立解压并逐位校验 b0 / 原程序及 canonical 捕帧 SHA，不依赖全量导出载荷。

## 恢复、保留与下一步

两份 PIX session 已释放为 idle，测试 Play Mode 已退出。实际正常帧确认 callbacks=null、override=-1、flags0、age128、16 px / 8 rays、四轮滤波、bias0.04、源8/32；两场景干净。没有修改 Runtime、Shader 或保存的 GI Profile，没有运行 Unity Test Framework。

两份 canonical `.wpix`、全部读回、C++ 源、压缩块、原始常量 / DXIL 与分析保留。验证后仅清理失败的首次尝试 `.wpix` 和两份可再生成的 `resources.bin`；C++ 目录需重新导出载荷后才能构建或回放，具体删除字节见 `pix-derived-retention.json`。当前捕帧尚未重现异常，下一步应在不引入 preparation drain / queue joins 的条件下核验原 dispatch 的输入稳定性，再恢复候选计时；不放宽 A/A 门槛或先推广长历史。
