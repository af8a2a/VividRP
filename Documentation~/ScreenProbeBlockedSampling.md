# 固定分布的分块 Screen Probe 采样实验

Unity 7000.0.0a16 / DX12，Cornell 顶部单面自发光场景，1920×1080、16 px 探针网格、每探针 8 条射线。默认仍为独立白噪声；本实验由 Editor 诊断 bit `8388608` 启用，没有提高每帧射线预算。

## 原因与实现

修复 covered pixel history 后，连续 Sobol 的独立 2,048 帧验证仍出现 Hybrid 地面 +2.466%、SDF Only 地面 +1.811% 的最终能量差。单纯降低时间方差不能作为启用依据。自适应采样分布依赖先前同序列的结果，是需要隔离的相关性来源；该实验不假定它是所有能量差的唯一原因。

- 每个探针使用 32 帧的 Sobol net。块内固定重要性分区，块间重新生成 digital shift；探针按自身 hash 错开块边界。
- 分区在使用该块随机偏移之前构造，继续完整覆盖余弦半球并使用原逆 PDF。每帧重新追踪当前几何和光照；不会把旧辐亮度当作新命中，也不重投影分区。
- 新增独立的 camera-owned proposal 双缓冲。失去历史且尚未到块边界时，使用固定余弦分区，避免在同一块中重新自适应。
- Screen Trace 与 SDF Gather 使用同一份分区纹理和采样函数。方向缓存、材质积分、覆盖率、历史拒绝及 À-trous 参数保持一致。
- 沿用现有 Guide dispatch；块内仅复制分区，没有新增 GPU pass。1080p 自适应探针下，RGBA16F 双缓冲描述符预算为 2,088,960 B（1.992 MiB / camera）；活跃时替代原 0.996 MiB transient guide，净增约 0.996 MiB。关闭实验后，已有 history 由 camera history owner 保留并负责释放。

## 已完成的验证

生产采样 helper 的 6,144 个 GPU 样本与独立 CPU 方向数实现逐项一致。连续 Sobol 的 16×16 分层与分块 Sobol 的 8×4 分层均通过，块间没有重复短循环，诊断 shader 无编译消息。

默认 flags=0 的 8,970 份 GPU 数据与修改前逐字节一致，包含静止与运动、命中、SH、像素历史和最终输出。新分支未改变默认结果。

冻结相同的 8 阶 Surface Cache（每批 64 样本、32 批），每组预热 512 帧后测量连续 256 帧：

| 路径 | 天花板最终时间方差下降 | 地面 | 右墙 |
|---|---:|---:|---:|
| Hybrid | 85.94% | 75.14% | 80.21% |
| SDF Only | 80.37% | 66.86% | 82.00% |

这里是时间方差下降，不能等同于 RMS 的同等百分比下降，也没有把白噪声参考作为物理真值。

静止 Hybrid、静止 SDF Only、固定往返轨迹分别检查了 41,500 个块内探针帧，实际分区纹理保持逐字节不变；边界更新次数为 924 / 936 / 1,202。每帧分区均无重叠或缺口，逆 PDF 质量和保持 8。

固定轨迹下 A/B 几何与 VP 逐位一致，28,104 个明确新露出样本均未继承 age>1 的旧像素历史；Temporal 相对当前 Resolve 的 P99/max 均为 0。同姿态往返差值 RMS 从 0.656511 降为 0.455109，绝对 P99 从 2.22659 降为 1.67166。这个检查不代表所有探针层历史或动态物体已验证。

## 独立长序列能量门槛

独立帧相位的 2,048 帧长序列结果：

| 路径 / 区域 | 最终均值变化 | block-128 条件 95% 区间 | ±1% 检查 |
|---|---:|---:|---|
| Hybrid / 天花板 | −0.211% | −0.452～+0.003% | 通过 |
| Hybrid / 地面 | +0.636% | −0.329～+1.556% | 未证实 |
| Hybrid / 右墙 | −0.528% | −0.865～−0.184% | 通过 |
| SDF Only / 天花板 | −0.332% | −0.576～−0.098% | 通过 |
| SDF Only / 地面 | +0.277% | −0.724～+1.259% | 未证实 |
| SDF Only / 右墙 | −0.368% | −0.655～−0.060% | 通过 |

