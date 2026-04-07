# Sky System Roadmap

## 目标

- 以 HDRP 天空系统为参考，优先补齐会直接影响画面稳定性、光照一致性、工作流和可扩展性的能力。
- 不追求逐项复刻 HDRP；只引入对 VividRP 当前架构和项目目标有明确收益的功能。
- CPU 天空卷积不再进入路线图。天空漫反射和镜面反射卷积统一维持 GPU-only，实现上不再恢复 CPU SH 投影路径。

## 当前实现现状

### 已完成

- 天空类型切换与更新模式已经具备基础框架：
  - `SkySettingsVolume` 支持 `SkyType` 和 `SkyUpdateMode`。
  - `SkyManager` 负责按 hash、更新模式和更新时间统一驱动天空刷新。
- 已实现两类天空：
  - `HDRISkyVolume` / `HDRISkyRenderer`
  - `PhysicallyBasedSkyVolume` / `PhysicallyBasedSkyRenderer`
- 物理大气天空已经具备基础散射链路：
  - `AtmosphereLUTPass` 生成 `TransmittanceLUT`、`MultiScatteringLUT`、`SkyViewLUT`
  - `PhysicallyBasedSkyPass` 用 LUT 绘制屏幕天空
  - `PhysicallyBasedSkyRenderer` 用 compute 生成运行时天空 cubemap
  - `AerialPerspectivePass` 负责高度雾 / aerial perspective 合成
- 天空光照已经具备 GPU-only 的基础能力：
  - `SkyAmbientProbeConvolution` 用 compute 生成环境探针缓冲
  - `SkySpecularCache` 已切到 GPU GGX prefilter
  - `DeferredLightingPass` 已从 `SkyManager` 导入天空 IBL cubemap
- 最近已修复两类稳定性问题：
  - 物理天空过亮导致 NaN，已通过 pre/post exposure 拆分和 radiance clamp 暂时稳定
  - 物理天空太阳位置和日盘瑕疵问题已修复，当前屏幕太阳盘改为逐像素解析叠加

### 当前明确边界

- `SkyDiffuseSHUtility` 已移除 CPU 投影入口，天空系统当前统一走 GPU-only 漫反射/镜面反射链路，不再保留 `VividSkyData` 中的 CPU SH 兼容字段。
- 物理天空已经支持基础参数：
  - 地表半径
  - 空气 / 气溶胶 / 臭氧密度
  - 地面色
  - 太阳盘开关与尺寸
  - 一个临时的天空曝光参数
  - 简化高度雾
- HDRI 天空目前仅支持：
  - cubemap
  - tint
  - exposure
  - rotation

### 当前短板

- 自动曝光基础链路已经落地，但天空系统仍需继续验证“屏幕实时曝光”和“sky baking 固定曝光”是否完全解耦。
- `skyData.exposure` 目前同时被当作天空强度和 IBL 强度使用，没有和相机曝光解耦。
- `AtmosphereLUTPass` 已经拆成按参数 hash 重建的缓存链路，但 `SkyViewLUT` 仍只停留在基础 hash 缓存，尚未进入分层缓存 / 重投影策略。
- runtime sky cubemap 和 specular prefilter 已支持基础分辨率配置；specular prefilter 进一步补上了质量档和 rebuild profiling，但 runtime cubemap 侧仍缺更细的采样质量控制。
- 物理天空还只是 HDRP Physically Based Sky 的一个基础子集。
- HDRI 天空还只是 HDRP HDRI Sky 的一个基础子集。
- 还没有云层系统，也没有把云参与天空反射、环境探针和雾的统一调度。
- 还没有自定义天空扩展接口。

## 对照 HDRP 的关键差距

| 方向 | HDRP | VividRP 当前 | 结论 |
| --- | --- | --- | --- |
| 视觉天空选择 | `Visual Environment` 管理 Sky Type，支持多种天空类型和 Volume 工作流 | `SkySettingsVolume` 已支持 `SkyType` 和基础更新模式 | 现有框架保留，继续扩展天空类型而不是重写 |
| 环境光照来源 | 区分视觉天空与环境光照；支持 `Ambient Mode`、`Static Lighting Sky`、`Lighting Override Mask` | 视觉天空和环境光照仍绑定在同一条 `SkyManager -> skyData` 链路上 | 这是下一阶段的核心架构差距 |
| HDRI Sky 能力 | 支持 `Intensity Mode`、`Lux`、失真、Backplate、独立环境更新 | 当前只有 cubemap / tint / exposure / rotation | HDRI 功能仍停留在最小可用集 |
| Physically Based Sky 能力 | 支持 `Spherical Mode`、`Planet Center Position`、`Sea Level`、`Planet Rotation`、地表纹理/发光、Space 贴图、Artistic Overrides、`Number Of Bounces`、`Include Sun In Baking` | 当前只实现基础散射参数、地面色、太阳盘、简化高度雾 | 物理天空需要从“能用”补到“可控且可维护” |
| 预计算策略 | 文档明确强调预处理与场景无关，初始化后缓存，参数变化时重建 | LUT 仍是逐帧计算，runtime cubemap 只有 hash 控制 | 需要先做参数缓存，再谈质量升级 |
| 曝光系统 | 独立的 `Exposure` Volume，支持 `Fixed`、`Automatic`、`Curve Mapping`、`Use Physical Camera` | 已具备自动曝光与独立 `Exposure` Volume，但天空实时渲染、IBL 与 sky baking 的曝光职责仍需继续收敛 | 下一步重点转为验证和收口曝光职责边界 |
| 云系统 | 同时提供 `Cloud Layer` 和 `Volumetric Clouds`，可与天空一起使用 | 没有云 | 云应作为独立阶段，不要和基础天空修补混在一起 |
| 自定义天空扩展 | 有 `SkySettings` / `SkyRenderer` 扩展点，自定义天空会自动出现在 Sky Type 中 | 当前天空类型是硬编码注册 | 需要补扩展接口，但优先级低于曝光和缓存 |

