# PrimitiveScene V1：Author Handle 与 Camera DrawSet

## 目标边界

V1 在 V0 旁路 PrimitiveScene 上增加真实 raster 消费者：主相机的 Meshlet 不透明路径和主方向光的 Meshlet CSM 路径使用 PrimitiveScene 生成的 CPU DrawSet 作为 GPU 粗剔除输入。

V1 只替代 Meshlet opaque 与 Meshlet CSM 提交前的 Primitive 粗剔除输入。Unity `ScriptableRenderContext.Cull` 暂时保留，因为可见灯光、Reflection Probe、级联阴影矩阵、普通 Renderer 的 Shadow Caster 调度、RendererList 和编辑器预览仍依赖它。

本阶段固定以下范围：

- `MeshletRenderer` 和 `VividTerrain` 只作为 author/change source，不参与相机遍历；
- 每个 author 缓存一个非序列化、带 SceneToken 的值类型 Primitive handle；
- PrimitiveScene 维护 sparse stable slot 和 dense active culling record；
- 每个 Camera 使用 Burst 对 Primitive world bounds 做粗视锥剔除；
- 可见 Primitive 展开为 DrawSection，并按现有八个 `VividRendererListID` 状态桶形成 DrawSet；
- DrawSet 只向 GPU 上传可见 legacy instance index，GPUInstanceCulling 不再扫描全场 instance；
- 后续 Meshlet LOD、细粒度 frustum/cone、HZB occlusion、indirect args 和 Visibility Buffer 绘制保持原路径；
- 主方向光阴影使用全部 CSM 级联视锥的保守并集 DrawSet；透明、普通 Unity Renderer、非 Meshlet 阴影、RTAS 和 SDF 不切换。
- stereo Camera 在 V1 保守回退到既有全场 GPU instance culling，避免单眼视锥产生 false negative；双眼联合 DrawSet 留待后续实现。
- 不新增公开注册 API、序列化 handle 或 Pipeline Asset 开关；author handle、DrawSet 与 bridge 均保持 runtime internal。

## Author handle

CPU handle 为 blittable 值类型：

```text
VividPrimitiveHandle
{
    int  Index
    uint Generation
    uint SceneToken
}
```

`Index + Generation` 继续构成 GPU PrimitiveID 的生命周期校验；`SceneToken` 只存在于 CPU，用于防止 GPUDrivenSystem Dispose/重建后，旧 author handle 意外命中新 Scene 中相同的 slot/generation。

handle 不序列化，也不构成场景权威状态。adapter 收到 database journal 后优先验证 author candidate handle；只有 SceneToken、generation、slot allocation 和 source EntityId 全部匹配时才走直接 slot 更新，否则回退到 EntityId dictionary reconciliation。OnDisable、OnDestroy、database Clear 和 system Dispose 都会立即清空 author handle。

## Dense traversal

stable GPU slot 允许空洞，不能作为高效的相机遍历数组。PrimitiveScene 因此同时维护：

```text
stable slot tables
    PrimitiveHandle.Index -> GPU/CPU payload

dense active culling records
    [0 .. ActivePrimitiveCount)

slot-to-active indirection
    stable slot -> dense row
```

注册时向 dense records 尾部追加，删除时 swap-back，并修正被移动记录对应的 slot-to-active index。相机 Burst job 的迭代数只与 active Primitive 数量相关，不受历史 slot 高水位影响。

每条 culling record 只保存 Camera 粗剔除需要的 blittable 数据：handle、world bounds、DrawSection range、pass/primitive flags 和 GameObject layer bit。`renderingLayerMask` 不能替代 `Camera.cullingMask`，因此 author/database 会额外跟踪 GameObject layer。

## DrawSet

DrawSet 是每 Camera 的短生命周期可见性结果，状态对象只复用容量，不跨 render request 缓存结果。每次 `beginCameraRendering` 都生成新的 pending build；只有 rendering Camera、实际 culling Camera、pipeline frame index、Scene revision 与 pending 状态全部匹配时，后续提交阶段才会消费它。

```text
VividPrimitiveDrawSet
├─ visible primitive flags
├─ VividPrimitiveDrawSetEntry[]
├─ visible legacy instance index[]
├─ VividPrimitiveDrawBucket[8]
├─ GraphicsBuffer(instance indices)
└─ frame / scene revision / statistics
```

