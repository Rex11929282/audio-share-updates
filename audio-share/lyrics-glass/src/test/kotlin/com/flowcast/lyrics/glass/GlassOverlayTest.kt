package com.flowcast.lyrics.glass

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class GlassOverlayTest {
    @Test
    fun connectedState_displaysOnlyHonestWaitingCopy() {
        val presentation = overlayPresentation(
            RendererState.initial().copy(
                connectionState = RendererConnectionState.ConnectedAwaitingLyrics,
            ),
        )

        assertEquals(OverlayDimensions(240, 64), presentation.dimensions)
        assertEquals("已連線，等待歌詞", presentation.text)
    }

    @Test
    fun findingState_usesCompactCapsuleDimensions() {
        val presentation = overlayPresentation(RendererState.initial())

        assertEquals(OverlayDimensions(190, 48), presentation.dimensions)
        assertEquals("正在尋找 FlowCast", presentation.text)
    }

    @Test
    fun committingSettings_emitsValidatedNativeValues() {
        val edited = GlassSettings(blurRadiusDp = 7.5f)

        assertEquals(
            SettingsCommittedEvent(ProtocolVersion, edited),
            settingsCommittedEvent(edited),
        )
    }

    @Test
    fun futureLyric_dimensionsAndDisplayUseTextUnchanged() {
        val lyric = LyricLine("Caller-supplied lyric")
        val presentation = overlayPresentation(RendererState.initial().copy(lyric = lyric))

        assertEquals(OverlayDimensions(420, 84), presentation.dimensions)
        assertEquals(lyric.text, presentation.text)
    }

    @Test
    fun dragTracker_movesFromActualWindowPositionAndEmitsOnceOnRelease() {
        val tracker = OverlayDragTracker()
        tracker.beginAtScreenPosition(
            windowAtPress = OverlayPosition(100.0, 200.0),
            pointerAtPress = OverlayPosition(10.0, 20.0),
        )

        assertEquals(OverlayPosition(105.0, 208.0), tracker.moveAtScreenPosition(OverlayPosition(15.0, 28.0)))
        assertEquals(
            PositionChangedEvent(ProtocolVersion, 105.0, 208.0),
            tracker.releaseEvent(),
        )
        assertNull(tracker.releaseEvent())
    }

    @Test
    fun dragTracker_doesNotEmitWhenPrimaryPressNeverMoves() {
        val tracker = OverlayDragTracker()
        tracker.beginAtScreenPosition(OverlayPosition(100.0, 200.0), OverlayPosition(10.0, 20.0))

        assertNull(tracker.releaseEvent())
    }

    @Test
    fun dragTracker_allowsReturningToTheActualPressPosition() {
        val tracker = OverlayDragTracker()
        tracker.beginAtScreenPosition(OverlayPosition(100.0, 200.0), OverlayPosition(10.0, 20.0))
        tracker.moveAtScreenPosition(OverlayPosition(15.0, 28.0))

        assertEquals(OverlayPosition(100.0, 200.0), tracker.moveAtScreenPosition(OverlayPosition(10.0, 20.0)))
        assertEquals(
            PositionChangedEvent(ProtocolVersion, 100.0, 200.0),
            tracker.releaseEvent(),
        )
    }

    @Test
    fun dragTracker_advancesForEveryNewGlobalPointerPositionAndEmitsOnceOnRelease() {
        val tracker = OverlayDragTracker()
        tracker.beginAtScreenPosition(
            windowAtPress = OverlayPosition(400.0, 300.0),
            pointerAtPress = OverlayPosition(420.0, 320.0),
        )

        assertEquals(OverlayPosition(410.0, 305.0), tracker.moveAtScreenPosition(OverlayPosition(430.0, 325.0)))
        assertEquals(OverlayPosition(440.0, 330.0), tracker.moveAtScreenPosition(OverlayPosition(460.0, 350.0)))
        assertEquals(OverlayPosition(470.0, 365.0), tracker.moveAtScreenPosition(OverlayPosition(490.0, 385.0)))
        assertEquals(
            PositionChangedEvent(ProtocolVersion, 470.0, 365.0),
            tracker.releaseEvent(),
        )
        assertNull(tracker.releaseEvent())
    }
}
