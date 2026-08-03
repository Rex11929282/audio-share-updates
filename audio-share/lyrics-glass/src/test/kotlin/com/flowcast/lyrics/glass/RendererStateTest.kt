package com.flowcast.lyrics.glass

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class RendererStateTest {
    @Test
    fun initialStateUsesFindingFlowcastWithNoLyricOrPosition() {
        val state = RendererState.initial()

        assertEquals(RendererConnectionState.FindingFlowcast, state.connectionState)
        assertNull(state.lyric)
        assertNull(state.position)
        assertEquals(GlassSettings(), state.glass)
    }

    @Test
    fun connectedAwaitingLyricsKeepsLyricNull() {
        val state = RendererState.initial().reduce(
            ConnectionStateCommand(ProtocolVersion, RendererConnectionState.ConnectedAwaitingLyrics),
        )

        assertEquals(RendererConnectionState.ConnectedAwaitingLyrics, state.connectionState)
        assertNull(state.lyric)
    }

    @Test
    fun connectionStateReductionClearsAnExistingReservedLyric() {
        val state = RendererState.initial().copy(lyric = LyricLine("reserved"))

        val reduced = state.reduce(ConnectionStateCommand(ProtocolVersion, RendererConnectionState.FindingFlowcast))

        assertNull(reduced.lyric)
    }

    @Test
    fun initializeAppliesPositionAndGlassWithoutCreatingLyric() {
        val position = OverlayPosition(20.0, 30.0)
        val glass = GlassSettings(blurRadiusDp = 5f)

        val state = RendererState.initial().reduce(
            InitializeCommand(ProtocolVersion, "token", position, glass),
        )

        assertEquals(position, state.position)
        assertEquals(glass, state.glass)
        assertNull(state.lyric)
    }

    @Test
    fun shutdownLeavesStateUnchanged() {
        val state = RendererState.initial()

        assertEquals(state, state.reduce(ShutdownCommand(ProtocolVersion)))
    }
}
