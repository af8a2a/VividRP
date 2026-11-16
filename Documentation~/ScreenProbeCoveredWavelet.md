# Coverage 与小波重建的同帧诊断

2026-11-16，Cornell Box / Unity7000.0.0a16 / D3D12。本轮确认小波颜色平均域会影响最终空间误差，并建立同帧反事实对照；**两个实验方案都没有通过全部质量门限，均不启用默认**。保持16px网格、每探针8条射线，没有修改Runtime、生产shader、场景或Profile。

证据根目录：`E:/VividRP_GI_Evidence/ScreenProbeCoveredWavelet-20261116-144800`。沿用[独立最终像素参考](ScreenProbePixelOracle.md)的2336个实际位置、解码法线与线性材质；所有旧失败判定保留。

## 定位和两个实验

旧back红通道MSE回退分布在Back与Tall White Block，部分最大误差点的coverage约0.56～0.58。两个端点的时域差异较小，À-trous后差异扩大。但这不是单独的因果证明：跨运行640份raw/geometry/GBuffer/depth检查虽一致，扩大到6421份小波前source记录后有649份hash不同，完整Resolve/历史也有差异。`wavelet-attribution.json`明确保持`preWaveletInputsByteExact=false`，未把它改为宽容比较。

`ScreenProbeCoveredWavelet.py`生成owned内存shader，提供两个实验：

1. `--covered-wavelet`：条件RGB引导、moments、深度/法线/材质判定、coverage置信权重与方差传播保持原实现；颜色分子改为平均`RGB×abs(coverage)`，写回时除以中心coverage，保留中心alpha和65000 HDR上限。它只检查颜色平均域，**尚未统一时域统计的能量域**。
2. 再加`--symmetric-coverage`：把偏向较高coverage邻居的单向置信权重改为`min(Ca,Cb)/max(Ca,Cb,1e-4)`，满足交换中心/邻居不变。这仅使coverage这一项对称，不能称整个双边算子已经守恒；深度梯度、亮度方差、拒绝与逐行归一化仍可能不对称。

每种实验各运行1733/9481两组独立采样序列。原四分层插值候选实际拥有原生历史；控制使用原插值和独立历史。冻结8阶/64样本/32批Radiosity，预热1024帧、测量128帧，FP32 anchor512、pixel32、四轮À-trous。每组132对全图Resolve及3300对原生/重放temporal、history、moments、geometry、final全部逐位一致。

新的协议允许改变小波后冷启动final不同，但仍要求冷启动时域输入、颜色历史、moments与所有几何一致，且候选final始终必须匹配真实原生输出。原协议仍要求四个private程序；新实验要求原四个加两个明确命名的covered wavelet程序。旧四分层数据重跑仍在back红通道报1.158%回退，未弱化原质量门限。

## 同帧相同输入的对照

对称实验另启用`--wavelet-witness`。每帧在相同candidate temporal颜色与moments上，重放原条件RGB小波，记录为`quadrature-reference`。它不更新任何历史，也不参与native A/A计数。原生候选与独立候选仍做完整A/A。

每组256对**全分辨率**输入/描述符hash逐位相同，覆盖128帧的颜色和moments。只有通过此检查，`AnalyzeCornellPixelOracle.py --control-side quadrature-reference`才允许报告两种小波的参考误差变化。新增检查会拒绝任意一帧颜色、moments、尺寸或格式不符，不能只用ROI输入或纹理名称声称同输入。

天花板288点的独立参考由每点16384提高到131072路径，采用不与原参考重叠的新种子，耗时164.61秒。其他2048点保持原数据。`RefineCornellPixelOracle.py`保留父metadata、父reference与新逐点记录hash；分析器逐点检查哪些记录被替换，不重写原参考，也不改材质/位置或拟合增益。仍为Lambert精确几何参考，不证明OpenPBR或Unreal等价。

## 结果与否决

仅改颜色平均域的`covered-a/b`，相对原始完整路径的天花板时间方差下降34.42%/37.21%；但第二序列的抽样天花板新增绝对平均Y误差0.341%，超过原0.2%门限，故未接受。

下面比较对称实验与**同帧、相同四分层时域输入的原小波**，不是与原插值路径比较。数值是128帧时间均值图相对独立参考的MSE变化；不是无限时间固定误差，也不是整幅图统计。

| 抽样ROI | Y均值图MSE变化：1733 / 9481 | 新增绝对平均Y误差：1733 / 9481 |
| --- | ---: | ---: |
| ceiling | −15.06% / −17.25% | +0.0568% / +0.2992% |
| floor | −6.21% / −6.76% | −0.1910% / −0.1397% |
| back | −3.40% / +1.65% | +0.0657% / +0.1665% |
| edge | −1.02% / −0.15% | −0.0728% / +0.0680% |

第二序列天花板新增平均误差0.2992%超过0.2%，back的Y MSE增加1.653%、最大RGB通道增加1.904%，超过1%门限。加上参考不确定度后也不满足接受条件。首组天花板蓝通道的三倍参考标准误上界尚不够紧，不能把首组点估计全部下降当作全面通过。

与原始完整路径比较，对称实验天花板时间方差下降34.46%/37.51%，各ROI逐帧差RMS没有增加；这些收益不能替代能量和空间误差门限。本轮停止这两种候选的推广，没有追加移动或GPU计时以掩盖质量失败。

## 下一步与恢复

下一步优先验证两项可分离的原因：时域moments仍统计条件RGB，是否与最终消费的`E/π`不匹配；双边算子仅逐行归一化，是否在coverage/光照梯度下改变局部DC能量。先做一致能量域的时域统计和有明确归一化约束的重建，再复用本轮同帧输入对照与独立参考。不能宣称本轮已证明这两项是唯一原因，也不按已测ROI调整增益或降低门限。

167项Python检查通过；包含coverage不同但消费能量恒定的独立数值检查、置信权重交换不变、HDR上限、同帧完整输入拒绝与原协议回归。四个实际DX12实验均完成，errors为空。源码在各次测量结束时核验未变；在第一种实验结束后才扩展工具产生第二种实验，旧生成脚本和shader全文保留。没有运行活动Editor的Unity Test Framework。

结束后运行1024个原路径正常帧，原时域/小波DXIL、persistent Hybrid trace、16/8、anchor512、pixel32、四轮、bias.04和源8阶/32批均核验恢复。回调为空、override=-1、flags0，两场景干净、播放暂停、Frame Debugger关闭、PIX idle。本轮未重启Editor、未产生PIX或大幅图片。

`symmetric-a`保留完整读回；另三组无损ZIP归档，逐项解压核验原SHA后清理外部原始副本，净释放469,321,714B（447.58MiB），报告、统计、源码和独立参考均保留。对归档组完整重跑分析前，需解压回原路径、复核SHA并移开`raw-retention.json`。空间复核时磁盘可用107.80GiB；根目录`post-experiment-storage.json`保留本轮实测大小与继承自上轮的总量估计。
