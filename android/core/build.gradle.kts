// Everything that is not Android: file names, the share protocol, Microsoft Graph, the offline queue and the local
// copy of the notes. Plain JVM, so it is tested without a device – against the vectors shared with the desktop
// (../testdata).
plugins {
    alias(libs.plugins.kotlin.jvm)
}

kotlin {
    jvmToolchain(21)
    compilerOptions { jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17) }
}

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

dependencies {
    // Android ships org.json; on the JVM (tests) the same API comes from the artifact.
    compileOnly(libs.json)
    testImplementation(libs.json)
    testImplementation(libs.junit)
}

tasks.test {
    // testdata/*.json in the repository root: the vectors the desktop tests read too.
    systemProperty("wolfmark.testdata", rootProject.projectDir.resolve("../testdata").canonicalPath)
}
