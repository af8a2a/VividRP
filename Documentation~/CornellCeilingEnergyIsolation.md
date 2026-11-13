# Cornell Box 天花板能量差异分层实验

2026-11-13，Unity 7000.0.0a16 / DX12、Unreal 5.7.4 / Lumen Software。

本轮只添加诊断与分析工具，没有修改 GI 算法、默认追踪偏移、场景资产或 Unreal 项目默认设置。VividRP 固定 **16 px 网格、每探针 8 条射线**。Unreal 保持上一轮原生 Epic 对照预算，并非每探针 8 条射线。

数据根目录：`E:/VividRP_Reborn/Logs/CeilingEnergy-20261113/`。`analysis.json` 保存全部分层统计、采集路径、Radiosity 校验和及逐射线证据；`ceiling-ablation.png` 使用统一线性标尺展示发光面片邻域。主比较口径沿用 [线性 GI 输出](LinearGiComparison.md)：消费 coverage 后、接收材质之前的 E/π，没有曝光或亮度匹配。

后续已完成四路单面材质判定及 Cornell 卡片升级，结果见 [发光单面判定与验证](SdfEmissionSidedness.md)；下面保留修正前的定位证据。

## 主要发现

原先天花板 **63.17%** 的差异，约 **90.52%** 的有符号能量差集中在距发光片边缘 32 像素以内；超过亮度 Y=10 的像素贡献约 86.99%。32 px 是本次用于定位的屏幕区域划分，不是预设质量门槛。

Unreal 的消融强烈支持：**屏幕深度厚度造成的近灯误命中与全分辨率时域／上一帧 SceneColor 反馈交互，是本场景亮环的主要来源。** 目前没有逐条导出 UE 光线的命中身份，因此不能把具体的误命中三角形当作已经证实的事实。

| 输出 / 单因素变化 | 天花板平均 Y | 灯片边缘 32 px 内 | 32 px 外 |
|---|---:|---:|---:|
| VividRP 原始线性基准 | 2.873 | 3.960 | 2.718 |
| Unreal 原始线性基准 | 4.688 | 17.117 | 2.915 |
| Unreal 关闭 Screen Traces | 2.764 | 3.666 | 2.636 |
| Unreal 关闭 Radiosity | 3.397 | 15.232 | 1.709 |
| Unreal 两者均关闭 | 0.318 | 0.147 | 0.343 |
| Unreal 关闭探针空间滤波 | 4.537 | 17.171 | 2.735 |
| Unreal 关闭全分辨率 GI 时域 | 3.134 | 5.278 | 2.828 |
| Unreal 屏幕相对深度厚度 0.02 → 0.001 | 2.884 | 4.715 | 2.623 |

关闭探针空间滤波后亮环依然存在，关闭 Radiosity 也不能消除它；关闭屏幕追踪则大幅消除。缩小屏幕深度厚度后，整面均值接近 VividRP，但近灯区域依然偏亮、远区稍暗，不能仅以总均值接近就宣称分布对齐。这个开关仅用于诊断，不替换原始 Unreal 基准，也没有写入项目设置。

这些消融存在交互，表内均值之差不能相加当作独立能量贡献。尤其关闭全分辨率 GI 时域仍会保留其他历史、屏幕来源和 Radiosity。

## Surface Cache Radiosity

VividRP 每组预热 512 帧，随后 32 帧中每隔 4 帧读取一次，共 8 个样本。Radiosity 固定 8 阶、每批 64 样本、32 批；采集状态确认阶数和批次完成，太阳和天空输入均为 0。

Hybrid 与 SDF Only 的发布 Radiosity atlas 在所有采样帧**逐字节一致**，SHA-256 均为：

```text
9a91ff7bc8e9bb38bf179579c158ee537c5e8f00aa34cfabc1a1a5d72219bafa
```

因此这两组的命中来源对比确实固定了 Surface Cache。最终天花板均值 Hybrid **2.86465**、SDF Only **2.87424**，相差约 0.335%。当前没有证据支持通过全局反弹增益、补 π 或增加 bounce 来追平原先的 63%。这不证明 Surface Cache 已经物理正确：其单面发光语义也需要下面的同一修正。

