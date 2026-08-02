import LiquidGlass from 'liquid-glass-react'
import type { OverlayState } from './overlayState'

interface LyricsOverlayProps {
  state: OverlayState
}

export function LyricsOverlay({ state }: LyricsOverlayProps) {
  const isConnected = state.connectionState === 'connected'
  const hasLyric = state.lyricLine !== null

  return (
    <main className={`overlay overlay--${state.connectionState}`} aria-live="polite">
      <div className="ambient ambient--cyan" />
      <div className="ambient ambient--blue" />
      <LiquidGlass
        className="lyrics-glass"
        mode="standard"
        displacementScale={96}
        blurAmount={0.32}
        saturation={145}
        aberrationIntensity={2}
        elasticity={0.32}
        cornerRadius={34}
        padding="0"
        style={{
          position: 'absolute',
          top: '50%',
          left: '50%',
          width: 'calc(100% - 16px)',
          height: 'calc(100% - 16px)',
        }}
      >
        <section className="glass-content">
          <header className="brand-row">
            <div className="brand-lockup">
              <span className="flowcast-mark" aria-hidden="true">
                <i />
                <i />
                <i />
                <i />
                <i />
              </span>
              <span className="brand-name">FlowCast</span>
              <span className="brand-product">Lyrics</span>
            </div>
            <div className="connection-chip">
              <span className="connection-dot" aria-hidden="true" />
              <span>{isConnected ? 'Radmin 已連線' : 'Radmin VPN'}</span>
            </div>
          </header>

          <div className={`message ${hasLyric ? 'message--lyric' : 'message--status'}`}>
            <p data-testid={hasLyric ? 'lyric-line' : undefined}>{state.displayText}</p>
          </div>
        </section>
      </LiquidGlass>
    </main>
  )
}
