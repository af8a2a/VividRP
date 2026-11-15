# Temporal 重投影与微小历史权重见证

2026-11-15，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti。纯自发光 Cornell Box，固定1920×1080机位、16 px / 8 rays、原生Gather、FP16/128、四轮À-trous、源8阶 / 64样本 / 32批。本轮定位了一个将坐标末位差异放大的分支，尚未形成生产画质修复或性能结论。

## 保持资源布局的原 dispatch 见证

[此前四帧PIX](ScreenProbeFourFramePix.md)未复现自然分歧。随后使用已有无效adaptive探针 `(0,68)` 的方向atlas存储见证，不增加纹理、行、资源绑定或旁路dispatch。每帧CPU读回和shader均检查该探针geometry.w<0；有效几何不得覆盖或屏蔽。所有对照使用同一掩码。

每个32位字拆成13/13/6位，分别编码到三层有限且精确可表示的half值。完整GPU纹理仍保存原始SHA；另对见证槽归零计算功能信号SHA，避免把诊断常量或中间量误判成渲染分歧。掩码只覆盖该无效探针：首轮9个bin，后两轮16个bin。有效探针消费者的geometry检查保持原样。

每阶段8组、每组64帧，每组源预热264帧、其他相机零帧；全部源码、协议和实际脚本在各自运行期间核验未变。每阶段临时持有基准与当前组，逻辑上限3 GiB；只保存首次分歧及其前帧的两组完整包，上限192 MiB。

| 证据目录（`E:/VividRP_GI_Evidence/` 下） | 首次保存的功能分歧 | 结果 |
|---|---|---|
| `ScreenProbeUnusedBinWitness-20261115-075715-360125/baseline` | 00/01，第31帧 | 原程序，427个temporal texel不同，max 27.359375 |
| 同目录 `witness` | 00/03，第2帧 | 518个texel不同，max 50.80859375；512份常量见证 |
| `ScreenProbeTargetWitness-20261115-082700-193853/witness` | 00/01，第25帧 | 424个texel不同，max 13.9306640625；另在00/04第2帧有匹配输入分歧 |
| `ScreenProbeTapWitness-20261115-083231-579451/witness` | 00/01，第2帧 | 再现518个texel、max 50.80859375；四个历史tap的原调用见证 |

这些是atlas分量绝对差，不能当成最终像素噪声或画质改善。首轮常量见证的32个字段逐位匹配CPU预期，共16,384次字比较；只代表记录它们的group `(0,68)`，不泛化到全部group。后两轮各保存512份原始384 B编码包（196,608 B），全部独立解码并与capture metadata核验。首轮只保留了四份完整temporal包可独立解码，其余常量值保存在已核验metadata中。

## 目标线程的实际分歧

目标为有效receiver `(74,47)`、方向bin10。第二轮直接记录其geometry、当前composite、hit、plane、重投影clip、历史累积量和邻域借用前结果；第三轮记录四个历史tap实际选择的geometry、地址、lighting、几何权重与最终权重，以及clip、probe/fraction和累积量。

第三轮00/01第2帧：整幅geometry、composite、previous geometry、previous directional功能信号及前帧temporal信号一致。目标线程实际四个tap的geometry、地址和lighting逐位相同；它们还分别匹配保存纹理中的对应位置。共8份实际geometry及8份lighting加载校验通过，不仅依靠pass结束后的整图hash推断输入。

| 目标线程见证 | 00组 | 01组 |
|---|---|---|
| clip | `(1.8890632391,-3.1850004196,0.0305765122,7.8000001907)` | 逐位相同 |
| probe Y | 47.0 | 47.000003814697266 |
| fraction Y | 0.0 | 3.814697265625e-6 |
| 历史total / geometryTotal | 7.589034794e-6 | 1.140370296e-5 |
| tap2最终权重 | 0.0 | 3.814668162e-6 |

因此，见证中最早记录到的差异位于clip之后的屏幕／探针坐标运算。历史total落在 `total > 1e-5` 的两侧。第二轮另一个第2帧匹配输入对也观察到：邻域借用前，一组为零，另一组保留了coverage约1.14e-5的历史估计。

当前 `BorrowFreshDirectionalNeighbors` 以 `lighting.a > 0 && hit.x != 0` 判断是否有center。极小coverage的历史仍能切换center分支，改变donor筛选和后续回填，而非只产生极小的线性亮度变化。这解释了该微小重投影差异如何被放大；尚不能据此认定具体除法指令、驱动、常量传输或同步错误。四个tap的hit/plane及其他线程没有逐项见证，不声称全部原调用输入已覆盖。

下一步分开验证两项可撤回修改：先约束该坐标路径的计算精度，再检查未采样方向的微小历史支持是否应拥有center资格。保持资源布局、16/8与当前默认值，先过短A/A，再做长序列、最终像素能量／噪声和移动边缘验证；不能以消除一个阈值分歧代替最终画质验收。

## 工具、恢复与限制

[未使用bin生成器](../Tools~/Diagnostics/BuildScreenProbeUnusedBinWitnessExperiment.py)、[目标线程生成器](../Tools~/Diagnostics/BuildScreenProbeTargetWitnessExperiment.py)、[四tap生成器](../Tools~/Diagnostics/BuildScreenProbeTapWitnessExperiment.py)保留受控协议；[首轮分析器](../Tools~/Diagnostics/AnalyzeScreenProbeUnusedBinWitness.py)与[目标分析器](../Tools~/Diagnostics/AnalyzeScreenProbeTargetWitness.py)复算载荷SHA、无效存储前置条件、1512条历史内容链接、匹配输入分歧和原始编码包。各轮实际执行的工具版本另按原指纹归档。

原型改变了shader代码与执行时序。见证运行本身复现分歧才有以上归因价值，不将其GPU时间与生产程序比较。首轮脚本初次dry-run因局部函数声明顺序失败，未执行；修正后的实际脚本与两种后续脚本均dry-run通过。三类原型先用临时内存ComputeShader编译四个DX12 kernel，再导入实测；没有创建临时资产或文件系统链接。

全部原型已在当前文件仍匹配自有原型hash时精确撤回。恢复后的temporal程序22,204 B，SHA256 `1527c6fb3bc97ebe3f2d8813a30cd504f732cb503923ce89e998247a217304f8`，与原实际PSO一致；其他三个受检kernel与原版编译字节相同。最终正常帧确认128、16/8、四轮、bias0.04、源8/32，场景干净、hooks为空、override=-1、flags0、PIX idle，Playing=true / Paused=true。Console游标211→213无新error、无drop/reset，编译失败标志为false；旧日志保留。没有运行Unity Test Framework（Editor保持打开）。

所有渲染默认值保持原样。新增二进制读回合计378,101,760 B，另两份原始见证包合计393,216 B；实际目录容量及未全盘重扫的范围见[GI数据保留](GiEvidenceRetention.md)。
