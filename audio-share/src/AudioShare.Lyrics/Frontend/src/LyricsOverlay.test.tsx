import { render, screen } from '@testing-library/react'
import type { CSSProperties, PropsWithChildren } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { LyricsOverlay } from './LyricsOverlay'

vi.mock('liquid-glass-react', () => ({
  default: ({ children, className, padding, style }: PropsWithChildren<{
    className?: string
    padding?: string
    style?: CSSProperties
  }>) => (
    <div data-testid="liquid-glass" className={className} data-padding={padding} style={style}>
      {children}
    </div>
  ),
}))

describe('LyricsOverlay', () => {
  it('shows the waiting copy without inventing a lyric', () => {
    render(
      <LyricsOverlay
        state={{
          connectionState: 'connected',
          displayText: '已連線，等待歌詞',
          lyricLine: null,
        }}
      />,
    )

    expect(screen.getByText('已連線，等待歌詞')).toBeInTheDocument()
    expect(screen.queryByTestId('lyric-line')).not.toBeInTheDocument()
  })

  it('stacks the liquid glass layers instead of placing the content below the window', () => {
    const { container } = render(
      <LyricsOverlay
        state={{
          connectionState: 'searching',
          displayText: '正在尋找 FlowCast',
          lyricLine: null,
        }}
      />,
    )

    expect(container.querySelector('[data-testid="liquid-glass"]')).toHaveStyle({
      position: 'absolute',
      top: '50%',
      left: '50%',
    })
    expect(container.querySelector('[data-testid="liquid-glass"]')).toHaveClass('lyrics-glass')
    expect(container.querySelector('[data-testid="liquid-glass"]')).toHaveAttribute('data-padding', '0')
  })

  it('renders only the lyric capsule without window chrome', () => {
    const { container } = render(
      <LyricsOverlay
        state={{
          connectionState: 'connected',
          displayText: '已連線，等待歌詞',
          lyricLine: null,
        }}
      />,
    )

    expect(container.querySelector('.brand-row')).not.toBeInTheDocument()
    expect(container.querySelector('.connection-chip')).not.toBeInTheDocument()
    expect(container.querySelector('.glass-content')).toBeInTheDocument()
  })
})
