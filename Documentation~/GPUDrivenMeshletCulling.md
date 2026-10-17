# 保守 Meshlet 视锥、法线锥与 HZB 裁剪 (`GPUMeshletCulling.compute`)

返回索引：[Documentation~ 索引](README.md) · 相关文档：[保守实例视锥裁剪](GPUDrivenInstanceCulling.md) | [Cluster LOD 生成与选择](GPUDrivenClusterLOD.md)

## 算法概述

`GPUMeshletCulling.compute` 与实例裁剪共享同一套**仿射包围盒（Affine-Box）平面支撑谓词**：

- **默认包围盒**：由于 Meshlet 元数据提供局部包围球，默认构建其外接局部立方体进行仿射变换。
- **VSM 紧致包围盒变体**：VSM 几何边界变体直接使用更紧致的辅助局部 AABB。
- **精确错切/非均匀缩放**：两条路径均不采用“最大变换轴半径”近似错切实例；现有的 GPU 缓冲区布局、绘制计数器、材质朝向约定、VSM 页查询以及双阶段遮挡剔除（First-Pass / Recovery Queue）契约保持不变。

### 参考实现（Unreal Engine Nanite）

本实现将 Nanite 的几何裁剪准则适配到 VividRP 的双阶段 HZB 遮挡管线中，同时保留 meshoptimizer 特有的局部空间法线锥（Normal Cone）裁剪阶段：

- `NaniteClusterCulling.usf`：`FBoxCull` 与 Cluster `FrustumHZB`；
- `NaniteCullingCommon.ush`：上一帧/当前帧视图 HZB 与近裁面保护；
- `NaniteHZBCull.ush`：投影包围盒矩形计算与 Reversed-Z 深度包含性测试。

---

## 三大裁剪阶段与精度规则

| 裁剪阶段 | 精度与保守性规则 |
| --- | --- |
| **1. 视锥平面支撑裁剪 (Frustum Support)** | • 对归一化半轴与有界平面法线使用严格的 `float16_t` / `float16_t3` 算术，并附加共享的 `1/32` 向外误差余量<br>• 世界坐标、矩阵变换、平面距离与偏移保持 `FP32` |
| **2. 局部空间法线锥背面裁剪 (Normal Cone)** | • 在非均匀缩放、错切与镜像反射下，法线锥与视线向量始终保持在**模型空间（Object Space）**计算（镜像绕序已由光栅化 RendererList 桶选择补偿，因此直接使用材质原始剔除面设置，双面材质跳过法线锥剔除）<br>• 透视测试采用 meshoptimizer 的球-锥充分条件，并针对相机变换误差做外扩保护<br>• 仅归一化视线方向与锥轴的点积使用 `FP16`，并附加 `1/128` 绝对余量覆盖半精度转换/运算与非规格化数刷零；退化/非法法线锥及边界情况一律保留可见 |
| **3. 双阶段 HZB 遮挡剔除 (HZB Occlusion)** | • 全程采用 `FP32` 投影与深度计算；固定遵循 **Reversed-Z** 与 D3D/Vulkan 裁剪深度区间 `[0, W]`（深度金字塔以 `min` 归约，Padding 清除深度为 `0`，深度相等判为可见）<br>• 投影矩形包围仿射盒全部 8 个角点，考虑模型/视图平移相消前的幅值误差，向可见方向扩展矩形与最近深度，并额外增加 **2 像素保护边距（Guard Band）**<br>• 触碰裁剪深度边界、跨越 $W = 0$ 或超出历史视口边界的查询一律保留可见（超出上一帧视口边缘不可安全 Clamp 到边界深度）<br>• Mip 选择保证最多 4 次采样覆盖矩形；缺失 Mip 或奇数尺寸截断尾部均保守保留几何 |

---

## 独立验证 (`Validate-MeshletCulling.ps1`)

在包根目录下执行：

```powershell
./Tools~/Validate-MeshletCulling.ps1
./Tools~/Validate-InstanceFrustumCulling.ps1
```

- **验证范围**：
  1. 编译全部 12 个生产 Meshlet 裁剪 Kernel（含 VSM 变体与深度归约），静态检查 DXIL 原生 `f16` 点积指令并在 D3D12 上直接运行共享谓词（输出默认位于 `Temp~/meshlet-culling`）；
  2. 对比 **24,640 个确定性用例**的 GPU 回读结果与独立 Python 双精度（FP64）8 角点投影及角度锥参考基准（覆盖全部视锥面、错切/镜像、极端缩放、大平移相消、透视/正交、近平面相交、历史视口边界、相等/Clear/非法深度、奇数 Mip 尾部与非法元数据）；
  3. 同时编译 6 个 LOD 构建 Kernel 并校验 **16,448 个父子 LOD 选择用例**（详见 [Cluster LOD 文档](GPUDrivenClusterLOD.md)）。
- **验证结果**：在 RTX 5070 Ti 上，原生 FP16 与诊断 FP32 变体均实现**零错误剔除（Zero False Frustum / Cone / Occlusion Rejections）**与**零投影矩形/深度包含失败**。
