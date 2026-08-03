import { render, screen } from '@testing-library/react'
import type { CSSProperties, PropsWithChildren } from 'react'
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { LyricsOverlay } from './LyricsOverlay'
import { officialLiquidGlassSettings } from './liquidGlassSettings'
import type { IslandMode, LyricSource } from './overlayState'
import './styles.css'
import styles from './styles.css?raw'

vi.mock('liquid-glass-react', () => ({
  default: ({
    children,
    className,
    padding,
    style,
    mode,
    displacementScale,
    blurAmount,
    saturation,
    aberrationIntensity,
    elasticity,
    cornerRadius,
  }: PropsWithChildren<{
    className?: string
    padding?: string
    style?: CSSProperties
    mode?: string
    displacementScale?: number
    blurAmount?: number
    saturation?: number
    aberrationIntensity?: number
    elasticity?: number
    cornerRadius?: number
  }>) => (
    <div
      data-testid="liquid-glass"
      className={className}
      data-padding={padding}
      data-mode={mode}
      data-displacement-scale={displacementScale}
      data-blur-amount={blurAmount}
      data-saturation={saturation}
      data-aberration-intensity={aberrationIntensity}
      data-elasticity={elasticity}
      data-corner-radius={cornerRadius}
      style={style}
    >
      {children}
    </div>
  ),
}))

describe('LyricsOverlay', () => {
  const stylesheet = document.createElement('style')

  beforeAll(() => {
    stylesheet.textContent = styles
    document.head.append(stylesheet)
  })

  afterAll(() => stylesheet.remove())

  const statusModes: ReadonlyArray<[IslandMode, string, LyricSource]> = [
    ['idle', '等待播放', 'none'],
    ['resolving', '正在取得歌詞', 'localNetEase'],
    ['noLyrics', '這首歌沒有歌詞', 'localNetEase'],
    ['unavailable', '暫時無法取得歌詞', 'localNetEase'],
  ]

  it.each(statusModes)('renders %s with exact status copy and no lyric', (mode, displayText, source) => {
    const { container } = render(
      <LyricsOverlay state={{ mode, displayText, lyricLine: null, source }} liquidSettings={officialLiquidGlassSettings} />,
    )

    expect(container.querySelector('main')).toHaveClass(`overlay--${mode}`)
    expect(screen.getByText(displayText)).toBeInTheDocument()
    expect(screen.queryByTestId('lyric-line')).not.toBeInTheDocument()
  })

  it.each([
    ['playing', 'localNetEase'],
    ['paused', 'remoteRadmin'],
  ] as const)('renders %s as a real lyric line', (mode, source) => {
    const { container } = render(
      <LyricsOverlay
        state={{
          mode,
          displayText: '真實歌詞',
          lyricLine: { text: '真實歌詞', startTimeMilliseconds: 1000, endTimeMilliseconds: 2500 },
          source,
        }}
        liquidSettings={officialLiquidGlassSettings}
      />,
    )

    expect(container.querySelector('main')).toHaveClass(`overlay--${mode}`)
    expect(container.querySelector('[data-testid="lyric-line"]')).toHaveTextContent('真實歌詞')
  })

  it('uses one standard liquid-glass capsule with no window chrome or controls', () => {
    const liquidSettings = {
      displacementScale: 80,
      blurAmount: 0.2,
      saturation: 150,
      aberrationIntensity: 3,
      elasticity: 0.3,
      cornerRadius: 80,
    }
    const { container } = render(
      <LyricsOverlay
        state={{ mode: 'idle', displayText: '等待播放', lyricLine: null, source: 'none' }}
        liquidSettings={liquidSettings}
      />,
    )

    const glass = container.querySelectorAll('[data-testid="liquid-glass"]')
    expect(glass).toHaveLength(1)
    expect(glass[0]).toHaveAttribute('data-mode', 'standard')
    expect(glass[0]).toHaveClass('lyrics-glass')
    expect(glass[0]).toHaveAttribute('data-padding', '0')
    expect(glass[0]).toHaveAttribute('data-displacement-scale', '80')
    expect(glass[0]).toHaveAttribute('data-blur-amount', '0.2')
    expect(glass[0]).toHaveAttribute('data-saturation', '150')
    expect(glass[0]).toHaveAttribute('data-aberration-intensity', '3')
    expect(glass[0]).toHaveAttribute('data-elasticity', '0.3')
    expect(glass[0]).toHaveAttribute('data-corner-radius', '80')
    expect(glass[0]).toHaveStyle({ position: 'absolute', top: '50%', left: '50%' })
    expect(container.querySelector('.brand-row')).not.toBeInTheDocument()
    expect(container.querySelector('.connection-chip')).not.toBeInTheDocument()
    expect(container.querySelector('.close-button')).not.toBeInTheDocument()
    expect(container.querySelector('.drag-handle')).not.toBeInTheDocument()
  })

  it('places the captured desktop behind the liquid glass instead of painting a fake gradient', () => {
    const { container } = render(
      <LyricsOverlay
        state={{ mode: 'idle', displayText: '????', lyricLine: null, source: 'none' }}
        backdropImageUrl="data:image/jpeg;base64,desktop-frame"
        liquidSettings={officialLiquidGlassSettings}
      />,
    )

    const backdrop = container.querySelector('.scene-backdrop')
    expect(backdrop).toHaveStyle({
      backgroundImage: 'url("data:image/jpeg;base64,desktop-frame")',
    })
    const content = container.querySelector('.glass-content')
    expect(content).toHaveClass('glass-content--live-backdrop')
    expect(getComputedStyle(content!).backgroundImage).toBe('none')
  })

  it('keeps the capsule transparent until a desktop frame is available', () => {
    const { container } = render(
      <LyricsOverlay
        state={{ mode: 'idle', displayText: '????', lyricLine: null, source: 'none' }}
        liquidSettings={officialLiquidGlassSettings}
      />,
    )

    expect(getComputedStyle(container.querySelector('.overlay')!).backgroundColor).toBe('rgba(0, 0, 0, 0)')
    expect(getComputedStyle(container.querySelector('.scene-backdrop')!).backgroundColor).toBe('rgba(0, 0, 0, 0)')
    expect(styles).not.toContain('background: rgb(18 28 40')
  })
})
