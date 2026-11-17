package io.github.vlko.wolfmark.core

import java.nio.ByteBuffer
import java.security.GeneralSecurityException
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * One message on the wire, the same bytes as the desktop's `ShareFrame` (ADR 0017):
 * `"WMS1" | kind (1) | body length (4, big-endian) | nonce (12) | ciphertext | tag (16)`, AES-256-GCM with the
 * paired key and the 9 header bytes as associated data. Checked against `testdata/share-frames.json`.
 */
object ShareFrame {
    const val HEADER_LENGTH = 9
    const val NONCE_LENGTH = 12
    const val TAG_LENGTH = 16
    const val KEY_LENGTH = 32
    const val MAX_BODY_LENGTH = 32 * 1024 * 1024
    const val REQUEST: Byte = 1
    const val RESPONSE: Byte = 2

    private val magic = "WMS1".toByteArray(Charsets.US_ASCII)
    private val random = SecureRandom()

    fun seal(kind: Byte, key: ByteArray, plaintext: ByteArray): ByteArray =
        seal(kind, key, ByteArray(NONCE_LENGTH).also(random::nextBytes), plaintext)

    /** With a given nonce – for the shared test vectors only; a nonce must never repeat under one key. */
    internal fun seal(kind: Byte, key: ByteArray, nonce: ByteArray, plaintext: ByteArray): ByteArray {
        require(key.size == KEY_LENGTH && nonce.size == NONCE_LENGTH)
        val bodyLength = NONCE_LENGTH + plaintext.size + TAG_LENGTH
        require(bodyLength <= MAX_BODY_LENGTH) { "The share is too large." }
        val header = ByteBuffer.allocate(HEADER_LENGTH).put(magic).put(kind).putInt(bodyLength).array()
        val cipher = cipher(Cipher.ENCRYPT_MODE, key, nonce, header)
        return header + nonce + cipher.doFinal(plaintext)
    }

    /** The body length announced by [header] when it is a frame of [kind] and an acceptable size, otherwise null. */
    fun bodyLength(header: ByteArray, kind: Byte): Int? {
        if (header.size != HEADER_LENGTH || !header.copyOf(4).contentEquals(magic) || header[4] != kind) return null
        val length = ByteBuffer.wrap(header, 5, 4).int
        return length.takeIf { it in (NONCE_LENGTH + TAG_LENGTH)..MAX_BODY_LENGTH }
    }

    /** Decrypts [body] read after [header]; null when the key or any byte is wrong. */
    fun open(header: ByteArray, body: ByteArray, key: ByteArray): ByteArray? {
        if (key.size != KEY_LENGTH || body.size < NONCE_LENGTH + TAG_LENGTH) return null
        return try {
            cipher(Cipher.DECRYPT_MODE, key, body.copyOf(NONCE_LENGTH), header)
                .doFinal(body, NONCE_LENGTH, body.size - NONCE_LENGTH)
        } catch (_: GeneralSecurityException) {
            null
        }
    }

    private fun cipher(mode: Int, key: ByteArray, nonce: ByteArray, header: ByteArray): Cipher =
        Cipher.getInstance("AES/GCM/NoPadding").apply {
            init(mode, SecretKeySpec(key, "AES"), GCMParameterSpec(TAG_LENGTH * 8, nonce))
            updateAAD(header)
        }
}
