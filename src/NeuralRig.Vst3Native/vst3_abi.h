#pragma once
#include <cstdint>
#include <cstring>

#if defined(_WIN32)
#define SMTG_CALLCONV __stdcall
#else
#define SMTG_CALLCONV
#endif

namespace nrs::vst3 {

using int8 = int8_t;
using uint8 = uint8_t;
using int16 = int16_t;
using uint16 = uint16_t;
using int32 = int32_t;
using uint32 = uint32_t;
using int64 = int64_t;
using uint64 = uint64_t;
using TBool = uint8_t;
using char8 = char;
using char16 = char16_t;
using TChar = char16_t;
using String128 = char16_t[128];
using FIDString = const char*;
using TUID = char[16];
using tresult = int32_t;

using ParamID = uint32_t;
using ParamValue = double;
using SampleRate = double;
using Sample32 = float;
using Sample64 = double;
using SpeakerArrangement = uint64_t;
using MediaType = int32_t;
using BusDirection = int32_t;
using BusType = int32_t;
using IoMode = int32_t;
using UnitID = int32_t;

// Win32 COM-compatible VST3 result codes
constexpr tresult kNoInterface = static_cast<tresult>(0x80004002L);
constexpr tresult kResultOk = static_cast<tresult>(0x00000000L);
constexpr tresult kResultTrue = kResultOk;
constexpr tresult kResultFalse = static_cast<tresult>(0x00000001L);
constexpr tresult kInvalidArgument = static_cast<tresult>(0x80070057L);
constexpr tresult kNotImplemented = static_cast<tresult>(0x80004001L);
constexpr tresult kInternalError = static_cast<tresult>(0x80004005L);
constexpr tresult kNotInitialized = static_cast<tresult>(0x8000FFFFL);
constexpr tresult kOutOfMemory = static_cast<tresult>(0x8007000EL);

constexpr MediaType kAudio = 0;
constexpr MediaType kEvent = 1;
constexpr BusDirection kInput = 0;
constexpr BusDirection kOutput = 1;
constexpr BusType kMain = 0;
constexpr BusType kAux = 1;
constexpr uint32 kDefaultActive = 1u << 0;

constexpr int32 kRealtime = 0;
constexpr int32 kPrefetch = 1;
constexpr int32 kOffline = 2;
constexpr int32 kSample32 = 0;
constexpr int32 kSample64 = 1;
constexpr uint32 kInfiniteTail = 0xFFFFFFFFu;

constexpr SpeakerArrangement kMono = 1ULL << 19; // kSpeakerM
constexpr SpeakerArrangement kStereo = 3ULL;     // kSpeakerL | kSpeakerR

constexpr int32 kCanAutomate = 1 << 0;
constexpr int32 kIsBypass = 1 << 4;

constexpr int32 kUnicodeFactoryFlag = 1 << 4; // PFactoryInfo::kUnicode = 0x10
constexpr uint32 kSimpleModeSupported = 1u << 1;

constexpr uint32 kNeedProjectTimeMusic = 1u << 2;
constexpr uint32 kNeedTempo = 1u << 6;
constexpr uint32 kNeedTransportState = 1u << 10;
constexpr uint32 kPlaying = 1u << 1;
constexpr uint32 kProjectTimeMusicValid = 1u << 9;
constexpr uint32 kTempoValid = 1u << 10;

constexpr int16 kCtrlModWheel = 1;
constexpr int16 kCtrlFoot = 4;
constexpr int16 kCtrlVolume = 7;
constexpr int16 kCtrlExpression = 11;
constexpr int16 kCtrlProgramChange = 130;

constexpr int32 kIBSeekSet = 0;
constexpr int32 kIBSeekCur = 1;
constexpr int32 kIBSeekEnd = 2;

constexpr FIDString kPlatformTypeHWND = "HWND";
constexpr FIDString kPlugViewEditor = "editor";
constexpr FIDString kAudioModuleClass = "Audio Module Class";

struct GuidBytes
{
    char b[16];
};

consteval GuidBytes MakeVst3Uid(uint32 l1, uint32 l2, uint32 l3, uint32 l4)
{
    return GuidBytes{{
        static_cast<char>(l1 & 0xFFu),
        static_cast<char>((l1 >> 8) & 0xFFu),
        static_cast<char>((l1 >> 16) & 0xFFu),
        static_cast<char>((l1 >> 24) & 0xFFu),
        static_cast<char>((l2 >> 16) & 0xFFu),
        static_cast<char>((l2 >> 24) & 0xFFu),
        static_cast<char>(l2 & 0xFFu),
        static_cast<char>((l2 >> 8) & 0xFFu),
        static_cast<char>((l3 >> 24) & 0xFFu),
        static_cast<char>((l3 >> 16) & 0xFFu),
        static_cast<char>((l3 >> 8) & 0xFFu),
        static_cast<char>(l3 & 0xFFu),
        static_cast<char>((l4 >> 24) & 0xFFu),
        static_cast<char>((l4 >> 16) & 0xFFu),
        static_cast<char>((l4 >> 8) & 0xFFu),
        static_cast<char>(l4 & 0xFFu),
    }};
}

inline bool UidEqual(const void* a, const GuidBytes& b)
{
    return std::memcmp(a, b.b, 16) == 0;
}

inline void CopyUid(TUID dst, const GuidBytes& src)
{
    std::memcpy(dst, src.b, 16);
}

constexpr GuidBytes kFUnknownIid = MakeVst3Uid(0x00000000u, 0x00000000u, 0xC0000000u, 0x00000046u);
constexpr GuidBytes kIPluginBaseIid = MakeVst3Uid(0x22888DDBu, 0x156E45AEu, 0x8358B348u, 0x08190625u);
constexpr GuidBytes kIPluginFactoryIid = MakeVst3Uid(0x7A4D811Cu, 0x52114A1Fu, 0xAED9D2EEu, 0x0B43BF9Fu);
constexpr GuidBytes kIPluginFactory2Iid = MakeVst3Uid(0x0007B650u, 0xF24B4C0Bu, 0xA464EDB9u, 0xF00B2ABBu);
constexpr GuidBytes kIPluginFactory3Iid = MakeVst3Uid(0x4555A2ABu, 0xC1234E57u, 0x9B122910u, 0x36878931u);
constexpr GuidBytes kIBStreamIid = MakeVst3Uid(0xC3BF6EA2u, 0x30994752u, 0x9B6BF990u, 0x1EE33E9Bu);
constexpr GuidBytes kIPlugViewIid = MakeVst3Uid(0x5BC32507u, 0xD06049EAu, 0xA6151B52u, 0x2B755B29u);
constexpr GuidBytes kIPlugFrameIid = MakeVst3Uid(0x367FAF01u, 0xAFA94693u, 0x8D4DA2A0u, 0xED0882A3u);
constexpr GuidBytes kIComponentIid = MakeVst3Uid(0xE831FF31u, 0xF2D54301u, 0x928EBBEEu, 0x25697802u);
constexpr GuidBytes kIAudioProcessorIid = MakeVst3Uid(0x42043F99u, 0xB7DA453Cu, 0xA569E79Du, 0x9AAEC33Du);
constexpr GuidBytes kIProcessContextRequirementsIid = MakeVst3Uid(0x2A654303u, 0xEF764E3Du, 0x95B5FE83u, 0x730EF6D0u);
constexpr GuidBytes kIComponentHandlerIid = MakeVst3Uid(0x93A0BEA3u, 0x0BD045DBu, 0x8E890B0Cu, 0xC1E46AC6u);
constexpr GuidBytes kIEditControllerIid = MakeVst3Uid(0xDCD7BBE3u, 0x7742448Du, 0xA874AACCu, 0x979C759Eu);
constexpr GuidBytes kIParamValueQueueIid = MakeVst3Uid(0x01263A18u, 0xED074F6Fu, 0x98C9D356u, 0x4686F9BAu);
constexpr GuidBytes kIParameterChangesIid = MakeVst3Uid(0xA4779663u, 0x0BB64A56u, 0xB44384A8u, 0x466FEB9Du);
constexpr GuidBytes kIEventListIid = MakeVst3Uid(0x3A2C4214u, 0x346349FEu, 0xB2C4F397u, 0xB9695A44u);
constexpr GuidBytes kIMidiMappingIid = MakeVst3Uid(0xDF0FF9F7u, 0x49B74669u, 0xB63AB732u, 0x7ADBF5E5u);

// NeuralRig Studio Processor / SingleComponentEffect CID: A73E91C4-52D8-4B6E-91F0-C38E4B72D101
constexpr GuidBytes kNeuralRigProcessorCid = MakeVst3Uid(0xA73E91C4u, 0x52D84B6Eu, 0x91F0C38Eu, 0x4B72D101u);

#pragma pack(push, 16)

struct PFactoryInfo
{
    char8 vendor[64];
    char8 url[256];
    char8 email[128];
    int32 flags;
};

struct PClassInfo
{
    TUID cid;
    int32 cardinality;
    char8 category[32];
    char8 name[64];
};

struct PClassInfo2
{
    TUID cid;
    int32 cardinality;
    char8 category[32];
    char8 name[64];
    uint32 classFlags;
    char8 subCategories[128];
    char8 vendor[64];
    char8 version[64];
    char8 sdkVersion[64];
};

struct PClassInfoW
{
    TUID cid;
    int32 cardinality;
    char8 category[32];
    char16 name[64];
    uint32 classFlags;
    char8 subCategories[128];
    char16 vendor[64];
    char16 version[64];
    char16 sdkVersion[64];
};

struct ViewRect
{
    int32 left{0};
    int32 top{0};
    int32 right{0};
    int32 bottom{0};

