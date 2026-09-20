# VSM 性能与质量优化方案

更新：2026-09-06。依据完整六档单轮基线 **20260905_113329_d0e8beb7**、当前 allocator/receiver 代码及已有 P5 质量诊断。Phase 0 的重绘实验与当前 Game 相机慢速斜移复现已完成，见[实测结果](../Temp~/VSM/Roadmap~/VSMPhase0Findings.md)；新增[四组密度/逐帧恢复对照](../Temp~/VSM/Roadmap~/VSMDensityFindings.md)确认 1/2/4 px 均未通过 256 页预算。后续算法优化与完整质量验收仍待实施。

**决策：Allocate 是首个性能优化对象；过渡质量按密度、覆盖、驻留三类原因诊断。以 4K 固定 256 页作为首个算法对照，以 8K 固定 256 页作为超预算压力对照。** 计时来源检查和质量复现先行，随后分别提交可验证的小改动。

## 1. 完整基线支持什么结论

[完整复核](../Temp~/VSM/Roadmap~/Baselines/20260905_113329_d0e8beb7/README.md)已核对 22 个文件、7,815 条观测和 108 行指标；各档超过 10 秒且 ≥300 条。必需 GPU 阶段覆盖率 100%，场景/相机/光源/参数快照一致，采集编辑器启动参数没有 D3D12 debug。

时间单位 ms，均为未过滤原始观测的 median；GPU 全帧数值仅作当前口径参考。

| 档位 | 全帧 GPU | Allocate | Resolve + Feedback | StaticCasterCull | 末帧驻留 / 请求 / 溢出 |
| --- | ---: | ---: | ---: | ---: | --- |
| 2K Hard | 4.563 | 0.737 | 1.590 | 0.085 | 79 / 79 / 0 |
| 2K PCF | 4.784 | 0.744 | 1.601 | 0.084 | 79 / 79 / 0 |
| 4K Hard | 6.935 | 3.235 | 1.084 | 0.159 | 187 / 187 / 0 |
| 4K PCF | 7.113 | 3.234 | 1.094 | 0.160 | 187 / 187 / 0 |
| 8K Hard | 19.922 | 16.210 | 1.029 | 0.358 | 256 / 491 / 235 |
| 8K PCF | 19.795 | 16.117 | 1.032 | 0.360 | 256 / 491 / 235 |

- 4K/8K Allocate 明显高于同档 Resolve；2K 则仍以 Resolve 更突出。分辨率翻倍时 Allocate 约增长 4.4 倍、再约 5 倍，优先检查随页表大小和缺页数增长的工作。
- Clear 约 0.033–0.036 ms，DynamicRaster 约 0.011–0.013 ms，StaticRaster 约 0.007–0.008 ms，均不是首要收益来源。PageCull 包含在父 cull scope 内，不重复求和。
- Hard/PCF 的 Resolve 差异约 0.003–0.012 ms，但单轮顺序扫描不能证明 PCF 免费，也没有依据先砍 PCF 获取主要收益。
- 8K 的 235 页溢出是末帧压力，不能把其较低 Resolve 时间当作“完整 8K 阴影”的质量/成本优势。末帧 new=0 也不代表整个窗口没有分配或页交换。
- 2K Resolve 反而高于 4K/8K 的原因尚未分解；应检查实际选层、遍历、反馈和驻留，不能从虚拟分辨率推断采样成本或画质单调变化。

**适用边界：**六档性能诊断已完成，完整质量矩阵仍未完成。每约 0.25 秒出现整帧 GPU 极低值，8K 约占 7.8%，疑似 Editor 重绘计时混入；正值/时间戳覆盖率不能证明相机帧来源。只有单轮，revision 为空，独立厂商驱动版本未记录。阶段与全帧数据不同步，不从分位数相加或相减预测精确总帧收益。

## 2. 第一批：计时来源检查与质量复现

### 2.1 建立可验收的计时口径

