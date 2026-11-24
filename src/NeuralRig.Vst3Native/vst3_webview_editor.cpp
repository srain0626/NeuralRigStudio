#include "vst3_webview_editor.h"
#include "native_core_loader.h"
#include <objbase.h>
#include <shellapi.h>
#include <shlobj.h>
#include <algorithm>
#include <functional>
#include <vector>

namespace nrs::vst3 {

// Clear backlink in owner when editor closes — declared implemented in vst3_plugin.cpp
void DetachEditorFromPlugin(NeuralRigVst3Plugin* owner, Vst3WebViewEditor* editor);

namespace {

constexpr UINT WM_APP_FLUSH_WEBMSG = WM_APP + 1;
constexpr UINT_PTR TIMER_ID_POLL_UI = 1;
constexpr int kMinWidth = 960;
constexpr int kMinHeight = 600;
const wchar_t* kHostWindowClassName = L"NeuralRigVst3WebViewHostWnd";

template <typename TInterface, typename... Args>
class W2Callback final : public TInterface
{
public:
    explicit W2Callback(std::function<HRESULT(Args...)> fn) : fn_(std::move(fn)) {}

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppvObject) override
    {
        if (!ppvObject) return E_POINTER;
        if (riid == IID_IUnknown)
        {
            *ppvObject = static_cast<IUnknown*>(this);
            AddRef();
            return S_OK;
        }
        *ppvObject = static_cast<TInterface*>(this);
        AddRef();
        return S_OK;
    }

    ULONG STDMETHODCALLTYPE AddRef() override
    {
        return ++refCount_;
    }

    ULONG STDMETHODCALLTYPE Release() override
    {
        ULONG c = --refCount_;
        if (c == 0) delete this;
        return c;
    }

    HRESULT STDMETHODCALLTYPE Invoke(Args... args) override
    {
        return fn_(args...);
    }

private:
    std::atomic<ULONG> refCount_{1};
    std::function<HRESULT(Args...)> fn_;
};

std::wstring Utf8ToWide(const char* s, int len)
{
    if (!s || len <= 0) return L"";
    int needed = MultiByteToWideChar(CP_UTF8, 0, s, len, nullptr, 0);
    if (needed <= 0) return L"";
    std::wstring out(static_cast<size_t>(needed), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s, len, out.data(), needed);
    return out;
}

std::string WideToUtf8(const wchar_t* s)
{
    if (!s || !*s) return "";
    int needed = WideCharToMultiByte(CP_UTF8, 0, s, -1, nullptr, 0, nullptr, nullptr);
    if (needed <= 1) return "";
    std::string out(static_cast<size_t>(needed - 1), '\0');
    WideCharToMultiByte(CP_UTF8, 0, s, -1, out.data(), needed, nullptr, nullptr);
    return out;
}

std::wstring GetVst3ResourceWwwRoot()
{
    std::wstring binDir = nrs::core::GetCurrentModuleDirW();
    if (binDir.empty()) return L"";

    std::wstring candidates[] = {
        binDir + L"\\..\\..\\Resources\\wwwroot",
        binDir + L"\\..\\Resources\\wwwroot",
        binDir + L"\\wwwroot",
    };

    for (const auto& c : candidates)
    {
        wchar_t full[MAX_PATH]{};
        if (GetFullPathNameW(c.c_str(), MAX_PATH, full, nullptr) > 0)
        {
            DWORD attr = GetFileAttributesW(full);
            if (attr != INVALID_FILE_ATTRIBUTES && (attr & FILE_ATTRIBUTE_DIRECTORY) != 0)
            {
                std::wstring indexFile = std::wstring(full) + L"\\index.html";
                if (GetFileAttributesW(indexFile.c_str()) != INVALID_FILE_ATTRIBUTES)
                    return std::wstring(full);
            }
        }
    }
    return L"";
}

std::wstring GetWebViewUserDataFolder()
{
    PWSTR localAppData = nullptr;
    std::wstring base;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &localAppData)) && localAppData)
    {
        base = localAppData;
        CoTaskMemFree(localAppData);
    }
    else
    {
        wchar_t tmp[MAX_PATH]{};
        GetTempPathW(MAX_PATH, tmp);
        base = tmp;
    }
    std::wstring dir1 = base + L"\\NeuralRigStudio";
    std::wstring dir2 = dir1 + L"\\VST3WebView2";
    CreateDirectoryW(dir1.c_str(), nullptr);
    CreateDirectoryW(dir2.c_str(), nullptr);
    return dir2;
}

