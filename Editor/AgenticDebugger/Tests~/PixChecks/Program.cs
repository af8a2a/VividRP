using System;
using VividRP.AgenticDebugger;

static class Program
{
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static PixValidationResult Valid() => new PixValidationResult { success = true, code = "ok", sessionId = "abc", capturePath = "capture.wpix", expectedPass = "Lighting", queueCount = 1, gpuWorkEventCount = 3, beginMarkerFound = true, endMarkerFound = true, markersOrdered = true, expectedPassFound = true };
    static PixCaptureSession Session(Fake fake) => new PixCaptureSession("abc", "capture.wpix", "Lighting", 10, fake);
    static bool Accept(PixValidationResult result) => PixCaptureSession.Accept(result, "abc", "capture.wpix", "Lighting");

    static void Main()
    {
        Check(!Accept(null), "missing validator result");
        Check(Accept(Valid()), "accept actual proof");
        Action<PixValidationResult>[] corruptions = {
            r => r.success = false, r => r.code = "failed", r => r.sessionId = "old", r => r.capturePath = "old.wpix",
            r => r.expectedPass = "Other", r => r.queueCount = 0, r => r.gpuWorkEventCount = 0,
            r => r.beginMarkerFound = false, r => r.endMarkerFound = false, r => r.markersOrdered = false, r => r.expectedPassFound = false
        };
        foreach (var corrupt in corruptions) { var r = Valid(); corrupt(r); Check(!Accept(r), "reject incomplete/stale proof"); }

        var fake = new Fake(); var session = Session(fake); session.Start();
        session.Tick(1); Check(session.State == "preparing" && fake.Renders == 0, "no render before drain");
        fake.State = PixNativeState.Armed; session.Tick(2); Check(fake.Renders == 1 && session.State == "armed", "subscribe after drain");
        session.Tick(3); Check(fake.Renders == 1 && fake.Validations == 0, "no repeated subscriptions/no early validate");
        fake.State = PixNativeState.Finalizing; session.Tick(4); Check(session.State == "finalizing" && fake.Validations == 0, "wait for native end");
        fake.State = PixNativeState.Captured; session.Tick(5); Check(session.State == "validating" && fake.Validations == 1, "begin validation only after end");
        session.Tick(6); Check(!session.Terminal, "validator pending is not ready");
        fake.Result = Valid(); session.Tick(7); Check(session.State == "ready" && fake.Disposals == 1, "validated ready/disposed");
        session.Tick(11); Check(session.State == "ready" && fake.Disposals == 1, "completed result retained");

        fake = new Fake { Hr = unchecked((int)0x80004005) }; session = Session(fake); session.Start(); session.Tick(1);
        Check(session.State == "failed" && fake.Cancels == 1, "negative HRESULT fails");
        fake = new Fake { Hr = 1, State = PixNativeState.Armed }; session = Session(fake); session.Tick(1);
        Check(fake.Renders == 1, "S_FALSE is a successful HRESULT");
        fake = new Fake(); session = Session(fake); session.Tick(10);
        Check(session.Code == "capture_timeout" && fake.Cancels == 1, "absolute timeout");
        fake = new Fake { CancelFinalizing = true }; session = Session(fake); session.Cancel("reload");
        Check(session.CleanupPending && fake.Disposals == 0, "retain native ownership while cancelling");
        fake.State = PixNativeState.Failed; session.Tick(11);
        Check(!session.CleanupPending && fake.Disposals == 1, "cancel completion releases resources");
        fake = new Fake { ThrowRender = true, State = PixNativeState.Armed }; session = Session(fake); session.Tick(1);
        Check(session.State == "failed" && fake.Cancels == 1, "render exception closes capture");
        fake = new Fake { ThrowPrepare = true }; session = Session(fake); session.Start();
        Check(session.Code == "prepare_failed" && fake.Disposals == 1, "prepare failure cleanup");
        fake = new Fake { State = PixNativeState.Captured, Result = Valid() }; fake.Result.sessionId = "stale";
        session = Session(fake); session.Tick(1); session.Tick(2);
        Check(session.Code == "capture_validation_failed", "reject stale validator success");
        fake = new Fake { State = PixNativeState.Captured }; session = Session(fake); session.Tick(1); session.Tick(10);
        Check(session.Code == "capture_timeout" && fake.Cancels == 1, "hung validator cancellation");
        Console.WriteLine(checks + " PIX session checks passed");
    }

    sealed class Fake : IPixCaptureOperations
    {
        internal PixNativeState State = PixNativeState.Preparing;
        internal int Hr, Renders, Validations, Cancels, Disposals;
        internal bool CancelFinalizing, ThrowRender, ThrowPrepare;
        internal PixValidationResult Result;
        public void Prepare() { if (ThrowPrepare) throw new Exception("prepare"); }
        public PixNativeState Poll(out int hr) { hr = Hr; return State; }
        public void ArmCamera() { Renders++; if (ThrowRender) throw new Exception("render"); }
        public void StartValidation() { Validations++; }
        public bool TryGetValidation(out PixValidationResult result) { result = Result; return result != null; }
        public void Cancel() { Cancels++; State = CancelFinalizing ? PixNativeState.Finalizing : PixNativeState.Failed; }
        public void Dispose() { Disposals++; }
    }
}
