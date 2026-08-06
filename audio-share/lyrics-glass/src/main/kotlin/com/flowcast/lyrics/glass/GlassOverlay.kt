package com.flowcast.lyrics.glass

import androidx.compose.foundation.ContextMenuArea
import androidx.compose.foundation.ContextMenuItem
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
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
            "正在尋找 FlowCast",
        )

        RendererConnectionState.ConnectedAwaitingLyrics -> OverlayPresentation(
            OverlayDimensions(240, 64),
            "已連線，等待歌詞",
        )
    }

    else -> OverlayPresentation(OverlayDimensions(420, 84), lyric.text)
}

@Composable
fun GlassOverlay(
    state: RendererState,
    onEvent: (RendererEvent) -> Unit,
) {
    val presentation = overlayPresentation(state)
    val backdrop = rememberLayerBackdrop()
    val shape = if (state.glass.cornerRadiusFraction == 1f) {
        CircleShape
    } else {
        RoundedCornerShape((presentation.dimensions.height / 2f * state.glass.cornerRadiusFraction).dp)
    }

    ContextMenuArea(
        items = {
            listOf(
                ContextMenuItem("調整玻璃") { onEvent(OpenOptionsEvent(ProtocolVersion)) },
                ContextMenuItem("結束 FlowCast Lyrics") { onEvent(CloseRequestEvent(ProtocolVersion)) },
            )
        },
    ) {
        Box(Modifier.fillMaxSize()) {
            Box(
                Modifier
                    .fillMaxSize()
                    .clip(shape)
                    .layerBackdrop(backdrop),
            ) {
                Box(Modifier.fillMaxSize().background(Color(0xFFF3F6FA).copy(alpha = .16f)))
                Box(
                    Modifier
                        .align(Alignment.TopStart)
                        .offset((-16).dp, (-18).dp)
                        .size(72.dp)
                        .clip(CircleShape)
                        .background(Color.White.copy(alpha = .18f)),
                )
                Box(
                    Modifier
                        .align(Alignment.BottomEnd)
                        .offset(16.dp, 18.dp)
                        .size(72.dp)
                        .clip(CircleShape)
                        .background(Color(0xFFDDE5EF).copy(alpha = .14f)),
                )
            }
            Box(
                Modifier
                    .fillMaxSize()
                    .drawBackdrop(
                        backdrop = backdrop,
                        shape = { shape },
                        effects = {
                            vibrancy()
                            blur(state.glass.blurRadiusDp)
                            lens(
                                refractionHeight = presentation.dimensions.height * state.glass.refractionHeightFraction,
                                refractionAmount = presentation.dimensions.height * state.glass.refractionAmountFraction,
                                chromaticAberration = state.glass.chromaticAberration,
                            )
                        },
                        highlight = { Highlight.Default },
                        onDrawSurface = { drawRect(Color.White.copy(alpha = .08f)) },
                    ),
                contentAlignment = Alignment.Center,
            ) {
                Text(presentation.text, color = Color(0xFF102A43))
            }
        }
    }
}
