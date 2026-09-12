# 动态质量解析方法

`python analyze-dynamic.py <Temp~/vsm-baseline/dynamic/yyyy...> --output <report.json>`

`--self-test` 当前通过18项聚焦检查：亮/暗跳变的2帧延迟、完全不响应、尾部截断、无动态事件、换表面、bias不一致、抽样支持域、正负误差恒等式、邻域边界、frame连续性、phase/模式配对、静态方案。

解析器读取每帧 float4 reference / signal / world，不翻转网格原始行序。元数据与二进制尺寸必须一致。验证frame连续、phase=frame&255且从0开始，以及同一case场景/维度/页数/参考射线数稳定。独立核对camera slide/turn、sun rotate、thin/receiver平移的step驱动方案；mode间比较同step/phase的矩阵、相机/光源/fixture位置、质量和SMRT路径设置。fixed8的SMRT.x=8是唯一预期ray预算差异。可选fixturePhase和fixtureGeometryHash逐帧比较；没有变形记录时明确写出几何不可核验。fixtureGeometryHash为整数精确比较，不先转float。

floorY只从首帧选一次并用于所有case：normalY>.9、|Y|<.5候选按2cm分箱，取最大峰内中位高度；也可显式`--floor-y`。首次2帧静态试跑给出Y=-0.02227427，每帧10650个floor样点。最终floor ROI为valid且normalY>.9且|Y-floorY|<.15，可用`--floor-tolerance`改变。该ROI限定接收者几何；强制opaque的alpha投影者仍是参考近似的限制。

输出两种法线bias(0.001/0.01m)各自MAE、平均正误差max(output-reference,0)与平均负误差max(reference-output,0)，以及bias差>0.05的占比和bias不敏感子集。正/负均除以整个选定样本数，MAE等于其和。全valid ROI的指标标记为diagnostic_only，不能当作所有材质的真值误差。256条固定Hammersley积分是有限参考估计，不叫精确真值。

动态首检使用相邻帧参考变化>=0.5、两帧bias不敏感、floor有效且world位移<=2cm的事件。输出首次跨过参考旧值/新值中点、持续2帧，记延迟0…8；参考在此期间必须持续处于新侧，接收者不得离开原world位置2cm。事先已在新侧的输出排除。检测结果分别记录：已检出、完整8帧窗口内未检出、采集尾部截断、参考反转/表面失效。P95/P99仅对已检出事件计算，同时必须带未检出率。没有可评估事件输出insufficient_detectable_events，不输出“0拖影通过”。相机移动没有伪造运动矢量对应；同屏幕像素换表面会剔除，因此相机移动事件不足不能证明稳定。

支持域残留只在上述实际跳变事件处计算：当前参考在保守邻域内全亮(>=.9)或全暗(<=.1)，输出还保留>0.1旧侧误差。生产空间核为±4px，TemporalV邻域又访问相邻横向结果，因此默认保守半径5px。若stride=4，用ceil(5/4)=2的网格邻域；这只是在被采样网格上检查支持包络，不能证明中间未采样像素无阴影，也不能声称精确越界拖尾。输出有实际eligible event-observation分母，零分母标insufficient_support_coverage。残差还可能含几何/积分误差，不全部归因于时域拖影。

阈值均记录在JSON，也可通过CLI改变：`--bias-threshold`、`--jump-threshold`、`--world-tolerance`、`--detect-fraction`、`--sustain-frames`、`--max-delay`、`--settled-threshold`、`--residual-threshold`、`--support-radius-pixels`、`--pair-tolerance`。更改阈值后必须共同重跑所有模式，不能只调有利的一组。

首个2帧static/adaptive试跑仅用于检查格式/ROI：21300个floor观测，bias敏感比例0.0657%，相对0.001m bias参考的MAE=0.01529、正误差0.01068、负误差0.00461；没有动态事件且缺fixed4/fixed8配对，因此明确不是动态质量验收结果。见`dynamic-trial.json`。
## receiver_lift

场景方案核对：fixture x相对第0帧增加1.4*motion，y增加0.6*motion，camera/light保持原姿态。保留原receiver_slide的近平面替换方案。额外顶部ROI使用实际每帧fixturePosition：normalY>.9、abs(worldY-(centerY+.02))<.002、abs(worldX-centerX)<=1.252且abs(worldZ-centerZ)<=1.002，对应无旋转的2.5×0.04×2 cube。该ROI输出逐帧/整体双bias MAE、史龄、bias敏感比例以及|误差|>.25的比例。强误差阈值可用`--strong-error-threshold`调整。

顶部最初可能也落入±.15m的原地板ROI；两个指标是分别诊断，不视为互斥分区。每个屏幕网格点并未建立移动物体的材质点对应，因此顶部结果不输出低拖影结论；史龄小或MAE低都不能替代运动对应验证。当前18项自检另覆盖receiver_lift方案、顶部像素选择/史龄以及3mm高度偏差剔除。

`plot-dynamic.py <dynamic-quality.json> --output-directory <figures>` 导出标准Matplotlib PNG：地板误差/偏置敏感性、参考强变化事件与条件P95/未检出、receiver_lift顶部逐帧曲线。无事件的延迟画空缺，不伪画0帧。