HINSTANCE GetSelfHInstance()
{
    HMODULE mod = nullptr;
    GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&GetSelfHInstance),
        &mod);
    return reinterpret_cast<HINSTANCE>(mod);
}

void EnsureWindowClassRegistered(HINSTANCE hInst)
{
    static std::once_flag flag;
    std::call_once(flag, [hInst]() {
        WNDCLASSEXW wc{};
        wc.cbSize = sizeof(wc);
        if (!GetClassInfoExW(hInst, kHostWindowClassName, &wc))
        {
            WNDCLASSEXW def{};
            def.cbSize = sizeof(def);
            def.style = CS_DBLCLKS;
            def.lpfnWndProc = &Vst3WebViewEditor::HostWndProc;
            def.hInstance = hInst;
            def.hCursor = LoadCursorW(nullptr, IDC_ARROW);
            def.hbrBackground = reinterpret_cast<HBRUSH>(GetStockObject(BLACK_BRUSH));
            def.lpszClassName = kHostWindowClassName;
            RegisterClassExW(&def);
        }
    });
}

} // namespace

Vst3WebViewEditor::Vst3WebViewEditor(NeuralRigVst3Plugin* owner, void* coreHandle)
    : owner_(owner)
    , coreHandle_(coreHandle)
    , alive_(std::make_shared<std::atomic<bool>>(false))
{
}

Vst3WebViewEditor::~Vst3WebViewEditor()
{
    removed();
    if (plugFrame_)
    {
        plugFrame_->release();
        plugFrame_ = nullptr;
    }
    DetachEditorFromPlugin(owner_, this);
}

tresult SMTG_CALLCONV Vst3WebViewEditor::queryInterface(const TUID _iid, void** obj)
{
    if (!obj) return kInvalidArgument;
    if (UidEqual(_iid, kFUnknownIid) || UidEqual(_iid, kIPlugViewIid))
    {
        *obj = static_cast<IPlugView*>(this);
        addRef();
        return kResultOk;
    }
    *obj = nullptr;
    return kNoInterface;
}

uint32 SMTG_CALLCONV Vst3WebViewEditor::addRef()
{
    return ++refCount_;
}

uint32 SMTG_CALLCONV Vst3WebViewEditor::release()
{
    uint32 r = --refCount_;
    if (r == 0)
        delete this;
    return r;
}

tresult SMTG_CALLCONV Vst3WebViewEditor::isPlatformTypeSupported(FIDString type)
{
    if (type && std::strcmp(type, kPlatformTypeHWND) == 0)
        return kResultOk;
    return kResultFalse;
}

tresult SMTG_CALLCONV Vst3WebViewEditor::attached(void* parent, FIDString type)
{
    if (!parent || !type || std::strcmp(type, kPlatformTypeHWND) != 0)
        return kInvalidArgument;

    if (hwnd_)
        removed();

    HRESULT hrCo = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    comInitialized_ = SUCCEEDED(hrCo);

    HINSTANCE hInst = GetSelfHInstance();
    EnsureWindowClassRegistered(hInst);

    int w = std::max(kMinWidth, static_cast<int>(rect_.getWidth()));
    int h = std::max(kMinHeight, static_cast<int>(rect_.getHeight()));
    rect_.left = 0;
    rect_.top = 0;
    rect_.right = w;
    rect_.bottom = h;

    hwnd_ = CreateWindowExW(
        0,
        kHostWindowClassName,
        L"NeuralRig Studio VST3",
        WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
        0,
        0,
        w,
        h,
        static_cast<HWND>(parent),
        nullptr,
        hInst,
        this);

    if (!hwnd_)
        return kInternalError;

    auto& api = nrs::core::GetNativeCoreApi();
    if (api.isLoaded() && api.set_parent_hwnd)
        api.set_parent_hwnd(coreHandle_, hwnd_);

    alive_ = std::make_shared<std::atomic<bool>>(true);
    SetTimer(hwnd_, TIMER_ID_POLL_UI, 33, nullptr);
    startWebViewInit();
    return kResultOk;
}