六组点估计都在 ±1% 内，但两个地面区间还不够窄，不能仅根据这一批宣布能量等价。随后执行预先设定的独立相位 15,731、每组 8,192 帧四组确认实验，保留相同 ±1% 点估计及区间门槛。

8,192 帧确认实验的四组全部完成，VP 逐位相同，源缓存首尾和四组之间的 SHA-256 相同；六个区域均通过原门槛：

| 路径 / 区域 | 最终均值变化 | block-128 条件 95% 区间 |
|---|---:|---:|
| Hybrid / 天花板 | −0.075% | −0.203～+0.049% |
| Hybrid / 地面 | +0.344% | +0.017～+0.672% |
| Hybrid / 右墙 | −0.071% | −0.309～+0.163% |
| SDF Only / 天花板 | −0.003% | −0.137～+0.136% |
| SDF Only / 地面 | +0.007% | −0.422～+0.420% |
| SDF Only / 右墙 | +0.162% | −0.072～+0.383% |

这是本机位、冻结源条件下对当前滤波后白噪声参考的验证，不是物理真值或跨场景保证。默认行为继续保持不变，仍需 Sponza 和有效 GPU 计时。

## 光照变化与资源验证

相机独立 proposal history 在预热后连续轮转 4,096 次：当前线程托管分配 0 B，两张 owned texture 保持不变且不互相别名。测量范围仅为 CameraHistory 获取/轮转，不包含 RenderGraph 注册或整个渲染帧。

纯自发光保持同机位、相同采样相位，分别在第 32 个测量帧升至 4 倍、降至 1/4 和关灯。每组预热 512 帧，测量 256 帧；实际 GPU 材质发光输入及相机矩阵配对一致。三块区域的 90% 响应均在改变后的首帧达到，且连续保持四帧；分块路径没有增加响应帧数。升光首帧响应为 99.02% / 99.04% / 98.11%，32 帧内的最大过冲为 0.32%；降光最大过冲为 0.49%。关灯后三块区域的最终输出均为 0，未检测到旧光照残留。

这些短序列的末 64 帧不能替代长期能量验证：例如降光地面均值相对白噪声为 −2.35%，当前 Resolve 已有相应差异；没有把这一结果称为动态情况下的能量等价。分块路径的 Temporal 相对当前 Resolve 的末段偏差在 ±0.50% 内。

额外执行顶部发光关闭、常量 HDRI 开启的方向分布切换。两组最终三块区域均在请求帧达到 90%，末段最终均值差为 −0.17% / +0.48% / +0.14%。但 CPU 天空辐亮度 bound 的就绪时间相差 3 帧，且它不等于 GPU 天空纹理的发布时间；因此本批不能作为严格的源延迟 A/B。分块天花板首 32 帧过冲约 7.14%，白噪声约 2.42%，仍需在匹配天空就绪条件下复验，不能据此启用默认。

实际分区检查继续拒绝块内自适应重建。光照使方向历史失效时，只允许 shader 已定义的固定余弦分区重置，仍检查完整半球支持及逆 PDF 质量和；静态验证不放宽任何重置条件。

## 计时与剩余检查

首次无纹理回读的 GPU ABBA 计时不可用：原始数据包含约 `1.794e12 ms` 的未校准时间戳及跨大量脚本帧不更新的旧值。分析器拒绝没有有效 profiler publication index、计数不为 1、数据不新鲜的记录。显式启用 GPU profiler publication 的复验仍无有效 GPU sample：1,024 条记录均为零；开启 Editor profiling 后 publication index 前进，但 GPU sample 仍为零。所有临时 profiler 设置均已恢复。

