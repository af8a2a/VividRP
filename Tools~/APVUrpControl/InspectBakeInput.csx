// Run with `unity command 'script eval' --code (Get-Content ... -Raw)` in either project.
var assembly = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Unity.PathTracing.Editor");
var serialization = assembly.GetType("UnityEditor.PathTracing.LightBakerBridge.BakeInputSerialization");
var result = "";
foreach (var item in new[] { new[] { "BakeInput", "Temp/bakeInput.bakeInput" }, new[] { "ProbeRequestData", "Temp/lightProbeRequests" } })
{
    var type = assembly.GetType("UnityEditor.PathTracing.LightBakerBridge." + item[0]);
    var method = serialization.GetMethods().First(m => m.Name == "Deserialize" && m.GetParameters()[0].ParameterType == typeof(string) && m.GetParameters()[1].ParameterType.GetElementType() == type);
    var args = new object[] { item[1], null };
    if (!(bool)method.Invoke(null, args)) throw new System.InvalidOperationException("Cannot deserialize " + item[1]);
    foreach (var field in type.GetFields())
    {
        var value = field.GetValue(args[1]);
        result += item[0] + "." + field.Name + "=";
        if (value is System.Array array)
        {
            result += "count:" + array.Length + "\n";
            if (field.Name == "requests" || field.Name == "instanceData" || field.Name == "lightData" || field.Name == "materialData")
                foreach (var entry in array) result += UnityEngine.JsonUtility.ToJson(entry) + "\n";
        }
        else if (field.Name != "environmentData") result += UnityEngine.JsonUtility.ToJson(value) + "\n";
        else result += "omitted environment texture\n";
    }
}
return result;
