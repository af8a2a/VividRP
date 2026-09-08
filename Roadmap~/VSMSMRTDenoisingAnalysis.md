# SMRT 噪声：Unreal 公开机制与当前实现差距

2026-09-08；当前源码 `584636b5`。本轮是文档、源码和现场设置分析，没有修改 Shader、TSR 或场景，也没有新增画质/性能实验。

建议先推进 **时空蓝噪声采样对照与 TSR 历史裁剪诊断**，再决定是否增加专用阴影历史。现有结果不足以把所有亮点归为零均值随机噪声；倾斜面求交和深度近似仍需独立验收。

后续已完成现有资源的复用分析：优先沿用 BlueNoise 框架做 BND 相位对照，详见 [复用可行性与离线结果](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMBlueNoiseReuse_20260908/README.md)。

## 可由 Epic 公开资料确认的机制

| 机制 | 与本问题的关系 | 一手资料 |
|---|---|---|
| UE 5.5 的 SMRT 改用时空蓝噪声，涉及步进偏移和方向光射线 | 应先让低样本误差更利于时域/空间重建；发布说明只称较小的质量与性能改善，不能承诺单项解决全部噪声 | [UE 5.5 Release Notes](https://dev.epicgames.com/documentation/de-de/unreal-engine/unreal-engine-5-5-release-notes) |
| SMRT 射线数控制半影噪声，沿射线样本数控制可达到的软化范围；Epic 阴影档公开说明为 8 rays，推荐 4–8 samples | 当前 4×8 的八步并不等于八条独立光源射线；增加步数未必降低噪声 | [VSM / SMRT](https://dev.epicgames.com/documentation/en-us/unreal-engine/virtual-shadow-maps-in-unreal-engine#controllingpenumbrashadowquality) |
| TSR 将历史裁剪与当前帧/历史混合分别控制；单靠裁剪会在噪声下产生拖影 | 必须同时观察历史被改变多少、保留多少，不能只看二值 rejection | [TSR FAQ](https://dev.epicgames.com/documentation/en-us/unreal-engine/temporal-super-resolution-frequently-asked-questions-for-unreal-engine#shadingrejection) |
| TSR 有多帧亮度振荡分析，并在需要时放宽拒绝；历史不足处有空间 AA；Epic/Cinematic 使用 200% 历史分辨率 | 是多个环节协作，并非只调一个 history length。200% 增加历史更新负担，不宜直接作为当前预算下的首选 | [TSR](https://dev.epicgames.com/documentation/en-us/unreal-engine/temporal-super-resolution-in-unreal-engine) |
| UE 已公开逐光源 texel temporal dither 控制 | 接收位置的随机化本身不是与 UE 相反的方向，关键在覆盖、分布和强度。不能据此声称本地原点量化算法等同 UE | [UE 5.4 Release Notes](https://dev.epicgames.com/documentation/en-us/unreal-engine/unreal-engine-5.4-release-notes?application_version=5.4) |

STBN 同时约束空间和时间的误差分布；逐帧换一张空间蓝噪声，或给 Sobol / 哈希序列加时间偏移，并不会自动保证 STBN。它改善的是误差结构与可过滤性，不会增加射线数量、修复缺失遮挡层或消除估计偏差。[NVIDIA 原论文](https://research.nvidia.com/publication/2022-07_spatiotemporal-blue-noise-masks)

公开资料没有提供此处所需的完整私有 Shader 源码，因此本文不声称逐行复现 UE，也不把 `r.Shadow.Denoiser.*`、Lumen denoiser 或本地 SIGMA 当成已经确认的 VSM SMRT 默认处理链。Epic 的 5.4 说明明确把有关 screen-space shadow denoiser 的修复用于 ray-traced transmission，与上述 VSM 采样变更分开描述。

## 当前现场条件

本轮只读 Unity 得到：VividRP_Reborn，2048，SMRT 开，4 rays × 8 cells，Angular Diameter 7.1°；PCF 与旧 stochastic filtering 均开；NativeAA TSR，history 16，sharpening 0.483。Max Ray Length 在 profile 中显示 50，但 override 关闭，effective stack 为 10。源码对主 SMRT 估计只使用 PCF 开关控制原点覆盖；旧 stochastic filtering 设置在进入参考滤波/兜底时生效，不能理解为每条 SMRT 射线又执行九次随机 PCF。

这与上一轮 4096 / 0.5° / 512 页实验不同。本轮没有临时改变这些设置。GIF 为 1402×684、40 帧、约 3.27 秒，Game 视图显示 1.6×；录制帧率与 GPU 渲染帧率并不相同，调色板抖动也可能混入颗粒，不能直接对 GIF 做 TSR 收敛量测。

较大角直径会改变半影和有限步数的截断条件。在本地实现中，每层发散长度实际为：

`min(maxLength, (steps − 2.001) / sqrt(2) × worldTexelSize / tan(angle / 2))`

因此把 maxLength 从 10 增至 50 未必改变画面；达到格数约束后仍被另一项限制。降低角度只是隔离实验，不应作为改变用户所需光源外观的最终修复。

## 源码中更值得关注的差距

**1. 目前没有 STBN 保证，而且同一组稀疏射线同时积分两类变化。**

[VSMSMRT.hlsl:23](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl:23) 的光盘采样使用像素哈希扰动、径向分层和逐帧旋转；[原点采样:36](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl:36) 使用另一组 shifted Hammersley 序列，再量化到 texel 中心。四条射线同时承担光源面积与接收覆盖的积分，单层输出只有 0、0.25、0.5、0.75、1 五档；跨层混合后可有更多值，但不会补足每层样本。

包内 [BlueNoise.hlsl](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Public/BlueNoise.hlsl) 有 Sobol ranking/scrambling 资源，当前 SMRT 没有绑定它们。资源的 `Temporal` 名称不是时空蓝噪声已成立的证据。对照应区分当前序列、现有空间 BND、真正 STBN，并明确光盘与原点的采样维度。我们的求交是逐 texel 的 DDA，不能为了模仿 UE 的 step offset 而随机跳过格点，破坏上一轮薄杆检查。

**2. 历史未被拒绝，不代表它没有被噪声破坏。**

[TSRRejectShading.compute:45](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRRejectShading.compute:45) 在接受判定之前无条件裁剪历史。[TSRCommon.hlsl:177](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRCommon.hlsl:177) 的包围盒来自当前 3×3 颜色，限制为 mean ± 1.25σ，再与邻域 min/max 相交。若低射线噪声使邻域偶然偏亮或偏暗，即使二值 acceptance 为 1，积累结果也可能已经被拉向当前噪声。这是明确的机制风险，是否为当前主要损失需测量裁剪前后差值。

本地 acceptance 是二值，后续虽有连续权重，也没有与 UE 相同的 ClampBlend / BlendFinal 分离控制。无条件放宽全部 clamp 同样可能恢复旧拖影，必须由噪声、几何有效性和真实光照变化证据约束。

**3. 本地“闪烁分析”尚不是多帧振荡模型。**

[TSRDilateVelocity.compute:104](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRDilateVelocity.compute:104) 主要组合当前 3×3 空间亮度结构与一帧历史亮度差，没有独立周期/振荡状态。[TSRRejectShading.compute:87](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRRejectShading.compute:87) 的三帧同方向确认解决的是静止接收面的持续光照变化；随机误差也可能连续同向，因此不能把它当作完备的噪声分类器。

已接受像素会跳过本地 [空间 AA](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRSpatialAntiAliasing.compute:43)。这意味着“接受但反复裁剪”的区域也不会得到这条空间补偿。应先观测这类区域，不能直接据此把全画面都模糊。

**4. History 16→32 不是确定有效的旋钮。**

[TSRUpdateHistory.compute:88](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRUpdateHistory.compute:88) 在样本计数到顶后，`previousSampleCount / sampleCount` 变成 1，稳态历史权重还受 0.96 等限制。连续静止平面满足其他条件时，增加计数上限不会改变最终的 0.96 上限；更不会取消前面的邻域裁剪。几何边界、移动、光照确认又各自缩短历史，所以必须记录实际权重而非仅记录 Inspector 数字。

[TSRUpscalerPass.cs:1014](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/PostProcessing/TSR/TSRUpscalerPass.cs:1014) 的历史纹理目前按 output size 创建，未实现独立的 200% 历史尺寸。提升该尺寸有成本，且不能直接修复当前噪声判定，优先级靠后。

## 推进顺序与验收

1. **固定条件，分离噪声来源。** 保留 4096 为主要验收，2048 为当前症状复现；分别测 0.5° 和用户实际 7.1°。先做光盘随机单独变化、原点随机单独变化、两者变化的诊断，检查原始 shadow、TSR 输入/输出及平面亮点。冻结采样只用于定位，不能把不闪的固定噪声当成修复。
2. **8×4 下做真正 STBN 对照。** 保持样本数、支持范围、反馈和深度不变。检查低频噪声与时间 RMS，保留薄杆、斜面、缺页和接触宽度检查。接触宽度不能靠关闭原点覆盖或扩大滤波半径取巧。8×8 保留为较大半影参考。
3. **定位 TSR 实际损失。** 增加裁剪位移、实际 history weight/sample count、二值 acceptance、静止光照确认和 flicker confidence 的诊断。在相同随机样本下比较，不把不同相位残差全解释为拖影。针对稳定接收面，评估空间一致性/方差支持的 clamp 与多帧振荡识别，同时保留真正光照变化的快速响应。
4. **若仍达不到目标，再评估阴影专用重建。** 对 0–1 可见度使用独立、受 depth/normal/有效性约束的时间积累和小范围空间过滤，可避免把砖墙材质细节参与噪声统计；但移动阴影不随接收面 motion vector 移动，需要 shadow-change rejection。它是本项目的备选架构，不是已核实的 UE 默认管线。现有 SIGMA 还需要半影/命中距离等输入合同适配，不能直接连接平均 visibility。

除了静止，必须测相机平移、转动、静止接收面上遮挡物移动，以及灯光变化。上一轮太阳方向变化会让 VSM 转入 CSM fallback；解决该失效或只统计 active VSM 帧之前，不能用那段序列验收 SMRT 降噪。固定方向改变角直径可以补充验证，但不能替代移动遮挡物。

使用原生无损帧，分别量测 shadow 可见度和曝光固定的 TSR 亮度。半影内稳定性、接触宽度、亮点均值偏差、停止后的时间轨迹与页池状态都要记录；性能读回分开，按与 7.3 ms 一致的相机口径验证。上述优先级是本轮分析建议，尚未宣称 STBN 或新历史策略已经在本地通过实验。
