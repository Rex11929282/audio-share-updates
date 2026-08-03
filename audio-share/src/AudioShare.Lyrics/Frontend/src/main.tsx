import { StrictMode, useEffect, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { LyricsOverlay } from './LyricsOverlay'
import { LiquidGlassTuner } from './LiquidGlassTuner'
import {
  officialLiquidGlassSettings,
  readLiquidGlassSettingsMessage,
} from './liquidGlassSettings'
import { initialState, reduceOverlayState } from './overlayState'
import { createTunerBridge } from './tunerBridge'
import './styles.css'

function App() {
  const [state, setState] = useState(initialState)
  const [backdropImageUrl, setBackdropImageUrl] = useState<string | null>(null)
  const [liquidSettings, setLiquidSettings] = useState(officialLiquidGlassSettings)

  useEffect(() => {
    const webview = window.chrome?.webview
    if (!webview) {
      return
    }

    const onMessage = (event: MessageEvent<unknown>) => {
      const nextLiquidSettings = readLiquidGlassSettingsMessage(event.data)
      if (nextLiquidSettings) {
        setLiquidSettings(nextLiquidSettings)
        return
      }

      if (
        typeof event.data === 'object'
        && event.data !== null
        && 'type' in event.data
        && event.data.type === 'backdrop'
        && 'dataUrl' in event.data
        && typeof event.data.dataUrl === 'string'
        && event.data.dataUrl.startsWith('data:image/jpeg;base64,')
      ) {
        setBackdropImageUrl(event.data.dataUrl)
        return
      }

      setState((current) => reduceOverlayState(current, event.data))
    }

    webview.addEventListener('message', onMessage)
    webview.postMessage({ type: 'ready' })
    return () => webview.removeEventListener('message', onMessage)
  }, [])

  return (
    <LyricsOverlay
      state={state}
      backdropImageUrl={backdropImageUrl}
      liquidSettings={liquidSettings}
    />
  )
}

function TunerSurface() {
  const [settings, setSettings] = useState(officialLiquidGlassSettings)
  const webview = window.chrome?.webview

  useEffect(() => {
    if (!webview) {
      return
    }

    const onMessage = (event: MessageEvent<unknown>) => {
      const next = readLiquidGlassSettingsMessage(event.data)
      if (next) {
        setSettings(next)
      }
    }

    webview.addEventListener('message', onMessage)
    webview.postMessage({ type: 'tuner-ready' })
    return () => webview.removeEventListener('message', onMessage)
  }, [webview])

  if (!webview) {
    return null
  }

  return <LiquidGlassTuner settings={settings} bridge={createTunerBridge(webview)} />
}

const isTuner = new URLSearchParams(window.location.search).get('surface') === 'tuner'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {isTuner ? <TunerSurface /> : <App />}
  </StrictMode>,
)
