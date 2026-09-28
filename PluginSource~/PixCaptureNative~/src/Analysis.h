// Included inside Analyzer.cpp's private namespace: the capture plugin never
// loads PIX analysis interfaces. All inspection/replay happens in this process.
struct ObjectJson
{
    std::string value = "{";
    void Add(const char* key, const std::string& json)
    { if (value.size() > 1) value += ','; value += Json(key) + ':' + json; }
    std::string Finish() const { return value + '}'; }
};
std::string ArrayJson(const std::vector<std::string>& rows)
{
    std::string value = "[";
    for (const auto& row : rows) { if (value.size() > 1) value += ','; value += row; }
    return value + ']';
}
std::string Text(LPCWSTR text) { return text ? Utf8(text) : ""; }
std::string Limited(const std::string& text, size_t limit = 4096)
{
    size_t end = std::min(text.size(), limit);
    while (end < text.size() && end > 0 && (static_cast<unsigned char>(text[end]) & 0xc0) == 0x80) --end;
    return text.substr(0, end);
}
std::string String(LPCWSTR text) { return Json(Limited(Text(text))); }
std::string Number(UINT64 value) { return std::to_string(value); }
std::string Real(float value) { return std::isfinite(value) ? std::to_string(value) : "null"; }
std::string Id(UINT64 value) { return Json(Number(value)); }
std::string OptionalTime(UINT64 value) { return value == PIX_EVENT_TIMING_NONE ? "null" : Id(value); }

