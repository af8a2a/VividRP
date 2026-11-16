# 四分层候选写入原生像素历史的同帧验证

2026-11-16，Unity7000.0.0a16 / D3D12 / RTX5070Ti。候选实际进入原生时域和四轮À-trous后，两组独立采样序列的天花板最终时间方差下降**35.61% / 37.77%**，各ROI平均能量门槛通过。**默认仍未启用候选**：本轮没有补齐逐像素高样本能量参考和可归因GPU成本，也没有证明移动中持续启用的降噪质量。

证据根目录：`E:/VividRP_GI_Evidence/ScreenProbeNativeEpochOnset-20261116-131500`。本轮没有修改Runtime、生产shader、Scene、Profile或同步资产。上一轮[跨运行控制失败](ScreenProbeNativeQuadrature.md)保持原判定；本轮采用新的同帧原生候选协议，没有调整原门槛或宣称修复追踪重复性。

## 冷启动短序列

原生shader、FP32 anchor512、16px / 8rays，两组各64帧。沿用有界首个分歧捕获，源完成8阶/64样本/32批并稳定八帧后，使Gather历史失效。两组各准备264个源帧，没有其他相机。

320对当前geometry/raw/composite/directional temporal/filtered功能hash完全相同，378条previous→前帧输出内容链接一致，CPU所选状态与历史owner检查通过，未产生首个分歧包。初帧无效previous directional存储仍有差异，未作为有效历史故障解释。记录在`native512-two-analysis.json`。

这不能推翻长窗口控制失败：观测仅覆盖64帧，读回、depth copy和启用的GPU Profiler也改变工作负载。`BuildScreenProbeDirectionalBindingExperiment.py`新增`--anchor-age 128|512`、`--arms 2|8`，原128/8组默认保持原协议。

## 为什么能够共享探针输入

对当前代码的数据流检查确认，像素重建的颜色不反馈到本次固定源实验的探针运输：

- `SdfScreenProbeImportanceSampling.compute`从directional filtered history生成引导，Surface Cache Radiosity也不读取最终像素GI颜色。
- `DeferredLit.compute`的屏幕源写入`bounceSource`；`VividDeferredLighting.hlsl`中OpenPBR的该量来自直接漫反射与自发光，其他Slab路径去掉indirect diffuse后导出。完整SceneColor另行合成。
- `ScreenProbePrepareAnchorCS`使用previous pixel geometry验证可见性；像素时域输出的该几何只由深度、接收法线与接收器有效性构造，与光照颜色无关。

因此，新协议让候选实际写入原生历史和最终输出，并在同一帧、同一套真实探针输入上保留独立的原插值控制历史。此数据流检查不解释此前的浮点/调度观测，也不把共享探针对照泛化为所有运输算法的独立物理真值。

## 原生候选闭环

`BuildScreenProbeNativeQuadratureHistory.py`复用既有私有历史工具，反转哪条路径实际拥有原生输出。原生Gather使用正coverage候选，正常7平面SH；控制从同一份SH与几何执行flag关闭的Resolve，独立持有颜色、moments、geometry ping-pong。另有独立候选重放，必须逐位等于真实原生结果。

每组保持原Cornell机位、1920×1080、16/8、FP32 anchor512、pixel32、四轮À-trous，冻结Radiosity source SHA256仍为`40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。预热1024帧、测量128帧，静态序列1733/9481，移动为原128帧正弦轨迹。

只向owned纹理输出负alpha资格见证，归一化后与正coverage原生Resolve做全图A/A。静态每组132对Resolve、3300对原生/独立候选的temporal、color history、moments、geometry及final输出全部逐位一致；移动组为132与1320对。最终ROI还必须等于真正原生最终输出，包含后置工作。旧协议仍要求原生等于baseline；新schema明确要求原生等于candidate，不能通过修改side标签规避匹配。

首组第一次请求因启动前错误取消暂停，被`Paused PlayMode required`守卫拦截，未执行GPU实验；保留`native-candidate-a-cli.initial-guard.json`。恢复所需暂停状态后执行原脚本成功。实际捕获均complete、errors为空，2788份源码/输入与实际脚本核验未变。

## 最终像素结果

下表为材质反射率相乘前的线性`RGB×coverage`。每个候选都与同帧独立控制比较；两列使用不同的完整采样序列，不用跨次raw追踪差异归因质量。

| ROI | 时间方差下降：1733 / 9481 | Y均值变化：1733 / 9481 |
| --- | ---: | ---: |
| 天花板 | 35.61% / 37.77% | +0.0059% / −0.0720% |
| floor | 18.66% / 0.96% | +0.0230% / +0.0632% |
| back | 17.77% / 24.91% | +0.0305% / +0.0387% |
| 混合边缘 | 2.09% / 1.84% | +0.1115% / +0.0642% |

各ROI、每个RGB通道的最大绝对均值变化为0.11405%，小于1%门槛；各ROI时间方差均未增加，逐帧差RMS均未增加。天花板逐帧差RMS下降41.03% / 40.53%，相对时间RMS由0.10319%→0.08279%、0.10534%→0.08315%。这些是128帧窗口的条件统计，不证明长期低频误差、Surface Cache误差消失或达到Unreal最终质量。天花板整个测量窗口都实际启用候选，首组pixel-frame启用率100%。

固定轨迹包含73帧、3894个明确新露出像素样本，两路径旧age比例、历史接受率均为0；temporal−自身当前Resolve的P99/max均为0。Final−控制当前Resolve的P99/max为2.18159/5.23414，两路径相同；该量包含空间滤波，不能当作鬼影或物理误差。候选只在首个测量帧启用，后续保守回退；因此`movingDenoisingQualityMeasured=false`保持真实状态。

## 工具、恢复和保留

155项Python检查通过，并用完整旧`static-a-v2`重新核验3300对原生baseline历史A/A，确认新schema未破坏原验收。三个新捕获的内存Gather六个DX12 kernel、时域、wavelet及覆盖率归一化kernel均实际编译执行；未在活动Editor中运行Unity Test Framework。

结束后运行1025个正常帧；原Hybrid persistent shader、原时域/小波DXIL哈希、16/8、FP32 anchor512、pixel32、四轮、bias.04、源8阶/32批核验通过。回调为空、override=-1、flags0、两场景干净，播放暂停、Frame Debugger关闭、PIX idle。无新Unity资产、include别名或临时链接。本轮没有新PIX捕帧或Editor重启。

首组静态完整读回保留。第二组与移动组全部frame binaries分别压缩到`frame-readbacks.zip`，逐个从ZIP解压并复核原SHA后删除外部原始副本，**净释放327,809,404 B（约312.62 MiB）**；不是丢弃原始证据。`raw-retention.json`保留每个条目的原路径、大小、SHA及archive SHA。直接重跑完整分析前，须恢复到原case路径、逐项核验，再移开该marker；当前这两组不能直接按未归档目录读取。报告、源/协议、hash及聚合量保持可用。

下一步以实际像素位置建立高样本参考，并用真实消费候选输出的原生GPU事件测量成本。原跨运行控制失败仍须作为观测限制保留，不能以本轮同帧收益宣称其已修复。
