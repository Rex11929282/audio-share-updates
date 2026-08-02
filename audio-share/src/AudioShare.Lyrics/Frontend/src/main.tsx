import { StrictMode, useEffect, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { LyricsOverlay } from './LyricsOverlay'
import { initialState, reduceOverlayState } from './overlayState'
import './styles.css'

function App() {
  const [state, setState] = useState(initialState)

  useEffect(() => {
    const webview = window.chrome?.webview
    if (!webview) {
      return
    }

    const onMessage = (event: MessageEvent<unknown>) => {
      setState((current) => reduceOverlayState(current, event.data))
    }

    webview.addEventListener('message', onMessage)
    webview.postMessage({ type: 'ready' })
    return () => webview.removeEventListener('message', onMessage)
  }, [])

  return <LyricsOverlay state={state} />
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