## 继续优化的路线图

## Phase 0: 曝光链路先补齐

### 目标

- 先解决“天空数值正确但显示错误”的问题，把天空强度、环境光强度和相机曝光彻底拆开。

### 任务

- 新增独立的 `Exposure` Volume，而不是继续复用天空参数做曝光补丁。
- 第一阶段至少实现：
  - `Fixed`
  - `Automatic`
  - `Compensation`
  - `Limit Min / Limit Max`
  - `Speed Dark to Light / Speed Light to Dark`
- 增加亮度统计 compute pass：
  - 先用 log luminance histogram 或降采样 average luminance
  - 输出统一的 `cameraExposureMultiplier` 或 EV100
- 解耦当前数据模型：
  - `skyData.exposure` 不再同时承担天空强度和相机曝光语义
  - 天空渲染、IBL 强度、最终曝光分别拥有独立字段
- 保留当前物理天空 pre/post exposure 逻辑作为过渡保护层，等自动曝光稳定后再决定是否收缩或移除。

### 验收标准

- 天空、雾、IBL、最终 post 看到的是同一套相机曝光结果。
- 物理天空不再依赖“把天空 exposure 调小”来避免 NaN。
- 室内外切换时曝光变化连续，没有明显闪烁。

## Phase 1: 把天空预计算从“逐帧跑”改成“按参数缓存”

### 目标

- 先解决性能和架构问题，再继续补画质。

### 任务

- 为 `AtmosphereLUTPass` 增加基于 sky hash 的缓存键，不再在每帧都重建 LUT。（已完成）
- 把“与场景无关”的大气预计算和“与相机相关”的屏幕绘制分开：
  - `TransmittanceLUT`
  - `MultiScatteringLUT`
  - 物理天空参数缓存（基础版已完成）
- 评估 `SkyViewLUT` 是否继续保留为逐帧资源，还是改成分层缓存和重投影策略。
- 给 runtime cubemap 和 specular prefilter 增加质量级别和分辨率配置，不再写死当前尺寸。（分辨率配置已完成；specular prefilter 质量档已完成，runtime cubemap 质量档仍待补）
- 清理 GPU-only 之后仍遗留的 CPU SH 接口：
  - 审查仍依赖 `RenderSettings.ambientProbe` / SH 常量缓冲的调用点，确认只保留“无天空数据”时的兜底语义
  - 为 GPU-only 路径补更直接的调试与验证手段，而不是再暴露 CPU SH 状态

### 验收标准

- 静态天空参数下，LUT 不再每帧重建。
- 天空 hash 改变时，只重建必要资源。
- Profile 中能明确看到天空预计算成本和触发原因。

### 当前实现进度（2026-04-07）

- 已完成：
  - `AtmosphereLUTPass` 现在会缓存 `TransmittanceLUT`、`MultiScatteringLUT`、`SkyViewLUT`，并区分 `MissingTexture` / `ParametersChanged` 两类重建原因。
  - HDRI / Physically Based Sky 的 runtime cubemap 与 ambient probe cubemap 已支持由 `SkySettingsVolume` 统一控制分辨率。
  - `SkySpecularCache` 已支持独立的 specular prefilter 分辨率、质量档，并对 source cubemap 尺寸做上限约束。
  - `VividSkyData` 与 `ShaderVariablesGlobal` 中的 CPU SH 兼容字段已移除，天空漫反射链路统一收敛到 GPU-only。
  - LUT / runtime cubemap / ambient probe / specular prefilter 的重建路径现在都带有更明确的 profiling 标记与触发原因。
- 仍待完成：
  - 明确 `SkyViewLUT` 是否升级到 layered cache / reprojection。
  - 为 runtime cubemap 增加更细的采样质量档，而不只是分辨率控制。
  - 为 `SkyViewLUT` 的后续策略补结论性验证，而不是只停留在路线图层面的评估。

## Phase 2: 补齐环境光照与视觉天空分离

### 目标

- 对齐 HDRP 在“视觉天空”和“环境光照”上的核心设计，而不是继续把两者耦合在一起。

### 任务

- 为天空系统增加 `Ambient Mode` 语义：
  - `Static`
  - `Dynamic`
