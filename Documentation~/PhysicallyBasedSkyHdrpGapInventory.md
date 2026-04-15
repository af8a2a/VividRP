# Physically Based Sky HDRP Gap Inventory

## 目的

- 在继续移植 PBRSky 之前，先把 VividRP 当前相对 HDRP 17.5 的关键偏差点收口成一份清单。
- 这份文档只记录“当前仍然存在的兼容层、MVP 简化、临时 fallback、以及尚未移植的 HDRP 保护措施”。
- 后续如果某个 workaround 被移除或替换，应该同步更新这份文档，而不是只改代码。

## 参考基线

- HDRP 基线：
  - `E:/hdrp17.5/Packages/com.unity.render-pipelines.high-definition/Runtime/Sky/PhysicallyBasedSky/PhysicallyBasedSky.cs`
  - `E:/hdrp17.5/Packages/com.unity.render-pipelines.high-definition/Runtime/Sky/PhysicallyBasedSky/PhysicallyBasedSkyRenderer.cs`
  - `E:/hdrp17.5/Packages/com.unity.render-pipelines.high-definition/Runtime/Sky/PhysicallyBasedSky/PhysicallyBasedSky.shader`
  - `E:/hdrp17.5/Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/AtmosphericScattering/AtmosphericScattering.hlsl`
- VividRP 当前实现：
  - `Runtime/SubSystem/Sky/PhysicallyBasedSky/`
  - `Shaders/Core/Private/Sky/`
  - `Shaders/Core/Private/AtmosphericScattering/`
  - `Runtime/RenderPass/Core/Sky/AtmosphericScatteringPass.cs`

## 1. Shader 入口与桥接层

### 当前状态

- HDRP 仍保留 `PhysicallyBasedSky.shader` + `PhysicallyBasedSkyRendering.hlsl` + `PhysicallyBasedSkyEvaluation.hlsl` 的分层入口。
- VividRP 目前已经收敛成单入口 `Shaders/Core/Private/Sky/PhysicallyBasedSky.shader`，并通过 `PhysicallyBasedSkyBridge.hlsl` 桥接运行时和 baking 两条路径。

### 当前 workaround / 偏差

- `Shaders/Core/Private/Sky/PhysicallyBasedSkyBridge.hlsl` 仍保留 `EvaluateAtmosphericFallback(...)`，用于 `SkyViewLUT` 不可用时的 fallback raymarch。
- `EvaluateSky(float3 directionWS, float2 positionCS)` 目前没有真正消费 `directionWS`，而是重新用 `GetSkyViewDirWS(positionCS)` 计算视线方向。
- bridge 文件里同时承载了 HDRP 原始 sky 评估、Vivid 的材质绑定、bindless surface texture、日盘和 fallback 逻辑，职责比 HDRP 的分层实现更混合。

### 风险信号

- 屏幕天空错误，但 cubemap baking 相对正常。
- `SkyViewLUT` 缺失时画面“能出图但味道不对”，而不是完全报错。
- 修 fullscreen sky 时容易误伤 baking 路径，反之亦然。

### 关键文件

- `Shaders/Core/Private/Sky/PhysicallyBasedSky.shader`
- `Shaders/Core/Private/Sky/PhysicallyBasedSkyBridge.hlsl`
- HDRP 对照：`Runtime/Sky/PhysicallyBasedSky/PhysicallyBasedSky.shader`

## 2. 预计算缓存结构已和 HDRP 分叉

### 当前状态

- HDRP 使用 `PrecomputationCache` + `PrecomputationData`，由 `GetPrecomputationHashCode(HDCamera)` 统一索引，并带引用计数。
- VividRP 现在已经把 PBRSky 预计算资源的所有权收回到 `PhysicallyBasedSkyRenderer`：
  - `PhysicallyBasedSkyAtmosphereLutCache` 作为 renderer 内部缓存，负责 `MultiScatteringLUT` / `SkyViewLUT` / `AtmosphericScatteringLUT`
  - renderer 自己仍持有 local sky world-space precompute 资源，如 `m_GroundIrradianceTable` / `m_AirSingleScatteringTable`

### 当前 workaround / 偏差

