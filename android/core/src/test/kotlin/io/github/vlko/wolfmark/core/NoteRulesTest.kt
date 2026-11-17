package io.github.vlko.wolfmark.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.LocalDate

class NoteRulesTest {
    @Test
    fun pathsAreReadLikeOnTheDesktop() {
        val note = NotePath.parse("2026/11/17/Share_1403.md")!!
        assertTrue(note.isStandard)
        assertEquals(LocalDate.of(2026, 11, 17), note.date)
        assertEquals("Share", note.tag)
        assertEquals("2026/11/17", NotePath.directory(note.date!!))
        assertTrue(NotePath.parse("2026\\11\\17\\praca_1403-LAPTOP.md")!!.name!!.isConflictCopy)

        // Notes without a day or a proper name are still notes.
        assertFalse(NotePath.parse("2026/11/17/notes.md")!!.isStandard)
        assertFalse(NotePath.parse("2026/02/30/0900.md")!!.isStandard)
        assertFalse(NotePath.parse("readme.md")!!.isStandard)

        for (path in listOf("", "2026/11/17/a.txt", "2026/11/17/.0900.md.tmp", "2026/11/17/~0900.md", ".wolfmark/trash/2026/11/17/0900.md", "2026/11/17/assets/0900.md", "2026/11/17/Assets/x.md", ".md")) {
            assertNull(path, NotePath.parse(path))
        }
    }

    @Test
    fun tagsFollowTheDesktopRules() {
        assertTrue(TagName.isValid("pracovné-veci"))
        assertTrue(TagName.isValid("Share"))
        assertFalse(TagName.isValid(""))
        assertFalse(TagName.isValid("a_b"))
        assertFalse(TagName.isValid("a b"))
        assertFalse(TagName.isValid("x".repeat(65)))
        assertEquals(TagName.key("Share"), TagName.key("SHARE"))
        assertEquals("žltá", TagName.normalize("žltá"))
    }

    @Test
    fun titlesAndPreviewsSkipTheSyntax() {
        val s = NoteText.summarize("---\nsource: claudecode\n---\n\n# Hlavný *nadpis*\n\n```kotlin\nval x = 1\n```\n> citát s `kódom` a [[Iná poznámka|odkazom]]\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n---\n1. prvý\n- ![obrázok](assets/a.png)")
        assertEquals("Hlavný nadpis", s.title)
        assertEquals("val x = 1 citát s kódom a odkazom A · B 1 · 2 prvý obrázok", s.preview)
        assertEquals(NoteText.Summary("", ""), NoteText.summarize("\n\n"))
        assertEquals(NoteText.TITLE_LENGTH, NoteText.summarize("x".repeat(500)).title.length)
        assertEquals("text\n", NoteText.body("---\na: b\n---\ntext\n"))
        assertEquals("---\nno end", NoteText.body("---\nno end"))
    }
}
