package io.github.vlko.wolfmark.core

import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URI
import java.net.URLDecoder
import java.net.URLEncoder

/** Microsoft Graph answered with an error; [status] is the HTTP status. */
class GraphException(val status: Int, message: String) : IOException(message)

/** The saved place in the change feed is too old: the local copy must be listed anew. */
class DeltaExpiredException : IOException("The delta link expired.")

/** A file or folder as OneDrive lists it. [parentPath] is relative to the drive root, without a leading slash. */
data class DriveItem(
    val id: String,
    val name: String,
    val parentPath: String?,
    val isFolder: Boolean,
    val deleted: Boolean,
    val size: Long,
    val modified: String?,
    val contentTag: String?,
)

class DeltaPage(val items: List<DriveItem>, val nextLink: String?, val deltaLink: String?)

/**
 * The few OneDrive calls the app needs, over `HttpURLConnection` (no HTTP library). Paths are relative to the root
 * of the user's OneDrive. Blocking – call from a worker thread.
 *
 * @param token returns a valid access token (`Files.ReadWrite`), or throws [IOException].
 */
class GraphClient(private val token: () -> String, private val baseUrl: String = "https://graph.microsoft.com/v1.0") {

    /**
     * Creates the file. Missing folders are created on the way.
     * @return false when a file of that name already exists – nothing was written.
     */
    fun upload(path: String, data: ByteArray, contentType: String = "application/octet-stream"): Boolean {
        val url = "${item(path)}:/content?@microsoft.graph.conflictBehavior=fail"
        var status = put(url, data, contentType)
        if (status == 404) {
            createFolders(path.substringBeforeLast('/', ""))
            status = put(url, data, contentType)
        }
        return when (status) {
            200, 201 -> true
            409, 412 -> false
            else -> throw GraphException(status, "Upload of $path failed ($status).")
        }
    }

    private fun put(url: String, data: ByteArray, contentType: String): Int {
        val c = open(url, "PUT")
        try {
            c.doOutput = true
            c.setRequestProperty("Content-Type", contentType)
            c.setFixedLengthStreamingMode(data.size)
            c.outputStream.use { it.write(data) }
            return c.responseCode.also { drain(c) }
        } finally {
            c.disconnect()
        }
    }

    fun createFolders(path: String) {
        var parent = ""
        for (segment in path.split('/').filter { it.isNotEmpty() }) {
            val url = if (parent.isEmpty()) "$baseUrl/me/drive/root/children" else "${item(parent)}:/children"
            val body = JSONObject()
                .put("name", segment)
                .put("folder", JSONObject())
                .put("@microsoft.graph.conflictBehavior", "fail")
                .toString().toByteArray()
            val c = open(url, "POST")
            try {
                c.doOutput = true
                c.setRequestProperty("Content-Type", "application/json")
                c.outputStream.use { it.write(body) }
                val status = c.responseCode
                drain(c)
                if (status != 201 && status != 200 && status != 409) {
                    throw GraphException(status, "Folder $parent/$segment could not be created ($status).")
                }
            } finally {
                c.disconnect()
            }
            parent = if (parent.isEmpty()) segment else "$parent/$segment"
        }
    }

    /** What is directly in the folder; null when there is no such folder. An empty [path] is the root. */
    fun children(path: String): List<DriveItem>? {
        val select = "\$select=id,name,parentReference,file,folder,size,lastModifiedDateTime,cTag&\$top=200"
        var url: String? = if (path.isEmpty()) "$baseUrl/me/drive/root/children?$select" else "${item(path)}:/children?$select"
        val result = ArrayList<DriveItem>()
        while (url != null) {
            val json = getJson(url) ?: return null
            json.optJSONArray("value")?.let { a -> for (i in 0 until a.length()) result += parseItem(a.getJSONObject(i)) }
            url = json.optString("@odata.nextLink").ifEmpty { null }
        }
        return result
    }