tresult SMTG_CALLCONV Vst3WebViewEditor::removed()
{
    if (alive_)
        alive_->store(false, std::memory_order_release);

    if (hwnd_)
    {
        KillTimer(hwnd_, TIMER_ID_POLL_UI);
        auto& api = nrs::core::GetNativeCoreApi();
        if (api.isLoaded() && api.set_parent_hwnd)
            api.set_parent_hwnd(coreHandle_, nullptr);
    }

    cleanupWebView();

    if (hwnd_)
    {
        HWND h = hwnd_;
        hwnd_ = nullptr;
        DestroyWindow(h);
    }

    if (comInitialized_)
    {
        CoUninitialize();
        comInitialized_ = false;
    }

    return kResultOk;
}

void Vst3WebViewEditor::cleanupWebView()
{
    if (webview_)
    {
        webview_->Release();
        webview_ = nullptr;
    }
    if (controller_)
    {
        controller_->Close();
        controller_->Release();
        controller_ = nullptr;
    }
    if (env_)
    {
        env_->Release();
        env_ = nullptr;
    }
}

void Vst3WebViewEditor::startWebViewInit()
{
    std::wstring udf = GetWebViewUserDataFolder();
    auto aliveToken = alive_;

    auto* envHandler = new W2Callback<
        ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler,
        HRESULT,
        ICoreWebView2Environment*>(
        [this, aliveToken](HRESULT res, ICoreWebView2Environment* env) -> HRESULT
        {
            if (FAILED(res) || !env || !aliveToken->load(std::memory_order_acquire) || !hwnd_)
                return S_OK;

            env_ = env;
            env_->AddRef();

            auto* ctrlHandler = new W2Callback<
                ICoreWebView2CreateCoreWebView2ControllerCompletedHandler,
                HRESULT,
                ICoreWebView2Controller*>(
                [this, aliveToken](HRESULT cRes, ICoreWebView2Controller* ctrl) -> HRESULT
                {
                    if (FAILED(cRes) || !ctrl || !aliveToken->load(std::memory_order_acquire) || !hwnd_)
                        return S_OK;

                    controller_ = ctrl;
                    controller_->AddRef();

                    ICoreWebView2* wv = nullptr;
                    if (FAILED(controller_->get_CoreWebView2(&wv)) || !wv)
                        return S_OK;
                    webview_ = wv;

                    RECT rc{};
                    GetClientRect(hwnd_, &rc);
                    controller_->put_Bounds(rc);
                    controller_->put_IsVisible(TRUE);

                    ICoreWebView2Settings* settings = nullptr;
                    if (SUCCEEDED(webview_->get_Settings(&settings)) && settings)
                    {
                        settings->put_AreDefaultContextMenusEnabled(FALSE);
                        settings->put_AreDevToolsEnabled(FALSE);
                        settings->put_IsStatusBarEnabled(FALSE);
                        settings->put_IsBuiltInErrorPageEnabled(TRUE);

                        ICoreWebView2Settings3* settings3 = nullptr;
                        if (SUCCEEDED(settings->QueryInterface(__uuidof(ICoreWebView2Settings3), reinterpret_cast<void**>(&settings3))) && settings3)
                        {
                            settings3->put_AreBrowserAcceleratorKeysEnabled(FALSE);
                            settings3->Release();
                        }
                        settings->Release();
                    }

                    ICoreWebView2_3* wv3 = nullptr;
                    std::wstring wwwroot = GetVst3ResourceWwwRoot();
                    if (!wwwroot.empty() &&
                        SUCCEEDED(webview_->QueryInterface(__uuidof(ICoreWebView2_3), reinterpret_cast<void**>(&wv3))) && wv3)
                    {
                        wv3->SetVirtualHostNameToFolderMapping(
                            L"app.local",
                            wwwroot.c_str(),
                            COREWEBVIEW2_HOST_RESOURCE_ACCESS_KIND_ALLOW);
                        wv3->Release();
                    }

                    EventRegistrationToken msgToken{};
                    auto* msgHandler = new W2Callback<
                        ICoreWebView2WebMessageReceivedEventHandler,
                        ICoreWebView2*,
                        ICoreWebView2WebMessageReceivedEventArgs*>(
                        [this, aliveToken](ICoreWebView2*, ICoreWebView2WebMessageReceivedEventArgs* args) -> HRESULT
                        {
                            if (!aliveToken->load(std::memory_order_acquire) || !args)
                                return S_OK;
                            LPWSTR rawMsg = nullptr;
                            if (SUCCEEDED(args->get_WebMessageAsJson(&rawMsg)) && rawMsg)
                            {
                                std::string utf8 = WideToUtf8(rawMsg);
                                CoTaskMemFree(rawMsg);
                                auto& api = nrs::core::GetNativeCoreApi();
                                if (api.isLoaded() && !utf8.empty())
                                    api.web_post_message(coreHandle_, utf8.data(), static_cast<int32_t>(utf8.size()));
                            }
                            return S_OK;
                        });
                    webview_->add_WebMessageReceived(msgHandler, &msgToken);
                    msgHandler->Release();

                    EventRegistrationToken navToken{};
                    auto* newWinHandler = new W2Callback<
                        ICoreWebView2NewWindowRequestedEventHandler,
                        ICoreWebView2*,
                        ICoreWebView2NewWindowRequestedEventArgs*>(
                        [](ICoreWebView2*, ICoreWebView2NewWindowRequestedEventArgs* args) -> HRESULT
                        {
                            if (!args) return S_OK;
                            args->put_Handled(TRUE);
                            LPWSTR uri = nullptr;
                            if (SUCCEEDED(args->get_Uri(&uri)) && uri)
                            {
                                if (wcsncmp(uri, L"https://", 8) == 0 || wcsncmp(uri, L"http://", 7) == 0)
                                    ShellExecuteW(nullptr, L"open", uri, nullptr, nullptr, SW_SHOWNORMAL);
                                CoTaskMemFree(uri);
                            }
                            return S_OK;
                        });
                    webview_->add_NewWindowRequested(newWinHandler, &navToken);
                    newWinHandler->Release();

                    if (!wwwroot.empty())
                        webview_->Navigate(L"https://app.local/index.html?host=vst3");
                    else
                        webview_->NavigateToString(L"<html><body style='background:#151514;color:#eee;font-family:sans-serif;padding:24px'><h2>NeuralRig Studio VST3</h2><p>UI resources (Resources/wwwroot) were not found in the VST3 bundle.</p></body></html>");

                    flushOutbox();
                    return S_OK;
                });

            env_->CreateCoreWebView2Controller(hwnd_, ctrlHandler);
            ctrlHandler->Release();
            return S_OK;
        });

    CreateCoreWebView2EnvironmentWithOptions(nullptr, udf.c_str(), nullptr, envHandler);
    envHandler->Release();
}

