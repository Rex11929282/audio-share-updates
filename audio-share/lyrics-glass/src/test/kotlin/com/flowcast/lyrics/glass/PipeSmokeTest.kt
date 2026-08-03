package com.flowcast.lyrics.glass

import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue

class PipeSmokeTest {
    @Test
    fun inMemorySession_emitsHello_acceptsMatchingInitialize_thenEmitsReady() {
        val output = ByteArrayOutputStream()
        val client = PipeClient(
            framed(InitializeCommand(ProtocolVersion, "expected-token", OverlayPosition(1.0, 2.0), GlassSettings())),
            output,
            "expected-token",
        )

        val initialize = client.handshake()

        assertEquals("expected-token", initialize?.token)
        assertEquals(
            listOf(
                HelloEvent(ProtocolVersion, "expected-token"),
                ReadyEvent(ProtocolVersion),
            ),
            events(output),
        )
    }

    @Test
    fun inMemorySession_rejectsBadTokenWithFaultInsteadOfReady() {
        val output = ByteArrayOutputStream()
        val client = PipeClient(
            framed(InitializeCommand(ProtocolVersion, "wrong-token", null, GlassSettings())),
            output,
            "expected-token",
        )

        val failure = assertFailsWith<ProtocolException> { client.handshake() }

        assertTrue(failure.message?.contains("token", ignoreCase = true) == true)
        assertEquals(HelloEvent(ProtocolVersion, "expected-token"), events(output).first())
        assertIs<FaultEvent>(events(output).last())
        assertTrue(events(output).none { it is ReadyEvent })
    }

    @Test
    fun inMemorySession_cleanEofBeforeInitialize_closesWithoutFault() {
        val output = ByteArrayOutputStream()
        val client = PipeClient(ByteArrayInputStream(ByteArray(0)), output, "expected-token")

        assertNull(client.handshake())
        assertEquals(listOf(HelloEvent(ProtocolVersion, "expected-token")), events(output))
    }

    @Test
    fun malformedOrMissingPipeArguments_areRejectedWithClearError() {
        listOf(emptyArray(), arrayOf("--pipe", "only"), arrayOf("--token", "only", "--pipe", "name", "extra")).forEach { arguments ->
            val failure = assertFailsWith<IllegalArgumentException> { parsePipeArguments(arguments) }
            assertTrue(failure.message?.contains("--pipe <name> --token <token>") == true)
        }
    }

    private fun framed(command: HostCommand): ByteArrayInputStream {
        val output = ByteArrayOutputStream()
        PipeFrameCodec.writeFrame(output, encodeHostCommand(command).encodeToByteArray())
        return ByteArrayInputStream(output.toByteArray())
    }

    private fun events(output: ByteArrayOutputStream): List<RendererEvent> {
        val input = ByteArrayInputStream(output.toByteArray())
        return buildList {
            while (input.available() > 0) {
                add(decodeRendererEvent(PipeFrameCodec.readFrame(input).decodeToString()))
            }
        }
    }
}
