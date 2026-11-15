# Directional temporal 首次分歧与冻结重放

2026-11-15，Unity 7000.0.0a16 / D3D12 / RTX 5070 Ti，纯自发光 Cornell Box，1920×1080、16 px / 8 rays、原生 Gather、FP16/128、四轮 À-trous、源8阶 / 64样本 / 32批。没有修改生产 shader 或 GI 默认值，没有验证画质或性能收益。

## 短窗口与有界保存

[前一轮控制](ScreenProbeRecorderCadence.md)在 recorder / pulse 生效前已分歧，本轮移除这两个因素，将每组缩短为64帧，8次独立冷启动，源各准备264帧。仅使用现有 Editor 回调，不添加 kernel、GPU见证、fence或 native pointer查询。

[生成器](../Tools~/Diagnostics/BuildScreenProbeDirectionalBindingExperiment.py)在每帧记录完整阶段hash、前一帧纹理、CPU纹理EntityId及history owner对应关系，并根据现有C#分支重建选用的history谓词与常量。它们是 **CPU选择与对象身份**，不是 GPU b0 字节、descriptor或物理heap地址。

首组及当前比较组的8类payload暂存于有3 GiB硬上限的内存中，仅持久化首个功能分歧的前帧与当前帧。逻辑持有量3,024,814,080 B，保存32份输入／输出94,525,440 B；不是全进程内存或VRAM测量。第17帧起比上一轮额外读取previous纹理，会改变观察条件，不能将本轮视为原长序列的逐位重演。

初版脚本在dry-run发现同名局部变量和Unity 7000禁止的 `GetInstanceID()`；改为独立变量与 `GetEntityId()` 后编译及实测通过。未执行的初版保留为 `capture-script.initial-dryrun.cs`。

证据目录：`E:/VividRP_GI_Evidence/ScreenProbeDirectionalBinding-20261115-061302-342892`。实际脚本、协议、源码指纹与分析均保留，目录加入保护。

## 已复现的首次差异

8组均完成64帧，运行错误为空、其他相机零帧，2,784份源码／模板／生成器以及外部协议、实际脚本核验未变。首个保存的比较为 `00-r0-t0` 与 `01-r0-t0`：

- 第1帧全部受检纹理一致；第2帧只有 `directional-temporal` / `directional-filtered` 不同。该帧 geometry、raw、rays、proposal、composite以及previous纹理仍相同。
- CPU重建的有效history谓词和所选常量一致；浮点数组按恢复后的FP32位比较，包含signed zero。
- 全8组 **1,512对** previous→上一帧输出的内容hash一致，覆盖directional、geometry与filtered history。
- CPU对象与history owner相符，current / previous对象不同，directional owner按预期轮换。未发现CPU层的历史内容链接或对象轮换异常；这不能证明GPU执行时实际读到的内容正确。

第2帧temporal三层atlas有 **518个texel** 不同：radiance层484、hit metadata层21、plane层13；最大分量差50.80859375。它们是atlas差异，不能作为天花板最终像素噪声指标。

## 原 kernel 冻结重放

[重放脚本](../Tools~/Diagnostics/ReplayScreenProbeDirectionalTemporal.cs)在暂停状态加载保存的4类输入：geometry、previous geometry、composite、previous directional。上传后逐份GPU读回验证hash，再用记录的CPU所选常量执行原 `ScreenProbeTemporallyAccumulateTraceRadianceCS`。不写入相机的历史资源，不保存场景或Profile。

每个输入组合重复8次，另对第2帧做关闭history的负控制。分别使用ComputeShader克隆与相机当前的原对象；共34对输出逐位相同，各正常输入组合8次均稳定。

| 帧与输入 | 正常重放次数 | 匹配对应相机输出 |
|---|---:|---:|
| 第1帧，首组 | 8 | 0 |
| 第1帧，比较组 | 8 | 0 |
| 第2帧，首组 | 8 | 8 |
| 第2帧，比较组 | 8 | 0 |

第2帧两组输入相同，因此重放都返回首组结果，与比较组正好差上述518个texel。第1帧的冻结重放也没有完全匹配相机输出，差545个texel；不能把该重放称为完整执行环境等价或据此断言缺少某条barrier。

直接重放的首次请求发现Editor已非暂停，守卫在GPU工作前停止；记录保留为 `replay-direct/cli-result.initial-guard.json`。随后重新核验正常参数并暂停再运行，源码核验未变。没有覆盖首次停止记录。

另在冻结输入上，对previous VP非零分量、camera forward的W、screen size的XY做单分量±1／2／4 bit步进，每帧61种，共122次。没有精确复现第1帧相机输出或第2帧比较组输出；扫描不构成实际GPU常量末位的证明，也不支持按拟合值修改生产参数。

自然捕获、克隆重放、直接重放与末位扫描的源码／输入／实际脚本分别固定并核验。[分析器](../Tools~/Diagnostics/AnalyzeScreenProbeDirectionalBinding.py)复算保存文件SHA、CPU身份、历史链接与重放结果。重放共额外保存75,202,560 B；合计 **169,728,000 B（161.87 MiB）**，低于192 MiB上限。没有新增PIX捕帧。

## 结论与后续

本轮得到可复现的第1→2帧分歧输入，未发现CPU层history轮换或内容链接异常；冻结原kernel稳定，克隆不是本例差异来源。观察仍在pass结束后，CPU所选常量也不等于原dispatch实际使用的b0，因此实际常量、descriptor／物理资源以及读取时序仍未被证明。

下一步针对这个更短的窗口，尝试连续0～3帧的原dispatch捕获，检查实际b0与资源访问。只有捕获内也复现相同分歧，才能将它作为故障dispatch证据；捕获若改变结果，须保留这一限制，不能替代原异常。

生产shader未改动，不添加猜测性栅栏、坐标量化或舍入补丁，不推广FP32/512。重放后重新运行正常相机帧以重绑共享ComputeShader参数，实测默认128、16/8、四轮、bias0.04、源8/32；两个场景干净、hooks为空、override=-1、flags0、PIX idle，Playing=true / Paused=true。Console游标202→203无新错误、无drop/reset；未运行Unity Test Framework（Editor保持打开）。容量复核见 [数据保留](GiEvidenceRetention.md)。
