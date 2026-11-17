package io.github.vlko.wolfmark

import android.app.Activity
import android.content.Context
import android.net.Uri
import com.microsoft.identity.client.AcquireTokenSilentParameters
import com.microsoft.identity.client.AuthenticationCallback
import com.microsoft.identity.client.IAccount
import com.microsoft.identity.client.IAuthenticationResult
import com.microsoft.identity.client.ISingleAccountPublicClientApplication
import com.microsoft.identity.client.PublicClientApplication
import com.microsoft.identity.client.SignInParameters
import com.microsoft.identity.client.exception.MsalException
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.IOException

/**
 * Sign-in with a personal Microsoft account through MSAL (one account). Everything except [signIn] blocks and
 * belongs on a worker thread.
 */
class Auth(private val context: Context) {
    private var client: ISingleAccountPublicClientApplication? = null

    val isConfigured: Boolean get() = BuildConfig.MSAL_CLIENT_ID.isNotEmpty() && BuildConfig.MSAL_SIGNATURE_HASH.isNotEmpty()

    @Synchronized
    private fun client(): ISingleAccountPublicClientApplication {
        client?.let { return it }
        if (!isConfigured) throw IOException("No Microsoft app registration in this build.")
        // MSAL reads its settings from a file; ours come from the build (see app/build.gradle.kts).
        val config = JSONObject()
            .put("client_id", BuildConfig.MSAL_CLIENT_ID)
            .put("redirect_uri", "msauth://${context.packageName}/${Uri.encode(BuildConfig.MSAL_SIGNATURE_HASH)}")
            .put("account_mode", "SINGLE")
            .put("broker_redirect_uri_registered", false)
            .put("authorization_user_agent", "DEFAULT")
            .put(
                "authorities",
                JSONArray().put(
                    JSONObject().put("type", "AAD").put(
                        "audience",
                        JSONObject().put("type", "PersonalMicrosoftAccount").put("tenant_id", "consumers"),
                    ),
                ),
            )
        val file = File(context.noBackupFilesDir, "msal_config.json").apply { writeText(config.toString()) }
        try {
            return PublicClientApplication.createSingleAccountPublicClientApplication(context, file).also { client = it }
        } catch (e: MsalException) {
            throw IOException(e.message, e)
        } catch (e: InterruptedException) {
            throw IOException(e)
        }
    }

    private fun account(): IAccount? =
        try {
            client().currentAccount.currentAccount
        } catch (e: MsalException) {
            throw IOException(e.message, e)
        } catch (e: InterruptedException) {
            throw IOException(e)
        }

    fun isSignedIn(): Boolean = isConfigured && runCatching { account() != null }.getOrDefault(false)

    fun accountName(): String? = runCatching { account()?.username }.getOrNull()

    /** A valid access token for OneDrive, refreshed silently when needed. */
    fun token(): String {
        val account = account() ?: throw IOException("Not signed in.")
        val parameters = AcquireTokenSilentParameters.Builder()
            .forAccount(account)
            .fromAuthority(account.authority)
            .withScopes(SCOPES)
            .build()
        try {
            return client().acquireTokenSilent(parameters).accessToken
        } catch (e: MsalException) {
            throw IOException(e.message, e)
        } catch (e: InterruptedException) {
            throw IOException(e)
        }
    }

    /** Opens the Microsoft sign-in page. [done] is called on the main thread with null on success, else the reason. */
    fun signIn(activity: Activity, done: (error: String?) -> Unit) {
        val callback = object : AuthenticationCallback {
            override fun onSuccess(result: IAuthenticationResult) = done(null)
            override fun onError(exception: MsalException) = done(exception.message ?: exception.errorCode)
            override fun onCancel() = done("")
        }
        Thread {
            try {
                val app = client()
                val parameters = SignInParameters.builder()
                    .withActivity(activity)
                    .withScopes(SCOPES)
                    .withCallback(callback)
                    .build()
                activity.runOnUiThread { app.signIn(parameters) }
            } catch (e: Exception) {
                activity.runOnUiThread { done(e.message ?: e.javaClass.simpleName) }
            }
        }.start()
    }

    fun signOut() {
        try {
            client().signOut()
        } catch (_: Exception) {
        }
    }

    private companion object {
        val SCOPES = listOf("Files.ReadWrite")
    }
}