- 虽然所有权已经回到 renderer，但当前仍不是 HDRP 那种单个 `PrecomputationData` 对象，而是“renderer 内部 LUT cache + renderer 内部本地表”的组合。
- fullscreen sky、runtime cubemap、ambient probe cubemap 之间仍不是同一个引用计数共享缓存对象。
- `PhysicallyBasedSkyRenderer.ResolveSkyViewTexture()` 和 baking 路径虽然都改成直接查询 renderer 自己的 cache，但 hash 和生命周期仍是 Vivid 自己的实现，不是 HDRP 的 `GetPrecomputationHashCode(HDCamera)` + ref-count 模型。
- ambient probe 在 realtime 下允许走 `TryCopyRuntimeCubemapToAmbientProbe(...)`，这是 Vivid 为避免重复 baking 增加的捷径，不是 HDRP 原始结构的一部分。

### 风险信号

- fullscreen sky 和 runtime cubemap 看起来不是同一套天空。
- realtime 下重复执行 precompute / rebake。
- hash 没变但某一条路径还是用了旧 LUT 或旧 cubemap。

### 关键文件

- `Runtime/SubSystem/Sky/PhysicallyBasedSky/PhysicallyBasedSkyAtmosphereLutCache.cs`
- `Runtime/SubSystem/Sky/PhysicallyBasedSky/PhysicallyBasedSkyRenderer.cs`
- `Runtime/SubSystem/Sky/SkyManager.cs`
- HDRP 对照：`Runtime/Sky/PhysicallyBasedSky/PhysicallyBasedSkyRenderer.cs`

## 3. Celestial Body 数据来源比 HDRP 更宽松

### 当前状态

- HDRP 的 celestial body 数据主要来自 `HDAdditionalLightData` 的 directional lights。
- VividRP 目前使用更宽松的 fallback 链：
  - visible lights
  - scene lights
  - `VividLightData.directionalLights`
  - `RenderSettings.sun`

### 当前 workaround / 偏差

- `PhysicallyBasedSkyCelestialBodyUtility` 中存在 `BuildFallbackApproximateCelestialBodies(...)` 和 `CreateApproximateCelestialBody(...)`。
- Vivid 的 celestial body 还带了 bindless `surfaceTextureIndex`、自定义 `shadowIndex`、`VividAdditionalLightData` 相关字段，这些都不是 HDRP 的原始最小路径。
- “实际光源路径失败后再回落到近似太阳”这一层，是 Vivid 当前为了兼容现有 light data 接口保留的 MVP 兜底。

### 风险信号

- `_CelestialLightCount` 为 0，或者与主方向光数量不一致。
- 日盘位置、亮度、阴影或 flare 只在某些场景配置下错误。
- 屏幕天空使用的是 fallback 太阳，而 shading / shadow 使用的是主方向光。

### 关键文件

- `Runtime/SubSystem/Sky/PhysicallyBasedSky/PhysicallyBasedSkyCelestialBodyData.cs`
- `Runtime/ComponentData/VividAdditionalLightData.cs`
- HDRP 对照：`Runtime/Sky/PhysicallyBasedSky/PhysicallyBasedSkyRenderer.cs`

## 4. 行星坐标模型已收回到共享 runtime planet state

### 当前状态

- HDRP 通过 `hdCamera.planet.center`、`hdCamera.planet.radius`、`hdCamera.planet.renderingSpace` 驱动行星坐标与渲染空间。
- VividRP 现在也改成通过共享的 `SkyPlanet` runtime state 统一解算：
  - `SkySettingsVolume.renderingSpace`
  - `SkySettingsVolume.centerMode`
  - `SkySettingsVolume.planetCenter`
  - `PhysicallyBasedSkyVolume.planetRadius`
- `ShaderVariablesGlobal`、`PhysicallyBasedSkyShaderParameters`、`PhysicallyBasedSkyRenderer.ResolveCameraPosition(...)` 现在消费的是同一套 planet 数据，而不是各自重新假设 `world Y up`。

### 当前剩余偏差

- HDRP 把 planet 数据缓存到 `hdCamera.planet`；VividRP 目前仍是在需要时从 active volumes 解析 `SkyPlanet`，还没有单独的相机级缓存对象。
- HDRP 的 `planet.radius` 属于 `VisualEnvironment`；VividRP 当前仍把半径保留在 `PhysicallyBasedSkyVolume`，`SkySettingsVolume` 只承载 `renderingSpace` 与 center 语义。

### 风险信号

- 后续如果云层、体积雾或其他行星相关系统开始消费 planet 数据，必须复用 `SkyPlanet`，不能再各自手推 `(0, -R, 0)`。
- 如果未来把 `planetRadius` 从 `PhysicallyBasedSkyVolume` 挪到共享环境 volume，需要同步审查 sky hash、local sky precompute hash 与 baking 路径。

