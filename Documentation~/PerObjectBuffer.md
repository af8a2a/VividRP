# Per-Object Buffer

VividRP 的 Per-Object Buffer 为 `MeshRenderer` 和 `SkinnedMeshRenderer` 提供不依赖
`MaterialPropertyBlock` 的逐 Renderer Shader 数据。系统把所有记录写入一个全局 Raw
`GraphicsBuffer`，并用 Renderer 的 `ShaderUserValue` 保存 16 字节粒度的记录地址，因此不会因本
系统的写入退出 SRP Batcher。

## 创建布局

从 `Assets > Create > VividRP > Per Object Buffer Layout` 创建布局资产。设置稳定的
`Shader Identifier`，然后添加 `Int`、`Float`、`Vector`、`Color` 或 `Matrix` 属性及默认值。

布局保存后，同目录会生成 `<Layout>.generated.hlsl`。生成文件会在布局保存、移动、重命名和
构建前自动同步。它是派生文件，不应手工编辑。属性按 4 字节紧密排列，记录 stride 最终对齐到
16 字节；修改属性、顺序、类型或默认值都会改变布局签名，因此运行时已经绑定的 Renderer 需要
重新绑定。

属性名和 Shader Identifier 必须是 ASCII HLSL 标识符。一个 Per-Object 属性不得同时作为同名
成员出现在 `UnityPerMaterial` CBUFFER 中。

## CPU 用法

```csharp
using VividRP.Runtime;

VividPerObjectBlock block = VividPerObjectBuffer.Bind(renderer, characterLayout);
block.SetFloat("_Dissolve", 0.35f);
block.SetColor("_Tint", Color.cyan);
block.SetMatrix("_Deformation", deformation);
```

字符串和 Shader property ID 会做属性查找。频繁更新时应预解析 handle：

```csharp
VividPerObjectPropertyHandle dissolve = characterLayout.GetProperty("_Dissolve");
VividPerObjectBlock block = VividPerObjectBuffer.Bind(renderer, characterLayout);

// 热路径不再进行属性字典查找。
block.SetFloat(dissolve, value);
```

同一 Renderer 重复绑定同一布局会返回原 block；切换布局会创建带新默认值的记录，并使旧 block
失效。系统绑定期间独占 Renderer 的 ShaderUserValue，`Unbind` 会恢复绑定前的值：

```csharp
VividPerObjectBuffer.Unbind(renderer);
```

所有 API 仅允许在 Unity 主线程调用。场景加载或域重载后，调用方需要重新绑定。系统会在每次
相机绘制前上传变化区间；Set 值的二进制表示没有变化时不会产生脏区。

## Shader 用法

在使用逐对象属性的每个 pass 中，于 VividRP Core/Input include 之后包含生成文件。每个读取属性的
Shader stage 都要在 `UNITY_SETUP_INSTANCE_ID` 后执行 setup：

```hlsl
#include "Assets/Shaders/Character.generated.hlsl"

Varyings Vert(Attributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    VIVID_SETUP_PER_OBJECT_Character();

    float dissolve = _Dissolve;
    float4 tint = _Tint;
    float4x4 deformation = _Deformation;
    // mul(deformation, position)
}
```

setup 会为当前 Shader invocation 验证 ShaderUserValue magic、buffer 容量和布局签名，并缓存
解析后的 base address。未绑定、越界或签名不匹配时，每个 accessor 返回布局资产中声明的默认值。
矩阵在 CPU 端按 `Matrix4x4` 列序保存，loader 会重建为适用于 `mul(matrix, vector)` 的
`float4x4`。

如果属性宏和现有 Shader 标识符冲突，可在 include 前关闭别名：

```hlsl
#define VIVID_PER_OBJECT_NO_PROPERTY_ALIASES
#include "Assets/Shaders/Character.generated.hlsl"

VIVID_SETUP_PER_OBJECT_Character();
float dissolve = VividPerObject_Character_<SIGNATURE>_Access__Dissolve();
```

`<SIGNATURE>` 是生成文件中函数名的一部分；以生成文件实际声明为准。

## 生命周期与诊断

系统从 64 KiB 开始按两倍扩容，受 `SystemInfo.maxGraphicsBufferSize` 和 ShaderUserValue 地址编码
上限约束。记录地址在其生命周期内保持不变；释放区间使用 best-fit 重用并合并相邻空闲区，不进行
自动压缩。Renderer 被销毁后，管线下一次相机准备阶段会回收其记录。

`VividPerObjectBuffer.GetStats()` 可查看当前 Renderer 数、容量、使用量、脏区数以及本帧上传字节数。
上传优先使用三缓冲 `LockBufferForWrite` staging buffer 和 compute range-copy；计算着色器不可用时，
使用合并区间的 `CommandBuffer.SetBufferData`。同一帧后续相机只重新绑定全局 buffer，不重复上传。

系统不会调用或清除 `MaterialPropertyBlock`。绑定已经具有 MPB 的 Renderer 时会在 Editor/开发构建
输出警告；这类 Renderer 的 SRP Batcher 兼容性不由本系统保证。

第一版不覆盖 Shader Graph、DOTS/BRG、Vivid meshlet GPU-driven、光线追踪或 Jobs writer。
