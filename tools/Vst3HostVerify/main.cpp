#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <vector>
#include <string>
#include "../../src/NeuralRig.Vst3Native/vst3_abi.h"

using namespace nrs::vst3;

namespace {

using InitDllFn = bool(__stdcall*)();
using ExitDllFn = bool(__stdcall*)();
using GetFactoryFn = IPluginFactory*(__stdcall*)();

class MemoryStream final : public IBStream
{
public:
    tresult SMTG_CALLCONV queryInterface(const TUID _iid, void** obj) override
    {
        if (!obj) return kInvalidArgument;
        if (UidEqual(_iid, kFUnknownIid) || UidEqual(_iid, kIBStreamIid))
        {
            *obj = static_cast<IBStream*>(this);
            addRef();
            return kResultOk;
        }
        *obj = nullptr;
        return kNoInterface;
    }

    uint32 SMTG_CALLCONV addRef() override { return ++ref_; }
    uint32 SMTG_CALLCONV release() override { return --ref_; }

    tresult SMTG_CALLCONV read(void* buffer, int32 numBytes, int32* numBytesRead) override
    {
        if (!buffer || numBytes < 0) return kInvalidArgument;
        int32 avail = static_cast<int32>(data_.size()) - pos_;
        int32 toRead = avail < numBytes ? (avail > 0 ? avail : 0) : numBytes;
        if (toRead > 0)
        {
            std::memcpy(buffer, data_.data() + pos_, static_cast<size_t>(toRead));
            pos_ += toRead;
        }
        if (numBytesRead) *numBytesRead = toRead;
        return kResultOk;
    }

    tresult SMTG_CALLCONV write(void* buffer, int32 numBytes, int32* numBytesWritten) override
    {
        if (!buffer || numBytes < 0) return kInvalidArgument;
        size_t endPos = static_cast<size_t>(pos_) + static_cast<size_t>(numBytes);
        if (endPos > data_.size())
            data_.resize(endPos);
        std::memcpy(data_.data() + pos_, buffer, static_cast<size_t>(numBytes));
        pos_ += numBytes;
        if (numBytesWritten) *numBytesWritten = numBytes;
        return kResultOk;
    }

    tresult SMTG_CALLCONV seek(int64 pos, int32 mode, int64* result) override
    {
        int64 base = 0;
        if (mode == kIBSeekSet) base = 0;
        else if (mode == kIBSeekCur) base = pos_;
        else if (mode == kIBSeekEnd) base = static_cast<int64>(data_.size());
        else return kInvalidArgument;
        int64 next = base + pos;
        if (next < 0) next = 0;
        pos_ = static_cast<int32>(next);
        if (result) *result = pos_;
        return kResultOk;
    }

    tresult SMTG_CALLCONV tell(int64* pos) override
    {
        if (!pos) return kInvalidArgument;
        *pos = pos_;
        return kResultOk;
    }

    void resetCursor() { pos_ = 0; }
    size_t size() const { return data_.size(); }

private:
    uint32 ref_{1};
    std::vector<uint8_t> data_;
    int32 pos_{0};
};

class HostComponentHandler final : public IComponentHandler
{
public:
    tresult SMTG_CALLCONV queryInterface(const TUID _iid, void** obj) override
    {
        if (!obj) return kInvalidArgument;
        if (UidEqual(_iid, kFUnknownIid) || UidEqual(_iid, kIComponentHandlerIid))
        {
            *obj = static_cast<IComponentHandler*>(this);
            addRef();
            return kResultOk;
        }
        *obj = nullptr;
        return kNoInterface;
    }

    uint32 SMTG_CALLCONV addRef() override { return ++ref_; }
    uint32 SMTG_CALLCONV release() override { return --ref_; }

    tresult SMTG_CALLCONV beginEdit(ParamID id) override { lastBeginId = id; editCount++; return kResultOk; }
    tresult SMTG_CALLCONV performEdit(ParamID id, ParamValue val) override { lastPerformId = id; lastPerformVal = val; return kResultOk; }
    tresult SMTG_CALLCONV endEdit(ParamID id) override { lastEndId = id; return kResultOk; }
    tresult SMTG_CALLCONV restartComponent(int32) override { return kResultOk; }

