# Sponza：全分辨率历史重投影复验

2026-11-14，Unity 7000.0.0a16 / DX12，原生 Gather，1920×1080、16 px / 8 rays。目的为区分实际历史误用与 CPU 近似诊断的误分类，不修改历史拒绝阈值或默认采样路径。

## 诊断实现

`InspectScreenProbeTemporalHistoryCS` 仅由 Editor 实验显式 dispatch，直接调用生产 `ReadScreenProbeTemporalHistory`。它记录实际四 tap reader 的有效性、footprint、前景权重、物体运动误差、covered lighting、coverage、有效 age，以及原始接收点/法线/深度、实际 previous pixel 和期望深度。

Capture 通过生产 C# `BindReceiver` / `BindProbeFrame` 绑定独立的 compute shader clone，再绑定生产使用的上一帧 VP、jitter、motion、pixel geometry 和 pixel lighting。记录的输入包含 32 px halo；没有从当前 VP 推测 motion，也没有把最近邻测试当作四 tap 的结果。诊断输出为每个采样点六个连续 float4，GraphicsBuffer stride 16；不改变生产 GPU 数据结构或正常 dispatch。

两组分别预热 512 帧，随后沿已记录的 128 帧往返轨迹运动 256 帧；每四帧捕获一次，每个捕获帧在 920×480 区域每四像素检查一个点，共 230×120。每组得到 **1,766,400** 个实际 GPU 样本。2 阶、64 samples / batch、32 batches 的源缓存首尾和两组之间相同。

## 结果

| 指标 | 白噪声 | 分块采样 |
|---|---:|---:|
| CPU replay 与 GPU 有效性判定不一致 | 0 | 0 |
| 读取超出捕获 halo | 0 | 0 |
| 生产 reader 拒绝后仍有 age>1 的样本 | 0 | 0 |
| 有当前 GI 的生产拒绝样本数 | 51,639 | 52,281 |
| 拒绝样本 Temporal−Resolve 最大绝对 Y | 0 | 0 |
| 当前有效像素平均 age | 25.750 | 25.613 |
| object-motion error P99，像素 | 0.004467 | 0.004469 |

CPU 使用记录的精确 GPU 接收点、法线、previous pixel、容差，以及原始前帧 geometry/lighting 独立执行四 tap 计算。footprint 最大差约 8.76e−8、foreground 约 5.97e−8、coverage 小于 2.71e−7、age 小于 9.29e−6。covered RGB 的最大绝对差约 0.001053，P99 约 0.000201，为本批 HDR 输入上的 float32 运算差异；没有将其称为逐位一致。

原来的“新露出诊断带”只看最近邻、采用 0.075 / 0.10 m 阈值；生产路径是四 tap、距离相关容差，并会拒绝 footprint<0.35 或 foreground≥0.25 的组合。因此此前诊断带里约 1.4% 的 age>1，**不能直接作为生产路径接受了已拒绝历史的证据**。本批未发现这类违规继承；它也不证明探针历史、所有几何遮挡或动态物体完全正确。

像素历史在移动中平均仍约 25.6 帧，物体运动残差很小，当前证据不支持为了降噪而放宽这一层拒绝。后续应分离 scalar anchor 的几何重投影、方向场和最终重建。

## 控制边界与复现

与上一批 Sponza 捕获比较时，相机相同，但重新加载场景后的源缓存 hash 不同。跨批 GI 输出对比已判为不合格控制，不用它证明诊断 dispatch 的输出等价或画质变化。本轮 reader 复验只使用本批相同冻结源的两组，以及各自的实际输入，不跨批比较绝对照明能量。

```text
E:/VividRP_GI_Evidence/SponzaReprojection-20261113-173802-195/capture/capture.json
E:/VividRP_GI_Evidence/SponzaReprojection-20261113-173802-195/history-replay.json
```

将 `SponzaBlockedSamplingCapture.cs` 的 experiment 设为 `sponza-reprojection`，通过 Unity CLI 执行；随后运行：

```powershell
python -B Tools~/Diagnostics/ScreenProbeHistoryReplay.py <capture 返回的目录>
```

实际 D3D12 dispatch、回读及 CPU 重放成功，错误清单为空；未运行 Unity Test Framework。未测量 GPU 耗时。本次加入诊断入口，生产拒绝规则、正常内存预算与默认采样不变。
