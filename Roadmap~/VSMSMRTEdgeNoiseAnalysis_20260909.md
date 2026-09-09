**SMRT 边缘噪声：Unreal 机制与当前推进方向**

2026-09-09；源码基线 `713213bd`。本轮只做源码、附件和 Epic/EA 一手资料分析，未改变渲染代码或场景。本文接续 9 月 8 日分析，并纳入已完成的 [BND 对照](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMBNDComparison_20260909/README.md)。

当前最值得对齐的是 **采样误差与 TSR 历史重建之间的配合**。BND 接入已经完成，但其收益不能按 UE 的 STBN 实现推定；上一轮原始阴影的部分低频残差下降，最终 TSR 噪声却没有下降。优先验证历史裁剪、混合权重和多帧噪声识别，再评估专门的时空噪声资源。

**附件能够说明什么**

附件为 1400×684、18 帧、总长 1.44 秒的 GIF，Game 视图显示 1.3×。横梁下方和拱顶附近的半影颗粒是本轮关注区域。GIF 调色板量化和视图放大也会改变细颗粒外观，不能用此片段直接量测 TSR 收敛。真实渲染噪声已有上一轮原生纹理读回支撑。

之前记录的现场配置为 2048、SMRT 4 rays × 8 steps、角直径 7.1°、TSR history 16；本轮没有重新查询或调整现场。以下定量结果来自此前固定 4096 的对照，不能当作这次 GIF 的直接测量。

**Unreal 的公开处理机制**

