package io.github.vlko.wolfmark.core

import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.time.LocalDateTime

/** A share on its way: to a computer on the network, else to OneDrive, else it waits – and never twice. */
class ShareDeliveryTest {
    @get:Rule
    val temp = TemporaryFolder()
    private val key = ByteArray(32) { (it * 3).toByte() }
    private val drive = FakeOneDrive()
    private val created = LocalDateTime.of(2026, 11, 17, 14, 3, 27)
    private var devices = listOf<ShareDevice>()
    private val closeables = ArrayList<AutoCloseable>()

    private val queue by lazy { ShareQueue(temp.newFolder("queue")) }
    private val delivery by lazy { ShareDelivery(queue, { devices }, { drive.client() }, { "WolfMark" }) }

    @After
    fun close() {
        drive.close()
        closeables.forEach { it.close() }
    }

    private fun share(text: String, vararg images: Pair<String, ByteArray>): PendingShare =
        queue.enqueue(created, { ShareComposer.compose(null, text, it) }, images.map { it.first }) { i, file ->
            file.writeBytes(images[i].second)
            true
        }

    private fun computer(answer: FakeComputer.(ByteArray) -> ByteArray?): FakeComputer {
        lateinit var pc: FakeComputer
        pc = FakeComputer(key) { pc.answer(it) }
        closeables += pc
        devices = listOf(pc.device)
        return pc
    }

    @Test
    fun withAComputerOnTheNetworkNothingGoesToOneDrive() {
        val pc = computer { response(ShareStatus.CREATED, "2026/11/17/Share_1403.md") }

        val outcome = delivery.deliver(share("https://example.com/x", "png" to byteArrayOf(1, 2, 3)), localNetwork = true, internet = true)

        assertEquals(ShareDelivery.Outcome.DELIVERED_TO_COMPUTER, outcome)
        assertEquals(1, pc.received.size)
        assertTrue(drive.files().isEmpty())
        assertTrue(queue.pending().isEmpty())
    }

    @Test
    fun withoutAComputerTheNoteIsWrittenStraightIntoTheNotesFolder() {
        // An address where nothing listens: the computer is off.
        devices = listOf(ShareDevice("pc", "DESKTOP", 9, listOf("127.0.0.1"), key))

        val outcome = delivery.deliver(share("https://example.com/x"), localNetwork = true, internet = true)

        assertEquals(ShareDelivery.Outcome.DELIVERED_TO_ONEDRIVE, outcome)
        assertEquals(listOf("WolfMark/2026/11/17/Share_1403.md"), drive.files())
        assertEquals("# example.com\nhttps://example.com/x\n", drive.text("WolfMark/2026/11/17/Share_1403.md"))
        assertTrue(queue.pending().isEmpty())
    }

    @Test
    fun aTakenNameGetsSecondsAndThenANumberAndNothingIsOverwritten() {
        drive.put("WolfMark/2026/11/17/Share_1403.md", "from the desktop")
        drive.put("WolfMark/2026/11/17/Share_140327.md", "another")

        delivery.deliver(share("third"), localNetwork = false, internet = true)

        assertEquals("from the desktop", drive.text("WolfMark/2026/11/17/Share_1403.md"))
        assertEquals("another", drive.text("WolfMark/2026/11/17/Share_140327.md"))
        assertEquals("third\n", drive.text("WolfMark/2026/11/17/Share_140327-2.md"))
    }

    @Test
    fun imagesGoToTheDaysAssetsAndTheNoteLinksThem() {
        drive.put("WolfMark/2026/11/17/assets/1403-01.png", byteArrayOf(9)) // pasted on the desktop in the same minute

        delivery.deliver(share("foto", "jpg" to byteArrayOf(1), "png" to byteArrayOf(2)), localNetwork = false, internet = true)

        assertEquals(
            "foto\n\n![](assets/1403-01.jpg)\n![](assets/1403-02.png)\n",
            drive.text("WolfMark/2026/11/17/Share_1403.md"),
        )
        assertEquals(
            listOf("1403-01.jpg", "1403-01.png", "1403-02.png"),
            drive.files().filter { "/assets/" in it }.map { it.substringAfterLast('/') },
        )
    }

    @Test
    fun aServiceThatWantsFoldersMadeFirstGetsThem() {
        FakeOneDrive(autoCreateFolders = false).use { strict ->
            val d = ShareDelivery(queue, { emptyList() }, { strict.client() }, { "WolfMark" })
            assertEquals(ShareDelivery.Outcome.DELIVERED_TO_ONEDRIVE, d.deliver(share("x", "png" to byteArrayOf(1)), false, true))
            assertEquals(listOf("WolfMark/2026/11/17/Share_1403.md", "WolfMark/2026/11/17/assets/1403-01.png"), strict.files())
        }
    }

    @Test
    fun offlineTheShareWaitsAndIsDeliveredOnceLater() {
        val pending = share("later")
        assertEquals(ShareDelivery.Outcome.RETRY_LATER, delivery.deliver(pending, localNetwork = false, internet = false))
        assertEquals(1, queue.pending().size)
        assertTrue(drive.files().isEmpty())

        // The queue is read again after a restart of the app.
        val again = ShareQueue(File(temp.root, "queue")).pending().single()
        assertEquals(pending.id, again.id)
        assertEquals(created, again.createdLocal)
        assertEquals(ShareDelivery.Outcome.DELIVERED_TO_ONEDRIVE, delivery.deliver(again, localNetwork = false, internet = true))
        assertEquals(1, drive.files().size)
        assertTrue(queue.pending().isEmpty())
    }

