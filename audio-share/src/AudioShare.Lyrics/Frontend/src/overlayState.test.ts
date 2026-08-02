import { describe, expect, it } from 'vitest'
import { initialState, reduceOverlayState } from './overlayState'

describe('reduceOverlayState', () => {
  it('accepts a real playing lyric and its local source', () => {
    const playing = reduceOverlayState(initialState, {
      mode: 'playing',
      displayText: '真實歌詞',
      lyricLine: { text: '真實歌詞', startTimeMilliseconds: 1000, endTimeMilliseconds: 2500 },
      source: 'localNetEase',
    })

    expect(playing.mode).toBe('playing')
    expect(playing.source).toBe('localNetEase')
    expect(playing.lyricLine?.text).toBe('真實歌詞')
  })

  it('accepts every status mode only without a lyric', () => {
    const states = [
      ['idle', '等待播放', 'none'],
      ['resolving', '正在取得歌詞', 'localNetEase'],
      ['noLyrics', '這首歌沒有歌詞', 'localNetEase'],
      ['unavailable', '暫時無法取得歌詞', 'localNetEase'],
    ] as const

    for (const [mode, displayText, source] of states) {
      expect(reduceOverlayState(initialState, { mode, displayText, lyricLine: null, source })).toEqual({
        mode,
        displayText,
        lyricLine: null,
        source,
      })
    }
  })

  it('rejects unknown modes and sources', () => {
    expect(reduceOverlayState(initialState, {
      mode: 'unknown',
      displayText: '錯誤',
      lyricLine: null,
      source: 'none',
    })).toBe(initialState)
    expect(reduceOverlayState(initialState, {
      mode: 'idle',
      displayText: '等待播放',
      lyricLine: null,
      source: 'unknown',
    })).toBe(initialState)
  })

  it('rejects a lyric attached to a status mode', () => {
    expect(reduceOverlayState(initialState, {
      mode: 'idle',
      displayText: '假歌詞',
      lyricLine: { text: '假歌詞', startTimeMilliseconds: 0, endTimeMilliseconds: null },
      source: 'none',
    })).toBe(initialState)
  })

  it('rejects playing or paused snapshots without a valid lyric', () => {
    expect(reduceOverlayState(initialState, {
      mode: 'playing',
      displayText: '假歌詞',
      lyricLine: null,
      source: 'remoteRadmin',
    })).toBe(initialState)
    expect(reduceOverlayState(initialState, {
      mode: 'paused',
      displayText: '假歌詞',
      lyricLine: { text: ' ', startTimeMilliseconds: 0, endTimeMilliseconds: null },
      source: 'remoteRadmin',
    })).toBe(initialState)
  })
})
