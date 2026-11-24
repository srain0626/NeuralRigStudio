#pragma once
#ifndef NOMINMAX
#define NOMINMAX
#endif
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <unknwn.h>
#include <objbase.h>
#include <objidl.h>
#include <WebView2.h>
#include <atomic>
#include <deque>
#include <memory>
#include <mutex>
#include <string>
#include "vst3_abi.h"

namespace nrs::vst3 {

class NeuralRigVst3Plugin;

class Vst3WebViewEditor final : public IPlugView
{
public:
    Vst3WebViewEditor(NeuralRigVst3Plugin* owner, void* coreHandle);
    ~Vst3WebViewEditor();

    static LRESULT CALLBACK HostWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);

    // FUnknown
    tresult SMTG_CALLCONV queryInterface(const TUID _iid, void** obj) override;
    uint32 SMTG_CALLCONV addRef() override;
    uint32 SMTG_CALLCONV release() override;

    // IPlugView
    tresult SMTG_CALLCONV isPlatformTypeSupported(FIDString type) override;
    tresult SMTG_CALLCONV attached(void* parent, FIDString type) override;
    tresult SMTG_CALLCONV removed() override;
    tresult SMTG_CALLCONV onWheel(float distance) override;
    tresult SMTG_CALLCONV onKeyDown(char16 key, int16 keyCode, int16 modifiers) override;
    tresult SMTG_CALLCONV onKeyUp(char16 key, int16 keyCode, int16 modifiers) override;
    tresult SMTG_CALLCONV getSize(ViewRect* size) override;
    tresult SMTG_CALLCONV onSize(ViewRect* newSize) override;
    tresult SMTG_CALLCONV onFocus(TBool state) override;
    tresult SMTG_CALLCONV setFrame(IPlugFrame* frame) override;
    tresult SMTG_CALLCONV canResize() override;
    tresult SMTG_CALLCONV checkSizeConstraint(ViewRect* rect) override;

    void enqueueWebMessage(const char* utf8Json, int32 len);

private:
    void handleTimer();
    void flushOutbox();
    void startWebViewInit();
    void cleanupWebView();

    std::atomic<uint32> refCount_{1};
    NeuralRigVst3Plugin* owner_{nullptr};
    void* coreHandle_{nullptr};
    IPlugFrame* plugFrame_{nullptr};
    HWND hwnd_{nullptr};
    ViewRect rect_{0, 0, 1280, 780};

    ICoreWebView2Environment* env_{nullptr};
    ICoreWebView2Controller* controller_{nullptr};
    ICoreWebView2* webview_{nullptr};
    std::shared_ptr<std::atomic<bool>> alive_;

    std::mutex outboxMtx_;
    std::deque<std::string> outbox_;
    bool comInitialized_{false};
};

} // namespace nrs::vst3