已完成三轮轮换对照：4 Hz 每轮约 40 次极低 GPU 读数，Manual 三轮均为 0；记录器默认改为 Manual。保留全部原始值，schema 3 分离来源、覆盖率、驻留与质量状态。精确 Game 帧归属仍不能由 FrameTiming 的观测行确定；下面的独立 Player/GPU 捕获验证仍适用。

范围：[录制窗口](../Editor/Tools/VSMBaselineRecorderWindow.cs)、[会话与报告](../Editor/Tools/VSMBaselineSession.cs)。

1. 固定 4K Hard，保持 Game view 连续渲染，做“当前 4 Hz 重绘 / 测量段停止主动重绘 / 改为 1 Hz”的受控对照。采样 Tick 继续独立运行，暂停等输入仍可响应；关闭窗口会结束会话，不能用于该实验。
2. 检查极低 GPU 值的频率是否跟随重绘频率变化；结合目标相机帧、GPU scope 和必要的 GPU 捕获确认来源。仍无法区分时，再补独立 Player 计时路径。当前录制器为 EditorWindow，Player 对照需要实际接入。
3. 按已验证的来源/帧身份改进采样；未知来源标为 mixed/unknown。禁止用固定 1 ms 阈值、删除长帧或挑选最快轮次制造通过结果。原样本和来源诊断都保留。
4. 报告分别显示采样完成、计时来源/覆盖率、驻留/预算、质量验证状态。展示末帧请求/驻留/新增/溢出，并明确它不是整段时序统计；现有 schema 2 原报告不回填。
5. 新运行记录 revision、工作区摘要、项目/相机标识、debug 标记、计时口径、实际驱动版本和调试图状态，不导出完整启动命令行。页状态时序在独立诊断运行中使用有界异步读回，避免污染正式性能窗口。

验收：重绘对照有可解释结果，Editor-only GPU 帧不能认证为 Game 帧；8K 超预算清楚可见；暂停/保存/恢复不依赖主动重绘。至少三轮交替顺序复采，同档 median 相对极差以 ≤5% 为初始稳定目标，超出则调查并延长窗口。

### 2.2 先把质量问题分型

使用 [VSMReceiverDebugPass 协议](../Temp~/VSM/Roadmap~/VSMQualityBaseline.md)，接在选定相机完成的 Resolve 后；质量诊断与性能计量分开。

保留三个输入锚点：

- **性能锚点：**当前 Perf-Camera（原 Camera_1），位置 (0.02020285, 7.66298294, -2.43060470)，FOV=60，1920×1080，用于六档性能对照。
- **密度锚点：**旧 E:/vsm-issue.rdc 墙面相机/ROI。该 ROI 无 fallback、无 transition，采样 level 2，虚拟 texel 约投影 5.09 px；这里的粗颗粒不能归因于过渡权重。
- **过渡锚点：**按用户选择，以当前基线 Game 相机建立世界坐标 (2,0,2) 慢速斜移、固定朝向。已完成两次 601 帧 / 31 组快照，全部 ROI 原始数据复跑一致，实际触发混合与第 460 步的 6 像素回退。原动图的完整轨迹未精确重建。

沿轨迹已记录 preferred/sample/transition level、请求与实际应用的 blend、footprint、缺页原因、密度覆盖约束、全局请求/驻留和实际 Resolve 阴影。四组均有 69 个采样点，440–480 逐帧连续。按层请求/驻留及具体 page ID 的生命周期仍待补充。当前 debug 只给请求 blend；实际是否应用可由“主样本仍在 preferred 且 transition 有效”判断，必要时补诊断字段。

| 现象 | 优先检查 | 改进方向 |
| --- | --- | --- |
| sampled=preferred、无缺页，footprint 偏大 | 选层密度、finer coverage clamp | 验证 Screen Density；PCF 只平滑已有低密度数据 |
| preferred 稳定，sampled/transition 随帧跳级 | dirty/unmapped、预算、下一帧反馈 | 先解决驻留和恢复 |
| 两层完整驻留，跨层仍有亮暗断阶 | 重投影、各层 bias、filter footprint、blend 端点 | 修正采样/覆盖一致性，增加回归 |
| camera cut、光源/动态 caster 更新后残影漏影 | ownership、失效、清理、反馈归属 | 修正缓存状态和帧序列 |

