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

val msalClientId = setting("wolfmark.msalClientId")
val msalSignatureHash = setting("wolfmark.msalSignatureHash")

android {
    namespace = "io.github.vlko.wolfmark"
    compileSdk = 37

    defaultConfig {
        applicationId = "io.github.vlko.wolfmark"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0"
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

    buildTypes {
        release {
            isMinifyEnabled = false
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
