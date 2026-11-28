# FurSlab DCC 资产制作与 Houdini 烘焙规范（Phase D1 / D2）

返回索引：[Documentation~ 索引](../README.md) · 相关实现：[FurSlab.slab.shader](../../Shaders/Material/Fur/FurSlab.slab.shader) · [VividFurSurface.hlsl](../../Shaders/Core/Public/VividFurSurface.hlsl) · [Bake-VividFurPatterns.py](../../Tools~/Houdini/Bake-VividFurPatterns.py)

本指南定义 VividRP `VividRP/Material/FurSlab` 在 **Raster** 与 **GPUDriven（Bindless / DesktopBCn Streamed Virtual Texture）** 双路径下使用的 DCC 毛发资产通道契约、Houdini 22 烘焙管线与 Unity 导入/SVT 绑定规范。

---

## 1. 支持的三种 Pattern 模式与 FlowMap 叠加

`FurSlab` 采用 **“几何壳层（Shells + Hybrid Slab Raymarch）+ 可选 DCC 梳理流向（FlowMap）+ 可选 Pattern 密度/高度场”** 的组合架构：

| 模式 / 开关 | Shader 属性 | 适用资产类型 | 典型分辨率 |
| --- | --- | --- | --- |
| **Mode 0: Procedural Lattice** | `_FurPatternMode = 0` | 常规绒毛、动物短/中毛、无需额外贴图的程序化毛发 | 仅需可选 `_FurMaskMap` / `_FurFlowMap` |
| **Mode 1: 2D Pattern Map** | `_FurPatternMode = 1` | 编织毛毯、针织菱格、规则绒面、簇状长毛地毯 | `512×512` 或 `1024×1024`（线性 RGBA） |
| **Mode 2: 3D Flipbook Atlas** | `_FurPatternMode = 2` | 3D 螺旋卷曲羊羔毛（Curly Wool）、纠缠毛毡、分层羽毛/绒球 | `1024×1024`（`4×4` = 16 高度切片，每格 `256²`）或 `512×512`（每格 `128²`） |
| **FlowMap（可与上述任意模式叠加）** | `_UseFlowMap = 1` | 动物毛流旋涡（Whorl）、分缝（Parting）、局部趴伏度与剪裁长度 | `512×512` 或 `1024×1024`（线性 RGBA） |

---

## 2. 贴图通道编码契约（必须为线性 Linear）

所有 DCC 毛发贴图在 GPUDriven 中均按 `VividSlabTextureSampleKind.Mask` 反射（见 `VividSlabShaderAssembler.cs`），**禁止启用 sRGB**。

### 2.1 `_FurFlowMap`（Grooming FlowMap）

在 `VividDecodeFurFlowMapSample`（[VividFurSurface.hlsl](../../Shaders/Core/Public/VividFurSurface.hlsl)）中按如下语义解码：

| 通道 | 编码范围 | 解码语义 | 默认值（中性） |
| --- | --- | --- | --- |
| **R** | `[0, 1]` | 切线空间梳理方向 `T` 分量：`flowTS.x = R * 2 - 1` | `0.5` |
| **G** | `[0, 1]` | 切线空间梳理方向 `B` 分量：`flowTS.y = G * 2 - 1`（开启 `_FlowMapFlipY` 时取反） | `0.5` |
| **B** | `[0, 1]` | 局部蓬松/立起度覆盖 `loftOverride`（`0` = 贴皮趴伏，`1` = 垂直立起） | `0.5` |
| **A** | `[0, 1]` | 局部毛长倍率 `lengthScale`（按 `_FlowMapStrength` 与 `1.0` 插值，并与 `_FurMaskMap.r` 相乘） | `1.0` |

> **坐标系提示**：若烘焙源使用 OpenGL 切线约定（V 向上为正）而目标网格副切线向下，可在材质中勾选 `Flip FlowMap Y`（`_FlowMapFlipY = 1`），无需重新烘焙。

### 2.2 `_FurPatternMap` — Mode 1（2D Pattern Map）

在 `VividEvaluateFur2DPatternSample` 中按如下语义求值：