struct AnalysisOptions
{
    std::string action, marker, requestId, expectedHash;
    std::wstring experiment;
    int64_t queue = -1, event = -1, counter = -1, type = -1, stage = -1;
    UINT64 offset = 0, count = 100;
    DWORD timeout = 120;
};
int64_t ParseInteger(const std::wstring& value, int64_t minimum, int64_t maximum)
{
    size_t end = 0;
    int64_t number;
    try { number = std::stoll(value, &end); }
    catch (...) { throw Error{E_INVALIDARG, "invalid_numeric_argument"}; }
    if (end != value.size() || number < minimum || number > maximum) throw Error{E_INVALIDARG, "invalid_numeric_argument"};
    return number;
}
AnalysisOptions ParseOptions(int argc, wchar_t** argv)
{
    AnalysisOptions o; o.action = Utf8(argv[1]);
    const std::vector<std::string> actions{"events", "event", "pipeline", "resources", "accessed_resources", "timing", "counters", "occupancy", "drpix"};
    if (std::find(actions.begin(), actions.end(), o.action) == actions.end()) throw Error{E_INVALIDARG, "unsupported_action"};
    std::unordered_map<std::wstring, bool> seen;
    for (int i = 8; i < argc; i += 2)
    {
        if (i + 1 >= argc || !seen.emplace(argv[i], true).second) throw Error{E_INVALIDARG, "invalid_options"};
        std::wstring key(argv[i]), v(argv[i + 1]);
        if (key == L"--marker") o.marker = Utf8(v);
        else if (key == L"--request_id") o.requestId = Utf8(v);
        else if (key == L"--capture_hash") o.expectedHash = Utf8(v);
        else if (key == L"--experiment") o.experiment = v;
        else if (key == L"--queue") o.queue = ParseInteger(v, 0, UINT32_MAX);
        else if (key == L"--event") o.event = ParseInteger(v, 0, UINT32_MAX);
        else if (key == L"--counter") o.counter = ParseInteger(v, 0, UINT32_MAX);
        else if (key == L"--type") o.type = ParseInteger(v, 0, UINT32_MAX);
        else if (key == L"--stage") o.stage = ParseInteger(v, 0, UINT32_MAX);
        else if (key == L"--offset") o.offset = ParseInteger(v, 0, UINT32_MAX);
        else if (key == L"--count") o.count = ParseInteger(v, 1, 256);
        else if (key == L"--timeout_seconds") o.timeout = static_cast<DWORD>(ParseInteger(v, 1, 300));
        else throw Error{E_INVALIDARG, "unknown_option"};
    }
    if (o.event >= 0 && o.queue < 0) throw Error{E_INVALIDARG, "queue_required"};
    if ((o.action == "event" || o.action == "pipeline" || o.action == "timing" || o.action == "accessed_resources" ||
         o.counter >= 0 || !o.experiment.empty()) && o.event < 0) throw Error{E_INVALIDARG, "event_required"};
    if ((o.type >= 0) != (o.stage >= 0)) throw Error{E_INVALIDARG, "occupancy_type_and_stage_required"};
    if (!o.expectedHash.empty())
    {
        if (o.expectedHash.size() != 64 || o.expectedHash.find_first_not_of("0123456789abcdefABCDEF") != std::string::npos)
            throw Error{E_INVALIDARG, "invalid_capture_hash"};
        std::transform(o.expectedHash.begin(), o.expectedHash.end(), o.expectedHash.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    }
    return o;
}
std::string Page(const std::vector<std::string>& rows, UINT64 total, const AnalysisOptions& o)
{
    ObjectJson page; page.Add("items", ArrayJson(rows)); page.Add("total", Number(total));
    page.Add("offset", Number(o.offset));
    auto next = std::min(total, o.offset + rows.size());
    page.Add("nextOffset", next < total ? Number(next) : "null"); return page.Finish();
}
struct QueueData
{
    ComPtr<IPixGpuCaptureQueueInfo> info;
    std::vector<PIX_EVENT_INFO> events;
    std::vector<bool> inside;
};
struct CaptureDocument
{
    ComPtr<IPixFactory> factory;
    ComPtr<IPixGpuCaptureDocument> document;
    std::vector<QueueData> queues;
    PIX_QUEUE_TYPE QueueType(UINT32 id) const
    {
        for (const auto& queue : queues) if (queue.info->GetId() == id) return queue.info->GetType();
        throw Error{E_INVALIDARG, "queue_not_found"};
    }
    void Open(const fs::path& pix, const fs::path& path, const std::string& session, const std::string& pass, bool all)
    {
        factory = CreateFactory(pix);
        Check(factory->OpenGpuCaptureDocument(path.c_str(), IID_PPV_ARGS(&document)), "open_capture_failed");
        ComPtr<IPixCollection> collection;
        Check(document->GetQueues(IID_PPV_ARGS(&collection)), "get_queues_failed");
        std::vector<vivid::pix::QueueEvents> proof;
        for (UINT64 q = 0; q < collection->GetCount(); ++q)
        {
            QueueData queue;
            Check(collection->Get(q, IID_PPV_ARGS(&queue.info)), "get_queue_failed");
            vivid::pix::QueueEvents evidence{queue.info->GetType() == PIX_QUEUE_TYPE_GRAPHICS, {}};
            for (UINT i = 0; i < queue.info->GetEventCount(); ++i)
            {
                PIX_EVENT_INFO event{};
                Check(queue.info->GetEvent(i, &event), "get_event_failed");
                queue.events.push_back(event);
                evidence.events.push_back({event.Index, event.ParentIndex, event.Name ? event.Name : "",
                    vivid::pix::IsWork(event.Name ? event.Name : "", event.ApiCallData ? event.ApiCallData : "")});
            }
            queue.inside.resize(queue.events.size(), false);
            for (const char* suffix : {"", "/Default", "/Background", "/Urgent"})
            {
                if (!all && *suffix) continue;
                std::string begin = "VividRP.AgentCapture/" + session + "/FrameBegin" + suffix;
                std::string end = "VividRP.AgentCapture/" + session + "/FrameEnd" + suffix;
                bool inside = false;
                for (size_t i = 0; i < queue.events.size(); ++i)
                {
                    std::string name = queue.events[i].Name ? queue.events[i].Name : "";
                    if (name == begin) inside = true;
                    queue.inside[i] = queue.inside[i] || inside;
                    if (name == end) inside = false;
                }
            }
            proof.push_back(std::move(evidence)); queues.push_back(std::move(queue));
        }
        auto validation = vivid::pix::ValidateQueues(proof, session, pass, all);
        if (!validation.success) throw Error{E_FAIL, "capture_validation_failed"};
    }
    PIX_EVENT_INFO Find(const AnalysisOptions& o)
    {
        for (auto& queue : queues)
            if (queue.info->GetId() == o.queue)
                for (size_t i = 0; i < queue.events.size(); ++i)
                    if (queue.events[i].Index == o.event)
                    {
                        if (!queue.inside[i]) throw Error{E_INVALIDARG, "event_outside_capture"};
                        return queue.events[i];
                    }
        throw Error{E_INVALIDARG, "event_not_found"};
    }
};
std::string EventJson(const PIX_EVENT_INFO& e, UINT32 queueId, PIX_QUEUE_TYPE queueType, bool details)
{
    ObjectJson row; row.Add("queueId", Number(queueId)); row.Add("eventIndex", Number(e.Index));
    row.Add("queueType", Number(queueType));
    row.Add("parentIndex", e.ParentIndex == UINT32_MAX ? "null" : Number(e.ParentIndex));
    row.Add("name", Json(Limited(e.Name ? e.Name : "")));
    row.Add("gpuWork", vivid::pix::IsWork(e.Name ? e.Name : "", e.ApiCallData ? e.ApiCallData : "") ? "true" : "false");
    row.Add("commandListId", Number(e.CommandListId));
    if (details) { row.Add("apiCallData", Json(Limited(e.ApiCallData ? e.ApiCallData : "", 16384)));
        row.Add("apiCallDataTruncated", e.ApiCallData && strlen(e.ApiCallData) > 16384 ? "true" : "false"); }
    return row.Finish();
}
std::string Events(CaptureDocument& capture, const AnalysisOptions& o)
{
    UINT64 total = 0; std::vector<std::string> rows;
    bool foundQueue = o.queue < 0;
    for (const auto& queue : capture.queues)
    {
        if (o.queue >= 0 && queue.info->GetId() != o.queue) continue;
        foundQueue = true;
        std::unordered_map<UINT32, size_t> positions;
        for (size_t i = 0; i < queue.events.size(); ++i) positions.emplace(queue.events[i].Index, i);
        for (size_t i = 0; i < queue.events.size(); ++i)
        {
            if (!queue.inside[i]) continue;
            bool match = o.marker.empty(); size_t p = i;
            while (!match)
            {
                if (queue.events[p].Name && o.marker == queue.events[p].Name) { match = true; break; }
                auto it = positions.find(queue.events[p].ParentIndex);
                if (it == positions.end() || it->second >= p || !queue.inside[it->second]) break;
                p = it->second;
            }
            if (!match) continue;
            if (total++ >= o.offset && rows.size() < o.count) rows.push_back(EventJson(queue.events[i], queue.info->GetId(), queue.info->GetType(), false));
        }
    }
    if (!foundQueue) throw Error{E_INVALIDARG, "queue_not_found"};
    return Page(rows, total, o);
}
std::string ResourceJson(IPixD3D12Resource* resource)
{
    ObjectJson r; r.Add("resourceId", Id(resource->GetApiObjectId())); r.Add("name", String(resource->GetName()));
    r.Add("allocationType", Number(resource->GetType()));
    auto d = resource->GetResourceDesc();
    if (!d) throw Error{E_FAIL, "resource_descriptor_missing"};
    r.Add("dimension", Number(d->Dimension)); r.Add("width", Id(d->Width)); r.Add("height", Number(d->Height));
    r.Add("depthOrArraySize", Number(d->DepthOrArraySize)); r.Add("mipLevels", Number(d->MipLevels));
    r.Add("format", Number(d->Format)); r.Add("sampleCount", Number(d->SampleDesc.Count)); r.Add("flags", Number(d->Flags));
    return r.Finish();
}
template<class T> std::string RootParameters(const T& d)
{
    std::vector<std::string> rows;
    for (UINT i = 0; i < std::min(d.NumParameters, 64u); ++i)
    {
        const auto& p = d.pParameters[i]; ObjectJson r;
        r.Add("index", Number(i)); r.Add("type", Number(p.ParameterType)); r.Add("visibility", Number(p.ShaderVisibility));
        if (p.ParameterType == D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE)
        {
            std::vector<std::string> ranges;
            for (UINT j = 0; j < std::min(p.DescriptorTable.NumDescriptorRanges, 64u); ++j)
            {
                auto& range = p.DescriptorTable.pDescriptorRanges[j]; ObjectJson x;
                x.Add("type", Number(range.RangeType)); x.Add("count", Number(range.NumDescriptors));
                x.Add("baseRegister", Number(range.BaseShaderRegister)); x.Add("space", Number(range.RegisterSpace));
                x.Add("offset", Number(range.OffsetInDescriptorsFromTableStart)); ranges.push_back(x.Finish());
            }
            r.Add("rangeCount", Number(p.DescriptorTable.NumDescriptorRanges)); r.Add("ranges", ArrayJson(ranges));
        }
        else if (p.ParameterType == D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS)
        {
            r.Add("register", Number(p.Constants.ShaderRegister)); r.Add("space", Number(p.Constants.RegisterSpace));
            r.Add("valueCount", Number(p.Constants.Num32BitValues));
        }
        else { r.Add("register", Number(p.Descriptor.ShaderRegister)); r.Add("space", Number(p.Descriptor.RegisterSpace)); }
        rows.push_back(r.Finish());
    }
    ObjectJson root; root.Add("flags", Number(d.Flags)); root.Add("parameterCount", Number(d.NumParameters));
    root.Add("parameters", ArrayJson(rows)); root.Add("staticSamplerCount", Number(d.NumStaticSamplers));
    rows.clear();
    for (UINT i = 0; i < std::min(d.NumStaticSamplers, 64u); ++i)
    {
        const auto& s = d.pStaticSamplers[i]; ObjectJson r;
        r.Add("register", Number(s.ShaderRegister)); r.Add("space", Number(s.RegisterSpace)); r.Add("visibility", Number(s.ShaderVisibility));
        r.Add("filter", Number(s.Filter)); r.Add("addressU", Number(s.AddressU)); r.Add("addressV", Number(s.AddressV)); r.Add("addressW", Number(s.AddressW));
        r.Add("mipLodBias", Real(s.MipLODBias)); r.Add("minLod", Real(s.MinLOD)); r.Add("maxLod", Real(s.MaxLOD));
        r.Add("maxAnisotropy", Number(s.MaxAnisotropy)); r.Add("comparison", Number(s.ComparisonFunc)); r.Add("borderColor", Number(s.BorderColor));
        rows.push_back(r.Finish());
    }
    root.Add("staticSamplers", ArrayJson(rows)); return root.Finish();
}
std::string RootSignature(IPixGpuProgram* program)
{
    ComPtr<IPixRootSignature> root;
    Check(program->GetGlobalRootSignature(IID_PPV_ARGS(&root)), "get_root_signature_failed");
    if (!root) return "null";
    auto d = root->GetDesc(); if (!d) throw Error{E_FAIL, "root_descriptor_missing"};
    ObjectJson r; r.Add("id", Id(root->GetApiObjectId())); r.Add("name", String(root->GetName())); r.Add("version", Number(d->Version));
    if (d->Version == D3D_ROOT_SIGNATURE_VERSION_1_0) r.Add("description", RootParameters(d->Desc_1_0));
    else if (d->Version == D3D_ROOT_SIGNATURE_VERSION_1_1) r.Add("description", RootParameters(d->Desc_1_1));
    else if (d->Version == D3D_ROOT_SIGNATURE_VERSION_1_2) r.Add("description", RootParameters(d->Desc_1_2));
    else throw Error{E_NOTIMPL, "root_signature_version_unsupported"};
    return r.Finish();
}
ComPtr<IPixGpuProgram> ProgramAt(CaptureDocument& c, const AnalysisOptions& o)
{
    auto e = c.Find(o); ComPtr<IPixProgramState> state; ComPtr<IPixGpuProgram> program;
    Check(c.document->GetProgramState(&e, IID_PPV_ARGS(&state)), "get_program_state_failed");
    Check(state->GetGpuProgram(IID_PPV_ARGS(&program)), "get_gpu_program_failed");
    if (!program) throw Error{E_FAIL, "no_gpu_program"};
    return program;
}
std::string ShaderHash(IPixShader* shader, bool pdb)
{
    auto size = pdb ? shader->GetPdbHashSizeBytes() : shader->GetHashSizeBytes();
    if (!size) return "null";
    if (size > 1024) throw Error{E_FAIL, "invalid_shader_hash_size"};
    std::vector<BYTE> bytes(size);
    HRESULT hr = pdb ? shader->GetPdbHash(size, bytes.data()) : shader->GetHash(size, bytes.data());
    if (FAILED(hr)) return "null"; // DXBC has no DXIL/PDB digest in this API.
    std::ostringstream out; for (auto b : bytes) out << std::hex << std::setw(2) << std::setfill('0') << unsigned(b);
    return Json(out.str());
}
std::string Pipeline(CaptureDocument& c, const AnalysisOptions& o)
{
    auto program = ProgramAt(c, o); ObjectJson r;
    r.Add("programType", Number(program->GetType())); r.Add("rootSignature", RootSignature(program.Get()));
    ComPtr<IPixCollection> shaders; Check(program->GetShaders(IID_PPV_ARGS(&shaders)), "get_shaders_failed");
    std::vector<std::string> rows;
    for (UINT64 i = 0; i < std::min(shaders->GetCount(), UINT64(256)); ++i)
    {
        ComPtr<IPixShader> shader; Check(shaders->Get(i, IID_PPV_ARGS(&shader)), "get_shader_failed"); ObjectJson s;
        s.Add("id", Id(shader->GetId())); s.Add("stage", Number(shader->GetStage()));
        s.Add("entry", String(shader->GetEntry())); s.Add("target", String(shader->GetTarget()));
        s.Add("sizeBytes", shader->GetSize() ? Id(shader->GetSize()) : "null"); s.Add("dxilHash", ShaderHash(shader.Get(), false));
        s.Add("pdbHash", ShaderHash(shader.Get(), true)); rows.push_back(s.Finish());
    }
    r.Add("shaderCount", Number(shaders->GetCount())); r.Add("shaders", ArrayJson(rows));
    ComPtr<IPixGenericPipeline> generic;
    HRESULT genericHr = program.As(&generic);
    if (SUCCEEDED(genericHr))
    {
        PIX_GENERIC_PIPELINE_TYPE type; Check(generic->GetPipelineType(&type), "get_pipeline_type_failed");
        r.Add("pipelineType", Number(type));
        ComPtr<IPixPipelineState> state; Check(generic->GetPipelineState(IID_PPV_ARGS(&state)), "get_pipeline_state_failed");
        rows.clear();
        for (UINT64 i = 0; i < std::min(state->GetSubobjectCount(), UINT64(256)); ++i)
        {
            D3D12_STATE_SUBOBJECT sub{}; Check(state->GetSubobject(i, &sub), "get_pipeline_subobject_failed");
            ObjectJson s; s.Add("type", Number(sub.Type)); bool described = false;
            // Never serialize pDesc addresses; enum identity is available for
            // every subobject, with typed fixed-function data where meaningful.
            if (sub.pDesc && sub.Type == D3D12_STATE_SUBOBJECT_TYPE_SAMPLE_MASK) { s.Add("sampleMask", Number(*static_cast<const UINT*>(sub.pDesc))); described = true; }
            if (sub.pDesc && sub.Type == D3D12_STATE_SUBOBJECT_TYPE_PRIMITIVE_TOPOLOGY) { s.Add("topologyType", Number(*static_cast<const D3D12_PRIMITIVE_TOPOLOGY_TYPE*>(sub.pDesc))); described = true; }
            if (sub.pDesc && sub.Type == D3D12_STATE_SUBOBJECT_TYPE_NODE_MASK) { s.Add("nodeMask", Number(*static_cast<const UINT*>(sub.pDesc))); described = true; }
            if (sub.pDesc && sub.Type == D3D12_STATE_SUBOBJECT_TYPE_FLAGS) { s.Add("flags", Number(*static_cast<const D3D12_PIPELINE_STATE_FLAGS*>(sub.pDesc))); described = true; }
            if (sub.pDesc && sub.Type == D3D12_STATE_SUBOBJECT_TYPE_DEPTH_STENCIL_FORMAT) { s.Add("format", Number(*static_cast<const DXGI_FORMAT*>(sub.pDesc))); described = true; }
            if (sub.pDesc && sub.Type == D3D12_STATE_SUBOBJECT_TYPE_SAMPLE_DESC)
            { auto d = static_cast<const DXGI_SAMPLE_DESC*>(sub.pDesc); s.Add("count", Number(d->Count)); s.Add("quality", Number(d->Quality)); described = true; }
            if (sub.pDesc && sub.Type == D3D12_STATE_SUBOBJECT_TYPE_RENDER_TARGET_FORMATS)
            {
                auto d = static_cast<const D3D12_RT_FORMAT_ARRAY*>(sub.pDesc); std::vector<std::string> formats;
                for (UINT j = 0; j < std::min(d->NumRenderTargets, 8u); ++j) formats.push_back(Number(d->RTFormats[j]));
                s.Add("formats", ArrayJson(formats)); described = true;
            }
            s.Add("detailsAvailable", described ? "true" : "false");
            rows.push_back(s.Finish());
        }
        r.Add("subobjectCount", Number(state->GetSubobjectCount())); r.Add("subobjects", ArrayJson(rows));
    }
    else if (genericHr != E_NOINTERFACE) Check(genericHr, "get_generic_pipeline_failed");
    return r.Finish();
}

struct Replay
{
    ComPtr<IPixGpuCaptureAnalysis> analysis;
    explicit Replay(CaptureDocument& c)
    {
        Check(c.document->GetAnalysis(IID_PPV_ARGS(&analysis)), "get_analysis_failed");
        PIX_CONNECTION_DESC_LOCAL local{}; PIX_CONNECTION_DESC connection{};
        connection.Type = PIX_CONNECTION_TYPE_LOCAL; connection.pLocal = &local;
        Check(analysis->Connect(&connection, nullptr), "replay_connect_failed");
        // Defaults preserve the captured adapter and ordinary power policy.
        // No ignore-incompatibility flag or forced single-queue replay.
        try { Check(analysis->StartAnalysis(nullptr, nullptr, nullptr), "replay_start_failed"); }
        catch (...) { analysis->Disconnect(); throw; }
    }
    ~Replay() { if (analysis) { analysis->StopAnalysis(); analysis->Disconnect(); } }
};
std::string ViewDescription(IPixResourceView* view)
{
    ObjectJson r; HRESULT hr = E_NOINTERFACE; bool supported = true;
    ComPtr<IPixUnorderedAccessView> uav; ComPtr<IPixShaderResourceView> srv; ComPtr<IPixConstantBufferView> cbv;
    ComPtr<IPixRenderTargetView> rtv; ComPtr<IPixDepthStencilView> dsv; ComPtr<IPixVertexBufferView> vbv; ComPtr<IPixIndexBufferView> ibv;
    if (SUCCEEDED(view->QueryInterface(IID_PPV_ARGS(&uav))))
    {
        const D3D12_UNORDERED_ACCESS_VIEW_DESC* d = nullptr; hr = uav->GetDesc(&d);
        if (SUCCEEDED(hr) && d)
        {
            r.Add("format", Number(d->Format)); r.Add("dimension", Number(d->ViewDimension));
            if (d->ViewDimension == D3D12_UAV_DIMENSION_BUFFER)
            { r.Add("firstElement", Id(d->Buffer.FirstElement)); r.Add("numElements", Number(d->Buffer.NumElements)); r.Add("stride", Number(d->Buffer.StructureByteStride)); r.Add("flags", Number(d->Buffer.Flags)); r.Add("counterOffsetBytes", Id(d->Buffer.CounterOffsetInBytes)); }
            else if (d->ViewDimension == D3D12_UAV_DIMENSION_TEXTURE2D)
            { r.Add("mipSlice", Number(d->Texture2D.MipSlice)); r.Add("planeSlice", Number(d->Texture2D.PlaneSlice)); }
            else if (d->ViewDimension == D3D12_UAV_DIMENSION_TEXTURE2DARRAY)
            { r.Add("mipSlice", Number(d->Texture2DArray.MipSlice)); r.Add("planeSlice", Number(d->Texture2DArray.PlaneSlice)); r.Add("firstArraySlice", Number(d->Texture2DArray.FirstArraySlice)); r.Add("arraySize", Number(d->Texture2DArray.ArraySize)); }
            else if (d->ViewDimension == D3D12_UAV_DIMENSION_TEXTURE3D)
            { r.Add("mipSlice", Number(d->Texture3D.MipSlice)); r.Add("firstWSlice", Number(d->Texture3D.FirstWSlice)); r.Add("wSize", Number(d->Texture3D.WSize)); }
        }
        else if (SUCCEEDED(hr)) hr = E_FAIL;
    }
    else if (SUCCEEDED(view->QueryInterface(IID_PPV_ARGS(&srv))))
    {
        const D3D12_SHADER_RESOURCE_VIEW_DESC* d = nullptr; hr = srv->GetDesc(&d);
        if (SUCCEEDED(hr) && d)
        {
            r.Add("format", Number(d->Format)); r.Add("dimension", Number(d->ViewDimension)); r.Add("componentMapping", Number(d->Shader4ComponentMapping));
            if (d->ViewDimension == D3D12_SRV_DIMENSION_BUFFER)
            { r.Add("firstElement", Id(d->Buffer.FirstElement)); r.Add("numElements", Number(d->Buffer.NumElements)); r.Add("stride", Number(d->Buffer.StructureByteStride)); r.Add("flags", Number(d->Buffer.Flags)); }
            else if (d->ViewDimension == D3D12_SRV_DIMENSION_TEXTURE2D)
            { r.Add("mostDetailedMip", Number(d->Texture2D.MostDetailedMip)); r.Add("mipLevels", Number(d->Texture2D.MipLevels)); r.Add("planeSlice", Number(d->Texture2D.PlaneSlice)); r.Add("minLodClamp", Real(d->Texture2D.ResourceMinLODClamp)); }
            else if (d->ViewDimension == D3D12_SRV_DIMENSION_TEXTURE2DARRAY)
            { r.Add("mostDetailedMip", Number(d->Texture2DArray.MostDetailedMip)); r.Add("mipLevels", Number(d->Texture2DArray.MipLevels)); r.Add("planeSlice", Number(d->Texture2DArray.PlaneSlice)); r.Add("firstArraySlice", Number(d->Texture2DArray.FirstArraySlice)); r.Add("arraySize", Number(d->Texture2DArray.ArraySize)); r.Add("minLodClamp", Real(d->Texture2DArray.ResourceMinLODClamp)); }
        }
        else if (SUCCEEDED(hr)) hr = E_FAIL;
    }
    else if (SUCCEEDED(view->QueryInterface(IID_PPV_ARGS(&cbv))))
    {
        const D3D12_CONSTANT_BUFFER_VIEW_DESC* d = nullptr; hr = cbv->GetDesc(&d);
        if (SUCCEEDED(hr) && d) { r.Add("sizeBytes", Number(d->SizeInBytes)); r.Add("offsetBytes", Id(cbv->GetBufferLocationOffset())); }
        else if (SUCCEEDED(hr)) hr = E_FAIL;
    }
    else if (SUCCEEDED(view->QueryInterface(IID_PPV_ARGS(&rtv))))
    { auto d = rtv->GetDesc(); hr = d ? S_OK : E_FAIL; if (d) { r.Add("format", Number(d->Format)); r.Add("dimension", Number(d->ViewDimension)); } }
    else if (SUCCEEDED(view->QueryInterface(IID_PPV_ARGS(&dsv))))
    { auto d = dsv->GetDesc(); hr = d ? S_OK : E_FAIL; if (d) { r.Add("format", Number(d->Format)); r.Add("dimension", Number(d->ViewDimension)); r.Add("flags", Number(d->Flags)); } }
    else if (SUCCEEDED(view->QueryInterface(IID_PPV_ARGS(&vbv))))
    { auto d = vbv->GetDesc(); hr = d ? S_OK : E_FAIL; if (d) { r.Add("sizeBytes", Number(d->SizeInBytes)); r.Add("stride", Number(d->StrideInBytes)); r.Add("offsetBytes", Id(vbv->GetBufferLocationOffset())); } }
    else if (SUCCEEDED(view->QueryInterface(IID_PPV_ARGS(&ibv))))
    { auto d = ibv->GetDesc(); hr = d ? S_OK : E_FAIL; if (d) { r.Add("sizeBytes", Number(d->SizeInBytes)); r.Add("format", Number(d->Format)); r.Add("offsetBytes", Id(ibv->GetBufferLocationOffset())); } }
    else supported = false;
    // Null/uninitialized views can legitimately lack a descriptor. Keep their
    // bindings and exact HRESULT instead of fabricating a zero descriptor.
    r.Add("supported", supported ? "true" : "false"); r.Add("available", SUCCEEDED(hr) ? "true" : "false");
    r.Add("hresult", std::to_string(static_cast<int32_t>(hr))); return r.Finish();
}
std::string Resources(CaptureDocument& c, const AnalysisOptions& o)
{
    std::vector<std::string> rows;
    if (o.event < 0)
    {
        ComPtr<IPixD3D12Resources> resources;
        Check(c.document->GetD3D12Resources(IID_PPV_ARGS(&resources)), "get_resources_failed");
        for (UINT64 i = o.offset; i < std::min(UINT64(resources->GetCount()), o.offset + o.count); ++i)
        {
            ComPtr<IPixD3D12Resource> r;
            Check(resources->GetD3D12Resource(static_cast<UINT32>(i), IID_PPV_ARGS(&r)), "get_resource_failed");
            rows.push_back(ResourceJson(r.Get()));
        }
        return Page(rows, resources->GetCount(), o);
    }
    auto program = ProgramAt(c, o); ComPtr<IPixGenericPipeline> pipeline;
    Check(program.As(&pipeline), "resource_views_program_unsupported");
    ComPtr<IPixResourceViews> views;
    Check(pipeline->GetResourceViews(IID_PPV_ARGS(&views)), "get_resource_views_failed");
    std::unique_ptr<Replay> replay;
    if (o.action == "accessed_resources")
    {
        replay = std::make_unique<Replay>(c);
        Check(replay->analysis->GatherAccessedResources(), "gather_accessed_resources_failed");
        Check(replay->analysis->GetAccessedResources(views.Get()), "get_accessed_resources_failed");
    }
    for (UINT64 i = o.offset; i < std::min(UINT64(views->GetCount()), o.offset + o.count); ++i)
    {
        ComPtr<IPixResourceView> view; Check(views->GetView(static_cast<UINT32>(i), IID_PPV_ARGS(&view)), "get_view_failed");
        ObjectJson r; r.Add("viewIndex", Number(i)); r.Add("type", Number(view->GetType())); r.Add("description", ViewDescription(view.Get()));
        ComPtr<IPixD3D12ResourceView> resourceView;
        HRESULT resourceHr = view.As(&resourceView);
        if (SUCCEEDED(resourceHr))
        {
            ComPtr<IPixD3D12Resource> resource; HRESULT hr = resourceView->GetD3D12Resource(IID_PPV_ARGS(&resource));
            r.Add("resourceHresult", std::to_string(static_cast<int32_t>(hr)));
            r.Add("resource", SUCCEEDED(hr) && resource ? ResourceJson(resource.Get()) : "null");
        }
        else if (resourceHr != E_NOINTERFACE) Check(resourceHr, "resource_view_query_failed");
        ComPtr<IPixResourceViewBindings> bindings; Check(view->GetBindings(IID_PPV_ARGS(&bindings)), "get_view_bindings_failed");
        std::vector<std::string> items;
        for (UINT j = 0; j < std::min(bindings->GetCount(), 64u); ++j)
        {
            ComPtr<IPixResourceBinding> binding; Check(bindings->GetViewBinding(j, IID_PPV_ARGS(&binding)), "get_binding_failed");
            ObjectJson b; b.Add("type", Number(binding->GetType()));
            ComPtr<IPixRootParameterResourceBinding> root;
            if (SUCCEEDED(binding.As(&root)))
            {
                b.Add("name", String(root->GetName())); b.Add("register", Number(root->GetIndex()));
                b.Add("space", Number(root->GetSpace())); b.Add("rootParameterIndex", Number(root->GetRootParameterIndex()));
            }
            ComPtr<IPixProgramResourceBinding> programBinding;
            if (SUCCEEDED(binding.As(&programBinding))) b.Add("bindingIndex", Number(programBinding->GetBindingIndex()));
            items.push_back(b.Finish());
        }
        r.Add("bindingCount", Number(bindings->GetCount())); r.Add("bindings", ArrayJson(items)); rows.push_back(r.Finish());
    }
    ObjectJson result; result.Add("access", Json(replay ? "replay_accessed" : "static_bindings_only"));
    result.Add("views", Page(rows, views->GetCount(), o)); return result.Finish();
}
std::string CounterValue(UINT64 bits, PIX_FORMAT_SPECIFIER_TYPE format)
{
    ObjectJson r; r.Add("rawBits", Id(bits)); r.Add("format", Number(format));
    // Keep raw bits and the declared format, including values beyond 2^53.
    // Missing data is handled separately; an unknown format never becomes 0.
    auto kind = format & PIX_FORMAT_SPECIFIER_TYPE_AND_SIZE_BITMASK;
    std::ostringstream value; value << std::setprecision(17);
    if (kind == PIX_FORMAT_SPECIFIER_FLOAT64) { double d; memcpy(&d, &bits, sizeof(d)); if (std::isfinite(d)) value << d; }
    else if (kind == PIX_FORMAT_SPECIFIER_FLOAT32) { UINT32 raw = static_cast<UINT32>(bits); float f; memcpy(&f, &raw, sizeof(f)); if (std::isfinite(f)) value << f; }
    else if (kind == PIX_FORMAT_SPECIFIER_INT64) value << static_cast<INT64>(bits);
    else if (kind == PIX_FORMAT_SPECIFIER_INT32) value << static_cast<INT32>(bits);
    else if (kind == PIX_FORMAT_SPECIFIER_INT16) value << static_cast<INT16>(bits);
    else if (kind == PIX_FORMAT_SPECIFIER_UINT64) value << bits;
    else if (kind == PIX_FORMAT_SPECIFIER_UINT32) value << static_cast<UINT32>(bits);
    else if (kind == PIX_FORMAT_SPECIFIER_UINT16) value << static_cast<UINT16>(bits);
    else if (kind == PIX_FORMAT_SPECIFIER_BOOL8 || kind == PIX_FORMAT_SPECIFIER_BOOL32) value << (bits ? "true" : "false");
    r.Add("decoded", value.str().empty() ? "false" : "true");
    r.Add("valueText", value.str().empty() ? "null" : Json(value.str())); return r.Finish();
}
std::string GuidString(const GUID& guid)
{
    wchar_t buffer[40]{}; StringFromGUID2(guid, buffer, 40); return String(buffer);
}
std::string ReplayQuery(CaptureDocument& c, const AnalysisOptions& o)
{
    // Resolve the requested event before connecting to a device.
    PIX_EVENT_INFO event{};
    if (o.event >= 0) event = c.Find(o);
    Replay replay(c);
    if (o.action == "timing")
    {
        ComPtr<IPixGpuCaptureTiming> timing; Check(replay.analysis->CollectTiming(IID_PPV_ARGS(&timing)), "collect_timing_failed");
        ObjectJson r; r.Add("unit", Json("nanoseconds"));
        if (!timing->HasEventData(&event)) { r.Add("available", "false"); r.Add("timing", "null"); return r.Finish(); }
        PIX_EVENT_TIMING t{}; Check(timing->GetEventData(&event, &t), "get_timing_failed");
        ObjectJson values; values.Add("topStart", OptionalTime(t.TopStart)); values.Add("topDuration", OptionalTime(t.TopDuration));
        values.Add("eopStart", OptionalTime(t.EopStart)); values.Add("eopDuration", OptionalTime(t.EopDuration));
        r.Add("available", "true"); r.Add("timing", values.Finish()); return r.Finish();
    }
    if (o.action == "counters")
    {
        ComPtr<IPixGpuCaptureCounters> counters; Check(replay.analysis->GetGpuCounters(IID_PPV_ARGS(&counters)), "get_counters_failed");
        ComPtr<IPixCollection> descriptions; Check(counters->GetCounters(IID_PPV_ARGS(&descriptions)), "counter_descriptions_failed");
        ComPtr<IPixGpuCounterDescription> selected; std::vector<std::string> rows;
        for (UINT64 i = 0; i < descriptions->GetCount(); ++i)
        {
            ComPtr<IPixGpuCounterDescription> d; Check(descriptions->Get(i, IID_PPV_ARGS(&d)), "get_counter_description_failed");
            if (d->GetId() == o.counter) selected = d;
            if (i < o.offset || rows.size() >= o.count) continue;
            ObjectJson r; r.Add("id", Number(d->GetId())); r.Add("name", String(d->GetName()));
            r.Add("description", String(d->GetDescription())); r.Add("format", Number(d->GetDataType())); rows.push_back(r.Finish());
        }
        if (o.counter < 0) return Page(rows, descriptions->GetCount(), o);
        if (!selected) throw Error{E_INVALIDARG, "counter_not_found"};
        PIX_GPU_COUNTER_ID id = selected->GetId(); ComPtr<IPixGpuCaptureCounterData> data;
        Check(counters->CollectCounters(1, &id, IID_PPV_ARGS(&data)), "collect_counter_failed");
        ObjectJson r; r.Add("counterId", Number(id)); r.Add("name", String(selected->GetName()));
        bool available = data->HasEventData(id, &event); UINT64 value = PIX_EVENT_COUNTER_NONE;
        if (available) Check(data->GetEventData(id, &event, &value), "get_counter_value_failed");
        available = available && value != PIX_EVENT_COUNTER_NONE;
        r.Add("available", available ? "true" : "false");
        r.Add("value", available ? CounterValue(value, selected->GetDataType()) : "null"); return r.Finish();
    }
    if (o.action == "occupancy")
    {
        ComPtr<IPixGpuCaptureOccupancy> occupancy; Check(replay.analysis->GetOccupancy(IID_PPV_ARGS(&occupancy)), "get_occupancy_failed");
        ComPtr<IPixCollection> types, stages;
        Check(occupancy->GetOccupancyTypes(IID_PPV_ARGS(&types)), "get_occupancy_types_failed");
        Check(occupancy->GetOccupancyStages(IID_PPV_ARGS(&stages)), "get_occupancy_stages_failed");
        if (o.type < 0)
        {
            std::vector<std::string> typeRows, stageRows;
            for (UINT64 i = o.offset; i < std::min(types->GetCount(), o.offset + o.count); ++i)
            {
                ComPtr<IPixGpuCaptureOccupancyType> type; Check(types->Get(i, IID_PPV_ARGS(&type)), "get_occupancy_type_failed");
                ObjectJson r; r.Add("index", Number(i)); r.Add("name", String(type->GetName())); r.Add("maxSlots", Number(type->GetMaxSlots())); typeRows.push_back(r.Finish());
            }
            for (UINT64 i = o.offset; i < std::min(stages->GetCount(), o.offset + o.count); ++i)
            {
                ComPtr<IPixGpuCaptureOccupancyStage> stage; Check(stages->Get(i, IID_PPV_ARGS(&stage)), "get_occupancy_stage_failed");
                ObjectJson r; r.Add("index", Number(i)); r.Add("name", String(stage->GetName())); stageRows.push_back(r.Finish());
            }
            ObjectJson r; r.Add("types", Page(typeRows, types->GetCount(), o)); r.Add("stages", Page(stageRows, stages->GetCount(), o)); return r.Finish();
        }
        if (UINT64(o.type) >= types->GetCount() || UINT64(o.stage) >= stages->GetCount()) throw Error{E_INVALIDARG, "occupancy_index_out_of_range"};
        ComPtr<IPixGpuCaptureOccupancyType> type; ComPtr<IPixGpuCaptureOccupancyStage> stage;
        Check(types->Get(o.type, IID_PPV_ARGS(&type)), "get_occupancy_type_failed");
        Check(stages->Get(o.stage, IID_PPV_ARGS(&stage)), "get_occupancy_stage_failed");
        ComPtr<IPixGpuCaptureOccupancyData> data; Check(occupancy->CollectOccupancy(IID_PPV_ARGS(&data)), "collect_occupancy_failed");
        UINT64 total = 0; const PIX_OCCUPANCY_POINT* points = nullptr;
        if (o.event >= 0) Check(data->GetEventPoints(type.Get(), stage.Get(), &event, &total, &points), "get_event_occupancy_failed");
        else Check(data->GetPoints(type.Get(), stage.Get(), &total, &points), "get_occupancy_points_failed");
        std::vector<std::string> rows;
        for (UINT64 i = o.offset; i < std::min(total, o.offset + o.count); ++i)
        { ObjectJson r; r.Add("timeNanoseconds", Id(points[i].TimeNanoseconds)); r.Add("slots", Number(points[i].Slots)); rows.push_back(r.Finish()); }
        ObjectJson r; r.Add("maxSlots", Number(type->GetMaxSlots())); r.Add("points", Page(rows, total, o)); return r.Finish();
    }
    ComPtr<IPixGpuCaptureDrPix> drpix; Check(replay.analysis->GetDrPix(IID_PPV_ARGS(&drpix)), "get_drpix_failed");
    if (o.experiment.empty())
    {
        std::vector<std::string> rows;
        for (UINT64 i = o.offset; i < std::min(drpix->GetExperimentCount(), o.offset + o.count); ++i)
        {
            PIX_EXPERIMENT_DESC d{}; Check(drpix->GetExperiment(i, &d), "get_experiment_failed");
            ObjectJson r; r.Add("guid", GuidString(d.Guid)); r.Add("name", String(d.Name)); r.Add("category", String(d.Category));
            r.Add("help", String(d.HelpText)); rows.push_back(r.Finish());
        }
        return Page(rows, drpix->GetExperimentCount(), o);
    }
    PIX_EXPERIMENT_RUN_PARAMS run{}; Check(CLSIDFromString(o.experiment.c_str(), &run.ExperimentGuid), "invalid_experiment_guid");
    bool found = false;
    for (UINT64 i = 0; i < drpix->GetExperimentCount(); ++i)
    { PIX_EXPERIMENT_DESC d{}; Check(drpix->GetExperiment(i, &d), "get_experiment_failed"); found = found || IsEqualGUID(d.Guid, run.ExperimentGuid); }
    if (!found) throw Error{E_INVALIDARG, "experiment_not_found"};
    run.FirstEvent = run.LastEvent = event; ComPtr<IPixAsyncOperation> operation;
    Check(drpix->RunExperiments(1, &run, nullptr, nullptr, nullptr, &operation), "run_experiment_failed");
    ComPtr<IPixCollection> results; Check(operation->GetResult(IID_PPV_ARGS(&results)), "experiment_result_failed");
    if (results->GetCount() != 1) throw Error{E_FAIL, "experiment_result_count_mismatch"};
    ComPtr<IPixGpuCaptureExperimentResult> result; Check(results->Get(0, IID_PPV_ARGS(&result)), "get_experiment_result_failed");
    HRESULT status; Check(result->GetExperimentStatus(&status), "get_experiment_status_failed"); Check(status, "experiment_failed");
    std::vector<std::string> rows;
    for (UINT64 i = o.offset; i < std::min(result->GetExperimentMetricCount(), o.offset + o.count); ++i)
    {
        PIX_EXPERIMENT_METRIC m{}; Check(result->GetExperimentMetric(i, &m), "get_experiment_metric_failed"); ObjectJson r;
        r.Add("group", String(m.GroupName)); r.Add("name", String(m.Name)); r.Add("label", String(m.ValueLabel)); r.Add("depth", Number(m.Depth));
        r.Add("value", m.Value.ValueType == PIX_VALUE_STRING ? String(m.Value.Value.ValueString) :
            m.Value.ValueType == PIX_VALUE_UNKNOWN ? "null" : CounterValue(m.Value.Value.ValueNumeric.Bits, m.Value.Value.ValueNumeric.FormatSpecifier));
        rows.push_back(r.Finish());
    }
    ObjectJson r; r.Add("experiment", GuidString(run.ExperimentGuid)); r.Add("metrics", Page(rows, result->GetExperimentMetricCount(), o));
    std::vector<std::string> messages;
    for (UINT64 i = 0; i < std::min(result->GetExperimentMessageCount(), UINT64(64)); ++i)
    { PIX_MESSAGE m{}; Check(result->GetExperimentMessage(i, &m), "get_experiment_message_failed"); ObjectJson x; x.Add("type", Number(m.Type)); x.Add("message", String(m.Message)); messages.push_back(x.Finish()); }
    r.Add("messageCount", Number(result->GetExperimentMessageCount())); r.Add("messages", ArrayJson(messages)); return r.Finish();
}

std::string AnalysisEnvelope(const AnalysisOptions& o, const fs::path& path, const std::string& session,
    const std::string& pass, bool all, const std::string& code, HRESULT hr, const std::string& data, const std::string& hash = "")
{
    ObjectJson r; r.Add("schemaVersion", "3"); r.Add("success", code == "ok" ? "true" : "false");
    r.Add("action", Json(o.action)); r.Add("requestId", Json(o.requestId)); r.Add("sessionId", Json(session));
    r.Add("capturePath", Json(Utf8(path.wstring()))); r.Add("expectedPass", Json(pass));
    r.Add("captureHash", hash.empty() ? "null" : Json(hash));
    r.Add("boundaryMode", Json(all ? "all" : "graphics")); r.Add("code", Json(code));
    r.Add("unsupported", hr == E_NOTIMPL || hr == E_NOINTERFACE || hr == HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED) ? "true" : "false");
    r.Add("hresult", std::to_string(static_cast<int32_t>(hr))); r.Add("data", data);
    r.Add("queueId", o.queue < 0 ? "null" : Number(o.queue)); r.Add("eventIndex", o.event < 0 ? "null" : Number(o.event));
    r.Add("replay", o.action == "timing" || o.action == "counters" || o.action == "occupancy" || o.action == "drpix" || o.action == "accessed_resources" ? "true" : "false");
    return r.Finish();
}

