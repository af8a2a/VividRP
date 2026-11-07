# Sponza 正午画面基准

2026-11-06，在 Unity 7000.0.0a16、Direct3D12、RTX 5070 Ti 上通过运行中的 Editor 调整和采样。

经用户确认，当前 `Assets/New Vivid Render Pipeline Asset 1.asset` 已切换到独立正午图；Editor 已打开两份正午场景。首次另存资产并重新加载后，原主视角近黑/近白比例为 0.001% / 0.003%，曝光历史有效；Console 没有新增 error。随后依据 Scene View / Game View 对照修正了主相机构图，结果见下节。

## 独立资产与使用

在当前 VividRenderPipelineAsset 的 `RenderGraphAsset` 中选择：

`Assets/ClassicSponza/Profiles/Sponza Noon Render Graph.vrdg`

同时加载下面两份场景，主场景设为第一份：

- `Assets/ClassicSponza/Scenes/SponzaNoon.unity`
- `Assets/ClassicSponza/Scenes/SponzaLightingNoon.unity`

光照场景的 Scene Settings Volume 使用 `Assets/ClassicSponza/Profiles/Sponza Noon Profile.asset`。当前主相机位置 `(-0.13, -4.11, 10.37)`，Euler `(6.49, 179.83, 0)`，垂直 FOV 60°。

原 Sponza 主场景、白天光照场景、白天 Scene Settings Profile 和 Standard Render Graph 已恢复为本次操作前的原始字节。Unity 保存白天光照场景时会丢弃缺失 HDRP 脚本的遗留字段，因此另存正午副本，避免改写原白天场景。

切换 RenderGraphAsset 会影响共用该管线资产的场景；其他场景的曝光仍由各自 Volume 决定。正午图接入 LocalExposurePass，最终画面绕过原图中未绑定纹理的 OverlayDebugPass；该调试节点仍保留在正午图中。

## Scene View / Game View 构图复查

用户反馈两种视图画面差异后，读取实际相机发现：原 Game 相机 `(0, -4, 8.5)`、俯仰 `-4°`，Scene View 则约为 `(-0.13, -4.11, 10.37)`、俯仰 `+6.49°`。一个向上看，一个向下看，看到的受光地面范围和自动测光样本不同。把 Game 相机移至 Scene View 机位后，Game View 同样出现中庭阳光地带及对应阴影。

当前已将正午主相机保存为上述向下看的机位，并对齐 Scene View。通过真实窗口截图复查，两边光照分布相符。Scene View 窗口比例约 2.24:1、TSR Native AA；Game View 为 16:9、TSR Balanced，因此构图宽度、细节清晰度和独立自动测光结果仍会略有区别，不能要求逐像素一致。

稳定后的 Scene / Game 曝光倍率分别约为 `0.00034264 / 0.00029539`，相差约 0.21 EV；两者曝光历史均有效。新主相机 Game View 近黑/近白比例为 0.001% / 0.000%。截图为输出目录中的 `aligned-scene-screen.png` 和 `aligned-game-screen.png`。本次仅修改正午主相机位置和朝向，没有为此修改渲染算法或曝光参数。

## 参数

| 项目 | 设置 |
| --- | --- |
| 太阳 | Time of Day 启用，12:00，Maximum Lux 110000 |
| 太阳颜色 | 白色、色温启用、6500 K |
| 天空 | 保留现有 Physically Based Sky |
| 自动曝光实现 | HDRP |
| 模式、测光 | Automatic Histogram，Center Weighted |
| 直方图区间 | 20%–85% |
| EV100 限制 | 6–16.5 |
| 曝光补偿、中灰 | +0.3 EV，14% |
| 适应 | Progressive，暗到亮 5，亮到暗 2 |
| Histogram Use Curve Remapping | 启用 |
| 局部曝光 | 启用；高光、阴影对比度均为 0.75 |
| 局部细节、模糊混合、核尺寸 | 1、0.6、50% |
| 局部 Middle Grey Bias | 0 |
| 色调映射 | ACES，Full ACES 启用 |
| Color Adjustments | Post Exposure 0，Contrast 0 |
| White Balance | Temperature +10，Tint 0 |

Histogram Curve Map 的 `(测得 EV100, 目标 EV100)` 关键帧：

```text
(6, 6), (10, 10), (12, 11.8), (14, 13.2), (16.5, 15)
```

对应入/出切线依次为 `1, 1, 0.8, 0.7, 0.72`。高亮场景保留更高的显示亮度，避免大片阳光地面也被压到中灰。

Local Exposure 的 Shadow Contrast Curve 乘以基础 Shadow Contrast Scale，关键帧为：

```text
(6, 1), (11, 1), (14, 0.7), (17, 0.5)
```

入/出切线依次为 `0, 0, -0.15, 0`。暗场景保留对比度，亮场景进一步抬起阴影。

## 实景验证

使用同一 Camera_1 移动采样，120 次 Editor 更新/重绘后采集 1280×720 Game View，并读取该相机的 GPU 曝光历史。各机位均有有效历史，最终采样时当前曝光与目标曝光接近。

| 机位 | 位置 | Euler | 近黑像素 | 近白像素 |
| --- | --- | --- | ---: | ---: |
| 原主视角 | (0, -4, 8.5) | (-4, 180, 0) | 0.001% | 0.003% |
| 中庭 | (0, -4, 5.5) | (-4, 180, 0) | 0.000% | 0.000% |
| 阴影回廊 | (-4, -4, 3) | (0, 180, 0) | 0.001% | 0.000% |
| 受光地面 | (0, -4, 1) | (55, 180, 0) | 0.039% | 0.000% |
| 仰望太阳 | (0, -4, 1) | (-80, 180, 0) | 0.295% | 0.178% |

近黑定义为 PNG 显示编码 RGB 按 `(0.2126, 0.7152, 0.0722)` 加权后低于 8/255；近白为三个通道均大于 250/255。这是显示图像指标，不是场景线性亮度或照度测量。仰望视角的近白区域主要为太阳盘。

同一受光地面机位，首轮固定局部对比度、未使用 Histogram Curve Map 的方案有 25.187% 近黑像素；最终曲线方案降至 0.039%，石材纹理与两侧阴影均可辨认。该比较针对本轮参数迭代，不能代替与其他渲染器的正确性对照。

正午渲染图编译顺序确认包含 `AutoExposurePass → LocalExposurePass → BloomPass`，并最终经过 ACES。复查阶段 Console 无新增 error，未出现 C# 编译失败。未运行 Unity Test Framework：Editor 正处于交互会话。

只验证了上述固定机位和曝光收敛，没有完成 Play Mode 连续漫游或所有角落的覆盖；太阳盘、微小镜面高光和遮蔽缝隙仍可能接近白/黑。屋顶外原场景没有几何的黑色区域不属于曝光可恢复的表面。

截图、曝光采样和 `metrics.json` 存于本次任务输出目录：

`C:/Users/11252/.codex/visualizations/2026/11/06/01a1b0d2-8c18-7171-823c-fd53dbc0b370`