void Vst3WebViewEditor::enqueueWebMessage(const char* utf8Json, int32 len)
{
    if (!utf8Json || len <= 0) return;
    if (!alive_ || !alive_->load(std::memory_order_acquire) || !hwnd_)
        return;

    {
        std::lock_guard<std::mutex> lk(outboxMtx_);
        if (outbox_.size() > 256)
            outbox_.pop_front();
        outbox_.emplace_back(utf8Json, static_cast<size_t>(len));
    }
    PostMessageW(hwnd_, WM_APP_FLUSH_WEBMSG, 0, 0);
}

void Vst3WebViewEditor::flushOutbox()
{
    if (!webview_ || !alive_ || !alive_->load(std::memory_order_acquire))
        return;

    std::deque<std::string> local;
    {
        std::lock_guard<std::mutex> lk(outboxMtx_);
        local.swap(outbox_);
    }

    for (const auto& json : local)
    {
        std::wstring wide = Utf8ToWide(json.data(), static_cast<int>(json.size()));
        if (!wide.empty())
            webview_->PostWebMessageAsJson(wide.c_str());
    }
}

void Vst3WebViewEditor::handleTimer()
{
    if (!alive_ || !alive_->load(std::memory_order_acquire))
        return;
    auto& api = nrs::core::GetNativeCoreApi();
    if (api.isLoaded() && api.poll_ui)
        api.poll_ui(coreHandle_);
}

