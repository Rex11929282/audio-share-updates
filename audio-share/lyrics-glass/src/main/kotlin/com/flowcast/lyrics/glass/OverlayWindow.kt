package com.flowcast.lyrics.glass

import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.foundation.layout.Box
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
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

@Composable
fun OverlayWindow(
    rendererState: RendererState,
    onEvent: (RendererEvent) -> Unit,
) {
    val presentation = overlayPresentation(rendererState)
    val position = rendererState.position?.let {
        WindowPosition.Absolute(it.x.dp, it.y.dp)
    } ?: WindowPosition.Aligned(Alignment.Center)
    val windowState = rememberWindowState(
        position = position,
        width = presentation.dimensions.width.dp,
        height = presentation.dimensions.height.dp,
    )

    LaunchedEffect(presentation.dimensions) {
        windowState.size = DpSize(presentation.dimensions.width.dp, presentation.dimensions.height.dp)
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
        WindowDraggableArea(windowState, window, ::emit) {
            GlassOverlay(rendererState, ::emit)
        }
    }
}

internal class OverlayDragTracker {
    private var windowAtPress: OverlayPosition? = null
    private var pointerAtPress: OverlayPosition? = null
    private var lastPosition: OverlayPosition? = null
    private var moved = false

    fun begin(windowAtPress: OverlayPosition, pointerAtPress: OverlayPosition) {
        this.windowAtPress = windowAtPress
        this.pointerAtPress = pointerAtPress
        lastPosition = null
        moved = false
    }

    fun move(pointer: OverlayPosition): OverlayPosition? {
        val initialWindow = windowAtPress ?: return null
        val initialPointer = pointerAtPress ?: return null
        val next = OverlayPosition(
            x = initialWindow.x + pointer.x - initialPointer.x,
            y = initialWindow.y + pointer.y - initialPointer.y,
        )
        if (next == initialWindow && !moved) return null

        moved = true
        lastPosition = next
        return next
    }

    fun releaseEvent(): PositionChangedEvent? {
        val event = if (moved) {
            lastPosition?.let { PositionChangedEvent(ProtocolVersion, it.x, it.y) }
        } else {
            null
        }
        windowAtPress = null
        pointerAtPress = null
        lastPosition = null
        moved = false
        return event
    }
}

@OptIn(ExperimentalComposeUiApi::class)
@Composable
private fun WindowDraggableArea(
    windowState: androidx.compose.ui.window.WindowState,
    awtWindow: java.awt.Window,
    onPositionChanged: (PositionChangedEvent) -> Unit,
    content: @Composable () -> Unit,
) {
    val density = LocalDensity.current
    val tracker = remember { OverlayDragTracker() }
    Box(
        androidx.compose.ui.Modifier
            .onPointerEvent(PointerEventType.Press) { event ->
                val pointer = event.changes.firstOrNull()?.position
                if (event.buttons.isPrimaryPressed && pointer != null) {
                    val location = awtWindow.locationOnScreen
                    with(density) {
                        tracker.begin(
                            windowAtPress = OverlayPosition(location.x.toDp().value.toDouble(), location.y.toDp().value.toDouble()),
                            pointerAtPress = OverlayPosition(pointer.x.toDp().value.toDouble(), pointer.y.toDp().value.toDouble()),
                        )
                    }
                }
            }
            .onPointerEvent(PointerEventType.Move) { event ->
                val point = event.changes.firstOrNull()?.position
                if (point != null && event.buttons.isPrimaryPressed) {
                    with(density) {
                        tracker.move(
                            OverlayPosition(point.x.toDp().value.toDouble(), point.y.toDp().value.toDouble()),
                        )?.let { position ->
                            windowState.position = WindowPosition.Absolute(position.x.toFloat().dp, position.y.toFloat().dp)
                        }
                    }
                }
            }
            .onPointerEvent(PointerEventType.Release) {
                tracker.releaseEvent()?.let(onPositionChanged)
            },
    ) {
        content()
    }
}
