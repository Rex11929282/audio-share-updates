import { describe, expect, it, vi } from 'vitest'
import { createTunerBridge } from './tunerBridge'
import { officialLiquidGlassSettings } from './liquidGlassSettings'
import type { FlowCastWebView } from './webview'

function createWebView(postMessage: (message: unknown) => void): FlowCastWebView {
  return {
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    postMessage,
  }
}

describe('createTunerBridge', () => {
  it('posts the exact preview command', () => {
    const postMessage = vi.fn<(message: unknown) => void>()
    const bridge = createTunerBridge(createWebView(postMessage))

    bridge.preview(officialLiquidGlassSettings)

    expect(postMessage).toHaveBeenCalledWith({ type: 'liquid-preview', settings: officialLiquidGlassSettings })
  })

  it('posts separate reset, cancel, and save commands', () => {
    const postMessage = vi.fn<(message: unknown) => void>()
    const bridge = createTunerBridge(createWebView(postMessage))

    bridge.reset()
    bridge.cancel()
    bridge.save()

    expect(postMessage).toHaveBeenNthCalledWith(1, { type: 'liquid-reset' })
    expect(postMessage).toHaveBeenNthCalledWith(2, { type: 'liquid-cancel' })
    expect(postMessage).toHaveBeenNthCalledWith(3, { type: 'liquid-save' })
  })
})
