# 同帧私有颜色历史与稳定像素裁剪实验

本轮建立了完整的私有像素历史重放，解决前轮顺序 A/B 的上游采样输入不一致问题。基线和候选共享同一帧的 Resolve、几何、Motion 与矩阵，各自持有颜色、矩和几何的 ping-pong 历史；候选不回写渲染器，不改变下帧采样引导。

**稳定像素不做弱裁剪的候选通过两组静态初筛与一次固定轨迹的新露出历史检查，但未启用默认行为。** 全图仍有低频杂斑，原高采样 Hybrid 联合参考仍未通过，GPU 增量开销尚未测量，不能宣称达到 Lumen 质量。生产 Runtime、shader、场景与 Profile 均未修改。

证据：`E:/VividRP_GI_Evidence/ScreenProbePrivateColorHistory-20261116-061000`。起点 `fb33d578`，生产渲染仍为 `7b9d8836`。Unity 7000.0.0a16 / CoreRP 17.7 / D3D12 / RTX5070Ti。Cornell 顶部自发光夹具，固定原机位、1920×1080、16 px / 每探针 8 rays、原生 Gather、4 轮 À-trous、FP32 anchor age512、像素历史上限32。Radiosity 固定8阶 / 64样本 / 32批，源 atlas hash 仍为 `40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。

## 重放可信度与元数据契约

`EditorGatherStageCapture` 的 index1 在原生 Temporal 后、原始 Resolve 被空间滤波复用前运行私有 Temporal。`EditorAtrousDispatchCapture` 的 index3 对比原生与私有基线四轮 wavelet 的最终输出。第一帧从相同的原生 previous 颜色、矩和几何初始化；三个历史均由各自路径独立递推。冻结源失效、A/A 不一致或读回预算超限均直接失败并在 finally 恢复状态。

`qualification` 使用完全相同的基线/候选 shader，1024帧预热、128帧测量。Temporal、颜色历史、矩、几何、最终输出的 **23,700 对 native/private-baseline hash 全部逐字一致**；候选也相同。其中每个重放帧检查四个 ROI，每个测量帧及关键预热帧检查全图。后续静态实验分别重复这个严格基线检查；独立序列256帧实验为26,900对。候选未保存的全图像素不作为其质量证明。

最初的验证器错误地要求候选与基线的后续年龄始终相同。`RectifyPixelHistory` 通过 `inout fastUpdate` 根据 RGB 连贯变化调整响应，原生累计公式会随之改变颜色与矩年龄；独立 RGB 历史可以合法地产生不同后续状态。因此改为：native/private-baseline 仍须完全一致；候选几何须完全一致；一次反馈发布本身必须保留颜色 alpha 以及矩的方差/年龄/coverage。

`feedback-witness` 保存前32帧发布前后的原始数据，直接改动这些受保护通道的数量为零。后续 `wavelet` 还对完整1152帧发布做了相同检查。原错误报告保存在 `analysis-cross-path-age-failure.json`，修正理由与源行号保存在根目录 `validation-correction.json`。这项更正没有修改方差或能量门槛。旧的部分发布见证不会自动被判为完整质量验收通过。

## 同帧反馈结果

事前初筛门槛仍是天花板最终像素时域方差下降至少5%、四个 ROI 平均消耗能量变化均不超过1%。各路径最终量均为 `RGB × coverage` 的场景线性 GI；当前受测设置没有 post-wavelet 近场遮挡。

| 私有候选 | 天花板方差 | 天花板平均能量 | 结论 |
| --- | ---: | ---: | --- |
| 对称守恒交换，同步矩均值，64帧见证 | 上升46.02% | −1.842% | 拒绝，且不具备全序列发布见证 |
| 第2轮 wavelet RGB 写回，128帧 | 下降28.42% | −3.101% | 完整发布检查通过，能量失败 |
| 稳定像素不做弱裁剪，128帧 | 下降9.08% | −0.156% | 初筛通过 |
| 同一裁剪候选，独立9481序列、256帧 | 下降24.76% | +0.381% | 初筛通过 |

普通 wavelet 写回仍造成变暗，受控结果证实它不能作为默认降噪方案。对称交换的天花板方差实际回退，不能用前轮顺序 A/B 的表面收益推广。

## 本机 Unreal 源码与候选边界

检查的是本机 UE **5.7.4**（`E:/UnrealEngine/Engine/Build/Build.version`）：

- `LumenScreenProbeFiltering.cpp:550` 把 composited radiance 存入探针历史；`:595` 将空间滤波后的 radiance 另存为重要性采样历史。两者用途不同，不能据此推导像素颜色历史应反复写入空间滤波结果。
- `LumenScreenProbeGather.cpp:1986` 将 `NewDiffuseIndirect` 的时域输出提取为历史。
- `LumenScreenProbeGatherTemporal.usf:591` 在相应邻域裁剪模式下，仅当 `FastUpdateModeAmount > 0` 执行颜色邻域裁剪。

VividRP 目前在稳定像素上仍以0.1权重向本帧 YCoCg 邻域边界裁剪。候选只在完成原来的连贯光照变化检测后，若 `fastUpdate == 0` 则保留旧颜色；反应性像素沿用全部原逻辑。所有容差、重投影、矩更新和最大年龄保留。本轮仅通过内存 candidate shader 修改私有路径，是针对差异的实验，不是 UE 逐行移植，也未改变 ReSTIR 默认路径。

两个序列中，四 ROI 平均能量变化分别小于0.18%和0.53%。独立序列背墙绝对方差小幅上升0.86%，归一化方差下降0.19%；不能描述成所有区域方差均下降。有限窗口的 block bootstrap 仅描述当前采样的条件离散程度，不是物理误差界限。

## 移动与全图观察

`stable-motion` 保持1024帧静止预热，随后走128帧固定轨迹：横移 `0.28 sin(t)` 米、前移 `0.06 sin(t)` 米、yaw `−7 sin(t)` 度。记录原始 Resolve、独立 Temporal/颜色历史/像素几何/最终输出，以及共享 VP；所有测量帧仍检查全图 native/private-baseline A/A，共6,420对通过。

板边 ROI 中保守识别出 **3,894个明确新露出样本、分布于73帧**。两条路径旧年龄占比与历史接受占比均为零，Temporal 减当前 Resolve 的 P99/最大差均为零。最终输出减 Resolve 的 P99 为2.2195 / 2.2386、最大差4.6904 / 4.7036；该量含空间滤波，不是纯历史拖影指标，当前 Resolve 也不是无噪声真值。

`stable-motion/visual-review.png` 展示同一帧、相同线性显示尺度的全图基线、候选和差值。仍可见墙面和地面的低频杂斑，局部时域方差改善不等于固定空间误差消失。这里只完成一次轨迹，不推广到任意运动、动态光照或 Sponza。

## 下一步与运行恢复

保留完整 `qualification`、`stable-unclamped`、`stable-holdout`、`stable-motion` 数据。候选下一步仍需：

1. 建立与实际接收几何和 Hybrid 输入一致的高样本能量参考，检查稳定均值和板边偏差；原联合参考失败继续保留。
2. 在合格能量检查后测真实生产 dispatch 的 GPU 开销。私有重放的额外 dispatch / readback 工作量不能代表生产性能。
3. 继续固定轨迹复验和动态光照响应，确认长期历史没有引入残留；再决定是否安装到默认原生 Gather。

本轮103项 Python 诊断检查通过，内存 DX12 shader 编译、实际 GPU 读回、源码/脚本/输入指纹核验通过；活动 Editor 中没有运行 Unity Test Framework。移动实验在 finally 恢复机位并清理原生相机历史，随后运行1024个正常生产帧。最后核对正常16/8、8阶/32批、age512/FP32、4-pass、原 Hybrid/Temporal/wavelet shader 程序 hash，场景/Profile 干净、回调清空、Frame Debugger 关闭、PIX idle，Editor 播放暂停。

用户已授权定期清理生成证据。归档的是已拒绝或被受控重放取代的实验中间 `.bin`；源码、协议、报告、聚合、删除 hash、首末帧和指定见证保留，完整序列重分析的缺失明确写入 `raw-retention.json`。本轮释放量、当前目录大小和继承旧清单的总量估计见 `post-experiment-storage.json`，没有删除接受的生产基线或高样本参考。

本轮共释放922,710,528字节（约880 MiB）；当前新目录约839.5 MiB，结合继承清单估计证据总量124.68 GiB，E盘剩余约111.99 GiB，未触及125 / 64 GiB审查线。当前目录扫描不跟随链接；较早目录的总量来自上次清单，不冒充全盘重新测量。

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeColorHistoryReplay.py NEW_STATIC --method stable-unclamped --sequence 1733 --measured 128
python -B Tools~/Diagnostics/BuildScreenProbeColorHistoryMotion.py NEW_MOTION --sequence 1733
python -B Tools~/Diagnostics/AnalyzeScreenProbeColorHistoryReplay.py COMPLETE_STATIC
python -B Tools~/Diagnostics/AnalyzeScreenProbeColorHistoryMotion.py COMPLETE_MOTION
python -B -m unittest discover -s Tools~/Diagnostics -p 'test_*.py'
```

执行前固定实际脚本和输入，结束立即核验源码。工具拒绝覆盖既有目录，单次二进制读回上限256 MiB。`none` 是资格检查模式，其余均为显式诊断选项，不代表生产默认设置。
