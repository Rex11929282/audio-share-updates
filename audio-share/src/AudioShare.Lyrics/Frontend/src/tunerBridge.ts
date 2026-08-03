import type { LiquidGlassSettings } from './liquidGlassSettings'
import type { FlowCastWebView } from './webview'

export interface TunerBridge {
  preview(settings: LiquidGlassSettings): void
  reset(): void
  cancel(): void
  save(): void
}

export function createTunerBridge(webview: FlowCastWebView): TunerBridge {
  return {
    preview: (settings) => webview.postMessage({ type: 'liquid-preview', settings }),
    reset: () => webview.postMessage({ type: 'liquid-reset' }),
    cancel: () => webview.postMessage({ type: 'liquid-cancel' }),
    save: () => webview.postMessage({ type: 'liquid-save' }),
  }
}
