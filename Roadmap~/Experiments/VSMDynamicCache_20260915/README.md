# VSM 动态缓存复用扩大

日期：2026-09-15。基线：`689aab661581b578ffabb53b8e5fe91c7a5c9cab`（补页压力与分配优化之后）。

后续补测：[场景跑测与阶段计时](timing/README.md)。已完成 24 个 A/B/B/A 窗口及 8 组全层深度对照；8192 静止动态对象的清页由约 0.344 ms 降至 0.119 ms，两池约 35.47 亿深度值差异为 0。以下保留首次实现时的验证范围；实际计时、短位移与禁用的新增证据以补测报告为准。

## 已实现的行为

**普通动态 meshlet 投影者现在可以逐页复用动态池。** 对象不需要标记为 Static；内容未变且缓存配置有效时，保留其已有深度。发生移动、增删、显隐或资源变化时，重绘受影响的页面。静动态双池及每池 16 层深度保持不变。

此前动态池每帧清空并重绘所有驻留页；现在通过动态变更日志、独立 dirty 位与占用有效位，跳过干净动态页的清页、深度覆盖、原子层插入及重复判空扫描。这是机制落地，**尚无生产场景帧时收益或运动画质 A/B 结论**。

## 1. 失效范围与质量边界

| 情形 | 当前处理 |
|---|---|
| 普通动态 meshlet 的变换、包围盒和资源均未变化 | 复用动态页，无需等待若干静止帧 |
| 移动或缩放 | 记录旧、新世界包围盒，各自投影到 clipmap；重绘受影响的已分配动态页，清掉旧位置残影 |
| 新增、删除、启用、禁用或阴影通道变化 | 按存在阴影的旧/新状态记录对应包围盒 |
| Static 与 Dynamic 分类切换 | 分别使旧池与新池的相关页面失效 |
| 材质或几何资源变化 | 现有依赖变更链扩展到动态池；无法定位时全量刷新两池 |
| VT alpha 采样变化 | 使两池中所有 alpha 投影者的包围盒失效；不主动刷新无关 opaque 页 |
| 变更日志溢出、非法 bounds、revision 已变但日志缺失 | 全量刷新动态池；日志上限为 1024 条 bounds，移动对象可能占两条 |
| 缓存身份、投影配置或资源描述变化 | 全量刷新；沿用现有 clipmap remap 与配置 key |
| 当前相机层掩码内存在动态 Skinned 投影者，或本帧存在 Unity Renderer 阴影路径 | 保守地每帧刷新整个共享动态池；最后一个此类投影者消失后再全刷一帧 |

Skinned 的骨骼变化可能不经过变换/资源日志，且现有阴影剔除也不信任其 bounds；Unity Renderer 路径尚无同等内容日志。因此这两条路径还不能获得选择性的动态池复用。缓存仍为全局共享资源，相机身份切换会触发全刷。

完成页面处理后才按捕获的 revision 确认变更；过期确认不会丢弃之后的移动。新分配或重新分配的页面同时带上两池 dirty 位，必须完成清页、绘制、判空和 finalize 后才能采样。复用期间不减少层数，也未引入首层遮挡剔除；有限 16 层容量的原有表示边界仍然存在。

源码入口：

- [动态变更日志与保守回退](E:/VividRP_Reborn/Packages/VividRP/Runtime/SubSystem/PrimitiveScene/VividPrimitiveScene.cs:119)
- [Prepare 的缓存判定](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:690)
- [绘制前动态页失效](E:/VividRP_Reborn/Packages/VividRP/Runtime/RenderPass/Core/CSMShadowPass.cs:1497)
- [共享 bounds 投影及两池独立清页](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Private/CSMShadowResolve.compute:867)
- [只写入对应池的脏页](E:/VividRP_Reborn/Packages/VividRP/Shaders/Core/Public/Shadow/VividVirtualShadowMapCaster.hlsl:49)

## 2. 成本分解

| 成本 | 本轮改变 | 仍保留的成本 |
|---|---|---|
| 变更收集和页失效 | 复用场景变更日志；新增 `VSM.InvalidateDynamic` sampler，仅变化时标记动态 dirty | 检查活动 Skinned 投影者；上传变化 bounds；每个 bounds/投影仍由一个线程遍历覆盖矩形 |
| 多层清页 | 干净动态页跳过全部 16 层写入 | 清页 dispatch 与读取页元数据；脏页的完整清理 |
| CPU 提交与物体剔除 | 没有动态 meshlet draws 时跳过整个动态 raster 块 | 存在动态对象时仍可能执行剔除、间接命令提交和 DSV 设置/清理，GPU 页列表可以为空 |
| GPU 覆盖与层插入 | 页列表只收集该池脏页，caster 写入也检查对应 dirty；干净页无覆盖和插入 | 脏页完整重绘相关投影者；每个覆盖片元仍执行原有 16 层原子插入 |
| 页判空 | 复用两池各自的 occupancy-known 结果 | dirty 或结果未知时重新扫描；扫描仍按原有首层非空证明实现 |
| 分配与质量反馈 | 继续使用上一轮补页压力控制与请求分配 | 本轮未进一步并行化串行提交阶段，也未改变预算/质量参数 |

每复用一个标准动态页，可避免 `128 × 128 × 16 × 4 = 1 MiB` 的显式清零写入 payload。1024 个动态页全部干净时，对应每帧最多 **1 GiB 清零 payload**。这是源码计算，不是显存占用下降、实际总线流量或测得的 GPU 毫秒收益。双池资源容量未缩减；判空扫描本身可能提前退出，不能据此给出固定每页读取节省量。