LRESULT CALLBACK Vst3WebViewEditor::HostWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    auto* self = reinterpret_cast<Vst3WebViewEditor*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    switch (msg)
    {
        case WM_NCCREATE:
        {
            auto* cs = reinterpret_cast<CREATESTRUCTW*>(lParam);
            self = reinterpret_cast<Vst3WebViewEditor*>(cs->lpCreateParams);
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
        case WM_SIZE:
        {
            if (self && self->controller_)
            {
                RECT rc{};
                GetClientRect(hwnd, &rc);
                self->controller_->put_Bounds(rc);
            }
            return 0;
        }
        case WM_TIMER:
        {
            if (self && wParam == TIMER_ID_POLL_UI)
                self->handleTimer();
            return 0;
        }
        case WM_APP_FLUSH_WEBMSG:
        {
            if (self)
                self->flushOutbox();
            return 0;
        }
        default:
            return DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}

tresult SMTG_CALLCONV Vst3WebViewEditor::onWheel(float)
{
    return kResultFalse;
}

tresult SMTG_CALLCONV Vst3WebViewEditor::onKeyDown(char16, int16, int16)
{
    return kResultFalse;
}

tresult SMTG_CALLCONV Vst3WebViewEditor::onKeyUp(char16, int16, int16)
{
    return kResultFalse;
}

tresult SMTG_CALLCONV Vst3WebViewEditor::getSize(ViewRect* size)
{
    if (!size) return kInvalidArgument;
    *size = rect_;
    return kResultOk;
}

tresult SMTG_CALLCONV Vst3WebViewEditor::onSize(ViewRect* newSize)
{
    if (!newSize) return kInvalidArgument;
    rect_ = *newSize;
    int w = std::max(kMinWidth, static_cast<int>(rect_.getWidth()));
    int h = std::max(kMinHeight, static_cast<int>(rect_.getHeight()));
    rect_.right = rect_.left + w;
    rect_.bottom = rect_.top + h;

    if (hwnd_)
    {
        SetWindowPos(hwnd_, nullptr, 0, 0, w, h, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
        if (controller_)
        {
            RECT rc{0, 0, w, h};
            controller_->put_Bounds(rc);
        }
    }
    return kResultOk;
}

tresult SMTG_CALLCONV Vst3WebViewEditor::onFocus(TBool state)
{
    if (state && controller_)
        controller_->MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC);
    return kResultOk;
}

tresult SMTG_CALLCONV Vst3WebViewEditor::setFrame(IPlugFrame* frame)
{
    if (plugFrame_)
        plugFrame_->release();
    plugFrame_ = frame;
    if (plugFrame_)
        plugFrame_->addRef();
    return kResultOk;
}

tresult SMTG_CALLCONV Vst3WebViewEditor::canResize()
{
    return kResultTrue;
}

tresult SMTG_CALLCONV Vst3WebViewEditor::checkSizeConstraint(ViewRect* rect)
{
    if (!rect) return kInvalidArgument;
    if (rect->getWidth() < kMinWidth)
        rect->right = rect->left + kMinWidth;
    if (rect->getHeight() < kMinHeight)
        rect->bottom = rect->top + kMinHeight;
    return kResultOk;
}

} // namespace nrs::vst3