| 通道 | 编码范围 | 语义 | 说明 |
| --- | --- | --- | --- |
| **R** | `[0, 1]` | 毛发高度场 `maxHeight` | 乘以 `densityMask` 后与当前壳层高度 `h` 对比；通过 `_PatternHeightContrast` 控制软过渡带宽 |
| **G** | `[0, 1]` | 切线空间微法线 / 毛丝中心偏移 `X` | 解码为 `[-1, 1]`，驱动 Marschner/OpenPBR 局部毛丝法线与切线微扰 |
| **B** | `[0, 1]` | 切线空间微法线 / 毛丝中心偏移 `Y` | 解码为 `[-1, 1]` |
| **A** | `[0, 1]` | 逐毛丝随机变化 ID `strandVariation` | 驱动 `_StrandColorVariance` 与 `_StrandTangentJitter` |

### 2.3 `_FurPatternMap` — Mode 2（3D Flipbook Atlas）

在 `VividFurComputeFlipbookSliceUV` 与 `VividEvaluateFurFlipbookSample` 中按如下布局采样：

- **图集排列**：行主序（Row-Major），由 `_PatternFlipbookGrid = (cols, rows, 0, 0)` 指定（推荐 `4×4 = 16` 层）：
  - `sliceIndex = 0` 对应根部 `h = 0.0`；`sliceIndex = cols * rows - 1` 对应毛尖 `h = 1.0`。
  - `col = sliceIndex % cols`，`row = sliceIndex / cols`（纹理 V 坐标从下到上递增，即 `row = 0` 位于 Unity 纹理底部，与 PNG 自底向上导入后 `v = 0` 一致）。
- **防跨格渗色边距**：Shader 内部将单元局部 UV 钳制在 `[0.004, 0.996]`，并显式传递解析导数 `ddx/ddy = uv0Ddx * patternTiling / grid`，杜绝 Mipmap 接缝与边缘黑线。建议烘焙时在每个 tile 内部按周期性无缝平铺（periodic wrap）生成内容。
- **通道布局**：

| 通道 | 编码范围 | 语义 |
| --- | --- | --- |
| **R** | `[0, 1]` | 当前高度切片的 3D 毛丝覆盖率 / Alpha（相邻两层按 `frac(sliceF)` 线性混合） |
| **G** | `[0, 1]` | 当前切片的局部 3D 螺旋/扭转切线偏移 `X`（`[-1, 1]`） |
| **B** | `[0, 1]` | 当前切片的局部 3D 螺旋/扭转切线偏移 `Y`（`[-1, 1]`） |
| **A** | `[0, 1]` | 逐毛丝确定性 ID `strandVariation`（用于颜色与高光微抖动） |

---

## 3. Houdini 22 自动化烘焙脚本与节点流

仓库提供了零外部依赖的 Houdini 22 `hython` 烘焙脚本：[Tools~/Houdini/Bake-VividFurPatterns.py](../../Tools~/Houdini/Bake-VividFurPatterns.py)。

### 3.1 一键命令行烘焙

在具备有效 Houdini 22 许可（Commercial / Indie / 未过期 Apprentice）的机器上运行：

```powershell
& "E:\Houdini 22.0.368\bin\hython.exe" `
    Tools~/Houdini/Bake-VividFurPatterns.py `
    --out "Editor/SceneTemplates/Outdoors/Textures/Fur" `
    --save-hip "Tools~/Houdini/VividFur_PatternBaker.hipnc" `
    --resolution 512 `
    --flipbook-resolution 1024 `
    --grid 4 4
```

脚本会在 Houdini 内自动构建 `/obj/vivid_fur_baker` SOP 网络并导出：
1. `VividFur_Flow_Swirl.png`：无缝周期性切线空间旋涡 FlowMap（含 `loft` 与 `lengthScale`）。
2. `VividFur_Pattern2D_BraidedPlush.png`：基于 3D 编织/针织绒线曲线的 2D Pattern Map。
3. `VividFur_Flipbook4x4_CurlyWool.png`：基于 Houdini SOP 真实 3D 螺旋卷曲毛发曲线（带根到尖半径锥缩与 3D 切线扭转）切分并打包的 `4×4` 16 层 3D Flipbook 图集。
4. `VividFur_PatternBaker.hipnc`（或商业许可下的 `.hip`）：可直接在 Houdini 22 GUI 中打开、替换为美术 `guidegroom` / `hairgen` 曲线并重新烘焙的工程文件。