保留现有 `VSM.DynamicRasterClear`、`VSM.DynamicRasterDraw`、`VSM.PageCull` 等 sampler，并新增动态失效 sampler。`metadata.w` 的 bit 15 标记本帧动态重绘，既有通用 dirty/redrawn 位仍有效。`DynamicRefreshCount` 仍表示完成动态池处理的帧数，**不能作为页面重绘数量**。

## 3. 验证结果

环境：Windows、Unity **6000.7.0a6**、D3D12；DXC **1.4.350.0 SDK 路径**。测试运行时交互 Editor 已退出，每次 batch 启动前检查不存在 Unity 进程。

| 验证 | 结果 |
|---|---|
| DXC：CSMShadowResolve 与采样测试 compute 全部入口 | 54/54 编译通过 |
| DXC：caster 的顶点/片元、页/兼容、alpha、VT 和 caster 关键字组合 | 32/32 编译通过 |
| 独立 Roslyn：Runtime / Editor / Editor.Tests | 3/3 编译通过；最后的测试夹具修改另由 Unity batch 重新编译 |
| PrimitiveScene、缓存/页面管理、采样与一项资源失效源码契约测试 | 分批最终覆盖 **260 个不同测试，全部通过，0 跳过** |
| 稳定/移动动态日志及动态 bounds 上传/缓存 key 热路径 | 32 次预热、256 次迭代后，当前线程托管分配均为 0 bytes |
| 隐藏层保持 | GPU readback 检查两池共 8192 个值，覆盖全部 16 层：仅 dirty 池清零，其余值逐项保持 |

GPU 生命周期用例包含静态单独 dirty、动态单独 dirty、两池 dirty、干净缓存和未映射物理槽；还检查独立判空、重复判空复用、finalize 状态、动态重绘记录、全动态失效保留静态状态及未映射槽。已有 9 组 bounds 边界用例同时检查静态和动态 kernel。

关键测试：[GPU 页面生命周期](E:/VividRP_Reborn/Packages/VividRP/Tests/Editor/RenderPass/Shadows/CascadedShadowSettingsVolumeTests.cs:837)、[动态缓存零分配](E:/VividRP_Reborn/Packages/VividRP/Tests/Editor/RenderPass/Shadows/CascadedShadowSettingsVolumeTests.cs:1661)、[移动/显隐/资源/移除](E:/VividRP_Reborn/Packages/VividRP/Tests/Editor/SubSystem/PrimitiveScene/VividPrimitiveSceneTests.cs:702)。

### 回归过程中的失败与修复

| 报告 | 通过 / 总数 | 说明 |
|---|---:|---|
| `tests.xml` | 118 / 118 | 首轮 PrimitiveScene 与设置/页面测试 |
| `tests-final.xml` | 238 / 260 | 扩大采样回归后 22 项失败；旧夹具缺少上一轮新增的请求位图、分配 prepare 与压力缓冲绑定 |
| `tests-sampling-repaired.xml` | 134 / 141 | 补齐绑定后，暴露 7 项 debug 输出维度错误：二维 UAV 错建成 16 层数组 |
| `tests-sampling-final.xml` | 141 / 141 | 修复二维 debug 纹理描述后全部通过 |

最终 260 项由第二份报告中其余 119 项通过结果，加最后一份报告的 141 项通过结果组成；没有把中间失败报告改写为成功。测试夹具修复只补齐资源/分配协议和正确的输出维度，未放松阴影结果断言。[机器可读汇总](validation/test-summary.json) 保留逐项最终结果来源。

## 4. 运行时事故与未完成的实场验证

交互 Editor 在 Play Mode 程序集热重载期间发生 `DXGI_ERROR_DEVICE_HUNG (0x887a0006)` 并退出。日志未报告本地显存超预算，**尚不能确定是否由本次改动引起**。[相关日志摘录](validation/editor-device-hung.txt) 已保留。

审计时发现新增 kernel 放在 pragma 列表中间会改变既有 kernel 索引，热导入期间可能与 C# 缓存索引不一致。最终代码将两个新入口追加在列表末尾，保持基线所有入口的索引；这只是去除了一个具体风险，不能证明崩溃根因或修复完成。后续 batch GPU 测试通过，也不能替代生产场景长时间运行。

本轮未重新启动交互 Editor，未改动场景或相机，未完成生产场景 GPU 计时、连续运动/显隐图像 A/B 或全线程 Unity Profiler 分配检查。因此结果只支持缓存机制、编译及覆盖用例的正确性；场景收益、全渲染线程零分配与热重载稳定性仍待实场验证。

下一次实场验证应在新启动的 Editor 中，对固定机位、单动态物体移动/停下/移除、动态 Skinned 出入及相机移动分别记录 dynamic dirty 页面数和上述 sampler；使用完整两池 16 层深度作等价参考。若少量 Unity/Skinned 投影者持续阻止整池复用，后续工作应先缩小其保守失效范围或隔离其存储，并建立可信的变形范围。

## 5. 归档

- `source-before/` 与 `source-final/`：本轮 8 个运行时/着色器文件及 4 个测试文件的文本快照。
- [source-manifest.json](source-manifest.json)：基线提交和每个文件的 SHA-256。
- `validation/`：DXC/Roslyn 编译结果、脱离启动命令/环境信息的测试 XML、崩溃摘录。
- `scripts/`：本机编译和归档脚本；编译脚本依赖现有 Unity/Bee 响应文件及 `Temp~/vsm-bnd-compare/dxc2/include`，不是独立分发工具。

未提交 Git。原始实场性能报告与上一轮压力参数保留各自原有口径。
