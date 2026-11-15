# Cornell 长窗口 anchor 的生产路径

2026-11-15，Emissive Cornell 已启用 **FP32 / 512 帧 scalar anchor**。保持原生 Screen Probe Gather、16 px 网格和每探针 8 条射线；不调整光源、材质反射率、BRDF、曝光或输出增益。静止天花板的最终 GI 时间方差降低约 **86.64%**，平均 Y 变化 **+0.0653%**。它改善静止收敛，尚不代表全场景无噪点或完成 Unreal 质量对齐。

完整证据：`E:/VividRP_GI_Evidence/ScreenProbeAnchorFinalQuality-20261115-092158-108905`。`decision.json` 汇总资格、质量、旧验证依赖、GPU 成本和正式配置保存结果；原始捕帧、失败尝试及被替代的接口实验均保留。

## 实际行为

- `screenProbeAnchorMaxAge` 的公开范围为 32～512，通用初始值仍为 32。Emissive Cornell 的已保存 Profile 和场景创建工具使用 512。
- 原生方向探针路径在配置大于 128 时，自动将 anchor 当前帧、历史、prepared 和 reconstructed 纹理选为 RGBA32F。128 及以下继续使用 RGBA16F。
- 沿用现有 GPU 参数的 bit 32 表示完整精度；正常渲染自动绑定该位。Editor 诊断字段保持零，age override 为 −1，无须挂接捕帧回调。
- 只有世界空间接收点位移不超过 0.1 mm 时，anchor 才允许收敛到 512。移动点继续使用原有最高 128 / 8 帧的位移衰减；新露出点拒绝旧历史。512 是有效样本年龄上限，不是强制等待 512 帧才显示。
- 配置或纹理描述变化使对应历史失效；稳定后复用已拥有的纹理。

没有新增 CPU / GPU 结构或常量缓冲布局。最终实现复用已验证的 shader 分支；实际编译的 Prepare Anchor、Filter Anchor、Convert To Irradiance 和 Integrate 四个 DX12 kernel，与长窗口实验及本轮 GPU 计时使用的程序逐字节相同。

## 正式参数路径的质量复测

Unity 7000.0.0a16、D3D12、RTX 5070 Ti，1920×1080，固定原机位及既有 128 帧正弦移动轨迹。固定同一份 8 阶 / 64 样本 / 32 批 Radiosity，bias 0.04、四轮 À-trous。每组 1,024 帧预热、128 帧观察，静止和运动各重复两组。

基线直接引用受保护目录 `ScreenProbeCoordinatePrecision-20261115-084302-744764/quality-original`，没有复制其原始载荷。正式候选为 `quality-final512`：通过公开 Volume 参数选择 512，Editor 诊断字段为零，age override 为 −1。每个观察帧检查实际年龄参数和三种 anchor 输出格式。冻结源、探针几何、像素几何及轨迹全部匹配，捕帧错误为空，前后 2,785 份源码 / 输入 / 脚本指纹不变。

统计对象是最终滤波后、消费 coverage 的线性 GI（RGB × coverage），不是最终 SceneColor 或物理参考误差。

| 区域 | 时间方差降低，两次复测 | 平均 Y 变化，两次复测 |
|---|---:|---:|
| 天花板 | 86.6379% / 86.6376% | +0.06530% / +0.06531% |
| 地面 | 85.4966% / 85.4908% | −0.74887% / −0.74927% |
| 后墙 | 94.9367% / 94.9368% | −0.25467% / −0.25463% |

天花板时间 RMS 从约 0.007752 降至 0.002834。128 帧条件分块的方差降低区间约 82.92%～89.26%；这不是跨独立实验或长期漂移的置信界。

固定轨迹中，3,894 个新露出样本分布在 73 帧。最终 GI 相对同帧 Resolve 的差值 P99 为 **2.309261 → 2.311121（+0.0806%）**，最大值 **11.956761 → 12.038663（+0.6850%）**。该差值包括空间滤波影响，不能单独解释为历史拖影。更长运动、历史年龄及板边颜色检查沿用[已通过的运动实验](ScreenProbeAnchorWindowMotion.md)。

`anchor512-ceiling-comparison.png` 显示实际天花板裁片和同一绝对色标的时间 RMS；`plot-inputs.json`、`FinalizeAnchor512Evidence.py` 保存坐标、展示变换及图表生成过程。两组使用同一显示映射，没有拟合亮度增益。

