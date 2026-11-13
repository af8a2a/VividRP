# Cornell Box：可比较的线性 GI 输出

2026-11-13，本机 Unity 7000.0.0a16 / VividRP 与 Unreal 5.7.4 / Lumen Software。

最终数据入口：`E:/VividRP_Reborn/Logs/LinearGI-20261113/linear/`。同机位、1920×1080，固定曝光的纯顶部自发光 Cornell Box；没有从 PNG 或 Tonemapper 结果反推 GI。

天花板约 63% 差异的后续消融与实现优先级见 [天花板能量分层实验](CornellCeilingEnergyIsolation.md)。

## 公共口径

主字段 `e_over_pi.exr` 是**已实际消费覆盖率的、接收面材质之前的 E/π 估计**，采用线性 sRGB / Rec.709 D65 RGB。没有显示曝光、Tonemapping、接收面 Albedo 或接收面 BRDF。RGB 保持原始场景亮度尺度，没有对两边做平均亮度匹配或艺术增益。格式为 float32 EXR，ZIP 无损压缩。

| 文件 | 定义 |
|---|---|
| `e_over_pi.exr` | 消费覆盖率后的 E/π，主比较字段 |
| `irradiance.exr` | `π × e_over_pi`；仅显式转换积分约定 |
| `lambert_radiance.exr` | `albedo × e_over_pi`；派生 Lambert 出射辐亮度，不冒充原生 OpenPBR / Default Lit 结果 |
| `albedo.exr` | 从实际 GPU GBuffer 解码的线性 Base Color；本场景 Metallic = 0 |
| `coverage.exr` | VividRP 为实际 GI alpha；UE 为几何有效性，UE 原生的分数追踪覆盖率不可用，两者不能直接按数值等同比较 |
| `receiver_mask.exr` | 真实几何有效性与公共场景掩码的交集；排除背景和发光面片本身 |
| `object_id.exr` | 公共场景的解析可见性标签，供区域统计使用；不是 GPU Object ID |
| `normal.exr` / `depth_m.exr` | GPU 几何，统一为 Unity 世界轴和沿相机前向的米制 eye depth |
| `conditional_e_over_pi.exr` | 仅 VividRP，保留未乘 GI alpha 的条件估计，便于检查覆盖率；不要拿它直接与 UE 主字段比较 |
| `metadata.json` | 数据口径、原生格式、曝光、源数据、采集条件、相机与几何验证结果 |

统一图像原点为左上。VividRP D3D12 readback 的原点为左下，转换时只翻转行，没有缩放或重采样。UE 厘米和世界轴 `(X,Y,Z)` 转成 Unity 米和 `(Y,Z,X)`；相机与模型仍使用之前只读导出的 `unity_fixture.json`。

VividRP `RealtimeOnly` 在 Deferred 消费 GI 时执行 `gi.rgb × gi.a`；导出忠实保留该语义，不能再额外乘一次 `coverage`。UE 的实际 Lumen diffuse 输入在 `ScreenProbeIntegrateCS` 内执行 `DiffuseLighting × Diffuse_Lambert(1)`，之后由 `DiffuseIndirectComposite` 乘接收面 DiffuseColor。本轮捕获的是这个 Composite 的真实输入。

## 采集链路

### VividRP

- `SdfDiffuseGatherPass.EditorLinearCapture` 只在 Editor 诊断时启用，正常路径为空，不增加 GPU pass 或 GPU 资源。
- `Editor/Resources/LinearGiExport.compute` 在 GI 最终输出之后，读取同一份 depth / GBuffer，将 GI、覆盖率、sRGB SRV 解码后的 Albedo 和几何导出为 RGBA32F。
- `Tools~/Diagnostics/LinearGiCapture.cs` 捕获 EV4 和 EV6；使用临时 Volume Profile，关闭附加近场遮挡，保持原生 16 px / 8 射线、Radiosity 8 阶 / 32 批。结束恢复原活动场景、相机状态、Profile 和回调，不保存用户场景。
- GI 本身未应用预曝光，因此原生输出的撤销倍率是 1。

### Unreal

- 没有修改引擎或从最终 SceneColor 除以 Albedo。通过内置 `DumpGPU`，将 root 限定到 `DiffuseIndirectComposite*`，读取实际绑定的 `Lumen.ScreenProbeGather.DiffuseIndirect`、GBuffer 和深度。
- 原生 GI 存储为 `PF_FloatR11G11B10`；按原始无符号浮点位布局解码，除以已记录的实际 PreExposure override。转换为 float32 不会恢复原生格式已经丢失的精度。
- 直接运行固定分辨率的游戏渲染视口，捕获第 512 帧，不使用 HighResScreenshot，也不重建高分辨率 dummy viewport。
- 专用 `BP_LinearGICaptureGameMode` 不生成 Pawn、Spectator Pawn 或 HUD，仅通过 URL 覆盖用于采集；参考关卡和项目默认 GameMode 不变。首次采集曾发现默认 Pawn 球形网格遮住地面，已被几何校验拒绝，相关原始数据保留在 `clean/` 等诊断目录，不作为最终基准。
- 关闭额外 ShortRangeAO / 常规 AO；保留 Lumen 本身的几何可见性、Radiosity、Screen Traces、原生质量与材质。此状态只存在于诊断进程。