    ParamID lastBeginId{0};
    ParamID lastPerformId{0};
    ParamID lastEndId{0};
    ParamValue lastPerformVal{0.0};
    int editCount{0};

private:
    uint32 ref_{1};
};

class HostPlugFrame final : public IPlugFrame
{
public:
    tresult SMTG_CALLCONV queryInterface(const TUID _iid, void** obj) override
    {
        if (!obj) return kInvalidArgument;
        if (UidEqual(_iid, kFUnknownIid) || UidEqual(_iid, kIPlugFrameIid))
        {
            *obj = static_cast<IPlugFrame*>(this);
            addRef();
            return kResultOk;
        }
        *obj = nullptr;
        return kNoInterface;
    }

    uint32 SMTG_CALLCONV addRef() override { return ++ref_; }
    uint32 SMTG_CALLCONV release() override { return --ref_; }

    tresult SMTG_CALLCONV resizeView(IPlugView* view, ViewRect* newSize) override
    {
        if (!view || !newSize) return kInvalidArgument;
        return view->onSize(newSize);
    }

private:
    uint32 ref_{1};
};

class HostParamQueue final : public IParamValueQueue
{
public:
    explicit HostParamQueue(ParamID id = 0) : id_(id) {}

    void set(ParamID id, int32 offset, ParamValue value)
    {
        id_ = id;
        offset_ = offset;
        val_ = value;
    }

    tresult SMTG_CALLCONV queryInterface(const TUID _iid, void** obj) override
    {
        if (!obj) return kInvalidArgument;
        if (UidEqual(_iid, kFUnknownIid) || UidEqual(_iid, kIParamValueQueueIid))
        {
            *obj = static_cast<IParamValueQueue*>(this);
            addRef();
            return kResultOk;
        }
        *obj = nullptr;
        return kNoInterface;
    }

    uint32 SMTG_CALLCONV addRef() override { return ++ref_; }
    uint32 SMTG_CALLCONV release() override { return --ref_; }

    ParamID SMTG_CALLCONV getParameterId() override { return id_; }
    int32 SMTG_CALLCONV getPointCount() override { return 1; }
    tresult SMTG_CALLCONV getPoint(int32 index, int32& sampleOffset, ParamValue& value) override
    {
        if (index != 0) return kInvalidArgument;
        sampleOffset = offset_;
        value = val_;
        return kResultOk;
    }
    tresult SMTG_CALLCONV addPoint(int32, ParamValue, int32&) override { return kNotImplemented; }

private:
    uint32 ref_{1};
    ParamID id_{0};
    int32 offset_{0};
    ParamValue val_{0.0};
};

class HostParamChanges final : public IParameterChanges
{
public:
    tresult SMTG_CALLCONV queryInterface(const TUID _iid, void** obj) override
    {
        if (!obj) return kInvalidArgument;
        if (UidEqual(_iid, kFUnknownIid) || UidEqual(_iid, kIParameterChangesIid))
        {
            *obj = static_cast<IParameterChanges*>(this);
            addRef();
            return kResultOk;
        }
        *obj = nullptr;
        return kNoInterface;
    }

    uint32 SMTG_CALLCONV addRef() override { return ++ref_; }
    uint32 SMTG_CALLCONV release() override { return --ref_; }

    void setSingle(ParamID id, int32 offset, ParamValue val)
    {
        q_.set(id, offset, val);
        count_ = 1;
    }

    void clear() { count_ = 0; }

    int32 SMTG_CALLCONV getParameterCount() override { return count_; }
    IParamValueQueue* SMTG_CALLCONV getParameterData(int32 index) override { return index == 0 && count_ == 1 ? &q_ : nullptr; }
    IParamValueQueue* SMTG_CALLCONV addParameterData(const ParamID&, int32&) override { return nullptr; }

private:
    uint32 ref_{1};
    HostParamQueue q_;
    int32 count_{0};
};

class HostEventList final : public IEventList
{
public:
    tresult SMTG_CALLCONV queryInterface(const TUID _iid, void** obj) override
    {
        if (!obj) return kInvalidArgument;
        if (UidEqual(_iid, kFUnknownIid) || UidEqual(_iid, kIEventListIid))
        {
            *obj = static_cast<IEventList*>(this);
            addRef();
            return kResultOk;
        }
        *obj = nullptr;
        return kNoInterface;
    }

