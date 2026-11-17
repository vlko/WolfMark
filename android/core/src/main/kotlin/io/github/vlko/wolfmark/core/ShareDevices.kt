package io.github.vlko.wolfmark.core

import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject
import java.io.File
import java.util.Base64

/**
 * The computers that receive shares: read from `.wolfmark/share` in the notes folder (through OneDrive) and
 * remembered in a file, so a share on the home network finds them without the internet.
 */
class ShareDevices(private val cache: File) {
    @Synchronized
    fun load(): List<ShareDevice> =
        try {
            val array = JSONArray(cache.readText())
            (0 until array.length()).mapNotNull { parse(array.getJSONObject(it)) }
        } catch (_: Exception) {
            emptyList()
        }

    /** Reads the announcements from OneDrive and remembers them; a folder that is not there means no computers. */
    fun refresh(graph: GraphClient, vault: String): List<ShareDevice> {
        val folder = "$vault/$DIRECTORY"
        val devices = graph.children(folder).orEmpty()
            .filter { !it.isFolder && it.name.endsWith(".json", ignoreCase = true) }
            .mapNotNull { graph.download("$folder/${it.name}") }
            .mapNotNull { parse(it.toString(Charsets.UTF_8)) }
        save(devices)
        return devices
    }

    @Synchronized
    private fun save(devices: List<ShareDevice>) {
        val array = JSONArray()
        devices.forEach { d ->
            array.put(
                JSONObject().put("version", 1).put("id", d.id).put("name", d.name).put("port", d.port)
                    .put("addresses", JSONArray(d.addresses)).put("key", Base64.getEncoder().encodeToString(d.key)),
            )
        }
        cache.parentFile?.mkdirs()
        val temp = File(cache.path + ".tmp")
        temp.writeText(array.toString())
        if (!temp.renameTo(cache)) {
            cache.delete()
            temp.renameTo(cache)
        }
    }

    companion object {
        const val DIRECTORY = ".wolfmark/share"

        fun parse(json: String): ShareDevice? =
            try {
                parse(JSONObject(json))
            } catch (_: JSONException) {
                null
            }

        /** Null for a file of another version or with anything missing – such a computer is simply not tried. */
        private fun parse(o: JSONObject): ShareDevice? {
            if (o.optInt("version") != 1) return null
            val key = runCatching { Base64.getDecoder().decode(o.optString("key")) }.getOrNull() ?: return null
            val port = o.optInt("port")
            val addresses = o.optJSONArray("addresses")?.let { a -> (0 until a.length()).map { a.optString(it) } }.orEmpty()
                .filter { it.matches(Regex("""\d{1,3}(\.\d{1,3}){3}""")) }
            val id = o.optString("id")
            if (key.size != ShareFrame.KEY_LENGTH || port !in 1..65535 || id.isEmpty()) return null
            return ShareDevice(id, o.optString("name").ifEmpty { id }, port, addresses, key)
        }
    }
}
