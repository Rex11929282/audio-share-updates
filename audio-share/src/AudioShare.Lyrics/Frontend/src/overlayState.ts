export type ConnectionState = 'searching' | 'unavailable' | 'connected' | 'disconnected'

export interface LyricLine {
  text: string
  startTimeMilliseconds: number
  endTimeMilliseconds: number | null
}

export interface OverlayState {
  connectionState: ConnectionState
  displayText: string
  lyricLine: LyricLine | null
}

export const initialState: OverlayState = {
  connectionState: 'searching',
  displayText: '正在尋找 FlowCast',
  lyricLine: null,
}

const connectionStates = new Set<ConnectionState>([
  'searching',
  'unavailable',
  'connected',
  'disconnected',
])

export function reduceOverlayState(current: OverlayState, message: unknown): OverlayState {
  if (!isRecord(message)) {
    return current
  }

  const connectionState = message.connectionState
  const displayText = message.displayText
  if (
    typeof connectionState !== 'string' ||
    !connectionStates.has(connectionState as ConnectionState) ||
    typeof displayText !== 'string' ||
    displayText.trim().length === 0
  ) {
    return current
  }

  const lyricLine = parseLyricLine(message.lyricLine)
  if (message.lyricLine !== null && lyricLine === null) {
    return current
  }

  return {
    connectionState: connectionState as ConnectionState,
    displayText,
    lyricLine,
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
    (value.endTimeMilliseconds !== null && typeof value.endTimeMilliseconds !== 'number')
  ) {
    return null
  }

  return {
    text: value.text,
    startTimeMilliseconds: value.startTimeMilliseconds,
    endTimeMilliseconds: value.endTimeMilliseconds,
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}
