export type IslandMode = 'idle' | 'resolving' | 'playing' | 'paused' | 'noLyrics' | 'unavailable'
export type LyricSource = 'none' | 'localNetEase' | 'remoteRadmin'

export interface LyricLine {
  text: string
  startTimeMilliseconds: number
  endTimeMilliseconds: number | null
}

export interface OverlayState {
  mode: IslandMode
  displayText: string
  lyricLine: LyricLine | null
  source: LyricSource
}

export const initialState: OverlayState = {
  mode: 'idle',
  displayText: '等待播放',
  lyricLine: null,
  source: 'none',
}

const islandModes = new Set<IslandMode>([
  'idle',
  'resolving',
  'playing',
  'paused',
  'noLyrics',
  'unavailable',
])
const lyricSources = new Set<LyricSource>(['none', 'localNetEase', 'remoteRadmin'])

export function reduceOverlayState(current: OverlayState, message: unknown): OverlayState {
  if (!isRecord(message)) {
    return current
  }

  const mode = message.mode
  const displayText = message.displayText
  const source = message.source
  if (
    typeof mode !== 'string' ||
    !islandModes.has(mode as IslandMode) ||
    typeof displayText !== 'string' ||
    displayText.trim().length === 0 ||
    typeof source !== 'string' ||
    !lyricSources.has(source as LyricSource)
  ) {
    return current
  }

  const typedMode = mode as IslandMode
  const typedSource = source as LyricSource
  if ((typedMode === 'idle') !== (typedSource === 'none')) {
    return current
  }

  const lyricLine = parseLyricLine(message.lyricLine)
  const requiresLyric = typedMode === 'playing' || typedMode === 'paused'
  if ((requiresLyric && lyricLine === null) || (!requiresLyric && message.lyricLine !== null)) {
    return current
  }

  return {
    mode: typedMode,
    displayText: displayText.trim(),
    lyricLine,
    source: typedSource,
  }
}

function parseLyricLine(value: unknown): LyricLine | null {
  if (value === null) {
    return null
  }

  if (
    !isRecord(value) ||
    typeof value.text !== 'string' ||
    value.text.trim().length === 0 ||
    typeof value.startTimeMilliseconds !== 'number' ||
    !Number.isFinite(value.startTimeMilliseconds) ||
    value.startTimeMilliseconds < 0 ||
    (value.endTimeMilliseconds !== null &&
      (typeof value.endTimeMilliseconds !== 'number' || !Number.isFinite(value.endTimeMilliseconds)))
  ) {
    return null
  }

  return {
    text: value.text.trim(),
    startTimeMilliseconds: value.startTimeMilliseconds,
    endTimeMilliseconds: value.endTimeMilliseconds,
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}
