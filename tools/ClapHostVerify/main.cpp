#define CLAP_EXPORT
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>
#include <clap/clap.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace {

class MemoryStream
{
public:
    std::vector<uint8_t> data;
    size_t readPos{0};

    static int64_t CLAP_ABI ReadFn(const clap_istream_t* stream, void* buffer, uint64_t size)
    {
        auto* self = static_cast<MemoryStream*>(stream->ctx);
        if (!self || !buffer) return -1;
        size_t avail = self->data.size() - self->readPos;
        size_t n = std::min(avail, static_cast<size_t>(size));
        if (n > 0)
        {
            std::memcpy(buffer, self->data.data() + self->readPos, n);
            self->readPos += n;
        }
        return static_cast<int64_t>(n);
    }

    static int64_t CLAP_ABI WriteFn(const clap_ostream_t* stream, const void* buffer, uint64_t size)
    {
        auto* self = static_cast<MemoryStream*>(stream->ctx);
        if (!self || !buffer) return -1;
        const auto* bytes = static_cast<const uint8_t*>(buffer);
        self->data.insert(self->data.end(), bytes, bytes + size);
        return static_cast<int64_t>(size);
    }
};

class HostEventVec
{
public:
    void clear() { events_.clear(); }

    void pushParamValue(clap_id paramId, double plainVal, uint32_t sampleTime = 0)
    {
        clap_event_param_value_t ev{};
        ev.header.size = sizeof(clap_event_param_value_t);
        ev.header.time = sampleTime;
        ev.header.space_id = CLAP_CORE_EVENT_SPACE_ID;
        ev.header.type = CLAP_EVENT_PARAM_VALUE;
        ev.header.flags = 0;
        ev.param_id = paramId;
        ev.cookie = nullptr;
        ev.note_id = -1;
        ev.port_index = -1;
        ev.channel = -1;
        ev.key = -1;
        ev.value = plainVal;
        Stored s{};
        std::memcpy(&s.param, &ev, sizeof(ev));
        events_.push_back(s);
    }

    void pushMidi(uint8_t status, uint8_t d1, uint8_t d2, uint32_t sampleTime = 0)
    {
        clap_event_midi_t ev{};
        ev.header.size = sizeof(clap_event_midi_t);
        ev.header.time = sampleTime;
        ev.header.space_id = CLAP_CORE_EVENT_SPACE_ID;
        ev.header.type = CLAP_EVENT_MIDI;
        ev.header.flags = 0;
        ev.port_index = 0;
        ev.data[0] = status;
        ev.data[1] = d1;
        ev.data[2] = d2;
        Stored s{};
        std::memcpy(&s.midi, &ev, sizeof(ev));
        events_.push_back(s);
    }

    clap_input_events_t asClapInput()
    {
        return clap_input_events_t{this, &SizeFn, &GetFn};
    }

private:
    union Stored
    {
        clap_event_header_t hdr;
        clap_event_param_value_t param;
        clap_event_midi_t midi;
    };

    static uint32_t CLAP_ABI SizeFn(const clap_input_events_t* list)
    {
        auto* self = static_cast<const HostEventVec*>(list->ctx);
        return static_cast<uint32_t>(self->events_.size());
    }

    static const clap_event_header_t* CLAP_ABI GetFn(const clap_input_events_t* list, uint32_t index)
    {
        auto* self = static_cast<const HostEventVec*>(list->ctx);
        return index < self->events_.size() ? &self->events_[index].hdr : nullptr;
    }

    std::vector<Stored> events_;
};

static bool CLAP_ABI DummyOutPush(const clap_output_events_t*, const clap_event_header_t*) { return true; }
static const void* CLAP_ABI HostGetExt(const clap_host_t*, const char*) { return nullptr; }
static void CLAP_ABI HostNoOp(const clap_host_t*) {}

LRESULT CALLBACK VerifyHostWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

void PumpMessagesForMs(DWORD ms)
{
    DWORD start = GetTickCount();
    MSG msg{};
    while (GetTickCount() - start < ms)
    {
        while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE))
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        Sleep(15);
    }
}

} // namespace