## 收敛、响应与 GPU 代价

[长静止实验](ScreenProbeAnchorPrecisionExperiment.md)的五区能量、独立高样本同渲染器参考，以及[升光 / 降光 / 关灯实验](ScreenProbeAnchorWindowRelight.md)均已通过。当前 GI 源码审计和上述四个实际程序的逐字节一致性支持沿用这些结果。统一发光变化首帧误差约 0.008% / 0.021%，关灯后五区 320 帧 RGB 为零且保留有效覆盖率；不外推到任意局部或彩色光照变化。

GPU 计时独立运行完整 **A/A → 两轮 ABBA**，使用原有严格输入及 GPU 计数资格检查，没有放宽门槛或删除异常值。每臂 1,024 帧预热、128 帧 flush、512 帧测量、64 帧 drain；排除预声明的前后各 64 份 GPU 样本后，每个测量臂保留 447 份，两个配置各四臂。计时使用诊断方式选择同一精度 / 年龄组合，最终生产 shader 程序及精度绑定与之对应。

| GPU marker | FP16 / 128 | FP32 / 512 | 差值 |
|---|---:|---:|---:|
| Diffuse GI Total | 2.9847 ms | 3.1233 ms | **+0.1386 ms（+4.64%）** |
| Convert To Irradiance | 0.1393 ms | 0.1577 ms | +0.0183 ms |
| Anchor Prefilter | 0.0443 ms | 0.0603 ms | +0.0160 ms |
| Resolve | 0.1602 ms | 0.1671 ms | +0.0069 ms |
| Pixel Temporal | 0.3417 ms | 0.3986 ms | +0.0569 ms |

两轮 GI Total 差值分别为 +0.1708 ms / +5.77% 和 +0.1065 ms / +3.54%。这些是当前受控 Editor 工作负载的 GPU 计数，只有两轮 ABBA，不能视为独立大样本性能分布或 Player 整帧开销。部分 marker 有嵌套关系，不能直接相加归因。

四张 anchor 纹理的逻辑容量由 1,305,600 B 增为 2,611,200 B，增加 **1,305,600 B（约 1.245 MiB）**。这不是驱动驻留显存或 RenderGraph 池总量实测。

## 保存与检查

`PromoteCornellAnchor512.cs` 先检查质量、计时及历史门槛，再以正常渲染路径执行 512 → 128 → 512：实际观察 34 / 18 / 640 帧。每次变更首帧均无有效历史，稳定阶段 history 当前 / 前帧始终复用两张纹理。这个检查只订阅相机渲染结束事件，没有 GI 诊断回调。通过后才使用 Unity 资产 API 保存 Cornell Profile；之前的原始资产副本与 hash 位于 `production-before`。

随后 `CheckAnchorFrameBindingAllocations.cs` 对新增精度选择所在的 `BindProbeFrame` 预热 128 次、测量 128 次，调用线程测得 **0 托管字节**。它不是整帧或其他线程分配测量。最后再驱动正常帧，确认 RGBA32F 生效、所有 GI 回调为空、override 清空、场景 / Profile 干净，Editor 恢复为暂停状态、PIX idle、Frame Debugger 关闭。

C# 重编译、公开参数上下限检查、九个相关 DX12 kernel 编译以及上述实时验证通过。最终 Console ground truth 为零错误；会话中历史错误仍保留，没有清空日志来制造通过。Editor 正在运行，因此未运行 Unity Test Framework；没有运行 Player 构建。本次未重新验证 Sponza，也未宣称修复先前诊断中的全部末位重复性差异。

复测工具：

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeAnchorQuality.py NEW_CAPTURE --age 512 --precision float --production
# 在受控 Editor 中执行生成的绝对路径 capture-script.cs，并完成源码 / 输入指纹复核后：
python -B Tools~/Diagnostics/AnalyzeScreenProbeCoordinateQuality.py RUN_ROOT --candidate quality-final512 --baseline BASELINE_ROOT
python -B Tools~/Diagnostics/AnalyzeScreenProbeAnchorGpuCost.py TIMING_ROOT
```

每次质量捕帧二进制上限 192 MiB，本轮计时上限 384 MiB，超限在写入前停止。新的根目录和依赖参考列入[证据保留策略](GiEvidenceRetention.md)；容量审查不会删除当前质量或性能的原始依赖。
