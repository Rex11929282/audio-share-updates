package com.flowcast.lyrics.glass

import java.io.EOFException
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream

data class PipeArguments(val name: String, val token: String)

fun parsePipeArguments(arguments: Array<String>): PipeArguments {
    if (arguments.size != 4 || arguments[0] != "--pipe" || arguments[2] != "--token" ||
        arguments[1].isBlank() || arguments[3].isBlank()
    ) {
        throw IllegalArgumentException("Expected --pipe <name> --token <token>")
    }
    return PipeArguments(arguments[1], arguments[3])
}

class PipeClient(
    private val input: InputStream,
    private val output: OutputStream,
    private val token: String,
) {
    private var faultSent = false
    private var intentionalStop = false

    @Synchronized
    fun send(event: RendererEvent) {
        PipeFrameCodec.writeFrame(output, encodeRendererEvent(event).encodeToByteArray())
        output.flush()
    }

    fun handshake(): InitializeCommand = try {
        send(HelloEvent(ProtocolVersion, token))
        val command = readCommand() ?: throw ProtocolException("Pipe closed before initialize.")
        val initialize = command as? InitializeCommand
            ?: throw ProtocolException("Expected initialize command.")
        if (initialize.token != token) {
            throw ProtocolException("Initialize token does not match.")
        }
        send(ReadyEvent(ProtocolVersion))
        initialize
    } catch (exception: Exception) {
        sendFault(exception.message ?: "Unable to initialize renderer.")
        throw exception
    }

    fun readCommand(): HostCommand? = try {
        decodeHostCommand(PipeFrameCodec.readFrame(input).decodeToString())
    } catch (exception: ProtocolException) {
        if (exception.cause is EOFException) null else throw exception
    }

    fun markIntentionalStop() {
        intentionalStop = true
    }

    fun sendFault(message: String) {
        if (!intentionalStop && !faultSent) {
            faultSent = true
            runCatching { send(FaultEvent(ProtocolVersion, message)) }
        }
    }
}

data class PipeStreams(val input: InputStream, val output: OutputStream) : AutoCloseable {
    override fun close() {
        runCatching { input.close() }
        runCatching { output.close() }
    }
}

fun connectPipe(name: String): PipeStreams? {
    val deadline = System.nanoTime() + 5_000_000_000L
    val path = "\\\\.\\pipe\\$name"
    while (System.nanoTime() < deadline) {
        try {
            return PipeStreams(FileInputStream(path), FileOutputStream(path))
        } catch (_: IOException) {
            Thread.sleep(100)
        }
    }
    return null
}
