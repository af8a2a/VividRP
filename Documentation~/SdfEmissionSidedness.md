# Screen Hit / Mesh SDF / Radiosity / 远场的材质单面判定

2026-11-13；Unity 7000.0.0a16 / DX12。Cornell Box 保持原机位、1920×1080、16 px 网格、每探针 8 条射线；默认追踪偏移仍为 0.04 m。

## 实现契约

- `SdfMaterialSidedness.hlsl` 统一使用 Unity `CullMode`：Off=0、Front=1、Back=2。判定采用原表面法线与出射方向；不把人工 SDF 壳梯度当作材质朝向。
- `DiffuseSurface.Emission.rgb` 保持原线性辐亮度，原先未使用的 `.w` 传递 CullMode。结构仍为 64 字节，各 float4 偏移为 0/16/32/48，CullMode 位于字节 28。材质朝向变化会使 transport 失效，不能进入普通发光强度同比缩放。
- 卡片仍保留原有朝向捕获面的着色法线，`NormalCoverage.a` 非零表示覆盖，额外用约 0.5 标记原法线被翻转，1 表示未翻转。消费者只按非零判断覆盖，不把这个标记当作半覆盖。运行时可恢复原法线，同时保留原着色与滤波法线。
- Screen Hit 在可用时从匹配的 Meshlet 几何重建三角形平面，避免法线贴图改变发光侧；Cull Front 对应反向原面。无 Meshlet 材质映射的屏幕兜底继续按单面处理。
- Mesh SDF、Radiosity、远场共用 `TraceSdfSurfaceRadiance`。远场缓存重连也复核原材质侧，防止正面缓存被背面消费。
- 被材质剔除的一侧是**有效黑命中**：保留几何遮挡、命中距离、实例及覆盖率；不会作为 miss 继续穿过薄壳。诊断状态新增 `RayMaterialBackface`，低三位仍保留 Screen/SDF 的有效命中类别。

没有修改光强、Albedo、积分 π、曝光、每探针射线数或默认 bias。单面材质的背面出射照明整体置黑，包含直接发光和该侧的卡片反射；双面材质保留两侧出射。

## 已有缓存的兼容与 Cornell 更新

旧卡片捕获时已丢失原法线正反面信息，无法从原纹理准确恢复。`m_OriginalNormalVersion` 区分新数据；未升级的缓存保留旧双面兼容行为，不凭猜测改变其他场景的照明。**需要重捕获后才能使用完整单面契约。** 新烘焙数据自动带版本。

新增 `VividSdfSurfaceCacheBaker.Recapture(cache)`，保留资产身份、现有条目和局部卡片布局。本次通过 Unity API 更新了 Cornell 的 `Emissive Ceiling Surface Cache.asset`，没有手改 `.asset` 或 `.meta`；更新前备份在：

```text
E:/VividRP_Reborn/Logs/EmissionSidedness-20261113/surface-cache-before.asset
```

备份与新资产的二进制纹理比对确认：Albedo/Depth 全部不变，法线 RGB 全部不变，只有发光面片背面卡片的 **4,096 个 alpha 标记**改变。当前 R8 存储的标记值为 0、127、255；运行时按 `<0.75` 恢复翻转符号。

## 实测

数据根目录：`E:/VividRP_Reborn/Logs/EmissionSidedness-20261113/`。

使用相同采样序列：每组预热 512 帧，随后 32 帧中每隔 4 帧采集一次，共 8 个样本；太阳／天空均为 0。8 阶 Radiosity 固定每批 64 样本、32 批。旧语义对照通过 Editor 开关强制 Cull Off，使用相同新链路和卡片数据，**不是旧二进制的重放**。

| 条件 | 背面高亮命中：旧语义 → 修正 | 地面最终线性均值变化 |
|---|---:|---:|
| Hybrid，零阶，4 cm | 9 → 0 | −0.179% |
| SDF Only，零阶，4 cm | 9 → 0 | 0.000% |
| Hybrid，8 阶，4 cm | 14 → 0 | −0.174% |
| SDF Only，零阶，5 mm 压力条件 | 1,935 → 0 | 0.000% |

零阶原始天花板命中光照均归零。默认偏移下，最终像素仍约有 0.000217 的极小重建残留；没有将这一项宣称为完全清零。5 mm 压力条件下最终天花板均值从 4.17668 降为 0。

