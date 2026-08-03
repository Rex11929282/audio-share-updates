import { describe, expect, it } from 'vitest'
import {
  officialLiquidGlassSettings,
  readLiquidGlassSettingsMessage,
} from './liquidGlassSettings'

describe('readLiquidGlassSettingsMessage', () => {
  it('uses the exact GitHub defaults', () => {
    expect(officialLiquidGlassSettings).toEqual({
      displacementScale: 70,
      blurAmount: 0.0625,
      saturation: 140,
      aberrationIntensity: 2,
      elasticity: 0.15,
      cornerRadius: 999,
    })
  })

  it('accepts one complete in-range settings message', () => {
    expect(readLiquidGlassSettingsMessage({
      type: 'liquid-settings',
      settings: {
        displacementScale: 80,
        blurAmount: 0.2,
        saturation: 150,
        aberrationIntensity: 3,
        elasticity: 0.3,
        cornerRadius: 80,
      },
    })).toEqual({
      displacementScale: 80,
      blurAmount: 0.2,
      saturation: 150,
      aberrationIntensity: 3,
      elasticity: 0.3,
      cornerRadius: 80,
    })
  })

  it('rejects partial, non-finite, and out-of-range settings', () => {
    expect(readLiquidGlassSettingsMessage({ type: 'liquid-settings', settings: { displacementScale: 70 } })).toBeNull()
    expect(readLiquidGlassSettingsMessage({
      type: 'liquid-settings',
      settings: { ...officialLiquidGlassSettings, blurAmount: Number.NaN },
    })).toBeNull()
    expect(readLiquidGlassSettingsMessage({
      type: 'liquid-settings',
      settings: { ...officialLiquidGlassSettings, saturation: 301 },
    })).toBeNull()
  })
})