Draw entry 保留 Primitive index/generation、absolute DrawSection index 和 legacy instance index，便于调试和未来移除 legacy bridge。当前 GPU 只消费紧凑的 `uint legacyInstanceIndex` buffer。

V1 的 bounds test 使用并行 `IJobParallelFor`；确定性 bucket count/prefix/scatter 暂时使用单个 Burst `IJob`。两个 job 在 `beginCameraRendering` 边界调度，与后续 Unity `ScriptableRenderContext.Cull` 重叠，并在 GPU 上传前完成。它避免了原子竞争并先验证数据模型，但多相机超大场景仍需通过 profile 判断是否升级为并行计数与 scatter。

每个 bucket 是一个连续 range，key 直接复用当前 opaque `VividRendererListID`：

- Cull Back（Default）/ Front / Off；
- Opaque / AlphaTest；
- FlipWinding 在非 CullOff 状态下翻转 CullFront bit。

这不是任意 ShaderLab RenderState。V1 的 Blend 固定关闭、ZWrite 固定开启、ZTest 使用 Visibility shader 既有设置；Stencil、Depth Bias、Topology、透明 Blend 和排序留给后续 RasterStateClass/TransparentPipelineClass。

## 每相机流程

```text
RenderPipelineManager.beginCameraRendering boundary (per render invocation)
    dispatch external beginCameraRendering callbacks
    PrepareFrameIfNeeded
        play mode: once per pipeline frame; Editor: once per Camera render invocation
        complete + invalidate outstanding DrawSet readers before Scene mutation
        SceneDataBuilder.Build
        PrimitiveScene adapter sync / bridge rebuild
        PrimitiveScene incremental GPU upload
    extract a conservative Camera frustum
    schedule Burst PrimitiveFrustumCullJob over dense active records
    schedule Burst BuildOpaqueDrawSetJob
        validate visible Primitive / DrawSection bridge
        count eight RenderState buckets
        prefix sum bucket ranges
        scatter deterministic entries and legacy instance indices

ScriptableRenderContext.Cull
    Unity lights / probes / shadows / RendererList culling
    overlaps the scheduled PrimitiveScene jobs

GPUDriven UpdateCore (per camera)
    validate render Camera / culling Camera / frame / revision / pending token
    complete the scheduled DrawSet jobs
    publish bucket ranges and upload visible uint indices only
    on token mismatch, synchronously rebuild from the current Camera
    stereo Camera keeps the full-scene GPU instance-culling fallback
    GPUInstanceCulling dispatches DrawSet.Count threads
    existing meshlet LOD / fine cull / HZB / indirect args

PrepareFrame mutation barrier
    outstanding jobs are completed without publishing or uploading stale results
    all prior DrawSet build metadata is invalidated before NativeArrays can change

VisibilityBufferPass
    skip empty DrawSet RenderState buckets
    existing DrawProceduralIndirect per non-empty bucket
```

## 主方向光阴影 DrawSet

`GPUDrivenSystem` 在 `SubsystemPreRender` 中检测当前编译图是否包含 `CSMShadowPass`。当本相机的 CSM 数据有效时，它使用每个级联的逻辑 `projection * view` 矩阵调度独立的 Shadow DrawSet；主视图与阴影各自持有 per-camera 状态，不能覆盖对方的 pending job。

阴影 CPU 粗剔除采用所有活动级联的 OR-union：Primitive 与任一级联相交就只写入一次 legacy instance index，随后现有二维 GPU workload 再按每个级联精确剔除。Shadow DrawSet 只接受 `VividInstancePassMask.Shadows`，并与现有 shadow pancaking 一致地禁用每个级联的 near plane；cascade sphere 仍由 GPU 路径处理，因此 CPU 结果只可能增加 false positive。

Shadow job 在 PreRender 只调度，不等待也不上传。pending DrawSet 随本相机的 `VividGPUDrivenFrameData` 传给 `CSMShadowPass`，直到该 pass 已构建全部级联 context、即将调用 `CullShadowCascades` 时才 Complete 并上传紧凑 index。相机、frame 或 Scene revision 不匹配时不消费旧结果，直接回退到原有全场景 GPU 阴影剔除。图中 pass 动态不活动时，pending reader 会在下一次 `PrepareFrame` mutation barrier 或系统 Dispose 时完成并失效，且不会发布或上传。

