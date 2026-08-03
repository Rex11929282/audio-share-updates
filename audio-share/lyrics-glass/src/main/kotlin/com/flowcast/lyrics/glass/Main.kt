package com.flowcast.lyrics.glass

import androidx.compose.ui.window.application

data class ApplicationMetadata(val packageName: String)

fun applicationMetadata() = ApplicationMetadata("FlowCast Lyrics Glass")

fun main() = application {
    // Task 3 installs the real transparent window.
}
