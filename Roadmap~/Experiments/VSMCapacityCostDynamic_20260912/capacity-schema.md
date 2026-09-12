# 16 层容量诊断接口

此目录的 `.compute.txt` / `.shader.txt` 是诊断源，恢复到 `Editor/Tools` 后由 Unity 自动生成 meta。生产 Runtime 和 shader 没有改动。仅在诊断帧的实际 raster/resolve 完成后执行；不能把扫描或读回混入无诊断 GPU 计时。

## 实景接入

`VSMCapacityAudit.compute.txt` 有两个 kernel，严格依次派发：

1. `VSMCapacityAuditPages`：`Dispatch(capacity, 2, 1)`。
2. `VSMCapacityReduce`：`Dispatch(projectionCount + 1, 2, 1)`。

参数名称复用生产名称：`_VSMPrototypePageSize`、`_VSMPrototypePagesPerAxis`、`_VSMPrototypePhysicalPagesPerRow`、`_VSMPrototypePageTableEntryCount`、`_VSMPrototypePhysicalPageCapacity`、`_VSMProjectionCount`。

Pages kernel 绑定实际 `StaticPhysicalPage` / `DynamicPhysicalPage`（分别为 `_VSMPrototypeStaticPhysicalPage` / `_VSMPrototypeDynamicPhysicalPage`），`PhysicalPageOwners`、`PageTable`、`PageMetadata`（同名 `_VSMPrototype` 前缀），以及 `_VSMCapacityPages` 输出。Reduce kernel 只需 `_VSMCapacityPages` 和 `_VSMCapacitySummary`，以及 capacity / projectionCount 两个标量。

两缓冲均为 Structured、stride=4，元素数分别 `2 * capacity * 64` 与 `2 * (projectionCount + 1) * 64`。无需 Clear；每次覆盖全部行。只回读 Summary 可得全局和逐 clipmap 汇总；选定快照额外回读 Pages 得页分布。256 页的 Pages 为 128 KiB，10 级的 Summary 为 5.5 KiB。扫描会读取有效已分配页的全部 16 层；诊断成本约等于整池读一次，不能声称廉价或实时常开。

宿主须检查所有标量为正、两个池都是 R32_UInt 数组且层数等于生产 `DepthLayerCount=16`、纹理足以覆盖全部 capacity 个页、buffer 大小正确，表项数为实际表长度。不检查 atlas 尾部不属于物理 capacity 的填充区域。数据必须来自同一完成帧，不可在页面更新期间异步读取无快照语义的 CPU 缓存。

## Pages 布局（64 个 uint/行）

行号 `pool * capacity + physicalPage`，pool 0=静态，1=动态；物理槽和页表归属由两池共享，因而分配页统计不可将两池直接相加当作逻辑页数。

| 列 | 内容 |
|---|---|
| 0 | state：0 owner=0 未分配；1 owner/页表/metadata/allocated 均有效；2 owner 或 clipmap 越界；3 页表未指回该物理槽；4 allocated 位缺失；5 metadata.y 未指回该槽。只统计第一个失败。 |
| 1 | 原始 encoded owner，虚拟页编号+1；0 表示未分配。 |
| 2 | metadata.x 原始 flags；越界或未分配时为0。 |
| 3 | owner 推导的零起点 clipmap；没有可用 owner 时为 UINT_MAX。 |
| 4 | 被检查 texel 数：有效页为 pageSize²，其余为0。 |
| 5 | 非零深度 slot 总数。 |
| 6 | 全部16个 slot 都为0的 texel 数。 |
| 7 | 第16层（index15）非零的 texel 数。**不是溢出数量。** |
| 8 | 单 texel 最大非零 slot 数，0–16。 |
| 9 | 相邻两个非零 slot 中，后者 uint 深度大于前者的对数（降序异常）。 |
| 10 | 相邻两个非零 slot 深度相等的对数。不是所有非相邻重复值的唯一计数；非相邻重复也会产生排序或中间空洞异常。 |
| 11 | 曾遇到0、其后又遇到非零的 texel 数（中间空洞）。 |
| 12 | 非零深度 bits 大于 asuint(1.0) 的 slot 数；包含负数、NaN、Inf 或 >1 等不符合生产饱和深度的值。 |
| 13 | 非空 texel 数，即列4−列6。 |
| 14 | 此有效页 dirty 位是否置位，0/1。 |
| 15 | metadata.y 原始 encoded physical slot；用于所有权异常定位。 |
| 16–31 | 各个深度层的非零 texel 数。 |
| 32–48 | 非零 slot 数为0…16的 texel 直方图（即便遇到错误列表，也按实际非零个数统计）。 |
| 49–63 | 保留，恒0。 |

