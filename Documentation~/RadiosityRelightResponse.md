# 大幅升降光的 Radiosity 与 Gather 历史换算

后续天空、自发光和独立混合光源的扩展及新实验见 [混合光源响应](MixedSourceRelightResponse.md)。本文保留纯太阳光阶段的实现与实测记录。

2026-11-12，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti。Cornell Bright 原机位，原生 Screen Probe Gather 保持 **16 px、每探针 8 条射线**。本轮未修改场景、材质、Profile 或采样预算的默认值。

## 定位与实现

此前大幅升光会将新低阶结果与旧高阶发布缓存混合。1800 → 3600 lux 的复测中，首帧卡片均值仅 9.17184，按原高阶结果线性换算应为 14.42841，源头已缺少约 36% 能量。即使卡片源头校正，重置稀疏 Gather 历史仍造成额外的首帧低估。

纯太阳光且几何、遮挡、材质不变时，各 RGB 通道的漫反射输运满足线性关系：`L_new[c] = L_old[c] * Sun_new[c] / Sun_old[c]`。实现分为两层：

1. `RescaleSdfSurfaceRadiosity` 在任何消费者读取前换算已发布 incident lighting；只处理有效卡片，保留覆盖与深度，显式 nearest-even 写回 half。新工作阶继续从自身的上一阶重建，不读取旧发布缓存作为反弹输入。完整新目标阶及后续样本批次照常替换，最终求解结果不加增益。
2. 原生 Gather 在读取上一帧方向辐亮度、scalar anchor、方向采样引导和全分辨率像素历史时，同步换算 RGB。Age、coverage、命中距离、法线和重投影拒绝规则保持原样。换算只作用于接受变化的当帧，其他帧绑定单位比例。

仅换算卡片、不保留 Gather 收敛时，升光首帧误差仍为 **−5.45%**；两层同时接通后为 **+1.12%**。这组消融说明剩余缺口来自历史重建，并非还需要提高 Bounce 增益。

### 适用边界

自动优化只用于身份兼容、已经发布完整目标阶、未处于重启过程的纯太阳光缓存。要求：

- 当前及之前均无被追踪的天空光；场景所有 SDF 漫反射表面无正发光贡献。
- 实例 / 材质 revision、卡片布局、太阳方向、shadow strength、layer、距离、bias、Radiosity 样本配置均兼容。
- RGB 均有限且非负；不从零通道恢复正能量；比例不超过 8；`max(oldSun,newSun) * (orders+1) <= 60000`，保守避开已饱和的历史。

不满足时继续采用已有渐进重建。ReSTIR 可以使用换算后的卡片，但**不复用此处的 Gather 历史换算**；其 reservoir 权重与方差需要独立处理。Screen Hit 的旧屏幕颜色和 Far Cache 仍按原光照变化规则失效，不对混合了其他分量的屏幕颜色直接乘比例。

## 同机位 GPU 对照

1920×1080，固定 EV100=8，关闭 AA / Local Exposure；恒定白色太阳，实验期间 `interactsWithSky=false`，结束恢复。相机位置 `(1.62,1.66,-1.7)`，旋转 `(0.03591185,-0.16398631,0.005973937,0.98579067)`。

保留 Adaptive 5×5、空间 anchor、2-pass À-trous、像素历史上限 16；关闭采样引导与 Far Cache。Radiosity 为 8 阶、64 rays/texel/batch、16 批；每帧可更新全部 37 张卡片。每组预热 256 帧，测量 256 帧，在第 64 帧改变光强，保持相同随机帧序列。另以默认的固定样本模式（128 rays、非持续累积）重复升光。

测量直接来自 GPU：天花板区域 `x=912,y=970,w=96,h=48`（GPU 底部坐标），记录 Resolve / Temporal / 最终 GI 的线性 `RGB * coverage` 亮度 Y；卡片为 128×24 RGBA16F atlas 的 37 张有效卡片，排除 11 张空白填充。以下首帧误差统一相对旧路径末段 64 帧均值，关灯则相对变化前均值。

| 观测 | 旧渐进路径 | 新路径 |
|---|---:|---:|
| 升光 1800 → 3600，首帧均值误差 | −14.49% | **+1.12%** |
| 升光，前 32 帧平均绝对误差 | 5.59% | **0.88%** |
| 升光，连续 8 帧进入 ±5% 的起点 | 变化后第 13 帧 | **变化当帧** |
| 降光 3600 → 1800，首帧均值误差 | −6.58% | **+1.11%** |
| 降光，前 32 帧平均绝对误差 | 3.18% | **0.87%** |
| 关灯，首帧残留 / 原亮度 | 5.23% | **0%** |
| 固定样本模式升光，首帧误差 | −14.35% | **+1.14%** |

