from pathlib import Path
p=Path('Temp~/vsm-dynamic-timing/live-abba.cs')
s=p.read_text(encoding='utf-8').replace('live-abba"','live-abba-v2"')
s=s.replace('for(int i=0;i<samplers.Length;i++) {', '''var syncTransform=(System.Action)System.Delegate.CreateDelegate(typeof(System.Action),meshlet,typeof(VividRP.Runtime.GPUDriven.MeshletRenderer).GetMethod("UpdateDatabaseIfNeeded",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic));
var scene=VividRP.Runtime.GPUDriven.VividGPUDrivenSystem.instance.PrimitiveScene;
var revisionProperty=scene.GetType().GetProperty("DynamicShadowRevision",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public);
uint revisionBefore=0;int otherCameraCallbacks=0;
for(int i=0;i<samplers.Length;i++) {''')
# PrimitiveScene's type/member accessibility is internal; get both through reflection.
s=s.replace('var scene=VividRP.Runtime.GPUDriven.VividGPUDrivenSystem.instance.PrimitiveScene;',
'''var systemType=runtime.Assembly.GetType("VividRP.Runtime.GPUDriven.VividGPUDrivenSystem");
var system=systemType.GetProperty("instance",flags|System.Reflection.BindingFlags.FlattenHierarchy).GetValue(null);
var scene=systemType.GetProperty("PrimitiveScene",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(system);''')
s=s.replace('warm=sample=frameIndex=0;began=', 'warm=sample=frameIndex=0;otherCameraCallbacks=0;revisionBefore=(uint)revisionProperty.GetValue(scene);began=')
s=s.replace('if(disposed||observed!=camera)return;\n if(force[stage])',
'''if(disposed)return;
 if(observed!=camera){otherCameraCallbacks++;return;}
 if(force[stage])''')
s=s.replace('if(scenarios[stage]==2)cube.transform.position=cubePosition+camera.transform.right*((frameIndex%32-16)*.025f);',
'''if(scenarios[stage]==2){cube.transform.position=cubePosition+camera.transform.right*((frameIndex%32-16)*.025f);syncTransform();}''')
s=s.replace('warmFrames,sampleFrames,allocated,', 'warmFrames,sampleFrames,otherCameraCallbacks,revisionBefore,revisionAfter=(uint)revisionProperty.GetValue(scene),allocated,')
Path('Temp~/vsm-dynamic-timing/live-abba-v2.cs').write_text(s,encoding='utf-8')
