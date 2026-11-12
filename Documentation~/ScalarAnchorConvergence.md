# Cornell Box：Scalar Anchor 时空收敛

2026-11-11，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti。原生 Screen Probe Gather 保持 **16 px 网格、每探针 8 条射线**；本轮只推进 scalar anchor 的时间收敛和重建边界保护，沿用此前逐方向历史与 SH 重构。

## 实现与能量约束

原固定 blend=0.85 改为带几何置信度的有效样本 Age。新增相机所属的双缓冲 RGBA16F 统计纹理，保存 Age、接受样本的平均世界位移、实际 blend 和拒绝类别。

- 通过历史探针接收面的深度、法线、距离验证，再在 covered RGB / coverage 上累积；没有依赖当前亮度的裁剪或增益补偿。
- 世界位置稳定且前一帧全分辨率几何证明确实可见时，Age 上限为 32；位移增大时连续退到 8。历史置信度衰减有效 Age，blend 为 `acceptedAge / (1 + acceptedAge)`。
- 以前一帧全分辨率 depth / normal 的四个双线性 tap 验证可见性。新露出或前景遮挡混合的接收点清空 anchor 历史，并禁止它与成熟历史 anchor 的空间交换。像素时域拒绝不能代替这层校验。
- 没有全分辨率几何历史时使用独立 kernel，只做探针几何重投影，Age 上限保持 8。关闭空间 anchor 时仍使用新时域累积。
- 继续使用已有对称 5×5 空间通量重建，只用于 SH 校准目标，不把空间平滑结果反馈进下一帧时间历史。新露出探针保留当前帧估计。
- 继续对反馈 half 显式执行 nearest-even，避免本机 D3D12 half UAV 截断造成长期能量损失。

这些规则适配 VividRP 的 scalar 校准目标，不是 Unreal 源码逐行移植。Editor-only 对照 bit：8192 恢复原 scalar blend；16384 禁用新全分辨率可见性校验；32768 输出当前帧 scalar，供固定机位 Monte Carlo 参考。正常 flags=0 启用完整实现。

## 固定机位结果

Cornell Bright 原机位，1920×1080，太阳 3600 lux、固定 EV100=8，关闭 AA / Local Exposure。Radiosity 固定 **8 阶、4096 射线 / texel、seed 0**，关闭连续采样累积，测量时更新卡片数为 0。各组与既有高样本参考的 37 张有效卡片内容逐位一致。

空间 anchor、Adaptive 5×5、两轮 À-trous、像素历史上限 16；关闭方向采样引导和 Far Cache，隔离 anchor 改动。每组 512 帧，168 个天花板探针、96×48 天花板像素。比较的是最终 GI 缓冲的 covered 线性 Y，不包含显示曝光 / Tonemapping 的数值变换。

| 指标 | 原 scalar | 新时域、不校验像素可见性 | 完整实现 |
|---|---:|---:|---:|
| 最终像素时间 RMS | 1.634686 | 0.960760 | **0.960604** |
| 最终像素平均 Y | 52.950607 | 52.935731 | **52.934244** |
| 校准后探针平均 Y | 55.462015 | 55.478051 | **55.478343** |
| 新 anchor 有效 Age | 对照仍用 0.85 | 32 | **32** |

完整组最终像素时间方差下降 **65.47%**，RMS 下降 **41.24%**；最终平均亮度变化 **−0.0309%**。配对 64 帧块 bootstrap 的条件 95% 方差下降区间为 **62.68–68.19%**，均值变化区间为 **−0.381–+0.416%**，描述该序列的离散程度，不代表跨场景保证。

独立 1024 帧原始 8-ray scalar 参考的天花板平均 Y 为 **55.491935**；完整组校准后平均误差 **−0.0245%**。时间 anchor 方差下降 **82.10%**，校准后 SH 方差下降 **81.91%**，收益传递到最终像素。静止天花板没有触发可见性拒绝。

## 固定移动轨迹

沿用 128 帧正弦往返轨迹，局部横向 0.28 m、前后 0.06 m，世界高度不变，yaw −7°。临时关闭太阳 `interactsWithSky`，以恒定光色避免源缓存变化；结束恢复原值。移动组与静止大气组分别比较，不跨组比较绝对亮度。

在同一批冻结卡片内依次捕获旧 scalar、关闭像素可见性校验、新实现三条轨迹，以及轨迹第 24 / 48 / 80 / 104 帧的固定机位参考。每个参考累计 512 个当前帧 scalar Resolve（4096 条原始射线 / 探针），使用独立帧序列；关闭 anchor 历史，保持相同空间重建。参考是当前渲染器的 Monte Carlo 估计，不是独立物理真值，标准误的 IID 假设不覆盖所有方向历史相关性。

三组轨迹的 VP、接收几何和有效源卡片逐位相同；四个参考的 VP 与对应轨迹机位逐位相同，源卡片首尾及与轨迹也逐位相同。全部参考均得到 512 个有效帧，未发生缓存失效或丢帧重采。

