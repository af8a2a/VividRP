# 保守实例视锥裁剪 (`GPUInstanceCulling.compute`)

返回索引：[Documentation~ 索引](README.md) · 相关文档：[保守 Meshlet 裁剪](GPUDrivenMeshletCulling.md) | [Cluster LOD 生成与选择](GPUDrivenClusterLOD.md) | [PrimitiveScene V1](PrimitiveSceneV1.md)

## 算法概述

`GPUInstanceCulling.compute` 将每个实例局部 AABB 的**仿射变换映像（Affine Box）**直接与 `VividGPUCullingContext` 中的 6 个向内世界空间视锥平面进行测试。

该实现参考了 Unreal Engine Nanite（`NaniteHZBCull.ush` 中的 `BoxCullFrustumOrtho` / `BoxCullFrustumPerspective` 及 `FBoxCull`）的包围盒极值思路由。由于 VividRP 在实例裁剪阶段仅需可见性判定，因此直接采用等价的**平面支撑函数（Plane-Support Predicate）**，无需构建投影屏幕矩形或除以裁剪空间 $W$；HZB 遮挡剔除、VSM 页过滤、Pass Mask 与 LOD Job 发射仍保持各自原有职责。

### 平面支撑公式

给定世界空间中心 $C$ 以及变换后的三个半轴向量 $X, Y, Z$，仿射盒到向内平面 $(N, D)$ 的最大有符号距离为：

```text
dot(N, C) + D + abs(dot(N, X)) + abs(dot(N, Y)) + abs(dot(N, Z))
```

- **保守剔除条件**：仅当该最大距离加上向外误差容限（Outward Error Bound）后**严格小于 0** 时才剔除实例；平面相切（Contact）情况一律保留。
- **变换完备性**：天然支持镜像变换、非均匀缩放与错切（Shear），不再依赖旧版“最大矩阵列长度”的保守包围球近似。
- **投影与退化保护**：透视、正交、非对称及 Reversed-Z 视图统一使用 CPU 提取的视锥平面（零值近平面继续表示禁用近裁面）；非法包围盒、非仿射变换或非法平面一律保守保留。

---

## 严格原生 FP16 算术与误差界

支撑距离计算采用原生 16 位半精度类型 `float16_t` 与 `float16_t3`，在 Shader 中声明 `#pragma use_dxc` 与 `#pragma require native16bit`（生产环境要求 DXC SM 6.2 及 `-enable-16bit-types`，不使用 `min16float` 或无 16-bit 硬件回退变体）。

| 数据项 | 运算精度 | 设计原因与误差控制 |
| --- | --- | --- |
| **世界坐标、矩阵、平移、平面截距 $D$** | `FP32` | 避免大世界坐标进入 FP16 有限的指数范围导致上溢 |
| **按最大绝对分量归一化的半轴、有界平面法线** | `FP16` (`float16_t3`) | 仅归一化方向向量进入 FP16（若自定义平面法线分量超过 `1` 则回退至 FP32 支撑计算） |
| **FP16 归一化支撑外扩余量** | `+ 1/32` | 在还原回世界单位前将归一化支撑值外扩 `1/32`。对于就近舍入 FP16（单位舍入误差 $u = 2^{-11}$），9 次有界乘法（含 2 次操作数转换）、6 次点积内加法和 2 次最终加法的一阶绝对误差低于 $57u$，高阶项与非规格化数刷零均完全落在 `1/32`（$= 64u$）余量内 |
| **FP32 世界空间附加余量** | `4e-6 * (dot(abs(N), worldMagnitude) + abs(D)) + 1e-30` | `worldMagnitude` 包含变换/平移相消前的幅值，确保视锥边界附近的模糊实例一律保守判为可见 |
| **点光源球体 / 旧版球体 VSM 页测试** | `FP32` 保守外接球 | 半径采用缩放长度计算以避免平方长度上溢/下溢；主视图裁剪在不请求光源球测试时跳过构造该球体 |

> [!NOTE]
> GPU 缓冲区字段、字节偏移、步长（Stride）以及间接绘制/Job 计数器契约均保持不变。

---

## 独立验证 (`Validate-InstanceFrustumCulling.ps1`)

在包根目录下执行：

```powershell
./Tools~/Validate-InstanceFrustumCulling.ps1
```

- **环境要求**：DXC、`clang-cl`、Windows SDK / Linker、Python 3 以及支持原生 16-bit 算术的 D3D12 GPU（默认输出至 `Temp~/instance-frustum`）。
- **验证内容**：
  1. 编译全部 8 个生产 Kernel，并静态检查 DXIL 中是否生成严格的原生 `f16` 点积指令；
  2. 在独立 D3D12 Compute Dispatch 中执行共享裁剪谓词，并与基于相同 FP32 输入的 Python 双精度（FP64）8 顶点变换参考基准（Oracle）逐一比对；
  3. 覆盖 **41,024 个确定性用例**（6 个视锥面、相切边界、禁用近裁面、退化/非法包围盒、镜像、错切、极端缩放、大世界平移及偏心透视视图）。
- **验证结论**：在 RTX 5070 Ti 上，原生 FP16 与诊断 FP32 变体均实现**零漏剔（Zero False Rejections）**与**零外接球包含失败**。
