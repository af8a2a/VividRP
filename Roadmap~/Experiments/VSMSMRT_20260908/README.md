# 方向光 SMRT 原型与 4096 对照

日期：2026-09-08；源码基线 `4c39274a`；VividRP_Reborn / Unity 6000.7.0a6 / Direct3D12。

已接入可选 SMRT，并完成解析深度场、稀疏页面、场景动态与独立性能对照。接触硬化成立，但现有 TSR 仍留下明显噪声，256 页也存在容量压力，暂不建议默认开启。**8 rays × 4 steps 是下一轮噪声优化的对照候选，尚未通过完整画质或 7.3 ms 预算验收。**

## 实现和使用

在 `VividRP/Shadows/Cascaded Shadow Maps` 的 VSM 设置中启用 `Virtual Shadow Map SMRT`。原型默认关闭，参数默认 4 rays、8 samples、最大发散距离 10 m；ray/sample 各支持 4–8。方向光的 `Angular Diameter` 控制软化，SMRT 内限制为不超过 10°。0° 精确进入现有 PCF / hard 分支。建议对照时保留 VSM PCF，它为 SMRT 射线原点积分原有两个 texel 宽的空间覆盖；关闭 PCF 则没有这部分接触抗锯齿。

实现采用光盘射线、逐 texel 边界遍历、有限厚度深度求交，以及有界的一格 gap fill。接收面深度梯度只用于重建偏移后的射线原点，不能作为射线路径。射线达到横向或长度上限后沿主光方向继续查询最终 texel 的远端遮挡；没有把截断直接解释为受光。

反馈覆盖所有随机相位可能访问的支持范围，沿用主采样、过渡和父层用途。缺页/脏页时重试完整父层估计；全部不可用时回到原 PCF 层级。有效空页与未知页面分开处理。SMRT 的有效软阴影不会再与硬阴影取 min。主 Resolve、Debug 与独立复现入口显式绑定同一参数。

