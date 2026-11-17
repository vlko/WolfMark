import java.security.KeyStore
import java.security.MessageDigest
import java.util.Base64
import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

// The Microsoft Entra app registration (see ../README.md): from local.properties, -P or the environment. Without
// it the app builds and runs, and says on the sign-in screen what is missing.
val local = Properties().apply {
    rootProject.file("local.properties").takeIf { it.isFile }?.inputStream()?.use(::load)
}
fun setting(name: String): String =
    (findProperty(name) as String?) ?: local.getProperty(name) ?: System.getenv(name.replace('.', '_').uppercase()) ?: ""

// A keystore of your own (wolfmark.keystore, .keystorePassword, .keyAlias, .keyPassword) signs the debug and the
// release build alike, so every build installs over the last one and has the same signature hash. Without it the
// debug build is signed with this machine's debug key and a release build is left unsigned.
val keystoreFile = setting("wolfmark.keystore").takeIf { it.isNotEmpty() }?.let { rootProject.file(it) }
val keystorePassword = setting("wolfmark.keystorePassword")
val signingAlias = setting("wolfmark.keyAlias")
val signingKeyPassword = setting("wolfmark.keyPassword").ifEmpty { keystorePassword }

/** What Microsoft Entra calls the signature hash: base64 of the SHA-1 of the signing certificate. */
fun signatureHash(file: File, storePassword: String, alias: String): String? = runCatching {
    val store = KeyStore.getInstance(KeyStore.getDefaultType())
    file.inputStream().use { store.load(it, storePassword.toCharArray()) }
    store.getCertificate(alias)?.let { Base64.getEncoder().encodeToString(MessageDigest.getInstance("SHA-1").digest(it.encoded)) }
}.getOrNull()

val debugKeystore = File(System.getProperty("user.home"), ".android/debug.keystore")
val msalClientId = setting("wolfmark.msalClientId")
// Given, or read from the key the build is signed with (the debug key exists after the first debug build).
val msalSignatureHash = setting("wolfmark.msalSignatureHash").ifEmpty {
    when {
        keystoreFile != null -> signatureHash(keystoreFile, keystorePassword, signingAlias)
        debugKeystore.isFile -> signatureHash(debugKeystore, "android", "androiddebugkey")
        else -> null
    }.orEmpty()
}

// 0.9.0 or 0.9.0-rc.1 (the release tag) -> versionName as given, versionCode 900.
val appVersion = setting("wolfmark.version").ifEmpty { "0.1.0" }
val appVersionCode = appVersion.substringBefore('-').substringBefore('+').split('.').map { it.toIntOrNull() ?: 0 }
    .let { (it.getOrElse(0) { 0 } * 10000) + (it.getOrElse(1) { 0 } * 100) + it.getOrElse(2) { 0 } }
    .coerceAtLeast(1)

tasks.register("printSignatureHash") {
    group = "help"
    description = "Prints the signature hash to register in Microsoft Entra for the key this build is signed with."
    val hash = msalSignatureHash
    doLast { println(if (hash.isEmpty()) "No signing key yet - build the app once, or set wolfmark.keystore." else "Signature hash: $hash") }
}

android {
    namespace = "io.github.vlko.wolfmark"
    compileSdk = 37

    defaultConfig {
        applicationId = "io.github.vlko.wolfmark"
        minSdk = 26
        targetSdk = 36
        versionCode = appVersionCode
        versionName = appVersion
        buildConfigField("String", "MSAL_CLIENT_ID", "\"$msalClientId\"")
        buildConfigField("String", "MSAL_SIGNATURE_HASH", "\"$msalSignatureHash\"")
        // The redirect MSAL's browser tab comes back to: msauth://<package>/<signature hash>.
        manifestPlaceholders["msalSignatureHash"] = msalSignatureHash.ifEmpty { "unconfigured" }
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    val ownKey = keystoreFile?.let { keystore ->
        signingConfigs.create("wolfmark") {
            storeFile = keystore
            storePassword = keystorePassword
            keyAlias = signingAlias
            keyPassword = signingKeyPassword
        }
    }

    buildTypes {
        debug {
            ownKey?.let { signingConfig = it }
        }
        release {
            isMinifyEnabled = false
            ownKey?.let { signingConfig = it }
        }
    }

    lint {
        // The app targets the API level it was written and tested against; raising it is a change of its own.
        disable += "OldTargetApi"
        abortOnError = true
        warningsAsErrors = true
    }

    packaging {
        resources.excludes += setOf("META-INF/{AL2.0,LGPL2.1}", "META-INF/DEPENDENCIES", "META-INF/NOTICE*", "META-INF/LICENSE*")
    }
}

dependencies {
    implementation(project(":core"))
    implementation(platform(libs.compose.bom))
    implementation(libs.compose.material3)
    implementation(libs.compose.ui)
    implementation(libs.compose.ui.tooling.preview)
    implementation(libs.activity.compose)
    implementation(libs.lifecycle.viewmodel.compose)
    implementation(libs.lifecycle.runtime.compose)
    implementation(libs.core.ktx)
    implementation(libs.work.runtime)
    implementation(libs.coroutines.android)
    implementation(libs.msal)
    implementation(libs.markdown)
    implementation(libs.markdown.coil)
}
