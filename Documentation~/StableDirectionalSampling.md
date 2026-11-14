# 4×4 proposal 更新稳定性实验

2026-11-14，Unity 7000.0.0a16 / DX12，Cornell 顶部单面发光场景。保持原机位、16 px 网格、每探针 8 rays，不改能量、曝光或方向缓存分辨率。

## 被否决的方差收益门槛

实验仅在 32 帧块边界比较新旧分区：以当前 4×4 滤波辐亮度近似分区内方差，预测收益超过 20% 才替换旧分区。使用独立实验 kernel；默认 kernel 的生成逻辑保持原样。光照／历史失效时不保留旧分区，不增加历史纹理。

128 组实际 GPU 决策与独立双精度 CPU 公式一致，最大归一化代价误差 6.55×10⁻⁷；1,024 个描述符精确发布；无效历史均不保留。这只证明门槛实现正确，不能证明它预测了真实追踪或最终像素的方差。

每组预热 512 帧、采集连续 256 帧，固定 8 阶 / 64 样本 / 32 批 Radiosity。已完成 Hybrid 和 SDF Only 的静态配对；静态质量失败后 owner cancel 结束剩余组，`complete=false`，未把运动组计入完整验证。首尾和配对间 Radiosity atlas 字节一致，相机矩阵、探针几何、半球覆盖和 proposal 块内冻结检查通过。

| 模式 | 天花板最终方差变化 | 地面最终方差变化 | 右墙最终方差变化 |
|---|---:|---:|---:|
| Hybrid | +8.45% | +36.73% | +10.13% |
| SDF Only | −1.26% | +46.25% | −5.62% |

Hybrid 地面方差增加的 block-32 条件 95% 区间为 [+10.09%, +69.67%]；均值变化 −0.019%。天花板及右墙的方差区间均跨零，不能宣称改善。SDF Only 地面均值变化 −0.516%，短序列本身不替代长期能量验证。

按真实射线分区统计，Hybrid 拓扑变化 **932→572（−38.63%）**，SDF Only **913→556（−39.10%）**。统计排除了不改变采样的历史坐标和 Screen/World 标签。可见此次门槛确实减少了分区变化，却没有达到像素降噪目标；不继续调整门槛寻找有利结果，也不把减少变化次数当成质量收益。

证据：

```text
E:/VividRP_GI_Evidence/StableProposalValidation-20261113-235837-320/gpu-validation.json
E:/VividRP_GI_Evidence/StableDirectionalSampling-20261114-000001-240/static-analysis.json
E:/VividRP_GI_Evidence/StableDirectionalSampling-20261114-000001-240/source-prototype/
```

生产 C# 和 compute 原型已撤回。`Tools~/Diagnostics/Prototypes/StableDirectional-VarianceGate.patch` 为零上下文重放补丁；在隔离 checkout 使用 `git apply --check --unidiff-zero` 及 `git apply --unidiff-zero`，再运行 `StableDirectionalProposalValidation.cs`。不应用补丁时，工具明确报告原型已撤回。

## 下一项受控比较

现有全分辨率 À-Trous 使用 5×5 核：两轮步长 1、2 的最大支撑宽度为 13 px；已有三轮模式增加步长 4，最大支撑宽度为 29 px。前者小于当前 16 px 探针间距。比较两轮和三轮模式，先检查所有滤波前输入是否相同，再比较最终噪声、线性能量和固定轨迹；不把更大滤波范围直接当成已证实的质量收益。

后续已完成比较并为 Cornell 保留带几何边界保护的三轮模式，完整有效性检查、未采用的直接三轮候选及结果见 [三轮 À-Trous 质量验证](ScreenProbeAtrousQuality.md)。
