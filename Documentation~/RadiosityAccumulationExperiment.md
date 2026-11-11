# Cornell Box：分离 Radiosity 反弹阶数与样本累积

2026-11-11，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti。实测结论：固定 8 阶后追加新样本能持续降低静态 Surface Cache 误差；灯光变化可以完全丢弃旧能量，但当前硬重置会造成短暂亮度缺口。因此以**默认关闭的实验选项**保留，未替换现有默认路径。

## 实现与边界

新增 Volume 参数：

- `surfaceCacheRadiosityAccumulate`：默认 false。开启后为每个反弹阶保留独立的累积 atlas。
- `surfaceCacheRadiosityBatchLimit`：默认 64，范围 1–4096。每个阶的独立采样批次数上限。
- 原 `surfaceCacheRadiositySamples` 仍表示一次更新的每 texel 射线数，默认 16；`surfaceCacheBounceOrders` 仍限制最大反弹阶数，未把新批次当成更高阶反弹。

以 8 阶为例：按顺序更新第 1–8 阶，每阶只读取上一阶的估计。第 n 批在本阶旧估计上以 `1/n` 的权重累积；后续批次按批次和阶数更换种子。第一批保留原固定采样序列。各阶中间结果与对外发布的结果使用不同 atlas，目标阶的完整卡片扫描结束后才发布。常规资源继续使用 RGBA16F，累积写回使用最近偶数舍入，避免反馈能量朝零偏移。

低阶估计也在随批次变化，因此“128×16=2048 射线 / texel / 阶”描述累计工作量，不等同于在固定输入上一次性追踪 2048 条射线的求解器。该方案仍有层间累积延迟和半精度误差底限，需要通过参考解观察实际收敛。

灯光、几何、卡片、天空、追踪参数、阶数、采样数、模式或批次上限变化，会重置工作阶、卡片 cursor 和批次。batch=0 不读取旧累积；新一轮 target-order 完整结果生成前，旧发布缓存不参与命中光照。各阶由 CameraHistory 持有，沿用现有相机资源生命周期。

## 实验设置

- Cornell Bright 原机位；1920×1080，固定 EV100=8，定向光 3600 / 1800 lux。
- 原生 Screen Probe Gather：**16 px 网格，每探针 8 条射线**，像素历史 16，anchor blend 0.85；空间 anchor、Adaptive 5×5、2-pass À-trous。
- 屏幕采样引导与 Far Radiance Cache 关闭。实验期间临时关闭太阳 `interactsWithSky`，使光照输入严格恒定，避免上一轮发现的大气透射微小变化重置源缓存。结束后恢复。
- 37 张卡片，每卡片 8×8 texel。每批使用 16 条 Radiosity 射线 / texel；静态累计 128 批。
- 分别生成 3600 / 1800 lux 下的 **8 阶、4096 射线 / texel** 参考，参考每帧更新 1 张卡片，预热 424 帧。
- 误差统计有效卡片 texel 的线性亮度 Y，不统计未分配 atlas padding；参考是现有 SDF / 卡片算法的高样本结果，不是路径追踪真值。

主实验渲染 3378 帧，另有 80 帧冷启动 / 上限 / 默认回归控制，全部通过 D3D12 实际 GPU 读取验证。

## 静态误差随新样本下降

旧模式完成 8 阶后停止更新，128 帧有效卡片内容逐位不变；其亮度 RMSE 为 9.5662，能量均值相对高样本参考偏高 1.6185%。实验路径维持最大 8 阶，分别使用 seed 0 和 912731：

| 每阶批次 | 累计射线 / texel / 阶 | seed A 的 Y RMSE | seed B 的 Y RMSE |
|---:|---:|---:|---:|
| 1 | 16 | 9.5712 | 9.4763 |
| 2 | 32 | 6.4835 | 6.2895 |
| 4 | 64 | 4.8179 | 4.6973 |
| 8 | 128 | 3.3183 | 3.2460 |
| 16 | 256 | 2.3715 | 2.2432 |
| 32 | 512 | 1.7147 | 1.6236 |
| 64 | 1024 | 1.1919 | 1.1217 |
| 128 | 2048 | **0.8187** | **0.8329** |

128 批时，两组相对各自首批的 RMSE 分别下降约 **91.45% / 91.21%**。相对 4096 射线参考的平均能量误差为 **−0.0326% / +0.3142%**。下降趋势不是靠增加反弹阶数或降低整体能量获得。

每个完整批次之间的目标阶保持为 8，累计 1929 次逐帧检查确认：未完成的新卡片扫描没有改变对外发布的有效卡片内容。全量与分批更新得到的首份新光照缓存逐位相同。

## 灯光变化响应与过渡代价

### 小场景全量扫描

每帧更新 37 张卡片，一帧完成一个阶；静态累计 128 批后将光从 3600 改为 1800 lux。

