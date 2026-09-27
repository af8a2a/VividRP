using System;

namespace VividRP.AgenticDebugger
{
    internal enum PixNativeState { Idle, Preparing, Armed, Capturing, WaitingForGpu, Finalizing, Captured, Failed }

    internal sealed class PixValidationResult
    {
        public bool success { get; set; }
        public string code { get; set; }
        public string message { get; set; }
        public string sessionId { get; set; }
        public string capturePath { get; set; }
        public string expectedPass { get; set; }
        public long queueCount { get; set; }
        public long gpuWorkEventCount { get; set; }
        public bool beginMarkerFound { get; set; }
        public bool endMarkerFound { get; set; }
        public bool expectedPassFound { get; set; }
        public bool markersOrdered { get; set; }
    }

    internal interface IPixCaptureOperations : IDisposable
    {
        void Prepare();
        PixNativeState Poll(out int hresult);
        void ArmCamera();
        void StartValidation();
        bool TryGetValidation(out PixValidationResult result);
        void Cancel();
    }

    // No Unity dependency: exercises the production transition/acceptance policy
    // in an ordinary .NET process, without starting Unity Test Framework.
    internal sealed class PixCaptureSession
    {
        internal readonly string Id;
        internal readonly string Path;
        internal readonly string ExpectedPass;
        internal readonly double Deadline;
        internal string State { get; private set; } = "preparing";
        internal string Code { get; private set; } = "pending";
        internal string Message { get; private set; }
        internal int HResult { get; private set; }
        internal bool CleanupPending { get; private set; }
        internal PixValidationResult Validation { get; private set; }
        internal bool Terminal => State == "ready" || State == "failed";
        private readonly IPixCaptureOperations operations;
        private bool disposed;

        internal PixCaptureSession(string id, string path, string expectedPass, double deadline, IPixCaptureOperations operations)
        {
            Id = id;
            Path = path;
            ExpectedPass = expectedPass;
            Deadline = deadline;
            this.operations = operations;
        }

        internal void Start()
        {
            try { operations.Prepare(); }
            catch (Exception e) { Fail("prepare_failed", e.Message); }
        }

        internal void Tick(double now)
        {
            if (CleanupPending) { TryCleanup(); return; }
            if (Terminal) return;
            if (now >= Deadline) { Fail("capture_timeout", "The capture or validation deadline expired."); return; }
            try
            {
                if (State == "validating")
                {
                    if (!operations.TryGetValidation(out var result)) return;
                    Validation = result;
                    if (!Accept(result, Id, Path, ExpectedPass))
                    {
                        Fail(result?.code == null || result.code == "ok" ? "capture_validation_failed" : result.code,
                            result?.message ?? "The PIX result did not prove target-frame GPU work.");
                        return;
                    }
                    State = "ready";
                    Code = "ok";
                    DisposeOperations();
                    return;
                }
                var native = operations.Poll(out int hr);
                HResult = hr;
                if (native == PixNativeState.Failed || hr < 0)
                {
                    Fail("native_capture_failed", "PIX/native operation failed: 0x" + unchecked((uint)hr).ToString("X8"));
                    return;
                }
                switch (native)
                {
                    case PixNativeState.Preparing: State = "preparing"; break;
                    case PixNativeState.Armed:
                        if (State != "armed") operations.ArmCamera();
                        State = "armed";
                        break;
                    case PixNativeState.Capturing: State = "capturing"; break;
                    case PixNativeState.WaitingForGpu: State = "waiting_for_gpu"; break;
                    case PixNativeState.Finalizing: State = "finalizing"; break;
                    case PixNativeState.Captured:
                        operations.StartValidation();
                        State = "validating";
                        break;
                    default: Fail("invalid_native_state", "The native capture lost its session."); break;
                }
            }
            catch (Exception e) { Fail("capture_failed", e.Message); }
        }

        internal static bool Accept(PixValidationResult result, string id, string path, string pass)
            => result != null && result.success && result.code == "ok" && result.sessionId == id
                && string.Equals(result.capturePath, path, StringComparison.OrdinalIgnoreCase)
                && result.expectedPass == pass && result.queueCount > 0 && result.gpuWorkEventCount > 0
                && result.beginMarkerFound && result.endMarkerFound && result.markersOrdered && result.expectedPassFound;

        internal void Cancel(string reason) { if (!Terminal) Fail("cancelled", reason); }

        private void Fail(string code, string message)
        {
            State = "failed";
            Code = code;
            Message = message;
            CleanupPending = true;
            try { operations.Cancel(); }
            catch (Exception e) { Message += " Cleanup: " + e.Message; }
            TryCleanup();
        }

        private void TryCleanup()
        {
            try
            {
                var native = operations.Poll(out int hr);
                if (hr < 0) HResult = hr;
                if (native != PixNativeState.Failed && native != PixNativeState.Captured && native != PixNativeState.Idle) return;
                CleanupPending = false;
                DisposeOperations();
            }
            catch (Exception e)
            {
                // Keep ownership when cleanup cannot be confirmed. A new capture
                // must never race an old EndCapture or its pending render events.
                Message = "Cleanup could not be confirmed: " + e.Message;
            }
        }

        private void DisposeOperations()
        {
            if (disposed) return;
            operations.Dispose();
            disposed = true;
        }
    }
}
