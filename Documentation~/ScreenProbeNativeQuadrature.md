# 四分层 Resolve 的原生整链检查与 PIX 常量校验

2026-11-16，Unity7000.0.0a16 / D3D12 / RTX5070Ti。原生整链试验未通过同配置重复性门槛，**四分层候选不进入默认行为**。此前共享输入、独立像素历史中的降噪结果仍限于[该重放实验](ScreenProbeQuadratureHistory.md)，不能当作原生整链收益。GPU成本也仍未取得合格归因。

后续已新增[候选实际写入原生历史的同帧闭环](ScreenProbeNativeCandidateHistory.md)：两组静态及固定轨迹通过对应门槛，天花板时间方差下降35.61%/37.77%。该新协议保留本文跨运行控制失败，默认仍未启用，逐像素参考与GPU成本尚待验证。

证据根目录：`E:/VividRP_GI_Evidence/ScreenProbeNativeQuadrature-20261116-123000`。本轮仅修改诊断工具和文档；没有修改Runtime、生产shader、场景或Profile。

## 原生路径与事前门槛

Cornell纯自发光场景、原机位、1920×1080，16px / 8rays，正常7平面SH、FP32 anchor512、pixel32、原4轮À-trous。固定Radiosity8阶/64样本/32批，冻结source SHA256仍为`40252e7ae82c1da6ae025dbf012a3ce232aa54ea10bfbf1ca9a7998df93270a8`。冻结后只失效Gather历史元数据，采样序列1733，预热1024帧、测量128帧。

`BuildScreenProbeNativeQuadrature.py`运行四组：persistent原Gather、内存Gather关闭候选两次、相同内存Gather开启候选。候选直接进入原生时域与小波链，不使用两条私有像素历史来替代本次整链验收。原生输出保持正coverage；仅在首末测量帧向owned纹理追加负alpha资格见证，随后恢复绑定。见证RGB与abs(alpha)逐值等于原生输出，证实候选分支确实执行，没有将负coverage写入原生历史。

各组输入深度、GBuffer、pixel geometry、VP与inverseVP逐位一致。probe normal/validity必须逐位相同；已知世界坐标浮点变化事前限定为2微米，实际最大0.7153微米，原Gather与首个内存control之间为0。该位置容差不允许修改法线或有效性，也不是放宽颜色质量门槛。

事前规定control各ROI的Y及每个RGB均值差绝对值≤0.1%、时间方差变化绝对值≤2%；必须先通过control，才能评价候选的均值≤1%、天花板方差至少下降5%、其他ROI方差最多增加2%。这些门槛没有在结果失败后调整。

## 重复性结果

以下为最终输出线性`RGB×coverage`的128帧统计，尚未乘接收材质反射率。方差栏正数表示下降，负数表示增加。

| ROI | 原Gather→内存control：Y均值 / 方差下降 | 同内存control A→B：Y均值 / 方差下降 |
| --- | ---: | ---: |
| 天花板 | −0.0807% / +13.43% | −0.1175% / −13.66% |
| 地面 | −0.0375% / +18.02% | +0.7128% / +44.95% |
| 背墙 | +0.2560% / +28.88% | +0.0901% / +18.09% |
| 混合边缘 | −0.1052% / −2.21% | +0.0956% / −4.00% |

两项control均失败。候选相对control A的天花板方差下降29.46%，但背墙方差增加55.88%；这些数值只作描述，不归因为候选改善或回退，`qualityAttributionQualified=false`、`nativePilotPassed=false`、`productionQualified=false`。

`native-analysis-stages.json`进一步对同一固定probe接收中心分别比较raw、anchor、空间重建、Resolve、像素时域和最终点采样。所有测量帧中raw和后续阶段已经不同；例如天花板raw消耗Y的两次运行差RMS为0.38644，anchor为0.01146。单独列出实际最终pixel ROI统计，不将不同支持域的量相减解释为噪声贡献。