- 变化当帧，工作阶、cursor、批次和 published order 全部归零，旧缓存立即停止参与命中光照。
- **第 8 帧末发布**第一份新 8 阶缓存，**第 9 帧**开始由 Gather 消费。
- 首批新缓存 RMSE 为 4.7856，16 批后降为 1.1858；能量误差从 +1.6729% 降为 +0.0383%。
- 阶跃后的第一份新缓存与独立冷启动到 1800 lux 的第一批结果**逐位相同**，验证没有混入之前 128 批旧光照。

同时测量与上一轮相同的天花板 96×48 最终 GI 区域，而非仅看 CPU 计数器：

| 指标 | 观测值 |
|---|---:|
| 变化前 64 帧平均 Y | 78.2507 |
| 变化后后段 64 帧平均 Y | 38.7665 |
| 变化当帧 Y | 30.2081 |
| 首帧相对后段均值 | **−22.08%** |
| 前 32 帧最大 Y | 44.1994 |
| 首次连续 8 帧落在后段均值 ±10% 内 | 第 12 帧开始 |

首轮没有 target-order 缓存时，命中照明只用直接光等当前来源，出现能量缺口；发布后还叠加了低样本源误差及 Gather 历史响应。上述过渡数据以观测后段均值为基准，不是逐帧 GI 真值误差，也不代表噪声已完全收敛。

### 每帧 16 张卡片的分批扫描

关闭 small-scene fast fill：37 张卡片按 16 / 16 / 5 更新，一个阶用 3 帧，8 阶冷启动需要 24 帧。中途改变同一盏灯：

- **第 24 帧末发布、第 25 帧消费**第一份新缓存。
- 第一份新缓存与全量扫描及独立冷启动结果逐位相同。
- 变化后可用的重建后窗口只有 16 帧；其天花板平均 Y 为 38.7098。前 32 帧最低 Y=30.6918，约低 20.72%。这一组不能当作充分收敛的动态参考。

结论是“响应延迟受卡片预算和目标阶数控制，旧光照不会拖入新样本”，并非无代价过渡。大场景下完整冷启动扫描会更慢，当前原型尚未解决这一点。下一步优先测试首轮完成低阶扫描后逐阶发布，以及灯光变化期间提高更新预算，缩短可见亮度缺口。

## 控制、成本与未覆盖范围

- batch cap=2 的独立控制：完成第 2 批后，后续 16 帧更新卡片数为 0，已发布缓存逐位不变。
- 关闭实验选项恢复旧路径后，连续 32 帧源缓存与旧模式控制逐位相同。
- 新增输入稳定性验证函数预热后重复调用 4096 次，两组均测得 0 B 托管分配。这仅覆盖该函数，不是全帧分配测量。
- 8 阶实验路径需要 8 张阶级 atlas 加 1 张发布 atlas。Cornell 描述符为 128×24 RGBA16F，纹理有效尺寸合计 **216 KiB**，旧两张 atlas 合计 **48 KiB**；这不是驱动实际显存测量。同一相机切换模式时 CameraHistory 可能保留两套资源直到历史释放。
- 4096 张卡片的同格式描述符：实验九张约 18 MiB，旧两张约 4 MiB。持续精化也增加总追踪工作，未测 GPU 毫秒开销。
- 验证限定 Cornell、恒定输入和强度阶跃；没有验证 Sponza、移动几何、天空连续变化或 Far Radiance Cache 的批次刷新。

## 复现与证据

本机输出：`E:/VividRP_Reborn/Logs/RadiosityAccumulation-20261111/`。

- `capture/frames.csv`、逐帧 `*-atlas.f16` / `*-gi.f16`：主实验输入、状态及真实 GPU 数据。
- `controls/`：独立冷启动、上限停止、默认恢复、输入验证分配控制。
- `analysis/summary.json`、`metrics.csv`、`gi-roi.csv`、`convergence-and-light-response.png`：源误差与实际 Gather 过渡。
- `capture-result.json`、`controls-result.json`、`editor-verification.json`：实际运行和恢复检查。
- `compile/shader-verification.txt`：三个 Radiosity 入口的 DXIL / SPIR-V 编译。
- `scripts/`、`source/`：实际实验脚本与最终源码快照。`smoke/` 保留早期 40 帧短验证。

```powershell
unity command script eval --project-path E:/VividRP_Reborn `
  --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/RadiosityAccumulationCapture.cs `
  --timeout 1800 --format json
python Tools~/Diagnostics/RadiosityAccumulationExperiment.py `
  E:/VividRP_Reborn/Logs/RadiosityAccumulation-20261111/capture `
  E:/VividRP_Reborn/Logs/RadiosityAccumulation-20261111/analysis
```

三入口 DXIL / SPIR-V 编译通过，Unity C# 导入和全部 GPU 捕获通过，成功组错误记录为空，Unity shader messages 为空。场景、原 Profile、相机、太阳 `interactsWithSky`、诊断 flags 与回调均已恢复；原 Profile 的 Radiosity 采样仍为 16，实验开关默认及 Profile 都为 false。未运行 Unity Test Framework，Editor 正在交互运行。
