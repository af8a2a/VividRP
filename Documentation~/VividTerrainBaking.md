# VividTerrain 分块 Meshlet 烘焙与多级 LOD

返回索引：[Documentation~ 索引](README.md) · 相关文档：[Cluster LOD 生成](GPUDrivenClusterLOD.md) | [Virtual Texture 核心架构](VirtualTextureCoreArchitecture.md)

`VividTerrain` 将 Unity 原生 `Terrain` 无损转换为 VividRP 管理的地形元数据与分块 Meshlet 几何。每个非空地形分块（Chunk）作为独立实例接入 GPU Driven 管线，直接复用实例视锥裁剪、Meshlet 裁剪、Visibility Buffer 解析、阴影以及虚拟纹理（VT）/ Bindless 表面绑定路径。

## 转换工作流

1. 在场景中选中一个挂载了 Unity `Terrain` 组件的 GameObject。
2. 选择顶部菜单 **GameObject > VividRP > GPUDriven > Create VividTerrain Copy**，或在 `Terrain` 组件右键菜单中选择 **Create VividTerrain Copy**。
3. VividRP 会在源 `TerrainData` 资产同级目录下生成 `VividTerrainData` 资产（若源资产不在 `Assets` 下，则输出至 `Assets` 根目录）。
4. VividRP 会在场景中复制出一个同级副本 GameObject，仅移除副本上的原生 `Terrain` 渲染组件并挂载 `VividTerrain` 组件。

> [!IMPORTANT]
> - **无损转换**：源 GameObject、`Terrain` 组件及 `TerrainData` 资产**不会被修改**；副本会完整保留其余组件、子物体以及 `TerrainCollider`。
> - **避免重影**：转换后原始 Terrain 仍保持开启状态；在启用 GPU Driven 对比或替换渲染时，请手动禁用原 Terrain 渲染组件以避免几何重叠。
> - **撤销行为**：Editor Undo 会移除场景中的副本物体，但已生成的 `VividTerrainData` 资产会保留在工程中，不再需要时可手动删除。

---

## 烘焙数据规格

### 1. 高度图采样与地形挖洞（Terrain Holes）

- **规则网格与共享边界**：烘焙器一次性读取源高度图，在地形局部空间构建规则网格（每个顶点包含位置、有限差分法线、切线和全地形 UV）。网格按独立包围盒划分为多个 Chunk，相邻 Chunk 之间保留**一行/一列共享边界顶点**；即使采样步长不能整除源分辨率，最末一条高度图边缘也始终会被采样。
- **挖洞保守剔除**：严格尊重 Terrain Holes——只要采样四边形覆盖了任意一个源挖洞纹素即被剔除，避免在使用较粗采样步长烘焙时错误桥接洞口。完全由洞组成的空 Chunk 不创建 GPU 实例。

### 2. 多层材质与 Virtual Texture 表面绑定

- **最多 8 层材质混合**：从每个 `TerrainLayer` 复制最多 8 层的 Diffuse、Normal、Mask 纹理及 Tiling 和核心材质标量参数，并保留前两张 RGBA Alphamap 作为 Control Map（每张覆盖 4 层）。超过 8 层的源数据仍保存在资产中以保证源保真度，但 GPU Driven 当前仅渲染前 8 层。
- **后端无关 Surface Binding**：多层地形复用静态网格相同的 Surface Binding 协议。每层地形分配一组 Base / Normal / Mask 绑定，每张 Control Map 复用线性 Mask 资源槽。
- **Virtual Texture 协同**：在 VT 后端下，地形图层与 Control Map 共享 GPU Driven VT 地址空间，使用 GBuffer Resolve Pass 重建的显式梯度采样，并仅对当前像素实际采样的 Control Map 与活跃图层发出缺页反馈（Feedback）。混合前会对权重执行 Clamp 与归一化，再混合 Base、切线空间法线、Metallic、Occlusion 与 Roughness。
- **向后兼容**：未捕获 Control Map 的旧版烘焙资产会确定性地回退至第一层材质，直至重新烘焙。

---

## Meshlet 存储与多级 LOD

- **锁边界简化**：每个非空 Chunk 通过 `VividMeshOptimizer`（meshoptimizer 绑定）生成最多 4 级 LOD。简化器开启**边界锁定（Locked Borders）**，且地形显式**禁用 Sloppy Fallback**（因为 Sloppy 模式无法保持边界顶点不退化），从而确保相邻 Chunk 选择不同 LOD 层级时不产生接缝裂缝。
- **LZ4 二进制子资产存储**：不持久化中间 Unity `Mesh` 资产。每个 Chunk 持有一个 `VividMeshletCollectionAsset` 子资产，其节点、Meshlet、顶点与局部索引序列化为版本化二进制数据块：
  - **Version 2（当前）**：采用平台无关的 **LZ4 块压缩**，避免重复的地形顶点/索引流膨胀为巨大的 YAML 数组。
  - **Version 1（兼容）**：旧版 GZip 数据块仍可正常读取，并在资产重新保存时自动重写为 LZ4。

### 默认烘焙参数

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| **Height Sample Stride** | `1` | 使用每一个源高度图采样点 |
| **Chunk Size** | `64 × 64` Quads | 单个分块的四边形网格尺寸 |
| **Maximum Chunk LOD Levels** | `4` | 每个分块请求的最大 LOD 层级数（边界锁无法继续有效减面时会提前终止） |
| **Vertex-Cache Optimization** | `Enabled` | 启用 meshoptimizer 顶点缓存优化 |
