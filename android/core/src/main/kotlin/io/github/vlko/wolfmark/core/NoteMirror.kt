package io.github.vlko.wolfmark.core

import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference

/** A note in the phone's copy. */
data class MirrorNote(
    val path: NotePath,
    val title: String,
    val preview: String,
    val modifiedMs: Long,
    val openTasks: Int = 0,
    val doneTasks: Int = 0,
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
    private class Entry(
        val id: String, var path: String, var tag: String?, var modifiedMs: Long, var title: String, var preview: String,
        /** Negative = not counted yet (a copy made by an older version): counted from the file on first use. */
        var openTasks: Int = -1, var doneTasks: Int = 0,
    )

    private val stateFile = File(root, "mirror.json")
    private val notesDir = File(root, "notes")
    private val tagsFile = File(root, "tags.json")
    private var tagsTag: String? = null
    private var tagsChecked = false
    private var vault: String? = null
    private var deltaLink: String? = null
    private val folders = HashMap<String, String>()
    private val entries = HashMap<String, Entry>()
    private val syncLock = Any()

    init {
        load()
    }

    @Synchronized
    fun notes(): List<MirrorNote> =
        entries.values.mapNotNull { e ->
            if (e.openTasks < 0) countTasks(e)
            NotePath.parse(e.path)?.let { MirrorNote(it, e.title, e.preview, e.modifiedMs, e.openTasks, e.doneTasks) }
        }
            .sortedWith(compareByDescending<MirrorNote> { it.day() }.thenByDescending { it.time() }.thenBy { it.path.relativePath })

    private fun countTasks(entry: Entry) {
        val text = runCatching { resolve(entry.path)?.takeIf { it.isFile }?.readBytes()?.let(::decode) }.getOrNull()
        val summary = text?.let(NoteText::summarize)
        entry.openTasks = summary?.openTasks ?: 0
        entry.doneTasks = summary?.doneTasks ?: 0
    }

    /** The text of the notes' `.wolfmark/tags.json` (tag colours chosen on the desktop); null when there is none. */
    fun tagsJson(): String? = runCatching { tagsFile.takeIf { it.isFile }?.readText() }.getOrNull()

    /** The note's text from the copy; null when it is not there. */
    fun read(note: NotePath): String? =
        resolve(note.relativePath)?.takeIf { it.isFile }?.readText()

    /**
     * Brings the copy up to date with [vaultPath] in OneDrive. Notes are downloaded a few at a time, and [notes]
     * can be read meanwhile – it shows what has arrived so far.
     * @param progress called with the number of notes added, changed or removed so far
     * @return how many notes were added, changed or removed
     */
    fun sync(graph: GraphClient, vaultPath: String, progress: (Int) -> Unit = {}): Int = synchronized(syncLock) {
        val folder = vaultPath.trim('/')
        synchronized(this) { if (!folder.equals(vault, ignoreCase = true)) reset(folder) }
        if (!tagsChecked) {
            // A copy that is already up to date never hears of tags.json through the change feed: ask once.
            val bytes = runCatching { graph.download("$folder/$TAGS") }.getOrNull()
            synchronized(this) {
                if (bytes != null) writeAtomically(tagsFile, bytes)
                tagsChecked = true
                save()
            }
        }
        try {
            run(graph, folder, progress)
        } catch (_: DeltaExpiredException) {
            synchronized(this) { reset(folder) }
            run(graph, folder, progress)
        }
    }

    private fun run(graph: GraphClient, folder: String, progress: (Int) -> Unit): Int {
        var changed = 0
        var link = synchronized(this) { deltaLink }
        val pool = Executors.newFixedThreadPool(DOWNLOADS)
        try {
            while (true) {
                val page = graph.delta(folder, link)
                // What has to be fetched is decided first, fetched side by side, and only then written down –
                // so the list stays readable while the network is busy.
                val wanted = synchronized(this) { page.items.mapNotNull { item -> downloadPath(folder, item)?.let { item.id to it } } }
                val fetched = HashMap<String, ByteArray?>()
                val failure = AtomicReference<Exception?>()
                val arrived = AtomicInteger()
                val before = changed
                wanted.map { (id, relative) ->
                    pool.submit {
                        if (failure.get() != null) return@submit
                        try {
                            val bytes = graph.download("$folder/$relative")
                            synchronized(fetched) { fetched[id] = bytes }
                            progress(before + arrived.incrementAndGet())
                        } catch (e: Exception) {
                            failure.compareAndSet(null, e)
                        }
                    }
                }.forEach { it.get() }

                synchronized(this) {
                    for (item in page.items) {
                        // After a failure the notes that did arrive are still kept; the rest come next time.
                        if (failure.get() != null && item.id !in fetched && downloadPath(folder, item) != null) continue
                        if (apply(folder, item) { fetched[item.id] }) changed++
                    }
                    save()
                }
                failure.get()?.let { throw it }
                progress(changed)

                link = page.nextLink ?: run {
                    synchronized(this) {
                        // Without a new delta link the old one stays: the next run repeats this stretch, which is harmless.
                        page.deltaLink?.let { deltaLink = it }
                        save()
                    }
                    return changed
                }
            }
        } finally {
            pool.shutdownNow()
        }
    }

    /** Where the item sits under the notes folder; null for the folder itself and for what is outside it. */
    private fun relativePath(folder: String, item: DriveItem): String? {
        val parent = item.parentPath ?: return null
        return when {
            parent.equals(folder, ignoreCase = true) -> item.name
            parent.length > folder.length && parent.startsWith("$folder/", ignoreCase = true) ->
                parent.substring(folder.length + 1) + "/" + item.name
            else -> null
        }
    }

    /** The path to download for this change, or null when nothing has to be fetched for it. */
    private fun downloadPath(folder: String, item: DriveItem): String? {
        if (item.deleted || item.isFolder) return null
        val relative = relativePath(folder, item) ?: return null
        if (relative.equals(TAGS, ignoreCase = true)) return relative.takeIf { tagsTag == null || tagsTag != item.contentTag || !tagsFile.isFile }
        if (NotePath.parse(relative) == null) return null
        val target = resolve(relative) ?: return null
        val known = entries[item.id]
        if (known != null && known.tag != null && known.tag == item.contentTag) {
            // Unchanged, or only moved (a tag change on the desktop is a rename): the text is already here.
            if ((known.path == relative && target.isFile) || resolve(known.path)?.isFile == true) return null
        }
        return relative
    }

    private fun apply(folder: String, item: DriveItem, fetched: () -> ByteArray?): Boolean {
        if (item.deleted) {
            folders.remove(item.id)?.let { gone ->
                var any = false
                entries.values.filter { it.path.startsWith("$gone/") }.forEach { any = remove(it) || any }
                return any
            }
            return entries[item.id]?.let(::remove) ?: false
        }

        val relative = relativePath(folder, item) ?: return false
        if (item.isFolder) {
            folders[item.id] = relative
            return false
        }

        if (relative.equals(TAGS, ignoreCase = true)) {
            fetched()?.let { bytes ->
                writeAtomically(tagsFile, bytes)
                tagsTag = item.contentTag
            }
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
                target.parentFile?.mkdirs()
                if (old.renameTo(target)) {
                    known.path = relative
                    known.modifiedMs = modified
                    return true
                }
            }
        }

        // Listed but gone by the time it was fetched: deleted in between.
        val bytes = fetched() ?: return known?.let(::remove) ?: false
        target.parentFile?.mkdirs()
        val temp = File(target.path + ".tmp")
        temp.writeBytes(bytes)
        if (!temp.renameTo(target)) {
            target.delete()
            temp.renameTo(target)
        }
        if (known != null && known.path != relative) resolve(known.path)?.delete()
        val summary = NoteText.summarize(decode(bytes))
        entries[item.id] = Entry(
            item.id, relative, item.contentTag, modified, summary.title.ifEmpty { item.name.removeSuffix(".md") }, summary.preview,
            summary.openTasks, summary.doneTasks,
        )
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

    private companion object {
        /** Notes fetched at the same time: enough to be several times faster, few enough not to be throttled. */
        const val DOWNLOADS = 4
        const val TAGS = ".wolfmark/tags.json"
    }

    private fun writeAtomically(file: File, bytes: ByteArray) {
        file.parentFile?.mkdirs()
        val temp = File(file.path + ".tmp")
        temp.writeBytes(bytes)
        if (!temp.renameTo(file)) {
            file.delete()
            temp.renameTo(file)
        }
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
        tagsFile.delete()
        tagsTag = null
        tagsChecked = false
        vault = folder
        save()
    }

    private fun load() {
        try {
            val o = JSONObject(stateFile.readText())
            vault = o.optString("vault").ifEmpty { null }
            deltaLink = o.optString("deltaLink").ifEmpty { null }
            tagsTag = o.optString("tagsTag").ifEmpty { null }
            tagsChecked = o.optBoolean("tagsChecked")
            o.optJSONObject("folders")?.let { f -> f.keys().forEach { folders[it] = f.getString(it) } }
            val notes = o.optJSONArray("notes") ?: JSONArray()
            for (i in 0 until notes.length()) {
                val n = notes.getJSONObject(i)
                entries[n.getString("id")] = Entry(
                    n.getString("id"), n.getString("path"), n.optString("tag").ifEmpty { null },
                    n.optLong("modified"), n.optString("title"), n.optString("preview"),
                    n.optInt("open", -1), n.optInt("done"),
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
            .put("tagsTag", tagsTag ?: "").put("tagsChecked", tagsChecked)
        val notes = JSONArray()
        entries.values.forEach { e ->
            notes.put(
                JSONObject().put("id", e.id).put("path", e.path).put("tag", e.tag ?: "")
                    .put("modified", e.modifiedMs).put("title", e.title).put("preview", e.preview)
                    .put("open", e.openTasks).put("done", e.doneTasks),
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
