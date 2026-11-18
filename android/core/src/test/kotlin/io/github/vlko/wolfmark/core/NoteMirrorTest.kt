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
        // Images are not downloaded with the notes and the trash never is; of .wolfmark only the tag colours are read.
        assertTrue(drive.requests.none { "assets" in it || "trash" in it })
        assertEquals(listOf("GET /raw/WolfMark/.wolfmark/tags.json"), drive.requests.filter { "/raw/" in it && ".wolfmark" in it }.distinct())
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

    @Test
    fun theListFillsWhileNotesAreStillComingAndAnInterruptedRunKeepsWhatArrived() {
        repeat(30) { drive.put("WolfMark/2026/09/%02d/0900.md".format(it + 1), "note $it") }
        drive.pageSize = 10
        val mirror = NoteMirror(temp.root)
        val seen = ArrayList<Int>()

        // The service gives up after the second page: the first twenty notes stay.
        val error = runCatching {
            mirror.sync(drive.client(), "WolfMark") { n ->
                synchronized(seen) { seen += mirror.notes().size }
                if (n >= 20) drive.failWith = 503
            }
        }.exceptionOrNull()

        assertTrue(error is GraphException)
        assertTrue(seen.any { it in 1..29 }) // the list was readable, and partly filled, during the run
        val kept = mirror.notes().size
        assertTrue("kept $kept", kept in 10..29)

        drive.failWith = null
        drive.requests.clear()
        mirror.sync(drive.client(), "WolfMark")
        assertEquals(30, mirror.notes().size)
        // What was already here is not fetched again.
        assertEquals(30 - kept, drive.requests.count { it.startsWith("GET /raw/") })
    }

    @Test
    fun tagColoursChosenOnTheDesktopAndTaskCountsComeWithTheNotes() {
        drive.put("WolfMark/.wolfmark/tags.json", """{"version":1,"tags":[{"name":"work","color":"#2F6FEB"}]}""")
        drive.put("WolfMark/2026/11/17/work_0900.md", "# Plan\n- [ ] one\n- [x] two\n- [ ] three\n")
        val mirror = NoteMirror(temp.root)
        mirror.sync(drive.client(), "WolfMark")

        assertEquals(0x2F6FEB, TagColors.parse(mirror.tagsJson()).of("work"))
        val note = mirror.notes().single()
        assertEquals(2 to 1, note.openTasks to note.doneTasks)

        // Changed on the desktop: the phone follows.
        drive.put("WolfMark/.wolfmark/tags.json", """{"version":1,"tags":[{"name":"work","color":"#00A884"}]}""")
        mirror.sync(drive.client(), "WolfMark")
        assertEquals(0x00A884, TagColors.parse(NoteMirror(temp.root).tagsJson()).of("work"))
        assertEquals(1, mirror.notes().size) // tags.json is not a note
    }

    @Test
    fun aCopyMadeBeforeTagColoursWereReadAsksForThemOnce() {
        drive.put("WolfMark/2026/11/17/work_0900.md", "- [ ] one\n")
        val mirror = NoteMirror(temp.root)
        mirror.sync(drive.client(), "WolfMark")
        // What an older version left behind: no task counts, no tags.json, a change feed that is already past it.
        val state = java.io.File(temp.root, "mirror.json")
        state.writeText(state.readText().replace("\"tagsChecked\":true", "\"tagsChecked\":false").replace(Regex("\"open\":\\d+,?"), "").replace(Regex(",\"done\":\\d+"), ""))
        drive.put("WolfMark/.wolfmark/tags.json", """{"version":1,"tags":[{"name":"work","color":"#2F6FEB"}]}""")
        java.io.File(temp.root, "tags.json").delete()
        drive.requests.clear()

        val old = NoteMirror(temp.root)
        assertEquals(1, old.notes().single().openTasks) // counted from the file on the phone
        old.sync(drive.client(), "WolfMark")
        assertEquals(0x2F6FEB, TagColors.parse(old.tagsJson()).of("work"))
        assertTrue(drive.requests.none { it.contains("work_0900") }) // the note itself is not fetched again
    }
}
