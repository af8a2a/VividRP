# Per-Object Buffer

VividRP 的 Per-Object Buffer 为 `MeshRenderer` 和 `SkinnedMeshRenderer` 提供不依赖
`MaterialPropertyBlock` 的逐 Renderer Shader 数据。所有布局的记录共享同一个 Raw
`GraphicsBuffer`；每条记录在 `ShaderUserValue` 中保存自己的 16 字节粒度地址，因此 Character、
Terrain、Decal、Effect 等不同 stride 的布局可以同时存在。

## 用代码声明布局

布局是不可变的代码类型，不使用 `ScriptableObject`：

```csharp
using UnityEngine;
using VividRP.Runtime;

public sealed class CharacterPerObjectLayout
    : VividPerObjectLayout<CharacterPerObjectLayout>
{
    public static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    public static readonly int TintId = Shader.PropertyToID("_Tint");
    public static readonly int DeformationId = Shader.PropertyToID("_Deformation");

    public CharacterPerObjectLayout()
    {
    }

    public override string ShaderIdentifier => "Character";

    protected override void Define(VividPerObjectLayoutBuilder builder)
    {
        builder.AddFloat("_Dissolve", 0.0f);
        builder.AddColor("_Tint", Color.white);
        builder.AddMatrix("_Deformation");
    }
}
```

`Define` 的声明顺序就是记录中的 packing 顺序：签名占最前面的 4 字节，属性按 4 字节紧密排列，
最终 stride 对齐到 16 字节。支持 `AddInt`、`AddFloat`、`AddVector`、`AddColor` 和 `AddMatrix`。
布局实例由 `CharacterPerObjectLayout.Instance` 共享，运行时不可修改。
具体布局需要提供 public 无参构造函数，以保持 Player/AOT 环境中的泛型实例化路径。

`ShaderIdentifier` 必须在整个 Player 代码中唯一。布局和属性名称都必须是 ASCII HLSL 标识符；
同一个布局不能声明重复属性。

## CPU 用法

可以直接用泛型绑定，然后沿用 MaterialPropertyBlock 风格的 string/property ID API：

```csharp
VividPerObjectBlock block =
    VividPerObjectBuffer.Bind<CharacterPerObjectLayout>(renderer);

block.SetFloat(CharacterPerObjectLayout.DissolveId, 0.35f);
block.SetColor(CharacterPerObjectLayout.TintId, Color.cyan);
block.SetMatrix(CharacterPerObjectLayout.DeformationId, deformation);
```

高频更新可以预解析 handle：

```csharp
VividPerObjectPropertyHandle dissolve =
    CharacterPerObjectLayout.Instance.GetProperty(CharacterPerObjectLayout.DissolveId);

block.SetFloat(dissolve, value);
```

也可以显式传布局实例：

```csharp
VividPerObjectBuffer.Bind(renderer, CharacterPerObjectLayout.Instance);
```

同一 Renderer 重复绑定等价布局会复用记录；切换到其他布局会分配带默认值的新记录并使旧 block
失效。`Unbind` 会恢复绑定前的 ShaderUserValue。所有 API 仅允许在 Unity 主线程调用。

## 集中生成的 Shader 接口

脚本重载和 Player 构建前，生成器会扫描 Player Assembly 中所有具体的
`VividPerObjectLayout` 类型，并集中写入：

```text
Packages/com.vivid.render-pipelines/Shaders/Core/Public/PerObjectBufferLayouts.generated.hlsl
```

Shader 在 include 前选择当前材质使用的布局。只有被选择布局的普通变量别名会生效，因此多个
布局可以声明相同的 `_Tint` 等属性而不产生宏冲突：

```hlsl
#define VIVID_PER_OBJECT_LAYOUT_Character
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/PerObjectBufferLayouts.generated.hlsl"

UNITY_SETUP_INSTANCE_ID(input);
VIVID_SETUP_PER_OBJECT_Character();

float dissolve = _Dissolve;
float4 tint = _Tint;
float4x4 deformation = _Deformation;
```

