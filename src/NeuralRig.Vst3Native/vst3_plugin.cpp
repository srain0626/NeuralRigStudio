#include "vst3_abi.h"
#include "native_core_loader.h"
#include "vst3_webview_editor.h"
#include <algorithm>
#include <atomic>
#include <cstdlib>
#include <cstring>
#include <cwchar>
#include <mutex>
#include <vector>

namespace nrs::vst3 {

namespace {

void CopyAsciiToChar8(char8* dst, size_t cap, const char* src)
{
    if (!dst || cap == 0) return;
    std::memset(dst, 0, cap);
    if (!src) return;
    std::strncpy(dst, src, cap - 1);
}

void CopyUtf8ToUtf16(char16* dst, size_t capChars, const char* srcUtf8)
{
    if (!dst || capChars == 0) return;
    std::memset(dst, 0, capChars * sizeof(char16));
    if (!srcUtf8 || !*srcUtf8) return;
    MultiByteToWideChar(
        CP_UTF8,
        0,
        srcUtf8,
        -1,
        reinterpret_cast<wchar_t*>(dst),
        static_cast<int>(capChars - 1));
}

void CopyWideToUtf16(char16* dst, size_t capChars, const wchar_t* src)
{
    if (!dst || capChars == 0) return;
    std::memset(dst, 0, capChars * sizeof(char16));
    if (!src) return;
    size_t i = 0;
    while (i + 1 < capChars && src[i] != L'\0')
    {
        dst[i] = static_cast<char16>(src[i]);
        i++;
    }
}

class ComponentBridge : public IComponent
{
public:
    virtual tresult SMTG_CALLCONV componentInitialize(FUnknown* context) = 0;
    virtual tresult SMTG_CALLCONV componentTerminate() = 0;
    virtual tresult SMTG_CALLCONV componentSetState(IBStream* state) = 0;
    virtual tresult SMTG_CALLCONV componentGetState(IBStream* state) = 0;

    tresult SMTG_CALLCONV initialize(FUnknown* context) override { return componentInitialize(context); }
    tresult SMTG_CALLCONV terminate() override { return componentTerminate(); }
    tresult SMTG_CALLCONV setState(IBStream* state) override { return componentSetState(state); }
    tresult SMTG_CALLCONV getState(IBStream* state) override { return componentGetState(state); }
};

class EditControllerBridge : public IEditController
{
public:
    virtual tresult SMTG_CALLCONV controllerInitialize(FUnknown* context) = 0;
    virtual tresult SMTG_CALLCONV controllerTerminate() = 0;
    virtual tresult SMTG_CALLCONV controllerSetState(IBStream* state) = 0;
    virtual tresult SMTG_CALLCONV controllerGetState(IBStream* state) = 0;

