import LiquidGlass from 'liquid-glass-react'
import type { OverlayState } from './overlayState'

interface LyricsOverlayProps {
  state: OverlayState
}

export function LyricsOverlay({ state }: LyricsOverlayProps) {
  const hasLyric = state.mode === 'playing' || state.mode === 'paused'
  const messageKey = `${state.mode}:${state.lyricLine?.startTimeMilliseconds ?? state.displayText}`

  return (
    <main className={`overlay overlay--${state.mode}`} aria-live="polite">
      <LiquidGlass
        className="lyrics-glass"
        mode="standard"
        displacementScale={96}
        blurAmount={0.32}
        saturation={145}
        aberrationIntensity={2}
        elasticity={0.32}
        cornerRadius={46}
        padding="0"
        style={{
          position: 'absolute',
          top: '50%',
          left: '50%',
          width: 'calc(100% - 8px)',
          height: 'calc(100% - 8px)',
        }}
      >
        <section className="glass-content">
          <div
            key={messageKey}
            className={`message ${hasLyric ? 'message--lyric' : 'message--status'}`}
          >
            <p data-testid={hasLyric ? 'lyric-line' : undefined}>{state.displayText}</p>
          </div>
        </section>
      </LiquidGlass>
    </main>
  )
}
