package io.github.vlko.wolfmark.core

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.time.LocalDate
import java.time.YearMonth
import java.time.ZoneOffset

/** Tag colours and the calendar: what the phone shows must be what the desktop shows. */
class CalendarAndColorsTest {
    private fun rgb(hex: String) = hex.removePrefix("#").toInt(16)

    private fun note(path: String, open: Int = 0, done: Int = 0, modified: Long = 0) =
        MirrorNote(NotePath.parse(path)!!, "t", "p", modified, open, done)

    @Test
    fun tagsWithoutAChosenColourGetTheOneTheDesktopGivesThem() {
        val vectors = JSONObject(File(System.getProperty("wolfmark.testdata"), "tag-colors.json").readText())
        assertEquals(rgb(vectors.getString("untagged")), TagColors.forTag(null))
        val tags = vectors.getJSONArray("tags")
        assertTrue(tags.length() > 10)
        for (i in 0 until tags.length()) {
            val v = tags.getJSONObject(i)
            val tag = v.getString("tag")
            assertEquals(tag, v.getLong("hash"), TagColors.stableHash(TagName.key(tag)).toLong())
            assertEquals(tag, rgb(v.getString("color")), TagColors.forTag(tag))
        }
        val foreground = vectors.getJSONArray("foreground")
        for (i in 0 until foreground.length()) {
            val v = foreground.getJSONObject(i)
            assertEquals(v.getString("color"), v.getBoolean("darkText"), TagColors.prefersDarkForeground(rgb(v.getString("color"))))
        }
        val blends = vectors.getJSONArray("blends")
        for (i in 0 until blends.length()) {
            val v = blends.getJSONObject(i)
            assertEquals(rgb(v.getString("result")), TagColors.blend(rgb(v.getString("from")), rgb(v.getString("to")), v.getDouble("amount")))
        }
    }

    @Test
    fun coloursChosenOnTheDesktopWinOverTheHashedOnes() {
        val colors = TagColors.parse(
            """{ "version": 1, "tags": [
                 { "name": "work", "color": "#2F6FEB", "order": 0, "showInCalendarLabels": false },
                 { "name": "Ideas", "color": null, "order": 1 },
                 { "name": "bad", "color": "blue" } ] }""",
        )
        assertEquals(0x2F6FEB, colors.of("Work"))
        assertEquals(TagColors.forTag("ideas"), colors.of("ideas"))
        assertEquals(TagColors.forTag("bad"), colors.of("bad"))
        assertEquals(TagColors.UNTAGGED, colors.of(null))
        assertEquals(TagColors.UNTAGGED, colors.of(""))
        // Nothing readable: the hashed colours.
        assertEquals(TagColors.forTag("work"), TagColors.parse("{").of("work"))
        assertEquals(TagColors.forTag("work"), TagColors.parse(null).of("work"))
        assertEquals(TagColors.DARK_INK, TagColors.foregroundOn(0xF7C62F))
        assertEquals(TagColors.WHITE, TagColors.foregroundOn(0x1E88F5))
    }

    @Test
    fun aDayKnowsItsNotesTagsAndTasks() {
        val model = CalendarModel(
            listOf(
                note("2026/11/17/Share_1403.md", open = 1),
                note("2026/11/17/share_1500.md", open = 2, done = 1),
                note("2026/11/17/praca_0900.md", done = 4),
                note("2026/11/17/0815.md"),
                note("2026/11/17/alfa_0700.md"),
                note("2026/11/18/praca_0900.md"),
            ),
            ZoneOffset.UTC,
        )
        val day = model.day(LocalDate.of(2026, 11, 17))
        assertEquals(5, day.noteCount)
        // Most notes first, then by name; notes without a tag are the tag "".
        assertEquals(listOf(TagCount("Share", 2), TagCount("", 1), TagCount("alfa", 1), TagCount("praca", 1)), day.tags)
        assertEquals(3, day.openTasks)
        assertEquals(5, day.doneTasks)
        assertTrue(day.hasTag("share"))
        assertTrue(day.hasTag(""))
        assertFalse(day.hasTag("dom"))
        assertEquals(CalendarDay(LocalDate.of(2026, 11, 19)), model.day(LocalDate.of(2026, 11, 19)))

        val monday = CalendarModel.monday(LocalDate.of(2026, 11, 19))
        assertEquals(LocalDate.of(2026, 11, 16), monday)
        assertEquals(listOf(0, 5, 1, 0, 0, 0, 0), model.week(monday).map { it.noteCount })
        assertEquals(listOf(TagCount("Share", 2), TagCount("praca", 2), TagCount("", 1), TagCount("alfa", 1)), model.topics(monday))
    }

    @Test
    fun aNoteOutsideTheDayFoldersBelongsToTheDayItWasChanged() {
        val changed = LocalDate.of(2026, 11, 20).atTime(10, 0).toInstant(ZoneOffset.UTC).toEpochMilli()
        val model = CalendarModel(listOf(note("readme.md", modified = changed)), ZoneOffset.UTC)
        assertEquals(1, model.day(LocalDate.of(2026, 11, 20)).noteCount)
    }

    @Test
    fun weeksAreIsoWeeks() {
        assertEquals(47, CalendarModel.isoWeek(LocalDate.of(2026, 11, 17)))
        // The turn of the year: 28 Dec 2026 starts week 53, 4 Jan 2027 week 1.
        assertEquals(53, CalendarModel.isoWeek(LocalDate.of(2027, 1, 1)))
        assertEquals(1, CalendarModel.isoWeek(LocalDate.of(2027, 1, 4)))
        assertEquals(LocalDate.of(2026, 12, 28), CalendarModel.monday(LocalDate.of(2027, 1, 3)))
        // A week is named after the month of its Thursday.
        assertEquals(YearMonth.of(2026, 12), CalendarModel.monthOfWeek(LocalDate.of(2026, 11, 30)))
        assertEquals(YearMonth.of(2026, 12), CalendarModel.monthOfWeek(LocalDate.of(2026, 12, 28)))

        val november = CalendarModel.weeksOfMonth(YearMonth.of(2026, 11))
        assertEquals(listOf(26, 2, 9, 16, 23, 30), november.map { it.dayOfMonth }) // 26 Oct … 30 Nov
        assertEquals(listOf(44, 45, 46, 47, 48, 49), november.map(CalendarModel::isoWeek))
        assertEquals(4, CalendarModel.weeksOfMonth(YearMonth.of(2027, 2)).size) // 1 Feb 2027 is a Monday
    }

    @Test
    fun tasksAreCountedOutsideCode() {
        val s = NoteText.summarize("# T\n- [ ] one\n* [x] two\n  - [X] three\n> - [ ] quoted\n```\n- [ ] in code\n```\n- [] not a task\n-[ ] nor this")
        assertEquals(2, s.openTasks)
        assertEquals(2, s.doneTasks)
    }
}
