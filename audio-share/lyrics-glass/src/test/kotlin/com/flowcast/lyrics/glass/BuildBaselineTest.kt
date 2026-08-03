package com.flowcast.lyrics.glass

import kotlin.test.Test
import kotlin.test.assertEquals

class BuildBaselineTest {
    @Test
    fun applicationMetadata_usesPrivateHelperName() {
        assertEquals("FlowCast Lyrics Glass", applicationMetadata().packageName)
    }
}
