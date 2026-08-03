package com.flowcast.lyrics.glass

import kotlin.test.Test
import kotlin.test.assertEquals

class GlassOverlayTest {
    @Test
    fun connectedState_displaysOnlyHonestWaitingCopy() {
        val presentation = overlayPresentation(
            RendererState.initial().copy(
                connectionState = RendererConnectionState.ConnectedAwaitingLyrics,
            ),
        )

        assertEquals("宸查€ｇ窔锛岀瓑寰呮瓕瑭瀈.", property(presentation, "text"))
    }

    @Test
    fun findingState_usesCompactCapsuleDimensions() {
        val presentation = overlayPresentation(RendererState.initial())

        assertEquals(190, property(property(presentation, "dimensions")!!, "width"))
        assertEquals(48, property(property(presentation, "dimensions")!!, "height"))
    }

    @Test
    fun committingSettings_emitsValidatedNativeValues() {
        val edited = GlassSettings(blurRadiusDp = 7.5f)

        val event = staticMethod("GlassOverlayKt", "settingsCommittedEvent", GlassSettings::class.java)
            .invoke(null, edited) as SettingsCommittedEvent

        assertEquals(ProtocolVersion, event.version)
        assertEquals(edited, event.glass)
    }

    @Test
    fun futureLyric_dimensionsAndDisplayUseTextUnchanged() {
        val lyric = LyricLine("No invented words — 그대로")
        val presentation = overlayPresentation(RendererState.initial().copy(lyric = lyric))

        assertEquals(420, property(property(presentation, "dimensions")!!, "width"))
        assertEquals(84, property(property(presentation, "dimensions")!!, "height"))
        assertEquals(lyric.text, property(presentation, "text"))
    }

    private fun overlayPresentation(state: RendererState): Any =
        staticMethod("GlassOverlayKt", "overlayPresentation", RendererState::class.java).invoke(null, state)

    private fun property(instance: Any, name: String): Any? =
        instance.javaClass.getMethod("get${name.replaceFirstChar(Char::uppercase)}").invoke(instance)

    private fun staticMethod(fileClass: String, name: String, vararg parameterTypes: Class<*>): java.lang.reflect.Method =
        expectedClass(fileClass).getMethod(name, *parameterTypes)

    private fun expectedClass(fileClass: String): Class<*> = try {
        Class.forName("com.flowcast.lyrics.glass.$fileClass")
    } catch (exception: ClassNotFoundException) {
        throw AssertionError("Expected Task 3 implementation class $fileClass", exception)
    }
}
