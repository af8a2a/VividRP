# 定向光 VSM：按页／池占用跳过空深度访问

2026-09-12。结论：**保留候选实现，默认启用。** 在固定输入的生产 Resolve 同帧对照中，九种情景、288 帧、597,196,800 个像素逐位相等。计入新增归约成本后，无静态失效的反序窗口 GPU P50 从 **10.698 ms 降至 7.569 ms，下降 29.2%**。这证明当前测试覆盖内的读取等价性与阶段收益；不代表既有 SMRT 几何误差已经消除。

工作起点为 `129171395e1ba210079c8cb0288c7a57332b5e86`，衔接 [16 层容量、阶段成本与动态质量基线](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/README.md)。本次仅修改四个生产文件，版本指纹见 [修改前](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/source-before.json) 与 [修改后](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/source-after.json)。

## 实现与保守条件

- 静态池、动态池及 Unity 兼容光栅都完成后，执行 `VSMPrototypeReducePageOccupancy`，随后才清除 dirty 标记。每个物理页一个 64 线程组；动态池每帧检查，静态池仅在 dirty 或占用未知时检查。
- 在现有页元数据中保存静态空、动态空、静态占用已知三个标志。页重映射、所有权与 dirty 检查仍由原有地址解析路径执行；没有空证明时照常读取。这里没有把未驻留页或未知数据判为空。
- 当前有序深度插入保证：某 texel 的第 0 层为零，则后续层均为空。归约读取第 0 层以证明整页某个池为空，实际场景审计另行检查全部 16 层。
- SMRT 缓存已验证页的标志，在首层和后续层访问中分别跳过已证明为空的池。虚拟 tap 同样使用标志；通用兼容采样入口仍保留完整读取。
- `_VSMPageOccupancySkipDisabled` 仅用于诊断 A/B，零值启用优化。禁用时归约不访问深度池，清除空证明及静态 known 位；重新启用后重新检查静态缓存。

生产入口为 [CSMShadowPass.cs](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs)、[CSMShadowResolve.compute](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute)、[VSMSMRT.hlsl](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/VSMSMRT.hlsl) 和 [VSMProfiling.cs](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/VSMProfiling.cs)。未新增生产纹理或缓冲；256 页、128² 页尺寸、双池 16 层仍占 512 MiB。求交规则、光程和射线预算保持原值。

## 正确性证据

**GPU 生命周期夹具：10 项通过。** 覆盖全空、静态单池、缓存静态与动态清空、双池独立 16 层、移动投影者移除、静态失效后重绘为空、末尾物理页仅动态数据、页重映射与无主残留、物理槽复用、诊断禁用后重新启用。每项比较原始读取与跳过后的深度元组，差异全部为零。见 [GPU 结果](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/gpu-fixture-result.txt)；这是真实 GPU 执行，不是仅检查源代码。

**实际场景全层审计：通过。** 每池检查 256 个有效页、4,194,304 个 texel 的全部 16 层。静态池空页 0、动态池空页 256，误判为空均为 0；层排序、重复、孔洞、非法深度和残留 dirty 检查均为零异常。静态池共 37,370,145 个非零深度槽。此项只描述该静态 Sponza 帧，见 [审计结果](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/capacity-verification.json) 与 `raw/capacity/`。

**固定同帧输入：288 帧全部逐位相等。** 对 static、camera_slide、camera_turn、thin_sweep、deform、receiver_slide、receiver_lift、sun_rotate、sun_step 各运行 32 帧。每帧对同一组当前场景缓冲、页映射、采样相位及前帧历史，重放两次生产 `CSMShadowResolve` 到独立 R32 输出，仅切换读取覆盖开关。GPU 比较全部 1920×1080 像素的浮点位模式，并检查有限值和实际阴影覆盖；总计 **597,196,800 像素、0 差异**。每帧至少 1,783,498 个像素有阴影，避免全亮空跑。两次重放不更新历史，完成后恢复生产输出绑定和开关。见 [同帧汇总](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/same-frame-comparison.json) 与 `same-frame/` 逐帧记录。

