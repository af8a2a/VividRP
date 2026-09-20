# SMRT 成本诊断

## 使用

Unity 菜单：**Tools → VividRP → Diagnostics → Capture SMRT Cost**。

也可调用现有诊断 API（`vivid_diagnostics` CLI 命令的 `request` 参数接受相同 JSON）：

```csharp
VividDiagnostics.Execute("{\"action\":\"smrt-cost\",\"warmupFrames\":64,\"repaintViews\":true}");
```

返回 `jobId` / `outputDirectory`；用 `status` 查看结果，或带该 `jobId` 调用 `cancel`。与 profile/capture 任务互斥。`smrt-cost` 在预热后只采一帧，`frames` 不表示重复采样数。需要正在渲染且 VSM Active 的目标相机；默认主相机，可用 `cameras` 的 id 指定 `cameraId`。未使用 VSM 的相机会超时，工具不会修改画质配置。

输出 `smrt-cost.json`、请求、前/后与采样帧状态快照。统计页内复用、footprint、DDA、双池首层/隐藏层读取、过渡与回退；每项有 total、每接收像素均值、p50/p95/p99/max、非零像素数和 activeP95。普通百分位包含零工作接收像素，排除天空；activeP95 再排除零工作像素，避免低频回退被 p95=0 隐藏。

## 范围与成本

- 按当前生产模式（固定预算或自适应退出）统计实际 ray 数；`_VSMHistoryParameters.y` 仅在诊断重放中选择策略，生产使用独立 kernel。
- 在原始 `ResolveTrace` 之后、降噪之前重放同一 receiver 函数。复用原相机全部参数、蓝噪声、页表、投影和完整 16 层双池；不改射线数、步数、质量、历史或页请求。
- 重放结果转换到当前原始阴影目标的 **R16_SFloat** 精度，与同帧原始输出比较。输出精确差异计数和超过 1e-6 / 非有限值计数；后者非零时任务标记失败。
- 32 个计数器按像素写入结构化缓冲，无全屏全局原子热点。buffer 为 `width * height * 128` 字节，1920×1080 约 **253.125 MiB**；只在显式采样时创建，读回完成后释放。不会逐帧常驻。
- 新入口 `VSMReceiverCost` 定义 `VIVID_VSM_SMRT_COST`；普通入口没有计数器存储、读回或额外 dispatch。无 Editor 订阅者时，捕获钩子不解析纹理或分配资源；Player 不编译钩子。
- 计数是源码级工作量，**不是内部函数 GPU 毫秒、带宽或缓存 miss**。front/hidden loads 计两池各自的标量 Load 调用，含 PCF 回退。footprintPageChecks 是执行到的检查数量，失败可提前结束；ddaPageResolves 包含每段的首次寻址，不能全当作跨页。
- `VSM.SMRTCostReplay` 标记只包含重放 dispatch。诊断会扰动缓存并产生大缓冲写回；应单独采集正常 `ResolveTrace`、`FilterHorizontal`、`FilterTemporalVertical`/`FilterVertical` 时间，不把诊断帧的 Resolve 父标记当作生产性能。
- 多项计数互相包含（例如 rays/segments/cells、projection/transition）；不可相加当作总工作或按读取次数比例分摊 GPU 时间。

计数 ABI：`SMRTCostCapture.Names` 与 `VSM_COST_ADD` 索引一一对应；counter 0 是非天空接收像素标志，30/31 为输出差异。CPU 归约用 64 位总量，百分位采用 nearest rank。修改 raw shadow 输出格式时必须同步重放比较的量化规则。

[首次实测与优化判断](../Temp~/VSM/Roadmap~/Experiments/SMRTCost_20260917/README.md)。

接收点/投影准备复用不会减少这些 ray、DDA 或深度读取计数；准备计算的收益由普通 kernel 配对计时测量，见[复用报告](../Temp~/VSM/Roadmap~/Experiments/SMRTReceiverPreparation_20260918/README.md)。
