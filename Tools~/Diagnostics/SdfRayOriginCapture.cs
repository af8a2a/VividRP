// Two-frame GPU origin inspection. Real scene buffers/cache, owned diagnostic outputs, no assets saved.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEditor;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Runtime.GPUDriven.Meshlets;
using VividRP.Editor.Tools;
using System.Runtime.InteropServices;
{
    const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
    const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    const string shaderPath = "Packages/com.vivid.render-pipelines/Editor/Resources/SdfRayOriginValidation.compute";
    const int rays = 1024;
    var type = typeof(SdfDiffuseGatherPass);
    var hook = type.GetField("EditorTraceCapture", stat);
    foreach (string name in new[] { "EditorTraceCapture", "EditorLinearCapture", "EditorGatherStageCapture", "EditorBindResolveProbe", "EditorAtrousDispatchCapture" })
        if (type.GetField(name, stat).GetValue(null) != null) throw new InvalidOperationException("Another GI diagnostic is active: " + name);
    if ((int)type.GetField("EditorGatherFrameOverride", stat).GetValue(null) != -1
        || (int)type.GetField("EditorAtrousIterationOverride", stat).GetValue(null) != -1
        || (int)type.GetField("EditorGatherHistoryDiagnostics", stat).GetValue(null) != 0)
        throw new InvalidOperationException("A diagnostic override is configured.");
    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(EmissiveCornellBoxSceneBuilder.ScenePath);
    if (!scene.isLoaded || scene.isDirty) throw new InvalidOperationException("Open the clean emissive Cornell fixture.");
    var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single();
    var game = Resources.FindObjectsOfTypeAll(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).OfType<EditorWindow>().First();
    var pass = ((System.Collections.IList)typeof(PassRecorder).GetField("s_RenderPasses", stat).GetValue(null)).Cast<object>().Single(p => p.GetType() == type);
    object Field(string name) => type.GetField(name, inst).GetValue(pass);
    var source = Field("m_Scene"); var sourceType = source.GetType();
    GraphicsBuffer Buffer(string name) => (GraphicsBuffer)sourceType.GetField(name, inst).GetValue(source);
    object Property(string name) => sourceType.GetProperty(name, inst).GetValue(source);
    var cache = AssetDatabase.LoadAssetAtPath<VividSdfSurfaceCache>(EmissiveCornellBoxSceneBuilder.CachePath);
    if (!object.ReferenceEquals(Property("SurfaceCache"), cache)) throw new InvalidOperationException("The live cache is not the emissive fixture.");
    uint revision = (uint)Property("Revision");
    int cards = (int)Property("RadiosityCardCount"), mask = (int)Field("m_LayerMask");
    if (cards <= 0 || mask != 1 << 29 || (int)Field("m_RadiosityPublishedOrder") != 8 || (int)Field("m_RadiosityBatch") != 32)
        throw new InvalidOperationException("Expected settled source 8/64/32 and the fixture layer.");
    Array ReadStructs(string name, int stride, params string[] fields) {
        var t = sourceType.GetNestedType(name, inst);
        var b = Buffer(name == "Instance" ? "m_InstanceBuffer" : name == "Field" ? "m_FieldBuffer" : "m_DiffuseSurfaceBuffer");
        if (b.stride != stride || Marshal.SizeOf(t) != stride) throw new InvalidOperationException(name + " stride mismatch.");
        // Every declared field is checked, not just the total stride.
        int offset = 0;
        foreach (string field in fields) { if (Marshal.OffsetOf(t, field).ToInt32() != offset) throw new InvalidOperationException(name + "." + field);
            offset += Marshal.SizeOf(t.GetField(field).FieldType); }
        var values = Array.CreateInstance(t, b.count); b.GetData(values); return values;
    }
    var instances = ReadStructs("Instance", 80, "WorldToLocal0", "WorldToLocal1", "WorldToLocal2", "FieldIndex", "LayerMask", "EntityIdLow", "EntityIdHigh", "DistanceScale", "Padding0", "Padding1", "Padding2");
    var fields = ReadStructs("Field", 48, "OriginAndVoxelSize", "Width", "Height", "Depth", "WordOffset", "SignMode", "PageOffset", "ShellThickness", "Padding");
    var surfaces = ReadStructs("DiffuseSurface", 64, "AlbedoAndValid", "Emission", "CardMinAndFirst", "CardExtent");
    object Member(object v, string name) => v.GetType().GetField(name).GetValue(v);
    int Find(float y) => Enumerable.Range(0, instances.Length).Single(i => {
        var v = instances.GetValue(i); var r = (Vector4)Member(v, "WorldToLocal1");
        return (uint)Member(v, "LayerMask") == (uint)mask && Mathf.Abs(r.y - 5) < 1e-4f && Mathf.Abs(r.w + 5*y) < 1e-4f;
    });
    int ceiling = Find(3.1f), floor = Find(-.1f);
    int emitter = Enumerable.Range(0, instances.Length).Single(i => (uint)Member(instances.GetValue(i), "LayerMask") == (uint)mask
        && ((Vector4)Member(surfaces.GetValue(i), "Emission")).x > 100);
    AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderPath);
    int inspect = shader.FindKernel("InspectSdfCardOrigins"), trace = shader.FindKernel("TraceSdfOriginPoints");
    var shaderErrors = ShaderUtil.GetComputeShaderMessages(shader).Where(m => m.severity.ToString() == "Error").ToArray();
    if (shaderErrors.Length > 0) throw new InvalidOperationException(string.Join("; ", shaderErrors.Select(m => m.message)));
    string output = "E:/VividRP_GI_Evidence/SdfRayOrigin-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
    System.IO.Directory.CreateDirectory(output);
    var variants = new List<object>();
    var compile = typeof(ShaderUtil).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Single(m => m.Name == "CompileComputeShaderVariant");
    foreach (string name in new[] { "InspectSdfCardOrigins", "TraceSdfOriginPoints" }) {
        var variant = compile.Invoke(null, new object[] { shader, shader.FindKernel(name), Array.Empty<string>(), GraphicsDeviceType.Direct3D12, BuildTarget.StandaloneWindows64 });
        if (!(bool)variant.GetType().GetProperty("Success").GetValue(variant)) {
            var messages = variant.GetType().GetProperty("Messages").GetValue(variant);
            System.IO.File.WriteAllText(output + "/compile-failure.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { name, messages }, Newtonsoft.Json.Formatting.Indented));
            throw new InvalidOperationException("Kernel compilation failed: " + name);
        }
        byte[] bytes = (byte[])variant.GetType().GetProperty("ShaderData").GetValue(variant);
        System.IO.File.WriteAllBytes(output + "/compiled-" + name + ".bin", bytes);
        using var hash = System.Security.Cryptography.SHA256.Create();
        variants.Add(new { name, bytes = bytes.Length, sha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() });
    }
    var cardResults = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cards * 64 * 5, 16);
    GraphicsBuffer pointsBuffer = null, rayResults = null, pointMetrics = null;
    var sky = new Cubemap(1, TextureFormat.RGBAHalf, false) { hideFlags = HideFlags.HideAndDontSave };
    for (int face = 0; face < 6; ++face) sky.SetPixels(new[] { Color.black }, (CubemapFace)face); sky.Apply();
    var points = new List<Vector4>(); var descriptions = new List<object>();
    void Point(string name, Vector3 position, Vector3 normal, float bias, int receiver) {
        points.Add(new Vector4(position.x, position.y, position.z, bias)); points.Add(new Vector4(normal.x, normal.y, normal.z, receiver));
        descriptions.Add(new { name, position = new[] { position.x, position.y, position.z }, normal = new[] { normal.x, normal.y, normal.z }, bias, receiver });
    }
    Point("gap-4cm", new Vector3(0, 3, 1.85f), Vector3.down, .04f, ceiling);
    Point("gap-5mm", new Vector3(0, 3, 1.85f), Vector3.down, .005f, ceiling);
    Point("gap-1mm", new Vector3(0, 3, 1.85f), Vector3.down, .001f, ceiling);
    Point("floor-4cm", new Vector3(0, 0, 1.85f), Vector3.up, .04f, floor);
    Point("floor-5mm", new Vector3(0, 0, 1.85f), Vector3.up, .005f, floor);
    Point("ceiling-roi-4cm", new Vector3(.001650165f, 3, .842733f), Vector3.down, .04f, ceiling);
    Point("ceiling-roi-5mm", new Vector3(.001650165f, 3, .842733f), Vector3.down, .005f, ceiling);
    int phase = 0, pending = 0; bool cardsDone = false, raysDone = false;
    Vector4[] cardValues = null; string sourceHash = null;
    var errors = new List<string>();
    var wrapped = typeof(ComputeCommandBuffer).BaseType.GetField("m_WrappedCommandBuffer", inst);
    void CheckSource() {
        if (!object.ReferenceEquals(Property("SurfaceCache"), cache) || (uint)Property("Revision") != revision
            || (int)Field("m_RadiosityPublishedOrder") != 8 || (int)Field("m_RadiosityBatch") != 32 || (int)Field("m_RadiosityUpdateCards") != 0)
            throw new InvalidOperationException("Source changed during origin inspection.");
    }
    void Read(CommandBuffer cmd, GraphicsBuffer buffer, string name, Action<Vector4[]> done) {
        pending++;
        cmd.RequestAsyncReadback(buffer, request => {
            try { if (request.hasError) throw new InvalidOperationException("GPU readback failed: " + name);
                System.IO.File.WriteAllBytes(output + "/" + name + ".f32", request.GetData<byte>().ToArray());
                done(request.GetData<Vector4>().ToArray()); }
            catch (Exception e) { errors.Add(e.ToString()); } finally { pending--; }
        });
    }
    Action<ComputePassContext, Texture, Texture, Texture> capture = (context, sources, lighting, geometry) => {
        if (context.Get<VividCameraData>().camera != camera || (phase != 0 && phase != 2)) return;
        try {
            CheckSource();
            int current = phase == 0 ? inspect : trace;
            var cmd = (CommandBuffer)wrapped.GetValue(context.cmd);
            cmd.SetComputeBufferParam(shader, current, "_MeshSdfVoxels", Buffer("m_Voxels"));
            cmd.SetComputeBufferParam(shader, current, "_MeshSdfFields", Buffer("m_FieldBuffer"));
            cmd.SetComputeBufferParam(shader, current, "_MeshSdfInstances", Buffer("m_InstanceBuffer"));
            cmd.SetComputeBufferParam(shader, current, "_MeshSdfNodes", Buffer("m_NodeBuffer"));
            cmd.SetComputeBufferParam(shader, current, "_MeshSdfLeafInstances", Buffer("m_LeafInstanceBuffer"));
            cmd.SetComputeBufferParam(shader, current, "_SdfDiffuseSurfaces", Buffer("m_DiffuseSurfaceBuffer"));
            cmd.SetComputeBufferParam(shader, current, "_SdfRadiosityWork", Buffer("m_RadiosityWorkBuffer"));
            cmd.SetComputeBufferParam(shader, current, "_SdfLocalCardTable", (GraphicsBuffer)typeof(VividSdfSurfaceCache).GetProperty("LocalCardTable", inst).GetValue(cache));
            cmd.SetComputeTextureParam(shader, current, "_SdfSurfaceAlbedoDepth", (Texture)typeof(VividSdfSurfaceCache).GetField("m_AlbedoDepth", inst).GetValue(cache));
            cmd.SetComputeTextureParam(shader, current, "_SdfSurfaceNormalCoverage", (Texture)typeof(VividSdfSurfaceCache).GetField("m_NormalCoverage", inst).GetValue(cache));
            var atlas = (Texture)(TextureHandle)Field("m_RadiosityInput");
            cmd.SetComputeTextureParam(shader, current, "_SdfRadiosityInput", atlas);
            cmd.SetComputeTextureParam(shader, current, "_SkyTexture", sky);
            cmd.SetComputeIntParam(shader, "_MeshSdfNodeCount", (int)Property("NodeCount"));
            cmd.SetComputeIntParam(shader, "_SdfRadiosityLayout", cards);
            cmd.SetComputeIntParam(shader, "_SdfDiffuseLayerMask", mask);
            cmd.SetComputeIntParam(shader, "_SdfDiffuseLightLayerMask", mask);
            cmd.SetComputeVectorParam(shader, "_SdfDiffuseLightPositionRange", Vector4.zero);
            cmd.SetComputeVectorParam(shader, "_SdfDiffuseLightColorShadow", Vector4.zero);
            cmd.SetComputeVectorParam(shader, "_SkyTextureParams", Vector4.zero);
            cmd.SetComputeFloatParam(shader, "_SdfDiffuseMaxDistance", (float)Field("m_TraceDistance"));
            cmd.SetComputeFloatParam(shader, "_SdfDiffuseSkyTraceDistance", 0);
            cmd.SetComputeFloatParam(shader, "_SdfDiffuseRayBias", (float)Field("m_Bias"));
            if (phase == 0) {
                phase = 1;
                cmd.SetComputeIntParam(shader, "_SdfOriginCardCount", cards);
                cmd.SetComputeBufferParam(shader, inspect, "_SdfOriginResults", cardResults);
                cmd.DispatchCompute(shader, inspect, 1, 1, cards);
                Read(cmd, cardResults, "cards", v => { cardValues = v; cardsDone = true; });
                pending++;
                cmd.RequestAsyncReadback(atlas, 0, request => { try {
                    if (request.hasError) throw new InvalidOperationException("Source readback failed.");
                    byte[] bytes = request.GetData<byte>().ToArray(); System.IO.File.WriteAllBytes(output + "/source.bin", bytes);
                    using var hash = System.Security.Cryptography.SHA256.Create(); sourceHash = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                } catch (Exception e) { errors.Add(e.ToString()); } finally { pending--; } });
            } else {
                phase = 3;
                cmd.SetComputeIntParam(shader, "_SdfOriginPointCount", points.Count / 2);
                cmd.SetComputeIntParam(shader, "_SdfOriginRayCount", rays);
                cmd.SetComputeIntParam(shader, "_SdfOriginSeed", 391827);
                cmd.SetComputeIntParam(shader, "_SdfOriginEmitter", emitter);
                cmd.SetComputeBufferParam(shader, trace, "_SdfOriginPoints", pointsBuffer);
                cmd.SetComputeBufferParam(shader, trace, "_SdfOriginResults", rayResults);
                cmd.SetComputeBufferParam(shader, trace, "_SdfOriginPointMetrics", pointMetrics);
                cmd.DispatchCompute(shader, trace, rays / 64, points.Count / 2, 1);
                Read(cmd, rayResults, "rays", v => { raysDone = true; });
                Read(cmd, pointMetrics, "point-metrics", v => { });
            }
        } catch (Exception e) { errors.Add(e.ToString()); phase = -1; }
    };
    async System.Threading.Tasks.Task Wait(Func<bool> done) {
        double deadline = EditorApplication.timeSinceStartup + 240;
        while ((!done() || pending != 0) && errors.Count == 0 && EditorApplication.timeSinceStartup < deadline) {
            game.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); await System.Threading.Tasks.Task.Delay(16);
        }
        if (!done() || pending != 0 || errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
    }
    Application.LogCallback onLog = (message, stack, severity) => {
        if (severity == LogType.Error || severity == LogType.Exception || severity == LogType.Assert)
            errors.Add(message + "\n" + stack);
    };
    EditorApplication.LockReloadAssemblies();
    Application.logMessageReceived += onLog;
    try {
        hook.SetValue(null, capture);
        await Wait(() => cardsDone);
        int covered = 0;
        for (int i = 0; i < cards * 64; ++i) {
            var mesh = cardValues[i*5]; var p = cardValues[i*5+1]; var n = cardValues[i*5+3]; var info = cardValues[i*5+2];
            if (mesh.w != ceiling+1 || p.w != 1 || n.y > -.99f || Mathf.Abs(mesh.x) >= .8f || mesh.z <= 1.25f || mesh.z >= 2.45f) continue;
            covered++;
            var position = new Vector3(p.x, p.y, p.z); var normal = new Vector3(n.x, n.y, n.z);
            Point("card-"+i+"-actual", position, normal, info.w, ceiling);
            Point("card-"+i+"-5mm", position, normal, .005f, ceiling);
        }
        if (covered == 0) throw new InvalidOperationException("No projected ceiling card cells under the lamp.");
        pointsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, points.Count, 16); pointsBuffer.SetData(points);
        rayResults = new GraphicsBuffer(GraphicsBuffer.Target.Structured, points.Count / 2 * rays * 4, 16);
        pointMetrics = new GraphicsBuffer(GraphicsBuffer.Target.Structured, points.Count / 2, 16);
        phase = 2; await Wait(() => raysDone); CheckSource();
        if (sourceHash != "8d1b4e596e3ef067de430082d0d25763b9df83c25b57422acbdd0e33c73d6e62")
            throw new InvalidOperationException("Canonical frozen source differs: " + sourceHash);
        var field = fields.GetValue((int)(uint)Member(instances.GetValue(ceiling), "FieldIndex"));
        var voxel = (Vector4)Member(field, "OriginAndVoxelSize");
        var result = new { schema = "sdf-ray-origin.v1", output, complete = true, cards, rays, ceiling, floor, emitter, ceilingSignMode = Member(field, "SignMode"),
            ceilingVoxel = new[] { voxel.x, voxel.y, voxel.z, voxel.w }, ceilingShellThickness = Member(field, "ShellThickness"),
            spacing = type.GetField("ProbeSpacing", stat).GetRawConstantValue(), gatherRays = type.GetField("RaysPerProbe", stat).GetRawConstantValue(),
            pointCount = points.Count/2, points = descriptions, sourceHash, sourceOrder = 8, sourceBatches = 32, revision, variants, errors };
        System.IO.File.WriteAllText(output + "/capture.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
        return new { output, complete = true, cards, pointCount = points.Count/2, covered, sourceHash, errors };
    } catch (Exception e) {
        errors.Add(e.ToString());
        throw;
    } finally {
        hook.SetValue(null, null); AsyncGPUReadback.WaitAllRequests();
        Application.logMessageReceived -= onLog;
        cardResults.Dispose(); pointsBuffer?.Dispose(); rayResults?.Dispose(); pointMetrics?.Dispose(); UnityEngine.Object.DestroyImmediate(sky);
        EditorApplication.UnlockReloadAssemblies();
        if (errors.Count > 0) System.IO.File.WriteAllText(output + "/failure.json", Newtonsoft.Json.JsonConvert.SerializeObject(errors));
    }
}