冷epoch0的四条probe区域内，statistics、geometry、raw、anchor均逐位相同。**本轮没有保留epoch1～1023的连续阶段数据**；因此1024只是首个保留的不同测量帧，不能称为首次因果分歧。结果说明原生整链的差异早已存在于测量窗口的追踪/历史输入，不能单凭最终图像把它归咎于插值。没有证明GPU race、驱动故障或某个浮点指令是根因。下一步应围绕冷启动后首次分歧定位原生追踪与引导/历史，而不是继续据不匹配输入调四分层系数。

## 真实 PIX dispatch 的常量

复用[上一轮](ScreenProbeQuadratureRecovery.md)已保留的canonical `pix-pair-v2/capture-1.wpix`，SHA256为`702db6c7c649b36aa3060b6ccc825c55ffeeeb5358d61ce8d8c433b14b2aaf57`。本轮没有重新捕获或重启Editor。官方`pixtool open-capture ... export-to-cpp ...`导出成功；未运行或构建导出的C++程序。

`ExtractPixQuadratureConstants.py`按生成的reader真实调用顺序定位压缩块，消费长度精确覆盖218,700,060B的`resources.bin`；按command-list1378的提交顺序重建resource359的修改3页面，确认后续修改不覆盖目标页。三个b0窗口分别位于2,242,816、2,244,096、2,245,376，每份提取1280B。

实际PSO1396中的shader解压后244,848B，SHA256为`63f409e3e9c68cf5194ddbf9ed108a7e0293fc7292de0bfe9234c28ad3e8a722`，精确等于同一内存源码经Unity DX12编译所得字节。实际DXIL入口是`ScreenProbeIntegrateCS`，8×8×1。由于DXIL剥离了类型名称，字段布局取自哈希匹配的Unity `VariantCompileInfo.ConstantBuffers`，`$Globals`大小896B；没有把1280B窗口或SDK报告的剩余资源长度当作cbuffer大小。

| 字段 | 偏移 | 原生 / 私有基线 / 四分层 |
| --- | ---: | --- |
| screenSize | 624 | 均为1920×1080及相同倒数 |
| nearOcclusion | 768 | 均为(.35,0,0,0)，方向近场AO关闭 |
| sampleFrame | 792 | 均为10964 |
| diagnostics | 856 | 32 / 32 / 67108896 |
| integrationJitter | 876 | 均为1 |
| preparedAnchor | 880 | 均为1 |

所有已声明字段中，原生与私有基线逐字相同，四分层仅diagnostics的67108864启用位不同。未声明padding不参与比较。`pix-constants-v3/extraction.json`的`constantsQualified=true`排除了这份捕帧中错采样帧、错分辨率、未启用候选或错误PSO的解释；**不解除上一轮GPU时间归因失败，也不能推广到未捕获的帧**。

## 验证与保留

150项Python诊断检查通过，新增检查覆盖事件reader调用顺序、递归拒绝、已声明字段/未使用padding、错误帧号/flag/分辨率拒绝、原生正coverage、owned见证恢复及control失败阻断归因。四组实际C#脚本与内存Gather的6个DX12 kernel编译执行完成，各组捕获期间2827份源码和实际脚本指纹不变。未在活动Editor中运行Unity Test Framework。

结束后运行1024个正常帧，核对原Hybrid persistent trace、原时域/小波DXIL哈希、16/8、anchor512、四轮、bias.04、8阶/32批；所有诊断回调为空，override=-1，flags0，两个场景干净，Editor播放暂停，Frame Debugger关闭，PIX idle。没有临时链接或新Unity资产。

完整control A/B保留供继续定位；原Gather和失败候选按审计仅保留首末帧、每16帧、报告、源码/hash及全部聚合量。这两组归档后不能重新执行完整原始序列分析。核验提取结果后删除可重导出的`pix-cpp/resources.bin`，保留canonical捕帧、全部C++源、所需XPRESS块、shader和常量窗口。精确释放字节及最终容量见根目录`*-retention.json`和`post-experiment-storage.json`。
