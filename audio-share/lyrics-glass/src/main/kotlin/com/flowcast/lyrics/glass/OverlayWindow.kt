package com.flowcast.lyrics.glass

import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.snapshotFlow
import androidx.compose.foundation.layout.Box
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.input.pointer.PointerEventType
import androidx.compose.ui.input.pointer.isPrimaryPressed
import androidx.compose.ui.input.pointer.onPointerEvent
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Window
import androidx.compose.ui.window.WindowPosition
import androidx.compose.ui.window.rememberWindowState
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.drop

@OptIn(FlowPreview::class)
@Composable
fun OverlayWindow(
    rendererState: RendererState,
    onEvent: (RendererEvent) -> Unit,
) {
    val presentation = overlayPresentation(rendererState)
    val position = rendererState.position?.let {
        WindowPosition.Absolute(it.x.dp, it.y.dp)
    } ?: WindowPosition.Absolute(120.dp, 120.dp)
    val windowState = rememberWindowState(
        position = position,
        width = presentation.dimensions.width.dp,
        height = presentation.dimensions.height.dp,
    )

    LaunchedEffect(presentation.dimensions) {
        windowState.size = DpSize(presentation.dimensions.width.dp, presentation.dimensions.height.dp)
    }
    LaunchedEffect(windowState) {
        snapshotFlow { windowState.position }
            .drop(1)
            .debounce(150)
            .collect { settled ->
                (settled as? WindowPosition.Absolute)?.let {
                    onEvent(PositionChangedEvent(ProtocolVersion, it.x.value.toDouble(), it.y.value.toDouble()))
                }
            }
    }

    var closeRequested by remember { mutableStateOf(false) }
    fun emit(event: RendererEvent) {
        if (event !is CloseRequestEvent || !closeRequested) {
            if (event is CloseRequestEvent) closeRequested = true
            onEvent(event)
        }
    }
    Window(
        onCloseRequest = { emit(CloseRequestEvent(ProtocolVersion)) },
        state = windowState,
        undecorated = true,
        transparent = true,
        alwaysOnTop = true,
        resizable = false,
    ) {
        LaunchedEffect(Unit) {
            window.type = java.awt.Window.Type.UTILITY
        }
        WindowDraggableArea(windowState) {
            GlassOverlay(rendererState, ::emit)
        }
    }
}

@OptIn(ExperimentalComposeUiApi::class)
@Composable
private fun WindowDraggableArea(
    windowState: androidx.compose.ui.window.WindowState,
    content: @Composable () -> Unit,
) {
    val density = LocalDensity.current
    var dragOrigin: Pair<Float, Float>? by remember { mutableStateOf(null) }
    var windowOrigin: Pair<Float, Float>? by remember { mutableStateOf(null) }
    Box(
        androidx.compose.ui.Modifier
            .onPointerEvent(PointerEventType.Press) { event ->
                if (event.buttons.isPrimaryPressed) {
                    (windowState.position as? WindowPosition.Absolute)?.let {
                        dragOrigin = event.changes.firstOrNull()?.position?.let { point -> point.x to point.y }
                        windowOrigin = it.x.value to it.y.value
                    }
                }
            }
            .onPointerEvent(PointerEventType.Move) { event ->
                val start = dragOrigin
                val origin = windowOrigin
                val point = event.changes.firstOrNull()?.position
                if (start != null && origin != null && point != null && event.buttons.isPrimaryPressed) {
                    with(density) {
                        windowState.position = WindowPosition.Absolute(
                            (origin.first + point.x - start.first).toDp(),
                            (origin.second + point.y - start.second).toDp(),
                        )
                    }
                }
            }
            .onPointerEvent(PointerEventType.Release) {
                dragOrigin = null
                windowOrigin = null
            },
    ) {
        content()
    }
}