### 关键文件

- `Runtime/SubSystem/Sky/SkySettingsVolume.cs`
- `Runtime/RenderGraph/FrameContext/ShaderVariablesGlobal.cs`
- `Runtime/SubSystem/Sky/PhysicallyBasedSky/PhysicallyBasedSkyShaderParameters.cs`
- `Runtime/SubSystem/Sky/PhysicallyBasedSky/PhysicallyBasedSkyRenderer.cs`
- HDRP 对照：
  - `Runtime/RenderPipeline/Camera/HDCamera.cs`
  - `Runtime/Sky/VisualEnvironment.cs`
  - `Runtime/Sky/PhysicallyBasedSky/PhysicallyBasedSkyRenderer.cs`

## 5. 曝光接口已替换成 Vivid 自己的链路

### 当前状态

- HDRP fullscreen sky pass 使用 `GetCurrentExposureMultiplier()`。
- VividRP 当前统一通过 `AutoExposure.hlsl` 的 `VividGetPreExposure()` / `VividApplyPreExposure(...)` 接入自身曝光链路。

### 当前 workaround / 偏差

- fullscreen sky 在 `Shaders/Core/Private/Sky/PhysicallyBasedSky.shader` 里做 pre-exposure。
- `SkyLUTGenerator.compute` 在写入 `_AtmosphericScatteringLUT_RW` 时直接乘了 `VividGetPreExposure()`。
- 这条链路是正确的 Vivid 集成方向，但它并不是 HDRP 原始的 shader variables / exposure helper 接口，因此回归排查时要同时看 sky shader 和 LUT compute。

### 风险信号

- 只在开启自动曝光时天空或大气散射异常。
- fullscreen sky 和 atmospheric scattering pass 的亮度不一致。
- cubemap baking 看起来正常，但屏幕天空过亮 / 过暗。

### 关键文件

- `Shaders/Core/Public/AutoExposure.hlsl`
- `Shaders/Core/Private/Sky/PhysicallyBasedSky.shader`
- `Shaders/Core/Private/Sky/SkyLUTGenerator.compute`
- HDRP 对照：`Runtime/Sky/PhysicallyBasedSky/PhysicallyBasedSky.shader`

## 6. Atmospheric Scattering 的接线方式已经脱离 HDRP 原始结构

### 当前状态

- HDRP 在 `PhysicallyBasedSkyRenderer` 内部生成 `AtmosphericScatteringLUT`，并通过 global buffer / global texture 暴露给后续 fog 路径。
- VividRP 现在也改成由 `PhysicallyBasedSkyRenderer` 自己持有 `AtmosphericScatteringLUT`，再通过 `VividSkyData.atmosphericScatteringLutHandle` 传给 `AtmosphericScatteringPass`。

### 当前 workaround / 偏差

- `AtmosphericScatteringPass.Prepare(...)` 现在从 `frameData` 读取 `VividSkyData.atmosphericScatteringLutHandle` 再 `ImportTextureForPass(...)`。
- pass 内保留了 `m_FallbackAtmosphericScatteringLut` 作为黑色 1x1x1 兜底资源。
- 这和 HDRP “renderer 统一生成并全局绑定 atmospheric scattering” 的结构仍不同，意味着渲染链上依旧保留了一层 frame-context handle 传递。

### 风险信号

- Frame Debugger 中 `AtmosphericScatteringPass` 前后完全无变化。
- LUT 明明生成了，但 pass 仍落到了 fallback texture。
- `SkyFogParams` 有效，但 `m_AtmosphericScatteringLutHandle` 为 null 或未导入。

### 关键文件

- `Runtime/SubSystem/Sky/PhysicallyBasedSky/PhysicallyBasedSkyRenderer.cs`
- `Runtime/RenderGraph/FrameContext/VividSkyData.cs`
- `Runtime/RenderPass/Core/Sky/AtmosphericScatteringPass.cs`
- `Shaders/Core/Private/AtmosphericScattering/AtmosphericScattering.hlsl`
- HDRP 对照：
  - `Runtime/Sky/PhysicallyBasedSky/PhysicallyBasedSkyRenderer.cs`
  - `Runtime/Lighting/AtmosphericScattering/AtmosphericScattering.hlsl`

## 7. 已刻意剥离的 HDRP 依赖

### 当前状态