本会话为 Edit Mode、DX12、Graphics Jobs 开启。[Unity 7000 文档](https://docs.unity.com/en-us/engine/7000.0/manual/analysis/graphics-performance-profiling/profile-rendering/profiler-gpu) 限定 GPU Profiler 用于 Play Mode / Player，且 Graphics Jobs 开启时仅支持 Vulkan。未为计时改变项目配置或启动另一 Editor。PIX 插件和 analyzer 已安装，预检的 `ERROR_MOD_NOT_FOUND` 来自当前进程未注入 **WinPixGpuCapturer.dll**，不是 VividPixCapture 插件文件缺失。源码检查确认此模块必须在 D3D12 device 创建前注入，尚未捕获。另在新的 owned build 目录重建工具：native CTest、35 项 session、24 项 M1、31 项 M2 独立检查通过；没有部署覆盖已加载 DLL，也没有重启 Editor。**没有有效 GPU 耗时结果，不能宣称 guide 或 GI 性能改善。**

相机独立 history 分配、8,192 帧能量确认和纯发光响应检查已完成。有效 GPU 计时、匹配天空就绪条件的切换复验以及 Sponza 运动稳定性/独立侧廊验证仍未完成。Editor 运行期间未启动 Unity Test Framework。目标“稳定无噪点并对齐 Lumen”尚未达到。

## Sponza 扩展验证：静止降噪，运动收益未证实

2026-11-14，运行时实现仍为 `28745d84`（之后 `0a268a52` 仅增加验证工具和记录）。通过 Unity API 临时以 Additive 载入 Sponza Noon 及其照明场景，使用原 `Camera_1`，保留 1920×1080、16 px / 8 rays、相同采样相位。临时 Profile 使用原生 Gather、Adaptive 5×5、spatial anchor、32 帧像素历史和两轮 À-trous；关闭相机 AA、使用固定 EV10，避免显示 AA 混入 GI 测量。原资产均未保存修改。

先等待 2 阶 Radiosity、64 samples / batch、32 batches 收敛，再为四组分别预热 512 帧、连续采集 256 帧。四组源缓存首尾 SHA-256 完全相同；静止和运动的 A/B VP 逐位一致。静止 ROI 的几何在组内、组间均逐位相同。

| 静止区域（底部原点） | 最终时间方差下降 | 短序列最终均值变化 |
|---|---:|---:|
| 左侧 (430,300)，160×240 | 67.71% | −0.049% |
| 右侧 (1330,300)，160×240 | 69.92% | −0.221% |
| 中部 (880,380)，160×160 | 70.62% | +0.653% |
| 左侧几何边缘三像素带 | 55.36% | −0.435% |
| 右侧几何边缘三像素带 | 58.85% | −0.407% |

边缘按深度/法线不连续生成，不等同于精确的帘子材质掩码；这些统计不证明跨帘串色已消失，也不是 Sponza 长序列能量等价验证。中部 ROI 没有几何边缘样本，不输出空掩码的误导统计。

轨迹为当前 Camera_1 附近两次 128 帧正弦往返（横移 0.28 m、前后 0.06 m、yaw −7°）。这是本轮明确记录的新轨迹，没有冒充原始历史数据中的缺失轨迹。A/B 深度逐位相同，但全部 256 帧中存在 21 个像素帧的法线编码差异，单帧最多一个；原因尚未定位。重复机位差值仅采用两组、两周期中几何编码完全相同的交集：35,327,985 个像素样本，排除 15 个。

| 固定往返 | 白噪声 | 分块采样 |
|---|---:|---:|
| 同姿态差值 RMS，线性 consumed E/π | 238.269 | 244.199 |
| 同姿态绝对差值 P99 | 1050.356 | 1090.727 |
| 几何诊断带中 age>1 的比例 | 1.451% | 1.435% |

RMS 点估计增加 2.49%、P99 增加 3.84%；本批未建立运动收益，也未测得该差异的独立长序列置信区间。几何诊断带使用前景深度、双法线平面差 >0.075 m、位置差 >0.10 m 和当前非零覆盖筛选，A/B 样本数分别为 333,658 / 333,172。这不是逐射线遮挡真值，且阈值并不完全等于 GPU 四 tap 历史拒绝规则；其 P99 不能直接解读为物理照明误差或严格配对抗鬼影结论。下一步应记录实际 motion 与四 tap 重投影条件，分离误分类、历史继承和当前帧 Gather 方差，再改动拒绝/重建规则。

实际 proposal 纹理在静止/运动中各检查 94,856 个块内探针帧，均未改变，没有固定余弦重置；分区支持和逆 PDF 质量和通过。源缓存固定、静止收益明确仍不足以覆盖运动边界，**本候选继续保留为诊断选项，默认不变**。

捕帧结束后原两个 Cornell 场景仍加载且未脏，活动相机恢复 Emissive Cornell Camera，Sponza 的两个自有临时场景已关闭；hooks 为空、flags=0、frame override=-1。没有修改共享 graph。

## 数据与复现

```text
E:/VividRP_GI_Evidence/BlockedTemporalValidation-20261113-151050-565/gpu-validation.json
E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/default-baseline-equivalence.json
E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/blocked/capture.json
E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/analysis.json
E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/reference-blocked/capture.json
E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/energy-reference.json
E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/blocked-sampling-quality.png
E:/VividRP_GI_Evidence/BlockedTemporalGpu-20261113-152436-253/analysis.json
E:/VividRP_GI_Evidence/BlockedTemporalConfirmation-20261113/reference-confirm-blocked/
E:/VividRP_GI_Evidence/BlockedTemporalConfirmation-20261113/hybrid-confirmation-partial.json
E:/VividRP_GI_Evidence/BlockedTemporalConfirmation-20261113/confirmation.json
E:/VividRP_GI_Evidence/ProposalHistoryValidation-20261113-162559-238/history-validation.json
E:/VividRP_GI_Evidence/BlockedTemporalRelight-20261113-164900-745/temporal-relight-analysis.json
E:/VividRP_GI_Evidence/BlockedTemporalSkyShift-20261113-165728-395/skyshift-analysis.json
E:/VividRP_GI_Evidence/BlockedTemporalGpu-20261113-162643-462/analysis.json
E:/VividRP_GI_Evidence/GpuPublicationProbe-20261113-163823-415/samples.json
E:/VividRP_GI_Evidence/SponzaBlockedSampling-20261113-171221-274/capture/capture.json
E:/VividRP_GI_Evidence/SponzaBlockedSampling-20261113-171221-274/analysis.json
E:/VividRP_GI_Evidence/SponzaBlockedSampling-20261113-171221-274/geometry-diagnostic.json
E:/VividRP_GI_Evidence/SponzaBlockedSampling-20261113-171221-274/sponza-blocked-quality.png
```

`TemporalSamplingCapture.cs` 的 `blocked` 模式采集六组短序列，`reference-blocked` 采集两组 2,048 帧候选，`reference-confirm-blocked` 采集四组 8,192 帧确认数据。使用新目录重跑，脚本拒绝覆盖；`cancel.txt` 可请求结束并恢复现场。参考阶段只读回最终区域及首尾源缓存，减少诊断 I/O，渲染算法与逐帧采样预算相同。

```powershell
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/TemporalSamplingValidation.cs --timeout 180 --format json
python -B Tools~/Diagnostics/TemporalSamplingExperiment.py E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113/blocked --candidate blocked --linear E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113/linear
python -B Tools~/Diagnostics/TemporalSamplingExperiment.py E:/VividRP_GI_Evidence/BlockedTemporalSampling-20261113 --reference-only --candidate blocked --control-root E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113 --linear E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113/linear
python -B Tools~/Diagnostics/TemporalSamplingConfirmation.py E:/VividRP_GI_Evidence/BlockedTemporalConfirmation-20261113/reference-confirm-blocked --linear E:/VividRP_GI_Evidence/TemporalSamplingCovered-20261113/linear
```