空 DrawSet 仍先 reset GPU culling counters/indirect args，然后跳过 instance dispatch，不能保留上一相机或上一帧的结果。

## 粗剔除规则

- Primitive 必须为 Valid、非 Disabled，并包含 Main pass；
- `Camera.cullingMask` 必须包含 Primitive 的 GameObject layer；
- AABB 与六个 inward-facing frustum plane 采用投影半径测试，接触平面保守保留；
- 退化的零平面视为禁用平面，保持可见；
- Skinned Primitive 在尚无动态 bounds backend 前保守视为通过视锥；
- Terrain 仍是一个 Primitive；其粗剔除 bounds 运行时由所有 chunk bounds 求并集，因此任一部分可见时会提交该 Terrain 的全部 chunk DrawSection。chunk 级 coarse culling 需要 section bounds 或新的 chunk Primitive 策略。

CPU DrawSet 只是 coarse visibility。GPU 仍执行原有 instance/meshlet 级精确剔除，因此 DrawSet 允许 false positive，但不允许 false negative。

`beginCameraRendering` 发生在 VividRP 应用 TAA/TSR/FSR/DLSS jitter 之前。CPU coarse frustum 使用与后续 jitter 相同的 non-jittered projection 基准，并在左右、上下各增加 4 output-pixel guard band；后续 GPU culling 仍使用实际 jittered projection。该边界只增加少量 false positive，避免屏幕边缘物体因 temporal jitter 被 CPU 提前剔除。Preview Camera 不进入 GPUDriven FrameContext，因而不调度；stereo Camera 在双眼联合视锥实现前继续走旧路径。

## 资源与性能口径

V1 降低的是每 Camera GPUInstanceCulling 的输入数量和上传量：

```text
DrawSet upload bytes = visible DrawSection count * sizeof(uint)
GPU instance-cull threads = visible DrawSection count
```

全局 Primitive、Transform、Geometry、Material 表和 legacy SceneData 仍是常驻资源；现有 culling scratch buffer 在 V1 仍按全场安全上限分配。只有补齐每 section 的最大 meshlet request metadata 和 grow-only capacity 策略后，才能安全缩小这些 scratch buffer。性能报告必须区分 DrawSet 提交量、PrimitiveScene 增量上传和 legacy 全量上传。

## V1 验收门槛

- Scene 重建后旧 SceneToken handle 必须无效，full reconciliation 能重新绑定 author；
- 删除 dense row 使用 swap-back，历史 slot 高水位不增加 Burst 迭代数；
- perspective/orthographic 相机对 inside/outside/straddle/touch AABB 的结果正确；
- Disabled、Main pass、Camera layer、FlipWinding 与八个 bucket 的行为和旧 GPUDriven 路径一致；
- 一个三 section Primitive 只做一次 bounds test，生成三个 DrawSet entry；
- 两个 Camera 拥有独立状态和 buffer，不能互相污染；
- DrawSet job 在 `beginCameraRendering` callbacks 之后、Unity `context.Cull` 之前调度，并在 GPUDriven 提交点完成；
- PrimitiveScene 更新前必须完成并失效所有 pending DrawSet，且不能上传或复用被丢弃的结果；
- temporal jitter 只能让 CPU coarse frustum 产生 false positive，4 px guard band 内不能产生边缘 false negative；
- empty DrawSet 安全 reset，主视图不重用旧 indirect args；
- Shadow DrawSet 必须按 Shadows pass 过滤、对全部活动级联取并集且禁用 near-plane coarse reject；
- Shadow job 必须在 GPUDriven PreRender 调度，并延迟到 `CSMShadowPass` 的 GPU cull 前才 Complete；
- empty Shadow DrawSet 仍 reset 全部 cascade indirect args，失配时则回退 full-scene shadow instance culling；
- DrawSet-fed GPU culling 与 full-scene GPU culling 的最终 Visibility Buffer 在代表性 Meshlet/Terrain 场景中一致；
- Unity `CullingResults` 仍服务于 lights/probes/shadows/RendererList，V1 不宣称已经完成整个 SRP culling replacement。
