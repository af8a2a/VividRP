# Unreal 纯自发光 Cornell Box 对照

本机独立项目：`E:/UnrealProjects/VividGICornellReference/VividGICornellReference.uproject`。

关卡：`/Game/VividReference/Maps/CornellBoxEmissiveCeiling`。

使用本机 `E:/UnrealEngine` 的 Unreal **5.7.4**；没有修改原有 Unreal 项目或引擎源码。

## 与 Unity 对应的输入

`unity_fixture.json` 是从 Unity 当前 `CornellBoxEmissiveCeiling.unity` 通过 Editor API 只读导出的输入。保留原始 sRGB 颜色以及转换后的线性反射率。

- 坐标与单位：Unity `(x, y, z)` 米映射到 Unreal `(z, x, y) × 100` 厘米；Unity 绕 Y 的角度对应 Unreal yaw。
- 房间、两个遮挡块、顶部面片的位置、尺寸与旋转均来自 JSON。每个物体以实际厘米尺寸生成独立静态网格，Actor 缩放保持 1，避免非均匀缩放影响 Mesh Distance Field。
- 相机：Unity `(0, 1.5, -3.8)` 米对应 Unreal `(-380, 0, 150)` 厘米；垂直 FOV 42°、宽高比 16:9，对应 Unreal 水平 FOV **68.621206°**。
- 顶部发光面片：1.6 × 1.2 m，中心为 Unreal `(185, 0, 296.5)` cm，单面法线朝下，线性白色发光约 `(200, 200, 200)`。开启组件 `Emissive Light Source`；没有解析区域光补光。
- 白色物体线性反射率 `(0.5704816, 0.5382356, 0.44798842)`；红墙 `(0.47699985, 0.013411741, 0.0071944054)`；绿墙 `(0.00854038, 0.36724645, 0.03310476)`。
- 材质使用 Default Lit、Metallic 0、Roughness 0.8、Specular 0.5，单面不透明。发光面片的反射率保留为 `(0.5704816, 0.5704816, 0.5704816)`。
- 场景没有 Light / SkyLight、天空或雾，关闭静态光照，不使用烘焙 Lightmap。

## 渲染基准与差异

使用 D3D12 / SM6、Lumen 软件追踪（Hardware Ray Tracing = 0），启用 Mesh Distance Field、Nanite 和 Screen Traces。Epic 档位作为 Unreal 原生质量基准，不将 Lumen 的光线预算强行改为 VividRP 的 16 px / 8 射线。

Unbound Post Process Volume 固定物理 EV100 = 4：ISO 100、f/4、快门 1 秒，曝光补偿 0。关闭 Bloom、Motion Blur、Vignette 和常规 AO；Local Exposure 的高光、阴影对比设为中性 1。

相同 EV 标签不代表两个引擎的曝光标定和最终显示完全相同。Unreal 的镜头透射标定、默认 Filmic Tonemapper、Default Lit 漫反射模型，与 Unity 当前曝光实现、ACES / OpenPBR 路径仍有差异。后续应分别比较线性 GI、反射率和最终图，不能仅凭两张 LDR 截图判断反弹能量是否一致。

普通几何的 Distance Field Resolution Scale 为 2，顶部面片为 4；面片仅在距离场生成时使用双面处理，材质和可见三角形仍是朝下单面。所有网格的 Lumen 卡片上限为 24。这些是 Unreal 的独立表示，不假定与 Unity 的 0.0125 m ThinShell 逐体素一致。

## 工具与资产位置

- `BuildCornellBox.py`：通过 Geometry Script、Material Editing 和 Editor Subsystem 生成资产；完成的关卡拒绝覆盖。需要完整 Editor，PythonScript commandlet 不创建 StaticMeshEditorSubsystem。
- `OpenReference.py`：加载关卡并 pilot 对照相机，不重建资产。
- `PreviewAndCapture.py`：使用真实 Editor tick 预热，完成发光开启 / 关闭 / 恢复，输出 1920 × 1080 截图与状态 JSON；高分辨率输出在目标分辨率额外运行 64 帧，避免直接使用默认 4 帧的历史重建结果。最后保存发光开启状态并恢复 pilot 相机。Editor 在后台可能节流，建议捕获期间保持窗口可见并置前。
- Unreal `Scripts/` 中保存了这些脚本和输入 JSON 的副本；运行证据位于项目的 `Saved/Diagnostics/`。
- 生成资产集中在 `/Game/VividReference/Materials`、`Meshes`、`Maps`，包括可实时切换 `EmissionScale` 的 `MI_CeilingEmitter`。

打开时可直接双击 `.uproject`，再在 Outliner 中 pilot `Cornell Comparison Camera - Unity Match`。使用脚本进入固定机位也可运行：

```powershell
& E:/UnrealEngine/Engine/Binaries/Win64/UnrealEditor.exe E:/UnrealProjects/VividGICornellReference/VividGICornellReference.uproject /Game/VividReference/Maps/CornellBoxEmissiveCeiling '-ExecCmds=py E:/UnrealProjects/VividGICornellReference/Scripts/OpenReference.py'
```

构建资产可使用一次性的 `-ExecutePythonScript=.../BuildCornellBox.py`。该模式完成后会退出 Editor；需要跨帧捕获的脚本应通过持续运行的 Editor 的 Python 控制台或本机 Python Remote Execution 执行。

Editor Level API 参见 Epic 的 [UE 5.7 LevelEditorSubsystem 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/LevelEditorSubsystem?application_version=5.7)。本轮具体接口也核对了本机引擎头文件。

## 本机验证记录（2026-11-12）

`Saved/Diagnostics/build-state.json` 记录生成资产和坐标转换；`final-audit.json` 从正在运行的 Editor 读取真实设置。核对结果：8 个 Static Mesh Actor、0 个 Light、发光倍率恢复为 1；相机位于 `(-380, 0, 150)` cm，水平 FOV 68.621208°，曝光参数对应 EV4。实际 AA 为 TSR（4），Lumen Temporal 开启且 ClearHistoryEveryFrame = 0；原生探针 DownsampleFactor = 16、TracingOctahedronResolution = 8，GI 档位为 Epic（3），硬件追踪为 0。

已输出 `lumen-emission-on.png`、`lumen-emission-off.png`、`lumen-emission-restored.png`，均为 1920 × 1080。关灯图全部 RGB 像素为 0，恢复后照明重新出现。编辑器视口也已通过窗口截图检查，几何、红绿墙和顶灯均可见；结束时重新 pilot 对照相机。

本次首张高分辨率截图使用默认 run-up，后续已将 `r.HighResScreenshotDelay` 提高到 64；捕帧仍有明显颗粒。因此这些 PNG 证明场景接通与光源隔离，不作为收敛后噪声或线性 GI 能量的定量基准；后续质量实验应统一输出方式、预热、曝光及参与比较的 GI 分量。
