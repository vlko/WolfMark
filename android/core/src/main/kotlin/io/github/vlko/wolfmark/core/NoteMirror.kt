package io.github.vlko.wolfmark.core

import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId

/** A note in the phone's copy. */
data class MirrorNote(
    val path: NotePath,
    val title: String,
    val preview: String,
    val modifiedMs: Long,
) {
    /** The note's day: from its folder, or (a note outside the day folders) when it was last changed. */
    fun day(zone: ZoneId = ZoneId.systemDefault()): LocalDate =
        path.date ?: Instant.ofEpochMilli(modifiedMs).atZone(zone).toLocalDate()

    fun time(zone: ZoneId = ZoneId.systemDefault()): LocalTime =
        path.name?.let { LocalTime.of(it.hour, it.minute, it.second) }
            ?: Instant.ofEpochMilli(modifiedMs).atZone(zone).toLocalTime()
}

/**
 * A read-only copy of the notes on the phone, kept current through OneDrive's change feed: the first run lists
 * everything, later runs only what changed. Notes can then be read without a connection. The copy is only a cache –
 * deleting [root] loses nothing.
 */
class NoteMirror(private val root: File) {
    private class Entry(val id: String, var path: String, var tag: String?, var modifiedMs: Long, var title: String, var preview: String)

    private val stateFile = File(root, "mirror.json")
    private val notesDir = File(root, "notes")
    private var vault: String? = null
    private var deltaLink: String? = null
    private val folders = HashMap<String, String>()
    private val entries = HashMap<String, Entry>()

    init {
        load()
    }

    @Synchronized
    fun notes(): List<MirrorNote> =
        entries.values.mapNotNull { e -> NotePath.parse(e.path)?.let { MirrorNote(it, e.title, e.preview, e.modifiedMs) } }
            .sortedWith(compareByDescending<MirrorNote> { it.day() }.thenByDescending { it.time() }.thenBy { it.path.relativePath })

    /** The note's text from the copy; null when it is not there. */
    fun read(note: NotePath): String? =
        resolve(note.relativePath)?.takeIf { it.isFile }?.readText()

    /**
     * Brings the copy up to date with [vaultPath] in OneDrive.
     * @param progress called with the number of notes downloaded so far
     * @return how many notes were added, changed or removed
     */
    @Synchronized
    fun sync(graph: GraphClient, vaultPath: String, progress: (Int) -> Unit = {}): Int {
        val folder = vaultPath.trim('/')
        if (!folder.equals(vault, ignoreCase = true)) reset(folder)
        return try {
            run(graph, folder, progress)
        } catch (_: DeltaExpiredException) {
            reset(folder)
            run(graph, folder, progress)
        }
    }

    private fun run(graph: GraphClient, folder: String, progress: (Int) -> Unit): Int {
        var changed = 0
        var link = deltaLink
        while (true) {
            val page = graph.delta(folder, link)
            for (item in page.items) {
                if (apply(graph, folder, item)) {
                    changed++
                    progress(changed)
                }
            }
            save()
            link = page.nextLink ?: run {
                // Without a new delta link the old one stays: the next run repeats this stretch, which is harmless.
                page.deltaLink?.let { deltaLink = it }
                save()
                return changed
            }
        }
    }

    private fun apply(graph: GraphClient, folder: String, item: DriveItem): Boolean {
        if (item.deleted) {
            folders.remove(item.id)?.let { gone ->
                var any = false
                entries.values.filter { it.path.startsWith("$gone/") }.forEach { any = remove(it) || any }
                return any
            }
            return entries[item.id]?.let(::remove) ?: false
        }

        val parent = item.parentPath ?: return false
        val relative = when {
            parent.equals(folder, ignoreCase = true) -> item.name
            parent.length > folder.length && parent.startsWith("$folder/", ignoreCase = true) ->
                parent.substring(folder.length + 1) + "/" + item.name
            else -> return false // the notes folder itself, or something outside it
        }
        if (item.isFolder) {
            folders[item.id] = relative
            return false
        }

        val known = entries[item.id]
        if (NotePath.parse(relative) == null) {
            return known?.let(::remove) ?: false // e.g. renamed to something that is not a note
        }

        val target = resolve(relative) ?: return false
        val modified = item.modified?.let { runCatching { Instant.parse(it).toEpochMilli() }.getOrNull() } ?: 0L
        if (known != null && known.tag != null && known.tag == item.contentTag) {
            if (known.path == relative && target.isFile) return false
            val old = resolve(known.path)
            if (old != null && old.isFile) {
                // Moved or renamed (a tag change on the desktop is a rename): no need to download it again.
                target.parentFile?.mkdirs()
                if (old.renameTo(target)) {
                    known.path = relative
                    known.modifiedMs = modified
                    return true
                }
            }
        }

        val bytes = graph.download("$folder/$relative") ?: return known?.let(::remove) ?: false
        target.parentFile?.mkdirs()
        val temp = File(target.path + ".tmp")
        temp.writeBytes(bytes)
        if (!temp.renameTo(target)) {
            target.delete()
            temp.renameTo(target)
        }
        if (known != null && known.path != relative) resolve(known.path)?.delete()
        val summary = NoteText.summarize(decode(bytes))
        entries[item.id] = Entry(item.id, relative, item.contentTag, modified, summary.title.ifEmpty { item.name.removeSuffix(".md") }, summary.preview)
        return true
    }