    uint32 SMTG_CALLCONV addRef() override { return ++ref_; }
    uint32 SMTG_CALLCONV release() override { return --ref_; }

    void clear() { events_.clear(); }

    void addLegacyCc(uint8 ctrlNum, int8 val, int32 offset)
    {
        Event ev{};
        ev.busIndex = 0;
        ev.sampleOffset = offset;
        ev.ppqPosition = 0.0;
        ev.flags = Event::kIsLive;
        ev.type = Event::kLegacyMIDICCOutEvent;
        ev.midiCCOut.controlNumber = ctrlNum;
        ev.midiCCOut.channel = 0;
        ev.midiCCOut.value = val;
        ev.midiCCOut.value2 = 0;
        events_.push_back(ev);
    }

    int32 SMTG_CALLCONV getEventCount() override { return static_cast<int32>(events_.size()); }

    tresult SMTG_CALLCONV getEvent(int32 index, Event& e) override
    {
        if (index < 0 || index >= static_cast<int32>(events_.size()))
            return kInvalidArgument;
        e = events_[static_cast<size_t>(index)];
        return kResultOk;
    }

    tresult SMTG_CALLCONV addEvent(Event& e) override
    {
        events_.push_back(e);
        return kResultOk;
    }

private:
    uint32 ref_{1};
    std::vector<Event> events_;
};

std::wstring GetArgOrDefaultVst3Path(int argc, char** argv, bool& noGui)
{
    noGui = false;
    std::wstring pathArg;
    for (int i = 1; i < argc; i++)
    {
        if (!argv[i] || argv[i][0] == '\0') continue;
        if (std::strcmp(argv[i], "--no-gui") == 0)
        {
            noGui = true;
            continue;
        }
        if (pathArg.empty())
        {
            int n = MultiByteToWideChar(CP_UTF8, 0, argv[i], -1, nullptr, 0);
            pathArg.assign(static_cast<size_t>(n > 0 ? n - 1 : 0), L'\0');
            if (n > 1) MultiByteToWideChar(CP_UTF8, 0, argv[i], -1, pathArg.data(), n);
        }
    }
    return pathArg.empty() ? L"dist-vst3\\NeuralRigStudio.vst3\\Contents\\x86_64-win\\NeuralRigStudio.vst3" : pathArg;
}

LRESULT CALLBACK TestHostWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

void PumpMessagesForMs(DWORD ms)
{
    ULONGLONG start = GetTickCount64();
    MSG msg{};
    while (GetTickCount64() - start < ms)
    {
        while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE))
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        Sleep(10);
    }
}

} // namespace

