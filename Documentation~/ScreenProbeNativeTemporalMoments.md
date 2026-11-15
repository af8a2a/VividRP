# 原生 Screen Probe 的像素时域方差

原生 Screen Probe Gather 现在保存全分辨率对数亮度中心方差，供 À-trous 判断成熟照明边界。Cornell 光片上方 28 个天花板探针中心的最终参考 RMS 误差由 0.119–0.122 降至 0.014–0.017。**收益是减少空间滤波引入的提亮偏差；局部时域 RMS 有所增加，尚不能宣称整幅噪声下降或移动质量通过验收。**

证据目录：`E:/VividRP_GI_Evidence/ScreenProbeNativeMoments-20261115-1400`。Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti；原生 Gather，16 px / 每探针 8 条射线，age512 / FP32 anchor，4-pass À-trous，bias=0.04。冻结 Surface Cache 为 8 阶 / 64 样本 / 32 批，源 SHA-256 为 `40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。没有调整采样预算、反弹增益、保存的场景或 Profile。

## 方差与滤波

`ScreenProbeTemporalReprojectionWithMomentsCS` 在现有原生像素时域 kernel 上增加 RGBA16F 历史：`log2(1+Y)` 的均值、中心方差、有效年龄、有效覆盖率。使用同一组经过运动、几何和前景遮挡检查的四个重投影 tap，按覆盖权重合并中心矩；避免半精度中 `E[x²]-E[x]²` 的消减误差。

光照重缩放同时调整矩。有效黑命中在光照不变时保留单位 Jacobian，避免零亮度的除法保护错误清空方差。历史矩的年龄独立记录，同样受到覆盖一致性、快速更新、邻域裁剪和原有历史拒绝约束。新露出像素从年龄 1、方差 0 开始。照明 RGB、原有覆盖编码及其累积公式保持原样；未启用像素时域或空间去噪时使用原入口，ReSTIR 保持其独立方差路径。

À-trous 首轮在有效年龄 2–8 间逐步由空间方差先验过渡到时域方差，同时将对数亮度权重的下限从 0.15 过渡到 0.01。新历史仍用原来的宽松先验，成熟历史减少跨真实照明边界的平均。后续尺度沿用已有方差传播与粗尺度几何保护。稀疏探针和相邻像素存在相关性，观测方差没有直接除以年龄。

新增历史由现有 CameraHistory 系统拥有、重建和释放。1080p 下两个 RGBA16F 纹理的描述符载荷共 33,177,600 字节，约 31.64 MiB；这不是实际显存驻留增量测量。稳定帧没有新增集合、委托或临时描述符分配。

## 严格静态对照

`control` 在同一程序集和同一 wavelet shader 中临时关闭新增 moments kernel，并在 `finally` 恢复；`final-moments` 使用最终实现。各两次独立静态重复，每次预热 1,024 帧、测量 128 帧。实际深度、GBuffer0/1、自适应布点、VP / 逆 VP、派生探针和 ROI 像素几何全部精确相同，28 点全部精确匹配冻结源的 16,384-ray 参考，没有位置容差、剔帧或增益拟合。

| 28 点指标 | 对照，两次重复 | 最终 moments，两次重复 |
|---|---:|---:|
| 平均消耗 Y | 0.37035 / 0.36756 | 0.26361 / 0.25962 |
| 高采样参考平均 Y | 0.25265 | 0.25265 |
| 参考 RMS 误差 | 0.12161 / 0.11894 | 0.01652 / 0.01436 |
| 时域 RMS | 0.00218 / 0.00172 | 0.00242 / 0.00199 |

最终版第一次重复的像素时域、step 1、step 2 均值分别为 0.25576、0.26073、0.26361；step 4 / 8 因既有几何保护保留该结果。旧路径前两轮把约 0.255 提亮到约 0.370。成熟 moments 的有效年龄达到 32，记录有限、方差非负。

较大 ceiling / floor / back ROI 的平均能量变化最大约 1.05%。地面时域噪声有所减少，天花板与背墙 ROI 的时域 RMS 有增加。不能把参考误差下降等同于全场景噪声下降。`native-moments-stages.png` 同时展示均值和时域 RMS；`native-moments-gi.png` 为两个单帧的未调制间接照明，同一固定显示变换，不是完整 SceneColor 或时域平均图。

早期 `before` 捕获有 8 个探针帧与参考不匹配，保留为阶段诊断；验收使用上述同程序 `control`。黑色历史 Jacobian 修正前的数据保存在 `moments` / `motion-moments`，最终版另存 `final-moments` / `final-motion`，未覆盖早期结果。

## 移动、耗时与恢复

`motion-control` / `final-motion` 各两次静态、两次固定轨迹。两组派生几何及矩阵完全一致，但 3 个移动帧出现 4 条全屏 GBuffer 哈希差异，故整组 `qualified=false`，没有降低严格输入检查。

每次轨迹各识别 3,894 个新露出像素，旧像素历史接受数均为 0，像素时域减当前 Resolve 的 P99 / 最大差均为 0。最终 moments 年龄全部为 1、方差为 0。最终减 Resolve 的移动 P99，对照为 2.230 / 2.044，候选为 1.795 / 2.096；该量包含空间滤波且输入未完全匹配，不作为整体移动质量不回退的证据。

一次正常加载状态的 GPU 观测获得 194 个有效、count=1 的样本：GI 总均值 3.145 ms、P95 3.536 ms；像素时域均值 0.412 ms，四轮 À-trous 均值合计约 1.521 ms。**未组成合格的 GPU ABBA 对照，不发布新增开销或性能不回退结论。**

新增绑定与 `BindProbeFrame` 一起预热 128 次、测量 128 次，当前线程托管分配为 0；不代表整帧分配。矩历史在渲染回调内确认有效。最初帧外调用 `CameraHistoryTexture.IsValid()` 的恢复检查失败，因为该 API 要求上一提交序列对应当前活动相机帧；保留失败记录，改为帧内检查有效性、帧外检查描述符，没有改动历史系统。

实际 C# 程序集、7 个 DX12 变体、静态和移动 GPU 读回、payload hash、冻结源与捕获期间源码检查通过。5 个源码快照工具测试、10 个证据保留工具测试通过；活动 Editor 内未运行 Unity Test Framework。

本机仅重新导入 include 时曾取得旧 wavelet 程序；刷新根 compute 后确认两个入口的程序更新。最终关键程序为：

- `ScreenProbeTemporalReprojectionWithMomentsCS`：`2f009ec8574f8921513655bad2b79dd17723b0ddff3cc2411ba1b9cb2a4f1151`
- `FilterSdfDiffuseAtrous`：`5447830ec42be1fa4b008d2b95e2cc928198a628120aa3bb436ad65999ef6067`
- `FilterSdfDiffuseAtrousFinal`：`c6174c8cec942736fdf40dcf8962bf99bf8d5ecb0bf529608ae7ebaa824d2da3`

保存的正常设置仍为 16 px / 8 rays、age512、4-pass、bias=0.04。最终核对实际 pass 程序、moments 历史尺寸、清空回调和诊断覆盖参数，场景及 Profile 干净，播放暂停。证据纳入保留清单，存储巡检见本目录 `post-experiment-storage.json`。

本轮目录约 3.198 GiB；结合上一份清单估算证据总量约 119.095 GiB，E 盘剩余约 116.905 GiB，未触及 125 / 64 GiB 审查线。未发现重解析链接，无删除操作；精确分析脚本及最终源码已独立归档。

## 复现

```powershell
python -B Tools~/Diagnostics/BuildScreenProbeAtrousExperiment.py NEW_STATIC
python -B Tools~/Diagnostics/BuildScreenProbeAtrousExperiment.py NEW_CONTROL --disable-native-moments
python -B Tools~/Diagnostics/BuildScreenProbeAtrousExperiment.py NEW_MOTION --motion-history
python -B Tools~/Diagnostics/AnalyzeScreenProbeAtrousExperiment.py NEW_STATIC --baseline NEW_CONTROL --reference-root E:/VividRP_GI_Evidence/ScreenProbeResidualMap-20261115-104052-278383
python -B Tools~/Diagnostics/AnalyzeScreenProbeAtrousExperiment.py NEW_MOTION --baseline MOTION_CONTROL --motion-only
```

生成后先 pin 实际 C# 脚本和源码，再通过活动 Unity CLI 执行，结束立即核验快照。每次捕获上限 512 MiB；复用既有高采样参考，没有复制参考图像序列。下一步优先在不重新引入亮度偏差的前提下改善成熟历史的平面噪声，并定位固定轨迹的 GBuffer 非确定性。
