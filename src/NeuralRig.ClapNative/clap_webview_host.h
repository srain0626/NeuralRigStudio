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
#include <cstdint>
#include <deque>
#include <memory>
#include <mutex>
#include <string>

namespace nrs::clap {

class ClapWebViewHost final
{
public:
    explicit ClapWebViewHost(void* coreHandle);
    ~ClapWebViewHost();

    static LRESULT CALLBACK HostWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);

    bool create();
    void destroy();
    bool setScale(double scale);
    bool getSize(uint32_t* width, uint32_t* height) const;
    bool adjustSize(uint32_t* width, uint32_t* height) const;
    bool setSize(uint32_t width, uint32_t height);
    bool setParent(HWND parent);
    bool show();
    bool hide();

    void enqueueWebMessage(const char* utf8Json, int32_t len);

private:
    void handleTimer();
    void flushOutbox();
    void startWebViewInit();
    void cleanupWebView();

    void* coreHandle_{nullptr};
    HWND hwnd_{nullptr};
    HWND parentWnd_{nullptr};
    uint32_t width_{1280};
    uint32_t height_{780};
    bool created_{false};
    bool visible_{false};
    bool comInitialized_{false};

    ICoreWebView2Environment* env_{nullptr};
    ICoreWebView2Controller* controller_{nullptr};
    ICoreWebView2* webview_{nullptr};
    std::shared_ptr<std::atomic<bool>> alive_;

    std::mutex outboxMtx_;
    std::deque<std::string> outbox_;
};

} // namespace nrs::clap
