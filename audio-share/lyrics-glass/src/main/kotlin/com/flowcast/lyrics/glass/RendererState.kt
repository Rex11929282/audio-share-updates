package com.flowcast.lyrics.glass

data class RendererState(
    val connectionState: RendererConnectionState,
    val lyric: LyricLine?,
    val position: OverlayPosition?,
    val glass: GlassSettings,
) {
    fun reduce(command: HostCommand): RendererState = when (command) {
        is InitializeCommand -> copy(position = command.position, glass = command.glass)
        is ConnectionStateCommand -> copy(connectionState = command.state, lyric = null)
        is ShutdownCommand -> this
    }

    companion object {
        fun initial() = RendererState(
            connectionState = RendererConnectionState.FindingFlowcast,
            lyric = null,
            position = null,
            glass = GlassSettings(),
        )
    }
}