int wmain(int argc, wchar_t** argv)
{
    if (argc < 2)
    {
        std::fprintf(stderr, "Usage: ClapHostVerify.exe <path-to-NeuralRigStudio.clap> [--no-gui]\n");
        return 1;
    }

    bool noGui = false;
    for (int i = 2; i < argc; i++)
    {
        if (wcscmp(argv[i], L"--no-gui") == 0)
            noGui = true;
    }

    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    HMODULE mod = LoadLibraryExW(argv[1], nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!mod) mod = LoadLibraryW(argv[1]);
    if (!mod)
    {
        std::fprintf(stderr, "FAIL: LoadLibraryW failed (err=%lu)\n", GetLastError());
        return 2;
    }

    const auto* entry = reinterpret_cast<const clap_plugin_entry_t*>(GetProcAddress(mod, "clap_entry"));
    if (!entry || !clap_version_is_compatible(entry->clap_version))
    {
        std::fprintf(stderr, "FAIL: Invalid clap_entry symbol or version\n");
        return 3;
    }

    // Verify CLAP 1.2.0 init/deinit re-entrancy & balance
    if (!entry->init("") || !entry->init(""))
    {
        std::fprintf(stderr, "FAIL: clap_entry.init failed\n");
        return 4;
    }
    entry->deinit();

    const auto* factory = static_cast<const clap_plugin_factory_t*>(entry->get_factory(CLAP_PLUGIN_FACTORY_ID));
    if (!factory || factory->get_plugin_count(factory) != 1)
    {
        std::fprintf(stderr, "FAIL: CLAP factory verification failed\n");
        return 5;
    }

    const clap_plugin_descriptor_t* desc = factory->get_plugin_descriptor(factory, 0);
    if (!desc || std::strcmp(desc->id, "com.neuralrig.studio.vstclap") != 0 ||
        std::strcmp(desc->version, "3.2.0") != 0)
    {
        std::fprintf(stderr, "FAIL: Descriptor mismatch (id=%s, ver=%s)\n",
                     desc ? desc->id : "(null)", desc ? desc->version : "(null)");
        return 6;
    }
    std::printf("[ClapHostVerify] Descriptor OK: id=%s, name=%s, ver=%s\n", desc->id, desc->name, desc->version);

    clap_host_t host{
        CLAP_VERSION_INIT,
        nullptr,
        "ClapHostVerify",
        "NeuralRig Audio",
        "https://neuralrigstudio.seltum.xyz",
        "3.2.0",
        &HostGetExt,
        &HostNoOp,
        &HostNoOp,
        &HostNoOp,
    };

    const clap_plugin_t* plugA = factory->create_plugin(factory, &host, desc->id);
    if (!plugA || !plugA->init(plugA))
    {
        std::fprintf(stderr, "FAIL: create_plugin / plugin->init failed\n");
        return 7;
    }

    const auto* audioPorts = static_cast<const clap_plugin_audio_ports_t*>(plugA->get_extension(plugA, CLAP_EXT_AUDIO_PORTS));
    const auto* notePorts = static_cast<const clap_plugin_note_ports_t*>(plugA->get_extension(plugA, CLAP_EXT_NOTE_PORTS));
    const auto* params = static_cast<const clap_plugin_params_t*>(plugA->get_extension(plugA, CLAP_EXT_PARAMS));
    const auto* stateExt = static_cast<const clap_plugin_state_t*>(plugA->get_extension(plugA, CLAP_EXT_STATE));
    const auto* latencyExt = static_cast<const clap_plugin_latency_t*>(plugA->get_extension(plugA, CLAP_EXT_LATENCY));
    const auto* tailExt = static_cast<const clap_plugin_tail_t*>(plugA->get_extension(plugA, CLAP_EXT_TAIL));
    const auto* guiExt = static_cast<const clap_plugin_gui_t*>(plugA->get_extension(plugA, CLAP_EXT_GUI));
    if (!audioPorts || !notePorts || !params || !stateExt || !latencyExt || !tailExt || !guiExt)
    {
        std::fprintf(stderr, "FAIL: Missing required CLAP extension(s)\n");
        return 8;
    }

    clap_audio_port_info_t apIn{}, apOut{};
    clap_note_port_info_t npIn{};
    if (audioPorts->count(plugA, true) != 1 || audioPorts->count(plugA, false) != 1 ||
        !audioPorts->get(plugA, 0, true, &apIn) || !audioPorts->get(plugA, 0, false, &apOut) ||
        apIn.channel_count != 2 || apOut.channel_count != 2 ||
        notePorts->count(plugA, true) != 1 || !notePorts->get(plugA, 0, true, &npIn))
    {
        std::fprintf(stderr, "FAIL: Audio/Note ports configuration mismatch\n");
        return 9;
    }

    uint32_t paramCount = params->count(plugA);
    if (paramCount != 160)
    {
        std::fprintf(stderr, "FAIL: Expected 160 params, got %u\n", paramCount);
        return 10;
    }
    for (uint32_t p = 0; p < paramCount; p++)
    {
        clap_param_info_t pInfo{};
        double val = 0.0;
        char txt[64]{};
        if (!params->get_info(plugA, p, &pInfo) ||
            !params->get_value(plugA, pInfo.id, &val) ||
            !params->value_to_text(plugA, pInfo.id, val, txt, sizeof(txt)))
        {
            std::fprintf(stderr, "FAIL: Param %u metadata/value/format failed\n", p);
            return 11;
        }
    }

    if (!plugA->activate(plugA, 48000.0, 32, 256) || !plugA->start_processing(plugA))
    {
        std::fprintf(stderr, "FAIL: activate / start_processing failed\n");
        return 12;
    }

    uint32_t initLatency = latencyExt->get(plugA);
    uint32_t initTail = tailExt->get(plugA);
    if (initLatency > 4096 || initTail > 48000u * 12u)
    {
        std::fprintf(stderr, "FAIL: Out-of-range latency/tail (lat=%u, tail=%u)\n", initLatency, initTail);
        return 13;
    }

    constexpr int kFrames = 256;
    std::vector<float> inL(kFrames), inR(kFrames), outL(kFrames), outR(kFrames);
    float* inPtrs[2] = {inL.data(), inR.data()};
    float* outPtrs[2] = {outL.data(), outR.data()};
    clap_audio_buffer_t inBuf{inPtrs, nullptr, 2, 0, 0};
    clap_audio_buffer_t outBuf{outPtrs, nullptr, 2, 0, 0};
    clap_output_events_t outEvents{nullptr, &DummyOutPush};
    HostEventVec evVec;

    double sumSq = 0.0;
    for (int blk = 0; blk < 500; blk++)
    {
        for (int i = 0; i < kFrames; i++)
        {
            float s = 0.25f * std::sinf(2.0f * 3.14159265f * 220.0f * static_cast<float>(blk * kFrames + i) / 48000.0f);
            inL[i] = s;
            inR[i] = s;
        }
        evVec.clear();
        if (blk == 50) evVec.pushParamValue(0, 3.0, 64);     // InputGainDb plain +3.0 dB
        if (blk == 100) evVec.pushParamValue(26, 7.2, 128);  // Slot 02 Knob 0
        if (blk == 150) evVec.pushMidi(0xB0, 11, 90, 32);    // CC#11 Expression

        double beats = (static_cast<double>(blk) * kFrames / 48000.0) * (136.0 / 60.0);
        clap_event_transport_t tr{};
        tr.flags = CLAP_TRANSPORT_HAS_TEMPO | CLAP_TRANSPORT_IS_PLAYING | CLAP_TRANSPORT_HAS_BEATS_TIMELINE;
        tr.tempo = 136.0;
        tr.song_pos_beats = static_cast<clap_beattime>(std::llround(beats * static_cast<double>(CLAP_BEATTIME_FACTOR)));

        clap_input_events_t inEv = evVec.asClapInput();
        clap_process_t procData{
            static_cast<int64_t>(blk) * kFrames,
            static_cast<uint32_t>(kFrames),
            &tr,
            &inBuf,
            &outBuf,
            1,
            1,
            &inEv,
            &outEvents,
        };
        if (plugA->process(plugA, &procData) == CLAP_PROCESS_ERROR)
        {
            std::fprintf(stderr, "FAIL: CLAP process() error at block %d\n", blk);
            return 14;
        }
        for (int i = 0; i < kFrames; i++)
        {
            if (!std::isfinite(outL[i]) || !std::isfinite(outR[i]))
            {
                std::fprintf(stderr, "FAIL: Non-finite CLAP audio sample at block %d sample %d\n", blk, i);
                return 15;
            }
            if (blk >= 400)
                sumSq += static_cast<double>(outL[i]) * outL[i] + static_cast<double>(outR[i]) * outR[i];
        }
    }

    double rms = std::sqrt(sumSq / (100.0 * kFrames * 2.0));
    if (!(rms > 0.0005 && rms < 10.0))
    {
        std::fprintf(stderr, "FAIL: CLAP audio RMS out of range: %f\n", rms);
        return 16;
    }

    double tempoVal = 0.0;
    params->get_value(plugA, 6, &tempoVal);
    if (std::fabs(tempoVal - 136.0) > 1.0)
    {
        std::fprintf(stderr, "FAIL: Host BPM sync failed (expected ~136, got %.2f)\n", tempoVal);
        return 17;
    }

    double savedGainPlain = 0.0;
    double savedSlot2Plain = 0.0;
    params->get_value(plugA, 0, &savedGainPlain);
    params->get_value(plugA, 26, &savedSlot2Plain);

    MemoryStream stateMem;
    clap_ostream_t ostream{&stateMem, &MemoryStream::WriteFn};
    if (!stateExt->save(plugA, &ostream) || stateMem.data.size() < 16)
    {
        std::fprintf(stderr, "FAIL: CLAP state->save failed (size=%zu)\n", stateMem.data.size());
        return 18;
    }

    const clap_plugin_t* plugB = factory->create_plugin(factory, &host, desc->id);
    if (!plugB || !plugB->init(plugB))
    {
        std::fprintf(stderr, "FAIL: Instance B init failed\n");
        return 19;
    }
    const auto* stateExtB = static_cast<const clap_plugin_state_t*>(plugB->get_extension(plugB, CLAP_EXT_STATE));
    const auto* paramsB = static_cast<const clap_plugin_params_t*>(plugB->get_extension(plugB, CLAP_EXT_PARAMS));
    clap_istream_t istream{&stateMem, &MemoryStream::ReadFn};
    if (!stateExtB->load(plugB, &istream))
    {
        std::fprintf(stderr, "FAIL: Instance B state->load failed\n");
        return 20;
    }
    double restoredGain = 0.0;
    double restoredSlot2 = 0.0;
    paramsB->get_value(plugB, 0, &restoredGain);
    paramsB->get_value(plugB, 26, &restoredSlot2);
    if (std::fabs(restoredGain - savedGainPlain) > 0.05 || std::fabs(restoredSlot2 - savedSlot2Plain) > 0.05)
    {
        std::fprintf(stderr, "FAIL: Restored state param mismatch (g: %.3f vs %.3f, s2: %.3f vs %.3f)\n",
                     restoredGain, savedGainPlain, restoredSlot2, savedSlot2Plain);
        return 21;
    }
    plugB->destroy(plugB);

    if (!noGui)
    {
        if (!guiExt->is_api_supported(plugA, CLAP_WINDOW_API_WIN32, false) ||
            !guiExt->create(plugA, CLAP_WINDOW_API_WIN32, false))
        {
            std::fprintf(stderr, "FAIL: CLAP GUI create failed\n");
            return 22;
        }
        uint32_t gw = 0, gh = 0;
        if (!guiExt->get_size(plugA, &gw, &gh) || gw != 1280 || gh != 780)
        {
            std::fprintf(stderr, "FAIL: Unexpected CLAP initial GUI size %ux%u\n", gw, gh);
            return 23;
        }

        WNDCLASSEXW wc{};
        wc.cbSize = sizeof(wc);
        wc.lpfnWndProc = &VerifyHostWndProc;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"ClapHostVerifyWindow";
        RegisterClassExW(&wc);

        HWND hostHwnd = CreateWindowExW(
            0,
            wc.lpszClassName,
            L"CLAP Host Verify",
            WS_OVERLAPPEDWINDOW,
            CW_USEDEFAULT, CW_USEDEFAULT, 1280, 780,
            nullptr, nullptr, wc.hInstance, nullptr);

        clap_window_t win{};
        win.api = CLAP_WINDOW_API_WIN32;
        win.win32 = hostHwnd;
        if (!guiExt->set_parent(plugA, &win) || !guiExt->show(plugA))
        {
            std::fprintf(stderr, "FAIL: CLAP gui->set_parent / show failed\n");
            return 24;
        }

        ShowWindow(hostHwnd, SW_SHOWNA);
        PumpMessagesForMs(1500);

        if (!guiExt->set_size(plugA, 1120, 700))
        {
            std::fprintf(stderr, "FAIL: CLAP gui->set_size failed\n");
            return 25;
        }
        PumpMessagesForMs(400);

        guiExt->hide(plugA);
        guiExt->destroy(plugA);
        DestroyWindow(hostHwnd);
    }

    plugA->stop_processing(plugA);
    plugA->deactivate(plugA);
    plugA->destroy(plugA);
    entry->deinit();
    FreeLibrary(mod);
    CoUninitialize();

    std::printf("[PASS] ClapHostVerify: 160 params, MIDI+Transport(136.0 BPM), PDC=%u, Tail=%u, 500 RT blocks (RMS=%.4f), State round-trip (%zu B), %s\n",
                initLatency, initTail, rms, stateMem.data.size(),
                noGui ? "Headless (--no-gui) OK" : "Real HWND+WebView2 GUI 1280x780->1120x700 OK");
    return 0;
}
