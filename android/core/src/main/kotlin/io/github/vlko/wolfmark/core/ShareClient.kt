package io.github.vlko.wolfmark.core

import java.io.DataInputStream
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket

/** A computer that receives shares, as it announces itself in `.wolfmark/share/<id>.json` of the notes folder. */
data class ShareDevice(val id: String, val name: String, val port: Int, val addresses: List<String>, val key: ByteArray) {
    override fun equals(other: Any?): Boolean =
        other is ShareDevice && id == other.id && name == other.name && port == other.port &&
            addresses == other.addresses && key.contentEquals(other.key)

    override fun hashCode(): Int = id.hashCode()
}

/** Sends one share to one address of a computer on the local network. */
object ShareClient {
    sealed interface Result {
        /** The computer answered. */
        data class Answered(val response: ShareResponse) : Result

        /** Nobody there (or not in time): nothing was sent, another address or OneDrive can be tried safely. */
        data object Unreachable : Result

        /** Connected, but no readable answer: the note may or may not exist. Try the same computer again. */
        data object Unknown : Result
    }

    fun send(
        address: String,
        device: ShareDevice,
        message: ShareMessage,
        connectTimeoutMs: Int = 1500,
        answerTimeoutMs: Int = 30_000,
    ): Result {
        val socket = Socket()
        try {
            try {
                socket.connect(InetSocketAddress(address, device.port), connectTimeoutMs)
            } catch (_: IOException) {
                return Result.Unreachable
            } catch (_: IllegalArgumentException) {
                return Result.Unreachable
            }

            return try {
                socket.soTimeout = answerTimeoutMs
                socket.getOutputStream().run {
                    write(ShareFrame.seal(ShareFrame.REQUEST, device.key, message.encode()))
                    flush()
                }
                val input = DataInputStream(socket.getInputStream())
                val header = ByteArray(ShareFrame.HEADER_LENGTH).also(input::readFully)
                val length = ShareFrame.bodyLength(header, ShareFrame.RESPONSE) ?: return Result.Unknown
                val body = ByteArray(length).also(input::readFully)
                val response = ShareFrame.open(header, body, device.key)?.let(ShareResponse::decode)
                if (response == null) Result.Unknown else Result.Answered(response)
            } catch (_: IOException) {
                // Also when the computer closes without a word: it does not know our key (it was replaced).
                Result.Unknown
            }
        } finally {
            runCatching { socket.close() }
        }
    }
}
