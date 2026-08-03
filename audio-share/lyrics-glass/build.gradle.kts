import org.jetbrains.compose.desktop.application.dsl.TargetFormat

plugins {
    kotlin("jvm") version "2.3.21"
    id("org.jetbrains.kotlin.plugin.compose") version "2.3.21"
    kotlin("plugin.serialization") version "2.3.21"
    id("org.jetbrains.compose") version "1.11.0"
}

dependencies {
    implementation(compose.desktop.currentOs)
    implementation("io.github.kyant0:backdrop:2.0.0")
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.9.0")
    testImplementation(kotlin("test"))
}

kotlin {
    jvmToolchain(21)
}

tasks.test {
    useJUnitPlatform()
}

compose.desktop {
    application {
        mainClass = "com.flowcast.lyrics.glass.MainKt"
        nativeDistributions {
            targetFormats(TargetFormat.AppImage)
            packageName = "FlowCast Lyrics Glass"
            packageVersion = "1.1.0"
        }
    }
}
