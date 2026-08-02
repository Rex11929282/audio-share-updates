import { render, screen } from '@testing-library/react'
import type { PropsWithChildren } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { LyricsOverlay } from './LyricsOverlay'

vi.mock('liquid-glass-react', () => ({
  default: ({ children }: PropsWithChildren) => <div data-testid="liquid-glass">{children}</div>,
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
})
