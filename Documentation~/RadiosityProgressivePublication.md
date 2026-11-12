# Radiosity 渐进发布、输入死区与动态预算

2026-11-11，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti。实现覆盖固定采样与持续样本累积两条 Radiosity 路径，原生 Gather 维持 **16 px、每探针 8 条射线**。

## 机制与默认行为

新增三个 Volume 参数：

- `surfaceCacheProgressiveRadiosity=true`：把发布缓存与工作阶分开；重建期间保留身份兼容的已发布 incident lighting。
- `surfaceCacheLightingTolerance=0.005`：默认 **0.5% RGB 相对能量死区**，可调至 1%，0 恢复精确能量比较。
- `surfaceCacheUpdateBoost=4`：重建首批和快速相机运动期间最多 **4 倍**卡片预算，1 关闭提升。

持续样本累积仍由现有 `surfaceCacheRadiosityAccumulate` 控制，默认 false；本轮没有改变其默认值或编辑场景 / Profile 资产。累积实验开启该选项，验证渐进发布与独立样本批次配合。

### 工作结果与发布结果分离

固定模式继续用两张工作 atlas；渐进模式增加独立发布 atlas。累积模式继续每阶独立 atlas 加一张发布 atlas。**新一阶始终读取工作缓存的上一阶，不能把保留的旧高阶发布缓存作为新一阶输入**，避免反弹阶数和能量反馈错误。

冷启动先清理未初始化的发布纹理，再按卡片发布已经完成的低阶结果。较大光照变化保留兼容旧缓存，收到新低阶卡片时混合替换；降光 / 升光实验中为 **75% 新结果 + 25% 旧结果**。新目标阶卡片强制采用 100% 新结果，旧贡献有明确结束点。后续独立采样批次只在完整目标阶扫描结束时发布，不重新混入低阶。

退火由新结果到达驱动，没有每帧把整张辐亮度乘以 0.75。后者在大场景新数据尚未生成时会再次把缓存衰减到黑。

### 小幅变化保留高阶，避免能量锯齿

实验发现，对缓慢光照变化也不断混入新低阶估计，会因为缺少高阶反弹造成约 **9.35%** 的周期性降亮。因此，已有完整高阶缓存、输入变化较小且身份兼容时，先保留高阶，等新目标阶卡片到达后逐卡片替换。

本轮小变化层允许相对光能 / 阴影变化最多 5%；方向仍限制在约 0.1°。这不是把默认失效死区提高到 5%：超过默认 0.5% 仍启动重建，只在正在构建的首批中容忍小幅变化，让它完成目标阶。保留的输入基准不随容忍帧更新，所以待处理变化会在当前目标阶完成后再次触发刷新。

几何分开记录两类版本：

- `RadiosityLayoutVersion`：完整比较卡片实体、slot、SDF 字段版本、局部卡片范围 / atlas 地址。移除、重排、重烘焙或不兼容布局会清理发布缓存。
- `RadiosityDiscontinuityVersion`：区别材质 / 字段 / 较大变换和小平移。当前小平移限定 **单次不超过 5 cm、基向量不变、layer 不变**；不把旋转、缩放或材质变化当作微小平移。

大变换仍可在卡片身份一致时暂用旧 incident lighting，但立即走较快低阶替换；拓扑不兼容时不复用别的卡片 texel。这里只缓和 Radiosity 生命周期；真实几何变化仍按原规则拒绝 Gather 几何历史，避免放宽遮挡验证。

### 输入基准与预算

Radiosity 比较上次接受的输入快照，而不是每帧把“当前值”升级为比较基准。这样 0.1% / 帧的缓慢变化不会永远停留在死区。Gather / Screen Hit 使用相同能量容差，环境曝光和 tint 使用相对比较；天空内容 hash、旋转、卡片身份等离散变化仍显式失效。

重建首批期间维持预算提升，完成后恢复；相机单帧位移超过 0.25 m 或转向超过 5° 时提升 8 帧。完整小场景更新和剩余卡片限制仍生效。预算变化不重置 cursor，乘法有界，4096 卡片末尾不会越界。

## 受控光照阶跃

