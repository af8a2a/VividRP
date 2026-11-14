# GI 实验数据保留与清理

继续推进 GI 质量时，在每轮实验收尾检查磁盘；大批量捕帧前也检查剩余空间。该步骤属于当前目标的工作流程，不需要后台定时任务。

默认证据目录是 `E:/VividRP_GI_Evidence`。软上限 **125 GiB**，E 盘剩余空间低于 **64 GiB** 时也触发清理审查。阈值用于提醒审查，不能作为任意删除证据的依据；它们不是分配配额或后台自动清理服务。新捕帧预计将越过阈值时，先审查旧数据，再开始采集。

## 保留原则

- 当前候选、当前默认配置的完整回归、高采样参考、场景线性输出、源缓存及其依赖保持完整。组合清单可能只引用其他目录的原始文件；检查其引用来源后才能归档。
- 所有分析报告、汇总对比图、捕帧清单、源码与 kernel 指纹、错误记录、候选补丁、Profile 备份保留。后续实验避免保存大量逐帧 PNG，采用 ROI 数据、汇总图和代表帧。
- 已被替代或拒绝的旧实验可只保留每个子目录的首尾完整帧包，清理中间帧 GPU `.bin` 读回。它们仍能说明历史结论，但不能再完整复算原长序列。
- 不按文件夹年龄猜测有效性。先核实捕帧进程/会话已经终止且诊断回调已释放；锁文件、旧清单或观察超时本身不能证明终止。活动捕帧期间不清理。
- 不清理 Unity 工程资产、用户截图、其他项目或未知所有者目录。这里的工具只处理明确列出的实验目录，不递归删除目录。

## 工具

`Tools~/Diagnostics/GiEvidenceRetentionPolicy.json` 列出保护目录与已审查的归档目录，每个归档项带理由和必须存在的证据。每轮有新的当前候选或参考时，更新保护项；旧项是否可归档仍需审查。

```powershell
# 只读统计；默认操作不删除数据。
python -B Tools~/Diagnostics/GiEvidenceRetention.py
# 预览明确列出的旧实验及可释放字节数。
python -B Tools~/Diagnostics/GiEvidenceRetention.py --policy Tools~/Diagnostics/GiEvidenceRetentionPolicy.json
# 确认没有活动捕帧、审查预览和引用后，执行同一白名单。
python -B Tools~/Diagnostics/GiEvidenceRetention.py --policy Tools~/Diagnostics/GiEvidenceRetentionPolicy.json --apply
```

工具拒绝路径越界、重复/受保护目录、链接与 reparse point。仅允许删除文件名以四至六位帧号开头的 `.bin`，完整首尾帧、非帧文件及编译 shader 二进制不删除。删除前校验大小/修改时间、计算 SHA-256，并先发布删除批次清单；只对已经验证的文件逐个 unlink。计划后发生变化即停止，不覆盖旧清理审计。

每个被归档目录新增 `raw-retention.json` 和 `raw-retention-files.jsonl`，记录理由、原证据 hash、删除文件 hash、首尾保留策略及完成/失败状态。`rawReanalysisAvailable=false` 明确表示完整原始序列已归档清理。中断时以文件清单与实际文件是否存在核对，不把未完成批次当作已完成。根目录 `retention-*.json` 汇总实际释放空间与是否需要继续审查。

安全回归使用独立临时目录，不启动 Unity 测试或访问真实证据：

```powershell
python -B Tools~/Diagnostics/test_gi_evidence_retention.py
```

## 2026-11-14 首次清理

在没有活动捕帧、最近的恢复检查确认诊断回调为空后，归档 13 个明确被替代或失败的旧实验。删除 **140,300** 份中间帧 GPU 读回，共 **33,370,699,776 B（31.079 GiB）**。目录由约 **153.856 GiB** 降至 **122.800 GiB**；当次 E 盘可用空间为 **113.154 GiB**，低于占用软上限且高于剩余空间下限。

当前候选、最新单面回归、线性夹具、高采样参考、已接受的预滤波静止/运动/光照响应数据及组合清单来源均未清理。298,384 个保护文件的大小和修改时间与清理前一致；归档目录中 148 份保留的报告、图片、快照等文件 SHA-256 均未变。删除日志和实际文件缺失逐项相符，所有归档项均完成；8 项独立文件安全测试通过。没有启动 Unity 测试。

```text
E:/VividRP_GI_Evidence/retention-before-20261114-1207.json
E:/VividRP_GI_Evidence/retention-20261114-121211-851121.json
E:/VividRP_GI_Evidence/retention-verification-20261114.json
```

旧实验的历史统计仍保留；其完整中间帧已删除，不能再将这些旧目录宣称为可完整复算。首尾帧包与编译二进制仍在。默认渲染设置没有因本次清理改变。
