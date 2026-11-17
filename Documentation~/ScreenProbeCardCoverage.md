# 光片边缘卡片覆盖与返回光照

2026-11-16，承接 [同方向首命中分解](ScreenProbeFirstHitOracle.md)。本轮完成了边缘缺失定位、受限修复候选和实际 GPU 返回光照验证，但**未通过最终画面验收**。生产两个 shader 已恢复；候选仅保存在 `Tools~/Diagnostics/ScreenProbePlanarCardCandidate.hlsl.txt`，不由运行时引用。固定云状斑块尚未修复。

证据目录：`E:/VividRP_GI_Evidence/ScreenProbeCardCoverage-20261116`。端点结果为 `endpoints.json`，最终图观察为 `quality-observed.json/png`，重复性失败为 `repeatability.json`，原补丁为 `withdrawn-candidate.patch`。全部原始捕获及失败记录保留。

## 定位结果与候选边界

从旧归档取出 55,624 条实际 receiver / direction，保持方向不变。重新导出 live 实例、SDF field、变换、材质和卡片纹理，按几何与材质匹配旧身份。发光片的两张 Y 卡片均有完整覆盖；边缘 SDF 梯度却接近水平，原查询据此选择侧卡，或因投影越界、法线不一致而拒绝真实平面。

光片局部 bounds 含每侧 1 mm 的烘焙 padding。第一版候选多接受了一条只落在 padding 内的射线，已在后续候选中排除。最终候选仅在原查询失败、ThinShell 且 bounds 表明轴对齐零厚度平面时，用射线与捕获深度平面求交，再按原卡片 coverage / 深度 / 原材质法线建立表面。移动距离限于已有 `1.75 voxel + 0.5 shellThickness`，不放宽全场景法线或深度门槛，原来接受的查询保持逐字节一致。

旧报告的“精确首命中也是光片”以物理 receiver 为起点；本轮另检查实际偏移后射线。因此不能把旧记录的全部 missing 都直接变成有效覆盖：

| 固定方向的 SDF 重放 | 原 missing 光片命中 | 从实际偏移起点确实交光片 | 在有界距离内补回 | 新增光片外接受 |
| --- | ---: | ---: | ---: | ---: |
| 自适应 1 cm 诊断 | 44 | 11 | 7（3 正面、4 背面） | 0 |
| 4 cm | 40 | 5 | 3（均正面） | 0 |

这里的端点重放采用归档的 field-diagonal marcher 以复现旧端点，绕过 Screen 路径；它不代表生产 192 步下每帧能补回相同数量。最终像素实验另用原始生产追踪预算。

原查询已接受、但实际偏移射线交点在光片外的样本仍有 72 / 283 条。候选刻意保留原有效查询，因此没有解决整个 SDF 膨胀轮廓、遮挡连续性与首命中选择问题。不能将这 7 / 3 条局部修复等同于完整光片边缘修复。

## 返回光照验证

`candidate-lighting-v3` 在 GPU 上调用生产 `TraceSdfSurfaceRadiance`，使用已捕获的冻结 Radiosity，SHA 为 `40252e7a…93270a8`。源纹理与前八个对象卡片槽位均验证后才允许重放。前两次尝试读取共享 pass / 相机当前 history 时源 SHA 不符，均停止；没有改成忽略源哈希。

补回正面的 RGB 约为 201.39–204.11，包含约 200 的 emission 与反射光。对六个正面端点分别运行 131,072 样本、八次漫反射的独立精确 box / quad Lambert 参考，返回亮度相对误差为 **+0.017% 至 +0.093%**。四个背面端点返回 RGB0，并保留有效覆盖；没有把背面改成透射或未解析 miss。

该参考检验补回端点的局部返回值；不是 OpenPBR / Lumen 等价验证，也不覆盖所有已接受端点。原 SDF hit distance / gradient 仍作为原追踪元数据保留。

## 最终画面：没有通过验收

两组基线和两组候选都使用 sequence1733、16 px / 8 rays、4 cm 偏移、原 192 步 marcher、anchor512 / pixel32 / 四轮小波，源固定为八阶 / 64 samples / 32 batches。每组从冷 Gather history 预热 1024 帧，再记录 128 帧；没有增加生产射线、迭代预算或添加能量增益。

