from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
s = (HERE.parent / "VSMDepthSearch_20260913/timing.cs.txt").read_text(encoding="utf-8-sig")
s = s.replace("VSMDepthSearch_20260913", "VSMDepthVariants_20260913")
order = list(range(16)) + list(reversed(range(16)))
def array(values):
    return "new int[] {" + ",".join(map(str, values)) + "}"
s = s.replace("new int[] {2048,2048,4096,4096,4096,2048,2048}", array([2048 if i < 8 else 4096 for i in order]))
s = s.replace("new int[] {2048,2048,4096,4096,4096,4096,2048,2048}", array([2048 if i < 8 else 4096 for i in order]))
s = s.replace("new int[] {1,0,1,0,0,1,0,1}", array([i % 4 for i in order]))
s = s.replace("var capacities = new int[] {1024,1024,1024,1024,1024,1024,1024,1024};", "var capacities = " + array([1024] * len(order)) + ";\nvar layouts = " + array([(i // 4) % 2 for i in order]) + ";")
s = s.replace('resolve.SetInt("_VSMDepthSearchLinear", modes[stage]);', '''resolve.SetInt("_VSMDepthSearchLinear",0);
    resolve.SetInt("_VSMDepthExperiment",modes[stage]);
    resolve.SetInt("_VSMDepthLayoutInterleaved",layouts[stage]);
    UnityEngine.Shader.SetGlobalInt("_VSMDepthLayoutInterleaved",layouts[stage]);
    runtime.GetMethod("InvalidateCache",flags).Invoke(null,null);
    additional.ResetPostProcessingHistory();''')
s = s.replace('resolve.SetInt("_VSMDepthSearchLinear", 0);', '''resolve.SetInt("_VSMDepthSearchLinear", 0);
    resolve.SetInt("_VSMDepthExperiment",0);resolve.SetInt("_VSMDepthLayoutInterleaved",0);
    UnityEngine.Shader.SetGlobalInt("_VSMDepthLayoutInterleaved",0);runtime.GetMethod("InvalidateCache",flags).Invoke(null,null);''')
s = s.replace('" linear=" + modes[stage]', '" layout="+layouts[stage]+" mode="+modes[stage]')
s = s.replace('"_p" + actualCapacity;', '"_p" + actualCapacity + "_l" + layouts[stage] + "_m" + modes[stage];')
(HERE / "timing.cs.txt").write_text(s, encoding="utf-8")
(ROOT / "Temp~/vsm-depth-variants/timing.cs").write_text(s, encoding="utf-8")