| 环节 | 公开机制 | 对本项目的意义 |
| --- | --- | --- |
| 半影采样 | 射线数决定半影噪声；Epic 阴影档正文说明使用 8 rays，沿线 4–8 samples 控制可达到的软化范围。[Epic VSM](https://dev.epicgames.com/documentation/unreal-engine/virtual-shadow-maps-in-unreal-engine) | 4×8 中的 8 是步数，只有 4 次光源方向估计。 |
| 噪声分布 | UE 5.5 对 SMRT 步进偏移和方向光射线采用时空蓝噪声；发布说明将收益描述为小幅质量和性能改善。[UE 5.5](https://dev.epicgames.com/documentation/de-de/unreal-engine/unreal-engine-5-5-release-notes) | 对齐目标包括实际方向、时间及像素之间的误差分布，不能仅看资源名是否带 BlueNoise。 |
| 历史取舍 | TSR 分别使用 ClampBlend 和 BlendFinal 控制历史颜色约束及当前帧占比；噪声下只依赖 clamp 有拖影风险，增加当前帧占比则会暴露噪声。[TSR FAQ](https://dev.epicgames.com/documentation/en-us/unreal-engine/temporal-super-resolution-frequently-asked-questions-for-unreal-engine#shadingrejection) | 应测量并分别控制裁剪和混合，而不是把 rejection 当作唯一可信度。 |
| 闪烁与重建 | TSR 在不透明光照后测量亮度，跟踪多帧振荡并有条件放宽历史拒绝；历史不可用时配合空间 AA。Epic/Cinematic 的 200% 历史分辨率主要改善重投影细节，历史像素量变为 4 倍。[TSR 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/temporal-super-resolution-in-unreal-engine) | 静止噪声、真实光照变化及几何边缘需要区别处理；200% 历史不是当前低成本首选。 |

以上是可由公开资料确认的组件与职责，不是对 UE 私有 Shader 的逐行复现。资料不足以把 `r.Shadow.Denoiser.*`、Lumen denoiser 或 NFOR 认作这条 VSM SMRT 默认管线。NFOR 的公开定位是离线路径追踪降噪。[UE 5.5](https://dev.epicgames.com/documentation/de-de/unreal-engine/unreal-engine-5-5-release-notes)

UE 还使用自适应射线数控制成本；这不能等同于专用降噪器。本地循环固定执行配置中的射线数，因此网上常见的“关闭自适应采样”排查思路没有对应的本地开关。[Epic VSM](https://dev.epicgames.com/documentation/unreal-engine/virtual-shadow-maps-in-unreal-engine)

**当前实现的具体差距**

1. **四条射线同时承担光盘和接收覆盖采样。** [VSMSMRT.hlsl:127](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl:127) 的每条射线输出 0/1，再平均。单层 4-ray 可见度只有 0、0.25、0.5、0.75、1 五档；跨层混合虽可增加数值档位，却不会增加单层采样信息。半影最容易看到这些变化。上一轮 BND 从 4×8 增至 8×8，最终 RMS 降低约 29%–32%，说明有限采样仍是实质因素。

2. **BND 目前提供的是分层射线集合的相位。** [VSMSMRT.hlsl:25](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl:25) 用维度 0/1 控制光盘、2/3 控制接收偏移；再经过径向分层、旋转、Hammersley 平移及 texel 中心量化。四条射线之间有结构性关联。即使相位图有良好空间分布，也不能据此推定最终遮挡误差具有 STBN 性质。上一轮只验证了这个具体接法没有最终降噪收益，没有否定 BND 的其他用法或真正 STBN。

3. **接受历史前已经无条件裁剪。** [TSRRejectShading.compute:44](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRRejectShading.compute:44) 使用当前 3×3 颜色盒裁剪历史；[TSRCommon.hlsl:206](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRCommon.hlsl:206) 将范围限制为均值 ±1.25σ，并与邻域 min/max 相交。随机偏亮或偏暗的当前邻域会推动历史颜色，即使后续 acceptance=1。上一轮静止片段接受率接近 100%，仍有约 15%–19% 的已接受样本发生超过 0.001 亮度的裁剪。这支持优先做因果对照，但还不能证明裁剪是唯一噪声来源。

4. **当前闪烁判定缺少独立的多帧振荡状态。** [TSRDilateVelocity.compute:137](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRDilateVelocity.compute:137) 主要使用当前空间对比和当前亮度相对一帧积累历史的差值。三帧同方向的光照变化确认是另一类状态，不能充当完整噪声分类器。静止 BND 4×8 片段仍有约 0.8%–2.5% 的样本触发该确认状态；来源可能包含随机采样和 jitter 覆盖，不应统一视作真实照明变化。

5. **样本计数、实际权重和空间补偿并未形成完整的连续控制。** [TSRUpdateHistory.compute:83](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRUpdateHistory.compute:83) 在计数到顶后得到 previous/sampleCount=1，再受 0.96、运动及边界限制。只把 history 16 改成 32，不保证改变稳定区域最终权重。已接受像素还会跳过 [空间 AA](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/TSR/TSRSpatialAntiAliasing.compute:40)，所以“接受但强裁剪”不会自动得到空间补偿。UE 的 History.SampleCount 则明确约束当前帧最小贡献为其倒数；两者不能按同名参数直接对标。[TSR FAQ](https://dev.epicgames.com/documentation/en-us/unreal-engine/temporal-super-resolution-frequently-asked-questions-for-unreal-engine#tsrhistory)

**更接近 Unreal 的噪声资源路线**

UE 5.5 发布说明还记录：通用 BlueNoiseScalar/BlueNoiseVec2 使用 FAST 改善资源质量。该条与 SMRT 使用 STBN 的条目共同说明了引擎方向，但不足以断言某个 UE SMRT 版本的所有维度都直接使用同一张 FAST 纹理。[UE 5.5](https://dev.epicgames.com/documentation/de-de/unreal-engine/unreal-engine-5-5-release-notes)

EA 的 FAST 根据空间和时间过滤器优化采样误差，并支持标量、向量和不同概率分布。这比只追求原始噪声图“看起来更蓝”更适合本项目的验收目标。[EA FAST](https://www.ea.com/seed/amp/news/spatio-temporal-sampling)、[官方实现](https://github.com/electronicarts/fastnoise)

后续对照应明确 Vector2 光盘采样、接收覆盖采样和帧索引的合同，验证映射后的实际 visibility，以及经过当前 TSR 的误差。评估完整周期、运动中历史重置与像素跨越时的表现。保留现有 DDA 的逐格求交保障，不通过随机跳过格子来模拟 UE 的 step offset。

**建议的实验顺序**

| 优先级 | 固定项和变量 | 验收重点 |
| --- | --- | --- |
| 1 | 固定 4096、4×8、BND、光源外观，仅分离历史裁剪强度和最终历史混合 | 原始输入一致时，最终 RMS、裁剪量下降；灯光变化与移动遮挡的响应不退化。 |
| 2 | 在前项基础上，对照多帧亮度振荡/趋势统计与现有三帧确认 | 静止噪声不频繁缩短历史；持续照明变化和新增可见区域仍及时响应。 |
| 3 | 固定射线数、步数、求交和覆盖范围，BND 与真正 STBN/FAST 对照 | 检查原始及最终低频噪声、完整时间周期，而非仅看静态纹理频谱。 |
| 4 | 8×8 作为已验证降噪的质量参考，再评估采样预算分配 | 性能单独测量；不能用不充分的早停判定破坏半影均值或薄遮挡。 |

优先级 1 应用几何连续性和噪声可信度约束裁剪放宽；真实照明变化需独立保留快速响应。诊断保留原始重投影历史与裁剪后历史，不能仅在已裁剪的结果上推断变化。均值/方差、振荡状态只是本项目候选实现，不声称等同 UE 未公开算法。

不建议把 **8×4 当作与 4×8 等价的 32 次比较对照**。本地 [CSMShadowResolve.compute:1088](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:1088) 将射线发散长度绑定到 `(steps−2.001)`；当格数上限起作用时，8 步降至 4 步会使范围缩至约三分之一。画面变干净可能同时来自软化范围收缩，尤其需要核对 7.1° 大角直径。

若上述仍无法达到预算和观感要求，可再评估 shadow visibility 的独立时间积累与小范围边缘保持过滤。它能将阴影统计从砖墙材质细节中分离，但移动阴影并不跟随接收面的 motion vector，仍须独立处理阴影变化。这是本项目的备选方案，尚非已确认的 UE 默认 SMRT 降噪流程。

Epic 当前在线 5.8 发布说明还列出实验性的远处投影物预滤方案：用低分辨率 clipmap、预滤和时间重投影生成平滑结果。它值得作为远距离覆盖的后续研究，但不能直接当作本例近处横梁和拱门的修复。[UE 5.8 发布说明](https://dev.epicgames.com/documentation/unreal-engine/unreal-engine-5-8-release-notes)

所有对照继续分开处理缺页、深度近似偏差和随机噪声。此前 512 页无溢出的组仍有噪声，因此页池不是全部原因；生产 256 页的溢出也不能被降噪掩盖。验收需同时记录接触宽度、薄杆阴影、半影均值、动态恢复、页面状态和主相机 GPU 时间。