    tresult SMTG_CALLCONV initialize(FUnknown* context) override { return controllerInitialize(context); }
    tresult SMTG_CALLCONV terminate() override { return controllerTerminate(); }
    tresult SMTG_CALLCONV setState(IBStream* state) override { return controllerSetState(state); }
    tresult SMTG_CALLCONV getState(IBStream* state) override { return controllerGetState(state); }
};

} // namespace

class NeuralRigVst3Plugin final
    : public ComponentBridge
    , public EditControllerBridge
    , public IAudioProcessor
    , public IProcessContextRequirements
    , public IMidiMapping
{
public:
    NeuralRigVst3Plugin() = default;
    ~NeuralRigVst3Plugin()
    {
        if (handler_)
        {
            handler_->release();
            handler_ = nullptr;
        }
        destroyCore();
    }

    void detachEditor(Vst3WebViewEditor* editor)
    {
        std::lock_guard<std::mutex> lk(editorMtx_);
        if (activeEditor_ == editor)
            activeEditor_ = nullptr;
    }

    // Unified FUnknown
    tresult SMTG_CALLCONV queryInterface(const TUID _iid, void** obj) override
    {
        if (!obj) return kInvalidArgument;
        if (UidEqual(_iid, kFUnknownIid) || UidEqual(_iid, kIPluginBaseIid) || UidEqual(_iid, kIComponentIid))
        {
            *obj = static_cast<IComponent*>(static_cast<ComponentBridge*>(this));
            addRef();
            return kResultOk;
        }
        if (UidEqual(_iid, kIAudioProcessorIid))
        {
            *obj = static_cast<IAudioProcessor*>(this);
            addRef();
            return kResultOk;
        }
        if (UidEqual(_iid, kIProcessContextRequirementsIid))
        {
            *obj = static_cast<IProcessContextRequirements*>(this);
            addRef();
            return kResultOk;
        }
        if (UidEqual(_iid, kIEditControllerIid))
        {
            *obj = static_cast<IEditController*>(static_cast<EditControllerBridge*>(this));
            addRef();
            return kResultOk;
        }
        if (UidEqual(_iid, kIMidiMappingIid))
        {
            *obj = static_cast<IMidiMapping*>(this);
            addRef();
            return kResultOk;
        }
        *obj = nullptr;
        return kNoInterface;
    }

    uint32 SMTG_CALLCONV addRef() override { return ++refCount_; }
    uint32 SMTG_CALLCONV release() override
    {
        uint32 r = --refCount_;
        if (r == 0)
            delete this;
        return r;
    }

    // IComponent / IPluginBase
    tresult SMTG_CALLCONV componentInitialize(FUnknown*) override
    {
        return ensureCoreInitialized() ? kResultOk : kInternalError;
    }

    tresult SMTG_CALLCONV componentTerminate() override
    {
        if (componentHandlerShutdown())
            destroyCore();
        return kResultOk;
    }

    tresult SMTG_CALLCONV controllerInitialize(FUnknown*) override
    {
        return ensureCoreInitialized() ? kResultOk : kInternalError;
    }

    tresult SMTG_CALLCONV controllerTerminate() override
    {
        return kResultOk;
    }

    tresult SMTG_CALLCONV getControllerClassId(TUID classId) override
    {
        // SingleComponentEffect returns kNoInterface
        std::memset(classId, 0, sizeof(TUID));
        return kNoInterface;
    }

    tresult SMTG_CALLCONV setIoMode(IoMode) override { return kResultOk; }

    int32 SMTG_CALLCONV getBusCount(MediaType type, BusDirection dir) override
    {
        if (type == kAudio && (dir == kInput || dir == kOutput))
            return 1;
        if (type == kEvent && dir == kInput)
            return 1;
        return 0;
    }

    tresult SMTG_CALLCONV getBusInfo(MediaType type, BusDirection dir, int32 index, BusInfo& bus) override
    {
        if (index != 0)
            return kInvalidArgument;

        if (type == kEvent && dir == kInput)
        {
            std::memset(&bus, 0, sizeof(bus));
            bus.mediaType = kEvent;
            bus.direction = kInput;
            bus.channelCount = 16;
            CopyWideToUtf16(bus.name, 128, L"MIDI In");
            bus.busType = kMain;
            bus.flags = kDefaultActive;
            return kResultOk;
        }

        if (type != kAudio || (dir != kInput && dir != kOutput))
            return kInvalidArgument;

        std::memset(&bus, 0, sizeof(bus));
        bus.mediaType = kAudio;
        bus.direction = dir;
        bus.channelCount = (dir == kInput && inputArr_ == kMono) ? 1 : 2;
        CopyWideToUtf16(bus.name, 128, dir == kInput ? L"Input" : L"Output");
        bus.busType = kMain;
        bus.flags = kDefaultActive;
        return kResultOk;
    }

    tresult SMTG_CALLCONV getRoutingInfo(RoutingInfo&, RoutingInfo&) override { return kNotImplemented; }
    tresult SMTG_CALLCONV activateBus(MediaType type, BusDirection dir, int32 index, TBool state) override
    {
        if (index != 0) return kInvalidArgument;
        if (type == kAudio || (type == kEvent && dir == kInput))
        {
            (void)state;
            return kResultOk;
        }
        return kInvalidArgument;
    }

    tresult SMTG_CALLCONV setActive(TBool state) override
    {
        auto& api = nrs::core::GetNativeCoreApi();
        if (!coreHandle_ || !api.isLoaded()) return kNotInitialized;
        if (state)
            api.activate(coreHandle_, static_cast<int32_t>(sampleRate_), maxBlockSize_);
        else
            api.deactivate(coreHandle_);
        return kResultOk;
    }

    tresult SMTG_CALLCONV componentSetState(IBStream* state) override
    {
        if (!state || !coreHandle_) return kInvalidArgument;
        auto& api = nrs::core::GetNativeCoreApi();
        if (!api.isLoaded()) return kNotInitialized;

        std::vector<uint8_t> buf;
        uint8_t chunk[8192];
        while (true)
        {
            int32 bytesRead = 0;
            tresult r = state->read(chunk, static_cast<int32>(sizeof(chunk)), &bytesRead);
            if (bytesRead > 0)
                buf.insert(buf.end(), chunk, chunk + bytesRead);
            if (r != kResultOk || bytesRead <= 0)
                break;
        }
        if (buf.empty())
            return kResultOk;

        return api.load_state(coreHandle_, buf.data(), static_cast<int32_t>(buf.size())) != 0
            ? kResultOk
            : kInternalError;
    }

    tresult SMTG_CALLCONV componentGetState(IBStream* state) override
    {
        if (!state || !coreHandle_) return kInvalidArgument;
        auto& api = nrs::core::GetNativeCoreApi();
        if (!api.isLoaded()) return kNotInitialized;

        int32_t len = 0;
        uint8_t* ptr = api.save_state(coreHandle_, &len);
        if (!ptr || len <= 0)
            return kInternalError;

        int32 written = 0;
        tresult res = state->write(ptr, len, &written);
        api.free_buffer(ptr);
        return (res == kResultOk && written == len) ? kResultOk : kInternalError;
    }

    // IAudioProcessor
    tresult SMTG_CALLCONV setBusArrangements(SpeakerArrangement* inputs, int32 numIns, SpeakerArrangement* outputs, int32 numOuts) override
    {
        if (!inputs || !outputs || numIns != 1 || numOuts != 1)
            return kResultFalse;
        if ((inputs[0] == kStereo || inputs[0] == kMono) && outputs[0] == kStereo)
        {
            inputArr_ = inputs[0];
            outputArr_ = outputs[0];
            return kResultOk;
        }
        return kResultFalse;
    }

    tresult SMTG_CALLCONV getBusArrangement(BusDirection dir, int32 index, SpeakerArrangement& arr) override
    {
        if (index != 0) return kInvalidArgument;
        if (dir == kInput) { arr = inputArr_; return kResultOk; }
        if (dir == kOutput) { arr = outputArr_; return kResultOk; }
        return kInvalidArgument;
    }

    tresult SMTG_CALLCONV canProcessSampleSize(int32 symbolicSampleSize) override
    {
        return symbolicSampleSize == kSample32 ? kResultOk : kResultFalse;
    }

    uint32 SMTG_CALLCONV getLatencySamples() override
    {
        auto& api = nrs::core::GetNativeCoreApi();
        if (!coreHandle_ || !api.isLoaded()) return 0;
        int32_t lat = api.get_latency_samples(coreHandle_);
        return lat > 0 ? static_cast<uint32>(lat) : 0u;
    }

    tresult SMTG_CALLCONV setupProcessing(ProcessSetup& setup) override
    {
        if (setup.symbolicSampleSize != kSample32)
            return kInvalidArgument;
        sampleRate_ = setup.sampleRate > 1000.0 ? setup.sampleRate : 48000.0;
        maxBlockSize_ = setup.maxSamplesPerBlock > 0 ? setup.maxSamplesPerBlock : 512;
        return kResultOk;
    }

    tresult SMTG_CALLCONV setProcessing(TBool) override { return kResultOk; }

    tresult SMTG_CALLCONV process(ProcessData& data) override
    {
        auto& api = nrs::core::GetNativeCoreApi();
        if (!coreHandle_ || !api.isLoaded())
            return kNotInitialized;

        if (data.inputParameterChanges)
        {
            int32 numChanged = data.inputParameterChanges->getParameterCount();
            for (int32 i = 0; i < numChanged; i++)
            {
                IParamValueQueue* queue = data.inputParameterChanges->getParameterData(i);
                if (!queue) continue;
                ParamID pid = queue->getParameterId();
                int32 pts = queue->getPointCount();
                for (int32 p = 0; p < pts; p++)
                {
                    int32 offset = 0;
                    ParamValue val = 0.0;
                    if (queue->getPoint(p, offset, val) == kResultOk)
                        api.set_param_normalized(coreHandle_, pid, val, offset);
                }
            }
        }

        if (data.inputEvents)
        {
            int32 numEvents = data.inputEvents->getEventCount();
            for (int32 i = 0; i < numEvents; i++)
            {
                Event ev{};
                if (data.inputEvents->getEvent(i, ev) == kResultOk &&
                    ev.type == Event::kLegacyMIDICCOutEvent)
                {
                    uint8 ctrl = ev.midiCCOut.controlNumber;
                    uint8 val = static_cast<uint8>(ev.midiCCOut.value);
                    if (ctrl == kCtrlProgramChange)
                        api.midi_event(coreHandle_, 0xC0, val, 0, ev.sampleOffset);
                    else
                        api.midi_event(coreHandle_, 0xB0, ctrl, val, ev.sampleOffset);
                }
            }
        }

        double hostBpm = 0.0;
        uint8_t hasHostBpm = 0;
        uint8_t isPlaying = 0;
        double ppqPosition = 0.0;
        uint8_t hasPpq = 0;
        if (data.processContext)
        {
            if ((data.processContext->state & kTempoValid) != 0)
            {
                hostBpm = data.processContext->tempo;
                hasHostBpm = 1;
            }
            if ((data.processContext->state & kPlaying) != 0)
                isPlaying = 1;
            if ((data.processContext->state & kProjectTimeMusicValid) != 0)
            {
                ppqPosition = data.processContext->projectTimeMusic;
                hasPpq = 1;
            }
        }

        if (data.numSamples <= 0 || data.numOutputs <= 0 || !data.outputs || !data.outputs[0].channelBuffers32)
        {
            api.process_ex(coreHandle_, nullptr, nullptr, nullptr, nullptr, 0, hostBpm, hasHostBpm, isPlaying, ppqPosition, hasPpq);
            return kResultOk;
        }

        float* inL = nullptr;
        float* inR = nullptr;
        if (data.numInputs > 0 && data.inputs && data.inputs[0].channelBuffers32)
        {
            if (data.inputs[0].numChannels >= 1) inL = data.inputs[0].channelBuffers32[0];
            if (data.inputs[0].numChannels >= 2) inR = data.inputs[0].channelBuffers32[1];
            else inR = inL;
        }

        int32 outCh = data.outputs[0].numChannels;
        float* outL = outCh >= 1 ? data.outputs[0].channelBuffers32[0] : nullptr;
        float* outR = outCh >= 2 ? data.outputs[0].channelBuffers32[1] : outL;
        data.outputs[0].silenceFlags = 0;

        api.process_ex(coreHandle_, inL, inR, outL, outR, data.numSamples, hostBpm, hasHostBpm, isPlaying, ppqPosition, hasPpq);
        return kResultOk;
    }

    uint32 SMTG_CALLCONV getTailSamples() override
    {
        auto& api = nrs::core::GetNativeCoreApi();
        if (!coreHandle_ || !api.isLoaded()) return 0;
        int32_t tail = api.get_tail_samples(coreHandle_);
        return tail > 0 ? static_cast<uint32>(tail) : 0u;
    }

    // IProcessContextRequirements
    uint32 SMTG_CALLCONV getProcessContextRequirements() override
    {
        return kNeedTempo | kNeedTransportState | kNeedProjectTimeMusic;
    }

    // IEditController
    tresult SMTG_CALLCONV setComponentState(IBStream* state) override
    {
        // SingleComponentEffect shares state directly with IComponent
        (void)state;
        return kResultOk;
    }

    tresult SMTG_CALLCONV controllerSetState(IBStream*) override { return kResultOk; }
    tresult SMTG_CALLCONV controllerGetState(IBStream*) override { return kResultOk; }

    int32 SMTG_CALLCONV getParameterCount() override
    {
        auto& api = nrs::core::GetNativeCoreApi();
        return api.isLoaded() ? api.get_param_count() : 0;
    }

    tresult SMTG_CALLCONV getParameterInfo(int32 paramIndex, ParameterInfo& info) override
    {
        auto& api = nrs::core::GetNativeCoreApi();
        if (!api.isLoaded() || paramIndex < 0 || paramIndex >= api.get_param_count())
            return kInvalidArgument;

        nrs::core::NrsParamInfoNative raw{};
        if (api.get_param_info(coreHandle_, static_cast<uint32_t>(paramIndex), &raw) == 0)
            return kInvalidArgument;

        std::memset(&info, 0, sizeof(info));
        info.id = raw.id;
        CopyUtf8ToUtf16(info.title, 128, raw.nameUtf8);
        CopyUtf8ToUtf16(info.shortTitle, 128, raw.shortNameUtf8);
        CopyUtf8ToUtf16(info.units, 128, raw.unitsUtf8);
        info.stepCount = raw.stepCount;
        info.defaultNormalizedValue = raw.defaultNormalized;
        info.unitId = 0;
        info.flags = kCanAutomate;
        return kResultOk;
    }

    tresult SMTG_CALLCONV getParamStringByValue(ParamID id, ParamValue valueNormalized, String128 string) override
    {
        auto& api = nrs::core::GetNativeCoreApi();
        if (!api.isLoaded()) return kNotInitialized;
        char utf8[128]{};
        int32_t n = api.format_param_value(coreHandle_, id, valueNormalized, utf8, static_cast<int32_t>(sizeof(utf8)));
        if (n <= 0) return kInvalidArgument;
        CopyUtf8ToUtf16(string, 128, utf8);
        return kResultOk;
    }

    tresult SMTG_CALLCONV getParamValueByString(ParamID id, TChar* string, ParamValue& valueNormalized) override
    {
        auto& api = nrs::core::GetNativeCoreApi();
        if (!api.isLoaded() || !string) return kInvalidArgument;
        const auto* ws = reinterpret_cast<const wchar_t*>(string);
        if (_wcsicmp(ws, L"ON") == 0 || _wcsicmp(ws, L"Parallel") == 0)
        {
            valueNormalized = 1.0;
            return kResultOk;
        }
        if (_wcsicmp(ws, L"BYPASS") == 0 || _wcsicmp(ws, L"OFF") == 0 || _wcsicmp(ws, L"Serial") == 0)
        {
            valueNormalized = 0.0;
            return kResultOk;
        }
        wchar_t* endPtr = nullptr;
        double plain = std::wcstod(ws, &endPtr);
        if (endPtr == ws) return kInvalidArgument;
        valueNormalized = api.param_plain_to_norm(coreHandle_, id, plain);
        return kResultOk;
    }

    ParamValue SMTG_CALLCONV normalizedParamToPlain(ParamID id, ParamValue valueNormalized) override
    {
        auto& api = nrs::core::GetNativeCoreApi();
        return api.isLoaded() ? api.param_norm_to_plain(coreHandle_, id, valueNormalized) : 0.0;
    }

    ParamValue SMTG_CALLCONV plainParamToNormalized(ParamID id, ParamValue plainValue) override
    {
        auto& api = nrs::core::GetNativeCoreApi();
        return api.isLoaded() ? api.param_plain_to_norm(coreHandle_, id, plainValue) : 0.0;
    }

    ParamValue SMTG_CALLCONV getParamNormalized(ParamID id) override
    {
        auto& api = nrs::core::GetNativeCoreApi();
        return (coreHandle_ && api.isLoaded()) ? api.get_param_normalized(coreHandle_, id) : 0.0;
    }

    tresult SMTG_CALLCONV setParamNormalized(ParamID id, ParamValue value) override
    {
        auto& api = nrs::core::GetNativeCoreApi();
        if (!coreHandle_ || !api.isLoaded()) return kNotInitialized;
        api.set_param_normalized(coreHandle_, id, value, 0);
        return kResultOk;
    }

    tresult SMTG_CALLCONV setComponentHandler(IComponentHandler* handler) override
    {
        std::lock_guard<std::mutex> lk(handlerMtx_);
        if (handler_)
            handler_->release();
        handler_ = handler;
        if (handler_)
            handler_->addRef();
        return kResultOk;
    }

    IPlugView* SMTG_CALLCONV createView(FIDString name) override
    {
        if (!name || std::strcmp(name, kPlugViewEditor) != 0 || !ensureCoreInitialized())
            return nullptr;
        auto* view = new Vst3WebViewEditor(this, coreHandle_);
        std::lock_guard<std::mutex> lk(editorMtx_);
        activeEditor_ = view;
        return view;
    }

    // IMidiMapping
    tresult SMTG_CALLCONV getMidiControllerAssignment(
        int32 busIndex,
        int16 channel,
        int16 midiControllerNumber,
        ParamID& id) override
    {
        (void)channel;
        if (busIndex != 0 || !coreHandle_)
            return kResultFalse;
        auto& api = nrs::core::GetNativeCoreApi();
        if (!api.isLoaded())
            return kResultFalse;
        uint32_t mappedId = 0;
        if (api.get_midi_cc_param(coreHandle_, static_cast<int32_t>(midiControllerNumber), &mappedId) != 0)
        {
            id = static_cast<ParamID>(mappedId);
            return kResultTrue;
        }
        return kResultFalse;
    }

private:
    static void __cdecl StaticWebSendCallback(void* userData, const char* utf8Json, int32_t len)
    {
        auto* self = static_cast<NeuralRigVst3Plugin*>(userData);
        if (!self) return;
        std::lock_guard<std::mutex> lk(self->editorMtx_);
        if (self->activeEditor_)
            self->activeEditor_->enqueueWebMessage(utf8Json, len);
    }

    static void __cdecl StaticHostParamCallback(void* userData, uint32_t paramId, int32_t phase, double norm01)
    {
        auto* self = static_cast<NeuralRigVst3Plugin*>(userData);
        if (!self) return;
        IComponentHandler* h = nullptr;
        {
            std::lock_guard<std::mutex> lk(self->handlerMtx_);
            h = self->handler_;
            if (h) h->addRef();
        }
        if (!h) return;
        if (phase == 0)
            h->beginEdit(paramId);
        else if (phase == 1)
            h->performEdit(paramId, norm01);
        else if (phase == 2)
            h->endEdit(paramId);
        h->release();
    }

    bool ensureCoreInitialized()
    {
        if (coreHandle_) return true;
        auto& api = nrs::core::GetNativeCoreApi();
        if (!api.isLoaded()) return false;
        coreHandle_ = api.create(&StaticWebSendCallback, &StaticHostParamCallback, this);
        if (coreHandle_)
            api.set_host_format(coreHandle_, "VST3");
        return coreHandle_ != nullptr;
    }

    bool componentHandlerShutdown()
    {
        std::lock_guard<std::mutex> lk(handlerMtx_);
        if (handler_)
        {
            handler_->release();
            handler_ = nullptr;
        }
        return true;
    }

    void destroyCore()
    {
        if (coreHandle_)
        {
            auto& api = nrs::core::GetNativeCoreApi();
            if (api.isLoaded())
                api.destroy(coreHandle_);
            coreHandle_ = nullptr;
        }
    }

    std::atomic<uint32> refCount_{1};
    void* coreHandle_{nullptr};
    double sampleRate_{48000.0};
    int32 maxBlockSize_{512};
    SpeakerArrangement inputArr_{kStereo};
    SpeakerArrangement outputArr_{kStereo};

    std::mutex editorMtx_;
    Vst3WebViewEditor* activeEditor_{nullptr};

    std::mutex handlerMtx_;
    IComponentHandler* handler_{nullptr};
};

void DetachEditorFromPlugin(NeuralRigVst3Plugin* owner, Vst3WebViewEditor* editor)
{
    if (owner)
        owner->detachEditor(editor);
}

class NeuralRigPluginFactory final : public IPluginFactory3
{
public:
    tresult SMTG_CALLCONV queryInterface(const TUID _iid, void** obj) override
    {
        if (!obj) return kInvalidArgument;
        if (UidEqual(_iid, kFUnknownIid) ||
            UidEqual(_iid, kIPluginFactoryIid) ||
            UidEqual(_iid, kIPluginFactory2Iid) ||
            UidEqual(_iid, kIPluginFactory3Iid))
        {
            *obj = static_cast<IPluginFactory3*>(this);
            addRef();
            return kResultOk;
        }
        *obj = nullptr;
        return kNoInterface;
    }

