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
        mode="standard"
        displacementScale={34}
        blurAmount={0.11}
        saturation={122}
        aberrationIntensity={1}
        elasticity={0.12}
        cornerRadius={34}
        overLight
        style={{ width: '100%', height: '100%' }}
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
