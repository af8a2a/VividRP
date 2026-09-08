# SMRT 接入 BND 1SPPTemporal（2026-09-08）

已复用项目现有三张纹理：ScramblingTile1SPP、RankingTile1SPP、SobolOwenScrambled256。没有新增噪声资源。

- 光盘相位使用维度 0–1，接收覆盖相位使用维度 2–3；采样索引使用相机 frameIndex，经现有函数保留低 8 位，周期为 256 帧。
- 相位在光线循环前计算。保留 4–8 条光线的等面积径向分层、黄金角间隔，以及接收覆盖的 Hammersley 分布。
- 主 Resolve、Debug Pass 登记 BlueNoise 的 RenderGraph 读取依赖；主路径和独立画质诊断均显式绑定 ComputeShader 资源。
- DDA 求交、接收面修正、光线长度、页请求支持范围、层级回退及 TSR 未修改。

## 验证

| 检查 | 结果 |
|---|---|
| DXC cs_6_2，主着色器及 GPU 检查入口 | 38 / 38，源码哈希稳定 |
| Roslyn Runtime / Editor / Tests | 3 / 3，源码及 Bee 响应文件稳定 |
| Direct3D12 独立 GPU 诊断 | 74,940 次断言通过；没有调用 NUnit / Unity Test Framework |
| BND 序列 | 三个像素位置，完整 256 帧、四个维度；4–8 条光线覆盖范围及周期通过 |
| 显式纹理绑定 | 覆盖故意设入的错误纹理后，与实际 BND 资源采样一致 |
| SMRT 回归 | 薄遮挡、有效空页、脏页、缺页回退、零角度 PCF、接触硬化、倾斜接收面通过 |
| 绑定分配 | 暖机 100 次后调用 10,000 次，当前线程 0 B |
| Unity Profiler | 暖机后的 11 帧，遍历 1,540 个线程帧数据视图；209 个 BlueNoise / CSM 范围中 GC.Alloc 样本为 0 |
| Unity 实际渲染 | 主 ComputeShader 消息为 0，BND 三张纹理存在；重绘后的日志未出现绑定或着色器错误 |

Profiler 采集期间的 Editor 和其他线程仍有分配；上述结果只确认本次相关渲染范围。Profiler enabled / profileEditor 已恢复为 false。

场景保持原有设置：2048、SMRT 开启、4 rays × 8 steps、光源角直径 7.1°、有效最大光线长度 10、TSR history 16。没有临时场景对象或遗留的临时 Editor 诊断入口。

## 验证边界与后续

这是 BND 相位复用，不等同于专门的时空蓝噪声 STBN。GPU 断言确认采样和求交正确性，并不直接证明最终 TSR 噪声已经改善。

本轮未执行 4096 的动态 TSR 画质 A/B 或 GPU 成本重测，不能据此确认 7.3 ms 预算。下一轮应在相同光源角度、曝光、采样数及相机轨迹下比较半影时域方差、拖影与 GPU cost。

Unity Editor 保持打开，按仓库规则未运行 Unity Test Framework；请手动运行 VirtualShadowMapSamplingTests 与 VirtualShadowMapReceiverQualityTests。检查工具和原始结果位于本目录；*.cs.txt / *.compute.txt / *.hlsl.txt 是独立诊断快照，不参与项目编译。
