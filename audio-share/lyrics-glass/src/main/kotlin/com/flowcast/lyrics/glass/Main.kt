package com.flowcast.lyrics.glass

import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.window.application
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlin.system.exitProcess

data class ApplicationMetadata(val packageName: String)

fun applicationMetadata() = ApplicationMetadata("FlowCast Lyrics Glass")

fun main(arguments: Array<String>) {
    val argumentsForPipe = try {
        parsePipeArguments(arguments)
    } catch (exception: IllegalArgumentException) {
        System.err.println(exception.message)
        exitProcess(2)
    }
    val streams = connectPipe(argumentsForPipe.name)
    if (streams == null) {
        System.err.println("Unable to connect to FlowCast Lyrics pipe")
        exitProcess(1)
    }

    streams.use {
        val pipe = PipeClient(it.input, it.output, argumentsForPipe.token)
        val initialize = try {
            pipe.handshake()
        } catch (exception: Exception) {
            System.err.println(exception.message ?: "Unable to initialize FlowCast Lyrics")
            exitProcess(1)
        }
        if (initialize == null) {
            pipe.markIntentionalStop()
            return
        }

        application {
            var rendererState by remember { mutableStateOf(RendererState.initial().reduce(initialize)) }
            OverlayWindow(rendererState, onEvent = { event ->
                if (event is CloseRequestEvent) pipe.markIntentionalStop()
                pipe.send(event)
            })

            LaunchedEffect(Unit) {
                try {
                    while (true) {
                        when (val command = withContext(Dispatchers.IO) { pipe.readCommand() }) {
                            null -> {
                                pipe.markIntentionalStop()
                                break
                            }

                            is ShutdownCommand -> {
                                pipe.markIntentionalStop()
                                break
                            }

                            is ConnectionStateCommand -> rendererState = rendererState.reduce(command)

                            is InitializeCommand -> throw ProtocolException("Initialize may only be sent once.")
                        }
                    }
                } catch (exception: Exception) {
                    withContext(Dispatchers.IO) {
                        pipe.sendFault(exception.message ?: "FlowCast Lyrics renderer fault")
                    }
                } finally {
                    exitApplication()
                }
            }
        }
    }
}