- 128 帧轨迹的 12,570 个明确新露出像素，涉及 114 帧：各组均无 age>1 的旧像素历史继承，像素 Temporal 相对当前 Resolve 的误差 P99 / max 为 0。
- 明确新露出的 39 次探针样本：完整组全部进入可见性拒绝类别、Age=1。禁用可见性校验的组则全部接受旧 anchor、Age>1。原 scalar 对照没有有效 Age 统计，不用它的诊断占位值判断是否继承历史。
- 完整组拒绝样本的 anchor 与当前原始 scalar 仅剩 half 量化差异，Y RMS=0.0236、最大 0.0758；原 scalar 的差异 RMS=13.0714。这项只是历史继承诊断，原始 8-ray 估计不是低噪声真值。

参考误差区域包括对应帧新露出像素及前三帧重投影后的新露出条带；四个机位分别有 857 / 506 / 15 / 38 个像素，共 **1416** 个。最终 GI 线性 Y 的绝对误差如下：

| 同机位参考误差 | 原 scalar | 新时域、不校验像素可见性 | 完整实现 |
|---|---:|---:|---:|
| 合并 RMSE | 3.11729 | 2.16085 | **2.19890** |
| 合并绝对 P99 | 7.84478 | 5.04833 | **5.13376** |
| 最大绝对误差 | 9.59234 | 5.40010 | **5.68133** |
| 有符号平均误差 | +0.09698 | −0.21022 | **−0.16671** |

四个机位各自的最终 RMSE / P99 / max 均低于原 scalar，没有观察到本轨迹下新增拖影。新时域但不校验可见性的组误差略低，却仍明确继承了不应保留的 anchor，因此不作为正常路径。完整组新露出样本少了历史降噪支持，仍存在当前帧采样噪声；没有把更长时间拖影换成静态 RMS 收益。

参考的平均 IID 标准误 Y 约为 0.447 / 0.450 / 0.169 / 0.173。后两个机位新露出样本较少，不单独据此推断普遍质量。完整指标见本机 `complete-analysis/motion-summary.json`。

## 检查、存储与限制

1920×1080、Adaptive 开启时，新增双缓冲统计纹理描述符预算 **0.249 MiB**。原本关闭空间 anchor 时还会使用约 **0.125 MiB** 的准备纹理；这是资源尺寸预算，不是驱动实测显存。没有增加追踪射线数。

- 32 个相关 kernel 的 DXIL / SPIR-V 编译通过；Unity 实际 D3D12 静止、移动、同机位参考捕获成功。最终 Unity ShaderUtil 检视 5 个 Gather shader，无 Error / Warning。
- 关闭像素时域历史、关闭空间 anchor 两种配置分别实际捕获 32 帧；统计纹理全部有限，天花板 anchor Age 分别稳定在 **8 / 32**，无资源绑定错误。
- 两张新增 / 扩展准备纹理的稳定 Resize 预热后重复 4096 次，**0 B** 托管分配。这只检查描述符更新，不是全帧 GC 测量。
- 原场景和 Profile 资产未编辑；临时 Profile 已撤销，相机位置 / 旋转、3600 lux、`interactsWithSky=true` 与所有 Editor 回调 / flags 已恢复。原 Profile 的 Radiosity 样本仍为 16、连续采样累积默认仍关闭；4096 样本只用于受控实验。
- 首次无像素历史冒烟脚本错误地检查了已关闭功能的像素 Age 参数；已修正诊断脚本后重跑，保留原失败记录，没有修改运行时以绕过该断言。

GPU 耗时未测量。验证范围限 Cornell Box；未证明动态物体、灯光变化、Sponza 或采样引导开启时的行为。未运行 Unity Test Framework，因为 Editor 正在交互运行。

## 证据与复现

本机证据根目录：`E:/VividRP_Reborn/Logs/ScalarAnchor-20261111/`。

- `initial/`、`analysis/`：静止三组 512 帧、阶段回读、能量与方差报告、曲线。
- `complete/`、`complete-analysis/`：同批冻结源的三条 128 帧轨迹和四个同机位参考。
- `motion/`：首轮三条完整轨迹；后续参考因 Radiosity 失效停止，未作为最终误差参考。`references/` 的独立重采虽然内部稳定，但与此前轨迹的卡片索引重排，未混用。
- `fallback-final/`、`editor-verification.json`、`compile/`：两种回退配置、恢复 / 分配检查和 32 个 kernel 编译结果。
- `scripts/`、`source/`：实际捕获脚本与源码快照。保留失败与排除数据，避免混淆源缓存变化和 Gather 改动。

```powershell
python Tools~/Diagnostics/ScalarAnchorExperiment.py `
  E:/VividRP_Reborn/Logs/ScalarAnchor-20261111/initial `
  E:/VividRP_Reborn/Logs/RadiosityPattern-20261111/capture `
  E:/VividRP_Reborn/Logs/ScalarAnchor-20261111/analysis
python Tools~/Diagnostics/ScalarAnchorMotion.py `
  E:/VividRP_Reborn/Logs/ScalarAnchor-20261111/complete `
  E:/VividRP_Reborn/Logs/ScalarAnchor-20261111/complete `
  E:/VividRP_Reborn/Logs/ScalarAnchor-20261111/complete-analysis
```
