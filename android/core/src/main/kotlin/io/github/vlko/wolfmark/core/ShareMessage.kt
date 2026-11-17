package io.github.vlko.wolfmark.core

import java.io.ByteArrayOutputStream
import java.io.DataOutputStream
import java.nio.ByteBuffer
import java.time.LocalDateTime
import java.time.ZoneOffset
import java.util.UUID

class ShareAttachment(val extension: String, val data: ByteArray)

/**
 * What the phone sends to the computer (the desktop's `ShareMessage`): a finished Markdown note and its images.
 * The text refers to image `n` as `wolfmark-attachment:n`.
 *
 * `version (1) | id (16) | sent, Unix ms (8) | created (8) | text length (4) | UTF-8 text | image count (1) |
 * { extension length (1) | ASCII extension | length (4) | bytes }*`. "Created" is the phone's wall clock written as
 * if it were UTC: the note gets the day and time the user saw.
 */
class ShareMessage(
    val id: UUID,
    val sentUnixMs: Long,
    val createdLocal: LocalDateTime,
    val markdown: String,
    val attachments: List<ShareAttachment>,
) {
    fun encode(): ByteArray {
        require(attachments.size <= MAX_ATTACHMENTS)
        val text = markdown.toByteArray(Charsets.UTF_8)
        val bytes = ByteArrayOutputStream(64 + text.size + attachments.sumOf { it.data.size + 16 })
        DataOutputStream(bytes).run {
            writeByte(VERSION)
            writeLong(id.mostSignificantBits)
            writeLong(id.leastSignificantBits)
            writeLong(sentUnixMs)
            writeLong(createdLocal.toInstant(ZoneOffset.UTC).toEpochMilli())
            writeInt(text.size)
            write(text)
            writeByte(attachments.size)
            for (a in attachments) {
                val extension = a.extension.toByteArray(Charsets.US_ASCII)
                writeByte(extension.size)
                write(extension)
                writeInt(a.data.size)
                write(a.data)
            }
        }
        return bytes.toByteArray()
    }

    companion object {
        const val VERSION = 1
        const val MAX_ATTACHMENTS = 20
        const val ATTACHMENT_PREFIX = "wolfmark-attachment:"

        /** The image types the desktop stores; anything else is not sent as an image. */
        val IMAGE_EXTENSIONS = listOf("png", "jpg", "jpeg", "gif", "bmp", "webp")
    }
}

enum class ShareStatus { CREATED, DUPLICATE, REFUSED, FAILED }

/** The computer's answer: `version (1) | status (1) | path length (2) | UTF-8 path` of the note. */
data class ShareResponse(val status: ShareStatus, val notePath: String) {
    companion object {
        fun decode(data: ByteArray): ShareResponse? {
            if (data.size < 4 || data[0].toInt() != ShareMessage.VERSION) return null
            val status = ShareStatus.entries.getOrNull(data[1].toInt()) ?: return null
            if (ByteBuffer.wrap(data, 2, 2).short.toInt() and 0xffff != data.size - 4) return null
            return ShareResponse(status, String(data, 4, data.size - 4, Charsets.UTF_8))
        }
    }
}