地面正面发光命中的辐亮度在两组中均为约 **200.000046**，没有出现强度缩放或重复 π。汇总线性均值的 1% 检查线仅用于本次短序列诊断，所有组均通过；它不等于长序列收敛证明。

有效黑命中方面，修正后的 Hybrid 零阶 **6,872** 条、SDF Only 零阶 **689** 条、8 阶 **514** 条、5 mm 压力组 **2,553** 条材质背面样本全部保持有效命中类别与非零探针覆盖。源计数与实际有效射线数完全对应，未把黑样本转成 miss。天花板最终平均 coverage 仍约为 0.748，包含场景原有的其他不完整覆盖；没有声称全图覆盖率为 100%。

### 真实 Radiosity / 远场入口

`EmissionTraceConformance.cs` 使用 Cornell 实际 GPU SDF、实例、材质和卡片，仅将诊断 BVH 叶子隔离为发光面片，避免房间天花板先遮住背面。它直接调用生产 `TraceCardRadiance` 和 `TraceFarProbe`，没有复制另一套追踪或着色算法：

| 入口 | 正面 RGB | 背面 RGB | 背面有效性 |
|---|---|---|---|
| Radiosity | 200.00006 | 0 | 真实命中返回黑 |
| Far Probe | 200.00006 | 0 | 实例 ID 保留，距离约 2.02209 m |

另有六种 GPU 极性组合检查，覆盖 Off / Front / Back 的原正面与原背面，结果符合预期。此隔离实验验证远场实际追踪入口，不冒充整套移动 Radiance Cache 的场景性能或时域测试。

## 验证与复现

- 实际四路 GPU 着色与捕帧通过；共享布局的 stride 和各字段偏移检查通过。
- 强度变化保留 transport、CullMode 变化使 transport 失效的检查通过。
- 预热后连续 128 次 `VividMeshSdfGpuScene.Update`：当前线程托管分配 **0 B**。没有据此宣称整个渲染帧零分配或 GPU 耗时改善。
- 保留 `SdfScreenProbeTracing.compute` 的编译器警告记录，包含 Receiver 内核寄存器压力及静态分析警告；实际捕帧没有 Shader 错误或运行错误。本轮没有测量 GPU 耗时，不能据此判断新增 GPU 开销。
- 编辑器运行期间未启动 Unity Test Framework。新增／更新的回归项位于 `SdfRadiosityBudgetTests` 和 `VividGPUDrivenBufferSetTests`，可在允许运行测试的会话中手动执行。

```powershell
# 输出到项目外的 VividRP_GI_Evidence/，每次自动创建唯一目录。
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/EmissionSidednessCapture.cs --timeout 4500 --format json
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/EmissionTraceConformance.cs --timeout 180 --format json
# 把下一行的路径替换为捕帧返回的 evidenceRoot。
python -B Tools~/Diagnostics/EmissionSidednessExperiment.py --root E:/VividRP_GI_Evidence/EmissionSidedness-YYYYMMDD-HHmmss-fff
```

`analysis.json` 记录配对能量、背面样本和覆盖率；`cache-diff.json` 记录卡片比对；`trace-conformance.json` 记录实际 Radiosity／远场入口；`final-verification.json` 记录布局、分配及恢复状态。`emission-sidedness.png` 使用相同线性标尺显示零阶顶灯邻域。

最终固定组使用 `capture-fixed-geometry/` 中的几何法线版本，旧语义组保留 `capture/` 中未变的 Cull Off 对照。原始固定组的数据保留，但不混入最终统计。

本轮验证范围是固定机位的短序列。运动时跨材质侧的历史拒绝仍应单独验证；其他旧缓存和 Sponza 本轮未重捕获。

交接时已确认：诊断回调为空、强制双面开关关闭、frame override=-1，配置与有效 bias 均恢复 0.04，原场景保持不脏，正常 Radiosity 已恢复 8 阶／32 批。

## 当前版本复验

原 `Logs/EmissionSidedness-20261113/` 中的历史原始数据当前缺失，上述表格保留为当时记录。复验工具现在将原始数据写入项目外的唯一目录，并在每组完成后保存清单；分析器拒绝未完成的捕帧。

复验同时按 Screen Hit / SDF Hit 区分天花板的有效黑命中，检查全图材质背面样本的 RGB、命中类别与覆盖率。`EmissionTraceConformance.cs` 在独立材质缓冲上运行 Cull Off / Front / Back，直接调用生产 Radiosity 和远场追踪入口；验证后确认场景发光值不变。GPU 共享结构逐字段检查仍为节点 48 B、材质 64 B。

