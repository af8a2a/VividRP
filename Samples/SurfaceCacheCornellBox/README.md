# Cornell Box 局部 Surface Cache 卡片

打开 `SurfaceCacheCornellBox.unity`，使用包含 `SdfDiffuseGatherPass` 的 VividRP Render Graph。场景中的相机和全局 Volume 使用第 30 层。

在 `Cornell GI Volume` 的 Profile 中切换 `SDF Diffuse GI` 的 `Surface Cache`。

- `A - Six Axis Cards` 使用常规六面卡片。外层橙色板挡住了同一网格的内层青色板在 `-Z` 卡片中的投影。
- `B - Inner Local Card` 在六面卡片之外增加一张 `-Z` 局部卡片。裁切范围仅覆盖内层右半边。场景默认使用 B。

固定射线验证比较外层、局部卡片内的内层、裁切范围外的内层以及反向命中。B 只补回局部卡片范围内的内层命中。
