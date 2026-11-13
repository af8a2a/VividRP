# 原生 Screen Probe：覆盖率量化与时域能量反馈

2026-11-13，Unity 7000.0.0a16 / D3D12。Cornell 纯自发光场景，1920×1080、固定 EV100=4、16 px 网格 / 每探针 8 条射线。修正限原生 Screen Probe 的全分辨率历史；ReSTIR 保留原来的历史契约。

## 原因与实现

原历史 RGB 为条件辐亮度 `L`，alpha 将 age、状态和 **8 位 coverage** 打包进一个有限 half。写入时 RGB 对应尚未量化的 `A`，下一帧读取却用量化后的 `Aq` 恢复 `C=L*Aq`，于是历史实际反馈的是 `C*Aq/A`。这个反馈不是单次输出的微小量化误差，会影响长期均值。关闭颜色裁剪或亮度 fast-update 没有消除天花板约 +1.17% 的偏差；单独取消 coverage 对 age 的影响，旧存储格式下偏差反而约 +1.97%。

现在原生路径：

- 历史 RGB 直接存 `C=L*A`，显式 nearest-even 到 half；读回不再乘量化 coverage。输出给 Deferred 的 RGBA 仍是条件 RGB + coverage，Render Graph 对外接口不变。
- 原 alpha 的 age / coverage / 状态打包不变，coverage 继续用于覆盖范围、历史有效性、条件颜色裁剪和 fallback。它不再改变已经存储的 RGB 能量。
- age 置信度使用重投影中通过几何验证的历史 footprint；当前帧 Monte Carlo coverage 不再选择自己的平均权重。保留 coverage 极差时的拒绝、深度 / 法线拒绝、前景遮挡、运动 fast-update 和 YCoCg 裁剪，age 上限仍为 Profile 设置的 32。
- 新的 `CoveredPixelHistoryId` 只供 `ScreenProbeTemporalReprojectionCS` 使用，防止旧条件 RGB 历史或 ReSTIR 历史被误读。其他时域入口继续使用原 ID 和格式。

没有改动光强、Albedo、π、曝光、源 Radiosity、探针网格、射线数或空间滤波强度。没有新增生产 GPU pass，也没有增加历史纹理通道；初次切换历史键需要重新初始化历史。

coverage 自身仍为 8 位估计。本轮保证的是实时 GI 的 covered RGB 反馈，不能据此宣称 coverage 或 APV 混合权重完全无偏。

## GPU 数值检查

`InspectSdfCoveredHistoryEnergyCS` 使用生产编码 / 解码函数，在 8 种 coverage 条件下反馈固定 covered RGB 共 4,096 次。旧格式在 coverage=0.1 时，目标 R=1 最终变成约 1.018995；新格式的非黑结果逐项保持 `(1,2,0.5)`。有效黑样本保持 RGB=0、coverage>0。

实际 Editor 反射检查确认新旧历史 ID 分离，C# 已完成编译。实际场景验证使用原渲染链，数值检查不代替画面验证。

## 同机位画面

Radiosity 固定 8 阶、每批 64 samples、32 batches。每组预热 512 帧，采集连续 512 帧；比较 scene-linear consumed E/π，区域以匹配机位解析 object ID 筛选。发布缓存首尾及组间逐字节相同。

原首组因冷启动的 Gather 历史，地面 Resolve 早期 82 个测量帧有最多 1 个 half ULP 的暂态差异，不能声称全组输入逐位相同。保留原数据，另在已热缓存上重采完整旧格式控制；最终分析通过 `--control temporal-repeat` 使用这组控制，与修正组的 Resolve **全部逐位一致**。Hybrid 引导组原本就全部一致。后续脚本增加独立源缓存预热，再让每个测量组经历相同 Gather 预热。

| 模式 / 区域 | 旧时域相对 Resolve 均值偏差 | 修正后偏差 | 旧最终时间 RMS | 修正后 RMS |
|---|---:|---:|---:|---:|
| Hybrid 引导 / 天花板 | +0.313% | −0.035% | 0.041459 | 0.037857 |
| Hybrid 引导 / 地面 | +0.552% | −0.143% | 0.619866 | 0.624380 |
| Hybrid 引导 / 右墙 | −0.175% | −0.057% | 0.345626 | 0.346054 |
| SDF Only 均匀 / 天花板 | +1.171% | +0.162% | 0.045493 | 0.040507 |
| SDF Only 均匀 / 地面 | +0.064% | −0.143% | 0.587407 | 0.585224 |
| SDF Only 均匀 / 右墙 | −0.165% | −0.037% | 0.431705 | 0.444748 |