各运行的原生结果与独立像素历史重放均通过 132 组 Resolve 和 3,300 组历史纹理逐字节检查。每次捕获前固定源码与执行脚本，捕获后 2,783 项源文件验证未变。2,336 个最终 GI 像素的 GPU 位置、法线与旧物理参考完全对应；按 ROI 和真实物体分别检查均值图 MSE、平均能量、去均值空间残差、时间方差与帧差。

但是跨运行严格配对失败：

- 候选第一次测量在 frame1024 出现 probe geometry 差异；重跑在 frame1024、1025、1054 仍出现差异，位置差量级低于 1 µm。没有跳过这些帧或放宽容差。
- 相同代码、同序列的两次基线以及两次候选也未达到逐字节重复，最早差异在冷启动 frame0。因此原生 / 私有历史相等，只能证明本次重放正确，不能证明跨运行 A/B 已被控制。
- 已保存的平均图和数值明确标注 `descriptiveOnly=true`、`acceptanceEligible=false`。下面是第二组捕获的实际观察，不作候选因果效应或改善幅度的证明。

| ROI | 平均能量误差，基线→候选 | 均值图 MSE 变化 | 时间方差变化 |
| --- | ---: | ---: | ---: |
| ceiling | −0.519% → −0.470% | +4.74% | −6.97% |
| floor | +2.356% → +2.671% | +11.72% | −17.66% |
| back | −1.118% → −1.178% | +1.10% | −26.24% |
| edge | +0.949% → +0.368% | +21.38% | +8.31% |

这些观察展示了时间方差下降时平均图仍可能更差。128 帧均值还包含相关低频噪声，不能当作无限收敛后的固定偏差。混合 ROI 的相反能量误差也可能抵消；按物体拆分后 Tall White Block 的平均能量误差为 −4.91% → −5.99%，完整 RGB 与参考不确定性保留在报告中。

既定空间 MSE / 能量标准与重复性资格都没有通过，因此停止推广，恢复原生产算法；没有用时间方差收益覆盖空间误差，也没有提高采样预算重试。

## 工具、恢复与后续

- `BuildScreenProbeCardCoverage.py` / `AnalyzeScreenProbeCardCoverage.py`：同方向卡片、材质侧、实际返回光照及端点物理参考。
- `BuildScreenProbeCardQuality.py` / `AnalyzeScreenProbeCardQuality.py`：在内存安装原算法或脱离生产的候选，保持原预算；严格模式遇到几何差异直接失败，描述模式永不具备验收资格。
- 脱离生产后的候选 builder 与实际 GPU 测试过的 shader 源码完全一致。候选未提交到运行时，也未提交 Git commit。
- 16 项首命中 / Lambert / 起点诊断测试通过；候选及原生产九个追踪 kernel 均实际完成 D3D12 编译。活动 Editor 未执行 Unity Test Framework。
- 已恢复开始时的 EditMode；两个原有场景均干净，四个诊断回调为空，frame / atrous override 为 −1、diagnostics flags 为0。恢复证据为 `restoration-programs.json`、`restoration-state.json`。
- 容量审查保留四组完整序列及失败原始数据，用于定位首次分歧；没有删除既有证据。详见 `post-experiment-storage.json`。

下一步的必要前置是定位相同代码冷启动的首次分歧，取得可信的最终图 A/A；然后在同方向下处理仍被错误接受的光片膨胀轮廓，并重新验证遮挡和返回光照。局部覆盖候选已经可复现，但固定斑块与完整能量误差的修复仍未完成。

后续同日精度实验曾完成 A/A 前置并重新测量候选，见 [同代码重复性与复测](ScreenProbeCaptureRepeatability.md)。卡片候选在该实验配置下仍使 edge 均值图 MSE 增加 1.599%，未合入。2026-11-17 按用户要求已撤销生产精度改动；这些实验结果不代表撤销后当前源码已通过 A/A。本文上面的早期未合格数据作为历史证据保留。