每个使用属性的 Shader stage 都要在 `UNITY_SETUP_INSTANCE_ID` 之后执行 setup。setup 对当前
invocation 缓存 base address、buffer 容量和布局有效性；未绑定、越界或签名不匹配会返回代码布局
中声明的默认值。

如果不希望生成普通变量别名：

```hlsl
#define VIVID_PER_OBJECT_LAYOUT_Character
#define VIVID_PER_OBJECT_NO_PROPERTY_ALIASES
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/PerObjectBufferLayouts.generated.hlsl"

VIVID_SETUP_PER_OBJECT_Character();
float dissolve = VividPerObject_Character_Get__Dissolve();
```

一个 Shader program 最多只能选择一个 `VIVID_PER_OBJECT_LAYOUT_<Identifier>` 来启用别名。
Per-Object 属性不得同时作为同名成员出现在 `UnityPerMaterial` CBUFFER 中。

## 内置颜色示例

包内提供了一个最小可运行示例：

- `VividPerObjectColorExampleLayout` 用代码声明 `_PerObjectColor`。
- `VividPerObjectColorExampleController` 每帧绑定 Renderer 并推送颜色；Inspector 中可以切换
  Cached Handle、Property ID 和 Property Name 三种寻址方式。
- `VividRP/Examples/Per-Object Color` Shader 将材质 `_BaseColor` 与逐物体
  `_PerObjectColor` 相乘后直接输出。

使用时创建该 Shader 的 Material，赋给 `MeshRenderer` 或 `SkinnedMeshRenderer`，然后在同一物体
上添加 `VividPerObjectColorExampleController`。关闭 `Animate Color` 可直接在 Inspector 中指定
固定颜色。整个示例不会创建或写入 `MaterialPropertyBlock`。

## MPB CPU 对比基准

在一组专用测试 Renderer 的父物体上添加 `VividPerObjectCpuBenchmarkController`，然后从组件菜单
执行 `Run MPB vs Per-Object CPU Benchmark`。Renderer 列表为空时默认收集所有子物体 Renderer。
测试会输出：

- MPB 与 PerObjectBuffer 在数值变化和数值不变时的每 Renderer 写入耗时。
- 每个操作的当前线程托管分配字节数。
- SSBO 每个模拟帧的 `PrepareAndBind + Graphics.ExecuteCommandBuffer` 独立耗时。
- MPB/SSBO 写入路径的倍速比。

MPB 路径复用单个 `MaterialPropertyBlock` 并使用 property ID；PerObjectBuffer 可选择 Cached Handle、
Property ID 或 Property Name。首次绑定、GameObject 创建和颜色计算都在计时区间之外。每个模拟帧
后都会提交 SSBO 脏区，避免连续循环造成脏区列表不符合真实帧行为。

基准要求使用未被其他 PerObject Layout 绑定的专用 Renderer。已有 MPB 会在测试前保存，结束后
恢复。更大的手动基准位于 `VividPerObjectCpuBenchmarkTests.CompareThousandRenderers_PrintsOptimizationBaseline`，
该测试标记为 `Explicit`，不会进入常规测试套件。分析实际渲染帧时，可同时查看 Unity Profiler 中的
`VividRP.PerObjectBuffer.PrepareAndBind` 与 `VividRP.PerObjectBuffer.Upload` 标记。

## 上传与生命周期

- CPU 保存完整 buffer 镜像，相同位表示不会产生脏区。
- 相机绘制前合并脏区，通过三缓冲 staging buffer 和 compute range-copy 上传。
- compute 不可用时使用合并后的 `CommandBuffer.SetBufferData`。
- 所有布局共享 best-fit 分配器；每条记录的地址在其生命周期内稳定。
- buffer 从 64 KiB 按 2 倍增长，扩容时现有记录地址不变。
- Renderer 销毁、显式解绑、程序集重载和 Render Pipeline 销毁都会回收相应资源。
- 系统不会调用 `SetPropertyBlock`，已有 MPB 只会产生开发期警告。
