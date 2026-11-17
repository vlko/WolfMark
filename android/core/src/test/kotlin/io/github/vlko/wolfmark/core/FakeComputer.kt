package io.github.vlko.wolfmark.core

import java.io.DataInputStream
import java.net.InetAddress
import java.net.ServerSocket
import kotlin.concurrent.thread

/** The desktop's listener, as far as the phone can tell: reads a request frame and answers what the test says. */
class FakeComputer(private val key: ByteArray, private val answer: (plaintext: ByteArray) -> ByteArray?) : AutoCloseable {
    private val server = ServerSocket(0, 10, InetAddress.getLoopbackAddress())
    val received = ArrayList<ByteArray>()
    val port: Int get() = server.localPort
    val device: ShareDevice get() = ShareDevice("pc", "DESKTOP", port, listOf("127.0.0.1"), key)

    init {
        thread(isDaemon = true) {
            while (!server.isClosed) {
                val socket = runCatching { server.accept() }.getOrNull() ?: break
                socket.use {
                    runCatching {
                        val input = DataInputStream(it.getInputStream())
                        val header = ByteArray(ShareFrame.HEADER_LENGTH).also(input::readFully)
                        val length = ShareFrame.bodyLength(header, ShareFrame.REQUEST) ?: return@runCatching
                        val body = ByteArray(length).also(input::readFully)
                        val plaintext = ShareFrame.open(header, body, key) ?: return@runCatching // no answer without the key
                        synchronized(received) { received += plaintext }
                        answer(plaintext)?.let { response -> it.getOutputStream().write(response) }
                    }
                }
            }
        }
    }

    fun response(status: ShareStatus, path: String): ByteArray {
        val bytes = path.toByteArray()
        val plaintext = byteArrayOf(1, status.ordinal.toByte(), (bytes.size shr 8).toByte(), bytes.size.toByte()) + bytes
        return ShareFrame.seal(ShareFrame.RESPONSE, key, plaintext)
    }

    override fun close() = server.close()
}