Cornell Bright 原机位，1920×1080，固定 EV100=8，关闭 AA / Local Exposure，太阳暂设为恒定光色。8 个反弹阶，**64 rays / texel / batch，最多 16 批**；对照同一 SDF / 卡片求解器的 **4096-ray** 参考。保留空间 anchor、Adaptive 5×5、2-pass À-trous 和像素历史上限 16。屏幕方向引导与 Far Cache 关闭。

每组先预热 256 帧，在测量第 64 帧改变灯光。表中误差相对各组后段 64 帧天花板 GI 平均 Y；是实际最终 GI 缓冲，不是 CPU 计数器，也不是独立路径追踪真值。旧 / 新保持相同 Gather、相机与随机帧序列。

| 观测项 | 旧路径 | 渐进路径 |
|---|---:|---:|
| 3600 → 1800 lux，全量卡片，首帧缺口 | −26.86% | **−6.29%** |
| 同组前 32 帧平均绝对偏差 | 6.82% | **2.68%** |
| 连续 8 帧进入后段 ±10% 的起点 | 第 9 帧 | **变化当帧** |
| 分批更新，首帧缺口 | −27.35% | **−4.99%** |
| 分批更新，首份完整新 8 阶结果 | 第 40 帧 | **第 16 帧** |
| 1800 → 3600 lux，全量，首帧缺口 | −26.86% | **−14.21%** |
| 升光后连续 8 帧进入 ±10% 的起点 | 第 9 帧 | **第 3 帧** |

全量组每帧 37 张卡片。分批组基础预算 8，新路径首批实际使用 **32 / 5**，旧路径 **8 / 8 / 8 / 8 / 5**。前者依然逐卡片保留旧缓存，不会等待整轮 8 阶才提供新照明。

**大幅升光仍有约 14% 的首帧不足。** 低阶估计尚不具备完整高阶能量，且 Gather 仍有稀疏采样与时间响应；本轮没有用额外增益补偿该缺口。新路径前 32 帧没有高于后段均值的过冲，但不能据此保证其他场景无过冲。

同样本批次的源数据验证：降光全量与升光全量的 **16 批**、分批组共同完成的 **6 批**，有效卡片内容均逐位一致。全量组最终相对 4096-ray 参考的平均能量误差为 **+0.318%**，新旧相同；最终像素后段均值变化约 **−0.29% / −0.31%**。分批组新旧在同一时刻完成的批次不同，不能把其后段均值差当作纯能量偏差。

关灯控制：3600 → 0 lux，变化后第 **2 帧**开始连续 8 帧低于原 GI 均值的 5%；Surface Cache 第 **8 帧**全部归零。末段 32 帧 GI 均值为 0.0217，相对变化前 77.8094 约 **0.028%**，仍有 Gather 历史尾部，未声称逐像素立即归零。

大变化数据位于 `capture-final/`，源快照在 `large-change-source/`。之后的小幅变化保阶修复不改变这些大变化分支；另用最终程序集的 `final-large-qa/` 复核降光及源能量一致性。

最终程序集复核得到首帧缺口 **−26.8626% → −6.2927%**，后段均值变化 **−0.2876%**，最终有效卡片逐位相同。

## 九组输入与几何控制

- **±0.2% 灯光抖动，128 帧**：精确比较组 127 帧重启到工作阶 0；新路径保持工作阶 8、卡片更新 0，Gather 历史 128 帧有效，atlas 首尾逐位一致。
- **真实大气相机轨迹，128 帧**：开启太阳 `interactsWithSky`，沿既有 0.28 m / 0.06 m / yaw −7° 正弦轨迹，世界高度不变。实际光色 R 在 **2959.2573–2959.2600** 间变化；没有重置 Radiosity，128 帧 Gather 历史有效，atlas 首尾逐位一致。
- **0.1% / 帧缓慢升光**：第 6 帧退出接受基准的死区，后续保持待更新输入；128 帧中完成 **15 次**新 8 阶刷新，没有阶数饥饿。相对固定采样线性缩放预测的源均值偏差范围 **−1.186%–0%**，平均 **−0.391%**；前一版逐低阶混入为 −9.354%–0%、平均 −5.272%。这项是同求解器线性预测，不是独立物理真值。
- **2 cm 单次 / 连续微动**：实际 Scene Revision 分别发生 1 次 / 多次改变，布局及 discontinuity 版本不变，已发布阶始终保留 8，没有清理。真实变换通过 renderer 数据库同步后才计入验证。
- **20 cm 位移**：discontinuity 改变，布局保持；走快速重建，不误用微动保阶。
- **移除 / 恢复卡片**：第 16 / 48 帧布局版本改变并清理发布 atlas，避免旧 texel 绑定到其他卡片。
- **快速相机转向**：第 16–23 / 48–55 帧暂时提高到最多 16 卡片，平稳时恢复 4；所有帧同时遵守剩余卡片限制。