这是根据公开 SMRT 思路独立实现的有界单层深度原型，没有核对 Unreal 私有 Shader，也不等价于几何光追。Epic 公开说明了光源采样、接触硬化、单层深度缺口和发散限制；具体厚度、格点遍历、原点覆盖及兜底策略是本项目的选择。[Epic 官方说明](https://dev.epicgames.com/documentation/en-us/unreal-engine/virtual-shadow-maps-in-unreal-engine#softshadowswithshadowmapraytracing)

## 测量条件

固定 4096、1920×1080，保持原 clipmap 布局、transition 0.2、screen density target 1、First Level 0。使用现有 NativeAA TSR、history 16、sharpening 0.483，固定曝光 EV100 12.252064；PCF 开、旧九比较随机滤波关。主质量对照使用临时 512 页排除溢出干扰，另测生产 256 页。

每组至少预热 128 帧并对齐起始相机 jitter；静止 32 帧，平移 96 帧，转动 128 帧，光照变化 128 帧。动态画面每四帧及末帧读回，计数逐帧记录。共 1,664 帧记录、620 帧原始阴影/TSR 前后数据、590 帧诊断数据。质量读回期间的耗时不用于性能结论。

完整记录见 [quality-summary.json](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMSMRT_20260908/quality-summary.json)。两个原始序列仍保留在包内 `Temp~/vsm-smrt-captures/20260908_135958_277` 和 `20260908_141454_723`；临时目录不纳入版本控制。归档的诊断源码与分析脚本位于本目录 `diagnostics`，其中脚本依赖前几轮的读取工具；重新运行场景诊断需要恢复其临时 Editor 入口和容量实验钩子，不能直接当作生产菜单使用。

## 画质与 TSR

下表是 TSR 输出亮度在固定阴影边缘掩码内的同相机 jitter 时间 RMS，越低表示本组时间变化越小，**不代表几何正确性或锯齿误差**。掩码统一从 PCF 生成；32 帧按相机 jitter 分组去均值，未对齐 SMRT 随机相位。区域为屋檐、横梁和拱顶。

| 512 页、0.5° | 屋檐 | 横梁 | 拱顶 |
|---|---:|---:|---:|
| PCF | 0.003047 | 0.002098 | 0.004647 |
| SMRT 4×4 | 0.010699 | 0.008179 | 0.011921 |
| SMRT 4×8 | 0.010386 | 0.008468 | 0.011958 |
| SMRT 8×4 | 0.008035 | 0.005592 | 0.008712 |
| SMRT 8×8 | 0.007986 | 0.005774 | 0.009109 |

8×4 比 4×8 的剩余噪声低，8×8 相比 8×4 没有明显改善当前目标区域。步数同时限制可表达的半影范围，因此不能据此认为所有遮挡距离都应使用四步。当前采样是逐像素扰动的低差异序列，没有绑定蓝噪声纹理，也没有增加专用阴影历史或 SIGMA。

原生静止输出：[PCF](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMSMRT_20260908/views/pcf512_static/last.png)、[4×8](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMSMRT_20260908/views/smrt4x8_static/last.png)、[8×4](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMSMRT_20260908/views/smrt8x4_static/last.png)、[8×8](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMSMRT_20260908/views/smrt8x8_static/last.png)。全画面对照还可见倾斜侧墙及部分拱边的额外颗粒和亮点，需要继续核对自阴影、偏置及单层深度表示；中央区域指标不能替代这些位置的验收。

相机平移、转动时，512 页各组保持 VSM Active，采样诊断没有最终不可用或 L9 兜底。连续改变太阳方向则触发已有的 `ReceiverFeedbackUnavailable`：PCF 和 SMRT 的 128 帧各有 63 帧回到 CSM，因此这些移动光源帧不能当作 active SMRT 验证。

补充使用固定方向、静止相机/接收面、角直径 0.5°→1°→0.5°：PCF、4×8、8×4 三组各 128 帧全程 VSM Active。停止后第 63 帧，屋檐 TSR 输出相对同 jitter 静止参考的 RMS 分别约 0.00223、0.01250、0.00763。这里包含不同随机相位的蒙特卡洛噪声，不能把残差全称为拖影，也不能据此声称精确的历史收敛帧数。

## 页池与边界

静止总页面需求中位数：PCF 419，4×4 / 8×4 为 434，4×8 / 8×8 为 447.5。512 页对照未溢出。相同 4×8 改为 256 页后，静止溢出峰值 195，平移峰值 198；平移的 480×270 诊断采样网格中 L9 样本峰值 2,631。动态粗层回退仍会影响阴影边缘，软化不能代替页面覆盖。

诊断捕获中页表与 metadata 一致、没有重复占用的物理槽或超容量槽，非有限阴影值为 0。`ownership_errors=0` 指这些检查，不是独立反向所有权缓冲验证。512 页没有最终 PCF 兜底不代表实现没有该路径；缺页、脏页、父层完整重试、终端 PCF 和反馈稳定性另由隔离 GPU 检查覆盖。

## 性能

质量读回关闭后，两轮按正反顺序测量，每组 5 秒预热、5 秒记录。以下为各组采样中位数的跨轮范围，单位 ms；原始 CSV 和计数器保留在 `timing`，汇总为 [首轮](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMSMRT_20260908/timing-summary.json) 和 [复测](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMSMRT_20260908/timing-final-summary.json)。

| 配置 | 整帧 GPU 中位数范围 | Resolve + Feedback 中位数范围 |
|---|---:|---:|
| PCF / 512 | 5.384–6.018 | 1.630–2.084 |
| 4×4 / 512 | 5.886–6.505 | 1.615–2.358 |
| 4×8 / 512 | 5.398–7.086 | 1.387–2.501 |
| 8×4 / 512 | 5.694–7.035 | 1.616–2.415 |
| 8×8 / 512 | 5.931–6.372 | 1.461–1.827 |
| PCF / 256 | 6.075–6.098 | 1.865–2.088 |
| 4×8 / 256 | 7.153–7.190 | 2.671–2.692 |

这些计时包含 Editor / 全相机，未归因到选定相机；记录均标为 `completed_with_warnings`、不可认证可比。跨组耗时不单调，不能断言 8×8 比 8×4 更快，也不能可靠推出 SMRT 的精确增量。**尚不能按用户的 7.3 ms 口径验收**，需要相机归因一致的 GPU capture。生产页池保持 256，没有为了这组数字永久扩大到 512。

## 正确性与编译

- 独立 D3D12 诊断完成 21,172 次检查（包括重复有效性断言，不是 21,172 个独立场景）。覆盖 400 条解析薄杆射线、接触硬化、倾斜接收面、4–8 条射线含奇数、单格 gap fill、远端平行兜底，以及稀疏页面合同。证据：[gpu.txt](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMSMRT_20260908/diagnostics/gpu.txt)。
- 单遮挡边界、光盘解析积分的最大可见度绝对误差约 0.00216。[contact.csv](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMSMRT_20260908/diagnostics/contact.csv) 的 0.1 / 0.5 / 1.5 m 项使用实际距离；5 m 项使用被限制为 2 m 的发散长度作解析参考，不是无界 5 m 几何光追精度。该项关闭原点 PCF 覆盖以隔离射线求交。
- 0° 隔离检查走原 PCF 分支；整场跨阶段原始阴影最大差异 0.00048828125（一个半精度量化步长），不声称场景输出逐位相同。
- 最终 DXC 37/37 kernels、Roslyn Runtime / Editor / Tests 三程序集通过；编译前后源码与 Bee 响应均稳定。记录位于 `validation/dxc-final` 和 `validation/csharp-final`；最终刷新后 Unity Console 错误数为 0。
- 新增稳定参数构建与实际 `SetComputeVectorParam` 绑定经 100 次预热、10,000 次调用，当前线程分配为 0 bytes。全 Editor 帧 GC 中位数首轮 14,539 bytes、复测 14,551 bytes，每轮 PCF 与 SMRT 相同；这不等于整个渲染管线或所有线程零分配。
- 交互 Editor 一直打开，没有运行 Unity Test Framework 或 NUnit。相关 `VirtualShadowMapSamplingTests`、`VirtualShadowMapReceiverQualityTests` 已编译，完整 Unity 测试仍应在结束当前 Editor 会话后手动运行。

实现期间修正过 HLSL 条件表达式同时执行两个带 `out shadow` 分支、导致 SMRT 被 PCF 覆盖的问题，现使用明确的分支并加入入口回归检查。归档只采纳修正后的两组完整质量序列。单层深度仍无法恢复所有隐藏遮挡，解析通过不代表 Sponza 全画面无新增漏光。

## 恢复与下一步

实验后已核对：场景与有效 Volume 回到 2048，SMRT 关闭，相机位置/旋转、NativeAA TSR / history 16 / sharpening 0.483 恢复，临时场景对象为 0，临时 Editor 诊断资产和 512 页钩子已删除。太阳角直径为场景原有的 **7.1°**（与已保存场景一致）；本轮场景质量矩阵只测了 0° / 0.5° / 1°，没有验收 7.1°。不要把直接开启原场景得到的结果与本报告 0.5° 截图混比。[最终状态](E:/VividRP_Reborn/Packages/VividRP/Roadmap~/Experiments/VSMSMRT_20260908/final-state.json)

下一轮优先：以 8×4 为对照处理噪声分布和专用阴影历史，保留光照变化响应；同时定位倾斜侧墙亮点与太阳旋转导致的反馈失效。随后在生产页池和相同相机计时口径下复测，决定页面覆盖/预算是否需要调整。继续增加步数对当前 TSR 噪声的收益有限。