正常 Hybrid 引导路径天花板 RMS 下降约 8.69%；地面 / 右墙 RMS 分别增加约 0.73% / 0.12%。均匀模式右墙 RMS 增加约 3.02%，不能把本轮宣传成所有区域噪声都下降。主要收益是消除错误反馈并稳定平均能量；剩余噪声仍需改进。

这些均值差描述有限序列相对于本帧 Resolve 的差异，包含时域滤波的窗口边界与相位影响，不是独立物理渲染真值。后面的 À-trous 仍可能改变最终区域均值，本轮没有同时修改它。

## 固定轨迹

Hybrid 引导，两组同样的 512 帧 / 四次 128 帧正弦往返，横向 0.28 m、前后 0.06 m、yaw −7°。A/B 的 VP、几何与源缓存逐位相同。

- 56,308 个明确新露出样本，两组均无 age>1 的历史继承，Temporal 相对当前 Resolve 的 P99 / max 均为 0。
- 重复机位的帧间 RMS：0.661853 → 0.640481，下降约 3.23%。最大差仍约 30，移动采样噪声远未全部解决。

这是像素历史继承及同姿态重复性验证，不证明全部探针层拖影和动态 GI 误差消失。

## 纯自发光变化

临时代理在测量第 32 帧改变顶灯到 4 倍、四分之一、0；每组先预热 512 帧，再测量 256 帧。记录的是运行时 SDF 场景实际发光值，不仅是脚本请求值。六组实际阶跃都在第 32 帧，变化后的 A/B 发光值逐位相同。首次升光前有一次 Gamma 往返造成的最大 0.00008 辐亮度差（约 0.00004%），已在报告中保留；后续脚本在 1 倍时直接使用原颜色，避免此往返。

- 所有区域的旧 / 新格式均在实际变化首帧达到 90% 响应，并连续保持至少 4 帧；修正组升光首帧响应约 92.2～99.8%。
- 关灯后两组测量区域的最大残留均为 0。
- 修正组首 32 帧的最大归一化过冲约 0.84%，旧组约 1.77%；包含采样波动，不是无噪声物理真值误差。
- 修正组末 64 帧 Temporal 相对自身 Resolve 的均值差约 −0.58～+0.16%。动态 Radiosity / 引导在这组测试中继续更新，两组 Resolve 并非逐位相同，因此不能拿它判断百分之一量级的端到端响应差异；严格同输入的偏差结论来自前面的冻结源实验。

结束后恢复原代理、Profile 与相机，并再次运行 512 帧，确认顶灯约 200、Radiosity 8 阶 / 32 批、场景不脏、回调为空、flags=0、frame override=-1。五个相关 ComputeShader 的最终检查没有 Shader Error；原有编译器 Warning 保留在记录中。没有进行整帧 GC 或 GPU 耗时测量。

## 证据与复现

本机根目录：`E:/VividRP_Reborn/Logs/PixelTemporalEnergy-20261113/`。

- `temporal-ablation/`：颜色裁剪、亮度响应、coverage age、线性历史五组消融；`source-before/` 保存当时源码。
- `temporal-validation/`、`temporal-repeat/`、`temporal-validation-analysis.json`：相同输入的格式 / age 修正、正常 Hybrid 和轨迹。
- `gpu-conservation.json`：4096 次真实 GPU 编码 / 解码反馈检查。
- `temporal-relight/`：临时发光代理的 4 倍升光、四分之一降光、关灯对照。代理和原数组均通过正式 API 设置与恢复，不保存资产。
- `temporal-relight-analysis.json`、`temporal-relight.png`、`final-verification.json`：响应统计 / 曲线与恢复检查。

诊断 bit：2097152 恢复旧条件 RGB 存储；4194304 恢复旧 coverage age；524288 禁用亮度响应；1048576 禁用颜色裁剪。原有 bit 1 / 8 分别跳过完整颜色纠正 / 固定 age 置信度。切换格式控制时必须重置历史；正常 flags=0 使用修正。

```powershell
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/PixelHistoryEnergyValidation.cs --timeout 120 --format json
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/PixelTemporalEnergyCapture.cs --timeout 4500 --format json
python -B Tools~/Diagnostics/PixelTemporalEnergyExperiment.py E:/VividRP_Reborn/Logs/PixelTemporalEnergy-20261113/temporal-validation --control E:/VividRP_Reborn/Logs/PixelTemporalEnergy-20261113/temporal-repeat
```

重跑需选择新的输出根目录，捕获脚本拒绝覆盖既有数据。Editor 运行期间没有启动 Unity Test Framework；运行中的实际渲染与 GPU 检查用于本轮验证。GPU 耗时、Vulkan、Sponza 和动态几何尚未验证，不能宣称整体 Lumen 质量目标已完成。
