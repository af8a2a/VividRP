# GCReport 优化记录（2026-09-20）

数据源：`E:/VividRP_Reborn/GCReport/allocations.csv` 和 `marker-table.csv`。
统计以逐条 allocation 为准；marker 表中同名条目可能来自不同上下文，不能当作同一个调用点。

## 采样范围

- 共 77,510 条，140,573,099 B（约 134.06 MiB）。
- 第 1 帧：139,748,584 B；第 2 帧：824,515 B。
- 约 99.4% 出现在第一帧。这份报告不足以判定稳态每帧分配。

## 已处理的主要路径

| 路径 | 原报告分配 | 修改 |
| --- | ---: | --- |
| GetOrAppendMeshletAsset → List.set_Capacity | 62,499,328 B | 静态重建前统计去重资源的最终数量，一次准备四类场景列表容量 |
| GetOrAppendMeshletAsset → List.InsertRange | 3,811,948 B | 同上，避免 AddRange 继续增长后备数组 |
| UploadStructuredBuffer | 16,877,056 B | 使用 GraphicsBuffer.SetData(List<T>)，移除结构化上传中转数组及复制 |
| ReadStructArray → BinaryReader.ReadBytes | 8,396,320 B | 直接读取到最终类型数组的字节 Span |
| ReadLZ4Payload → BinaryReader.ReadBytes | 5,018,617 B | 直接读取序列化数据中的压缩片段，不再复制压缩输入 |
| VividLZ4Codec.Decompress | 9,330,190 B | 增加调用方提供输出的接口，反序列化复用 ArrayPool 临时缓冲 |

这些数值是原采样中相关路径的分配量，并非全部可消除：场景列表的最终后备存储和池首次租赁仍需要内存。
反序列化的最终数组由资源独立持有，不使用池内存作为返回结果。
解析流长度严格限制为有效解压长度，不能把池数组的多余容量视为有效数据；成功和异常路径均归还临时缓冲。

## 验证

- Runtime、Editor、Editor.Tests 三个程序集通过 Roslyn 编译。
- 独立 Mono 环境执行 11 项序列化/解压检查，包括版本 1–4、空数据、重复与随机数据、损坏 LZ4、输出切片边界、池容量边界和输出所有权。
- 独立检查将 Unity 的 SizeOf/MemCpy 接口替换为等价的 sizeof/Buffer.MemoryCopy；不包含 Unity 原生引擎或 GPU 验证。
- 调用方复用输出的 LZ4 解压，预热后测得 0 B 托管分配。
- 同一组 83 次反序列化基准：修改前 32,715,944 B，修改后 10,907,528 B，降低约 66.7%。最终顶点数组的有效数据本身占 10,878,976 B。该比例不是原场景的实测改善率。
- 已补充列表容量、直接上传分配与 GPU 数据读取检查；Editor 正在运行，未启动 Unity Test Framework。
- `git diff --check` 通过。

## 仍需保留或复测

- VTResidencyManager 等初始化阶段持有的数组、反序列化最终结果属于实际存储，不能直接删除。
- 原始索引上传仍保留满足四字节对齐与清零要求的中转数组。
- UIElements/Editor 等引擎内部调用不在本次修改范围。
- 请手动运行 GPUDriven 的相关 Unity 测试，并重新采集同场景进入 PlayMode 的前几帧及一段稳定运行帧，以验证场景收益和剩余热点。
