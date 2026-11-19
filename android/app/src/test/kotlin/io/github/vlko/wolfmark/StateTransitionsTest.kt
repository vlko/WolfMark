package io.github.vlko.wolfmark

import io.github.vlko.wolfmark.core.MirrorNote
import io.github.vlko.wolfmark.core.NotePath
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Test
import java.time.LocalDate
import java.time.YearMonth

/** M6-08: picking a note and "Today" behave as on the desktop (`CalendarModeTests` there). */
class StateTransitionsTest {
    private val today = LocalDate.of(2026, 11, 19)
    private val old = MirrorNote(NotePath.parse("2026/10/08/praca_0900.md")!!, "Old", "", 0)
    private val other = MirrorNote(NotePath.parse("2026/11/19/Share_1403.md")!!, "Today's", "", 0)
    private val start = UiState(
        screen = Screen.NOTES, today = today, selectedDay = today,
        week = LocalDate.of(2026, 11, 16), month = YearMonth.of(2026, 11),
    )

    @Test
    fun aNotePickedInTheMonthViewBringsBackTheWeekViewOnItsWeek() {
        val s = start.copy(view = CalendarView.MONTH, month = YearMonth.of(2026, 10)).opened(old)

        assertEquals(CalendarView.WEEK, s.view)
        assertEquals(old, s.open?.note)
        assertEquals(LocalDate.of(2026, 10, 8), s.selectedDay)
        assertEquals(LocalDate.of(2026, 10, 5), s.week) // the Monday of that week
    }

    @Test
    fun aNotePickedInTheWeekViewAlsoMovesTheBandToItsWeek() {
        // The history is not limited to the week in the band: a note from elsewhere takes the band along.
        val s = start.opened(old)

        assertEquals(LocalDate.of(2026, 10, 5), s.week)
        assertEquals(LocalDate.of(2026, 10, 8), s.selectedDay)
    }

    @Test
    fun theSameNoteKeepsItsTextWhileAnotherStartsEmpty() {
        val shown = start.copy(open = OpenNote(old, "# text"))

        assertEquals("# text", shown.opened(old).open?.markdown)
        assertNull(shown.opened(other).open?.markdown)
    }

    @Test
    fun todayClearsTheFiltersAndKeepsWeekOrMonth() {
        for (view in CalendarView.entries) {
            val wandered = start.copy(
                view = view, tagFilter = "praca", dayFilter = true, selectedDay = LocalDate.of(2026, 10, 8),
                week = LocalDate.of(2026, 10, 5), month = YearMonth.of(2026, 10), open = OpenNote(old, "# text"),
            )

            val s = wandered.atToday(today)

            assertEquals(view, s.view)
            assertNull(s.tagFilter)
            assertFalse(s.dayFilter)
            assertEquals(today, s.selectedDay)
            assertEquals(LocalDate.of(2026, 11, 16), s.week)
            assertEquals(YearMonth.of(2026, 11), s.month)
        }
    }

    @Test
    fun todayAfterMidnightIsTheNewDay() {
        val s = start.atToday(today.plusDays(1))
        assertEquals(today.plusDays(1), s.today)
        assertEquals(today.plusDays(1), s.selectedDay)
    }
}
