package com.flowcast.lyrics.glass

import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.io.OutputStream
import java.lang.reflect.InvocationTargetException
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertIs
import kotlin.test.assertTrue

class PipeSmokeTest {
    @Test
    fun inMemorySession_emitsHello_acceptsMatchingInitialize_thenEmitsReady() {
        val input = framed(
            InitializeCommand(ProtocolVersion, "expected-token", OverlayPosition(1.0, 2.0), GlassSettings()),
        )
        val output = ByteArrayOutputStream()
        val session = newSession(input, output, "expected-token")

        val initialize = session.javaClass.getMethod("handshake").invoke(session) as InitializeCommand

        assertEquals("expected-token", initialize.token)
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
        val session = newSession(
            framed(InitializeCommand(ProtocolVersion, "wrong-token", null, GlassSettings())),
            output,
            "expected-token",
        )

        val failure = assertFailsWith<InvocationTargetException> {
            session.javaClass.getMethod("handshake").invoke(session)
        }

        assertTrue(failure.cause?.message?.contains("token", ignoreCase = true) == true)
        assertEquals(HelloEvent(ProtocolVersion, "expected-token"), events(output).first())
        assertIs<FaultEvent>(events(output).last())
        assertTrue(events(output).none { it is ReadyEvent })
    }

    @Test
    fun malformedOrMissingPipeArguments_areRejectedWithClearError() {
        val parse = staticMethod("PipeClientKt", "parsePipeArguments", Array<String>::class.java)

        listOf(emptyArray(), arrayOf("--pipe", "only"), arrayOf("--token", "only", "--pipe", "name", "extra")).forEach { arguments ->
            val failure = assertFailsWith<InvocationTargetException> { parse.invoke(null, arguments) }
            assertTrue(failure.cause?.message?.contains("--pipe <name> --token <token>") == true)
        }
    }

    private fun newSession(input: InputStream, output: OutputStream, token: String): Any =
        expectedClass("PipeClient").getConstructor(InputStream::class.java, OutputStream::class.java, String::class.java)
            .newInstance(input, output, token)

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

    private fun staticMethod(fileClass: String, name: String, vararg parameterTypes: Class<*>): java.lang.reflect.Method =
        expectedClass(fileClass).getMethod(name, *parameterTypes)

    private fun expectedClass(fileClass: String): Class<*> = try {
        Class.forName("com.flowcast.lyrics.glass.$fileClass")
    } catch (exception: ClassNotFoundException) {
        throw AssertionError("Expected Task 3 implementation class $fileClass", exception)
    }
}
