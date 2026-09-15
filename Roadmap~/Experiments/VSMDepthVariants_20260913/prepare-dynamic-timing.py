from pathlib import Path
import re

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
s = (HERE / "timing.cs.txt").read_text()
s = s.replace('VSMDepthVariants_20260913/timing"', 'VSMDepthVariants_20260913/timing-moving-light"')
order = list(range(8)) + list(reversed(range(8)))
values = dict(resolutions=[2048 if i < 4 else 4096 for i in order],
              modes=[[0, 1, 2, 0][i % 4] for i in order],
              layouts=[1 if i % 4 == 3 else 0 for i in order], capacities=[1024] * len(order))
for name, items in values.items():
    s = re.sub(r"var " + name + r" = new int\[\] \{[^}]+\};", "var " + name + " = new int[] {" + ",".join(map(str, items)) + "};", s)
s = s.replace(' || UnityEngine.RenderSettings.sun.transform.rotation != lightRotation', '')
s = s.replace('if (disposed) return; disposed = true;', 'if (disposed) return; disposed = true;UnityEngine.RenderSettings.sun.transform.rotation=lightRotation;')
s = s.replace('warm = sample = 0; started =', 'UnityEngine.RenderSettings.sun.transform.rotation=lightRotation;warm = sample = 0; started =')
s = s.replace('        UnityEditor.EditorApplication.QueuePlayerLoopUpdate(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();',
    '        UnityEngine.RenderSettings.sun.transform.rotation=lightRotation*UnityEngine.Quaternion.Euler(0,(warm%16-8)*.025f,0);\n        UnityEditor.EditorApplication.QueuePlayerLoopUpdate(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();')
(HERE / "timing-moving-light.cs.txt").write_text(s, encoding="utf-8")
(ROOT / "Temp~/vsm-depth-variants/timing-moving-light.cs").write_text(s, encoding="utf-8")