复验运行于 `23adf215` 的渲染实现（包含后续的 covered pixel history 修正），未重新修改四路着色或场景资产。仍为原机位、16 px / 8 rays、每组预热 512 帧，32 帧中采集 8 个样本。

| 条件 | 天花板背面高亮命中：双面对照 → 单面 | 全图有效黑命中／保留覆盖 | 地面最终线性均值变化 |
|---|---:|---:|---:|
| Hybrid，零阶，4 cm | 9 → 0 | 35,532 / 35,532 | −0.155% |
| SDF Only，零阶，4 cm | 9 → 0 | 4,278 / 4,278 | 0.000% |
| Hybrid，8 阶，4 cm | 11 → 0 | 3,864 / 3,864 | −0.066% |
| SDF Only，零阶，5 mm | 1,935 → 0 | 8,737 / 8,737 | 0.000% |

Hybrid 零阶的全图有效黑命中包含 Screen Hit 31,603 条、SDF Hit 3,929 条；仅天花板区域分别为 6,258 和 617 条。全部材质背面样本 RGB 为零，保留原有效类别与非零覆盖，零阶源计数与有效射线数匹配。正面发光命中辐亮度仍为 200.000046；四组均通过本次短序列的地面均值 ±1% 检查。默认偏移下，零阶最终天花板仍有约 0.000219 的重建残留，原始命中光照为零；5 mm 压力组最终均值为零。

生产 Radiosity / Far Probe 三模式矩阵通过：Off 两侧均为 200.00006；Front 为原正面 0、原背面 200.00006；Back 为原正面 200.00006、原背面 0。黑命中的实例标识仍为 9、远场距离仍为正（约 1.95209 / 2.02209 m）。实际 GPU 数据与布局检查通过，场景发光材质未被更改。诊断 shader 无编译错误，保留两条 `floating point division by zero` 静态分析警告；捕帧无运行错误。

本次原始数据与分析：

```text
E:/VividRP_GI_Evidence/EmissionSidedness-20261113-145618-928/capture/capture.json
E:/VividRP_GI_Evidence/EmissionSidedness-20261113-145618-928/analysis.json
E:/VividRP_GI_Evidence/EmissionSidedness-20261113-145618-928/emission-sidedness.png
E:/VividRP_GI_Evidence/EmissionSidedness-20261113-145618-928/final-verification.json
E:/VividRP_GI_Evidence/EmissionTraceConformance-20261113-150649-654/trace-conformance.json
```

复验后确认回调为空、诊断 flags=0、强制双面关闭、frame override=-1、bias=0.04，正常 Radiosity 恢复 8 阶 / 32 批，场景未脏。未运行 Unity Test Framework（Editor 正在使用），未测量 GPU 耗时；本次不包含移动机位或旧场景缓存迁移。

## 远场缓存重连补验（2026-11-14）

复核当前工作区的四路实现后，重新完成同机位八组 GPU 对照。仍为 1920×1080、16 px / 8 rays、512 帧预热后采集 8 个样本；使用 Cull Off 作为旧双面语义对照。没有修改生产着色、场景、光强或曝光；保留工作区中尚未提交的方向采样实验，捕帧时诊断 flags=0。

| 条件 | 背面高亮命中：双面 → 单面 | 全图有效黑命中／保留覆盖 | 地面最终线性均值变化 |
|---|---:|---:|---:|
| Hybrid，零阶，4 cm | 9 → 0 | 35,532 / 35,532 | −0.155% |
| SDF Only，零阶，4 cm | 9 → 0 | 4,278 / 4,278 | 0.000% |
| Hybrid，8 阶，4 cm | 11 → 0 | 3,864 / 3,864 | −0.066% |
| SDF Only，零阶，5 mm | 1,935 → 0 | 8,737 / 8,737 | 0.000% |

全部 **52,411** 条材质背面黑样本 RGB 为零，保留有效 Screen/SDF 类别及非零探针覆盖。零阶正面发光命中均为 200.000046。默认 4 cm 偏移下，零阶天花板原始命中照明为零，最终重建仍有约 0.000219 的微小残留；5 mm 压力组最终为零。短序列地面均值通过 ±1% 检查，不替代长序列收敛或运动质量验证。

