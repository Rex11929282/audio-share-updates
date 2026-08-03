package com.flowcast.lyrics.glass

import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.DataOutputStream
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertIs

class ProtocolTest {
    @Test
    fun versionTwoHostCommand_isRejected() {
        assertFailsWith<ProtocolException> {
            decodeHostCommand("""{"type":"initialize","version":2,"token":"token","glass":{}}""")
        }
    }

    @Test
    fun frames_roundTripWithBigEndianLengthHeader() {
        val bytes = "glass".encodeToByteArray()
        val output = ByteArrayOutputStream()

        PipeFrameCodec.writeFrame(output, bytes)

        assertContentEquals(bytes, PipeFrameCodec.readFrame(ByteArrayInputStream(output.toByteArray())))
    }

    @Test
    fun frames_rejectNegativeAndOversizedHeadersBeforeAllocation() {
        fun inputWithHeader(length: Int): ByteArrayInputStream {
            val output = ByteArrayOutputStream()
            DataOutputStream(output).use { it.writeInt(length) }
            return ByteArrayInputStream(output.toByteArray())
        }

        assertFailsWith<ProtocolException> { PipeFrameCodec.readFrame(inputWithHeader(-1)) }
        assertFailsWith<ProtocolException> {
            PipeFrameCodec.readFrame(inputWithHeader(PipeFrameCodec.MaximumFrameBytes + 1))
        }
    }

    @Test
    fun commandAndEventJsonRoundTripsRetainTypeAndPayload() {
        val command = InitializeCommand(
            version = ProtocolVersion,
            token = "token",
            position = OverlayPosition(12.5, 48.0),
            glass = GlassSettings(blurRadiusDp = 8f),
        )
        val event = PositionChangedEvent(ProtocolVersion, 1.25, 2.5)

        val decodedCommand = decodeHostCommand(encodeHostCommand(command))
        val decodedEvent = decodeRendererEvent(encodeRendererEvent(event))

        assertIs<InitializeCommand>(decodedCommand)
        assertEquals(command, decodedCommand)
        assertIs<PositionChangedEvent>(decodedEvent)
        assertEquals(event, decodedEvent)
    }

    @Test
    fun defaultSettingsAndNativeRangesAreValidated() {
        assertEquals(1f, GlassSettings().cornerRadiusFraction)
        assertEquals(2f, GlassSettings().blurRadiusDp)
        assertEquals(.42f, GlassSettings().refractionHeightFraction)
        assertEquals(.62f, GlassSettings().refractionAmountFraction)
        assertEquals(true, GlassSettings().chromaticAberration)

        assertFailsWith<IllegalArgumentException> { GlassSettings(cornerRadiusFraction = -.01f) }
        assertFailsWith<IllegalArgumentException> { GlassSettings(cornerRadiusFraction = 1.01f) }
        assertFailsWith<IllegalArgumentException> { GlassSettings(blurRadiusDp = -.01f) }
        assertFailsWith<IllegalArgumentException> { GlassSettings(blurRadiusDp = 32.01f) }
        assertFailsWith<IllegalArgumentException> { GlassSettings(refractionHeightFraction = -.01f) }
        assertFailsWith<IllegalArgumentException> { GlassSettings(refractionHeightFraction = 1.01f) }
        assertFailsWith<IllegalArgumentException> { GlassSettings(refractionAmountFraction = -.01f) }
        assertFailsWith<IllegalArgumentException> { GlassSettings(refractionAmountFraction = 1.01f) }
    }
}
