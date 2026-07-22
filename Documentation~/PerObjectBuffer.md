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

## 上传与生命周期

- CPU 保存完整 buffer 镜像，相同位表示不会产生脏区。
- 相机绘制前合并脏区，通过三缓冲 staging buffer 和 compute range-copy 上传。
- compute 不可用时使用合并后的 `CommandBuffer.SetBufferData`。
- 所有布局共享 best-fit 分配器；每条记录的地址在其生命周期内稳定。
- buffer 从 64 KiB 按 2 倍增长，扩容时现有记录地址不变。
- Renderer 销毁、显式解绑、程序集重载和 Render Pipeline 销毁都会回收相应资源。
- 系统不会调用 `SetPropertyBlock`，已有 MPB 只会产生开发期警告。