## 检查、成本和限制

34 个相关 shader kernel 的 DXIL / SPIR-V 编译通过；实际 D3D12 捕获成功。新增纯函数边界 / 分配回归用例放在现有 `SdfRadiosityBudgetTests.cs`；Editor 正在运行，未启动 Unity Test Framework，需在关闭 Editor 后手动执行。

实际程序集的容差、预算边界、小变换分类检查通过。输入匹配、预算、颜色容差、变换分类及稳定卡片准备的委托与输入均在测量前准备，预热后联合重复 **4096 次，0 B 托管分配**；这不是全帧 GC 测量。最终 ShaderUtil 检查六个相关 compute shader，无 Error / Warning。临时 Profile、面板位置 / 激活状态、相机、3600 lux、太阳 `interactsWithSky=true` 与全部诊断回调 / flags 已恢复。

固定模式的渐进发布增加一张 RGBA16F atlas：Cornell 为 **24 KiB**，4096 张卡片描述符为 **2 MiB**；累积模式已经有发布 atlas，不增加这张纹理。卡片身份使用复用的 CPU scratch 列表，稳定帧不重建身份表。上述是描述符预算，不是驱动显存测量。

GPU 毫秒开销未测量；4 倍预算提高了瞬时追踪工作，不能等同于耗时无回退。未验证 Sponza 或大规模流式拓扑变化。几何连续微动仍按原有 Scene Revision 规则拒绝 Gather 历史，本轮保留的是 Radiosity 缓存。

本机 UE 5.7.4 的 `Lumen/Radiosity/LumenRadiosity.usf` 以有限帧历史融合更新 texel，也有无效 texel / 历史失效条件；不把“绝不会失效”作为对齐承诺。[Epic 的缓存传播说明](https://dev.epicgames.com/documentation/en-us/unreal-engine/lumen-global-illumination-and-reflections-in-unreal-engine)也明确说明，大范围光照变化有传播延迟，提高更新速度会增加 GPU 成本。

## 复现与证据

`E:/VividRP_Reborn/Logs/RadiosityProgressive-20261111/` 保存实际 GPU 数据、逐帧计数、曲线、编译记录与脚本。

- `capture-final/`、`analysis-final/`：升光 / 降光 / 关灯与高样本参考。
- `controls-best/`、`controls-analysis/`：最终九组控制与严格断言。
- `smoke/`、`capture/`、`controls/`、`controls-final/` 保留中间版本。首轮几何控制没有触发数据库更新，未当作有效几何验证；中间低阶锯齿与饥饿问题也未隐藏。
- `final-large-qa/`、`editor-verification.json`、`scripts/`、`source/`：最终程序集回归、恢复 / 分配检查和源码快照。

```powershell
unity command script eval --project-path E:/VividRP_Reborn `
  --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/RadiosityProgressiveCapture.cs `
  --timeout 1800 --format json
unity command script eval --project-path E:/VividRP_Reborn `
  --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/RadiosityProgressiveControls.cs `
  --timeout 1800 --format json
python Tools~/Diagnostics/RadiosityProgressiveExperiment.py `
  E:/VividRP_Reborn/Logs/RadiosityProgressive-20261111/capture-final `
  E:/VividRP_Reborn/Logs/RadiosityProgressive-20261111/analysis-final
python Tools~/Diagnostics/RadiosityProgressiveControls.py `
  E:/VividRP_Reborn/Logs/RadiosityProgressive-20261111/controls-best `
  E:/VividRP_Reborn/Logs/RadiosityProgressive-20261111/controls-analysis
```
