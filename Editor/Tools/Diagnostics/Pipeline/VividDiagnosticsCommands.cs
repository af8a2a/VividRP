using Unity.Pipeline.Commands;

namespace VividRP.Editor
{
    public static class VividDiagnosticsCommands
    {
        [CliCommand("vivid_diagnostics", "VividRP snapshot, GPU stage profile and raw VSM capture; JSON request/result.",
            MainThreadRequired = true, Tags = new[] { "vividrp/diagnostics" })]
        public static string Execute([CliArg("request", "JSON with action: cameras, snapshot, profile, capture, status or cancel.")] string request = "{\"action\":\"status\"}")
            => VividDiagnostics.Execute(request);
    }
}