    uint32 SMTG_CALLCONV addRef() override { return ++refCount_; }
    uint32 SMTG_CALLCONV release() override
    {
        uint32 r = --refCount_;
        if (r == 0)
            delete this;
        return r;
    }

    tresult SMTG_CALLCONV getFactoryInfo(PFactoryInfo* info) override
    {
        if (!info) return kInvalidArgument;
        std::memset(info, 0, sizeof(*info));
        CopyAsciiToChar8(info->vendor, sizeof(info->vendor), "NeuralRig Audio");
        CopyAsciiToChar8(info->url, sizeof(info->url), "https://neuralrig.studio");
        CopyAsciiToChar8(info->email, sizeof(info->email), "support@neuralrig.studio");
        info->flags = kUnicodeFactoryFlag;
        return kResultOk;
    }

    int32 SMTG_CALLCONV countClasses() override { return 1; }

    tresult SMTG_CALLCONV getClassInfo(int32 index, PClassInfo* info) override
    {
        if (index != 0 || !info) return kInvalidArgument;
        std::memset(info, 0, sizeof(*info));
        CopyUid(info->cid, kNeuralRigProcessorCid);
        info->cardinality = 0x7FFFFFFF;
        CopyAsciiToChar8(info->category, sizeof(info->category), kAudioModuleClass);
        CopyAsciiToChar8(info->name, sizeof(info->name), "NeuralRig Studio");
        return kResultOk;
    }