同帧比较发生在诊断禁用路径改为“不扫描深度”之前；最终修改未改变默认启用路径，第 10 项 GPU 夹具另外验证了最终禁用与重新启用行为。同帧证据覆盖未滤波 Resolve，不把跨次采集的滤波历史等同为相同输入。

**两次独立动态采集仍有三项差异，原样保留。** 两侧各九情景 ×32 帧，使用自适应模式及 256 射线 RTAS 参考。thin_sweep、deform、receiver_slide、receiver_lift、sun_rotate、sun_step 的 world/reference/signal/pages 全部逐位相等。另三项如下：

|情景|world 差异浮点字|signal 差异浮点字|可见度最大绝对差|历史年龄最大差|
|---|---:|---:|---:|---:|
|static|0|16,257|0.00048828125|0|
|camera_slide|20|51|0.0166015625|2|
|camera_turn|12|16|0.0000152587890625|1|

所有情景的参考、页数据与检查的元数据字段一致。但跨次运行未能保证全部输入和历史初态逐位相同，三项不标记为严格通过，也不声称其全部差异成因已被定位。严格结果文件保持 `status: differences`，见 [独立采集对照](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/dynamic-comparison.json)。采用上面的同帧重放隔离本次读取优化。

## 阶段成本

环境：RTX 5070 Ti、DX12、Unity 6000.7.0a6，1920×1080、AA=None；虚拟分辨率 2048、256 物理页、128 页尺寸、16 层、10 个 clipmap；自适应 2/4 射线、8 步、光程 10、光源角直径 7.1°，短历史设置与前一基线一致。相机及光照原始值随采集归档。

最终静态 A/B/B/A 的每个窗口预热 64 次、观测 128 次。基线归约只清除元数据，**不扫描任何深度池**，因此排除了先扫描再忽略标记所带来的深度缓存预热。下表基线为 Resolve，候选为每次观测的 Resolve + PageOccupancy 之和，再计算分位数；没有把各阶段的中位数直接相加。

|窗口|P50 ms|P95 ms|P99 ms|静态失效观测数|
|---|---:|---:|---:|---:|
|基线，前序|10.605|11.428|11.624|33 / 128|
|跳过空池，前序|7.559|8.558|8.870|0 / 128|
|跳过空池，反序|7.569|8.483|8.948|0 / 128|
|基线，反序|10.698|11.712|11.941|0 / 128|

![Resolve 加候选占用归约成本](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/cost-comparison.png)

候选占用归约 P50 为 **0.0604 ms**；基线残留的元数据清除 dispatch 约 0.0031 ms，不计入上表基线。无失效的反序对照下降 **29.2%**，前序下降 28.7%。前序基线出现 33 次静态失效及重绘尖峰，原因未证实；不能把这些额外重绘成本归因于是否跳过空池。主结论使用两侧都没有静态失效的反序窗口，详见 [最终统计](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/stage-comparison-final.json) 与 `timing-final/` 原始观测。

早期动态计时另有九情景、正反序共 36 窗口、4,608 次观测。以下为非嵌套已记录阶段之和的 P50，按每次观测扣除基线占用归约，候选保留全部归约成本：

|情景|前序：基线 → 候选 ms|反序：基线 → 候选 ms|
|---|---:|---:|
|static|11.183 → 8.077|11.401 → 7.849|
|camera_slide|13.612 → 7.809|14.020 → 7.886|
|camera_turn|14.495 → 8.426|13.446 → 8.281|
|thin_sweep|11.871 → 8.193|11.310 → 8.252|
|deform|12.045 → 8.406|10.988 → 8.367|
|receiver_slide|12.498 → 8.780|10.991 → 8.709|
|receiver_lift|12.139 → 8.758|11.152 → 8.822|
|sun_rotate|21.918 → 18.418|21.908 → 18.397|
|sun_step|10.821 → 8.059|11.028 → 8.167|

这些动态数据属于探索性证据：当时基线仍扫描占用，只是在 Resolve 中忽略标志；从时间中扣除扫描成本不能消除缓存预热影响。不能用它替代最终严格静态 A/B。见 [动态阶段统计](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/dynamic-stage-comparison.json) 与 `dynamic-timing/`。早期静态结果也保留在 `stage-comparison.json` 和 `timing/`。