- 为了让 MVP 先落地，VividRP 已经明确移除了几类 HDRP 依赖。

### 当前 workaround / 偏差

- `SkyLUTGenerator.compute` 当前直接循环 `_CelestialBodyDatas`，不再依赖 HDRP 的 `HDShadowContext`、`_DirectionalLightDatas`、cookie sampling、volumetric cloud shadows。
- 旧 copied HDRP fullscreen render chain 已移除，保留的是 Vivid 自己的单 shader + bridge 路径。
- 这让当前 PBRSky 更适合接入 Vivid 现有架构，但也意味着后续如果要补云影、cookie、复杂天体 shading，需要重新审查 compute 和 shading 的数据来源。

### 风险信号

- 某些 HDRP 示例场景的日盘 / 云影 / cookie 表现无法一比一复现。
- 某些“看似只是调参数”的改动，实际需要先补回依赖链。

### 关键文件

- `Shaders/Core/Private/Sky/SkyLUTGenerator.compute`
- `Shaders/Core/Private/Sky/CelestialBodyData.hlsl`
- `Tests/Editor/PhysicallyBasedSkyHdrpPortTests.cs`

## 8. HDRP 里已有、但 Vivid 还没带过来的保护措施

### 当前状态

- 有些保护逻辑在 HDRP 里已经是实战 workaround，但 Vivid 当前还没有同步带过来。

### 当前未对齐项

- HDRP 在 `PhysicallyBasedSkyRenderer.PrecomputationData` 里有针对 `AtmosphericScatteringBlur` 的显卡 workaround，用于规避 `RwTex3D` 在部分设备上输出异常。
- Vivid 当前只要 `AtmosphericScatteringBlur` kernel 存在，就会继续 dispatch blur，没有设备黑名单或格式保护。
- HDRP 的 precomputation hash 显式纳入 `hdCamera.planet.renderingSpace`；Vivid 现在还没有同等级的 rendering-space 维度。
- HDRP 明确标注 `TODO: include fog & scattering in cubemaps`；Vivid 当前 cubemap / ambient probe 仍是与 fullscreen atmospheric scattering 分离的路径，后续继续对齐时要特别注意不要把这个差异误判成 shader bug。

### 风险信号

- 某些 GPU 上 atmospheric scattering LUT blur 后异常，但开发机上无法稳定复现。
- cubemap 看起来没问题，只有屏幕 atmospheric scattering 或反过来不一致。

### 关键文件

- Vivid:
  - `Runtime/SubSystem/Sky/PhysicallyBasedSky/PhysicallyBasedSkyAtmosphereLutCache.cs`
  - `Runtime/SubSystem/Sky/PhysicallyBasedSky/PhysicallyBasedSkyRenderer.cs`
- HDRP:
  - `E:/hdrp17.5/Packages/com.unity.render-pipelines.high-definition/Runtime/Sky/PhysicallyBasedSky/PhysicallyBasedSkyRenderer.cs`

## 回归排查建议

如果后续继续移植时出现回归，优先按下面顺序定位：

1. 先判断问题属于哪条路径：
   - fullscreen sky
   - runtime cubemap / ambient probe
   - `AtmosphericScatteringPass`
   - celestial body / sun disk
2. 如果只有屏幕天空异常，先检查 `PhysicallyBasedSkyBridge.hlsl` 和 `PhysicallyBasedSky.shader`，不要先怀疑 cubemap baking。
3. 如果 cubemap 和屏幕不一致，先检查 `SkyManager.Update(...)`、`PhysicallyBasedSkyAtmosphereLutCache`、`TryPrepareLocalSkyPrecomputation(...)` 和 `TryCopyRuntimeCubemapToAmbientProbe(...)`。
4. 如果太阳、月亮、flare、light count 异常，先检查 `PhysicallyBasedSkyCelestialBodyUtility` 是否走到了 fallback approximate chain。
5. 如果大气散射 pass 前后无变化，先检查：
   - `VividSkyData.atmosphericScatteringLutHandle`
   - `AtmosphericScatteringPass` 是否导入到了真实 LUT
   - `AtmosphericScattering.hlsl` 的 `isSky / tFrag` 分支
6. 如果问题和世界原点、planet center、相机高度相关，优先审查 `ResolveCameraPosition(...)` 和 `planetCenter = (0, -planetRadius, 0)` 这条 MVP 坐标模型。

## 相关文档

- `Documentation~/SkySystemRoadmap.md`
