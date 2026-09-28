#pragma once
#include <cstdint>
#include <string>
#include <vector>
#include <unordered_map>
#include <algorithm>

namespace vivid::pix
{
    struct Event
    {
        uint32_t index, parent;
        std::string name;
        bool work;
    };
    struct Validation
    {
        bool success = false, beginFound = false, endFound = false, ordered = false, passFound = false;
        uint64_t workCount = 0;
        unsigned validatedScopes = 0;
        std::string code = "target_frame_marker_missing";
    };

    inline bool PositiveArgument(const std::string& api, const char* argument)
    {
        std::string open = "<" + std::string(argument) + ">", close = "</" + std::string(argument) + ">";
        auto start = api.find(open);
        if (start == std::string::npos) return false;
        start += open.size();
        auto end = api.find(close, start);
        if (end == std::string::npos || end == start) return false;
        bool positive = false;
        for (auto i = start; i < end; ++i)
        {
            if (api[i] < '0' || api[i] > '9') return false;
            positive |= api[i] != '0';
        }
        return positive;
    }

    // Verified against 2606.18-preview: Name is the bare API name;
    // ApiCallData is XML for calls, empty for markers. Do not infer GPU work
    // from arbitrary marker text or from ExecuteIndirect's maximum count.
    inline bool IsWork(const std::string& name, const std::string& api)
    {
        if (api.compare(0, name.size() + 2, "<" + name + ">") != 0 ||
            api.find("<this>ID3D12GraphicsCommandList") == std::string::npos) return false;
        if (name == "DrawInstanced")
            return PositiveArgument(api, "VertexCountPerInstance") && PositiveArgument(api, "InstanceCount");
        if (name == "DrawIndexedInstanced")
            return PositiveArgument(api, "IndexCountPerInstance") && PositiveArgument(api, "InstanceCount");
        if (name == "Dispatch" || name == "DispatchMesh")
            return PositiveArgument(api, "ThreadGroupCountX") && PositiveArgument(api, "ThreadGroupCountY") && PositiveArgument(api, "ThreadGroupCountZ");
        if (name == "DispatchRays")
            return PositiveArgument(api, "Width") && PositiveArgument(api, "Height") && PositiveArgument(api, "Depth");
        return false;
    }

    inline Validation Validate(const std::vector<Event>& events, const std::string& begin,
        const std::string& end, const std::string& pass)
    {
        Validation result;
        size_t beginPos = events.size(), endPos = events.size();
        unsigned begins = 0, ends = 0;
        std::unordered_map<uint32_t, size_t> positions;
        for (size_t i = 0; i < events.size(); ++i)
        {
            if (!positions.emplace(events[i].index, i).second) { result.code = "duplicate_event_index"; return result; }
            if (events[i].name == begin) { ++begins; beginPos = i; }
            if (events[i].name == end) { ++ends; endPos = i; }
        }
        result.beginFound = begins > 0;
        result.endFound = ends > 0;
        if (begins != 1 || ends != 1)
        {
            result.code = begins > 1 || ends > 1 ? "duplicate_session_marker" : "target_frame_marker_missing";
            return result;
        }
        result.ordered = beginPos < endPos;
        if (!result.ordered) { result.code = "session_markers_reversed"; return result; }
        for (size_t i = beginPos + 1; i < endPos; ++i)
        {
            if (events[i].name == pass) result.passFound = true;
            if (!events[i].work) continue;
            uint32_t parent = events[i].parent;
            size_t childPos = i;
            // Require GPU work to be a descendant of the expected pass, inside
            // both markers on this SAME queue. Strictly decreasing ancestors
            // also reject malformed/cyclic parent chains.
            while (true)
            {
                auto found = positions.find(parent);
                if (found == positions.end() || found->second >= childPos || found->second <= beginPos) break;
                childPos = found->second;
                if (events[childPos].name == pass) { ++result.workCount; break; }
                parent = events[childPos].parent;
            }
        }
        result.success = result.passFound && result.workCount > 0;
        result.code = !result.passFound ? "expected_pass_missing" : result.workCount == 0 ? "no_target_gpu_work" : "ok";
        return result;
    }

    struct QueueEvents
    {
        bool graphics;
        std::vector<Event> events;
    };

    // Each logical queue scope must have one ordered pair on one physical
    // queue. Unity may alias compute queues, including onto the graphics queue.
    // Never infer cross-queue ordering from PIX event indices.
    inline Validation ValidateQueues(const std::vector<QueueEvents>& queues,
        const std::string& id, const std::string& pass, bool allQueues)
    {
        Validation result;
        if (queues.empty()) { result.code = "empty_capture"; return result; }
        const char* suffixes[] = { "", "/Default", "/Background", "/Urgent" };
        std::vector<uint64_t> workPerQueue(queues.size(), 0);
        for (unsigned scope = 0; scope < (allQueues ? 4u : 1u); ++scope)
        {
            const std::string begin = "VividRP.AgentCapture/" + id + "/FrameBegin" + suffixes[scope];
            const std::string end = "VividRP.AgentCapture/" + id + "/FrameEnd" + suffixes[scope];
            unsigned begins = 0, ends = 0;
            size_t beginQueue = queues.size(), endQueue = queues.size();
            for (size_t q = 0; q < queues.size(); ++q)
            {
                for (const auto& event : queues[q].events)
                {
                    if (event.name == begin) { ++begins; beginQueue = q; }
                    if (event.name == end) { ++ends; endQueue = q; }
                }
            }
            result.beginFound = begins > 0;
            result.endFound = ends > 0;
            if (begins != 1 || ends != 1)
            {
                result.code = begins > 1 || ends > 1 ? "duplicate_session_marker" : "target_frame_marker_missing";
                return result;
            }
            if (beginQueue != endQueue || (scope == 0 && !queues[beginQueue].graphics))
            {
                result.code = "session_marker_queue_mismatch";
                return result;
            }
            auto candidate = Validate(queues[beginQueue].events, begin, end, pass);
            if (!candidate.ordered || candidate.code == "duplicate_event_index") return candidate;
            ++result.validatedScopes;
            result.passFound |= candidate.passFound;
            // Aliased logical queues enclose the same work. Do not count one
            // dispatch repeatedly merely because its queue has several scopes.
            workPerQueue[beginQueue] = std::max(workPerQueue[beginQueue], candidate.workCount);
        }
        for (auto work : workPerQueue) result.workCount += work;
        result.ordered = true;
        result.success = result.passFound && result.workCount > 0;
        result.code = !result.passFound ? "expected_pass_missing" : result.workCount == 0 ? "no_target_gpu_work" : "ok";
        return result;
    }
}
