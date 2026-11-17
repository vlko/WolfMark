package io.github.vlko.wolfmark.core

import java.io.IOException

/**
 * Delivers a queued share (ADR 0017): first straight to a computer on the local network, otherwise as files into
 * the notes folder in OneDrive. The note is the same either way.
 */
class ShareDelivery(
    private val queue: ShareQueue,
    private val devices: () -> List<ShareDevice>,
    private val graph: () -> GraphClient?,
    private val vault: () -> String,
    private val now: () -> Long = System::currentTimeMillis,
    private val send: (address: String, device: ShareDevice, message: ShareMessage) -> ShareClient.Result =
        { address, device, message -> ShareClient.send(address, device, message) },
) {
    enum class Outcome { DELIVERED_TO_COMPUTER, DELIVERED_TO_ONEDRIVE, RETRY_LATER }

    /**
     * @param localNetwork the phone is on Wi-Fi (or a VPN) – computers are worth trying
     * @param internet OneDrive can be reached
     */
    fun deliver(pending: PendingShare, localNetwork: Boolean, internet: Boolean): Outcome {
        var share = pending
        if (localNetwork && share.unknownAttempts < MAX_UNKNOWN_ATTEMPTS) {
            when (toComputer(share)) {
                Lan.DELIVERED -> {
                    queue.remove(share)
                    return Outcome.DELIVERED_TO_COMPUTER
                }
                Lan.UNKNOWN -> {
                    // The computer may have the note. Asking it again is safe (it knows the id); OneDrive would
                    // make a second note – so that comes only after several tries.
                    share = queue.update(share, unknownAttempts = share.unknownAttempts + 1)
                    if (share.unknownAttempts < MAX_UNKNOWN_ATTEMPTS) return Outcome.RETRY_LATER
                }
                Lan.NOT_DELIVERED -> Unit
            }
        }

        if (!internet) return Outcome.RETRY_LATER
        val client = graph() ?: return Outcome.RETRY_LATER
        return try {
            toOneDrive(share, client)
            queue.remove(share)
            Outcome.DELIVERED_TO_ONEDRIVE
        } catch (_: IOException) {
            Outcome.RETRY_LATER
        }
    }

    private enum class Lan { DELIVERED, NOT_DELIVERED, UNKNOWN }

    private fun toComputer(share: PendingShare): Lan {
        val candidates = devices().filter { it.addresses.isNotEmpty() }
        if (candidates.isEmpty() || share.images.size > ShareMessage.MAX_ATTACHMENTS ||
            share.images.sumOf { it.file.length() } > MAX_LAN_BYTES
        ) return Lan.NOT_DELIVERED

        val attachments = try {
            share.images.map { ShareAttachment(it.extension, it.file.readBytes()) }
        } catch (_: IOException) {
            return Lan.NOT_DELIVERED
        }
        var unknown = false
        for (device in candidates) {
            for (address in device.addresses.take(MAX_ADDRESSES)) {
                val message = ShareMessage(share.id, now(), share.createdLocal, share.markdown, attachments)
                when (val result = send(address, device, message)) {
                    is ShareClient.Result.Answered -> when (result.response.status) {
                        ShareStatus.CREATED, ShareStatus.DUPLICATE -> return Lan.DELIVERED
                        // Its clock disagrees or its disk failed: OneDrive it is. It did not write the note.
                        ShareStatus.REFUSED, ShareStatus.FAILED -> return Lan.NOT_DELIVERED
                    }
                    ShareClient.Result.Unknown -> {
                        unknown = true
                        break // the computer was there; its other addresses lead to the same place
                    }
                    ShareClient.Result.Unreachable -> Unit
                }
            }
        }
        return if (unknown) Lan.UNKNOWN else Lan.NOT_DELIVERED
    }

    private fun toOneDrive(pending: PendingShare, client: GraphClient) {
        var share = pending
        val time = share.createdLocal
        val day = "${vault().trim('/')}/${NotePath.directory(time.toLocalDate())}"

        // Images first, like the desktop: assets/HHmm-NN.ext, the first free number.
        for (i in share.uploadedLinks.size until share.images.size) {
            val image = share.images[i]
            val data = image.file.readBytes()
            var link: String? = null
            for (n in 1..999) {
                val name = "%02d%02d-%02d.%s".format(time.hour, time.minute, n, image.extension)
                if (client.upload("$day/${NotePath.ASSETS}/$name", data, mimeType(image.extension))) {
                    link = "${NotePath.ASSETS}/$name"
                    break
                }
            }
            share = queue.update(share, uploadedLinks = share.uploadedLinks + (link ?: throw IOException("No free image name.")))
        }

        val text = ShareComposer.withLinks(share.markdown, share.uploadedLinks).toByteArray(Charsets.UTF_8)
        var name = NoteName.create(TagName.SHARE, time.hour, time.minute)
        repeat(500) {
            if (client.upload("$day/${name.fileName}", text, "text/markdown")) return
            name = name.nextCollisionCandidate(time.second)
        }
        throw IOException("No free note name.")
    }

    companion object {
        const val MAX_UNKNOWN_ATTEMPTS = 3
        private const val MAX_ADDRESSES = 4
        private const val MAX_LAN_BYTES = 24L * 1024 * 1024

        fun mimeType(extension: String): String = when (extension.lowercase()) {
            "png" -> "image/png"
            "jpg", "jpeg" -> "image/jpeg"
            "gif" -> "image/gif"
            "bmp" -> "image/bmp"
            "webp" -> "image/webp"
            else -> "application/octet-stream"
        }

        /** The extension to store an image of this MIME type under; null for what the desktop does not show. */
        fun extensionFor(mimeType: String?): String? = when (mimeType?.lowercase()?.substringBefore(';')?.trim()) {
            "image/png" -> "png"
            "image/jpeg", "image/jpg" -> "jpg"
            "image/gif" -> "gif"
            "image/bmp", "image/x-ms-bmp" -> "bmp"
            "image/webp" -> "webp"
            else -> null
        }
    }
}