### 3.2 在 Houdini 22 GUI 中接入自定义 Groom

若需将自己梳理的 `guidegroom` / `hairgen` 资产烘焙为 VividRP 3D Flipbook：
1. 将可无缝平铺的毛发曲线放置在 `[0, 1] × [0, 1]` 的 `XZ`（或 `UV`）周期方片上，毛发高度归一化至 `Y ∈ [0, 1]`，并在每根曲线上保留 `@strand_id`（`[0, 1]`）与 `@width`（半径）属性。
2. 在 SOP 中通过 `attribwrangle` 或 Copernicus `rasterizegeo` 按 `slice = 0 .. (cols * rows - 1)` 求每层水平切面 `Y = slice / (totalSlices - 1)` 处到所有周期平移（3×3 邻域）曲线段的最近距离，输出：
   - `R = smoothstep(radius, radius * 0.55, dist)`
   - `GB = clamp(tangentXZ * 0.65 + radialDirXZ * 0.35, -1, 1) * 0.5 + 0.5`
   - `A = strand_id`
3. 按 `row = 0` 在图像底部（`v = 0`）的顺序拼合为 `cols × rows` 图集，以 **Linear RGBA PNG（无色彩空间变换、无 Alpha 预乘）** 导出。

### 3.3 离线契约自检（无需 Houdini 许可）

可通过 Houdini 自带的 Python 3.13 运行纯契约测试，验证 PNG 编码、行序（bottom-up UV 对应）与 `VividFurComputeFlipbookSliceUV` 索引一致性：

```powershell
& "E:\Houdini 22.0.368\python313\python.exe" `
    Tools~/Houdini/Bake-VividFurPatterns.py --self-test
```

---

## 4. Unity 导入与 GPUDriven SVT 配置

### 4.1 TextureImporter 设置（Raster 与 Bindless）

| 选项 | `_FurFlowMap` | `_FurPatternMap`（Mode 1） | `_FurPatternMap`（Mode 2 Flipbook） |
| --- | --- | --- | --- |
| **sRGB (Color Texture)** | `Off`（必须 Linear） | `Off`（必须 Linear） | `Off`（必须 Linear） |
| **Wrap Mode** | `Repeat` | `Repeat` | `Clamp`（Shader 内按单格 `frac` 平铺） |
| **Generate Mip Maps** | `On` | `On` | `On`（Shader 使用解析 `SampleGrad`） |
| **Compression** | `High Quality (BC7)` 或 `None` | `High Quality (BC7)` 或 `None` | `High Quality (BC7)` 或 `None` |
| **Alpha Is Transparency** | `Off` | `Off` | `Off` |

### 4.2 GPUDriven VirtualTexture（DesktopBCn SVT）绑定

当 `VividRenderPipelineAsset.GPUDrivenTextureBackend == VirtualTexture` 时，运行时禁用裸 `Texture2D` 实时压页；必须为自定义线性贴图烘焙 `PackedRGBA`（BC7_UNorm）格式 `.vividvt` 并在 `GPUDrivenMaterialProxy` 上绑定对应无下划线符号名：

```csharp
// 符号名不带前导下划线，与 VividSlabProgramDescriptor.Resources[].Symbol 严格一致
materialProxy.SetVirtualTextureOverride("FurFlowMap", flowSvtAsset, new Vector4(1f, 1f, 0f, 0f));
materialProxy.SetVirtualTextureOverride("FurPatternMap", patternSvtAsset, new Vector4(1f, 1f, 0f, 0f));
```

- 烘焙 `.vividvt` 时需指定 `BuildProfile = GPUDrivenSurface`、`StorageProfile = DesktopBCn`、`MaskStorage = PackedRGBA`（保留全部 4 通道 RGBA，勿使用 `SingleChannelR` BC4）。
- Mode 2 Flipbook 的 SVT 应使用 `AddressMode = Clamp`；FlowMap 与 Mode 1 2D Pattern 使用 `AddressMode = Repeat`。
