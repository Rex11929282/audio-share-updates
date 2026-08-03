package com.flowcast.lyrics.glass

import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material.Checkbox
import androidx.compose.material.Slider
import androidx.compose.material.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.input.pointer.PointerEventType
import androidx.compose.ui.input.pointer.isSecondaryPressed
import androidx.compose.ui.input.pointer.onPointerEvent
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Popup
import androidx.compose.ui.window.PopupProperties
import com.kyant.backdrop.backdrops.layerBackdrop
import com.kyant.backdrop.backdrops.rememberLayerBackdrop
import com.kyant.backdrop.drawBackdrop
import com.kyant.backdrop.effects.blur
import com.kyant.backdrop.effects.lens
import com.kyant.backdrop.effects.vibrancy
import com.kyant.backdrop.highlight.Highlight

data class OverlayDimensions(val width: Int, val height: Int)

data class OverlayPresentation(val dimensions: OverlayDimensions, val text: String)

fun overlayPresentation(state: RendererState): OverlayPresentation = when (val lyric = state.lyric) {
    null -> when (state.connectionState) {
        RendererConnectionState.FindingFlowcast -> OverlayPresentation(
            OverlayDimensions(190, 48),
            "姝ｅ湪灏嬫壘 FlowCast",
        )

        RendererConnectionState.ConnectedAwaitingLyrics -> OverlayPresentation(
            OverlayDimensions(240, 64),
            "宸查€ｇ窔锛岀瓑寰呮瓕瑭瀈.",
        )
    }

    else -> OverlayPresentation(OverlayDimensions(420, 84), lyric.text)
}

fun settingsCommittedEvent(settings: GlassSettings) = SettingsCommittedEvent(ProtocolVersion, settings)

@OptIn(ExperimentalComposeUiApi::class)
@Composable
fun GlassOverlay(
    state: RendererState,
    onEvent: (RendererEvent) -> Unit,
) {
    val presentation = overlayPresentation(state)
    var menuVisible by remember { mutableStateOf(false) }
    var sheetVisible by remember { mutableStateOf(false) }
    var committedSettings by remember(state.glass) { mutableStateOf(state.glass) }
    var previewSettings by remember(state.glass) { mutableStateOf(state.glass) }
    val backdrop = rememberLayerBackdrop()
    val shape = if (previewSettings.cornerRadiusFraction == 1f) {
        CircleShape
    } else {
        RoundedCornerShape((presentation.dimensions.height / 2f * previewSettings.cornerRadiusFraction).dp)
    }

    Box(Modifier.fillMaxSize().layerBackdrop(backdrop)) {
        Box(
            Modifier
                .fillMaxSize()
                .drawBackdrop(
                    backdrop = backdrop,
                    shape = { shape },
                    effects = {
                        vibrancy()
                        blur(previewSettings.blurRadiusDp)
                        lens(
                            refractionHeight = presentation.dimensions.height * previewSettings.refractionHeightFraction,
                            refractionAmount = presentation.dimensions.height * previewSettings.refractionAmountFraction,
                            chromaticAberration = previewSettings.chromaticAberration,
                        )
                    },
                    highlight = { Highlight.Default },
                )
                .onPointerEvent(PointerEventType.Press) { event ->
                    if (event.buttons.isSecondaryPressed) menuVisible = true
                },
            contentAlignment = Alignment.Center,
        ) {
            Text(presentation.text, color = Color(0xFF102A43))
        }

        if (menuVisible) {
            Popup(
                alignment = Alignment.TopEnd,
                onDismissRequest = { menuVisible = false },
                properties = PopupProperties(focusable = true),
            ) {
                Column(Modifier.background(Color(0xFFF5F7FA)).padding(8.dp)) {
                    Text("Adjust Glass", Modifier.clickable {
                        menuVisible = false
                        previewSettings = committedSettings
                        sheetVisible = true
                    }.padding(8.dp))
                    Text("Close FlowCast Lyrics", Modifier.clickable {
                        menuVisible = false
                        onEvent(CloseRequestEvent(ProtocolVersion))
                    }.padding(8.dp))
                }
            }
        }

        if (sheetVisible) {
            Popup(
                alignment = Alignment.TopEnd,
                onDismissRequest = {
                    previewSettings = committedSettings
                    sheetVisible = false
                },
                properties = PopupProperties(focusable = true),
            ) {
                GlassOptionSheet(
                    settings = previewSettings,
                    onChange = { previewSettings = it },
                    onReset = { previewSettings = GlassSettings() },
                    onCancel = {
                        previewSettings = committedSettings
                        sheetVisible = false
                    },
                    onDone = {
                        committedSettings = previewSettings
                        onEvent(settingsCommittedEvent(previewSettings))
                        sheetVisible = false
                    },
                )
            }
        }
    }
}

@Composable
private fun GlassOptionSheet(
    settings: GlassSettings,
    onChange: (GlassSettings) -> Unit,
    onReset: () -> Unit,
    onCancel: () -> Unit,
    onDone: () -> Unit,
) {
    Column(Modifier.background(Color(0xFFF5F7FA)).padding(12.dp).width(240.dp)) {
        GlassSlider("Corner radius", settings.cornerRadiusFraction, 0f..1f) {
            onChange(settings.copy(cornerRadiusFraction = it))
        }
        GlassSlider("Blur radius", settings.blurRadiusDp, 0f..32f) {
            onChange(settings.copy(blurRadiusDp = it))
        }
        GlassSlider("Refraction height", settings.refractionHeightFraction, 0f..1f) {
            onChange(settings.copy(refractionHeightFraction = it))
        }
        GlassSlider("Refraction amount", settings.refractionAmountFraction, 0f..1f) {
            onChange(settings.copy(refractionAmountFraction = it))
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Checkbox(settings.chromaticAberration, onCheckedChange = {
                onChange(settings.copy(chromaticAberration = it))
            })
            Text("Chromatic aberration")
        }
        Row {
            Text("Reset", Modifier.clickable(onClick = onReset).padding(8.dp))
            Spacer(Modifier.weight(1f))
            Text("Cancel", Modifier.clickable(onClick = onCancel).padding(8.dp))
            Text("Done", Modifier.clickable(onClick = onDone).padding(8.dp))
        }
    }
}

@Composable
private fun GlassSlider(label: String, value: Float, range: ClosedFloatingPointRange<Float>, onChange: (Float) -> Unit) {
    Text(label)
    Slider(value = value, onValueChange = onChange, valueRange = range)
}
