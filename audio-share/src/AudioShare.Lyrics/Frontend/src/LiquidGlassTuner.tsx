import { useEffect, useState } from 'react'
import LiquidGlass from 'liquid-glass-react'
import type { LiquidGlassSettings } from './liquidGlassSettings'
import type { TunerBridge } from './tunerBridge'

const controls = [
  { key: 'displacementScale', min: 0, max: 200, step: 1 },
  { key: 'blurAmount', min: 0, max: 1, step: 0.001 },
  { key: 'saturation', min: 0, max: 300, step: 1 },
  { key: 'aberrationIntensity', min: 0, max: 20, step: 0.1 },
  { key: 'elasticity', min: 0, max: 1, step: 0.01 },
  { key: 'cornerRadius', min: 0, max: 999, step: 1 },
] as const

interface LiquidGlassTunerProps {
  settings: LiquidGlassSettings
  bridge: TunerBridge
}

export function LiquidGlassTuner({ settings, bridge }: LiquidGlassTunerProps) {
  const [draft, setDraft] = useState(settings)

  useEffect(() => setDraft(settings), [settings])

  const update = (control: (typeof controls)[number], rawValue: string) => {
    const parsed = Number(rawValue)
    if (!Number.isFinite(parsed)) {
      return
    }

    const value = Math.min(control.max, Math.max(control.min, parsed))
    const next = { ...draft, [control.key]: value }
    setDraft(next)
    bridge.preview(next)
  }

  return (
    <main className="tuner-shell">
      <header>
        <h1>Liquid Glass</h1>
        <p>rdev/liquid-glass-react 1.1.1</p>
      </header>

      <div className="tuner-preview-scene" aria-label="Liquid Glass preview">
        <LiquidGlass
          mode="standard"
          displacementScale={draft.displacementScale}
          blurAmount={draft.blurAmount}
          saturation={draft.saturation}
          aberrationIntensity={draft.aberrationIntensity}
          elasticity={draft.elasticity}
          cornerRadius={draft.cornerRadius}
          padding="18px 34px"
        >
          <span>Liquid Glass</span>
        </LiquidGlass>
      </div>

      <section className="tuner-controls">
        {controls.map((control) => (
          <label className="tuner-control" key={control.key}>
            <span>{control.key}</span>
            <input
              aria-label={`${control.key} slider`}
              type="range"
              min={control.min}
              max={control.max}
              step={control.step}
              value={draft[control.key]}
              onChange={(event) => update(control, event.currentTarget.value)}
            />
            <input
              aria-label={`${control.key} value`}
              type="number"
              min={control.min}
              max={control.max}
              step={control.step}
              value={draft[control.key]}
              onChange={(event) => update(control, event.currentTarget.value)}
            />
          </label>
        ))}
      </section>

      <footer>
        <button type="button" onClick={bridge.reset}>Reset</button>
        <button type="button" onClick={bridge.cancel}>Cancel</button>
        <button type="button" className="primary" onClick={bridge.save}>Save</button>
      </footer>
    </main>
  )
}
