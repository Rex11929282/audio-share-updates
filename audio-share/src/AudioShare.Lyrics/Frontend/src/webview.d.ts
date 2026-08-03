export interface FlowCastWebView {
  addEventListener(type: 'message', listener: (event: MessageEvent<unknown>) => void): void
  removeEventListener(type: 'message', listener: (event: MessageEvent<unknown>) => void): void
  postMessage(message: unknown): void
}

declare global {
  interface Window {
    chrome?: {
      webview?: FlowCastWebView
    }
  }
}
