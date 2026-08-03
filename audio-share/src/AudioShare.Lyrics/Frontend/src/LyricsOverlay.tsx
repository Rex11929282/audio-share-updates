import LiquidGlass from 'liquid-glass-react'
import type { CSSProperties } from 'react'
import type { LiquidGlassSettings } from './liquidGlassSettings'
import type { OverlayState } from './overlayState'

interface LyricsOverlayProps {
  state: OverlayState
  backdropImageUrl?: string | null
  liquidSettings: LiquidGlassSettings
}

const glassPositionStyle: CSSProperties = {
  position: 'absolute',
  top: '50%',
  left: '50%',
  width: 'calc(100% - 8px)',
  height: 'calc(100% - 8px)',
}

export function LyricsOverlay({ state, backdropImageUrl = null, liquidSettings }: LyricsOverlayProps) {
  const hasLyric = state.mode === 'playing' || state.mode === 'paused'
  const messageKey = `${state.mode}:${state.lyricLine?.startTimeMilliseconds ?? state.displayText}`

  return (
    <main className={`overlay overlay--${state.mode}`} aria-live="polite">
      <div
        className={`scene-backdrop${backdropImageUrl ? ' scene-backdrop--live' : ''}`}
        style={backdropImageUrl ? { backgroundImage: `url("${backdropImageUrl}")` } : undefined}
        aria-hidden="true"
      />
      <LiquidGlass
        className="lyrics-glass"
        mode="standard"
        displacementScale={liquidSettings.displacementScale}
        blurAmount={liquidSettings.blurAmount}
        saturation={liquidSettings.saturation}
        aberrationIntensity={liquidSettings.aberrationIntensity}
        elasticity={liquidSettings.elasticity}
        cornerRadius={liquidSettings.cornerRadius}
        padding="0"
        style={glassPositionStyle}
      >
        <section className={`glass-content${backdropImageUrl ? ' glass-content--live-backdrop' : ''}`}>
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
