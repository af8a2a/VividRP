# 原生 Gather 的连续帧与自然帧控制

2026-11-15，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti，纯自发光 Cornell Box，同机位1920×1080、16 px / 8 rays、原生 Gather、四轮 À-trous、8阶 / 64样本 / 32批源。本轮没有修改生产 GI 默认值，没有执行或推广 FP32/512 候选。

## 同 kernel 常量见证：有效，但没有复现异常

`E:/VividRP_GI_Evidence/ScreenProbeTemporalInlineWitness-20261115-041510-409875` 的临时原型让 temporal 原调用把所用常量写入额外一行，不增加旁路 dispatch。32位值分成13/13/6位，编码成三个有限且精确可表示的半精度值，避免半精度存储丢失常量位。

两组各256帧，510份实际见证逐位匹配 CPU 预期，4,661对受检信号一致。末帧255没有读取常量，不能称为512份见证。2,782份源码 / 协议 / 实际脚本核验未变，二进制载荷52,632,192 B，低于96 MiB上限。

该运行没有复现原异常，不能证明先前失败 dispatch 的输入正确。新增存储和编译代码会改变观察条件，因此原型已撤回，仅保留精确 patch、原型源码、原始见证与报告。正常 temporal 纹理已实测恢复为1920×408。

## 有界连续 PIX 窗口

`PixCaptureBridge.Execute` 保留原19参数公开签名、默认参数及默认单帧行为；另一个重载接受必填的 `captureFrames`（1～4）。当前是 C# API，不添加未经实现的 CLI 参数。

每个真实相机调用仍使用自己的 Begin/End 帧索引。窗口拒绝不配对、重复、倒序与超出数量的调用，多帧窗口内插入其他相机也失败，防止把其他相机的GPU工作当作目标相机证据；每帧检查提交结果和渲染错误，仅在最后一个有效提交后执行捕帧专用 join / FrameEnd。Status 增加 `requestedCameraFrames` / `submittedCameraFrames`。原有 native 所有权、取消、超时及 GPU 内容校验保持使用。

52项独立 .NET 会话检查通过，覆盖默认1帧、连续2帧、边界4帧、非法范围、错误序列和其他相机插入。未运行 Unity Test Framework。

实际证据在 `E:/VividRP_GI_Evidence/ScreenProbeContinuousPixPair-20261115-043230-349638`：

| 控制 | 真实相机帧 | GI GPU 工作 / 内容验证 |
|---|---|---|
| 连续组A | 2508、2509；准备排空在2244 | 34个工作、4个逻辑scope、ready |
| 连续组B | 2901、2902；准备排空在2637 | 34个工作、4个逻辑scope、ready |
| 默认API，不传帧数 | requested=1 / submitted=1 | 16个工作、4个逻辑scope、ready |

连续组各有3个物理queue，默认单帧为2个；不将数量或逻辑scope解释成 GI 在异步队列执行的证明。连续组将 preparation drain 提前到源预热第一帧，等待264次源渲染后捕获 epoch0→1；两帧之间不关闭相机或加入捕帧专用同步。两组输出一致，但捕帧开始仍在 epoch0 前加入边界同步，不能冒充完全未受扰动的渲染。

两份连续 canonical 捕帧、默认单帧捕帧、状态 / 内容校验、脚本、源码指纹及读回保留，SHA见 `analysis.json`。另从第一份连续捕帧的实际PSO1237提取 temporal DXIL：22,204 B，SHA256为 `1527c6fb3bc97ebe3f2d8813a30cd504f732cb503923ce89e998247a217304f8`，与诊断前原程序逐位相同。这排除了只恢复磁盘源码、GPU仍加载见证原型的情况。

提取脚本、原程序、压缩块及生成C++保留；188,544,504 B的可再生成 `resources.bin` 在核验后删除，审计为 `loaded-shader-derived-retention.json`。导出目录需重导出该数据后才能构建或回放。

## 自然冷启动与完整长 A/A

随后关闭 PIX 会话，`ScreenProbeNaturalColdTrials-20261115-043832-933141` 连续完成八个独立冷启动，每组只观察 epoch0 / 1。280对有效对应纹理一致，全部源等待264帧、其他相机零帧、源码未变。与连续 PIX 第一组对照，仅第1帧的 temporal / filtered directional 不同，其余受检输入相同。该相关性不能代替未捕获 dispatch 的常量或执行顺序证明，也不构成生产同步错误已定位。

基于稳定短控制，`ScreenProbePostControlGpuTiming-20261115-044317-996633` 再执行原有完整长 A/A→两轮 ABBA 门槛。两组原 FP16/128 都运行到1728帧；前1022帧的密集方向状态和第1023帧 raw一致，末帧几何与Radiosity一致，但第1727帧 raw有 **3,228 / 16,320** 个texel不同，最大分量差79.685546875。18,488对观察中，排除无效reset帧旧存储后仅该保存检查点不同。

源码、两份外部输入及实际脚本未变。第1727帧只是首次保存到的raw分歧；raw实际起点在1024～1727之间，方向状态可能从1023开始变化。不能把检查点当作发生时刻，也不能断言停止读回或启用recorder就是原因。

`timing-qualification.json` 已拒绝比较。每个原配置 marker 得到575个 GPU 样本，也不能补救输入失配；所有512候选均未执行，不发布候选 GPU 成本或画质改善。下一步用原 shader 分离 recorder 开启与密集读回结束的影响，保留稀疏端点检查，不先加生产栅栏或推广长历史。

## 收尾

见证 C# / shader 原型精确撤回，默认 FP16/128、16 px / 8 rays、四轮、bias0.04、源8/32均经正常帧核验；两个场景干净，hooks为空、override=-1、flags0、PIX idle。完整计时前发现原播放状态已暂停，临时恢复后按原状态收尾。兼容API重载的编译与最后状态另存于连续捕帧目录；没有保存场景或 GI Profile。

两份被撤回的旧 Sponza planar 原型已按引用审查归档，释放20,391,526,400 B，224份首尾载荷校验通过。当前候选与参考保持完整；容量和旧数据再分析限制见 [GI 数据保留](GiEvidenceRetention.md)。
