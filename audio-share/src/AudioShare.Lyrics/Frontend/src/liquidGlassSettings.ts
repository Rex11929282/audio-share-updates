export interface LiquidGlassSettings {
  displacementScale: number
  blurAmount: number
  saturation: number
  aberrationIntensity: number
  elasticity: number
  cornerRadius: number
}

export const officialLiquidGlassSettings: LiquidGlassSettings = {
  displacementScale: 70,
  blurAmount: 0.0625,
  saturation: 140,
  aberrationIntensity: 2,
  elasticity: 0.15,
  cornerRadius: 999,
}

export function readLiquidGlassSettingsMessage(message: unknown): LiquidGlassSettings | null {
  if (!isRecord(message) || message.type !== 'liquid-settings' || !isRecord(message.settings)) {
    return null
  }

  const settings = message.settings
  if (
    !isFiniteRange(settings.displacementScale, 0, 200)
    || !isFiniteRange(settings.blurAmount, 0, 1)
    || !isFiniteRange(settings.saturation, 0, 300)
    || !isFiniteRange(settings.aberrationIntensity, 0, 20)
    || !isFiniteRange(settings.elasticity, 0, 1)
    || !isFiniteRange(settings.cornerRadius, 0, 999)
  ) {
    return null
  }

  return {
    displacementScale: settings.displacementScale,
    blurAmount: settings.blurAmount,
    saturation: settings.saturation,
    aberrationIntensity: settings.aberrationIntensity,
    elasticity: settings.elasticity,
    cornerRadius: settings.cornerRadius,
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isFiniteRange(value: unknown, minimum: number, maximum: number): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= minimum && value <= maximum
}
