package com.flowcast.lyrics.glass

import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.InputStream
import java.io.IOException
import java.io.OutputStream
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json

const val ProtocolVersion = 1

class ProtocolException(message: String, cause: Throwable? = null) : IllegalArgumentException(message, cause)

@Serializable
data class GlassSettings(
    val cornerRadiusFraction: Float = 1f,
    val blurRadiusDp: Float = 2f,
    val refractionHeightFraction: Float = .42f,
    val refractionAmountFraction: Float = .62f,
    val chromaticAberration: Boolean = true,
) {
    val isValid: Boolean
        get() = cornerRadiusFraction in 0f..1f &&
            blurRadiusDp in 0f..32f &&
            refractionHeightFraction in 0f..1f &&
            refractionAmountFraction in 0f..1f

    init {
        require(isValid) { "Glass settings are out of range." }
    }
}

@Serializable
data class OverlayPosition(val x: Double, val y: Double)

@Serializable
data class LyricLine(val text: String)

@Serializable
enum class RendererConnectionState {
    @SerialName("finding-flowcast")
    FindingFlowcast,

    @SerialName("connected-awaiting-lyrics")
    ConnectedAwaitingLyrics,
}

@Serializable
sealed interface HostCommand {
    val version: Int
}

@Serializable
@SerialName("initialize")
data class InitializeCommand(
    override val version: Int,
    val token: String,
    val position: OverlayPosition?,
    val glass: GlassSettings,
) : HostCommand

@Serializable
@SerialName("connection-state")
data class ConnectionStateCommand(
    override val version: Int,
    val state: RendererConnectionState,
) : HostCommand

@Serializable
@SerialName("shutdown")
data class ShutdownCommand(override val version: Int) : HostCommand

@Serializable
sealed interface RendererEvent {
    val version: Int
}

@Serializable
@SerialName("hello")
data class HelloEvent(override val version: Int, val token: String) : RendererEvent

@Serializable
@SerialName("ready")
data class ReadyEvent(override val version: Int) : RendererEvent

@Serializable
@SerialName("settings-committed")
data class SettingsCommittedEvent(
    override val version: Int,
    val glass: GlassSettings,
) : RendererEvent

@Serializable
@SerialName("position-changed")
data class PositionChangedEvent(
    override val version: Int,
    val x: Double,
    val y: Double,
) : RendererEvent

@Serializable
@SerialName("close-request")
data class CloseRequestEvent(override val version: Int) : RendererEvent

@Serializable
@SerialName("fault")
data class FaultEvent(override val version: Int, val message: String) : RendererEvent

private val protocolJson = Json {
    ignoreUnknownKeys = true
    classDiscriminator = "type"
}

fun encodeHostCommand(command: HostCommand): String = protocolJson.encodeToString(command)

fun decodeHostCommand(json: String): HostCommand = decodeProtocolInput {
    protocolJson.decodeFromString<HostCommand>(json).also(::requireCurrentVersion)
}

fun encodeRendererEvent(event: RendererEvent): String = protocolJson.encodeToString(event)

fun decodeRendererEvent(json: String): RendererEvent = decodeProtocolInput {
    protocolJson.decodeFromString<RendererEvent>(json).also(::requireCurrentVersion)
}

object PipeFrameCodec {
    const val MaximumFrameBytes = 65_536

    fun writeFrame(output: OutputStream, payload: ByteArray) = writeFrame(DataOutputStream(output), payload)

    fun writeFrame(output: DataOutputStream, payload: ByteArray) {
        requireFrameLength(payload.size)
        output.writeInt(payload.size)
        output.write(payload)
    }

    fun readFrame(input: InputStream): ByteArray = readFrame(DataInputStream(input))

    fun readFrame(input: DataInputStream): ByteArray {
        val length = try {
            input.readInt()
        } catch (exception: Exception) {
            throw ProtocolException("Unable to read frame header.", exception)
        }
        requireFrameLength(length)
        return try {
            ByteArray(length).also(input::readFully)
        } catch (exception: IOException) {
            throw ProtocolException("Unable to read frame payload.", exception)
        }
    }

    private fun requireFrameLength(length: Int) {
        if (length <= 0 || length > MaximumFrameBytes) {
            throw ProtocolException("Invalid frame length: $length")
        }
    }
}

private fun requireCurrentVersion(message: HostCommand) = requireCurrentVersion(message.version)

private fun requireCurrentVersion(message: RendererEvent) = requireCurrentVersion(message.version)

private fun requireCurrentVersion(version: Int) {
    if (version != ProtocolVersion) {
        throw ProtocolException("Unsupported protocol version: $version")
    }
}

private inline fun <T> decodeProtocolInput(decode: () -> T): T = try {
    decode()
} catch (exception: ProtocolException) {
    throw exception
} catch (exception: SerializationException) {
    throw ProtocolException("Invalid JSON protocol input.", exception)
} catch (exception: IllegalArgumentException) {
    throw ProtocolException("Invalid protocol input.", exception)
}