**空深度 slot** = `列4 * 16 − 列5`；不包括未分配物理槽残留数据。**空 texel** 指所有层都为0，不能仅凭第0层为0判空。**已分配有效页** 指 state=1，要求 owner、页表、metadata.y 和 allocated 相互一致；有效但 dirty 的页仍被检查并单独计数，截取时机应在生产 finalize 后。边界值0与生产一致地解释为无表面，而不是深度恰好为远平面的几何证明。

一致性检查：`sum(hist)=列4`；`sum(k*hist[k])=列5`；`sum(层占用)=列5`；`hist[0]=列6`；正常列表要求列9/10/11/12均为0，且各层占用单调不增、末层非零等于 hist[16]。

## Summary 布局

行号 `pool * (projectionCount + 1) + levelRow`。levelRow=0 汇总整个池；1…projectionCount 对应 clipmap index 0…projectionCount−1。

列0=owner非零的物理槽数；列1=未分配物理槽数（只会出现在全池行）；列2=有效页数；列3=无效 owner/映射页数；列4–14与16–48按有效页累加，唯独列8取最大值。列15及49–63恒0。owner可推导 clipmap 的失败页会计入相应级的列0/3；完全越界 owner 只计入全池行。因此全池列3可能大于各级列3之和。两池的0–3计数应相同。

## GPU 合成检查

`VSMCapacityFixture.compute.txt` 的 `VSMCapacityFixture` 以 Dispatch(2,1,1) 写入 fixture：pageSize=4、capacity=6、physicalPagesPerRow=3、pagesPerAxis=2、projectionCount=2、tableEntryCount=8；两个池为12×8×16 R32_UInt，owners=6 uint、table=8 uint、metadata=8 uint4。绑定 `_FixtureStatic`、`_FixtureDynamic`、`_FixtureOwners`、`_FixtureTable`、`_FixtureMetadata` 后，再用生产诊断两个 kernel 读取它们。

有效页仅 slot0→level0、slot1→level1；slot2未分配但纹理内故意含陈旧值；slot3 owner越界；slot4页表不回指；slot5缺 allocated。slot0 的两池都有空、3层、16层、相邻重复、倒序、空洞、非法深度七类输入；动态池的图样位置不同。slot1静态仅1个有效深度，动态全部16 texel×16层非零。`capacity-fixture.py` 生成独立 CPU 期望 Pages/Summary，并可核对GPU回读。随后将 metadata[0].y 改为99并再跑两个kernel，验证 state5；恢复后可验证句柄复用和重复扫描完全覆盖输出。

## 真实容量遗漏：最小证据链

仅读现有16层**无法**知道是否还有第17个表面；统计满层不能冒充溢出。当前没有生产插入丢弃计数，此诊断也没有伪造该指标。

1. `VSMCapacityInsertion.shader.txt` 直接调用生产 `VividInsertVSMDepth`，保留 ZWrite On / ZTest LEqual / UAV 副作用，不复制插入算法。以16×16 viewport、清空的16层池和深度附件，设置 `_CapacityDistinctCount` 为1、8、16、17、32、64，分别正序/逆序/乘5置换（`_CapacityOrder`=0/1/2），draw instance 数为 distinctCount×64。每 texel 的独立输入集合是精确的 `{k/128 | 1≤k≤distinctCount}`，CPU 排序截取16个为实际池参考。测试集中的count与5互质，因此第三种提交覆盖所有不同深度。
2. 对 count≤16，输入应无遗漏，剩余slot为0；count>16时，生产池应等于最近16个不同深度，**唯一遗漏集合大小已知为 count−16**。重复片元不增加唯一遗漏数。此夹具证明有界行为与排序，不代表实景溢出率。每种order都必须GPU读回比较全部层，不能只看末层。
3. 实景选择满层/高层命中处，用独立 RTAS 从该纹素中心沿主光轴枚举表面，并与实际光栅层比较。记录几何枚举能否复現原始mesh/alpha/顶点变形/背面剔除和深度量化；若不一致，不能把差集直接叫生产丢弃事件。真正有意义的“容量遗漏导致漏遮挡”还需要接收者斜射线先命中被丢失表面的逐射线反例，可沿用 residual leak 的 ray-example 方式。
4. 若要精确实景插入丢弃事件，需另行协调修改生产插入函数：仅在第16层确实执行 `depth=min(previous,depth)` 后仍有非零 depth 时原子计数；必须排除 `previous==depth` 的去重提前退出，不能在整个循环后只判断 `depth!=0`。并发及重复提交下这是丢弃事件数，不是唯一丢失表面数；不能从外部只看插入前后首/末层重建它。本轮未擅自修改生产函数。

隐藏深度也约束未来 HZB：仅最前深度后的 meshlet 不可直接删除。任何剪枝先证明16层列表等价，再核对几何质量；列表等价本身不证明16层足够。