int main(int argc, char** argv)
{
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    bool noGui = false;
    std::wstring vst3Path = GetArgOrDefaultVst3Path(argc, argv, noGui);
    wprintf(L"[Vst3HostVerify] Loading: %s\n", vst3Path.c_str());

    HMODULE mod = LoadLibraryExW(vst3Path.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!mod)
    {
        fprintf(stderr, "FAIL: LoadLibraryW failed (err=%lu)\n", GetLastError());
        return 1;
    }

    auto initFn = reinterpret_cast<InitDllFn>(GetProcAddress(mod, "InitDll"));
    auto exitFn = reinterpret_cast<ExitDllFn>(GetProcAddress(mod, "ExitDll"));
    auto getFactoryFn = reinterpret_cast<GetFactoryFn>(GetProcAddress(mod, "GetPluginFactory"));
    if (!initFn || !exitFn || !getFactoryFn)
    {
        fprintf(stderr, "FAIL: Missing VST3 entry exports\n");
        return 2;
    }

    if (!initFn())
    {
        fprintf(stderr, "FAIL: InitDll() returned false (NeuralRig.NativeCore.dll failed to load)\n");
        return 3;
    }

    IPluginFactory* factory = getFactoryFn();
    if (!factory)
    {
        fprintf(stderr, "FAIL: GetPluginFactory() returned null\n");
        return 4;
    }

    IPluginFactory3* factory3 = nullptr;
    if (factory->queryInterface(kIPluginFactory3Iid.b, reinterpret_cast<void**>(&factory3)) != kResultOk || !factory3)
    {
        fprintf(stderr, "FAIL: IPluginFactory3 queryInterface failed\n");
        return 5;
    }

    PFactoryInfo fInfo{};
    PClassInfo2 cInfo2{};
    PClassInfoW cInfoW{};
    if (factory3->countClasses() != 1 ||
        factory3->getFactoryInfo(&fInfo) != kResultOk ||
        factory3->getClassInfo2(0, &cInfo2) != kResultOk ||
        factory3->getClassInfoUnicode(0, &cInfoW) != kResultOk)
    {
        fprintf(stderr, "FAIL: Factory metadata verification failed\n");
        return 6;
    }
    printf("[Vst3HostVerify] Factory OK: vendor=%s, class=%s, sdk=%s\n", fInfo.vendor, cInfo2.name, cInfo2.sdkVersion);

    IComponent* compA = nullptr;
    if (factory3->createInstance(reinterpret_cast<FIDString>(kNeuralRigProcessorCid.b), reinterpret_cast<FIDString>(kIComponentIid.b), reinterpret_cast<void**>(&compA)) != kResultOk || !compA)
    {
        fprintf(stderr, "FAIL: createInstance(IComponent) failed\n");
        return 7;
    }

    if (compA->initialize(nullptr) != kResultOk)
    {
        fprintf(stderr, "FAIL: compA->initialize failed\n");
        return 8;
    }

    IAudioProcessor* procA = nullptr;
    IEditController* ctrlA = nullptr;
    IProcessContextRequirements* reqA = nullptr;
    IMidiMapping* midiA = nullptr;
    if (compA->queryInterface(kIAudioProcessorIid.b, reinterpret_cast<void**>(&procA)) != kResultOk ||
        compA->queryInterface(kIEditControllerIid.b, reinterpret_cast<void**>(&ctrlA)) != kResultOk ||
        compA->queryInterface(kIProcessContextRequirementsIid.b, reinterpret_cast<void**>(&reqA)) != kResultOk ||
        compA->queryInterface(kIMidiMappingIid.b, reinterpret_cast<void**>(&midiA)) != kResultOk)
    {
        fprintf(stderr, "FAIL: SingleComponentEffect COM queryInterface failed\n");
        return 9;
    }

    FUnknown* unkComp = nullptr;
    FUnknown* unkProc = nullptr;
    FUnknown* unkCtrl = nullptr;
    FUnknown* unkMidi = nullptr;
    compA->queryInterface(kFUnknownIid.b, reinterpret_cast<void**>(&unkComp));
    procA->queryInterface(kFUnknownIid.b, reinterpret_cast<void**>(&unkProc));
    ctrlA->queryInterface(kFUnknownIid.b, reinterpret_cast<void**>(&unkCtrl));
    midiA->queryInterface(kFUnknownIid.b, reinterpret_cast<void**>(&unkMidi));
    if (unkComp == nullptr || unkComp != unkProc || unkComp != unkCtrl || unkComp != unkMidi)
    {
        fprintf(stderr, "FAIL: COM FUnknown identity mismatch (%p vs %p vs %p vs %p)\n", unkComp, unkProc, unkCtrl, unkMidi);
        return 10;
    }
    unkComp->release();
    unkProc->release();
    unkCtrl->release();
    unkMidi->release();

    // Verify MIDI kEvent Input Bus + IMidiMapping + ProcessContextRequirements
    BusInfo evBus{};
    if (compA->getBusCount(kEvent, kInput) != 1 ||
        compA->getBusInfo(kEvent, kInput, 0, evBus) != kResultOk ||
        compA->activateBus(kEvent, kInput, 0, 1) != kResultOk)
    {
        fprintf(stderr, "FAIL: VST3 kEvent MIDI input bus verification failed\n");
        return 11;
    }

    ParamID exprPid = 0xFFFFFFFFu;
    ParamID pcPid = 0xFFFFFFFFu;
    if (midiA->getMidiControllerAssignment(0, 0, kCtrlExpression, exprPid) != kResultTrue ||
        midiA->getMidiControllerAssignment(0, 0, kCtrlProgramChange, pcPid) != kResultTrue ||
        pcPid != 5)
    {
        fprintf(stderr, "FAIL: IMidiMapping verification failed (expr=%u, pc=%u)\n", exprPid, pcPid);
        return 11;
    }

    uint32 reqFlags = reqA->getProcessContextRequirements();
    if ((reqFlags & (kNeedTempo | kNeedTransportState | kNeedProjectTimeMusic)) !=
        (kNeedTempo | kNeedTransportState | kNeedProjectTimeMusic))
    {
        fprintf(stderr, "FAIL: Missing ProcessContextRequirements flags (got=0x%X)\n", reqFlags);
        return 11;
    }

    TUID ctrlCid{};
    if (compA->getControllerClassId(ctrlCid) != kNoInterface)
    {
        fprintf(stderr, "FAIL: SingleComponentEffect getControllerClassId must return kNoInterface\n");
        return 12;
    }

    int32 paramCount = ctrlA->getParameterCount();
    if (paramCount != 160)
    {
        fprintf(stderr, "FAIL: Expected 160 parameters, got %d\n", paramCount);
        return 13;
    }

    for (int32 p = 0; p < paramCount; p++)
    {
        ParameterInfo pInfo{};
        String128 strBuf{};
        if (ctrlA->getParameterInfo(p, pInfo) != kResultOk ||
            ctrlA->getParamStringByValue(pInfo.id, pInfo.defaultNormalizedValue, strBuf) != kResultOk)
        {
            fprintf(stderr, "FAIL: Param %d metadata/format failed\n", p);
            return 14;
        }
    }

    HostComponentHandler compHandler;
    ctrlA->setComponentHandler(&compHandler);

    SpeakerArrangement inArr = kStereo;
    SpeakerArrangement outArr = kStereo;
    ProcessSetup setup{kRealtime, kSample32, 256, 48000.0};
    if (procA->setBusArrangements(&inArr, 1, &outArr, 1) != kResultOk ||
        procA->setupProcessing(setup) != kResultOk ||
        compA->setActive(1) != kResultOk ||
        procA->setProcessing(1) != kResultOk)
    {
        fprintf(stderr, "FAIL: Audio setup/activation failed\n");
        return 15;
    }

    uint32 initLatency = procA->getLatencySamples();
    uint32 initTail = procA->getTailSamples();
    if (initLatency > 4096 || initTail > 48000u * 12u)
    {
        fprintf(stderr, "FAIL: Out-of-range latency/tail from processor (lat=%u, tail=%u)\n", initLatency, initTail);
        return 15;
    }
    printf("[Vst3HostVerify] Latency/Tail OK: lat=%u, tail=%u\n", initLatency, initTail);

    constexpr int kFrames = 256;
    std::vector<float> inL(kFrames), inR(kFrames), outL(kFrames), outR(kFrames);
    float* inPtrs[2] = {inL.data(), inR.data()};
    float* outPtrs[2] = {outL.data(), outR.data()};
    AudioBusBuffers inBus{2, 0, {inPtrs}};
    AudioBusBuffers outBus{2, 0, {outPtrs}};
    ProcessContext pCtx{};
    pCtx.state = kTempoValid | kPlaying | kProjectTimeMusicValid;
    pCtx.tempo = 142.0;

    HostParamChanges pChanges;
    HostEventList evList;
    double sumSq = 0.0;
    for (int blk = 0; blk < 1000; blk++)
    {
        for (int i = 0; i < kFrames; i++)
        {
            float s = 0.25f * std::sinf(2.0f * 3.14159265f * 220.0f * static_cast<float>(blk * kFrames + i) / 48000.0f);
            inL[i] = s;
            inR[i] = s;
        }
        pChanges.clear();
        evList.clear();
        pCtx.projectTimeMusic = (static_cast<double>(blk) * kFrames / 48000.0) * (142.0 / 60.0);
        if (blk == 100) pChanges.setSingle(0, 64, 0.65);  // InputGainDb sample-accurate automation
        if (blk == 200) pChanges.setSingle(26, 128, 0.72); // Slot 02 Knob 0
        if (blk == 250) evList.addLegacyCc(static_cast<uint8>(kCtrlExpression), 84, 32);

        ProcessData pData{kRealtime, kSample32, kFrames, 1, 1, &inBus, &outBus, &pChanges, nullptr, &evList, nullptr, &pCtx};
        if (procA->process(pData) != kResultOk)
        {
            fprintf(stderr, "FAIL: process() failed at block %d\n", blk);
            return 16;
        }
        for (int i = 0; i < kFrames; i++)
        {
            if (!std::isfinite(outL[i]) || !std::isfinite(outR[i]))
            {
                fprintf(stderr, "FAIL: Non-finite audio sample at block %d sample %d\n", blk, i);
                return 17;
            }
            if (blk >= 900)
                sumSq += static_cast<double>(outL[i]) * outL[i] + static_cast<double>(outR[i]) * outR[i];
        }
    }
    double rms = std::sqrt(sumSq / (100.0 * kFrames * 2.0));
    if (!(rms > 0.0005 && rms < 10.0))
    {
        fprintf(stderr, "FAIL: Audio RMS out of range: %f\n", rms);
        return 18;
    }

    // Verify Host Tempo Sync synced parameter 6 (TempoBpm) to 142 BPM
    double tempoPlain = ctrlA->normalizedParamToPlain(6, ctrlA->getParamNormalized(6));
    if (std::fabs(tempoPlain - 142.0) > 1.0)
    {
        fprintf(stderr, "FAIL: Host BPM sync failed (expected ~142, got %.2f)\n", tempoPlain);
        return 19;
    }

    // State save & restore equivalence across fresh instance
    MemoryStream stateStream;
    if (compA->getState(&stateStream) != kResultOk || stateStream.size() < 16)
    {
        fprintf(stderr, "FAIL: getState failed (size=%zu)\n", stateStream.size());
        return 20;
    }

    IComponent* compB = nullptr;
    factory3->createInstance(reinterpret_cast<FIDString>(kNeuralRigProcessorCid.b), reinterpret_cast<FIDString>(kIComponentIid.b), reinterpret_cast<void**>(&compB));
    compB->initialize(nullptr);
    IAudioProcessor* procB = nullptr;
    IEditController* ctrlB = nullptr;
    compB->queryInterface(kIAudioProcessorIid.b, reinterpret_cast<void**>(&procB));
    compB->queryInterface(kIEditControllerIid.b, reinterpret_cast<void**>(&ctrlB));
    stateStream.resetCursor();
    if (compB->setState(&stateStream) != kResultOk)
    {
        fprintf(stderr, "FAIL: compB->setState failed\n");
        return 21;
    }
    double restoredInGain = ctrlB->getParamNormalized(0);
    double restoredSlot2Knob0 = ctrlB->getParamNormalized(26);
    if (std::fabs(restoredInGain - 0.65) > 0.01 || std::fabs(restoredSlot2Knob0 - 0.72) > 0.01)
    {
        fprintf(stderr, "FAIL: Restored params mismatch (p0=%.4f, p26=%.4f)\n", restoredInGain, restoredSlot2Knob0);
        return 22;
    }

    if (procB->setBusArrangements(&inArr, 1, &outArr, 1) != kResultOk ||
        procB->setupProcessing(setup) != kResultOk ||
        compB->setActive(1) != kResultOk ||
        procB->setProcessing(1) != kResultOk)
    {
        fprintf(stderr, "FAIL: compB audio setup failed\n");
        return 22;
    }
    pChanges.clear();
    evList.clear();
    for (int i = 0; i < kFrames; i++)
    {
        float s = 0.25f * std::sinf(2.0f * 3.14159265f * 220.0f * static_cast<float>(i) / 48000.0f);
        inL[i] = s;
        inR[i] = s;
    }
    ProcessData pDataB{kRealtime, kSample32, kFrames, 1, 1, &inBus, &outBus, &pChanges, nullptr, &evList, nullptr, &pCtx};
    if (procB->process(pDataB) != kResultOk)
    {
        fprintf(stderr, "FAIL: compB process failed\n");
        return 22;
    }
    double sumSqB = 0.0;
    for (int i = 0; i < kFrames; i++)
    {
        if (!std::isfinite(outL[i]) || !std::isfinite(outR[i]))
        {
            fprintf(stderr, "FAIL: compB non-finite output sample %d\n", i);
            return 22;
        }
        sumSqB += static_cast<double>(outL[i]) * outL[i] + static_cast<double>(outR[i]) * outR[i];
    }
    double rmsB = std::sqrt(sumSqB / (kFrames * 2.0));
    if (!(rmsB > 0.0005 && rmsB < 10.0))
    {
        fprintf(stderr, "FAIL: compB RMS out of range: %f\n", rmsB);
        return 22;
    }
    procB->setProcessing(0);
    compB->setActive(0);
    procB->release();
    ctrlB->release();
    compB->terminate();
    compB->release();

    if (!noGui)
    {
        // Real Win32 HWND + WebView2 IPlugView Test
        WNDCLASSEXW wc{};
        wc.cbSize = sizeof(wc);
        wc.lpfnWndProc = &TestHostWndProc;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"Vst3HostVerifyWindow";
        RegisterClassExW(&wc);

        HWND hostHwnd = CreateWindowExW(
            0,
            wc.lpszClassName,
            L"VST3 Host Verify",
            WS_OVERLAPPEDWINDOW,
            CW_USEDEFAULT, CW_USEDEFAULT, 1280, 780,
            nullptr, nullptr, wc.hInstance, nullptr);

        IPlugView* view = ctrlA->createView(kPlugViewEditor);
        if (!view || view->isPlatformTypeSupported(kPlatformTypeHWND) != kResultOk)
        {
            fprintf(stderr, "FAIL: createView('editor') or HWND support failed\n");
            return 23;
        }

        HostPlugFrame plugFrame;
        view->setFrame(&plugFrame);
        ViewRect initRect{};
        view->getSize(&initRect);
        if (initRect.getWidth() != 1280 || initRect.getHeight() != 780)
        {
            fprintf(stderr, "FAIL: Unexpected initial view rect %dx%d\n", initRect.getWidth(), initRect.getHeight());
            return 24;
        }

        if (view->attached(hostHwnd, kPlatformTypeHWND) != kResultOk)
        {
            fprintf(stderr, "FAIL: view->attached(HWND) failed\n");
            return 25;
        }

        ShowWindow(hostHwnd, SW_SHOWNA);
        PumpMessagesForMs(2500);

        ViewRect resized{0, 0, 1120, 700};
        if (view->onSize(&resized) != kResultOk)
        {
            fprintf(stderr, "FAIL: view->onSize failed\n");
            return 26;
        }
        PumpMessagesForMs(500);

        view->removed();
        view->setFrame(nullptr);
        view->release();
        DestroyWindow(hostHwnd);
    }

    ctrlA->setComponentHandler(nullptr);
    procA->setProcessing(0);
    compA->setActive(0);
    midiA->release();
    reqA->release();
    procA->release();
    ctrlA->release();
    compA->terminate();
    compA->release();

    factory3->release();
    factory->release();
    exitFn();
    FreeLibrary(mod);
    CoUninitialize();

    printf("[PASS] Vst3HostVerify: 160 params, MIDI+IMidiMapping, PDC=%u, Tail=%u, 1000 RT blocks (RMS=%.4f, Tempo=%.1f BPM), State round-trip (%zu B), %s\n",
           initLatency, initTail, rms, tempoPlain, stateStream.size(),
           noGui ? "Headless (--no-gui) OK" : "Real HWND+WebView2 GUI 1280x780->1120x700 OK");
    return 0;
}
