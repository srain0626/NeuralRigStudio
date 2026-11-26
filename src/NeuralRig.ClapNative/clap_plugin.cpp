#include <clap/clap.h>
#include "../NeuralRig.Vst3Native/native_core_loader.h"
#include "clap_webview_host.h"
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <mutex>
#include <vector>

namespace nrs::clap {

namespace {

constexpr const char* kPluginId = "com.neuralrig.studio.vstclap";
constexpr const char* kPluginName = "NeuralRig Studio";
constexpr const char* kVendor = "NeuralRig Audio";
constexpr const char* kUrl = "https://neuralrigstudio.seltum.xyz";
constexpr const char* kManualUrl = "https://github.com/srain0626/NeuralRigStudio";
constexpr const char* kSupportUrl = "https://github.com/srain0626/NeuralRigStudio/issues";
constexpr const char* kVersion = "3.2.0";
constexpr const char* kDescription = "AVX2/FMA Neural Amp Modeler + MNA SPICE 2x8 Guitar Multi-FX";

const char* const kPluginFeatures[] = {
    CLAP_PLUGIN_FEATURE_AUDIO_EFFECT,
    "guitar",
    "amp-simulator",
    CLAP_PLUGIN_FEATURE_MULTI_EFFECTS,
    CLAP_PLUGIN_FEATURE_STEREO,
    nullptr,
};

const clap_plugin_descriptor_t kDescriptor = {
    CLAP_VERSION_INIT,
    kPluginId,
    kPluginName,
    kVendor,
    kUrl,
    kManualUrl,
    kSupportUrl,
    kVersion,
    kDescription,
    kPluginFeatures,
};

std::mutex g_entryMtx;
int g_entryInitCount = 0;

struct QueuedHostParamEvent
{
    uint32_t paramId;
    int32_t phase; // 0=begin, 1=value, 2=end
    double norm01;
};

struct ClapPluginInstance
{
    clap_plugin_t clapPlugin{};
    const clap_host_t* host{nullptr};
    const clap_host_params_t* hostParams{nullptr};

    void* coreHandle{nullptr};
    std::unique_ptr<ClapWebViewHost> guiHost;
    std::vector<float> zeroInputBuf;

