# Screen Probe 原生 PIX 绑定与常量证据

2026-11-15，Unity7000.0.0a16 / D3D12 / RTX5070Ti / PIX2606.18-preview。承接[追踪重复性定位](ScreenProbeTraceRepeatability.md)。本轮取得真实捕帧及原dispatch的常量上传字节，没有新增降噪或性能改善结论，默认shader/Profile保持原行为。

## 捕帧身份与范围

用户明确授权正常退出并带PIX注入重启`E:/VividRP_Reborn`。旧Editor10380退出、工程锁消失后，官方`pixtool launch`启动Editor42572，launcher36960；原生桥返回S_OK、D3D12、正确的WinPixGpuCapturer路径。重启前唯一未保存的默认VolumeProfile已先备份磁盘及内存数据，再保存原值。

原watchdog晚于launcher启动，因时间/初始PID判定竞态退出；失败日志保留。修正版以授权时间、launcher身份和父子进程链附加到同一个Editor，没有重复启动或强杀。重附后的60秒监控完成，约5.4GiB/3700handles；首次监控失败至重新附加之间存在观测缺口，不能宣称完整启动过程已覆盖。启动监控脚本为本轮外部证据，不属于Runtime改动。

证据根目录：`E:/VividRP_GI_Evidence/ScreenProbeOriginalStageControl-20261115-021737-918072`。

- 完整捕帧：`pix-gi-consolefix/capture-1.wpix`，170,517,013 B。
- SHA256：`8f0e46703eb1d0a727cacd198d226cd76be7a01a33c50efe31fe6ab6ccd3a120`。
- Session：`a6666f51e8834021a383c57ca3d33ae5`；精确marker：`VividRP.RenderPass.Record/12:SdfDiffuseGatherPass`。
- 相机：`Emissive Cornell Camera`，1920×1080，16px/8rays，临时固定采样1735。默认8阶/32批Radiosity已就绪。
- 内容校验：2个物理queue，目标pass内16个GPU工作事件，四个逻辑queue scope边界成对且有序，Console游标范围内没有新增错误。

`diagnosis.json`先选择该pass中第一个有效工作事件799（Adaptive Placement），不将其冒充追踪事件。随后使用同一捕帧的事件树和SHA进一步查询：

| marker范围 | queueId / eventIndex | 实际工作 | 资源视图（静态 / replay访问） |
|---|---|---|---|
| Diffuse GI Screen Trace | 1 / 818 | Dispatch 15×17×1 | 15 / 15 |
| Diffuse GI Gather and SDF Fallback | 1 / 829 | Dispatch 15×17×1 | 33 / 27 |

两者`queueType=0`，是graphics queue上的compute dispatch。原始查询与访问结果位于`pix-trace-dispatches/`；没有GPU耗时或瓶颈结论。Shader entry/hash等字段由SDK返回空/null，仍记录为不可用。

## 从官方 C++ 导出读取常量

官方`pixtool open-capture ... export-to-cpp ...`成功导出，未编译或运行导出的回放程序。`CommandLists_000.cpp`保留原调用及barrier：Screen和SDF的root b0分别指向resource353偏移2,693,376及2,694,400。读取各1024B窗口，不将窗口长度或SDK报告的剩余资源长度当成shader声明的cbuffer大小。

`ResourceModifications_000.cpp`与页表证明：main command-list1358提交前，modification4更新resource353的12个页。`resources.bin`末尾八个运行时更新块按生成的reader调用顺序排列；取第四块3811B，使用生成代码相同的Windows XPRESS解压为49,152B，并按页号重组窗口。后续修改不触及两个b0所在页657/658。`ReverifyPixConstantExtraction.py`实际运行，逐位核对所有解压数据和提取结果。

| b0窗口内字段 | 字节偏移 | Screen/SDF结果 |
|---|---|---|
| inverseVP | 352～415 | 逐位一致 |
| VP | 416～479 | 逐位一致 |
| previousVP | 480～543 | 逐位一致 |
| screenSize | 624～639 | 1920、1080及倒数，逐位一致 |
| sampleFrame | 792～795 | uint1735，逐位一致 |

无名称的前缀/未使用槽位不作为有效shader输入解释，其中的旧值/NaN不能单独认定为故障。

两个dispatch的全局b1均指向heap7偏移11,472,896。SDK资源查询对此返回E_NOINTERFACE，导出则明确将其解析为placed resource91（分配2048B，名称`Buffer-512-1856`）。因此该返回值不等于GPU漏绑。

原始初始化数据另存，但它不是事件时刻的数据。生成的实际调用先在command-list1311从resource353偏移2,559,392复制1856B至resource91，再在同一graphics queue批次的command-list1358执行两条追踪。按该更新顺序提取的`global-b1-at-trace-1856.bin`与当前HLSL布局一致：相机位置`(0,1.5,-3.8,1)`、正交标志0、屏幕1920×1080、jitter0。没有修改GPU共享struct或其布局。

## 已确认的依赖与结论边界

resource1149由Screen Trace作为UAV写入，再由SDF作为SRV读取；两次dispatch之间确实包含该资源的UAV barrier与`UNORDERED_ACCESS → NON_PIXEL_SHADER_RESOURCE`转换。HZB和directional guide读取前也有相应转换。这份捕帧没有支持“缺少Screen→SDF barrier”的解释；不能将单份正常帧推广为失败A/A全部时序的证明。

本次是原shader、两个场景加载、14个注册receiver的固定采样捕帧，区别于此前隔离播放实验的8个receiver。下一步应在已注入的干净Editor中重新运行原A/A，并在分歧附近捕获对应状态。当前没有证明首次失配的根因，也没有据此推广FP32/512或精度重建原型。

## 工具修复、恢复与保留

当前Unity Pipeline的命令schema为`console get`和`--since-session`。旧PIX脚本在捕帧前调用旧命令而停止，失败与原脚本字节副本保留；随后最小修复命令与游标参数。15项离线回归通过，新增用例覆盖当前命令路径、session跟随及cursor0。修正版完整PIX捕帧/内容校验/查询通过，2,783份源码与实际hook脚本指纹未变。未运行Unity Test Framework。

固定采样hook记录method/module所有权，`finally`只释放本次hook并解锁程序集；PIX session已释放为idle。Editor仍正常运行，默认age128/四轮/16px/8rays；最终恢复证据另存于根目录。

为避免重复占用，核验完整捕帧SHA与提取脚本后，删除170,517,013B的closed live副本，以及169,859,959B可重新导出的`pix-cpp/resources.bin`，共340,376,972B。保留canonical完整`.wpix`、所有C++源、所需压缩块、原始常量字节和审计。C++目录当前不能直接构建/回放，需先从保留的捕帧重导出数据；不能宣称仍保留全套导出载荷。

`pix-retention.json`与`post-pix-storage.json`记录本次删除。清理后证据125.044GiB、E盘可用115.910GiB，略高于125GiB软审查线，`needsFurtherReview=true`保持真实状态；下一轮大捕帧前继续审查预算。