    private fun remove(entry: Entry): Boolean {
        resolve(entry.path)?.delete()
        return entries.remove(entry.id) != null
    }

    /**
     * The local file of an image a note links to (`assets/1403-01.png`), downloaded on first use; null for links
     * that are not files beside the notes (web addresses, paths leading out of the notes folder) or are missing.
     */
    fun asset(graph: GraphClient?, note: NotePath, link: String): File? {
        if (link.contains("://") || link.startsWith("/") || link.startsWith("\\") || link.contains(':')) return null
        val relative = normalize(note.relativePath.substringBeforeLast('/', "") + "/" + link.substringBefore('?').substringBefore('#')) ?: return null
        val file = resolve(relative) ?: return null
        if (file.isFile) return file
        val folder = vault ?: return null
        val bytes = runCatching { graph?.download("$folder/$relative") }.getOrNull() ?: return null
        file.parentFile?.mkdirs()
        val temp = File(file.path + ".tmp")
        temp.writeBytes(bytes)
        temp.renameTo(file)
        return file.takeIf { it.isFile }
    }

    private fun normalize(path: String): String? {
        val parts = ArrayList<String>()
        for (segment in path.replace('\\', '/').split('/')) {
            when (segment) {
                "", "." -> Unit
                ".." -> if (parts.isEmpty()) return null else parts.removeAt(parts.lastIndex)
                else -> parts += segment
            }
        }
        return parts.joinToString("/").takeIf { it.isNotEmpty() }
    }

    /** The file for a path inside the copy; null when the path would leave it. */
    private fun resolve(relative: String): File? {
        val clean = normalize(relative) ?: return null
        val file = File(notesDir, clean)
        return file.takeIf { it.canonicalPath.startsWith(notesDir.canonicalPath + File.separator) }
    }

    private fun decode(bytes: ByteArray): String {
        val bom = bytes.size >= 3 && bytes[0] == 0xEF.toByte() && bytes[1] == 0xBB.toByte() && bytes[2] == 0xBF.toByte()
        return String(bytes, if (bom) 3 else 0, bytes.size - if (bom) 3 else 0, Charsets.UTF_8)
    }

    private fun reset(folder: String) {
        notesDir.deleteRecursively()
        entries.clear()
        folders.clear()
        deltaLink = null
        vault = folder
        save()
    }

    private fun load() {
        try {
            val o = JSONObject(stateFile.readText())
            vault = o.optString("vault").ifEmpty { null }
            deltaLink = o.optString("deltaLink").ifEmpty { null }
            o.optJSONObject("folders")?.let { f -> f.keys().forEach { folders[it] = f.getString(it) } }
            val notes = o.optJSONArray("notes") ?: JSONArray()
            for (i in 0 until notes.length()) {
                val n = notes.getJSONObject(i)
                entries[n.getString("id")] = Entry(
                    n.getString("id"), n.getString("path"), n.optString("tag").ifEmpty { null },
                    n.optLong("modified"), n.optString("title"), n.optString("preview"),
                )
            }
        } catch (_: Exception) {
            // No state or a damaged one: start over, the notes are downloaded again.
            entries.clear()
            folders.clear()
            deltaLink = null
        }
    }

    private fun save() {
        val o = JSONObject().put("vault", vault ?: "").put("deltaLink", deltaLink ?: "").put("folders", JSONObject(folders as Map<*, *>))
        val notes = JSONArray()
        entries.values.forEach { e ->
            notes.put(
                JSONObject().put("id", e.id).put("path", e.path).put("tag", e.tag ?: "")
                    .put("modified", e.modifiedMs).put("title", e.title).put("preview", e.preview),
            )
        }
        o.put("notes", notes)
        root.mkdirs()
        val temp = File(stateFile.path + ".tmp")
        temp.writeText(o.toString())
        if (!temp.renameTo(stateFile)) {
            stateFile.delete()
            temp.renameTo(stateFile)
        }
    }
}