扩展 `EmissionTraceConformance.cs` 和独立 Editor 诊断 shader，进一步直接调用生产 `TryLoadFarRadiance`。从真实 Cornell SDF 中选择实际缓存单元与八面体方向：正面 cell `(0,0,0)` / bin 42，背面 cell `(0,1,0)` / bin 17。诊断 atlas **两侧均预存约 200 的亮值**，用于检查消费端会不会直接复用错误侧的缓存。材质缓冲、BVH 与 atlas 均为独立临时资源，房间其他几何不参与本隔离实验。

| CullMode | 原正面重连 RGB | 原背面重连 RGB | 两侧有效性 |
|---|---:|---:|---|
| Off | 200.00006 | 200.00006 | 均为 1，距离为正 |
| Front | 0 | 200.00006 | 均为 1，距离为正 |
| Back | 200.00006 | 0 | 均为 1，距离为正 |

Radiosity 与 Far Probe 原始追踪入口的三模式矩阵也通过；黑命中保留实例 ID 9，远场命中距离约 1.95209 / 2.02209 m，实时场景发光材质未改变。新增诊断 kernel 首次因缺少共享函数的编译定义失败，失败输出未计入结果；修正后增加 dispatch 后的 shader 错误检查和方向有限值检查，最终 GPU 运行无 shader 错误。

本次证据：

```text
E:/VividRP_GI_Evidence/EmissionSidedness-20261113-185011-700/capture/capture.json
E:/VividRP_GI_Evidence/EmissionSidedness-20261113-185011-700/analysis.json
E:/VividRP_GI_Evidence/EmissionSidedness-20261113-185011-700/final-verification.json
E:/VividRP_GI_Evidence/EmissionSidedness-20261113-185011-700/source-verification.json
E:/VividRP_GI_Evidence/EmissionTraceConformance-20261113-190324-973/trace-conformance.json
```

源码校验和记录于捕帧运行中，不能声称首帧前已冻结；记录时点至结束生产渲染源码一致，仅独立 Editor 诊断 shader 在此期间扩展。`EmissionSidednessState.cs` 已实际运行，确认回调为空、flags=0、强制双面关闭、frame override=-1、bias=0.04、正常 8 阶 / 32 批、两个 Cornell 场景均未脏。该脚本只检查并等待正常缓存收敛，不保存资产：

```powershell
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/EmissionSidednessState.cs --timeout 240 --format json
```

本轮补齐远场缓存消费的回归证据，没有测量 GPU 耗时，也未运行 Unity Test Framework（Editor 正在使用）。旧卡片仍需重捕获才能启用完整原法线契约，移动时跨材质侧的历史验证仍属于后续工作。

## 默认采样版本完整复验（2026-11-14）

在 `56247693` 的生产实现上重新完成八组捕帧，未重复修改已统一的四路着色。默认独立采样、诊断 flags=0；保持同一机位、1920×1080、16 px / 8 rays、512 帧预热和随后 8 次采样。捕帧前记录的 **219 个**受检 GI 源码及 Cornell 夹具文件在捕帧后全部校验一致；未混入工作区中另行开展的采样／三轮去噪候选。

| 条件 | 背面高亮命中：双面 → 单面 | 有效黑命中／保留覆盖 | 地面最终线性均值变化 |
|---|---:|---:|---:|
| Hybrid，零阶，4 cm | 9 → 0 | 35,578 / 35,578 | −0.010% |
| SDF Only，零阶，4 cm | 9 → 0 | 4,278 / 4,278 | 0.000% |
| Hybrid，8 阶，4 cm | 11 → 0 | 3,864 / 3,864 | −0.066% |
| SDF Only，零阶，5 mm | 1,935 → 0 | 8,737 / 8,737 | 0.000% |

共 **52,457** 条材质背面样本 RGB 为零，全部保留有效命中类别与非零覆盖。零阶地面正面发光命中仍为 **200.000046**；短序列地面均值均通过 ±1% 检查。默认偏移下，零阶天花板原始命中照明为零，最终重建仍约有 **0.000219** 的残留；不将其描述为最终图像完全归零，也不以短序列代替长序列收敛或运动验证。

再次在当前 Editor 中执行生产 Radiosity、Far Probe 和远场缓存重连的 Off / Front / Back GPU 矩阵。Back 模式原正面均为 200.00006、原背面均为 0；背面重连有效性仍为 1，命中距离为正。原场景发光值未改变，诊断 shader 无错误或警告；材质 64 B 与 BVH 节点 48 B 的逐字段布局检查通过。