    tresult SMTG_CALLCONV getClassInfo2(int32 index, PClassInfo2* info) override
    {
        if (index != 0 || !info) return kInvalidArgument;
        std::memset(info, 0, sizeof(*info));
        CopyUid(info->cid, kNeuralRigProcessorCid);
        info->cardinality = 0x7FFFFFFF;
        CopyAsciiToChar8(info->category, sizeof(info->category), kAudioModuleClass);
        CopyAsciiToChar8(info->name, sizeof(info->name), "NeuralRig Studio");
        info->classFlags = kSimpleModeSupported;
        CopyAsciiToChar8(info->subCategories, sizeof(info->subCategories), "Fx");
        CopyAsciiToChar8(info->vendor, sizeof(info->vendor), "NeuralRig Audio");
        CopyAsciiToChar8(info->version, sizeof(info->version), "3.2.0");
        CopyAsciiToChar8(info->sdkVersion, sizeof(info->sdkVersion), "VST 3.7.11");
        return kResultOk;
    }

    tresult SMTG_CALLCONV getClassInfoUnicode(int32 index, PClassInfoW* info) override
    {
        if (index != 0 || !info) return kInvalidArgument;
        std::memset(info, 0, sizeof(*info));
        CopyUid(info->cid, kNeuralRigProcessorCid);
        info->cardinality = 0x7FFFFFFF;
        CopyAsciiToChar8(info->category, sizeof(info->category), kAudioModuleClass);
        CopyWideToUtf16(info->name, 64, L"NeuralRig Studio");
        info->classFlags = kSimpleModeSupported;
        CopyAsciiToChar8(info->subCategories, sizeof(info->subCategories), "Fx");
        CopyWideToUtf16(info->vendor, 64, L"NeuralRig Audio");
        CopyWideToUtf16(info->version, 64, L"3.2.0");
        CopyWideToUtf16(info->sdkVersion, 64, L"VST 3.7.11");
        return kResultOk;
    }