现有代码已有 smoothstep、每层重新投影与 receiver bias、完整 PCF footprint 回退、缺失 coarse 不与 lit 混合、fallback primary 不二次混合及完整 coarse 请求链。先定位这些语义在真实场景中的缺口，不重复新增已经实现的功能。

## 3. 性能优先项：减少 Allocate 的串行工作

范围：[CSMShadowResolve.compute](../Shaders/Core/Private/CSMShadowResolve.compute) 的 VSMPrototypeAllocatePages，以及 [CSMShadowPass.cs](../Runtime/RenderPass/Core/CSMShadowPass.cs) 的 dispatch 和资源依赖。

当前 kernel 为 numthreads(1,1,1)：每帧先遍历 N 项写 snapshot，再两次遍历 N 项处理请求；未驻留请求另搜索最多 C 个物理槽。确认无可驱逐槽后虽停止重复驱逐搜索，后续 miss 仍重复搜索空闲槽。

| 虚拟分辨率 | N：9 层页表项 | 末帧请求数 | 请求/N（仅末帧） |
| --- | ---: | ---: | ---: |
| 2048 | 2,304 | 79 | 3.43% |
| 4096 | 9,216 | 187 | 2.03% |
| 8192 | 36,864 | 491 | 1.33% |

若 8K 稳态与末帧相同，235 次 miss 对应约 60,160 次无空槽搜索，另有 3N=110,592 次页表迭代。这是代码路径推算，不是已测分项耗时。先做隔离 GPU 诊断，区分 snapshot、请求遍历和物理槽搜索成本。

按三个小改动递进，每步独立计时和验证：

**A1：满池时跳过空槽搜索。** 依据已有 allocated count，满池直接进入驱逐/溢出逻辑；有空槽时保持选择最低槽号。覆盖滚动孔洞、同帧换主、满池可驱逐/不可驱逐情况。此步针对 8K 压力路径，不期待解决 4K 的全表扫描。

**A2：并行生成 metadata snapshot。** 拆出 64/128 线程逐页 kernel，后接原串行分配器。每线程只写自己的 snapshot，保持 request bits/timestamp/debug ABI，明确 dispatch 间 UAV 依赖。先验证单独收益；新 kernel 通过资源同步流程注册。

**A3：确定性压缩请求列表。** 使用块计数、前缀和、稳定 scatter，并行筛选当前 frame 请求，让串行分配只遍历实际请求。保留原顺序：terminal coarse 优先，其余层由粗到细，同层按页索引递增。无序 append 不能直接改变饱和时的分配赢家。不可提前剔除 resident 请求，以免 coarse 驱逐细页后丢掉其需求/溢出计数；stale request 清理和 debug snapshot 也须保留。

A2/A3 后重新分析；只有 eviction 仍显著占时，才继续做空槽/候选列表。暂不一次重写为全并行分配器。

必须保持 physical slot 稳定、page table/owner 双向一致、最低槽号和 LRU 同龄规则、camera/frame request 保护、frame zero/reset、terminal/intermediate 优先级、撤销旧映射、新页 dirty 与静态/动态清理、四计数器/debug flags 语义。资源容量有界，RenderGraph 依赖明确，稳定帧零托管分配，不加 CPU 同步读回。

验收以原 kernel 为参考，从相同初始 GPU 状态逐帧比较 page table、owners、metadata、计数器与阴影结果。覆盖零请求、全命中、冷启动、孔洞、coarse 驱逐、同龄淘汰、超预算、反馈重置、多相机。

**工程目标而非收益预测：**A1–A3 后争取 4K Allocate median 降低 ≥30%、8K ≥50%，P95 同向改善，2K 无超出重复噪声的回退。状态不等价则退回分解；不改变预算/过滤来换取性能通过。全帧来源未确认前不宣称精确 FPS 收益。

## 4. 质量优先项：密度、连续性与预算