    @Test
    fun anUploadThatFailsHalfwayContinuesWithoutUploadingImagesTwice() {
        val pending = share("foto", "jpg" to byteArrayOf(1), "jpg" to byteArrayOf(2))
        var uploads = 0
        val flaky = ShareDelivery(queue, { emptyList() }, {
            GraphClient({ if (++uploads == 2) throw java.io.IOException("connection lost") else "token" }, drive.url)
        }, { "WolfMark" })

        assertEquals(ShareDelivery.Outcome.RETRY_LATER, flaky.deliver(pending, false, true))
        assertEquals(listOf("assets/1403-01.jpg"), queue.pending().single().uploadedLinks)
        assertEquals(ShareDelivery.Outcome.DELIVERED_TO_ONEDRIVE, delivery.deliver(queue.pending().single(), false, true))

        assertEquals(2, drive.files().count { "/assets/" in it })
        assertEquals("foto\n\n![](assets/1403-01.jpg)\n![](assets/1403-02.jpg)\n", drive.text("WolfMark/2026/11/17/Share_1403.md"))
    }

    @Test
    fun aComputerThatRefusesOrFailsHandsTheShareToOneDrive() {
        for (status in listOf(ShareStatus.REFUSED, ShareStatus.FAILED)) {
            computer { response(status, "") }
            assertEquals(ShareDelivery.Outcome.DELIVERED_TO_ONEDRIVE, delivery.deliver(share("x $status"), true, true))
        }
        assertEquals(2, drive.files().size)
    }

    @Test
    fun aComputerThatTookTheShareWithoutAnsweringIsAskedAgainBeforeOneDriveIsUsed() {
        var silent = true
        val pc = computer { if (silent) null else response(ShareStatus.DUPLICATE, "2026/11/17/Share_1403.md") }
        val pending = share("maybe written")

        // The note may exist on the computer: OneDrive would make a second one.
        assertEquals(ShareDelivery.Outcome.RETRY_LATER, delivery.deliver(pending, true, true))
        assertTrue(drive.files().isEmpty())
        assertEquals(1, queue.pending().single().unknownAttempts)

        silent = false
        assertEquals(ShareDelivery.Outcome.DELIVERED_TO_COMPUTER, delivery.deliver(queue.pending().single(), true, true))
        assertTrue(drive.files().isEmpty())
        // Both attempts carried the same id: that is how the computer knows.
        assertEquals(2, pc.received.size)
        assertEquals(pc.received[0].copyOfRange(1, 17).toList(), pc.received[1].copyOfRange(1, 17).toList())
    }

    @Test
    fun aComputerThatNeverAnswersDoesNotHoldTheShareForever() {
        computer { null }
        var pending = share("stuck")
        repeat(ShareDelivery.MAX_UNKNOWN_ATTEMPTS - 1) {
            assertEquals(ShareDelivery.Outcome.RETRY_LATER, delivery.deliver(pending, true, true))
            pending = queue.pending().single()
        }
        assertEquals(ShareDelivery.Outcome.DELIVERED_TO_ONEDRIVE, delivery.deliver(pending, true, true))
        assertEquals(1, drive.files().size)
    }

    @Test
    fun aComputerWithAnotherKeyGivesNoAnswer() {
        val pc = computer { response(ShareStatus.CREATED, "x") }
        devices = listOf(pc.device.copy(key = ByteArray(32)))
        assertEquals(ShareDelivery.Outcome.RETRY_LATER, delivery.deliver(share("x"), true, false))
        assertTrue(pc.received.isEmpty())
    }

    @Test
    fun theDevicesAnnouncedInTheNotesFolderAreReadAndRemembered() {
        val json = """{"version":1,"id":"abc","name":"DESKTOP","port":47821,"addresses":["192.168.1.20","not-an-address"],"key":"${java.util.Base64.getEncoder().encodeToString(key)}"}"""
        drive.put("WolfMark/.wolfmark/share/abc.json", json)
        drive.put("WolfMark/.wolfmark/share/future.json", """{"version":2,"id":"x"}""")
        drive.put("WolfMark/.wolfmark/share/broken.json", "{")
        val cache = File(temp.root, "devices.json")

        val fetched = ShareDevices(cache).refresh(drive.client(), "WolfMark")

        assertEquals(listOf(ShareDevice("abc", "DESKTOP", 47821, listOf("192.168.1.20"), key)), fetched)
        assertEquals(fetched, ShareDevices(cache).load()) // without the internet
        assertNotNull(ShareDevices.parse(json))
        assertNull(ShareDevices.parse(json.replace("47821", "0")))
        assertTrue(ShareDevices(File(temp.root, "none.json")).load().isEmpty())

        FakeOneDrive().use { empty -> assertTrue(ShareDevices(cache).refresh(empty.client(), "WolfMark").isEmpty()) }
    }
}
