#pragma once
#ifndef NOMINMAX
#define NOMINMAX
#endif
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <string>

namespace nrs::core {

struct NrsParamInfoNative
{
    uint32_t id;
    int32_t stepCount;
    float minValue;
    float maxValue;
    float defaultNormalized;
    uint8_t flags;
    char nameUtf8[128];
    char shortNameUtf8[64];
    char unitsUtf8[32];
};

using WebSendCallback = void(__cdecl*)(void* userData, const char* utf8Json, int32_t len);
using HostParamCallback = void(__cdecl*)(void* userData, uint32_t paramId, int32_t phase, double norm01);

struct NativeCoreApi
{
    HMODULE module{nullptr};

    int32_t(__cdecl* abi_version)(){nullptr};
    void* (__cdecl* create)(WebSendCallback webSendCb, HostParamCallback hostParamCb, void* userData){nullptr};
    void(__cdecl* destroy)(void* handle){nullptr};
    void(__cdecl* set_parent_hwnd)(void* handle, void* hwnd){nullptr};
    void(__cdecl* activate)(void* handle, int32_t sampleRate, int32_t maxBlockSize){nullptr};
    void(__cdecl* deactivate)(void* handle){nullptr};
    void(__cdecl* process)(void* handle, float* inL, float* inR, float* outL, float* outR, int32_t numSamples, double hostBpm, uint8_t hasHostBpm){nullptr};
    int32_t(__cdecl* get_param_count)(){nullptr};
    int32_t(__cdecl* get_param_info)(void* handle, uint32_t paramId, NrsParamInfoNative* outInfo){nullptr};
    double(__cdecl* get_param_normalized)(void* handle, uint32_t paramId){nullptr};
    void(__cdecl* set_param_normalized)(void* handle, uint32_t paramId, double norm01, int32_t sampleOffset){nullptr};
    double(__cdecl* param_norm_to_plain)(void* handle, uint32_t paramId, double norm01){nullptr};
    double(__cdecl* param_plain_to_norm)(void* handle, uint32_t paramId, double plain){nullptr};
    int32_t(__cdecl* format_param_value)(void* handle, uint32_t paramId, double norm01, char* utf8Out, int32_t maxBytes){nullptr};
    uint8_t* (__cdecl* save_state)(void* handle, int32_t* outLen){nullptr};
    void(__cdecl* free_buffer)(void* ptr){nullptr};
    int32_t(__cdecl* load_state)(void* handle, const uint8_t* data, int32_t len){nullptr};
    void(__cdecl* web_post_message)(void* handle, const char* utf8Json, int32_t len){nullptr};
    void(__cdecl* poll_ui)(void* handle){nullptr};

    bool isLoaded() const
    {
        return module != nullptr && abi_version != nullptr && create != nullptr && destroy != nullptr &&
               activate != nullptr && deactivate != nullptr && process != nullptr &&
               get_param_count != nullptr && get_param_info != nullptr &&
               get_param_normalized != nullptr && set_param_normalized != nullptr &&
               param_norm_to_plain != nullptr && param_plain_to_norm != nullptr &&
               format_param_value != nullptr && save_state != nullptr && free_buffer != nullptr &&
               load_state != nullptr && web_post_message != nullptr && poll_ui != nullptr;
    }
};

inline std::wstring GetCurrentModuleDirW()
{
    HMODULE selfMod = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&GetCurrentModuleDirW),
            &selfMod) ||
        selfMod == nullptr)
    {
        return L"";
    }

    wchar_t path[MAX_PATH]{};
    DWORD len = GetModuleFileNameW(selfMod, path, MAX_PATH);
    if (len == 0 || len >= MAX_PATH)
        return L"";

    std::wstring ws(path, len);
    size_t slash = ws.find_last_of(L"\\/");
    return slash == std::wstring::npos ? L"" : ws.substr(0, slash);
}

inline NativeCoreApi& GetNativeCoreApi()
{
    static NativeCoreApi api = []() -> NativeCoreApi
    {
        NativeCoreApi out{};
        std::wstring dir = GetCurrentModuleDirW();
        if (dir.empty())
            return out;

        std::wstring dllPath = dir + L"\\NeuralRig.NativeCore.dll";
        out.module = LoadLibraryExW(dllPath.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (!out.module)
            out.module = LoadLibraryW(dllPath.c_str());
        if (!out.module)
            return out;

        auto load = [&](auto& fn, const char* name)
        {
            fn = reinterpret_cast<std::decay_t<decltype(fn)>>(GetProcAddress(out.module, name));
        };

        load(out.abi_version, "nrs_abi_version");
        load(out.create, "nrs_create");
        load(out.destroy, "nrs_destroy");
        load(out.set_parent_hwnd, "nrs_set_parent_hwnd");
        load(out.activate, "nrs_activate");
        load(out.deactivate, "nrs_deactivate");
        load(out.process, "nrs_process");
        load(out.get_param_count, "nrs_get_param_count");
        load(out.get_param_info, "nrs_get_param_info");
        load(out.get_param_normalized, "nrs_get_param_normalized");
        load(out.set_param_normalized, "nrs_set_param_normalized");
        load(out.param_norm_to_plain, "nrs_param_norm_to_plain");
        load(out.param_plain_to_norm, "nrs_param_plain_to_norm");
        load(out.format_param_value, "nrs_format_param_value");
        load(out.save_state, "nrs_save_state");
        load(out.free_buffer, "nrs_free_buffer");
        load(out.load_state, "nrs_load_state");
        load(out.web_post_message, "nrs_web_post_message");
        load(out.poll_ui, "nrs_poll_ui");

        if (!out.isLoaded() || out.abi_version() != 1)
        {
            FreeLibrary(out.module);
            out.module = nullptr;
        }
        return out;
    }();
    return api;
}

} // namespace nrs::core