GPU Recorder 返回最近完成的 GPU profiling frame；观测所带相机帧号是采集时间信息，并非精确 GPU 帧关联。阶段和不包含未埋点空隙，也不是整个 VSM 或整帧墙钟时间。本次没有 Nsight 硬件计数器，因此不声称实际显存事务降低了某个百分比。

## 验证与复现

- 最终 DXC `cs_6_6`：生产 28、SamplingTests 19、GPU 夹具 8、同帧比较 2，共 **57/57** 入口通过，见 [DXC 结果](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/dxc-final.json)。当前 Unity shader 错误列表为空。
- 独立 Roslyn 编译 Runtime、Editor、Editor.Tests 全通过，源文件与 Bee response 在编译期间稳定，见 [C# 编译结果](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/validation/csharp-validation.json)。Editor 正在交互运行，未启动 Unity Test Framework；如需运行其既有测试，由用户手动执行。
- 实际新增录制路径在预热 128 次后重复 4,096 次，当前线程 `GC.GetAllocatedBytesForCurrentThread()` 增量 **0 B**，包括缓存 sampler、两个 GraphicsBuffer 绑定、两个 RTHandle 绑定及 DispatchCompute 命令录制。见 [GC 结果](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/gc-result.txt)。此项不覆盖 GPU 执行或整个渲染管线全部线程。
- 独立质量采集的原始浮点数据分别归档到 `raw/dynamic-baseline/` 与 `raw/dynamic-skip/`，压缩体积约 43.31 MB / 42.95 MB。已实际解包、按解压后字节 SHA256 验证并重跑比较，结果与原统计相同，包括三项差异，见 [恢复验证](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/reproduction-verification.json)。同帧全屏比较在 GPU 完成，归档保留逐帧摘要与元数据，未保存两套全屏 scratch 原图。

离线重算独立质量对照，在包目录执行：

```powershell
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/archive-dynamic.py restore Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/raw/dynamic-baseline Temp~/vsm-empty-skip/repro-baseline
python Roadmap~/Experiments/VSMCapacityCostDynamic_20260912/archive-dynamic.py restore Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/raw/dynamic-skip Temp~/vsm-empty-skip/repro-skip
python Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/compare-dynamic.py Temp~/vsm-empty-skip/repro-baseline Temp~/vsm-empty-skip/repro-skip --output Temp~/vsm-empty-skip/repro-comparison.json
```

GPU 夹具、同帧比较、动态和静态采集器均以 `.txt` 源码保存在本目录。它们需要临时导入 Editor/Tools，由 Unity 生成 meta；容量与 RTAS 参考 shader 复用前一基线目录的归档。`compile-shaders.py` 记录本机 DXC 和 Unity include 映射位置，重跑前需具备对应 SDK 与 `Temp~/vsm-bnd-compare/dxc2/include` 映射。代码版本、夹具来源及最终删除清单可在 `source-*.json`、`cleanup-manifest.json`、`cleanup-final-manifest.json` 中核对。

## 恢复状态与结论边界

所有临时 Editor 脚本、shader 及 Unity 生成的 meta 已通过 AssetDatabase 删除；临时场景对象为零，诊断开关恢复零，未进入 Play，runInBackground 恢复 false。相机、光源及解析后 Volume 序列化值逐项恢复一致，原动态请求文件已恢复。见 [状态恢复记录](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMEmptyPoolSkip_20260912/state-restoration.json)。

本轮没有记录开始时的场景 dirty 标志，因此不能证明 dirty 状态与开始时一致。结束时 Sponza 为 dirty=true，SponzaLightingDay 为 false；没有清除 dirty 标志或保存任何场景。

本次可接受的是“只省略已有空证明的读取”。16 层容量截断、粗层细几何误差和 SMRT 的几何准确性仍由前一基线约束；本轮没有增加容量或修复这些误差。性能结论限于当前硬件、场景与设置；动态夹具沿用 Meshlet 路径，不扩展为真实 SkinnedMeshRenderer 骨骼动画结论。
