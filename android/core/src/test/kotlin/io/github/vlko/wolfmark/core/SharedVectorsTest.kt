package io.github.vlko.wolfmark.core

import org.json.JSONObject
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.time.LocalDateTime
import java.util.UUID

/** The vectors the desktop tests read too (`testdata/` in the repository root): both sides agree on every byte. */
class SharedVectorsTest {
    private fun vectors(name: String) = JSONObject(File(System.getProperty("wolfmark.testdata"), name).readText())

    private fun hex(s: String) = ByteArray(s.length / 2) { s.substring(it * 2, it * 2 + 2).toInt(16).toByte() }
    private fun hex(b: ByteArray) = b.joinToString("") { "%02x".format(it) }

    @Test
    fun validNamesParseAsListedAndRoundTrip() {
        val valid = vectors("note-names.json").getJSONArray("valid")
        assertTrue(valid.length() > 5)
        for (i in 0 until valid.length()) {
            val v = valid.getJSONObject(i)
            val file = v.getString("file")
            val name = NoteName.parse(file)
            assertNotNull(file, name)
            name!!
            assertEquals(file, if (v.isNull("tag")) null else v.getString("tag"), name.tag)
            assertEquals(file, v.getInt("hour"), name.hour)
            assertEquals(file, v.getInt("minute"), name.minute)
            assertEquals(file, v.getInt("second"), name.second)
            assertEquals(file, v.getBoolean("hasSeconds"), name.hasSeconds)
            assertEquals(file, v.getInt("suffix"), name.suffix)
            assertEquals(file, if (v.isNull("conflict")) null else v.getString("conflict"), name.conflictMarker)
            assertEquals(file.lowercase(), name.fileName.lowercase())
        }
    }

    @Test
    fun invalidNamesAreRejected() {
        val invalid = vectors("note-names.json").getJSONArray("invalid")
        for (i in 0 until invalid.length()) assertNull(invalid.getString(i), NoteName.parse(invalid.getString(i)))
    }

    @Test
    fun aTakenNameGetsSecondsAndThenANumber() {
        val collisions = vectors("note-names.json").getJSONArray("collisions")
        for (i in 0 until collisions.length()) {
            val v = collisions.getJSONObject(i)
            var name = NoteName.create(if (v.isNull("tag")) null else v.getString("tag"), v.getInt("hour"), v.getInt("minute"))
            val candidates = v.getJSONArray("candidates")
            for (k in 0 until candidates.length()) {
                assertEquals(candidates.getString(k), name.fileName)
                name = name.nextCollisionCandidate(v.getInt("second"))
            }
        }
    }

    @Test
    fun requestsEncodeToTheSharedVectors() {
        val all = vectors("share-frames.json")
        val key = hex(all.getString("keyHex"))
        val requests = all.getJSONArray("requests")
        assertEquals(2, requests.length())
        for (i in 0 until requests.length()) {
            val v = requests.getJSONObject(i)
            val attachments = v.getJSONArray("attachments").let { a ->
                (0 until a.length()).map { ShareAttachment(a.getJSONObject(it).getString("extension"), hex(a.getJSONObject(it).getString("dataHex"))) }
            }
            val message = ShareMessage(
                UUID.fromString(v.getString("id")),
                v.getLong("sentUnixMs"),
                LocalDateTime.parse(v.getString("createdLocal")),
                v.getString("markdown"),
                attachments,
            )
            val plaintext = message.encode()
            assertEquals(v.getString("plaintextHex"), hex(plaintext))
            val frame = ShareFrame.seal(ShareFrame.REQUEST, key, hex(v.getString("nonceHex")), plaintext)
            assertEquals(v.getString("frameHex"), hex(frame))

            val header = frame.copyOf(ShareFrame.HEADER_LENGTH)
            val body = frame.copyOfRange(ShareFrame.HEADER_LENGTH, frame.size)
            assertEquals(body.size, ShareFrame.bodyLength(header, ShareFrame.REQUEST))
            assertNull(ShareFrame.bodyLength(header, ShareFrame.RESPONSE))
            assertArrayEquals(plaintext, ShareFrame.open(header, body, key))
        }
    }

    @Test
    fun responsesOfTheDesktopAreRead() {
        val all = vectors("share-frames.json")
        val key = hex(all.getString("keyHex"))
        val responses = all.getJSONArray("responses")
        for (i in 0 until responses.length()) {
            val v = responses.getJSONObject(i)
            val frame = hex(v.getString("frameHex"))
            val header = frame.copyOf(ShareFrame.HEADER_LENGTH)
            val body = frame.copyOfRange(ShareFrame.HEADER_LENGTH, frame.size)
            assertEquals(body.size, ShareFrame.bodyLength(header, ShareFrame.RESPONSE))
            val response = ShareResponse.decode(ShareFrame.open(header, body, key)!!)
            assertEquals(ShareResponse(ShareStatus.entries[v.getInt("status")], v.getString("notePath")), response)
        }
    }

    @Test
    fun aFrameDoesNotOpenWithAnotherKeyOrAfterAChange() {
        val key = ByteArray(32) { it.toByte() }
        val frame = ShareFrame.seal(ShareFrame.RESPONSE, key, "hello".toByteArray())
        val header = frame.copyOf(ShareFrame.HEADER_LENGTH)
        val body = frame.copyOfRange(ShareFrame.HEADER_LENGTH, frame.size)
        assertArrayEquals("hello".toByteArray(), ShareFrame.open(header, body, key))
        assertNull(ShareFrame.open(header, body, ByteArray(32)))
        assertNull(ShareFrame.open(header, body.clone().also { it[body.size - 1] = (it[body.size - 1] + 1).toByte() }, key))
        // The kind is authenticated: a request cannot be passed off as a response.
        assertNull(ShareFrame.open(header.clone().also { it[4] = ShareFrame.REQUEST }, body, key))
        assertNull(ShareFrame.bodyLength(hex("574d53310202000001"), ShareFrame.RESPONSE)) // over the limit
        assertNull(ShareFrame.bodyLength(hex("574d533102ffffffff"), ShareFrame.RESPONSE))
    }
}
