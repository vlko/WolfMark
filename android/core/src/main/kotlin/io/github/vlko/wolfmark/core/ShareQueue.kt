package io.github.vlko.wolfmark.core

import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.time.LocalDateTime
import java.util.UUID

/** A share waiting to be delivered: its text and where its images are kept. */
class PendingShare internal constructor(
    val id: UUID,
    val createdLocal: LocalDateTime,
    val markdown: String,
    val images: List<Image>,
    /** Links of the images already uploaded to OneDrive, in order – a retry continues after them. */
    val uploadedLinks: List<String>,
    /** How many times a computer took the share without a readable answer. */
    val unknownAttempts: Int,
    internal val directory: File,
) {
    class Image(val file: File, val extension: String)
}

/**
 * Shares kept on the phone until they are delivered – one folder per share, so a share survives the app being
 * killed and the phone being offline. The description (`share.json`) is written last and atomically: a folder
 * without it is a share that was never finished.
 */
class ShareQueue(private val root: File) {

    /**
     * Stores a share. [writeImage] is called for every image with the file to fill; an image that cannot be read is
     * left out (return false).
     */
    @Synchronized
    fun enqueue(
        createdLocal: LocalDateTime,
        compose: (imageCount: Int) -> String,
        imageExtensions: List<String>,
        writeImage: (index: Int, target: File) -> Boolean,
    ): PendingShare {
        val id = UUID.randomUUID()
        val dir = File(root, id.toString()).apply { mkdirs() }
        val images = ArrayList<PendingShare.Image>()
        imageExtensions.forEachIndexed { i, extension ->
            val file = File(dir, "${images.size}.$extension")
            if (runCatching { writeImage(i, file) }.getOrDefault(false) && file.length() > 0) {
                images += PendingShare.Image(file, extension)
            } else {
                file.delete()
            }
        }
        val share = PendingShare(id, createdLocal, compose(images.size), images, emptyList(), 0, dir)
        save(share)
        return share
    }

    @Synchronized
    fun pending(): List<PendingShare> =
        root.listFiles { f -> f.isDirectory }.orEmpty().mapNotNull { dir ->
            val description = File(dir, DESCRIPTION)
            if (!description.isFile) {
                // Never finished (the app died while the images were being copied) – an hour is more than enough.
                if (System.currentTimeMillis() - dir.lastModified() > 3_600_000) dir.deleteRecursively()
                return@mapNotNull null
            }
            runCatching { read(dir, JSONObject(description.readText())) }.getOrNull()
        }.sortedBy { it.createdLocal }

    @Synchronized
    fun update(share: PendingShare, uploadedLinks: List<String> = share.uploadedLinks, unknownAttempts: Int = share.unknownAttempts): PendingShare =
        PendingShare(share.id, share.createdLocal, share.markdown, share.images, uploadedLinks, unknownAttempts, share.directory)
            .also(::save)

    @Synchronized
    fun remove(share: PendingShare) {
        share.directory.deleteRecursively()
    }

    private fun save(share: PendingShare) {
        val json = JSONObject()
            .put("id", share.id.toString())
            .put("created", share.createdLocal.toString())
            .put("markdown", share.markdown)
            .put("images", JSONArray(share.images.map { JSONObject().put("file", it.file.name).put("extension", it.extension) }))
            .put("uploaded", JSONArray(share.uploadedLinks))
            .put("unknownAttempts", share.unknownAttempts)
        val temp = File(share.directory, "$DESCRIPTION.tmp")
        temp.writeText(json.toString())
        val target = File(share.directory, DESCRIPTION)
        if (!temp.renameTo(target)) {
            target.delete()
            check(temp.renameTo(target)) { "The share could not be stored." }
        }
    }

    private fun read(dir: File, o: JSONObject): PendingShare {
        val images = o.getJSONArray("images").let { a ->
            (0 until a.length()).map { a.getJSONObject(it) }.map { PendingShare.Image(File(dir, File(it.getString("file")).name), it.getString("extension")) }
        }
        val uploaded = o.optJSONArray("uploaded")?.let { a -> (0 until a.length()).map { a.getString(it) } }.orEmpty()
        return PendingShare(
            UUID.fromString(o.getString("id")),
            LocalDateTime.parse(o.getString("created")),
            o.getString("markdown"),
            images,
            uploaded,
            o.optInt("unknownAttempts"),
            dir,
        )
    }

    private companion object {
        const val DESCRIPTION = "share.json"
    }
}
