// Isolated owned history check; does not advance or dispose the active camera's history.
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;
{
    var field = typeof(SdfDiffuseGatherPass).GetField("ProposalHistoryId",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    var id = (CameraHistoryId)field.GetValue(null);
    var descriptor = new CameraHistoryTextureDescriptor(960, 136, GraphicsFormat.R16G16B16A16_SFloat,
        filterMode: FilterMode.Point, enableRandomWrite: true);
    using var history = new CameraHistory();
    CameraHistoryTexture pair = null;
    void Step()
    {
        history.BeginFrame(1920, 1080);
        pair = history.GetOrCreateTexture(id, 2, descriptor);
        pair.GetCurrent(); pair.GetPrevious(); pair.MarkWritten(); history.CommitFrame();
    }
    Action step = Step;
    for (int i = 0; i < 256; i++) step();
    history.BeginFrame(1920, 1080);
    pair = history.GetOrCreateTexture(id, 2, descriptor);
    if (!pair.IsValid()) throw new InvalidOperationException("Proposal history did not become valid.");
    var first = pair.GetCurrent(); var second = pair.GetPrevious();
    if (System.Object.ReferenceEquals(first, second)) throw new InvalidOperationException("Proposal read/write resources alias.");
    pair.MarkWritten(); history.CommitFrame();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 4096; i++) step();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    history.BeginFrame(1920, 1080);
    pair = history.GetOrCreateTexture(id, 2, descriptor);
    bool stable = System.Object.ReferenceEquals(first, pair.GetPrevious()) && System.Object.ReferenceEquals(second, pair.GetCurrent());
    history.AbortFrame();
    if (!stable || allocated != 0) throw new InvalidOperationException("Proposal history reallocated: " + allocated + " bytes.");
    string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "VividRP_GI_Evidence",
        "ProposalHistoryValidation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
    System.IO.Directory.CreateDirectory(root);
    var report = new {root, currentThreadManagedBytes = allocated, iterations = 4096, stableOwnedTextures = stable,
        width = 960, height = 136, frameCount = 2, descriptorBytes = 2L * 960 * 136 * 8,
        scope = "CameraHistory allocation/rotation path only; excludes RenderGraph registration and whole-frame allocations"};
    System.IO.File.WriteAllText(root + "/history-validation.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
    return report;
}