    std::mutex outEvMtx;
    std::vector<QueuedHostParamEvent> pendingHostEvents;
    std::vector<QueuedHostParamEvent> scratchHostEvents;
};

ClapPluginInstance* FromPlugin(const clap_plugin_t* p)
{
    return p ? static_cast<ClapPluginInstance*>(p->plugin_data) : nullptr;
}

void CopyCString(char* dst, size_t cap, const char* src)
{
    if (!dst || cap == 0) return;
    std::memset(dst, 0, cap);
    if (!src) return;
    std::strncpy(dst, src, cap - 1);
}

void __cdecl StaticWebSendCallback(void* userData, const char* utf8Json, int32_t len)
{
    auto* self = static_cast<ClapPluginInstance*>(userData);
    if (!self || !self->guiHost) return;
    self->guiHost->enqueueWebMessage(utf8Json, len);
}

void __cdecl StaticHostParamCallback(void* userData, uint32_t paramId, int32_t phase, double norm01)
{
    auto* self = static_cast<ClapPluginInstance*>(userData);
    if (!self) return;
    {
        std::lock_guard<std::mutex> lk(self->outEvMtx);
        if (self->pendingHostEvents.size() < 1024)
            self->pendingHostEvents.push_back(QueuedHostParamEvent{paramId, phase, norm01});
    }
    if (self->hostParams && self->hostParams->request_flush && self->host)
        self->hostParams->request_flush(self->host);
}

void ApplyInputEvents(ClapPluginInstance* self, const nrs::core::NativeCoreApi& api, const clap_input_events_t* in)
{
    if (!in || !in->size || !in->get) return;
    const uint32_t count = in->size(in);
    for (uint32_t i = 0; i < count; i++)
    {
        const clap_event_header_t* hdr = in->get(in, i);
        if (!hdr || hdr->space_id != CLAP_CORE_EVENT_SPACE_ID) continue;

        if (hdr->type == CLAP_EVENT_PARAM_VALUE)
        {
            const auto* ev = reinterpret_cast<const clap_event_param_value_t*>(hdr);
            double norm = api.param_plain_to_norm(self->coreHandle, ev->param_id, ev->value);
            api.set_param_normalized(self->coreHandle, ev->param_id, norm, static_cast<int32_t>(hdr->time));
        }
        else if (hdr->type == CLAP_EVENT_MIDI)
        {
            const auto* mev = reinterpret_cast<const clap_event_midi_t*>(hdr);
            api.midi_event(self->coreHandle, mev->data[0], mev->data[1], mev->data[2], static_cast<int32_t>(hdr->time));
        }
    }
}

void DrainQueuedOutputEvents(
    ClapPluginInstance* self,
    const nrs::core::NativeCoreApi& api,
    const clap_output_events_t* out,
    bool blockLock)
{
    if (!out || !out->try_push) return;

    self->scratchHostEvents.clear();
    if (blockLock)
    {
        std::lock_guard<std::mutex> lk(self->outEvMtx);
        self->scratchHostEvents.swap(self->pendingHostEvents);
    }
    else
    {
        std::unique_lock<std::mutex> lk(self->outEvMtx, std::try_to_lock);
        if (!lk.owns_lock()) return;
        self->scratchHostEvents.swap(self->pendingHostEvents);
    }

    for (const auto& ev : self->scratchHostEvents)
    {
        if (ev.phase == 0 || ev.phase == 2)
        {
            clap_event_param_gesture_t g{};
            g.header.size = sizeof(clap_event_param_gesture_t);
            g.header.time = 0;
            g.header.space_id = CLAP_CORE_EVENT_SPACE_ID;
            g.header.type = static_cast<uint16_t>(ev.phase == 0 ? CLAP_EVENT_PARAM_GESTURE_BEGIN : CLAP_EVENT_PARAM_GESTURE_END);
            g.header.flags = 0;
            g.param_id = ev.paramId;
            out->try_push(out, &g.header);
        }
        else if (ev.phase == 1)
        {
            clap_event_param_value_t pv{};
            pv.header.size = sizeof(clap_event_param_value_t);
            pv.header.time = 0;
            pv.header.space_id = CLAP_CORE_EVENT_SPACE_ID;
            pv.header.type = CLAP_EVENT_PARAM_VALUE;
            pv.header.flags = CLAP_EVENT_IS_LIVE;
            pv.param_id = ev.paramId;
            pv.cookie = nullptr;
            pv.note_id = -1;
            pv.port_index = -1;
            pv.channel = -1;
            pv.key = -1;
            pv.value = api.param_norm_to_plain(self->coreHandle, ev.paramId, ev.norm01);
            out->try_push(out, &pv.header);
        }
    }
}

// =========================================================================
// clap.audio-ports
// =========================================================================

uint32_t CLAP_ABI AudioPortsCount(const clap_plugin_t*, bool is_input)
{
    (void)is_input;
    return 1;
}

bool CLAP_ABI AudioPortsGet(const clap_plugin_t*, uint32_t index, bool is_input, clap_audio_port_info_t* info)
{
    if (index != 0 || !info) return false;
    std::memset(info, 0, sizeof(*info));
    info->id = 0;
    CopyCString(info->name, sizeof(info->name), is_input ? "Stereo In" : "Stereo Out");
    info->flags = CLAP_AUDIO_PORT_IS_MAIN;
    info->channel_count = 2;
    info->port_type = CLAP_PORT_STEREO;
    info->in_place_pair = 0;
    return true;
}

const clap_plugin_audio_ports_t kExtAudioPorts = {
    &AudioPortsCount,
    &AudioPortsGet,
};

// =========================================================================
// clap.note-ports
// =========================================================================

uint32_t CLAP_ABI NotePortsCount(const clap_plugin_t*, bool is_input)
{
    return is_input ? 1u : 0u;
}

bool CLAP_ABI NotePortsGet(const clap_plugin_t*, uint32_t index, bool is_input, clap_note_port_info_t* info)
{
    if (!is_input || index != 0 || !info) return false;
    std::memset(info, 0, sizeof(*info));
    info->id = 0;
    info->supported_dialects = CLAP_NOTE_DIALECT_MIDI;
    info->preferred_dialect = CLAP_NOTE_DIALECT_MIDI;
    CopyCString(info->name, sizeof(info->name), "MIDI In");
    return true;
}

const clap_plugin_note_ports_t kExtNotePorts = {
    &NotePortsCount,
    &NotePortsGet,
};

// =========================================================================
// clap.params
// =========================================================================

uint32_t CLAP_ABI ParamsCount(const clap_plugin_t*)
{
    auto& api = nrs::core::GetNativeCoreApi();
    return api.isLoaded() ? static_cast<uint32_t>(std::max<int32_t>(0, api.get_param_count())) : 0u;
}

bool CLAP_ABI ParamsGetInfo(const clap_plugin_t* plugin, uint32_t param_index, clap_param_info_t* info)
{
    auto* self = FromPlugin(plugin);
    auto& api = nrs::core::GetNativeCoreApi();
    if (!self || !self->coreHandle || !api.isLoaded() || !info) return false;

    uint32_t count = ParamsCount(plugin);
    if (param_index >= count) return false;

    nrs::core::NrsParamInfoNative native{};
    if (!api.get_param_info(self->coreHandle, param_index, &native))
        return false;

    std::memset(info, 0, sizeof(*info));
    info->id = native.id;
    info->flags = CLAP_PARAM_IS_AUTOMATABLE;
    if (native.stepCount > 0)
        info->flags |= CLAP_PARAM_IS_STEPPED;
    info->cookie = nullptr;
    CopyCString(info->name, sizeof(info->name), native.nameUtf8);

    if (native.id < 16)
    {
        CopyCString(info->module, sizeof(info->module), "Global");
    }
    else
    {
        uint32_t slotNum = 1u + ((native.id - 16u) / 9u);
        std::snprintf(info->module, sizeof(info->module), "Chain/Slot %02u", slotNum);
    }

    double minPlain = api.param_norm_to_plain(self->coreHandle, native.id, 0.0);
    double maxPlain = api.param_norm_to_plain(self->coreHandle, native.id, 1.0);
    double defPlain = api.param_norm_to_plain(self->coreHandle, native.id, native.defaultNormalized);
    if (!std::isfinite(minPlain)) minPlain = 0.0;
    if (!std::isfinite(maxPlain) || maxPlain <= minPlain) maxPlain = minPlain + 1.0;
    if (!std::isfinite(defPlain)) defPlain = minPlain;
    defPlain = std::clamp(defPlain, minPlain, maxPlain);

    info->min_value = minPlain;
    info->max_value = maxPlain;
    info->default_value = defPlain;
    return true;
}

bool CLAP_ABI ParamsGetValue(const clap_plugin_t* plugin, clap_id param_id, double* out_value)
{
    auto* self = FromPlugin(plugin);
    auto& api = nrs::core::GetNativeCoreApi();
    if (!self || !self->coreHandle || !api.isLoaded() || !out_value) return false;
    if (param_id >= ParamsCount(plugin)) return false;

    double norm = api.get_param_normalized(self->coreHandle, param_id);
    *out_value = api.param_norm_to_plain(self->coreHandle, param_id, norm);
    return true;
}

bool CLAP_ABI ParamsValueToText(
    const clap_plugin_t* plugin,
    clap_id param_id,
    double value,
    char* out_buffer,
    uint32_t out_buffer_capacity)
{
    auto* self = FromPlugin(plugin);
    auto& api = nrs::core::GetNativeCoreApi();
    if (!self || !self->coreHandle || !api.isLoaded() || !out_buffer || out_buffer_capacity == 0)
        return false;
    if (param_id >= ParamsCount(plugin)) return false;

    double norm = api.param_plain_to_norm(self->coreHandle, param_id, value);
    int32_t len = api.format_param_value(
        self->coreHandle,
        param_id,
        norm,
        out_buffer,
        static_cast<int32_t>(out_buffer_capacity));
    if (len <= 0)
        std::snprintf(out_buffer, out_buffer_capacity, "%.2f", value);
    return true;
}

bool CLAP_ABI ParamsTextToValue(
    const clap_plugin_t* plugin,
    clap_id param_id,
    const char* param_value_text,
    double* out_value)
{
    auto* self = FromPlugin(plugin);
    auto& api = nrs::core::GetNativeCoreApi();
    if (!self || !self->coreHandle || !api.isLoaded() || !param_value_text || !out_value)
        return false;

    clap_param_info_t info{};
    if (!ParamsGetInfo(plugin, param_id, &info)) return false;

    if ((info.flags & CLAP_PARAM_IS_STEPPED) != 0 && static_cast<int>(info.max_value - info.min_value) == 1)
    {
        if (_stricmp(param_value_text, "on") == 0 || _stricmp(param_value_text, "true") == 0 ||
            _stricmp(param_value_text, "active") == 0)
        {
            *out_value = info.max_value;
            return true;
        }
        if (_stricmp(param_value_text, "off") == 0 || _stricmp(param_value_text, "false") == 0 ||
            _stricmp(param_value_text, "bypass") == 0)
        {
            *out_value = info.min_value;
            return true;
        }
    }

    char* endPtr = nullptr;
    double v = std::strtod(param_value_text, &endPtr);
    if (endPtr == param_value_text || !std::isfinite(v))
        return false;

    *out_value = std::clamp(v, info.min_value, info.max_value);
    return true;
}

void CLAP_ABI ParamsFlush(
    const clap_plugin_t* plugin,
    const clap_input_events_t* in,
    const clap_output_events_t* out)
{
    auto* self = FromPlugin(plugin);
    auto& api = nrs::core::GetNativeCoreApi();
    if (!self || !self->coreHandle || !api.isLoaded()) return;

    ApplyInputEvents(self, api, in);
    DrainQueuedOutputEvents(self, api, out, /*blockLock=*/true);
}

const clap_plugin_params_t kExtParams = {
    &ParamsCount,
    &ParamsGetInfo,
    &ParamsGetValue,
    &ParamsValueToText,
    &ParamsTextToValue,
    &ParamsFlush,
};

// =========================================================================
// clap.state
// =========================================================================

bool CLAP_ABI StateSave(const clap_plugin_t* plugin, const clap_ostream_t* stream)
{
    auto* self = FromPlugin(plugin);
    auto& api = nrs::core::GetNativeCoreApi();
    if (!self || !self->coreHandle || !api.isLoaded() || !stream || !stream->write)
        return false;

    int32_t len = 0;
    uint8_t* ptr = api.save_state(self->coreHandle, &len);
    if (!ptr || len <= 0)
    {
        if (ptr) api.free_buffer(ptr);
        return false;
    }

    int32_t offset = 0;
    bool ok = true;
    while (offset < len)
    {
        int64_t written = stream->write(stream, ptr + offset, static_cast<uint64_t>(len - offset));
        if (written <= 0)
        {
            ok = false;
            break;
        }
        offset += static_cast<int32_t>(written);
    }

    api.free_buffer(ptr);
    return ok;
}

bool CLAP_ABI StateLoad(const clap_plugin_t* plugin, const clap_istream_t* stream)
{
    auto* self = FromPlugin(plugin);
    auto& api = nrs::core::GetNativeCoreApi();
    if (!self || !self->coreHandle || !api.isLoaded() || !stream || !stream->read)
        return false;

    std::vector<uint8_t> bytes;
    uint8_t chunk[8192];
    for (;;)
    {
        int64_t n = stream->read(stream, chunk, sizeof(chunk));
        if (n == 0) break;
        if (n < 0 || bytes.size() + static_cast<size_t>(n) > 16u * 1024u * 1024u)
            return false;
        bytes.insert(bytes.end(), chunk, chunk + n);
    }

    if (bytes.empty()) return false;
    return api.load_state(self->coreHandle, bytes.data(), static_cast<int32_t>(bytes.size())) != 0;
}

const clap_plugin_state_t kExtState = {
    &StateSave,
    &StateLoad,
};

// =========================================================================
// clap.latency & clap.tail
// =========================================================================

uint32_t CLAP_ABI LatencyGet(const clap_plugin_t* plugin)
{
    auto* self = FromPlugin(plugin);
    auto& api = nrs::core::GetNativeCoreApi();
    if (!self || !self->coreHandle || !api.isLoaded()) return 0u;
    int32_t samples = api.get_latency_samples(self->coreHandle);
    return samples > 0 ? static_cast<uint32_t>(samples) : 0u;
}

const clap_plugin_latency_t kExtLatency = { &LatencyGet };

uint32_t CLAP_ABI TailGet(const clap_plugin_t* plugin)
{
    auto* self = FromPlugin(plugin);
    auto& api = nrs::core::GetNativeCoreApi();
    if (!self || !self->coreHandle || !api.isLoaded()) return 0u;
    int32_t samples = api.get_tail_samples(self->coreHandle);
    return samples > 0 ? static_cast<uint32_t>(samples) : 0u;
}

const clap_plugin_tail_t kExtTail = { &TailGet };

// =========================================================================
// clap.gui (Win32 HWND + WebView2)
// =========================================================================

bool CLAP_ABI GuiIsApiSupported(const clap_plugin_t*, const char* api, bool is_floating)
{
    return !is_floating && api && std::strcmp(api, CLAP_WINDOW_API_WIN32) == 0;
}

bool CLAP_ABI GuiGetPreferredApi(const clap_plugin_t*, const char** api, bool* is_floating)
{
    if (api) *api = CLAP_WINDOW_API_WIN32;
    if (is_floating) *is_floating = false;
    return true;
}

bool CLAP_ABI GuiCreate(const clap_plugin_t* plugin, const char* api, bool is_floating)
{
    auto* self = FromPlugin(plugin);
    if (!self || !self->coreHandle) return false;
    if (!GuiIsApiSupported(plugin, api, is_floating)) return false;
    if (!self->guiHost)
        self->guiHost = std::make_unique<ClapWebViewHost>(self->coreHandle);
    return self->guiHost->create();
}

void CLAP_ABI GuiDestroy(const clap_plugin_t* plugin)
{
    auto* self = FromPlugin(plugin);
    if (!self) return;
    if (self->guiHost)
    {
        self->guiHost->destroy();
        self->guiHost.reset();
    }
}

bool CLAP_ABI GuiSetScale(const clap_plugin_t* plugin, double scale)
{
    auto* self = FromPlugin(plugin);
    return self && self->guiHost ? self->guiHost->setScale(scale) : false;
}

bool CLAP_ABI GuiGetSize(const clap_plugin_t* plugin, uint32_t* width, uint32_t* height)
{
    auto* self = FromPlugin(plugin);
    return self && self->guiHost ? self->guiHost->getSize(width, height) : false;
}

bool CLAP_ABI GuiCanResize(const clap_plugin_t*)
{
    return true;
}

bool CLAP_ABI GuiGetResizeHints(const clap_plugin_t*, clap_gui_resize_hints_t* hints)
{
    if (!hints) return false;
    hints->can_resize_horizontally = true;
    hints->can_resize_vertically = true;
    hints->preserve_aspect_ratio = false;
    hints->aspect_ratio_width = 16;
    hints->aspect_ratio_height = 10;
    return true;
}

bool CLAP_ABI GuiAdjustSize(const clap_plugin_t* plugin, uint32_t* width, uint32_t* height)
{
    auto* self = FromPlugin(plugin);
    return self && self->guiHost ? self->guiHost->adjustSize(width, height) : false;
}

bool CLAP_ABI GuiSetSize(const clap_plugin_t* plugin, uint32_t width, uint32_t height)
{
    auto* self = FromPlugin(plugin);
    return self && self->guiHost ? self->guiHost->setSize(width, height) : false;
}

bool CLAP_ABI GuiSetParent(const clap_plugin_t* plugin, const clap_window_t* window)
{
    auto* self = FromPlugin(plugin);
    if (!self || !self->guiHost || !window || !window->api) return false;
    if (std::strcmp(window->api, CLAP_WINDOW_API_WIN32) != 0) return false;
    return self->guiHost->setParent(static_cast<HWND>(window->win32));
}

bool CLAP_ABI GuiSetTransient(const clap_plugin_t*, const clap_window_t*)
{
    return false;
}

void CLAP_ABI GuiSuggestTitle(const clap_plugin_t*, const char*)
{
}

bool CLAP_ABI GuiShow(const clap_plugin_t* plugin)
{
    auto* self = FromPlugin(plugin);
    return self && self->guiHost ? self->guiHost->show() : false;
}

bool CLAP_ABI GuiHide(const clap_plugin_t* plugin)
{
    auto* self = FromPlugin(plugin);
    return self && self->guiHost ? self->guiHost->hide() : false;
}

const clap_plugin_gui_t kExtGui = {
    &GuiIsApiSupported,
    &GuiGetPreferredApi,
    &GuiCreate,
    &GuiDestroy,
    &GuiSetScale,
    &GuiGetSize,
    &GuiCanResize,
    &GuiGetResizeHints,
    &GuiAdjustSize,
    &GuiSetSize,
    &GuiSetParent,
    &GuiSetTransient,
    &GuiSuggestTitle,
    &GuiShow,
    &GuiHide,
};

// =========================================================================
// clap_plugin_t lifecycle & process
// =========================================================================

bool CLAP_ABI PluginInit(const clap_plugin_t* plugin)
{
    auto* self = FromPlugin(plugin);
    if (!self) return false;
    if (self->coreHandle) return true;

    auto& api = nrs::core::GetNativeCoreApi();
    if (!api.isLoaded()) return false;

    self->coreHandle = api.create(&StaticWebSendCallback, &StaticHostParamCallback, self);
    if (!self->coreHandle) return false;

    api.set_host_format(self->coreHandle, "CLAP");
    if (self->host && self->host->get_extension)
        self->hostParams = static_cast<const clap_host_params_t*>(self->host->get_extension(self->host, CLAP_EXT_PARAMS));
    return true;
}

void CLAP_ABI PluginDestroy(const clap_plugin_t* plugin)
{
    auto* self = FromPlugin(plugin);
    if (!self) return;

    if (self->guiHost)
    {
        self->guiHost->destroy();
        self->guiHost.reset();
    }
    if (self->coreHandle)
    {
        auto& api = nrs::core::GetNativeCoreApi();
        if (api.isLoaded())
            api.destroy(self->coreHandle);
        self->coreHandle = nullptr;
    }
    delete self;
}

bool CLAP_ABI PluginActivate(
    const clap_plugin_t* plugin,
    double sample_rate,
    uint32_t min_frames_count,
    uint32_t max_frames_count)
{
    (void)min_frames_count;
    auto* self = FromPlugin(plugin);
    auto& api = nrs::core::GetNativeCoreApi();
    if (!self || !self->coreHandle || !api.isLoaded()) return false;

    int32_t sr = std::clamp(static_cast<int32_t>(std::lround(sample_rate)), 44100, 192000);
    int32_t maxFrames = std::clamp(static_cast<int32_t>(max_frames_count), 32, 8192);
    self->zeroInputBuf.assign(static_cast<size_t>(maxFrames), 0.0f);
    self->scratchHostEvents.reserve(64);
    api.activate(self->coreHandle, sr, maxFrames);
    return true;
}

void CLAP_ABI PluginDeactivate(const clap_plugin_t* plugin)
{
    auto* self = FromPlugin(plugin);
    auto& api = nrs::core::GetNativeCoreApi();
    if (self && self->coreHandle && api.isLoaded())
        api.deactivate(self->coreHandle);
}

bool CLAP_ABI PluginStartProcessing(const clap_plugin_t*) { return true; }
void CLAP_ABI PluginStopProcessing(const clap_plugin_t*) {}
void CLAP_ABI PluginReset(const clap_plugin_t*) {}

clap_process_status CLAP_ABI PluginProcess(const clap_plugin_t* plugin, const clap_process_t* proc)
{
    auto* self = FromPlugin(plugin);
    auto& api = nrs::core::GetNativeCoreApi();
    if (!self || !self->coreHandle || !api.isLoaded() || !proc)
        return CLAP_PROCESS_ERROR;

    ApplyInputEvents(self, api, proc->in_events);
    DrainQueuedOutputEvents(self, api, proc->out_events, /*blockLock=*/false);

    if (proc->frames_count == 0)
        return CLAP_PROCESS_CONTINUE;

    int32_t n = static_cast<int32_t>(proc->frames_count);
    if (proc->audio_outputs_count == 0 || !proc->audio_outputs || !proc->audio_outputs[0].data32 ||
        proc->audio_outputs[0].channel_count == 0)
    {
        return CLAP_PROCESS_CONTINUE;
    }

    float* outL = proc->audio_outputs[0].data32[0];
    float* outR = proc->audio_outputs[0].channel_count >= 2 ? proc->audio_outputs[0].data32[1] : outL;

    float* fallbackZero = !self->zeroInputBuf.empty() && n <= static_cast<int32_t>(self->zeroInputBuf.size())
        ? self->zeroInputBuf.data()
        : outL;

    float* inL = fallbackZero;
    float* inR = fallbackZero;
    if (proc->audio_inputs_count > 0 && proc->audio_inputs && proc->audio_inputs[0].data32 &&
        proc->audio_inputs[0].channel_count > 0)
    {
        inL = proc->audio_inputs[0].data32[0];
        inR = proc->audio_inputs[0].channel_count >= 2 ? proc->audio_inputs[0].data32[1] : inL;
    }
    else if (fallbackZero == outL)
    {
        std::memset(outL, 0, static_cast<size_t>(n) * sizeof(float));
        if (outR != outL) std::memset(outR, 0, static_cast<size_t>(n) * sizeof(float));
    }

    double hostBpm = 0.0;
    uint8_t hasHostBpm = 0;
    uint8_t isPlaying = 0;
    double ppqPos = 0.0;
    uint8_t hasPpq = 0;

    if (const clap_event_transport_t* tr = proc->transport)
    {
        if ((tr->flags & CLAP_TRANSPORT_HAS_TEMPO) != 0)
        {
            hasHostBpm = 1;
            hostBpm = tr->tempo;
        }
        if ((tr->flags & CLAP_TRANSPORT_IS_PLAYING) != 0)
            isPlaying = 1;
        if ((tr->flags & CLAP_TRANSPORT_HAS_BEATS_TIMELINE) != 0)
        {
            hasPpq = 1;
            ppqPos = static_cast<double>(tr->song_pos_beats) / static_cast<double>(CLAP_BEATTIME_FACTOR);
        }
    }

    api.process_ex(
        self->coreHandle,
        inL,
        inR,
        outL,
        outR,
        n,
        hostBpm,
        hasHostBpm,
        isPlaying,
        ppqPos,
        hasPpq);

    return CLAP_PROCESS_CONTINUE;
}

const void* CLAP_ABI PluginGetExtension(const clap_plugin_t*, const char* id)
{
    if (!id) return nullptr;
    if (std::strcmp(id, CLAP_EXT_AUDIO_PORTS) == 0) return &kExtAudioPorts;
    if (std::strcmp(id, CLAP_EXT_NOTE_PORTS) == 0) return &kExtNotePorts;
    if (std::strcmp(id, CLAP_EXT_PARAMS) == 0) return &kExtParams;
    if (std::strcmp(id, CLAP_EXT_STATE) == 0) return &kExtState;
    if (std::strcmp(id, CLAP_EXT_LATENCY) == 0) return &kExtLatency;
    if (std::strcmp(id, CLAP_EXT_TAIL) == 0) return &kExtTail;
    if (std::strcmp(id, CLAP_EXT_GUI) == 0) return &kExtGui;
    return nullptr;
}

void CLAP_ABI PluginOnMainThread(const clap_plugin_t*) {}

// =========================================================================
// clap_plugin_factory
// =========================================================================

uint32_t CLAP_ABI FactoryGetPluginCount(const clap_plugin_factory_t*)
{
    return 1u;
}

const clap_plugin_descriptor_t* CLAP_ABI FactoryGetPluginDescriptor(
    const clap_plugin_factory_t*,
    uint32_t index)
{
    return index == 0u ? &kDescriptor : nullptr;
}

const clap_plugin_t* CLAP_ABI FactoryCreatePlugin(
    const clap_plugin_factory_t*,
    const clap_host_t* host,
    const char* plugin_id)
{
    if (!plugin_id || std::strcmp(plugin_id, kPluginId) != 0)
        return nullptr;

    auto* inst = new ClapPluginInstance();
    inst->host = host;
    inst->clapPlugin.desc = &kDescriptor;
    inst->clapPlugin.plugin_data = inst;
    inst->clapPlugin.init = &PluginInit;
    inst->clapPlugin.destroy = &PluginDestroy;
    inst->clapPlugin.activate = &PluginActivate;
    inst->clapPlugin.deactivate = &PluginDeactivate;
    inst->clapPlugin.start_processing = &PluginStartProcessing;
    inst->clapPlugin.stop_processing = &PluginStopProcessing;
    inst->clapPlugin.reset = &PluginReset;
    inst->clapPlugin.process = &PluginProcess;
    inst->clapPlugin.get_extension = &PluginGetExtension;
    inst->clapPlugin.on_main_thread = &PluginOnMainThread;
    return &inst->clapPlugin;
}

const clap_plugin_factory_t kFactory = {
    &FactoryGetPluginCount,
    &FactoryGetPluginDescriptor,
    &FactoryCreatePlugin,
};

bool CLAP_ABI EntryInit(const char*)
{
    std::lock_guard<std::mutex> lk(g_entryMtx);
    g_entryInitCount++;
    return true;
}

void CLAP_ABI EntryDeinit(void)
{
    std::lock_guard<std::mutex> lk(g_entryMtx);
    if (g_entryInitCount > 0)
        g_entryInitCount--;
}

const void* CLAP_ABI EntryGetFactory(const char* factory_id)
{
    if (factory_id && std::strcmp(factory_id, CLAP_PLUGIN_FACTORY_ID) == 0)
        return &kFactory;
    return nullptr;
}

} // namespace

} // namespace nrs::clap

extern "C" CLAP_EXPORT const clap_plugin_entry_t clap_entry = {
    CLAP_VERSION_INIT,
    &nrs::clap::EntryInit,
    &nrs::clap::EntryDeinit,
    &nrs::clap::EntryGetFactory,
};