    int32 getWidth() const { return right - left; }
    int32 getHeight() const { return bottom - top; }
};

struct BusInfo
{
    MediaType mediaType;
    BusDirection direction;
    int32 channelCount;
    String128 name;
    BusType busType;
    uint32 flags;
};

struct RoutingInfo
{
    MediaType mediaType;
    int32 busIndex;
    int32 channel;
};

struct ParameterInfo
{
    ParamID id;
    String128 title;
    String128 shortTitle;
    String128 units;
    int32 stepCount;
    ParamValue defaultNormalizedValue;
    UnitID unitId;
    int32 flags;
};

struct ProcessSetup
{
    int32 processMode;
    int32 symbolicSampleSize;
    int32 maxSamplesPerBlock;
    SampleRate sampleRate;
};

struct AudioBusBuffers
{
    int32 numChannels;
    uint64 silenceFlags;
    union
    {
        Sample32** channelBuffers32;
        Sample64** channelBuffers64;
    };
};

struct FrameRate
{
    uint32 framesPerSecond;
    uint32 flags;
};

struct Chord
{
    uint8 keyNote;
    uint8 rootNote;
    int16 chordMask;
};

struct ProcessContext
{
    uint32 state;
    int64 projectTimeSamples;
    double systemTime;
    int64 continousTimeSamples;
    double projectTimeMusic;
    double barPositionMusic;
    double cycleStartMusic;
    double cycleEndMusic;
    double tempo;
    int32 timeSigNumerator;
    int32 timeSigDenominator;
    Chord chord;
    int32 smpteOffsetSubframes;
    FrameRate frameRate;
    int32 samplesToNextClock;
};

struct NoteOnEvent
{
    int16 channel;
    int16 pitch;
    float tuning;
    float velocity;
    int32 length;
    int32 noteId;
};

struct LegacyMIDICCOutEvent
{
    uint8 controlNumber;
    int8 channel;
    int8 value;
    int8 value2;
};

struct Event
{
    int32 busIndex;
    int32 sampleOffset;
    double ppqPosition;
    uint16 flags;