    tresult SMTG_CALLCONV setHostContext(FUnknown*) override { return kResultOk; }

    tresult SMTG_CALLCONV createInstance(FIDString cid, FIDString _iid, void** obj) override
    {
        if (!cid || !_iid || !obj) return kInvalidArgument;
        if (!UidEqual(cid, kNeuralRigProcessorCid))
        {
            *obj = nullptr;
            return kNoInterface;
        }
        auto* plugin = new NeuralRigVst3Plugin();
        tresult res = plugin->queryInterface(_iid, obj);
        plugin->release();
        return res;
    }

private:
    std::atomic<uint32> refCount_{1};
};

} // namespace nrs::vst3

static std::mutex gFactoryMtx;
static nrs::vst3::NeuralRigPluginFactory* gFactory = nullptr;

extern "C" bool __stdcall InitDll()
{
    return nrs::core::GetNativeCoreApi().isLoaded();
}

extern "C" bool __stdcall ExitDll()
{
    std::lock_guard<std::mutex> lk(gFactoryMtx);
    if (gFactory)
    {
        gFactory->release();
        gFactory = nullptr;
    }
    return true;
}

extern "C" nrs::vst3::IPluginFactory* __stdcall GetPluginFactory()
{
    std::lock_guard<std::mutex> lk(gFactoryMtx);
    if (!gFactory)
        gFactory = new nrs::vst3::NeuralRigPluginFactory();
    gFactory->addRef();
    return gFactory;
}
