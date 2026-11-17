package io.github.vlko.wolfmark.core

import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.time.LocalDate

/** The phone's read-only copy of the notes, kept current through the change feed. */
class NoteMirrorTest {
    @get:Rule
    val temp = TemporaryFolder()
    private val drive = FakeOneDrive()

    @After
    fun close() = drive.close()

    private fun seed() {
        drive.put("WolfMark/2026/11/17/Share_1403.md", "# Example\nhttps://example.com/x\n")
        drive.put("WolfMark/2026/11/17/praca_0927.md", "---\nsource: x\n---\n## Porada\n\n- [ ] zavolať **Petrovi**\n- [link](https://a.b)\n")
        drive.put("WolfMark/2026/11/16/0815.md", "ranná poznámka")
        drive.put("WolfMark/2026/11/17/assets/1403-01.png", byteArrayOf(1, 2, 3))
        drive.put("WolfMark/.wolfmark/trash/2026/11/01/old_1200.20261117-135357.md", "deleted")
        drive.put("WolfMark/.wolfmark/tags.json", "{}")
        drive.put("WolfMark/readme.md", "outside the day folders")
        drive.put("Other/2026/11/17/0900.md", "not ours")
    }

    @Test
    fun theFirstRunCopiesTheNotesAndOnlyTheNotes() {
        seed()
        val mirror = NoteMirror(temp.root)

        assertEquals(4, mirror.sync(drive.client(), "WolfMark"))

        val notes = mirror.notes()
        assertEquals(
            listOf("2026/11/17/Share_1403.md", "2026/11/17/praca_0927.md", "2026/11/16/0815.md"),
            notes.filter { it.path.isStandard }.map { it.path.relativePath },
        )
        val share = notes.first()
        assertEquals("Share", share.path.tag)
        assertEquals("Example", share.title)
        assertEquals("https://example.com/x", share.preview)
        assertEquals(LocalDate.of(2026, 11, 17), share.day())
        val work = notes.single { it.path.tag == "praca" }
        assertEquals("Porada", work.title)
        assertEquals("zavolať Petrovi link", work.preview)
        assertEquals("ranná poznámka", notes.single { it.path.relativePath.endsWith("0815.md") }.title)
        // A Markdown file outside the day folders is a note too, without a tag.
        assertNull(notes.single { it.path.relativePath == "readme.md" }.path.tag)
        assertEquals("# Example\nhttps://example.com/x\n", mirror.read(share.path))
        // Images are not downloaded with the notes; the trash and the settings never are.
        assertTrue(drive.requests.none { "assets" in it || ".wolfmark" in it })
    }

    @Test
    fun laterRunsFetchOnlyWhatChanged() {
        seed()
        val mirror = NoteMirror(temp.root)
        mirror.sync(drive.client(), "WolfMark")
        drive.requests.clear()

        assertEquals(0, mirror.sync(drive.client(), "WolfMark"))
        assertEquals(1, drive.requests.size) // one question, no downloads

        drive.put("WolfMark/2026/11/17/Share_1403.md", "# Changed\n")
        drive.put("WolfMark/2026/11/18/Share_0700.md", "# New\n")
        drive.delete("WolfMark/2026/11/16/0815.md")
        drive.rename("WolfMark/2026/11/17/praca_0927.md", "WolfMark/2026/11/17/dom_0927.md") // the tag changed on the desktop
        drive.requests.clear()

        assertEquals(4, mirror.sync(drive.client(), "WolfMark"))

        // Also after a restart. (readme.md sorts by when it was changed, so it is left out of the order here.)
        val notes = NoteMirror(temp.root).notes().filter { it.path.isStandard }
        assertEquals(
            listOf("2026/11/18/Share_0700.md", "2026/11/17/Share_1403.md", "2026/11/17/dom_0927.md"),
            notes.map { it.path.relativePath },
        )
        assertEquals("Changed", notes[1].title)
        assertEquals("Porada", notes[2].title)
        assertEquals("dom", notes[2].path.tag)
        // The renamed note was not downloaded again.
        assertEquals(2, drive.requests.count { it.startsWith("GET /raw/") })
    }

    @Test
    fun aDeletedFolderTakesItsNotesAlong() {
        seed()
        val mirror = NoteMirror(temp.root)
        mirror.sync(drive.client(), "WolfMark")

        drive.delete("WolfMark/2026/11/17")
        mirror.sync(drive.client(), "WolfMark")

        assertEquals(listOf("2026/11/16/0815.md", "readme.md"), mirror.notes().map { it.path.relativePath }.sorted())
    }

    @Test
    fun manyNotesComeInPagesAndAnExpiredFeedStartsOver() {
        repeat(25) { drive.put("WolfMark/2026/10/%02d/0900.md".format(it + 1), "note $it") }
        drive.pageSize = 7
        val mirror = NoteMirror(temp.root)
        assertEquals(25, mirror.sync(drive.client(), "WolfMark"))
        assertEquals(25, mirror.notes().size)

        drive.delete("WolfMark/2026/10/01/0900.md")
        drive.expireDelta = true
        mirror.sync(drive.client(), "WolfMark")
        assertEquals(24, mirror.notes().size)
    }

    @Test
    fun anotherNotesFolderReplacesTheCopy() {
        seed()
        val mirror = NoteMirror(temp.root)
        mirror.sync(drive.client(), "WolfMark")
        mirror.sync(drive.client(), "Other")
        assertEquals(listOf("2026/11/17/0900.md"), mirror.notes().map { it.path.relativePath })
    }

    @Test
    fun imagesAreFetchedWhenANoteShowsThemAndOnlyFromBesideTheNotes() {
        seed()
        drive.put("secret.png", byteArrayOf(7))
        val mirror = NoteMirror(temp.root)
        mirror.sync(drive.client(), "WolfMark")
        val note = mirror.notes().first().path

        val image = mirror.asset(drive.client(), note, "assets/1403-01.png")
        assertNotNull(image)
        assertEquals(listOf<Byte>(1, 2, 3), image!!.readBytes().toList())
        assertNotNull(mirror.asset(null, note, "assets/1403-01.png")) // now without a connection
        assertNull(mirror.asset(drive.client(), note, "assets/missing.png"))
        // Nothing outside the notes folder, and nothing from the web.
        assertNull(mirror.asset(drive.client(), note, "../../../../secret.png"))
        assertNull(mirror.asset(drive.client(), note, "https://example.com/a.png"))
        assertNull(mirror.asset(drive.client(), note, "/etc/passwd"))
    }

    @Test
    fun aFailingServiceLeavesTheCopyAsItWas() {
        seed()
        val mirror = NoteMirror(temp.root)
        mirror.sync(drive.client(), "WolfMark")
        drive.failWith = 503

        val error = runCatching { mirror.sync(drive.client(), "WolfMark") }.exceptionOrNull()

        assertTrue(error is GraphException && error.status == 503)
        assertEquals(4, mirror.notes().size)
    }
}
