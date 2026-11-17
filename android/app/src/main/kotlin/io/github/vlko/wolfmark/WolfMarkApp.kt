package io.github.vlko.wolfmark

import android.app.Application
import android.content.Context
import androidx.core.content.edit
import io.github.vlko.wolfmark.core.GraphClient
import io.github.vlko.wolfmark.core.NoteMirror
import io.github.vlko.wolfmark.core.ShareDelivery
import io.github.vlko.wolfmark.core.ShareDevices
import io.github.vlko.wolfmark.core.ShareQueue
import java.io.File

/** The app's few long-lived objects, wired by hand (as on the desktop: no container). */
class WolfMarkApp : Application() {
    val auth by lazy { Auth(this) }
    val queue by lazy { ShareQueue(File(filesDir, "queue")) }
    val devices by lazy { ShareDevices(File(filesDir, "devices.json")) }

    /** The copy of the notes is a cache: Android may clear it, the next refresh brings it back. */
    val mirror by lazy { NoteMirror(File(cacheDir, "mirror")) }

    val delivery by lazy { ShareDelivery(queue, devices::load, ::graphOrNull, { vault }) }

    private val preferences by lazy { getSharedPreferences("settings", Context.MODE_PRIVATE) }

    /** The notes folder, relative to the root of OneDrive; the desktop's default is "WolfMark". */
    var vault: String
        get() = preferences.getString("vault", DEFAULT_VAULT) ?: DEFAULT_VAULT
        set(value) = preferences.edit { putString("vault", value.trim('/')).putBoolean("vaultConfirmed", true) }

    /** False until the folder was found in OneDrive or chosen by hand. */
    val vaultConfirmed: Boolean get() = preferences.getBoolean("vaultConfirmed", false)

    var devicesRefreshedAt: Long
        get() = preferences.getLong("devicesRefreshedAt", 0)
        set(value) = preferences.edit { putLong("devicesRefreshedAt", value) }

    /** A client for the signed-in account. The token is asked for at every request; MSAL caches it. */
    fun graph(): GraphClient = GraphClient({ auth.token() })

    /** Null when nobody is signed in – then OneDrive is simply not an option right now. Blocking. */
    fun graphOrNull(): GraphClient? = if (auth.isSignedIn()) graph() else null

    companion object {
        const val DEFAULT_VAULT = "WolfMark"
    }
}

val Context.wolfMark: WolfMarkApp get() = applicationContext as WolfMarkApp
