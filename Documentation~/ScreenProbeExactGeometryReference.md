# Screen Probe 精确几何参考与追踪模式校验

本轮增加了逐字固定接收面位置的高采样参考、显式追踪模式和原 shader 对照。实际捕获中的两种几何可精确覆盖此前全部7,168个目标点帧，不再因1 ULP位置差异丢帧。但 **Hybrid 联合参考尚未通过能量一致性检查**：相同接收点在一个采样块中返回不同的块均值。这个失败保留，未放宽匹配条件、平均冲突数据或修改默认渲染参数。

证据目录：`E:/VividRP_GI_Evidence/ScreenProbeGeometryReference-20261115-200000`。起点 `229782ef`；生产 Runtime / shader 仍为 `7b9d8836`。Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti；Cornell 固定机位，16 px / 每探针8条射线，原生 Gather，关闭采样引导进行均匀序列参考。冻结 Radiosity 为8阶 / 64样本 / 32批，SHA-256 为 `40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。

## 几何与参考范围

上一轮256帧的完整基础探针网格有两种位模式，出现7次和249次。固定输入为实际捕获的120×136 RGBA32F几何，其中基础网格120×68；不修改原生几何或增加位置容差。目标仍为天花板第59行、x46–73共28个探针中心。17个目标有两种几何，联合目录共45个目标键，键包含探针位置及4个float的原始字节，区分有符号零。

每种参考运行两个独立序列（7919、25387），各512帧预热、1024帧测量，合计16,384条射线/探针。32个64帧块保存双精度RGB、coverage、亮度的一阶和二阶聚合；这只是同渲染器的当前帧 consumed E/π 参考，不是物理真值或 Unreal 全分辨率参考。

参考 shader 仅在内存中创建，保持原9个 kernel 编号，追加基础网格法线见证。原始 Record 负责场景/缓存绑定。每序列前64测量帧及末帧另做当前原生位置与相同位置强制重放，输出使用诊断自有资源，比较目标几何及raw RGBA16字节。每种参考内部的130帧A/A均通过；2048帧基础几何与完整浮点法线hash稳定，GBuffer0/1、冻结Radiosity端点hash一致。两序列首个64帧块也由Python从原始数据独立重算。

**固定位置只作用于 TraceProbe；Hybrid 的屏幕追踪仍使用原生接收面及屏幕历史输入。** 因此仅凭上述内部A/A，不能宣布Hybrid的所有追踪输入已经固定，也不能推广到全部网格像素。

## 模式校验与联合参考失败

最初新参考显式使用SDF Only，旧参考沿用Profile的Hybrid。二者不能直接作为同一能量目标。新增原 shader 短对照明确切换模式，保持相同7919序列、512帧预热、64帧测量和冻结Radiosity：

| 数据 | 目标平均Y | 核验范围 |
| --- | ---: | --- |
| 原 shader SDF Only，64帧 | 0 | 与对应参考的当前原生位置重放，1792个RGBA点逐字一致 |
| 原 shader Hybrid，64帧 | 0.2471648583 | 对应1792个RGBA点逐字一致；几何仅49/64帧逐字一致 |
| SDF Only，完整双序列 | 0.0008819516 | 两种固定几何内部通过；均值不能替代Hybrid目标 |
| Hybrid，固定几何0 | 0.2526491160 | 目标Y的32个块均值与旧Hybrid参考一致 |
| Hybrid，固定几何1 | 0.2526369437 | 与几何0的整体均值差约−0.0048% |

短对照不是高采样能量验收；原程序与参考的raw相同，也不能掩盖未匹配的几何。`native-mode-conformance/analysis.json` 将raw输出一致性、几何一致帧数和能量参考资格分别记录，后者保持false。

`hybrid-matched-reference-analysis.json` 保留联合失败：两参考的11个共同几何目标中，探针(48,59)的第21块（uniform-b、测量帧832–895）有差异，最大块均值差0.0109063202。其余共同目标块未发现差异。联合几何覆盖仍为7,168/7,168，能量联合资格为false；分析命令写出失败报告并返回非零。没有删除该块或用另一参考替换冲突值。

目前只能定位到Hybrid参考仍有未固定的输入。GBuffer、法线和Radiosity端点一致不足以证明屏幕光线、HZB、屏幕几何/辐亮度历史与相关矩阵全序列一致。下一步应针对这个64帧块记录屏幕光线及输入见证，再决定是否需要固定屏幕追踪输入。尚未实施颜色历史空间反馈，也没有新的降噪或GPU性能收益结论。

## 验证、恢复与存储

90项Python诊断回归检查通过，覆盖不可覆盖输出、实际写入源码hash、模式/程序/源码禁止混合、1 ULP/有符号零拒绝、冲突报告及既有统计/保留策略。活动Editor中未运行Unity Test Framework。实际DX12参考编译及GPU读回通过；首个SDF尝试因法线见证引用未绑定的Adaptive Placement失败，改为基础网格独立见证后另建目录重跑。首次Hybrid脚本在执行前因未限定`ReferenceEquals`编译失败，改为`object.ReferenceEquals`后在新目录运行。失败记录保留；该未执行目录的工具源核验失败来自随后修正模板，未当作有效捕获。

实际正常帧确认默认Hybrid、16 px / 8 rays、原生Gather、4-pass À-trous、FP32 anchor age512、8阶/32批Radiosity恢复。追踪shader恢复为原持久资产，三个生产重建程序DX12 hash与实验前相同，所有诊断回调清空、覆盖参数复原、场景及Profile干净。Editor播放暂停，Frame Debugger关闭，PIX空闲。当前Editor已正确注入PIX，本轮复用该进程，没有新重启或新增`.wpix`。

补充模式对照及两组Hybrid参考后，目录曾达到约196.6 MiB，超出最初SDF-only计划的192 MiB审查目标。已按显式清理策略归档闲置`variant-0-v2`的630个中间raw/A/A读回，释放16,619,904字节；其全部聚合、报告、源码及首末帧仍保留，该变体不再可全序列重分析。模式对照使用完整保留的`variant-1-v2`，Hybrid两组及其失败证据完整保留。最终目录约181 MiB，证据总量估算124.18 GiB，E盘剩余约112.49 GiB。最终存储、工具归档及逐文件删除hash见`post-experiment-storage.json`、`tool-archive-v2/manifest.json`、`archive-apply.json`与对应`raw-retention-files.jsonl`。未删除已接受的生产基线或原高采样参考。

## 复现

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeGeometryReference.py NEW_REFERENCE --geometry CAPTURED_GEOMETRY.bin --trace-mode Hybrid
python -B Tools~/Diagnostics/BuildScreenProbeTraceModeConformance.py NEW_MODE_CONTROL
python -B Tools~/Diagnostics/AnalyzeScreenProbeGeometryReference.py REFERENCE_0 REFERENCE_1 --comparison-capture NATIVE_CAPTURE --output NEW_REPORT.json
python -B Tools~/Diagnostics/AnalyzeScreenProbeTraceModeConformance.py MODE_CONTROL --sdf-reference SDF_REFERENCE --hybrid-reference HYBRID_REFERENCE
python -B -m unittest discover -s Tools~/Diagnostics -p 'test_*.py'
```

每次先固定实际脚本、协议、输入及源码快照，再通过活动Unity CLI执行；捕获结束立即核验源码。参考读回各受48 MiB上限约束，模式短对照受24 MiB上限约束。混用模式、程序或源码的参考必须拒绝；几何覆盖资格不等于能量比较资格。
