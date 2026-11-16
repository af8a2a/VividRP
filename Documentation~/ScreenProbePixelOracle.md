# Cornell 最终像素的独立能量参考

2026-11-16，Unity7000.0.0a16 / D3D12。四分层插值候选在抽样天花板像素上的时间方差下降33.05%，但**固定空间误差门限未通过，默认不启用**。back区域红通道的时间均值图MSE增加1.158%，超过预设1%；重复捕获重现。平均能量稳定不足以证明每个位置更准确。

证据：`E:/VividRP_GI_Evidence/ScreenProbePixelEnergyReference-20261116-134500`。主捕获为`oracle-capture-v2`，独立参考为`reference-16k`，报告为`pixel-reference-analysis-v2.json`。`oracle-capture`是同序列重复，另存`pixel-reference-analysis-repeat.json`；不是另一独立序列。没有修改Runtime、生产shader、场景、Profile或同步资产。

## 实际像素与模型

`BuildScreenProbePixelOracleCapture.py`沿用[原生候选历史闭环](ScreenProbeNativeCandidateHistory.md)：候选实际拥有原生像素历史，控制持有独立历史，共享真实探针输入。每批132对全图Resolve、3300对时域/颜色历史/moments/几何/final核验逐位一致。保持16px网格、每探针8条射线，FP32 anchor512、pixel32、四轮À-trous，预热1024帧、测量128帧，冻结8阶/64样本/32批Radiosity。

在原四个ROI内以4px步长抽样2336个最终像素，均不在探针中心。owned小kernel只读取真实深度和解码法线，不执行SDF追踪，也不复制一套追踪资源绑定。另从当前`ReadDiffuseSurface`导出八件几何的实际线性反射率、发光、Cull Back语义和变换，禁止无效材质或非黑环境。原生追踪shader保持不变。

接收点对应曾有两次开发失败，均未删改验收结果：

- 早期私有追踪重放缺少`_SdfDiffusePreviousProbes`绑定，屏幕命中A/A也不一致；`pilot-v2`与四份撤回工具源码保存在`failed-prototype-source`。这批不作为参考。
- 初始理想相机射线交点与GPU重建位置的最大分量差为23.951微米，未通过20微米门限。擦边入射把约6微米的法向误差放大为沿射线误差。后续改用**实际GPU位置的切向坐标**，沿匹配的精确表面法线投影到边界，避免在墙体内部启动参考路径。此处改变了接收点对应定义，并未放宽移动上限；原失败保留在报告说明中。

实际最大法向校正6.103微米，法线最小cos为0.999999088；投影点与理想像素中心交点的距离最多占半像素足迹的1.078%。还要求属于同一最近表面、全部128帧接收几何逐位不变。参考首顶点使用实际解码的像素法线，后续使用精确几何法线，不加入4cm SDF bias。

`CornellPixelOracle.py`复用[独立Lambert模型](CornellLambertReference.md)，每像素16384条路径、9次漫反射，三CPU worker。共38,273,024条路径，耗时188.70秒。每个散射顶点直接采样顶部面积光，后续按余弦半球追踪，背面吸收但保留不透明遮挡。没有SDF、卡片、SH、曝光或能量拟合，也没有增加生产GPU射线数。

这是常量材质Lambert `E/π`参考，不能当作OpenPBR或Unreal完全等价输出。模型9次反射比8阶卡片加Gather查询多一个反射阶；第9阶占各ROI平均Y的0.0071%～0.0181%，单独报告，不用它拟合引擎。

## 最终像素结果

以`RGB×coverage`计算，先对128帧求每个像素时间均值，再与各自物理位置的参考比较。下表不是整幅图统计，floor/back/edge也不是纯单一平面；报告保存物体构成。

| 抽样ROI / 点数 | 时间方差下降 | 原路径 / 候选平均Y误差 | Y均值图MSE变化 |
| --- | ---: | ---: | ---: |
| ceiling / 288 | 33.05% | −0.5986% / −0.5910% | −1.8057% |
| floor / 384 | 19.05% | +3.1040% / +3.1120% | +0.7524% |
| back / 384 | 16.86% | −1.3699% / −1.3431% | +0.8640% |
| edge / 1280 | 1.70% | +0.4388% / +0.5510% | −0.7754% |

平均误差相对于精确几何Lambert模型，包含SDF、Surface Cache与重建的共同近似；不能把全部误差归因于插值。各RGB/Y通道新增绝对均值误差的保守界最大0.1151%，通过0.2%门限。但back红通道MSE增加1.1584%，加三倍参考标准误后为1.2996%；重复捕获结论一致，故1%空间误差门限失败。

参考每点保存RGB均值、均值协方差、标准误及各阶均值。亮度标准误保留通道相关性，用`LᵀCovL`传播。两条路径MSE差写为`mean[(candidate−baseline)×(candidate+baseline−2×reference)]`，参考噪声的共同二次项相消；另报告这项差异的参考采样标准误。原MSE含参考自身噪声，也保存扣除该期望项的值。这些不确定度仅描述独立参考采样，不把128个相关渲染帧假设成IID。

天花板蓝通道等三倍标准误上界也尚不足以证明通过1%门限。增加参考样本可缩小这类不确定度，无法直接消除已重现的back红通道回退。下一步先分解纯表面与几何/光照边界上的局部误差，检查插值覆盖归一化及后续时域裁剪是否产生偏移，再验证新候选；不按本次ROI拟合增益或放宽门限。移动中持续启用的质量和可归因原生GPU成本仍独立待验收。

## 复现与恢复

```powershell
python -B Tools~/Diagnostics/BuildScreenProbePixelOracleCapture.py NEW_CAPTURE --sequence 1733
# 固定源码快照后，使用unity-cli执行NEW_CAPTURE/capture-script.cs；核验source-verification.json。
python -B Tools~/Diagnostics/CornellPixelOracle.py NEW_CAPTURE NEW_REFERENCE --samples 16384 --bounces 9 --workers 3
python -B Tools~/Diagnostics/AnalyzeCornellPixelOracle.py NEW_CAPTURE NEW_REFERENCE --output NEW_ANALYSIS.json
python -B -m unittest discover -s Tools~/Diagnostics -p 'test_*.py'
```

接收点、夹具和脚本hash均固定。跨捕获复用参考必须满足夹具、像素列表、投影点、解码法线和资格记录完全一致；不以同名场景作为充分条件。两批源快照的2784份文件、三个输入及实际脚本核验均未变。参考计算前后也核验输入与积分器hash。

162项Python检查通过，包含解析面积光、反射线性、RGB协方差、表面匹配拒绝和配对MSE不确定度检查。实际DX12几何kernel及原生候选闭环编译执行；没有在活动Editor中运行Unity Test Framework。本轮无新PIX捕帧或重启。

实验后运行1024个原始路径正常帧，并核验原时域/À-trous DXIL、persistent Hybrid trace、16/8、anchor512、pixel32、bias.04、源8阶/32批。回调为空、override=-1、flags0，两场景干净，播放暂停、Frame Debugger关闭、PIX idle。重复捕获8633份完整读回无损归档，复核ZIP中每个文件的SHA后才删除外部原始副本，净释放152,704,048B（约145.63MiB）。主批保留全量，报告、聚合统计、失败记录、源码、协议与独立参考均保留。完整重跑首批分析前需解压回原路径、复核SHA并移开`raw-retention.json`；主批可直接重跑。
