package io.github.vlko.wolfmark.core

import java.time.DayOfWeek
import java.time.LocalDate
import java.time.YearMonth
import java.time.ZoneId
import java.time.temporal.IsoFields
import java.time.temporal.TemporalAdjusters

/** Notes of one tag; [tag] is empty for notes without one. */
data class TagCount(val tag: String, val count: Int)

/** What the calendar shows for a day, as the desktop's `CalendarDay`. */
data class CalendarDay(
    val date: LocalDate,
    val noteCount: Int = 0,
    /** Most notes first, then by name. */
    val tags: List<TagCount> = emptyList(),
    val openTasks: Int = 0,
    val doneTasks: Int = 0,
) {
    fun hasTag(tagKey: String?): Boolean = tags.any { TagName.key(it.tag) == tagKey.orEmpty() }
}

/**
 * The calendar computed from the phone's copy of the notes – what the desktop keeps in its index: per day the
 * number of notes, their tags and tasks. Weeks are ISO 8601 (they start on Monday).
 */
class CalendarModel(notes: List<MirrorNote>, private val zone: ZoneId = ZoneId.systemDefault()) {
    private val byDay: Map<LocalDate, List<MirrorNote>> = notes.groupBy { it.day(zone) }
    private val days = HashMap<LocalDate, CalendarDay>()

    fun day(date: LocalDate): CalendarDay = days.getOrPut(date) {
        val list = byDay[date] ?: return@getOrPut CalendarDay(date)
        CalendarDay(date, list.size, count(list), list.sumOf { it.openTasks }, list.sumOf { it.doneTasks })
    }

    /** The seven days from [monday]. */
    fun week(monday: LocalDate): List<CalendarDay> = (0L..6L).map { day(monday.plusDays(it)) }

    /** What the week was about: its tags by number of notes (the legend under the week band). */
    fun topics(monday: LocalDate): List<TagCount> =
        count((0L..6L).flatMap { byDay[monday.plusDays(it)].orEmpty() })

    private fun count(notes: List<MirrorNote>): List<TagCount> =
        notes.groupBy { it.path.tag?.let(TagName::key).orEmpty() }
            .map { (_, list) -> TagCount(list.minOf { it.path.tag.orEmpty() }, list.size) }
            .sortedWith(compareByDescending<TagCount> { it.count }.thenBy { it.tag })

    companion object {
        fun monday(date: LocalDate): LocalDate = date.with(TemporalAdjusters.previousOrSame(DayOfWeek.MONDAY))

        fun isoWeek(date: LocalDate): Int = date.get(IsoFields.WEEK_OF_WEEK_BASED_YEAR)

        /** The month a week is named after: the one its Thursday is in. */
        fun monthOfWeek(monday: LocalDate): YearMonth = YearMonth.from(monday.plusDays(3))

        /** The Mondays of every week that touches the month. */
        fun weeksOfMonth(month: YearMonth): List<LocalDate> {
            val weeks = ArrayList<LocalDate>()
            var monday = monday(month.atDay(1))
            while (!monday.isAfter(month.atEndOfMonth())) {
                weeks += monday
                monday = monday.plusWeeks(1)
            }
            return weeks
        }
    }
}
