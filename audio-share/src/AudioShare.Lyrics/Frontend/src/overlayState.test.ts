import { describe, expect, it } from 'vitest'
import { initialState, reduceOverlayState } from './overlayState'

describe('reduceOverlayState', () => {
  it('accepts the honest connected waiting state', () => {
    const state = reduceOverlayState(initialState, {
      connectionState: 'connected',
      displayText: '已連線，等待歌詞',
      lyricLine: null,
    })

    expect(state.displayText).toBe('已連線，等待歌詞')
    expect(state.lyricLine).toBeNull()
  })

  it('ignores malformed bridge messages', () => {
    expect(reduceOverlayState(initialState, { connectionState: 'unknown' })).toBe(initialState)
  })
})
