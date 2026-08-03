import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { PropsWithChildren } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { LiquidGlassTuner } from './LiquidGlassTuner'
import { officialLiquidGlassSettings } from './liquidGlassSettings'
import type { TunerBridge } from './tunerBridge'

vi.mock('liquid-glass-react', () => ({
  default: ({ children }: PropsWithChildren) => <div>{children}</div>,
}))

function createBridge(): TunerBridge {
  return {
    preview: vi.fn(),
    reset: vi.fn(),
    cancel: vi.fn(),
    save: vi.fn(),
  }
}

describe('LiquidGlassTuner', () => {
  afterEach(cleanup)

  it('renders exactly six official prop controls', () => {
    const bridge = createBridge()

    render(<LiquidGlassTuner settings={officialLiquidGlassSettings} bridge={bridge} />)

    expect(screen.getAllByRole('slider')).toHaveLength(6)
    for (const name of Object.keys(officialLiquidGlassSettings)) {
      expect(screen.getByText(name)).toBeInTheDocument()
    }
  })

  it('previews a slider value without saving', () => {
    const bridge = createBridge()

    render(<LiquidGlassTuner settings={officialLiquidGlassSettings} bridge={bridge} />)
    fireEvent.change(screen.getByLabelText('displacementScale slider'), { target: { value: '82' } })

    expect(bridge.preview).toHaveBeenLastCalledWith({
      ...officialLiquidGlassSettings,
      displacementScale: 82,
    })
    expect(bridge.save).not.toHaveBeenCalled()
  })

  it('previews a number value without saving', () => {
    const bridge = createBridge()

    render(<LiquidGlassTuner settings={officialLiquidGlassSettings} bridge={bridge} />)
    fireEvent.change(screen.getByLabelText('blurAmount value'), { target: { value: '0.2' } })

    expect(bridge.preview).toHaveBeenLastCalledWith({
      ...officialLiquidGlassSettings,
      blurAmount: 0.2,
    })
    expect(bridge.save).not.toHaveBeenCalled()
  })

  it('maps Reset Cancel and Save to separate bridge commands', () => {
    const bridge = createBridge()

    render(<LiquidGlassTuner settings={officialLiquidGlassSettings} bridge={bridge} />)
    fireEvent.click(screen.getByRole('button', { name: 'Reset' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(bridge.reset).toHaveBeenCalledOnce()
    expect(bridge.cancel).toHaveBeenCalledOnce()
    expect(bridge.save).toHaveBeenCalledOnce()
  })
})
