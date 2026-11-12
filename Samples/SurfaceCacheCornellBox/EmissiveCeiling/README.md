# 顶部纯自发光 Cornell Box

场景：`CornellBoxEmissiveCeiling.unity`。通过菜单 **VividRP > Samples > Open Emissive Ceiling Cornell Box** 打开。使用含 `SdfDiffuseGatherPass` 的 Render Graph；当前项目的 `Sponza Noon Realtime GI` 已接通。

## 场景与光源

- 盒内尺寸约 4 × 3 × 4 m，左红墙、右绿墙、米白色地面与顶面；内部放置两个不同高度、旋转角度的白色遮挡块。
- 顶部发光面片为 1.6 × 1.2 m，中心位于 `(0, 2.965, 1.85)`，法线朝下，背面剔除。它是实际 Meshlet 几何，没有附加 Light。
- 白色出射辐亮度为线性 RGB `(200, 200, 200)`。`Ceiling Emitter.mat` 与 `Ceiling Emitter Proxy.asset` 保持相同发光语义；Inspector 的 HDR 颜色存储值经过 gamma 编码，不应直接用存储数值当作线性辐亮度。
- `Emitter White.asset` 是持久化的 1 × 1 白色发光贴图，避免内置 `Texture2D.whiteTexture` 在材质保存、重导入后引用丢失。
- 场景没有方向光、点光或区域 Light；天空、环境光和环境反射关闭。`Realtime Only` 抑制 APV / SH 漫反射回退，因此可单独验证自发光的间接照明。

## GI 基准

使用独立的 `Emissive Ceiling Profile.asset` 与 `Emissive Ceiling Surface Cache.asset`，固定 EV100 = 4。保持原生 Screen Probe Gather、16 px 网格、每探针 8 根光线，以及 Hybrid 追踪、Adaptive 5×5 探针滤波、空间 anchor、方向重要性引导、32 帧像素历史和两轮 À-Trous。启用接收面自相交修正。

Surface Cache 使用 64 × 64 卡片，覆盖房间、遮挡块和顶部面片。Radiosity 使用 8 阶额外反弹，每阶每批 64 样本，累计 32 批；小场景每帧可更新完整卡片扫描。等待约 256 帧以上以收敛缓存和探针历史。

顶部面片使用独立的 ThinShell Mesh SDF，体素 0.0125 m、总厚度 0.025 m，尺寸 139 × 11 × 107。墙面和两个方块复用原 Cornell Box 的立方体 Meshlet、Mesh SDF 与材质。

## 预览与验证

场景、相机和全局 Volume 使用第 29 层。打开菜单保留原场景的未保存内容，并临时关闭其他启用的 Game Camera，避免不同 Surface Cache 身份交替渲染而重置 GI 历史。切换到其他活动场景时关闭本实验相机并恢复其他相机；脚本重载后若本场景仍处于活动状态，会重新隔离预览。独立打开该场景时不需要其他 Cornell 场景。

创建菜单 **Create Emissive Ceiling Cornell Box** 调用 `Editor/Tools/EmissiveCornellBoxSceneBuilder.cs`，仅在目标场景尚不存在时生成资产，拒绝覆盖已有实验。

运行时验证脚本：`Tools~/Diagnostics/EmissiveCornellBoxCapture.cs`。它在同一机位执行开灯、关灯、恢复三个阶段，输出完整 GI 半精度纹理、最终帧截图和源项状态，并在退出时恢复临时改动。输出目录为 `E:/VividRP_Reborn/Logs/EmissiveCornellBox-20261112`。

```powershell
unity command script eval --project-path E:/VividRP_Reborn --file E:/VividRP_Reborn/Packages/VividRP/Tools~/Diagnostics/EmissiveCornellBoxCapture.cs --timeout 1400 --format json
```

这个场景用于暴露纯自发光 GI 的收敛、色溢、软阴影与残余噪声，不包含烘焙 Lightmap 或隐藏的解析区域光补光。

2026-11-12 的 D3D12 实测完成 992 帧开灯 / 关灯 / 恢复对照：开灯与恢复均达到 8 阶、32 批；方向光 RGB 和天空追踪距离均为零。关闭自发光后，GI RGB 的均值和最大值均为零；恢复后的全图 GI 平均亮度与关闭前相差约 −0.16%。GPU 输出均为有限值，无新增运行错误。画面仍有低频斑块，保留为后续 GI 采样与重建优化基准。
