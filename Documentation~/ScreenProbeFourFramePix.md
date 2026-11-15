# 四帧 PIX 控制与 geometry SRV 原型

2026-11-15，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti，纯自发光 Cornell Box，1920×1080、16 px / 8 rays、原生Gather、FP16/128、四轮À-trous、源8阶 / 64样本 / 32批。本轮没有形成生产画质修复，没有发布GPU性能结论。

## 四帧捕获没有复现故障

[此前短窗口](ScreenProbeDirectionalBinding.md)在前帧输入一致时，第2帧temporal出现518个atlas texel差异。本轮使用现有PIX会话API捕获真实epoch0～3：源预热264帧，在epoch0前开始捕获，四帧之间不执行捕帧专用join，最后一帧后结束。捕获边界仍会改变调度。

[生成器](../Tools~/Diagnostics/BuildScreenProbeFourFramePixExperiment.py)沿用已有会话所有权、内容验证与有界source gate；没有复制native捕获实现。两组都保存四帧的8类输入／输出，共64份、189,050,880 B，低于192 MiB上限。

证据：`E:/VividRP_GI_Evidence/ScreenProbeFourFramePix-20261115-064454-901919`。

| 组 | 真实相机帧 | 验证 |
|---|---|---|
| 00 | 53585～53588 | ready，4次实际提交，68个pass内GPU工作 |
| 01 | 53981～53984 | ready，4次实际提交，68个pass内GPU工作 |

两组各3个物理queue、4个逻辑scope，不能将其解释为GI运行在独立compute queue。68次工作包含既有采集模板每帧一次的诊断depth copy，不是纯生产GI的工作计数。2,785份源码／模板／生成器、实际脚本及协议核验未变。两个canonical `.wpix` 共381,117,046 B，完整SHA在 `analysis.json`。

两份捕获的geometry、raw、composite、temporal、filtered在四帧内一致；reset帧previous、rays及screen-rays整图hash仍有差异。原始观测保留，无效历史不能用于归因，也不把这些整图差异直接当成原功能故障复现。第1帧temporal与之前的冻结重放逐位相同，与自然相机结果不同。因此 `attributionGatePassed=false`，没有取得原故障dispatch的b0证据，也没有继续做大体积C++／常量导出。

最初用猜测的 `Screen Probe Directional Temporal` 标签查询得到空结果，保留于 `events.json`；随后按源码确认精确标签 `Screen Probe Temporal Trace Radiance`，得到48条有界记录、4次实际Dispatch。正常捕获的第1帧temporal为queue1/event3049，queueType0（graphics），前一条event3048含composite的UAV→SRV转换，以及 **geometry资源1135的UAV barrier**。静态绑定显示该geometry位于UAV u0；这些是正常捕获的合同检查，不是缺失barrier或实际故障访问的证明。

## 可撤回的只读 geometry 实验

在 `E:/VividRP_GI_Evidence/ScreenProbeGeometrySrv-20261115-065335-414126` 执行原配置→SRV原型→恢复原配置，每阶段8组、每组64帧，各源预热264帧。初次基线再次于第2帧复现原518个texel差异。

原型只让temporal kernel使用geometry的 `Texture2D` 声明；其他kernel保留RW声明。初版漏改两处未使用函数里的 `RWTexture2D.Load(int2)`，因此导入产生两条DX12编译错误及一次invalid kernel记录。改成同时适用两类资源的索引形式后，四个目标DX12 kernel编译通过。编译数据和DXC dump验证temporal变为4个SRV／1个UAV；另外三个受检kernel的编译字节与恢复版一致。

原型未取得可采纳收益：第一组与第二组首帧的receiver geometry XYZ已相差最多 **7.152557373046875e-7 m**，packed normal一致；composite radiance层一致，但hit metadata有1个texel、plane metadata有45个texel不同，temporal radiance层34个texel不同。其输入在目标temporal之前已不同，不能将该结果当作SRV对temporal的净影响，也不能声称它导致最终画质变差。

由于输入门槛失败，没有继续长序列、最终画质或GPU计时。原型已撤回，仅在当前文件仍匹配自有原型hash时恢复两份精确备份；patch、原型与原始源码均保留。恢复后temporal编译数据为22,204 B，SHA256 `1527c6fb3bc97ebe3f2d8813a30cd504f732cb503923ce89e998247a217304f8`，与此前实际PSO提取结果相同。恢复阶段的00与05组又在第2帧出现原518个texel差异。没有将原型留为默认值。

三个阶段合计保存80份载荷236,313,600 B；原型首个分歧发生在epoch0，只保存两组该帧，其余两阶段各保存首个分歧的前帧及当前帧。每阶段均低于192 MiB上限，受检history内容链接通过，源码在各自运行期间未变。[分析器](../Tools~/Diagnostics/AnalyzeScreenProbeFourFramePix.py)复算SHA、会话身份、barrier所属资源、捕获与自然结果以及撤回状态。

## 运行程序与收尾

Runtime DLL时间早于源码最后写入时间，因此另核对实际加载MVID、PE/PDB身份和portable PDB中Gather源文件的SHA256。三者均匹配当前源码，避免仅凭文件时间误判旧C#：`loaded-runtime-source-verification.json` 保留全部身份与checksum。本项只覆盖受检Gather文件，不泛化成全部Editor源码均一致。

又在已有冻结输入上扫描10种历史诊断分支，共20次GPU重放；没有精确重现第1帧自然输出或第2帧比较组输出，未保存额外二进制。记录追加在保护的 `ScreenProbeDirectionalBinding-20261115-061302-342892/replay-flags-scan`，不把拟合诊断标志当作实际GPU常量。

四帧捕获和三阶段自然运行的错误列表均为空。全程Console游标203→211保留上述原型初次导入的3条错误，时间为06:57:50 UTC，晚于四帧捕获完成；不能称整个控制区间零错误。修正后及撤回后的四个kernel编译通过，最终ground truth编译错误为0，日志未清除；后续游标211检查无新错误。原shader精确字节恢复，正常参数、128、16/8、四轮、bias0.04、源8/32及干净场景均核验；hooks为空、override=-1、flags0、PIX idle，恢复检查为Playing=true / Paused=true。新脚本dry-run和实机捕获通过，15项离线PIX工作流检查通过；离线检查不替代故障复现。没有运行Unity Test Framework（Editor保持打开）。

下一步应保持资源尺寸与数量，尝试在现有、经geometry有效性验证的未使用adaptive bin中记录原kernel常量；当前参考的adaptive原点geometry.w=-1。如果运行时该位置有效则不得覆盖。只有见证运行本身也复现分歧，才可用其常量归因；否则继续明确观察条件限制，不推广猜测性同步或舍入改动。

新增目录及追加的旧目录已复核容量并保持保护，没有删除文件或创建临时链接。容量范围见 [GI 数据保留](GiEvidenceRetention.md)。

后续[保持布局的原dispatch见证](ScreenProbeTemporalWeightWitness.md)已复现第2帧518个texel差异：记录group的32个常量字段与CPU预期一致，目标线程实际四个历史tap的geometry、地址和lighting也一致；clip相同而probe Y出现约3.8e-6差异，使微小历史权重跨过接受阈值，随后center分支放大结果。尚未修改生产路径，原型全部撤回；下一步可针对坐标运算精度与低支持历史的center资格分别验证。