// PIX calls may hang in drivers/native code. This watchdog bounds the helper
// even when no Editor is alive, and writes a failure artifact before exiting.
class AnalysisDeadline
{
    HANDLE done = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    std::thread worker;
public:
    AnalysisDeadline(DWORD seconds, const fs::path& output, const std::string& failure)
    {
        if (!done) throw Error{E_OUTOFMEMORY, "watchdog_creation_failed"};
        worker = std::thread([=]()
        {
            if (WaitForSingleObject(done, seconds * 1000) == WAIT_TIMEOUT)
            { try { SaveNew(output, failure); } catch (...) { } TerminateProcess(GetCurrentProcess(), 4); }
        });
    }
    ~AnalysisDeadline() { SetEvent(done); worker.join(); CloseHandle(done); }
};
class CaptureFileLock
{
    HANDLE file = INVALID_HANDLE_VALUE;
public:
    explicit CaptureFileLock(const fs::path& path)
    {
        file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
        if (file == INVALID_HANDLE_VALUE) throw Error{HRESULT_FROM_WIN32(GetLastError()), "capture_read_lock_failed"};
    }
    ~CaptureFileLock() { CloseHandle(file); }
    std::string Hash()
    {
        BCRYPT_ALG_HANDLE algorithm = nullptr; BCRYPT_HASH_HANDLE hash = nullptr;
        auto cleanup = [&]() { if (hash) BCryptDestroyHash(hash); if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0); };
        auto check = [](NTSTATUS status) { if (status < 0) throw Error{E_FAIL, "capture_hash_failed"}; };
        try
        {
            check(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0));
            check(BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0));
            std::vector<BYTE> buffer(1024 * 1024); DWORD bytes;
            for (;;)
            {
                if (!ReadFile(file, buffer.data(), static_cast<DWORD>(buffer.size()), &bytes, nullptr))
                    throw Error{HRESULT_FROM_WIN32(GetLastError()), "capture_hash_read_failed"};
                if (!bytes) break;
                check(BCryptHashData(hash, buffer.data(), bytes, 0));
            }
            BYTE digest[32]; check(BCryptFinishHash(hash, digest, 32, 0));
            std::ostringstream out; for (auto b : digest) out << std::hex << std::setw(2) << std::setfill('0') << unsigned(b);
            cleanup(); return out.str();
        }
        catch (...) { cleanup(); throw; }
    }
};
int Analyze(const fs::path& pix, const fs::path& path, const std::string& session, const std::string& pass,
    const fs::path& output, bool all, const AnalysisOptions& o)
{
    AnalysisDeadline deadline(o.timeout, output, AnalysisEnvelope(o, path, session, pass, all, "analysis_timeout", HRESULT_FROM_WIN32(WAIT_TIMEOUT), "null"));
    std::string data = "null", code = "ok", hash; HRESULT hr = S_OK;
    try
    {
        // PIX 2606 opens the document with access incompatible with our read
        // lock. Hash under a short lock before opening, then again after closing;
        // changed captures must never produce accepted analysis evidence.
        { CaptureFileLock file(path); hash = file.Hash(); }
        if (!o.expectedHash.empty() && hash != o.expectedHash) throw Error{E_INVALIDARG, "capture_hash_mismatch"};
        {
            CaptureDocument c; c.Open(pix, path, session, pass, all);
            if (o.action == "events") data = Events(c, o);
            else if (o.action == "event") data = EventJson(c.Find(o), static_cast<UINT32>(o.queue), c.QueueType(static_cast<UINT32>(o.queue)), true);
            else if (o.action == "pipeline") data = Pipeline(c, o);
            else if (o.action == "resources" || o.action == "accessed_resources") data = Resources(c, o);
            else data = ReplayQuery(c, o);
        }
        { CaptureFileLock file(path); if (hash != file.Hash()) throw Error{E_FAIL, "capture_changed_during_analysis"}; }
    }
    catch (const Error& e) { code = e.operation; hr = e.hr; data = "null"; }
    catch (const std::exception&) { code = "analysis_exception"; hr = E_FAIL; data = "null"; }
    auto json = AnalysisEnvelope(o, path, session, pass, all, code, hr, data, hash);
    SaveNew(output, json); std::cout << json << '\n'; return code == "ok" ? 0 : 2;
}