### Q1：验证 4K + Screen Density + PCF 候选

固定 4K/256 页、FirstLevel=1、Transition=0.2、bias=1/1/2.5，先只切换 Screen Density off/on；on 使用 target=1 px、LOD bias=0，Hard/PCF 分别对照。这是试验候选，未认证为默认档位。

PCF 同轨迹对照已完成：off 的 69 个采样点均无溢出；on 的 target=1/2/4 px 分别有 69/69、69/69、65/69 个点溢出。1 px 改善大量局部足迹，却增加持续回退；4 px 足迹变大的配对像素占 22.893%，变小占 20.270%。因此当前 256 页预算下三者均不进入默认候选。下一步拆分按层需求、覆盖限制与预算拒绝，保留 coarse 保护语义后再评估请求策略或单独的容量实验。Hard 对照、完整画面质量与独立性能仍未认证，见[完整结果](../Temp~/VSM/Roadmap~/VSMDensityFindings.md)。

旧墙面 level 2 的 5.09 px footprint 若能使用覆盖完整的 level 0，按层尺度可降至约 1.27 px；这是局部几何推算，仍可能受 coverage、最细层或驻留限制，不是严格 1 px 保证。先定位限制再调整资源分辨率、FirstLevel 或需求目标，不用 light bias 掩盖牙齿。

### Q2：修复有证据的过渡或缺页连续性问题

- 对完整驻留的相邻层，检查边界两侧逼近时的输出和 blend 端点；覆盖 Hard/PCF、掠射 receiver、页角和 clipmap 边缘。
- 各层使用各自的坐标重投影、normal offset、receiver-plane depth 与完整 footprint；保持 coarse 缺失不变亮、缺页整核 fallback。
- off 的第 460/461 步分别出现 6/24 个 unmapped 回退像素，462 步为 0，期间无 overflow 或 dirty/ownership 位。优先补充 page ID 与首次请求→分配→dirty 清除→有效采样帧序列，区分新暴露、滚动与淘汰后再决定修复。尚不能说同一页连续缺失两帧。扩大 Transition 无法创造缺失的中间层。
- 保留完整 fallback 请求链和现有 terminal/intermediate 分配优先级，避免重复引入已经实现的中间层恢复策略。

验收：合成层切换案例保持既有 1e-5 容差；场景固定 ROI 比较边缘位置、过渡亮度、sample level 跳变和逐帧闪烁。新增漏影或无依据变亮即失败；质量策略改变时记录预期变化的边缘区域，而非强求整图像素不变。

### Q3：8K 压力与稳定降级

8K/256 页保留为压力测试；按层记录请求、resident、fallback、overflow 和恢复帧数，保证 coarse fallback 可用，检查细层反复置换造成的抖动。

独立比较 256/512 页预算，或改变 Screen Density target；一次只改一项，记录质量、显存、clear/raster、allocator 成本。512 页只是覆盖当前 491 个末帧请求的候选，不能保证运动场景零溢出。

当前 MaxPhysicalPageCount=256 为硬上限；扩预算须同时审计 pool、array slices、raster/request/indirect buffers、容量检查与图资源，不能只改一个常量。两张 R32_UINT 池按现有排布：256 页约 32 MiB，512 页约 66.125 MiB；另有约 16→32 MiB 的 Depth32 page array 和其他资源，不能将两池数字当作 VSM 总显存。

只有明确观察到 demand/residency 抖动后，才评估 LOD/residency hysteresis。时间滤波、更大 PCF、软阴影作为后续扩展，不作为页地址、缓存或过渡错误的首修方案。

## 5. 第二性能对象：Resolve / Feedback

分配器收益确认且质量语义固定后再实施。2K Resolve 约 1.6 ms 仍值得优化；4K/8K 先解决 Allocate。

TryEvaluateVSMProjection 即使不再比较深度，也继续投影并请求完整 fallback 链；MarkVSMReceiverPage 对每像素每层的 distinct page 执行 OR/Max 原子写，页角最多四页。邻近像素的重复请求确实存在，原子争用占比仍待测。