## 命中光照：确认了本侧的单面发光问题

关闭 VividRP Radiosity 后，天花板原始命中光照在部分单帧为 0，但多帧均值并非始终为 0。8 个采样帧、1,044 个天花板探针、每探针 8 条射线中：

- 共 66,816 条射线，**9 条** SDF 命中返回约 **200** 的发光辐亮度；Hybrid 和 SDF Only 的命中相同。
- 这些方向相对于原网格朝下的单面发光法线，全都位于背面。源场景构建器明确将法线设为 `Vector3.down`，材质和 Proxy 均为 `CullMode.Back`。
- 原始命中估计均值约 **0.026934**；最终像素均值 Hybrid **0.029843**、SDF Only **0.030189**。探针均值与像素均值的空间权重不同，不应直接作为同一总体做百分比相减。

代表样本：接收点 `(0.879575, 3.000005, 1.521891)`，方向 `(-0.961556, -0.073948, 0.264466)`，返回 Y≈200。原网格法线与出射方向点积为 **−0.073948**，但 SDF 壳梯度法线约为 `(0.512899, -0.858432, 0.005399)`；两者不能互相代替来决定单面发光。

源码也存在对应缺口：`VividMeshSdfGpuScene` 上传的 `DiffuseSurface.Emission.w` 固定为 0，GPU 结构未携带发光面的材质剔除语义；`TraceHitLighting` 无条件加入 `surface.emission.rgb`。追踪的 shell gradient 背面测试不能保证命中原始材质的正面。太阳 Lambert 分量和卡片间接光又有各自的法线验证，不能只修某一个 Gather 分支。

### 偏移消融否定了直接减小 bias 的办法

实际默认追踪偏移是 **0.04 m**，顶灯与天花板间隔 **0.035 m**。追加了同机位、同序列的单因素实验，只把偏移改为 **0.005 m**，仍关闭 Radiosity：

- 采集状态逐帧确认 `rayBias=0.005`、Published Order=0。
- 高亮发光命中从 **9** 增至 **1,935 / 66,816**；原始命中估计均值升到约 **5.666**。
- 最终天花板均值升到 **4.177**。

这说明 4 cm 偏移目前会偶然避开许多薄壳背面命中；缩小偏移会暴露更大的材质朝向问题。**5 mm 候选已撤回，原默认 4 cm 已恢复。** 需要先修正真实表面／材质的单面发光判定，再评估避免穿越近邻几何的偏移策略，不能把这次失败候选作为质量默认。

逐射线统计中的无权重计数与采样引导 PDF 加权估计分开保存；能量以 `raw_hit` 和最终像素字段为准。

## Gather 重建

固定高样本 Radiosity 的 Hybrid 路径：

| 阶段 | 平均 Y |
|---|---:|
| 原始命中光照，天花板探针 | 2.75790 |
| raw scalar anchor，天花板探针 | 2.75694 |
| temporal scalar anchor，天花板探针 | 2.77260 |
| 逐方向滤波后、校准前 SH，天花板探针 | 2.69518 |
| 校准后 SH，天花板探针 | 2.77163 |
| 全分辨率 Resolve，天花板像素 | 2.86847 |
| 全分辨率时域后，天花板像素 | 2.86664 |
| À-Trous 后，天花板像素 | 2.86465 |

同一像素集合上，Resolve → 最终平均值变化约 **−0.13%**。同一探针集合上，SH 校准主要恢复了锚点附近的能量，没有出现 63% 量级的衰减。探针集合与像素集合的平均值不能直接互除来衡量插值增益。

零阶残留同时存在于命中与重建：原始稀疏错误会进入 scalar anchor、SH 校准和全分辨率历史。不能继续仅加大滤波来压平，否则可能把错误照明保存得更久。

## 下一步实现顺序

