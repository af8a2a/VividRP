# Cornell Box 局部 Surface Cache 卡片

打开 `SurfaceCacheCornellBox.unity`，使用包含 `SdfDiffuseGatherPass` 的 VividRP Render Graph。场景中的相机和全局 Volume 使用第 30 层。

在 `Cornell GI Volume` 的 Profile 中切换 `SDF Diffuse GI` 的 `Surface Cache`。

- `A - Six Axis Cards` 使用常规六面卡片。外层橙色板挡住了同一网格的内层青色板在 `-Z` 卡片中的投影。
- `B - Inner Local Card` 在六面卡片之外增加一张 `-Z` 局部卡片。裁切范围仅覆盖内层右半边。场景默认使用 B。
- `C - Full Inner Local Card` 将同一张 `-Z` 局部卡片扩展到内层的整个正面，沿用 B 的分辨率和深度范围。可用它对比卡片覆盖，以及缓存额外反弹阶数 0、1、2。

固定射线验证比较外层、局部卡片内的内层、裁切范围外的内层以及反向命中。B 只补回局部卡片范围内的内层命中。对比 B 与 C 时，保持机位、射线序列和追踪模式一致，并分别统计补回的着色命中和仍被拒绝的命中。

## 高亮定向光对照

单独打开 `SurfaceCacheCornellBoxBright.unity` 运行高亮对照。它与原场景共用相机配置、材质、Volume Profile 和 Surface Cache，`Cornell Directional` 的强度从 900 lx 提到 3,600 lx。

比较间接光稳定性时固定 EV 8 和相机轨迹。读取 Deferred 使用的实时漫反射 GI，按覆盖率加权，再把高亮组辐亮度除以 4。用归一化后的结果比较静止噪点、板边帧间变化和新露出区域。固定机位从 900 lx 切到 3,600 lx 再切回，可单独测量光强变化后的收敛时间。