先固定反馈与驻留快照，用隔离诊断变体区分采样/投影与原子成本；禁用反馈后持续运行会改变下一帧状态，不能直接把整段差异当成反馈耗时。

首个候选为 wave 内按相同 page/request bits/frame stamp 精确合并。处理循环/分支内实际 active lanes 和不同 wave 大小，设备能力不足保留原路径。保持 PCF halo、terminal coarse 标志及完整中间层请求，不跨层/相机/帧合并不等价项。无收益后再考虑小型 group 合并，不先引入大规模全局哈希表。

验收：请求集合、OR/MAX metadata 和后续分配完全等价；慢移、页角、跨层、缺页/超预算不增加恢复延迟。工程目标为 Resolve median 改善 ≥10%、P95 不回退且收益超过重复噪声，不等于整帧同比提速。

## 6. 执行顺序与验收矩阵

| 批次 | 交付 | 完成判据 |
| --- | --- | --- |
| 0 | 重绘计时 A/B、报告状态区分；质量轨迹/ROI | 来源有证据，预算可见，原过渡问题可重复 |
| 1 | A1 满池快速路径、A2 并行 snapshot，分别提交 | 与原分配器逐帧状态等价，每步有实测 |
| 2 | A3 确定性请求压缩；Q1/Q2 最小修复 | 4K 主对照与 8K 压力通过，质量改善有证据 |
| 3 | Resolve 精确去重；Q3 独立预算实验 | 反馈语义等价，质量/显存/时长成套记录 |
| 4 | 重复全矩阵、选择默认档位、关闭 P5 验收项 | 性能、质量、缓存、GC、目标平台均可复核 |

计量与质量复现可以独立推进，质量诊断无需等待分配器完成。算法、质量策略、预算变化分别对照，避免一个样本同时改变多个原因。

正式性能比较固定场景/相机轨迹、光源、尺寸、VT/动画、预算、调试开关；每档预热 ≥5 秒，采样 ≥10 秒且 ≥300 个已确认来源的有效观测，至少三轮交替顺序。修改前后各自复采；旧单轮数据只作起点。冷启动、失效/重建和 camera cut 另设窗口，真实尖峰保留，报告 median/P95/max 与覆盖率。

质量矩阵包括固定墙面、平面斜坡/掠射、页边/页角、clipmap 与密度 LOD 边界、慢速斜移、FOV 变化、camera cut、静态失效、动态/Unity 混合 caster、alpha/VT 更新、超预算、多相机换主。记录无 usable sample 比例、fallback 层差、sampled footprint、请求到有效样本的恢复帧数和边缘闪烁；除预先定义的超预算终端策略外不得新增漏影。

代码验证使用定向 Roslyn 与 DXC，覆盖所有受影响生产/诊断/test entry。以 VirtualShadowMapSamplingTests、CascadedShadowSettingsVolumeTests、VirtualShadowMapClipmapTests、VirtualShadowMapReceiverQualityTests、VSMReceiverDebugPassTests 为基础补真实失败案例。稳定热路径预热后验证零托管分配，再用 Profiler 检查相关线程。Editor 开启期间不主动启动 Unity Test Framework；编译不能替代 GPU/场景验收。

## 7. 记录与当前状态

- [最新原始报告及审计](../Temp~/VSM/Roadmap~/Baselines/20260905_113329_d0e8beb7/README.md)：本文数值来源，保留全部原始样本。
- [旧方案存档](../Temp~/VSM/Roadmap~/Baselines/20260905_103520_f847acad/optimization-plan-history.md)：保留上一轮不完整数据下的推导。
- [P5-A 质量协议](../Temp~/VSM/Roadmap~/VSMQualityBaseline.md)、[P5-B 选择语义](VSMReceiverQuality.md)：性能相机与旧质量 ROI 不混用。
- 已完成：完整六档单轮性能采集与统计审计。
- 待完成：计时来源隔离、三轮重复性、P5-B 场景质量矩阵，以及本文提出的优化实施与验收。
