package io.github.vlko.wolfmark.core

import org.junit.Assert.assertEquals
import org.junit.Assume.assumeTrue
import org.junit.Test
import java.time.LocalDateTime
import java.util.Base64
import java.util.UUID

/**
 * The phone's side against a real WolfMark that receives shares – skipped unless told where one is:
 *
 *     WOLFMARK_LIVE_DESKTOP=192.168.1.20:47821:KEY ./gradlew :core:test --tests LiveDesktopTest
 *
 * (address, port and the base64 key from the computer's file in `.wolfmark/share` of the notes folder)
 *
 * It creates one note with the tag Share in that WolfMark's notes.
 */
class LiveDesktopTest {
    @Test
    fun aShareSentTwiceBecomesOneNote() {
        val target = System.getenv("WOLFMARK_LIVE_DESKTOP").orEmpty().split(':')
        assumeTrue("WOLFMARK_LIVE_DESKTOP is not set", target.size == 3)
        val device = ShareDevice("live", "live", target[1].toInt(), listOf(target[0]), Base64.getDecoder().decode(target[2]))
        val id = UUID.randomUUID()
        val created = LocalDateTime.now()
        fun message() = ShareMessage(
            id, System.currentTimeMillis(), created,
            ShareComposer.compose("Skúška z Kotlinu – žltá ruža", "https://example.com/live", 1),
            listOf(ShareAttachment("png", PNG)),
        )

        val first = ShareClient.send(target[0], device, message())
        val second = ShareClient.send(target[0], device, message())

        first as ShareClient.Result.Answered
        second as ShareClient.Result.Answered
        assertEquals(ShareStatus.CREATED, first.response.status)
        assertEquals(ShareResponse(ShareStatus.DUPLICATE, first.response.notePath), second.response)
        println("LIVE NOTE ${first.response.notePath}")

        // Another key: the desktop says nothing at all.
        val stranger = device.copy(key = ByteArray(32))
        assertEquals(ShareClient.Result.Unknown, ShareClient.send(target[0], stranger, message(), answerTimeoutMs = 5000))
    }

    private companion object {
        /** A 1×1 PNG. */
        val PNG: ByteArray = Base64.getDecoder().decode(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==",
        )
    }
}