1. **先统一命中材质朝向。** 保留 ThinShell 的两侧几何遮挡能力，单独传递或解析材质的单／双面发光语义；用卡片捕获的原网格法线判断发光朝向，避免用壳边梯度直接决定发光。Screen Hit、Mesh SDF、Radiosity 与远场命中使用同一契约。背面是有效黑命中，不能作为 miss 继续穿透到更亮的后表面。
2. **再处理近邻几何与偏移。** 明确物理接收点、SDF 起点和卡片表面位置；在接收面与灯片仅隔 3.5 cm 时避免偏移跳过几何。结合已有 receiver self-hit 修正和体素尺度做局部约束，不把全场景 bias 直接统一成 5 mm。重新验证零阶天花板、正面地面照明和接触遮挡。
3. **随后让历史识别照明来源。** Screen / SDF 切换时对命中身份、发光侧和端点一致性做拒绝／衰减；保持 covered black。先清除错误样本，再收敛 scalar anchor 与方向历史，继续保留 16 px / 8 射线。
4. **最后审计剩余反弹差异。** 建立这组简单盒子的高样本 Lambert 几何参考，固定线性反射率与单面发光，逐卡片点、逐方向检查 SC 再比较 Gather。Unreal 原始、Screen Traces off、薄深度阈值三个输出都保留；用近灯、远区、地面各自能量及固定轨迹时域指标判断，不以一个全局均值作为终点。

首个修正的明确验收项：零阶天花板上已识别的背面发光样本不再返回 200；有效黑命中保持 coverage；地面正面照明保持能量；高样本 Radiosity 固定时不增加卡片反馈；同轨迹新露出区域不增加残留。阈值应根据修正后独立参考与多帧离散度确定，本轮不把任意百分比包装成已通过的质量门槛。

## 工具、复现与限制

- `Tools~/Diagnostics/CeilingEnergyCapture.cs`：默认四组 Unity 分级捕帧。复跑需选新的输出目录；将 `biasControl` 改为 true 可运行独立 5 mm 失败候选。通过 Unity CLI 实时执行，不运行 Unity Test Framework，不保存场景／Profile。
- `Tools~/UnrealCornellReference/CaptureLinearGI.ps1` 新增 `CaseName`、`DiagnosticCommands`，诊断开关只影响独立进程。
- `LinearGiExport.py` 对每个诊断开关都核对 GPU dump 中实际记录的 CVar。首轮数组串接错误导致开关未生效的两个 UE dump 被拒绝；已重采集，旧数据明确排除。
- 首轮 Unity GPU 数据已捕获，但 Vector4 日志序列化失败；恢复程序集锁并修正日志后，完整重跑到 `unity-verified/`。首轮没有参数日志的数据不进入统计。
- `CeilingEnergyExperiment.py` 从真实 GPU 数据生成报告和图，不进行曝光或均值拟合。`unity-bias5mm/` 为额外单因素控制。

```powershell
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/CeilingEnergyCapture.cs --timeout 2200 --format json

# 示例：只关闭 Screen Traces；输出目录必须不存在。
./Tools~/UnrealCornellReference/CaptureLinearGI.ps1 -Output <new-root> -CaseName sdf -DiagnosticCommands @('r.Lumen.ScreenProbeGather.ScreenTraces 0')

python -B Tools~/Diagnostics/LinearGiExport.py export <raw-case> <new-normalized-case>
python -B Tools~/Diagnostics/CeilingEnergyExperiment.py --experiment E:/VividRP_Reborn/Logs/CeilingEnergy-20261113
```

本轮是固定机位的定位实验：UE 每组一帧，Unity 每组 8 个间隔采样帧，不是收敛物理真值、长序列抗鬼影测试或 GPU 耗时对比。所有改动均为诊断工具；最终状态检查应确认回调为空、frame override=-1、诊断开关恢复、实际偏移=0.04、原有场景不脏。

最终诊断脚本 dry-run 编译无诊断；三项 R11G11B10 / EXR / 几何掩码数值测试通过，Python / PowerShell 语法及变更空白检查通过。实时查询确认回调为空、frame override=-1、诊断开关恢复，配置偏移和刷新后的有效偏移均为 0.04，原有两个场景均不脏。没有运行 Unity Test Framework。
