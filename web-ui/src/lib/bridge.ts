type PendingRequest = {
  resolve: (data: unknown) => void
  reject: (err: Error) => void
}

type EventHandler = (data: unknown) => void

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (msg: unknown) => void
        addEventListener: (type: "message", fn: (e: { data: unknown }) => void) => void
        removeEventListener: (type: "message", fn: (e: { data: unknown }) => void) => void
      }
    }
  }
}

class StudioBridge {
  private pending = new Map<string, PendingRequest>()
  private listeners = new Map<string, Set<EventHandler>>()
  private seq = 0

  public readonly isWebView2 = Boolean(window.chrome?.webview)
  private _hostMode: "standalone" | "vst3" | "clap" = (() => {
    if (typeof window === "undefined") return "standalone"
    const hostParam = new URLSearchParams(window.location.search).get("host")
    if (hostParam === "clap") return "clap"
    if (hostParam === "vst3") return "vst3"
    return "standalone"
  })()

  public get hostMode(): "standalone" | "vst3" | "clap" {
    return this._hostMode
  }

  public get isVst3Host(): boolean {
    return this._hostMode === "vst3" || this._hostMode === "clap"
  }

  public setHostMode(mode?: string) {
    if (mode === "vst3" || mode === "clap" || mode === "standalone") {
      this._hostMode = mode
    }
  }

  constructor() {
    if (window.chrome?.webview) {
      window.chrome.webview.addEventListener("message", (evt) => {
        const msg = evt.data as Record<string, unknown> | null
        if (!msg || typeof msg !== "object") return

        if (msg.type === "response" && typeof msg.reqId === "string") {
          const entry = this.pending.get(msg.reqId)
          if (!entry) return
          this.pending.delete(msg.reqId)
          if (msg.ok) entry.resolve(msg.data)
          else entry.reject(new Error(String(msg.error || "요청 실패")))
          return
        }

        if (typeof msg.type === "string") {
          const set = this.listeners.get(msg.type)
          if (set) {
            for (const fn of set) fn(msg.data)
          }
        }
      })
    }
  }

  public on<T = unknown>(eventType: string, handler: (data: T) => void): () => void {
    let set = this.listeners.get(eventType)
    if (!set) {
      set = new Set()
      this.listeners.set(eventType, set)
    }
    const wrapped = handler as EventHandler
    set.add(wrapped)
    return () => {
      set?.delete(wrapped)
    }
  }

  public invoke<T = unknown>(action: string, payload?: unknown): Promise<T> {
    if (!window.chrome?.webview) {
      return Promise.reject(new Error(`WebView2 호스트 외부에서는 '${action}' 네이티브 기능을 사용할 수 없습니다.`))
    }
    const reqId = `r_${++this.seq}_${Date.now()}`
    return new Promise<T>((resolve, reject) => {
      this.pending.set(reqId, {
        resolve: resolve as (v: unknown) => void,
        reject,
      })
      window.chrome!.webview!.postMessage({ reqId, action, payload: payload ?? {} })
    })
  }
}

export const studioBridge = new StudioBridge()
