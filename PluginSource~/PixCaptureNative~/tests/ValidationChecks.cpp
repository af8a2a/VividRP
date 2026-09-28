#include "../src/Validation.h"
#include <iostream>
#include <stdexcept>
using namespace vivid::pix;

int main()
{
    unsigned checks = 0;
    auto check = [&](bool ok, const char* name) { if (!ok) throw std::runtime_error(name); ++checks; };
    const std::string begin = "VividRP.AgentCapture/abc/FrameBegin", end = "VividRP.AgentCapture/abc/FrameEnd", pass = "Lighting";
    const std::vector<Event> valid{{10, UINT32_MAX, begin, false}, {11, UINT32_MAX, pass, false},
        {12, 11, "DrawInstanced(3, 1, 0, 0)", true}, {13, UINT32_MAX, end, false}};
    auto run = [&](const std::vector<Event>& events) { return Validate(events, begin, end, pass); };
    check(run(valid).success, "valid work under expected pass");
    check(!run({}).success, "empty file/event stream");
    auto events = valid; events.erase(events.begin()); check(!run(events).success, "missing begin");
    events = valid; events.pop_back(); check(!run(events).success, "missing end");
    events = valid; std::swap(events.front().name, events.back().name); check(!run(events).success, "reversed markers");
    events = valid; events[1].name = "Other"; check(!run(events).success, "wrong pass");
    events = valid; events[2].work = false; check(!run(events).success, "marker-only capture");
    events = valid; events[2].parent = UINT32_MAX; check(!run(events).success, "work outside pass");
    events = valid; events[2].parent = 12; check(!run(events).success, "cyclic parent");
    events = valid; events.push_back({14, UINT32_MAX, begin, false}); check(!run(events).success, "duplicate session");
    events = valid; events[2].index = 11; check(!run(events).success, "duplicate event id");
    events = valid; events[0].name = "VividRP.AgentCapture/old/FrameBegin"; check(!run(events).success, "stale capture");
    const std::string args = "<this>ID3D12GraphicsCommandList obj#3</this><ThreadGroupCountX>1</ThreadGroupCountX><ThreadGroupCountY>1</ThreadGroupCountY><ThreadGroupCountZ>1</ThreadGroupCountZ>";
    check(IsWork("Dispatch", "<Dispatch>" + args + "</Dispatch>"), "real PIX dispatch XML");
    check(IsWork("DispatchMesh", "<DispatchMesh>" + args + "</DispatchMesh>"), "mesh API");
    check(!IsWork("My DrawInstanced pass", ""), "do not count marker substring");
    check(!IsWork("Dispatch", ""), "API-named marker has no call data");
    check(!IsWork("Dispatch", "<Other>" + args + "</Other>"), "name/XML mismatch");
    check(!IsWork("CopyBufferRegion", "<CopyBufferRegion>" + args + "</CopyBufferRegion>"), "copy-only is not target rendering");
    check(!IsWork("ClearRenderTargetView", "<ClearRenderTargetView>" + args + "</ClearRenderTargetView>"), "clear-only is not target rendering");
    auto zero = args; zero.replace(zero.find(">1<") + 1, 1, "0");
    check(!IsWork("Dispatch", "<Dispatch>" + zero + "</Dispatch>"), "zero-size dispatch");
    check(!IsWork("ExecuteIndirect", "<ExecuteIndirect><MaxCommandCount>1</MaxCommandCount></ExecuteIndirect>"), "indirect maximum is not actual work");
    std::vector<QueueEvents> queues{{true, {{0, UINT32_MAX, begin, false}, {1, UINT32_MAX, end, false}}}};
    for (const char* suffix : {"/Default", "/Background", "/Urgent"})
    {
        auto stream = valid;
        stream.front().name += suffix;
        stream.back().name += suffix;
        if (std::string(suffix) != "/Background") { stream[1].name = "Other"; stream[2].work = false; }
        queues.push_back({false, stream});
    }
    auto runQueues = [&](const std::vector<QueueEvents>& q) { return ValidateQueues(q, "abc", pass, true); };
    auto all = runQueues(queues);
    check(all.success && all.validatedScopes == 4 && all.workCount == 1, "work only on background queue accepted");
    auto changed = queues; changed.pop_back(); check(!runQueues(changed).success, "every required queue needs evidence");
    changed = queues; changed[2].events[2].work = false; check(runQueues(changed).code == "no_target_gpu_work", "async marker-only rejected");
    changed = queues; changed[2].events.pop_back(); check(!runQueues(changed).success, "async missing end rejected");
    changed = queues; std::swap(changed[2].events.front().name, changed[2].events.back().name);
    check(runQueues(changed).code == "session_markers_reversed", "async reversed boundary rejected");
    changed = queues; changed[3].events.push_back(changed[2].events.front());
    check(runQueues(changed).code == "duplicate_session_marker", "cross-queue duplicate rejected");
    changed = queues; changed[3].events.push_back(changed[2].events.back()); changed[2].events.pop_back();
    check(runQueues(changed).code == "session_marker_queue_mismatch", "split queue marker pair rejected");
    changed = queues; changed[0].graphics = false;
    check(runQueues(changed).code == "session_marker_queue_mismatch", "primary scope must be graphics");
    // Logical queues can alias one physical queue; all scopes enclose the work.
    std::vector<Event> aliased;
    for (const char* suffix : {"", "/Default", "/Background", "/Urgent"})
        aliased.push_back({static_cast<uint32_t>(aliased.size()), UINT32_MAX, begin + suffix, false});
    aliased.push_back({4, UINT32_MAX, pass, false}); aliased.push_back({5, 4, "Dispatch", true});
    for (const char* suffix : {"/Default", "/Background", "/Urgent", ""})
        aliased.push_back({static_cast<uint32_t>(aliased.size()), UINT32_MAX, end + suffix, false});
    all = runQueues({{true, aliased}});
    check(all.success && all.workCount == 1, "aliased queues do not duplicate work count");
    std::cout << checks << " validation checks passed\n";
}