    /** The file's bytes; null when there is no such file. */
    fun download(path: String): ByteArray? {
        val c = open("${item(path)}:/content", "GET")
        try {
            // The content lives on another host behind a pre-signed address: our token must not follow there.
            c.instanceFollowRedirects = false
            return when (val status = c.responseCode) {
                200 -> c.inputStream.use { it.readBytes() }
                301, 302, 303, 307, 308 -> {
                    val location = c.getHeaderField("Location") ?: throw GraphException(status, "Redirect without a target.")
                    drain(c)
                    plainGet(location)
                }
                404 -> null.also { drain(c) }
                else -> throw GraphException(status, "Download of $path failed ($status).").also { drain(c) }
            }
        } finally {
            c.disconnect()
        }
    }

    private fun plainGet(url: String): ByteArray {
        val c = URI(url).toURL().openConnection() as HttpURLConnection
        try {
            c.connectTimeout = TIMEOUT_MS
            c.readTimeout = TIMEOUT_MS
            if (c.responseCode != 200) throw GraphException(c.responseCode, "Download failed (${c.responseCode}).")
            return c.inputStream.use { it.readBytes() }
        } finally {
            c.disconnect()
        }
    }

    /**
     * One page of changes under the folder. Start with [link] = null (everything), follow `nextLink` to the end and
     * keep `deltaLink` for the next time.
     */
    fun delta(folder: String, link: String?): DeltaPage {
        val url = link
            ?: "${item(folder)}:/delta?\$select=id,name,parentReference,file,folder,deleted,size,lastModifiedDateTime,cTag"
        val c = open(url, "GET")
        val json = try {
            when (val status = c.responseCode) {
                200 -> JSONObject(c.inputStream.use { it.readBytes() }.toString(Charsets.UTF_8))
                410 -> throw DeltaExpiredException().also { drain(c) }
                else -> throw GraphException(status, "Listing changes failed ($status).").also { drain(c) }
            }
        } finally {
            c.disconnect()
        }
        val items = ArrayList<DriveItem>()
        json.optJSONArray("value")?.let { a -> for (i in 0 until a.length()) items += parseItem(a.getJSONObject(i)) }
        return DeltaPage(
            items,
            json.optString("@odata.nextLink").ifEmpty { null },
            json.optString("@odata.deltaLink").ifEmpty { null },
        )
    }

    private fun getJson(url: String): JSONObject? {
        val c = open(url, "GET")
        try {
            return when (val status = c.responseCode) {
                200 -> JSONObject(c.inputStream.use { it.readBytes() }.toString(Charsets.UTF_8))
                404 -> null.also { drain(c) }
                else -> throw GraphException(status, "Request failed ($status).").also { drain(c) }
            }
        } finally {
            c.disconnect()
        }
    }

    private fun open(url: String, method: String): HttpURLConnection {
        val c = URI(url).toURL().openConnection() as HttpURLConnection
        c.requestMethod = method
        c.connectTimeout = TIMEOUT_MS
        c.readTimeout = TIMEOUT_MS
        c.setRequestProperty("Authorization", "Bearer ${token()}")
        c.setRequestProperty("Accept", "application/json")
        return c
    }

    private fun drain(c: HttpURLConnection) {
        runCatching { (c.errorStream ?: c.inputStream)?.use { it.readBytes() } }
    }

    private fun item(path: String): String = "$baseUrl/me/drive/root:/${encodePath(path)}"

    companion object {
        private const val TIMEOUT_MS = 30_000

        fun encodePath(path: String): String =
            path.split('/').filter { it.isNotEmpty() }.joinToString("/") { URLEncoder.encode(it, "UTF-8").replace("+", "%20") }

        internal fun parseItem(o: JSONObject): DriveItem {
            // "/drive/root:/WolfMark/2026/11/17", percent-encoded; absent on deleted items.
            val parent = o.optJSONObject("parentReference")?.optString("path")?.takeIf { it.isNotEmpty() }
                ?.substringAfter("root:", "")
                ?.let { URLDecoder.decode(it.replace("+", "%2B"), "UTF-8") }
                ?.trim('/')
            return DriveItem(
                id = o.optString("id"),
                name = o.optString("name"),
                parentPath = parent,
                isFolder = o.has("folder"),
                deleted = o.has("deleted"),
                size = o.optLong("size"),
                modified = o.optString("lastModifiedDateTime").ifEmpty { null },
                contentTag = o.optString("cTag").ifEmpty { null },
            )
        }
    }
}
