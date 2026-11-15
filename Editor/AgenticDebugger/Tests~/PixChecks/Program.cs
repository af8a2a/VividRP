using System;
using VividRP.AgenticDebugger;

static class Program
{
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static PixValidationResult Valid() => new PixValidationResult { success = true, code = "ok", sessionId = "abc", capturePath = "capture.wpix", expectedPass = "Lighting", queueCount = 1, gpuWorkEventCount = 3, beginMarkerFound = true, endMarkerFound = true, markersOrdered = true, expectedPassFound = true, boundaryMode = "graphics", validatedQueueScopes = 1 };
    static PixCaptureSession Session(Fake fake) => new PixCaptureSession("abc", "capture.wpix", "Lighting", 10, fake);
    static bool Accept(PixValidationResult result) => PixCaptureSession.Accept(result, "abc", "capture.wpix", "Lighting");

    static void Main()
    {
        var single = new PixCameraCaptureWindow(1);
        single.Begin(100);
        single.ObserveOtherCamera(); // Preserve the existing single-camera invocation behavior.
        Check(single.Submit(100) && single.Submitted == 1, "default window closes on one submitted camera");
        var pair = new PixCameraCaptureWindow(2);
        pair.ObserveOtherCamera(); // Preparation before the window is not captured work.
        pair.Begin(100);
        Check(!pair.Submit(100) && pair.Submitted == 1, "pair stays open after first camera");
        pair.Begin(101);
        Check(pair.Submit(101) && pair.Submitted == 2, "pair closes only after second matched camera");
        var four = new PixCameraCaptureWindow(4);
        for (int i = 0; i < 4; ++i)
        {
            four.Begin(200 + i);
            Check(four.Submit(200 + i) == (i == 3), "maximum window closes only on its fourth camera");
        }
        foreach (int invalid in new[] { 0, 5 })
        {
            bool rejected = false;
            try { _ = new PixCameraCaptureWindow(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "reject unbounded capture length");
        }
        Action<PixCameraCaptureWindow>[] invalidSequences = {
            w => w.Submit(0),
            w => { w.Begin(1); w.Submit(2); },
            w => { w.Begin(1); w.Begin(2); },
            w => { w.Begin(1); w.Submit(1); w.Begin(1); },
            w => { w.Begin(1); w.Submit(1); w.Begin(0); },
            w => { w.Begin(1); w.Submit(1); w.Begin(2); w.Submit(2); w.Begin(3); }
        };
        foreach (var invalid in invalidSequences)
        {
            bool rejected = false;
            try { invalid(new PixCameraCaptureWindow(2)); } catch (PixCaptureFailure e) { rejected = e.Code == "invalid_camera_frame_sequence"; }
            Check(rejected, "reject unmatched, duplicate, reversed or excess camera frames");
        }
        foreach (bool betweenFrames in new[] { false, true })
        {
            var mixed = new PixCameraCaptureWindow(2);
            mixed.Begin(1);
            if (betweenFrames) mixed.Submit(1);
            bool rejected = false;
            try { mixed.ObserveOtherCamera(); } catch (PixCaptureFailure e) { rejected = e.Code == "intervening_camera"; }
            Check(rejected, "reject another camera during or between captured target frames");
        }
        Check(!Accept(null), "missing validator result");
        Check(Accept(Valid()), "accept actual proof");
        Action<PixValidationResult>[] corruptions = {
            r => r.success = false, r => r.code = "failed", r => r.sessionId = "old", r => r.capturePath = "old.wpix",
            r => r.expectedPass = "Other", r => r.queueCount = 0, r => r.gpuWorkEventCount = 0,
            r => r.beginMarkerFound = false, r => r.endMarkerFound = false, r => r.markersOrdered = false, r => r.expectedPassFound = false
        };
        foreach (var corrupt in corruptions) { var r = Valid(); corrupt(r); Check(!Accept(r), "reject incomplete/stale proof"); }
        var all = Valid(); all.boundaryMode = "all"; all.validatedQueueScopes = 4;
        Check(PixCaptureSession.Accept(all, "abc", "capture.wpix", "Lighting", "all"), "async proof with all queue scopes");
        Check(!Accept(all), "reject different boundary mode");
        all.validatedQueueScopes = 3;
        Check(!PixCaptureSession.Accept(all, "abc", "capture.wpix", "Lighting", "all"), "reject missing async scope");
        var legacy = Valid(); legacy.boundaryMode = null;
        Check(!Accept(legacy), "reject outdated validator evidence");

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
        fake = new Fake { ThrowPipelineFailure = true }; session = Session(fake); session.Tick(1);
        Check(session.Code == "target_camera_rendergraphfailed" && !session.CleanupPending, "pipeline failure code survives cleanup");
        Console.WriteLine(checks + " PIX session checks passed");
    }

    sealed class Fake : IPixCaptureOperations
    {
        internal PixNativeState State = PixNativeState.Preparing;
        internal int Hr, Renders, Validations, Cancels, Disposals;
        internal bool CancelFinalizing, ThrowRender, ThrowPrepare, ThrowPipelineFailure;
        internal PixValidationResult Result;
        public void Prepare() { if (ThrowPrepare) throw new Exception("prepare"); }
        public PixNativeState Poll(out int hr)
        {
            if (ThrowPipelineFailure) { ThrowPipelineFailure = false; throw new PixCaptureFailure("target_camera_rendergraphfailed", "RenderGraph failed"); }
            hr = Hr; return State;
        }
        public void ArmCamera() { Renders++; if (ThrowRender) throw new Exception("render"); }
        public void StartValidation() { Validations++; }
        public bool TryGetValidation(out PixValidationResult result) { result = Result; return result != null; }
        public void Cancel() { Cancels++; State = CancelFinalizing ? PixNativeState.Finalizing : PixNativeState.Failed; }
        public void Dispose() { Disposals++; }
    }
}