## 预曝光与防亮点钳制

Lumen 的 `r.Lumen.ScreenProbeGather.MaxRayIntensity` 在**预曝光空间**中钳制射线照明；Radiosity 也有相对于曝光的钳制。只改 PreExposure 而固定阈值，改变的是渲染算法的物理亮度截断上限，不只是存储倍率。

主基准采用 EV4 对应的 PreExposure = **1/16**，并保留原生阈值：Gather 10、Radiosity 40、Reflections 40。验证组采用 PreExposure = **1/8**，阈值同比变成 20 / 80 / 80，使两组的物理截断上限一致。这些验证覆盖只作用于独立采集进程。

`LinearGiExport.py` 核对 GPU dump 中真实记录的渲染 CVar，并进行独立的成对归一化验证。不能从 `SceneColor / 发光颜色` 推断精确 PreExposure：发光表面本身也接收反射照明，且 SceneColor 是叠加后的资源。

首次将 PreExposure 从 1 改为 2、阈值不变的实验，归一化后平均 Y 相差 **−46.65%**；这是钳制条件改变的证据，明确没有计为通过。记录保存在 `paired/unreal-preexposure-clamp-diagnostic.json`，用于避免后续误用。

## 本次验证

导出时验证实际分辨率、几何有效性、深度、法线和 GPU Albedo；不能通过校验的数据不生成可消费的 metadata。对比时检查口径、相机、fixture hash、预曝光、材质和 coverage 标志，拒绝把条件估计或显示图混入比较。

- 两边几何有效性与公共场景的分歧均为 **0**。去除边缘后，深度相对误差 P99：VividRP 约 0.00051%，UE 约 0.162%；法线平均点积约 1.000 / 0.99991。误差包括 GBuffer 量化与子像素抖动。
- GPU Albedo 相对场景线性输入的绝对误差 P99 约 0.00347 / 0.00279；保留了真实 GBuffer 的量化差异。
- VividRP EV4 → EV6：有效接收像素平均线性 Y 变化 **+0.1109%**；各物体区域的最大绝对均值变化约 0.63%。
- UE PreExposure 1/16 → 1/8，保持物理钳制一致后：平均线性 Y 变化 **+0.00634%**；各区域最大绝对均值变化约 0.068%。
- 所有交付通道均为有限值，float32 EXR 写入后逐值读回检查通过；独立数值测试覆盖 R11G11B10 已知编码、HDR / signed EXR 往返和解析几何掩码。
- 最终捕帧 C# 脚本 dry-run 编译通过；实时 Editor 查询导出 compute shader 无诊断、捕帧回调已清空，两个原有场景均未标记为脏。PowerShell / Python 语法和变更空白检查通过；没有在运行中的 Editor 启动 Unity Test Framework。

这些是各一帧输出的口径验证，不是长序列时域方差或独立物理真值。`cross-engine.json` 给出同口径区域统计；不因两引擎均值不同就自动归一化，也不宣称 Lumen 是无偏参考。当前数据中 UE 的平均 E/π 比 VividRP 高约 9.33%，地面约低 2.75%，天花板约高 63.17%；分布并非单一全局增益。这些差异可作为后续定位依据，不能单凭这一帧认定根因。

## 复现

```powershell
# Unity Editor 保持打开；不运行 Unity Test Framework。
# 复跑前将 LinearGiCapture.cs 的 output 常量改为新的空目录。
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/LinearGiCapture.cs --timeout 700 --format json

# Unreal：首次先用完整 Editor -nullrhi -ExecutePythonScript 执行
# Tools~/UnrealCornellReference/CreateLinearCaptureGameMode.py，等待进程退出。
./Tools~/UnrealCornellReference/CaptureLinearGI.ps1 -PreExposure 0.0625
./Tools~/UnrealCornellReference/CaptureLinearGI.ps1 -PreExposure 0.125

python Tools~/Diagnostics/LinearGiExport.py export <raw-case-directory> <new-output-directory>
python Tools~/Diagnostics/LinearGiExport.py compare <dataset-A> <dataset-B> <report.json>
python -B Tools~/Diagnostics/test_linear_gi_export.py
```

Python 依赖为 numpy、scipy、OpenEXR。采集与导出拒绝覆盖已有输出，请为复跑使用新的目录。Windows 受限沙盒中的原生 OpenEXR DLL 可能无法访问 AppContainer 临时目录；本轮文件写入与测试在获准的本机执行环境中完成，没有修改测试判断。