    enum EventFlags : uint16
    {
        kIsLive = 1 << 0,
    };

    enum EventTypes : uint16
    {
        kNoteOnEvent = 0,
        kNoteOffEvent = 1,
        kLegacyMIDICCOutEvent = 65535,
    };

    uint16 type;
    union
    {
        NoteOnEvent noteOn;
        LegacyMIDICCOutEvent midiCCOut;
        uint8 raw[64];
    };
};

class IParameterChanges;
class IEventList;

struct ProcessData
{
    int32 processMode;
    int32 symbolicSampleSize;
    int32 numSamples;
    int32 numInputs;
    int32 numOutputs;
    AudioBusBuffers* inputs;
    AudioBusBuffers* outputs;
    IParameterChanges* inputParameterChanges;
    IParameterChanges* outputParameterChanges;
    IEventList* inputEvents;
    IEventList* outputEvents;
    ProcessContext* processContext;
};

#pragma pack(pop)

class FUnknown
{
public:
    virtual tresult SMTG_CALLCONV queryInterface(const TUID _iid, void** obj) = 0;
    virtual uint32 SMTG_CALLCONV addRef() = 0;
    virtual uint32 SMTG_CALLCONV release() = 0;
};

class IPluginBase : public FUnknown
{
public:
    virtual tresult SMTG_CALLCONV initialize(FUnknown* context) = 0;
    virtual tresult SMTG_CALLCONV terminate() = 0;
};

class IPluginFactory : public FUnknown
{
public:
    virtual tresult SMTG_CALLCONV getFactoryInfo(PFactoryInfo* info) = 0;
    virtual int32 SMTG_CALLCONV countClasses() = 0;
    virtual tresult SMTG_CALLCONV getClassInfo(int32 index, PClassInfo* info) = 0;
    virtual tresult SMTG_CALLCONV createInstance(FIDString cid, FIDString _iid, void** obj) = 0;
};

class IPluginFactory2 : public IPluginFactory
{
public:
    virtual tresult SMTG_CALLCONV getClassInfo2(int32 index, PClassInfo2* info) = 0;
};

class IPluginFactory3 : public IPluginFactory2
{
public:
    virtual tresult SMTG_CALLCONV getClassInfoUnicode(int32 index, PClassInfoW* info) = 0;
    virtual tresult SMTG_CALLCONV setHostContext(FUnknown* context) = 0;
};

class IBStream : public FUnknown
{
public:
    virtual tresult SMTG_CALLCONV read(void* buffer, int32 numBytes, int32* numBytesRead = nullptr) = 0;
    virtual tresult SMTG_CALLCONV write(void* buffer, int32 numBytes, int32* numBytesWritten = nullptr) = 0;
    virtual tresult SMTG_CALLCONV seek(int64 pos, int32 mode, int64* result = nullptr) = 0;
    virtual tresult SMTG_CALLCONV tell(int64* pos) = 0;
};

class IPlugFrame;

class IPlugView : public FUnknown
{
public:
    virtual tresult SMTG_CALLCONV isPlatformTypeSupported(FIDString type) = 0;
    virtual tresult SMTG_CALLCONV attached(void* parent, FIDString type) = 0;
    virtual tresult SMTG_CALLCONV removed() = 0;
    virtual tresult SMTG_CALLCONV onWheel(float distance) = 0;
    virtual tresult SMTG_CALLCONV onKeyDown(char16 key, int16 keyCode, int16 modifiers) = 0;
    virtual tresult SMTG_CALLCONV onKeyUp(char16 key, int16 keyCode, int16 modifiers) = 0;
    virtual tresult SMTG_CALLCONV getSize(ViewRect* size) = 0;
    virtual tresult SMTG_CALLCONV onSize(ViewRect* newSize) = 0;
    virtual tresult SMTG_CALLCONV onFocus(TBool state) = 0;
    virtual tresult SMTG_CALLCONV setFrame(IPlugFrame* frame) = 0;
    virtual tresult SMTG_CALLCONV canResize() = 0;
    virtual tresult SMTG_CALLCONV checkSizeConstraint(ViewRect* rect) = 0;
};

class IPlugFrame : public FUnknown
{
public:
    virtual tresult SMTG_CALLCONV resizeView(IPlugView* view, ViewRect* newSize) = 0;
};

class IComponent : public IPluginBase
{
public:
    virtual tresult SMTG_CALLCONV getControllerClassId(TUID classId) = 0;
    virtual tresult SMTG_CALLCONV setIoMode(IoMode mode) = 0;
    virtual int32 SMTG_CALLCONV getBusCount(MediaType type, BusDirection dir) = 0;
    virtual tresult SMTG_CALLCONV getBusInfo(MediaType type, BusDirection dir, int32 index, BusInfo& bus) = 0;
    virtual tresult SMTG_CALLCONV getRoutingInfo(RoutingInfo& inInfo, RoutingInfo& outInfo) = 0;
    virtual tresult SMTG_CALLCONV activateBus(MediaType type, BusDirection dir, int32 index, TBool state) = 0;
    virtual tresult SMTG_CALLCONV setActive(TBool state) = 0;
    virtual tresult SMTG_CALLCONV setState(IBStream* state) = 0;
    virtual tresult SMTG_CALLCONV getState(IBStream* state) = 0;
};

class IAudioProcessor : public FUnknown
{
public:
    virtual tresult SMTG_CALLCONV setBusArrangements(SpeakerArrangement* inputs, int32 numIns, SpeakerArrangement* outputs, int32 numOuts) = 0;
    virtual tresult SMTG_CALLCONV getBusArrangement(BusDirection dir, int32 index, SpeakerArrangement& arr) = 0;
    virtual tresult SMTG_CALLCONV canProcessSampleSize(int32 symbolicSampleSize) = 0;
    virtual uint32 SMTG_CALLCONV getLatencySamples() = 0;
    virtual tresult SMTG_CALLCONV setupProcessing(ProcessSetup& setup) = 0;
    virtual tresult SMTG_CALLCONV setProcessing(TBool state) = 0;
    virtual tresult SMTG_CALLCONV process(ProcessData& data) = 0;
    virtual uint32 SMTG_CALLCONV getTailSamples() = 0;
};

class IProcessContextRequirements : public FUnknown
{
public:
    virtual uint32 SMTG_CALLCONV getProcessContextRequirements() = 0;
};

class IComponentHandler : public FUnknown
{
public:
    virtual tresult SMTG_CALLCONV beginEdit(ParamID id) = 0;
    virtual tresult SMTG_CALLCONV performEdit(ParamID id, ParamValue valueNormalized) = 0;
    virtual tresult SMTG_CALLCONV endEdit(ParamID id) = 0;
    virtual tresult SMTG_CALLCONV restartComponent(int32 flags) = 0;
};

class IEditController : public IPluginBase
{
public:
    virtual tresult SMTG_CALLCONV setComponentState(IBStream* state) = 0;
    virtual tresult SMTG_CALLCONV setState(IBStream* state) = 0;
    virtual tresult SMTG_CALLCONV getState(IBStream* state) = 0;
    virtual int32 SMTG_CALLCONV getParameterCount() = 0;
    virtual tresult SMTG_CALLCONV getParameterInfo(int32 paramIndex, ParameterInfo& info) = 0;
    virtual tresult SMTG_CALLCONV getParamStringByValue(ParamID id, ParamValue valueNormalized, String128 string) = 0;
    virtual tresult SMTG_CALLCONV getParamValueByString(ParamID id, TChar* string, ParamValue& valueNormalized) = 0;
    virtual ParamValue SMTG_CALLCONV normalizedParamToPlain(ParamID id, ParamValue valueNormalized) = 0;
    virtual ParamValue SMTG_CALLCONV plainParamToNormalized(ParamID id, ParamValue plainValue) = 0;
    virtual ParamValue SMTG_CALLCONV getParamNormalized(ParamID id) = 0;
    virtual tresult SMTG_CALLCONV setParamNormalized(ParamID id, ParamValue value) = 0;
    virtual tresult SMTG_CALLCONV setComponentHandler(IComponentHandler* handler) = 0;
    virtual IPlugView* SMTG_CALLCONV createView(FIDString name) = 0;
};

class IParamValueQueue : public FUnknown
{
public:
    virtual ParamID SMTG_CALLCONV getParameterId() = 0;
    virtual int32 SMTG_CALLCONV getPointCount() = 0;
    virtual tresult SMTG_CALLCONV getPoint(int32 index, int32& sampleOffset, ParamValue& value) = 0;
    virtual tresult SMTG_CALLCONV addPoint(int32 sampleOffset, ParamValue value, int32& index) = 0;
};

class IParameterChanges : public FUnknown
{
public:
    virtual int32 SMTG_CALLCONV getParameterCount() = 0;
    virtual IParamValueQueue* SMTG_CALLCONV getParameterData(int32 index) = 0;
    virtual IParamValueQueue* SMTG_CALLCONV addParameterData(const ParamID& id, int32& index) = 0;
};

class IEventList : public FUnknown
{
public:
    virtual int32 SMTG_CALLCONV getEventCount() = 0;
    virtual tresult SMTG_CALLCONV getEvent(int32 index, Event& e) = 0;
    virtual tresult SMTG_CALLCONV addEvent(Event& e) = 0;
};

class IMidiMapping : public FUnknown
{
public:
    virtual tresult SMTG_CALLCONV getMidiControllerAssignment(
        int32 busIndex,
        int16 channel,
        int16 midiControllerNumber,
        ParamID& id) = 0;
};

} // namespace nrs::vst3