- 增加“光照使用的天空”和“屏幕看到的天空”分离能力。
- 设计 VividRP 版的 `Lighting Override Mask` 或等价机制。
- 为反射 probe / ambient probe 增加独立更新控制，而不是只共享一个 `SkyUpdateMode`。
- 加入“是否在环境光照中包含太阳盘”的选项，避免太阳在方向光和天空烘焙中重复计入。

### 验收标准

- 允许屏幕上显示一套天空，同时用另一套天空驱动环境光。
- 太阳不再因为 HDRI / PBR sky 与主方向光重复计入而导致过曝。
- 环境探针和反射探针具备独立刷新策略。

## Phase 3: 把 Physically Based Sky 从基础版补到可控版

### 目标

- 参考 HDRP 的能力范围，把当前物理天空补到足够稳定、足够可调、足够容易扩展。

### 任务

- 补齐行星空间参数：
  - `Spherical Mode`
  - `Planet Center Position`
  - `Sea Level`
  - `Planet Rotation`
- 补齐地表 / 太空外观参数：
  - ground color texture
  - ground emission texture
  - space emission texture
  - 对应 multiplier / tint
- 补齐艺术控制参数：
  - horizon tint
  - zenith tint
  - saturation
  - alpha 控制
- 重新设计多次散射控制：
  - 当前近似先保留
  - 后续评估是否暴露 `Number Of Bounces` 或固定质量档位
- 审查物理天空和高度雾之间的参数耦合，避免把 sky-only 参数硬塞给 fog pass。

### 验收标准

- 物理天空参数能覆盖“地球默认”、“艺术化天空”、“自定义行星”三类用例。
- 太阳高度、地表、太空背景和反射结果保持一致。
- 参数面板结构清晰，不再依赖内部实现细节命名。

## Phase 4: 补 HDRI Sky 的生产力特性

### 目标

- 让 HDRI sky 从“调试天空”变成“可用于场景搭建和灯光工作流”的天空类型。

### 任务

- 补 `Intensity Mode`：
  - Exposure
  - Multiplier
  - Lux
- 评估并实现 HDRI distortion。
- 实现 Backplate 的最小可用子集：
  - Infinite
  - Rectangle 或 Disc 任选一个先落地
- 为 HDRI sky 增加“环境光使用时是否包含太阳”的工作流说明和参数。

### 验收标准

- 灯光师可以不用改贴图内容，仅靠 sky 参数完成强度标定。
- 有明确的“视觉背景”和“环境光照”使用方式。
- HDRI sky 不再只是一个简单 cubemap 采样器。

## Phase 5: 云系统接入

### 目标

- 参考 HDRP 的拆分方式，先远后近，而不是一次性硬上完整体积云。

### 任务

- 先做 `Cloud Layer` 风格的远景云：
  - 只参与天空背景
  - 进入天空 cubemap / 反射 / 环境光工作流
- 再做 `Volumetric Clouds`：
  - 与 fog / aerial perspective / volumetric lighting 对齐
  - 明确与 planet center 的兼容范围
- 设计云和天空更新调度：
  - 哪些变化需要重建 sky cubemap
  - 哪些变化只影响屏幕绘制

### 验收标准

- 云能够进入天空反射和环境光，而不是只在屏幕上可见。
- 云和雾、主方向光、太阳盘的遮挡关系可控。

## Phase 6: 扩展性、调试与测试

### 目标

- 在功能补齐后，把天空系统变成一个可继续迭代的子系统，而不是一组分散 pass。

### 任务

- 设计类似 HDRP `SkySettings` / `SkyRenderer` 的扩展接口，允许注册自定义天空。
- 增加天空调试视图：
  - 当前曝光
  - 当前 sky hash
  - LUT rebuild 原因
  - sky cubemap 来源和分辨率
- 增加 PlayMode 或截图回归测试，覆盖：
  - HDRI sky
  - physically based sky
  - exposure adaptation
  - cloud integration
- 为关键天空 pass 增加 GPU profiling 标记和统计文档。

### 验收标准

- 新天空类型不需要修改 `SkyManager` 的硬编码分支。
- 能快速定位“画面不对”是曝光、天空、IBL、雾还是缓存失效问题。

## 建议的实施顺序

1. `Phase 0`
2. `Phase 1`
3. `Phase 2`
4. `Phase 3`
5. `Phase 4`
6. `Phase 5`
7. `Phase 6`

## 当前阶段建议

- 当前最优先的不是继续补天空参数，而是先收尾 `Phase 1`，并把自动曝光与天空烘焙之间的职责边界验证清楚。
- 如果 sky baking 仍然混入实时曝光，后续 Lux、物理单位和云层接入仍会建立在不稳定的光照基线之上。
- 如果 LUT 和环境光照缓存没有继续收敛到“按需重建”，后续每加一种天空能力，性能和调试成本都会继续上升。

## 非目标

- 不恢复 CPU cubemap 卷积或 CPU SH 投影主路径。
- 不在自动曝光、环境光照分离和预计算缓存完成前，直接进入完整体积云。
- 不为了“参数数量接近 HDRP”而引入没有配套缓存、没有配套测试的表面功能。