```text
E:/VividRP_GI_Evidence/EmissionSidedness-20261114-010323-368/analysis.json
E:/VividRP_GI_Evidence/EmissionSidedness-20261114-010323-368/emission-sidedness.png
E:/VividRP_GI_Evidence/EmissionSidedness-20261114-010323-368/source-before-capture.json
E:/VividRP_GI_Evidence/EmissionSidedness-20261114-010323-368/source-verification.json
E:/VividRP_GI_Evidence/EmissionSidedness-20261114-010323-368/final-verification.json
E:/VividRP_GI_Evidence/EmissionTraceConformance-20261114-010146-130/trace-conformance.json
```

分析器现在要求清单显式标记 `complete=true`，并检查完整 1920×1080 分辨率，避免把缺少完成标记的捕帧当作有效证据。捕帧无运行错误；恢复检查确认回调为空、flags=0、强制双面关闭、frame override=-1、bias=0.04、正常 8 阶 / 32 批、两个场景均未脏。本轮未测量 GPU 耗时，未启动 Unity Test Framework。

## 当前 Cornell 三轮滤波配置复验（2026-11-14）

四路单面规则已在现有实现中统一，本轮没有再次修改生产着色。修正捕帧工具原先固定覆盖为 Atrous2 的行为，改为保留夹具当前的空间滤波设置；清单新增 `emission-sidedness.v2`、配置名称与逐帧实际迭代数，分析器核对配置与实际 dispatch 路径一致。本次使用 `c7f7cc43` 的护边 Atrous3，仍为原机位、1920×1080、16 px / 8 rays；每组预热 512 帧后采集 8 帧。捕帧前后 **2,837 个**受检源码、诊断及 Cornell 夹具文件的校验和一致。

| 条件 | 背面高亮命中：双面 → 单面 | 有效黑命中／保留覆盖 | 地面最终线性均值变化 |
|---|---:|---:|---:|
| Hybrid，零阶，4 cm | 9 → 0 | 35,532 / 35,532 | −0.157% |
| SDF Only，零阶，4 cm | 9 → 0 | 4,278 / 4,278 | 0.000% |
| Hybrid，8 阶，4 cm | 11 → 0 | 3,864 / 3,864 | −0.067% |
| SDF Only，零阶，5 mm | 1,933 → 0 | 8,728 / 8,728 | −0.102% |

全部 **52,402** 条材质背面样本 RGB 为零，保留有效 Screen/SDF 类别与非零探针覆盖；零阶源计数检查通过。地面正面发光命中的辐亮度仍为 **200.000046**。四组地面最终均值均在本次 ±1% 检查线内；该短序列不等于长序列能量收敛证明。默认偏移下，零阶天花板原始命中照明为零，最终重建仍有约 **0.000218** 的微小残留；5 mm 压力组最终均值为零。

重新执行生产 Radiosity、Far Probe 和远场缓存重连的 Off / Front / Back GPU 矩阵。Back 下正面均为约 200.00006、背面均为 0；远场黑命中保留有效性与正距离，即使诊断 atlas 两侧都预存亮值也不会错误复用。材质 64 B、节点 48 B 的逐字段布局检查通过，原场景发光值不变，独立诊断 shader 无错误或警告。

```text
E:/VividRP_GI_Evidence/EmissionSidedness-20261114-031949-475/analysis.json
E:/VividRP_GI_Evidence/EmissionSidedness-20261114-031949-475/emission-sidedness.png
E:/VividRP_GI_Evidence/EmissionSidedness-20261114-031949-475/source-before-capture.json
E:/VividRP_GI_Evidence/EmissionSidedness-20261114-031949-475/source-verification.json
E:/VividRP_GI_Evidence/EmissionSidedness-20261114-031949-475/final-verification.json
E:/VividRP_GI_Evidence/EmissionTraceConformance-20261114-031916-377/trace-conformance.json
```

捕帧无运行错误。恢复检查确认诊断回调为空、flags=0、强制双面关闭、frame override=-1、bias=0.04、正常 Radiosity 为 8 阶 / 32 批，两个场景均未脏。未运行 Unity Test Framework（Editor 正在使用），未测量 GPU 耗时；旧卡片的原法线版本迁移与移动机位验证仍不在本轮范围内。
