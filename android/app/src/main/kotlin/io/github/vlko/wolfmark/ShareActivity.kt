package io.github.vlko.wolfmark

import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.core.content.IntentCompat
import androidx.lifecycle.lifecycleScope
import io.github.vlko.wolfmark.core.ShareComposer
import io.github.vlko.wolfmark.core.ShareDelivery
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.time.LocalDateTime

/**
 * The share target. It only puts what was shared into the queue – copying the images while this activity still
 * may read them – shows a toast and goes away. Sending is the worker's job, so sharing never waits for a network.
 */
class ShareActivity : ComponentActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val shared = intent
        lifecycleScope.launch {
            val message = withContext(Dispatchers.IO) { runCatching { store(shared) }.getOrDefault(R.string.share_nothing) }
            Toast.makeText(applicationContext, message, Toast.LENGTH_SHORT).show()
            finish()
        }
    }

    /** @return the text of the toast */
    private fun store(shared: Intent): Int {
        val created = LocalDateTime.now()
        val subject = shared.getStringExtra(Intent.EXTRA_SUBJECT)
        var text = shared.getCharSequenceExtra(Intent.EXTRA_TEXT)?.toString()

        val streams: List<Uri> = when (shared.action) {
            Intent.ACTION_SEND -> listOfNotNull(IntentCompat.getParcelableExtra(shared, Intent.EXTRA_STREAM, Uri::class.java))
            Intent.ACTION_SEND_MULTIPLE -> IntentCompat.getParcelableArrayListExtra(shared, Intent.EXTRA_STREAM, Uri::class.java).orEmpty()
            else -> emptyList()
        }

        // A shared text file (a .txt or .md "sent" from a file manager) is text, not an attachment.
        val images = ArrayList<Uri>()
        for (uri in streams) {
            val type = contentResolver.getType(uri) ?: shared.type.orEmpty()
            if (type.startsWith("text/")) {
                val content = runCatching { contentResolver.openInputStream(uri)?.use { readText(it) } }.getOrNull()
                if (!content.isNullOrBlank()) text = listOfNotNull(text?.takeIf { it.isNotBlank() }, content).joinToString("\n\n")
            } else {
                images += uri
            }
        }

        if (text.isNullOrBlank() && images.isEmpty()) return R.string.share_nothing

        val extensions = images.map { ShareDelivery.extensionFor(contentResolver.getType(it)) ?: "jpg" }
        val app = wolfMark
        val share = app.queue.enqueue(created, { ShareComposer.compose(subject, text, it) }, extensions) { i, target ->
            copyImage(images[i], ShareDelivery.extensionFor(contentResolver.getType(images[i])) != null, target)
        }
        if (share.markdown.isBlank()) {
            app.queue.remove(share)
            return R.string.share_nothing
        }

        DeliveryWorker.schedule(applicationContext)
        return if (app.auth.isSignedIn()) R.string.share_saved else R.string.share_saved_sign_in
    }

    /**
     * Copies an image the desktop can show as it is; anything else (HEIC from the camera, AVIF) is decoded and
     * stored as JPEG.
     */
    private fun copyImage(uri: Uri, asIs: Boolean, target: File): Boolean {
        if (asIs) {
            contentResolver.openInputStream(uri)?.use { input -> target.outputStream().use(input::copyTo) } ?: return false
            return true
        }
        val bitmap = contentResolver.openInputStream(uri)?.use(BitmapFactory::decodeStream) ?: return false
        target.outputStream().use { bitmap.compress(Bitmap.CompressFormat.JPEG, 90, it) }
        bitmap.recycle()
        return true
    }

    /** The stream as UTF-8 text, at most [MAX_TEXT_BYTES] of it. */
    private fun readText(input: java.io.InputStream): String {
        val out = java.io.ByteArrayOutputStream()
        val buffer = ByteArray(64 * 1024)
        while (out.size() < MAX_TEXT_BYTES) {
            val n = input.read(buffer, 0, minOf(buffer.size, MAX_TEXT_BYTES - out.size()))
            if (n < 0) break
            out.write(buffer, 0, n)
        }
        return out.toString(Charsets.UTF_8.name()).removePrefix("\uFEFF")
    }

    private companion object {
        const val MAX_TEXT_BYTES = 4 * 1024 * 1024
    }
}