新路径升光 / 降光的前 32 帧区域均值最大过冲分别 **+1.14% / +1.12%**。这不是逐像素误差上界。关灯组从变化当帧至测量结束，区域 Resolve、Temporal、最终 GI 和有效卡片 RGB 均为零。

### 最终能量与长序列

所有主对照最终有效卡片内容均逐位一致；升降光源缓存相对同求解器 4096-ray 参考的平均能量误差均 **+0.297%**，新旧相同。该参考仍使用现有 SDF / Surface Cache，不是独立路径追踪真值。

256 帧测量时，最终像素末段均值差为 +0.52% 左右，因此额外把升光组延长至 1024 帧（变化后 960 帧）：

| 测量帧窗口 | 新旧最终 GI 区域均值差 |
|---|---:|
| 192–255 | +0.516% |
| 256–511 | +0.184% |
| 512–767 | +0.074% |
| 768–1023 | **+0.045%** |
| 960–1023 | **+0.024%** |

最后 64 帧逐帧区域均值的最大新旧差为 **0.029%**；长序列最后有效卡片仍逐位一致。剩余历史差持续缩小，没有观察到持续的增亮漂移。以长序列末段 256 帧作为参考，首帧误差为旧 −14.57%、新 +1.08%。

## 保护与验证

- 额外 1856 帧 GPU 控制实验：正常升光 / 光色变化只在变化帧换算；实际发光代理、几何移动、方向改变、shadow strength 改变、超保守 HDR 范围、从零开灯及未收敛缓存均拒绝换算。光色变化按实际线性 RGB 比例校验，首帧 half 卡片与预期逐位相同。
- 发光实验保留烘焙材质 identity，仅克隆代理；`HasEmissiveSurfaces=true` 已实际观测。早期克隆材质的无效 fixture 不作为验证证据。
- 同步 CPU 策略实例验证当前 / 历史天空贡献拒绝及 ReSTIR 历史拒绝；这部分不是天空场景 GPU 对照。
- 预热后重复 4096 次缩放判定与历史策略调用，当前线程分配 **0 B**；不代表整个渲染帧分配为零。
- 相关 36 个 compute kernel 的 DXIL / SPIR-V 编译通过，Unity 六个相关 compute 的消息中无错误。没有以普通编译代替 GPU 图像验证。
- 场景太阳、机位、材质代理和 Profile 恢复；所有临时 hook / flag 清除。重开项目后的原活动场景 SampleScene 已恢复。
- 已补充 `SdfRadiosityBudgetTests` 的比例边界和稳定分配回归；Editor 正在运行，未启动 Unity Test Framework，需在关闭 Editor 后运行该组测试。

没有采集 GPU 时间戳，因此不报告毫秒收益。新增开销是在接受换算时对有效卡片执行一次 dispatch，以及已有历史读取处的 RGB 乘法；没有增加 Gather 射线或持久纹理。

## 复现与证据

`Tools~/Diagnostics/RadiosityRelightCapture.cs`、`RadiosityRelightGuards.cs` 通过 Unity CLI eval 执行；`RadiosityRelightExperiment.py` 分析浮点捕获。诊断位 65536 禁用卡片换算以得到旧路径，131072 保留卡片换算但禁用 Gather 历史换算；均仅在 Editor 捕获 hook 存在时生效。

证据：`E:/VividRP_Reborn/Logs/RadiosityRelight-20261111/`。

- `capture-final/`、`analysis-final/summary.json`：5458 渲染帧的主对照、源参考及消融。
- `guards-final/`、`analysis-final/guard-summary.json`：有效发光与其他控制实验。
- `convergence/`、`analysis-final/convergence-summary.json`：2560 渲染帧的长序列。
- `analysis-final/radiosity-relight-response.png`：升光 / 降光 / 关灯响应曲线。
- `verify-result.json`、`final-compile-result.txt`：策略分配、恢复及编译结果。
- `scripts/`、`source/`：实际实验脚本及实现快照；较早的 `capture/` 是仅换算卡片的阶段性结果，`guards/` 含已排除的无效发光 fixture。
