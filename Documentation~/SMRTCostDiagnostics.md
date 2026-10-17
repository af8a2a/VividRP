# SMRT 成本诊断 (`smrt-cost`)

返回索引：[Documentation~ 索引](README.md) · 相关文档：[SMRT 自适应射线退出](SMRTAdaptiveRays.md) | [SMRT 接收点准备复用](SMRTReceiverPreparation.md) | [VSM 代码布局](VirtualShadowMapCodeLayout.md)

## 调用方式

### 1. Unity Editor 菜单
选择 **Tools → VividRP → Diagnostics → Capture SMRT Cost**。

### 2. C# / CLI 诊断接口
通过 `VividDiagnostics.Execute`（或 `vivid_diagnostics` CLI 命令的 `request` 参数）提交 JSON 请求：

```csharp
VividDiagnostics.Execute("{\"action\":\"smrt-cost\",\"warmupFrames\":64,\"repaintViews\":true}");
```

- **任务管理**：返回 `jobId` 与 `outputDirectory`；可通过 `status` 查询进度或携带 `jobId` 调用 `cancel`（与 profile/capture 任务互斥）。
- **采样规则**：在 `warmupFrames` 预热后**仅采集一帧**（`frames` 参数不代表重复采样帧数）。
- **相机要求**：目标相机必须正在渲染且处于 VSM Active 状态（默认使用主相机，可通过 `cameraId` 指定）；未启用 VSM 的相机会超时退出，工具不会自动篡改画质配置。

---

## 输出格式与统计口径 (`schemaVersion: 3`)

采集完成后在输出目录生成 `smrt-cost.json` 以及请求参数、采样前后与当帧状态快照：

| 统计维度 | 说明 |
| --- | --- |
| **核心计数项** | UE 固定步点（`traceSamples`）、逐点粗层寻址（`samplePageProbes`）、最终深度 Slice 0 读取（`finalDepthLoads`，含 PCF 回退）、层间过渡与回退（旧 DDA / 隐藏层计数槽位保留，但在当前生产路径中恒为 `0`） |
| **聚合指标** | 每项指标输出：`total`、每接收像素均值、`p50` / `p95` / `p99` / `max`、非零像素数以及 `activeP95` |
| **百分位口径** | 普通百分位（`p50`/`p95`/`p99`）包含零工作量的非天空接收像素（排除天空）；**`activeP95`** 进一步排除零工作量像素，避免低频回退被 `p95 = 0` 掩盖 |
| **计数器 ABI** | `SMRTCostCapture.Names` 与 `VSM_COST_ADD` 索引一一对应：Counter `0` 为非天空接收像素标志，Counter `30` / `31` 为重放输出差异计数；CPU 端使用 64 位整数归约总量，百分位采用 Nearest Rank |

---

## 重放机制与性能测量边界

- **同帧原位重放**：在原始 `ResolveTrace` 之后、降噪之前，使用新增的 `VSMReceiverCost`（定义 `VIVID_VSM_SMRT_COST`）重放同一个 Receiver 函数，完全复用当帧相机的蓝噪声、页表、投影与统一物理深度池（Slice 0），不改变射线数、步数、历史或缺页请求。
- **精度一致性自检**：重放结果会量化到原始阴影目标的 **`R16_SFloat`** 精度并与同帧生产输出逐像素比对，统计精确差异数以及超出 `1e-6` / 非有限值的像素数（后者非零时任务直接标记为失败）。
- **按需显存分配**：32 个计数器按像素写入无全局原子竞争的结构化缓冲区（大小 `width × height × 128` 字节，1920×1080 下约 **253.125 MiB**），仅在显式采样帧创建并在读回后立即释放；正常生产 Kernel 不包含任何计数器存储、读回或额外 Dispatch。

> [!IMPORTANT]
> **如何正确解读诊断数据**：
> 1. **源码级工作量 ≠ GPU 耗时**：计数器反映的是着色器源码级事件次数（例如 `traceSamples` 为实际步点数、每条射线最多 $N+1$；`samplePageProbes` 含初始选层与步点回退寻址，不等于深度纹理读取次数），**不能**直接等同于 GPU 毫秒数、显存带宽或缓存未命中率。
> 2. **计数存在包含关系**：多项计数互相包含（如 `rays` / `traceCalls` / `traceSamples`、`projection` / `transition`），切勿直接相加当作总工作量。
> 3. **计时必须与诊断帧分离**：`VSM.SMRTCostReplay` 重放会扰动 GPU 缓存并产生 253 MiB 缓冲写回；评估 GPU 耗时时，必须单独采集未开启诊断的正常 `ResolveTrace`、`FilterHorizontal`、`FilterTemporalVertical` / `FilterVertical` 时间。
